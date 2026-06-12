import { ClearOutlined, DeleteOutlined, PlusOutlined, TranslationOutlined } from '@ant-design/icons'
import { Button, Checkbox, Image, Input, InputNumber, message, Modal, Select, Space, Table, Tag, Tooltip } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { assignProductsToContainer, checkContainerConflicts, getContainerList } from '../../../services/containerService'
import { batchDetectProducts, batchImportConfirm, batchUpdateDomesticProducts, fixProductImage, sendToHq, syncToHBSales } from '../../../services/domesticProductImportService'
import { batchTranslate } from '../../../services/translationService'
import type { ProductImportItem, DuplicateGroup, PageState } from './types'
import { applyProductImportNameTranslations, buildAssignContainerItems, calculateStatistics, containsChineseText, createEmptyProduct, detectDuplicates, findInvalidAssignContainerItems, generateImageUrl, mergeDuplicateProducts, stripAssignContainerItemsForRequest, summarizeAssignProductsResult, updateCalculatedFields, validateProduct } from './utils'
import { ConflictResolutionDialog } from './ConflictResolutionDialog'
import { DuplicateDialog } from './DuplicateDialog'
import './styles.css'

const EDITABLE_COLUMNS = ['quantity', 'productCode', 'barcode', 'productName', 'englishName', 'domesticPrice', 'oemPrice', 'midPackQuantity', 'casePackQuantity', 'volume'] as const

const ALL_COLUMN_KEYS = ['selection', 'quantity', 'newImage', 'productCode', 'barcode', 'productName', 'englishName', 'domesticPrice', 'oemPrice', 'midPackQuantity', 'casePackQuantity', 'volume'] as const

const COLUMN_KEY_TO_EDITABLE: Record<string, string | null> = {
  selection: null, quantity: 'quantity', newImage: null, productCode: 'productCode',
  barcode: 'barcode', productName: 'productName', englishName: 'englishName',
  domesticPrice: 'domesticPrice', oemPrice: 'oemPrice', midPackQuantity: 'midPackQuantity',
  casePackQuantity: 'casePackQuantity', volume: 'volume',
}

const PRODUCT_IMPORT_BASE_TABLE_SCROLL_X = 1280
const PRODUCT_IMPORT_DETECTED_TABLE_SCROLL_X = 2500

export default function ProductImportPage() {
  const { t } = useTranslation()
  const [state, setState] = useState<PageState>({
    supplier: null,
    mode: 'import',
    products: [createEmptyProduct()],
    selectedIds: [],
    statistics: { total: 0, duplicateCount: 0, newCount: 0, updateCount: 0, unchangedCount: 0, errorCount: 0, dbDuplicateCount: 0, selectedCount: 0, totalQuantity: 0, totalProducts: 0, totalVolume: 0 },
    loading: false,
    detecting: false,
    saving: false,
    needsDetection: false,
  })
  const [showStatistics, setShowStatistics] = useState(false)
  const [suppliers, setSuppliers] = useState<Array<{ supplierCode: string; supplierName: string; shopNumber?: string }>>([])
  const [loadingSuppliers, setLoadingSuppliers] = useState(false)
  const [duplicateDialogOpen, setDuplicateDialogOpen] = useState(false)
  const [containers, setContainers] = useState<any[]>([])
  const [loadingContainers, setLoadingContainers] = useState(false)
  const [translating, setTranslating] = useState(false)
  const [selectedContainerId, setSelectedContainerId] = useState('')
  const [conflictDialogOpen, setConflictDialogOpen] = useState(false)
  const [conflictItems, setConflictItems] = useState<Array<{ productCode: string; existingPieces?: number }>>([])
  const [pendingSend, setPendingSend] = useState<{ containerId: string; notes: string; products: ProductImportItem[] } | null>(null)
  const syncIncludeImageRef = useRef(false)
  const productImportTableRef = useRef<HTMLDivElement | null>(null)
  const [duplicateGroups, setDuplicateGroups] = useState<DuplicateGroup[]>([])
  const [selectedColumnKey, setSelectedColumnKey] = useState<string | null>(null)
  const [tableScrollY, setTableScrollY] = useState(500)
  // 检测前列数少，不能沿用检测后宽度，否则 AntD 会把基础列横向拉宽。
  const productImportTableScrollX = showStatistics ? PRODUCT_IMPORT_DETECTED_TABLE_SCROLL_X : PRODUCT_IMPORT_BASE_TABLE_SCROLL_X

  useEffect(() => {
    const load = async () => {
      try {
        setLoadingSuppliers(true)
        const result = await getActiveChinaSuppliers()
        setSuppliers(result || [])
      } catch { message.error(t('productImport.loadSuppliersFailed', '加载供应商列表失败')) }
      finally { setLoadingSuppliers(false) }
    }
    load()
  }, [])

  const loadContainers = useCallback(async () => {
    try {
      setLoadingContainers(true)
      const response = await getContainerList({ sortBy: '装柜日期', sortDirection: 'desc', page: 1, pageSize: 50 })
      const filtered = (response.containers || []).filter((c: any) => c.状态 === 0)
      setContainers(filtered)
    } catch { /* ignore */ }
    finally { setLoadingContainers(false) }
  }, [])

  useEffect(() => {
    void loadContainers()
  }, [loadContainers])

  useEffect(() => {
    const updateTableScrollY = () => {
      const tableTop = productImportTableRef.current?.getBoundingClientRect().top ?? 0
      const bottomPadding = 24
      const minTableBodyHeight = 260
      const nextScrollY = Math.max(Math.floor(window.innerHeight - tableTop - bottomPadding), minTableBodyHeight)
      setTableScrollY(nextScrollY)
    }

    // 表格高度跟随视口剩余空间，避免导入多行时撑高整个页面，只让表格内部滚动。
    updateTableScrollY()
    window.addEventListener('resize', updateTableScrollY)
    return () => window.removeEventListener('resize', updateTableScrollY)
  }, [showStatistics, state.products.length])

  const formatDate = (d?: string) => {
    if (!d) return '-'
    const date = new Date(d)
    if (isNaN(date.getTime())) return d
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
  }

  const addEmptyRows = useCallback((count = 10) => {
    setState((prev) => {
      const newRows = Array.from({ length: count }, () => createEmptyProduct())
      const newProducts = [...prev.products, ...newRows]
      return { ...prev, products: newProducts, needsDetection: true, statistics: calculateStatistics(newProducts, prev.selectedIds) }
    })
  }, [])

  const deleteSelectedRows = useCallback(() => {
    setState((prev) => {
      const newProducts = prev.products.filter((p) => !prev.selectedIds.includes(p.id))
      return { ...prev, products: newProducts, selectedIds: [], needsDetection: true, statistics: calculateStatistics(newProducts, []) }
    })
  }, [])

  const updateProduct = useCallback((rowId: string, field: string, value: unknown) => {
    setState((prev) => {
      const newProducts = prev.products.map((p) => {
        if (p.id !== rowId) return p
        const updated = { ...p, newProduct: { ...p.newProduct, [field]: value } }
        if (field === 'productCode' && !p.customImage) {
          updated.imageUrl = generateImageUrl(value as string)
          updated.imageLoadStatus = 'loading'
        }
        return updateCalculatedFields(updated)
      })
      return { ...prev, products: newProducts, needsDetection: true, statistics: calculateStatistics(newProducts, prev.selectedIds) }
    })
  }, [])

  const handleDetect = useCallback(async () => {
    if (!state.supplier) { message.warning(t('productImport.selectSupplierFirst', '请先选择供应商')); return }
    if (state.products.length === 0) { message.warning(t('productImport.noDataToCheck', '没有可检测的数据')); return }
    const duplicates = detectDuplicates(state.products)
    if (duplicates.length > 0) {
      setDuplicateGroups(duplicates)
      setDuplicateDialogOpen(true)
      setState((prev) => ({
        ...prev,
        products: prev.products.map((product) => {
          const isDup = duplicates.some((group) => group.items.some((item) => item.id === product.id))
          return { ...product, isDuplicate: isDup, status: isDup ? 'duplicate' : product.status } as ProductImportItem
        }),
      }))
      return
    }
    setState((prev) => ({ ...prev, detecting: true }))
    try {
      const detectionData = state.products.filter((p) => p.newProduct.productCode).map((p) => ({
        HBProductNo: p.newProduct.productCode,
        ProductName: p.newProduct.productName,
        EnglishProductName: p.newProduct.englishName,
        DomesticPrice: p.newProduct.domesticPrice,
        OEMPrice: p.newProduct.oemPrice,
        MiddlePackQuantity: p.newProduct.midPackQuantity,
        PackingQuantity: p.newProduct.casePackQuantity,
        UnitVolume: p.newProduct.volume,
        Barcode: p.newProduct.barcode,
      }))
      const response = await batchDetectProducts({ SupplierCode: state.supplier, Products: detectionData })
      if (response.success && response.data) {
        const detectionMap = new Map(response.data.filter((item: any) => item?.inputData?.hbProductNo).map((item: any) => [item.inputData.hbProductNo, item]))
        setState((prev) => {
          const pascalToCamelMap: Record<string, string> = {
            ProductName: 'productName', EnglishProductName: 'englishProductName', Barcode: 'barcode',
            DomesticPrice: 'domesticPrice', OEMPrice: 'oemPrice', PackingQuantity: 'packingQuantity',
            UnitVolume: 'unitVolume', MiddlePackQuantity: 'middlePackQuantity',
          }
          const newProducts = prev.products.map((product) => {
            if (!product.newProduct.productCode) return { ...product, matchedProduct: undefined, diffFields: [], status: 'error', errors: validateProduct(product, 'import') } as ProductImportItem
            const detection = detectionMap.get(product.newProduct.productCode)
            if (!detection) return { ...product, matchedProduct: undefined, diffFields: [], status: 'new' } as ProductImportItem
            if (!detection.isNewProduct && detection.existingData) {
              const diffFields = (detection.changeList || []).map((f: string) => pascalToCamelMap[f] || f)
              const isDbDuplicate = detection.hasDuplicateInDatabase === true
              return { ...product, matchedProduct: detection.existingData, diffFields, status: isDbDuplicate ? 'dbDuplicate' : detection.hasChanges || diffFields.length > 0 ? 'updated' : 'unchanged' } as ProductImportItem
            }
            return { ...product, matchedProduct: undefined, diffFields: [], status: 'new' } as ProductImportItem
          })
          return { ...prev, products: newProducts, detecting: false, needsDetection: false, statistics: calculateStatistics(newProducts, prev.selectedIds) }
        })
        setShowStatistics(true)
        message.success(t('productImport.checkComplete', '检测完成！'))
        try {
          const itemsNeedingImage = (response.data || []).filter((item: any) => !item.isNewProduct && item.existingData && (!item.existingData.productImage || String(item.existingData.productImage).trim() === '') && !!item.existingData.hbProductNo)
          if (itemsNeedingImage.length > 0) {
            await Promise.all(itemsNeedingImage.map(async (item: any) => {
              const defaultUrl = generateImageUrl(item.existingData.hbProductNo)
              try {
                await fixProductImage(item.existingData.productCode, defaultUrl)
                setState((prev) => ({ ...prev, products: prev.products.map((p) => p.matchedProduct?.productCode === item.existingData.productCode ? { ...p, matchedProduct: { ...p.matchedProduct, productImage: defaultUrl } } : p) }))
              } catch { /* ignore */ }
            }))
          }
        } catch { /* ignore */ }
      } else { throw new Error(response.message || t('productImport.checkFailed', '检测失败')) }
    } catch (error: any) {
      message.error(error.message || t('productImport.checkFailedRetry', '检测失败，请重试'))
      setState((prev) => ({ ...prev, detecting: false }))
    }
  }, [state.supplier, state.products])

  const handleMergeDuplicates = useCallback(() => {
    const mergedProducts = mergeDuplicateProducts(state.products)
    setState((prev) => ({ ...prev, products: mergedProducts, selectedIds: [], needsDetection: true, statistics: calculateStatistics(mergedProducts, []) }))
    message.success(t('productImport.mergeSuccess', '成功合并重复数据，共 {{count}} 组', { count: duplicateGroups.length }))
  }, [state.products, duplicateGroups])

  const handleBatchTranslate = useCallback(async () => {
    const selectedIdSet = new Set(state.selectedIds)
    const targetProducts = state.selectedIds.length > 0
      ? state.products.filter((product) => selectedIdSet.has(product.id))
      : state.products
    const names = Array.from(new Set(targetProducts
      .map((product) => product.newProduct.productName.trim())
      .filter((name) => name && containsChineseText(name))))

    if (names.length === 0) {
      message.warning(t('productImport.noNamesToTranslate', '没有可翻译的商品名称'))
      return
    }

    try {
      setTranslating(true)
      const translations = await batchTranslate(names)
      const result = applyProductImportNameTranslations(state.products, translations, state.selectedIds)

      if (result.appliedCount === 0) {
        message.warning(t('productImport.noValidTranslatedNames', '没有可保存的英文翻译结果'))
        return
      }

      setState((prev) => ({
        ...prev,
        products: result.products,
        needsDetection: true,
        statistics: calculateStatistics(result.products, prev.selectedIds),
      }))
      message.success(t('productImport.batchTranslateSuccess', '成功翻译 {{count}} 个商品名称', { count: result.appliedCount }))
      if (result.skippedCount > 0) {
        message.warning(t('productImport.invalidTranslatedNamesSkipped', '有 {{count}} 条翻译结果仍包含中文或无变化，已跳过', { count: result.skippedCount }))
      }
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('productImport.batchTranslateFailed', '批量翻译失败'))
    } finally {
      setTranslating(false)
    }
  }, [state.products, state.selectedIds, t])

  const handleBatchCreate = useCallback(async () => {
    const newProducts = state.products.filter((p) => p.status === 'new')
    if (newProducts.length === 0) { message.warning(t('productImport.noNewProducts', '没有新商品需要创建')); return }
    if (!state.supplier) { message.warning(t('productImport.selectSupplierFirst', '请先选择供应商')); return }
    setState((prev) => ({ ...prev, saving: true }))
    try {
      const removeUndefined = (obj: any) => Object.fromEntries(Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== ''))
      const dto: any = {
        SupplierCode: state.supplier,
        NewProducts: newProducts.map((item) => removeUndefined({
          HBProductNo: item.newProduct.productCode?.trim(), ProductName: item.newProduct.productName?.trim(), EnglishProductName: item.newProduct.englishName?.trim(),
          Barcode: item.newProduct.barcode?.trim(), DomesticPrice: typeof item.newProduct.domesticPrice === 'number' ? item.newProduct.domesticPrice : undefined,
          OEMPrice: typeof item.newProduct.oemPrice === 'number' ? item.newProduct.oemPrice : undefined,
          MiddlePackQuantity: item.newProduct.midPackQuantity && item.newProduct.midPackQuantity > 0 ? item.newProduct.midPackQuantity : undefined,
          PackingQuantity: item.newProduct.casePackQuantity && item.newProduct.casePackQuantity > 0 ? item.newProduct.casePackQuantity : undefined,
          UnitVolume: item.newProduct.volume !== undefined && item.newProduct.volume >= 0 ? item.newProduct.volume : undefined,
        })),
        UpdateProducts: [],
      }
      const response = await batchImportConfirm(dto)
      const createdCount = response?.data?.createdProducts?.length ?? response?.data?.created ?? 0
      message.success(t('productImport.createSuccessCount', '成功新建 {{count}} 个商品', { count: createdCount }))
      setState((prev) => {
        const newList = prev.products.map((p) => p.status === 'new' ? { ...p, status: 'unchanged' as const, diffFields: [] } : p)
        return { ...prev, products: newList, saving: false, statistics: calculateStatistics(newList, prev.selectedIds) }
      })
      setTimeout(() => handleDetect(), 0)
    } catch (error: any) {
      message.error(error?.response?.data?.message || error?.message || t('productImport.batchCreateFailed', '批量创建失败'))
      setState((prev) => ({ ...prev, saving: false }))
    }
  }, [state.products, state.supplier, handleDetect])

  const handleBatchUpdate = useCallback(async () => {
    const updatedProducts = state.products.filter((p) => p.status === 'updated')
    if (updatedProducts.length === 0) { message.warning(t('productImport.noProductsToUpdate', '没有商品需要更新')); return }
    setState((prev) => ({ ...prev, saving: true }))
    try {
      const productsToUpdate = updatedProducts.map((product) => {
        const targetCode = product.matchedProduct?.productCode
        if (!targetCode) return null
        const updateItem: any = { ProductCode: targetCode }
        if (product.newProduct.productName) updateItem.ProductName = product.newProduct.productName
        if (product.newProduct.englishName) updateItem.EnglishProductName = product.newProduct.englishName
        if (product.newProduct.barcode?.trim()) updateItem.Barcode = product.newProduct.barcode.trim()
        if (product.newProduct.domesticPrice !== undefined) updateItem.DomesticPrice = product.newProduct.domesticPrice
        if (product.newProduct.oemPrice !== undefined) updateItem.OEMPrice = product.newProduct.oemPrice
        if (product.newProduct.midPackQuantity !== undefined) updateItem.MiddlePackQuantity = product.newProduct.midPackQuantity
        if (product.newProduct.casePackQuantity !== undefined) updateItem.PackingQuantity = product.newProduct.casePackQuantity
        if (product.newProduct.volume !== undefined) updateItem.UnitVolume = product.newProduct.volume
        return updateItem
      }).filter(Boolean) as any[]
      const response = await batchUpdateDomesticProducts({ Products: productsToUpdate })
      const successCount = response?.data?.updatedProducts?.length ?? 0
      message.success(t('productImport.updateSuccessCount', '成功更新 {{count}} 个商品', { count: successCount }))
      setState((prev) => {
        const newProducts = prev.products.map((p) => p.status === 'updated' ? { ...p, status: 'unchanged' as const, diffFields: [] } : p)
        return { ...prev, products: newProducts, saving: false, statistics: calculateStatistics(newProducts, prev.selectedIds) }
      })
      setTimeout(() => handleDetect(), 0)
    } catch { message.error(t('productImport.batchUpdateFailed', '批量更新失败，请重试')); setState((prev) => ({ ...prev, saving: false })) }
  }, [state.products, state.supplier, handleDetect])

  const formatInvalidAssignItems = useCallback((products: ReturnType<typeof buildAssignContainerItems>) => (
    findInvalidAssignContainerItems(products)
      .map((item) => `${item.hbProductNo || item.productCode || '(无货号)'}(${item.fields.join('/')})`)
      .join(', ')
  ), [])

  const formatAssignFailedItems = useCallback((failed: Array<{ hbProductNo?: string; productCode?: string; reason: string }>) => (
    failed
      .map((item) => `${t('productImport.failedItemHbProductNo', '货号')}: ${item.hbProductNo || 'N/A'} / ${t('productImport.failedItemProductCode', '本地编码')}: ${item.productCode || 'N/A'} / ${item.reason}`)
      .join(', ')
  ), [t])

  const getMissingProductCodeError = useCallback((items: ReturnType<typeof buildAssignContainerItems>) => {
    const invalidItems = findInvalidAssignContainerItems(items)
    const missingProductCodeItems = invalidItems.filter((item) => item.reasons.includes('未匹配本地商品编码'))
    if (missingProductCodeItems.length === 0) return ''
    return missingProductCodeItems
      .map((item) => `${item.hbProductNo || 'N/A'}(${item.reasons.join('/')})`)
      .join(', ')
  }, [])

  const mergeAssignSummaries = useCallback((
    summaries: Array<ReturnType<typeof summarizeAssignProductsResult>>,
  ) => {
    const created = summaries.reduce((sum, item) => sum + item.created, 0)
    const updated = summaries.reduce((sum, item) => sum + item.updated, 0)
    const succeeded = summaries.reduce((sum, item) => sum + item.succeeded, 0)
    const failedCount = summaries.reduce((sum, item) => sum + item.failedCount, 0)
    const failed = summaries.flatMap((item) => item.failed)
    const messageText = summaries.map((item) => item.message).filter(Boolean).join('; ')

    if (summaries.some((item) => item.status === 'apiError')) {
      return { status: 'apiError' as const, created, updated, succeeded, failedCount, failed, message: messageText }
    }
    if (failedCount > 0 && succeeded === 0) {
      return { status: 'failed' as const, created, updated, succeeded, failedCount, failed, message: messageText }
    }
    if (failedCount > 0) {
      return { status: 'partial' as const, created, updated, succeeded, failedCount, failed, message: messageText }
    }
    return { status: 'success' as const, created, updated, succeeded, failedCount, failed, message: messageText }
  }, [])

  const showAssignSummaryMessage = useCallback((summary: ReturnType<typeof mergeAssignSummaries>) => {
    const failedDetails = summary.failedCount > 0
      ? t('productImport.assignFailedDetails', '失败明细：{{items}}', { items: formatAssignFailedItems(summary.failed) })
      : ''

    if (summary.status === 'success') {
      message.success(t('productImport.sendResultDetail', '发送完成：新建 {{created}}，更新 {{updated}}', { created: summary.created, updated: summary.updated }))
      return
    }

    if (summary.status === 'partial') {
      message.warning(t('productImport.sendPartialSuccess', '部分发送成功：新建 {{created}}，更新 {{updated}}，失败 {{failedCount}}。{{details}}', {
        created: summary.created,
        updated: summary.updated,
        failedCount: summary.failedCount,
        details: failedDetails,
      }))
      return
    }

    const fallbackMessage = t('productImport.sendFailedWithDetails', '发送失败：新建 {{created}}，更新 {{updated}}，失败 {{failedCount}}。{{details}}', {
      created: summary.created,
      updated: summary.updated,
      failedCount: summary.failedCount,
      details: failedDetails,
    })
    message.error([summary.message, failedDetails].filter(Boolean).join(' ') || fallbackMessage)
  }, [formatAssignFailedItems, t])

  const markProductsSentToContainer = useCallback((productIds: string[]) => {
    if (productIds.length === 0) return
    setState((prev) => ({
      ...prev,
      // 只有整批成功时才标记为已发送，避免局部失败时出现假成功状态。
      products: prev.products.map((product) => productIds.includes(product.id) ? { ...product, sentToContainer: true } : product),
    }))
  }, [])

  const handleSendToContainer = useCallback(async (containerId: string, notes: string) => {
    const selectedProducts = state.products.filter((p) => state.selectedIds.includes(p.id))
    const invalid = selectedProducts.filter((p) => !p.newProduct.quantity || p.newProduct.quantity <= 0)
    if (invalid.length > 0) { message.error(t('productImport.invalidQuantity', '以下商品件数不能为空且必须>0：{{items}}', { items: invalid.map((p) => p.newProduct.productCode || '(无货号)').join(', ') })); return }
    const assignItems = buildAssignContainerItems(selectedProducts, notes)
    const missingProductCodeError = getMissingProductCodeError(assignItems)
    if (missingProductCodeError) {
      message.error(t('productImport.missingContainerProductCodes', '以下商品未匹配本地商品编码，无法发送到货柜：{{items}}', { items: missingProductCodeError }))
      return
    }
    const invalidAssignItems = formatInvalidAssignItems(assignItems)
    if (invalidAssignItems) { message.error(t('productImport.invalidContainerBusinessFields', '发送货柜商品业务字段不能为空或为 0：{{items}}', { items: invalidAssignItems })); return }
    try {
      const checkResp = await checkContainerConflicts(containerId, selectedProducts.filter((p) => p.newProduct.productCode || p.matchedProduct?.productCode).map((p) => ({ hbProductNo: p.newProduct.productCode || undefined, productCode: p.matchedProduct?.productCode || undefined })))
      if (checkResp?.data && checkResp.data.length > 0) {
        setConflictItems(checkResp.data)
        setPendingSend({ containerId, notes, products: selectedProducts })
        setConflictDialogOpen(true)
        return
      }
      const assignResp = await assignProductsToContainer(containerId, stripAssignContainerItemsForRequest(assignItems), 'increase', notes)
      const summary = mergeAssignSummaries([summarizeAssignProductsResult(assignResp, assignItems)])
      showAssignSummaryMessage(summary)
      if (summary.status === 'success') {
        markProductsSentToContainer(selectedProducts.map((product) => product.id))
      }
    } catch (error: any) { message.error(error?.response?.data?.message || error?.message || t('productImport.sendFailed', '发送失败')) }
  }, [formatInvalidAssignItems, getMissingProductCodeError, markProductsSentToContainer, mergeAssignSummaries, showAssignSummaryMessage, state.products, state.selectedIds, t])

  const handleSyncToHBSales = async () => {
    if (state.selectedIds.length === 0) { message.warning(t('productImport.selectSyncProductsFirst', '请先选择要同步的商品')); return }
    Modal.confirm({
      title: t('productImport.syncToHBSales', '同步到HBSales'),
      content: (
        <div>
          <p>{t('productImport.syncConfirmText', '确定要将选中的 {{count}} 件商品同步到HBSales数据库吗？', { count: state.selectedIds.length })}</p>
          <div style={{ marginTop: 12 }}>
            <Checkbox defaultChecked={syncIncludeImageRef.current} onChange={(e) => { syncIncludeImageRef.current = e.target.checked }}>{t('productImport.syncIncludeImage', '同时更新商品图片')}</Checkbox>
          </div>
        </div>
      ),
      okText: t('common.confirm', '确定'),
      cancelText: t('common.cancel', '取消'),
      onOk: async () => {
        try {
          const selectedProducts = state.products.filter((p) => state.selectedIds.includes(p.id))
          const productCodes = selectedProducts.map((p) => p.matchedProduct?.productCode).filter((code): code is string => code !== undefined)
          if (productCodes.length === 0) { message.warning(t('productImport.noMatchedCodes', '选中的商品中没有已匹配的本地商品编码')); return }
          const response = await syncToHBSales(productCodes, syncIncludeImageRef.current)
          if (response.success) { message.success(response.data?.message || t('productImport.syncResult', '同步完成：成功 {{count}} 条', { count: response.data?.addedCount || 0 })) }
          else { message.error(t('productImport.syncFailedMsg', '同步失败: ') + response.message) }
        } catch { message.error(t('productImport.syncFailedRetry', '同步失败，请稍后重试')) }
      },
    })
  }

  const handleSendToHq = useCallback(async () => {
    if (state.selectedIds.length === 0) { message.warning(t('productImport.selectSendProductsFirst', '请先选择要发送的商品')); return }
    const selectedProducts = state.products.filter((p) => state.selectedIds.includes(p.id))
    const productCodes = selectedProducts.map((p) => p.matchedProduct?.productCode).filter((code): code is string => !!code)
    if (productCodes.length === 0) { message.warning(t('productImport.noMatchedCodesCheck', '选中的商品中没有已匹配的本地商品编码，请先检测匹配')); return }
    const missingPrice = selectedProducts.filter((p) => !p.matchedProduct?.productCode)
    if (missingPrice.length > 0) { message.warning(`${t('productImport.unmatchedProducts', '{{count}} 个商品未匹配，请先检测', { count: missingPrice.length })}`); return }
    Modal.confirm({
      title: t('productImport.sendToHQ', '发送商品到HQ'),
      content: (
        <div>
          <p>{t('productImport.sendConfirmText', '确定要将选中的 {{count}} 个商品发送到HQ数据库吗？', { count: productCodes.length })}</p>
          <p style={{ fontSize: 12, color: '#888' }}>{t('productImport.sendHint', '将先同步到 HBSales（含商品图片），再写入 HQ 的 DIC_商品信息字典表、DIC_商品零售价表 和 CBP_DIC_商品库存表')}</p>
          <p style={{ fontSize: 12, color: '#f97316' }}>{t('productImport.sendPriceWarning', '⚠️ 要求商品必须有进口价格和贴牌价格')}</p>
        </div>
      ),
      okText: t('productImport.confirmSend', '确定发送'),
      cancelText: t('common.cancel', '取消'),
      onOk: async () => {
        try {
          const response = await sendToHq(productCodes)
          if (response.success) { message.success(response.data?.message || t('productImport.sendComplete', '发送完成')) }
          else { message.error(t('productImport.sendFailedMsg', '发送失败: ') + response.message) }
        } catch { message.error(t('productImport.sendFailedRetry', '发送失败，请稍后重试')) }
      },
    })
  }, [state.selectedIds, state.products])

  const handleResolveConflicts = async (result: { global?: 'override' | 'increase'; perItem?: Record<string, 'override' | 'increase'> }) => {
    if (!pendingSend) return
    const { containerId, notes, products } = pendingSend
    let overrideItems: ProductImportItem[] = []
    let increaseItems: ProductImportItem[] = []
    if (result.global === 'override') overrideItems = products
    else if (result.global === 'increase') increaseItems = products
    else {
      const perItem = result.perItem || {}
      overrideItems = products.filter((p) => perItem[p.newProduct.productCode] === 'override')
      increaseItems = products.filter((p) => perItem[p.newProduct.productCode] !== 'override')
    }
    try {
      const summaries: Array<ReturnType<typeof summarizeAssignProductsResult>> = []
      const overrideAssignItems = buildAssignContainerItems(overrideItems, notes)
      const increaseAssignItems = buildAssignContainerItems(increaseItems, notes)
      const allAssignItems = [...overrideAssignItems, ...increaseAssignItems]
      const missingProductCodeError = getMissingProductCodeError(allAssignItems)
      if (missingProductCodeError) { message.error(t('productImport.missingContainerProductCodes', '以下商品未匹配本地商品编码，无法发送到货柜：{{items}}', { items: missingProductCodeError })); return }
      const invalidAssignItems = formatInvalidAssignItems(allAssignItems)
      if (invalidAssignItems) { message.error(t('productImport.invalidContainerBusinessFields', '发送货柜商品业务字段不能为空或为 0：{{items}}', { items: invalidAssignItems })); return }
      if (overrideItems.length > 0) {
        const resp = await assignProductsToContainer(containerId, stripAssignContainerItemsForRequest(overrideAssignItems), 'override', notes)
        summaries.push(summarizeAssignProductsResult(resp, overrideAssignItems))
      }
      if (increaseItems.length > 0) {
        const resp = await assignProductsToContainer(containerId, stripAssignContainerItemsForRequest(increaseAssignItems), 'increase', notes)
        summaries.push(summarizeAssignProductsResult(resp, increaseAssignItems))
      }
      if (summaries.length === 0) {
        message.warning(t('productImport.selectProductsFirst', '请先选择商品'))
        return
      }
      const summary = mergeAssignSummaries(summaries)
      showAssignSummaryMessage(summary)
      if (summary.status === 'success') {
        markProductsSentToContainer(products.map((product) => product.id))
      }
      setPendingSend(null)
      setConflictDialogOpen(false)
    } catch (error: any) { message.error(error?.response?.data?.message || error?.message || t('productImport.sendFailed', '发送失败')) }
  }

  const selectedProducts = useMemo(() => state.products.filter((p) => state.selectedIds.includes(p.id)), [state.products, state.selectedIds])
  const invalidSelectedCount = useMemo(() => selectedProducts.filter((p) => !p.newProduct.quantity || p.newProduct.quantity <= 0).length, [selectedProducts])

  const getRowClassName = (row: ProductImportItem) => {
    if (row.isDuplicate) return 'row-duplicate'
    if (row.status === 'new') return 'row-new'
    if (row.status === 'updated') return 'row-updated'
    if (row.status === 'error') return 'row-error'
    if (row.sentToContainer) return 'row-sent'
    return ''
  }

  const resolveColumnKeyFromTd = useCallback((td: HTMLTableCellElement): string | null => {
    const tr = td.closest('tr')
    if (!tr) return null
    const allTds = Array.from(tr.querySelectorAll('td'))
    const tdIndex = allTds.indexOf(td)
    if (tdIndex < 0) return null
    const isMobile = window.innerWidth < 768
    const baseOffset = isMobile ? 0 : 1
    const visualIndex = tdIndex - baseOffset
    if (visualIndex < 0 || visualIndex >= ALL_COLUMN_KEYS.length) return null
    return ALL_COLUMN_KEYS[visualIndex]
  }, [])

  const handleClearColumn = useCallback(() => {
    if (!selectedColumnKey) return
    const editableKey = COLUMN_KEY_TO_EDITABLE[selectedColumnKey]
    if (!editableKey) { message.warning(t('productImport.columnCannotClear', '该列不支持清空')); return }
    const defaultValue: any = ['quantity', 'midPackQuantity', 'casePackQuantity'].includes(editableKey) ? undefined
      : ['domesticPrice', 'oemPrice', 'volume'].includes(editableKey) ? undefined
      : ''
    setState((prev) => {
      const newProducts = prev.products.map((p) => {
        const updated = { ...p, newProduct: { ...p.newProduct, [editableKey]: defaultValue } }
        return updateCalculatedFields(updated)
      })
      return { ...prev, products: newProducts, needsDetection: true, statistics: calculateStatistics(newProducts, prev.selectedIds) }
    })
    message.success(t('productImport.columnCleared', '已清空列数据'))
  }, [selectedColumnKey])

  const handlePaste = useCallback((e: ClipboardEvent) => {
    const text = e.clipboardData?.getData('text/plain')
    if (!text) return
    const target = e.target as HTMLElement
    const tableContainer = target.closest('.ant-table-wrapper')
    if (!tableContainer) return
    const rows = text.split('\n').filter((row) => row.trim())
    const data = rows.map((row) => row.split('\t'))
    const isSingleCell = data.length === 1 && data[0].length === 1
    const isInInput = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'
    if (isInInput && isSingleCell) return
    e.preventDefault()
    e.stopPropagation()

    let startEditableIndex: number
    let rowIndex: number

    if (selectedColumnKey) {
      const editableKey = COLUMN_KEY_TO_EDITABLE[selectedColumnKey]
      if (!editableKey) { message.warning(t('productImport.cannotPaste', '该列不支持粘贴')); return }
      startEditableIndex = EDITABLE_COLUMNS.indexOf(editableKey as any)
      if (startEditableIndex < 0) { message.warning(t('productImport.cannotPaste', '该列不支持粘贴')); return }
      const td = target.closest('td')
      if (td) {
        const tr = td.closest('tr')
        if (!tr) return
        const startRowIndex = parseInt(tr.getAttribute('data-row-key') || '0')
        rowIndex = state.products.findIndex((p) => p.id === String(startRowIndex))
        if (rowIndex === -1) { message.warning(t('productImport.cannotDetermineRow', '无法确定行位置')); return }
      } else {
        rowIndex = 0
      }
    } else {
      const td = target.closest('td')
      if (td) {
        const colKey = resolveColumnKeyFromTd(td)
        if (!colKey || !COLUMN_KEY_TO_EDITABLE[colKey]) { message.warning(t('productImport.selectEditableCol', '请先选中一个可编辑列')); return }
        const editableKey = COLUMN_KEY_TO_EDITABLE[colKey]!
        startEditableIndex = EDITABLE_COLUMNS.indexOf(editableKey as any)
        const tr = td.closest('tr')
        if (!tr) return
        const startRowIndex = parseInt(tr.getAttribute('data-row-key') || '0')
        rowIndex = state.products.findIndex((p) => p.id === String(startRowIndex))
        if (rowIndex === -1) { message.warning(t('productImport.cannotDetermineRow', '无法确定行位置')); return }
      } else {
        message.warning(t('productImport.selectPasteStart', '请先选中一个单元格或列头作为粘贴起点'))
        return
      }
    }

    const newProducts = [...state.products]
    const requiredRows = rowIndex + data.length
    while (newProducts.length < requiredRows) newProducts.push(createEmptyProduct())
    data.forEach((rowData, rowOffset) => {
      const currentRowIndex = rowIndex + rowOffset
      if (currentRowIndex < 0 || currentRowIndex >= newProducts.length) return
      const currentRow = { ...newProducts[currentRowIndex], newProduct: { ...newProducts[currentRowIndex].newProduct } }
      rowData.forEach((cellValue, colOffset) => {
        const currentColIndex = startEditableIndex + colOffset
        if (currentColIndex < 0 || currentColIndex >= EDITABLE_COLUMNS.length) return
        const columnKey = EDITABLE_COLUMNS[currentColIndex]
        let cleanedValue: any = cellValue.trim()
        if (!cleanedValue) return
        if (['quantity', 'midPackQuantity', 'casePackQuantity'].includes(columnKey)) cleanedValue = parseInt(cleanedValue) || undefined
        else if (['domesticPrice', 'oemPrice', 'volume'].includes(columnKey)) { cleanedValue = cleanedValue.replace(/[¥￥€£$₩₹,，]/g, ''); cleanedValue = parseFloat(cleanedValue) || undefined }
        if (cleanedValue !== undefined && cleanedValue !== '') (currentRow.newProduct as any)[columnKey] = cleanedValue
      })
      if (currentRow.newProduct.productCode) { currentRow.imageUrl = generateImageUrl(currentRow.newProduct.productCode); currentRow.imageLoadStatus = 'loading' }
      newProducts[currentRowIndex] = updateCalculatedFields(currentRow)
    })
    setState((prev) => ({ ...prev, products: newProducts, needsDetection: true, statistics: calculateStatistics(newProducts, prev.selectedIds) }))
    message.success(t('productImport.pasteSuccess', '成功粘贴 {{count}} 行数据', { count: data.length }))
  }, [state.products, selectedColumnKey, resolveColumnKeyFromTd])

  useEffect(() => {
    document.addEventListener('paste', handlePaste)
    return () => document.removeEventListener('paste', handlePaste)
  }, [handlePaste])

  const handleHeaderClick = useCallback((columnKey: string, e: React.MouseEvent) => {
    e.stopPropagation()
    setSelectedColumnKey((prev) => prev === columnKey ? null : columnKey)
  }, [])

  const selectedColumnEditableKey = useMemo(() => {
    if (!selectedColumnKey) return null
    return COLUMN_KEY_TO_EDITABLE[selectedColumnKey]
  }, [selectedColumnKey])

  const columns: ColumnsType<ProductImportItem> = useMemo(() => {
    const renderEditable = (field: string, type: 'text' | 'number', record: ProductImportItem, extra?: { step?: string; precision?: number }) => {
      const value = (record.newProduct as any)[field]
      const isDiff = record.diffFields?.includes(field)
      const isColSelected = selectedColumnEditableKey === field
      const style: React.CSSProperties = {
        ...(isDiff ? { backgroundColor: '#fef3c7' } : {}),
        ...(isColSelected ? { backgroundColor: '#e6f7ff' } : {}),
      }
      if (type === 'number') {
        return <InputNumber value={value} onChange={(v) => updateProduct(record.id, field, v)} style={{ width: '100%', ...style }} step={extra?.step} precision={extra?.precision} size="small" />
      }
      return <Input value={value} onChange={(e) => updateProduct(record.id, field, e.target.value)} style={style} size="small" />
    }
    const base: ColumnsType<ProductImportItem> = [
      {
        title: t('productImport.quantity', '件数'),
        dataIndex: ['newProduct', 'quantity'],
        key: 'quantity',
        width: 80,
        fixed: 'left',
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('quantity', e) } as any),
        className: selectedColumnKey === 'quantity' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('quantity', 'number', record),
      },
      {
        title: t('productImport.newImage', '新图片'),
        key: 'newImage',
        width: 70,
        render: (_, record) => {
          if (!record.imageUrl) return <span style={{ color: '#ccc' }}>-</span>
          return <Image src={record.imageUrl} alt={record.newProduct.productCode} width={40} height={40} style={{ objectFit: 'contain' }} fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=" preview />
        },
      },
      {
        title: t('productImport.hbProductNoCol', '货号(hbProductNo)'),
        dataIndex: ['newProduct', 'productCode'],
        key: 'productCode',
        width: 130,
        fixed: 'left',
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('productCode', e) } as any),
        className: selectedColumnKey === 'productCode' ? 'col-selected' : undefined,
        render: (text, record) => (
          <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <Input value={text} onChange={(e) => updateProduct(record.id, 'productCode', e.target.value)} size="small" style={{ flex: 1, ...(selectedColumnEditableKey === 'productCode' ? { backgroundColor: '#e6f7ff' } : {}) }} />
            {record.status === 'dbDuplicate' && <span style={{ color: '#ef4444' }}>⚠️</span>}
            {record.isDuplicate && <span style={{ color: '#f97316' }}>⚠️</span>}
          </div>
        ),
      },
      {
        title: t('domesticProducts.barcode', '条码'),
        dataIndex: ['newProduct', 'barcode'],
        key: 'barcode',
        width: 130,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('barcode', e) } as any),
        className: selectedColumnKey === 'barcode' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('barcode', 'text', record),
      },
      {
        title: t('domesticProducts.productName', '商品名称'),
        dataIndex: ['newProduct', 'productName'],
        key: 'productName',
        width: 150,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('productName', e) } as any),
        className: selectedColumnKey === 'productName' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('productName', 'text', record),
      },
      {
        title: t('domesticProducts.englishName', '英文名称'),
        dataIndex: ['newProduct', 'englishName'],
        key: 'englishName',
        width: 130,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('englishName', e) } as any),
        className: selectedColumnKey === 'englishName' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('englishName', 'text', record),
      },
      {
        title: t('productImport.domesticPriceCol', '国内价格'),
        dataIndex: ['newProduct', 'domesticPrice'],
        key: 'domesticPrice',
        width: 90,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('domesticPrice', e) } as any),
        className: selectedColumnKey === 'domesticPrice' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('domesticPrice', 'number', record, { precision: 2 }),
      },
      {
        title: t('productImport.oemPriceCol', '贴牌价格'),
        dataIndex: ['newProduct', 'oemPrice'],
        key: 'oemPrice',
        width: 90,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('oemPrice', e) } as any),
        className: selectedColumnKey === 'oemPrice' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('oemPrice', 'number', record, { precision: 2 }),
      },
      {
        title: t('productImport.middlePackCol', '中包数'),
        dataIndex: ['newProduct', 'midPackQuantity'],
        key: 'midPackQuantity',
        width: 80,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('midPackQuantity', e) } as any),
        className: selectedColumnKey === 'midPackQuantity' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('midPackQuantity', 'number', record),
      },
      {
        title: t('productImport.packingQtyCol', '单件装箱数'),
        dataIndex: ['newProduct', 'casePackQuantity'],
        key: 'casePackQuantity',
        width: 100,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('casePackQuantity', e) } as any),
        className: selectedColumnKey === 'casePackQuantity' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('casePackQuantity', 'number', record),
      },
      {
        title: t('productImport.unitVolumeCol', '单件体积'),
        dataIndex: ['newProduct', 'volume'],
        key: 'volume',
        width: 100,
        onHeaderCell: () => ({ onClick: (e: React.MouseEvent) => handleHeaderClick('volume', e) } as any),
        className: selectedColumnKey === 'volume' ? 'col-selected' : undefined,
        render: (_, record) => renderEditable('volume', 'number', record, { step: '0.001', precision: 3 }),
      },
    ]
    if (showStatistics) {
      const fieldNameMap: Record<string, string> = { productName: t('domesticProducts.productName', '商品名称'), englishProductName: t('domesticProducts.englishName', '英文名称'), barcode: t('domesticProducts.barcode', '条码'), domesticPrice: '国内价格', oemPrice: '贴牌价格', packingQuantity: '单件装箱数', unitVolume: '单件体积', middlePackQuantity: '中包数' }
      base.push(
        { title: t('productImport.matchStatus', '匹配状态'), key: 'matchStatus', width: 100, fixed: 'right', render: (_, record) => {
          const map: Record<string, { color: string; text: string }> = { new: { color: 'green', text: t('productImport.statusNew', '✨ 新商品') }, updated: { color: 'orange', text: '📝 需更新' }, unchanged: { color: 'blue', text: '✅ 无变化' }, duplicate: { color: 'default', text: '⚠️ 重复' }, dbDuplicate: { color: 'purple', text: '⚠️ DB重复' }, error: { color: 'red', text: '❌ 错误' } }
          const info = map[record.status] || { color: 'default', text: t('productImport.statusUndetected', '⏳ 未检测') }
          return <Tag color={info.color}>{info.text}</Tag>
        }},
        { title: t('productImport.oldHbProductNo', '旧货号'), dataIndex: ['matchedProduct', 'hbProductNo'], key: 'matchedProductCode', width: 120, render: (text) => text || <span style={{ color: '#ccc' }}>-</span> },
        { title: t('productImport.oldBarcode', '旧条码'), dataIndex: ['matchedProduct', 'barcode'], key: 'matchedBarcode', width: 120, render: (text) => text || <span style={{ color: '#ccc' }}>-</span> },
        { title: t('productImport.oldProductName', '旧商品名称'), dataIndex: ['matchedProduct', 'productName'], key: 'matchedProductName', width: 130, render: (text) => text || <span style={{ color: '#ccc' }}>-</span> },
        { title: t('productImport.oldImage', '旧图片'), key: 'matchedProductImage', width: 70, render: (_, record) => {
          const imageUrl = record.matchedProduct?.productImage
          if (!imageUrl || !String(imageUrl).trim()) return <span style={{ color: '#ccc' }}>-</span>
          return <Image src={String(imageUrl).trim()} alt={t("productImport.oldImage", "旧图片")} width={40} height={40} style={{ objectFit: 'contain' }} fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=" preview />
        }},
        { title: t('productImport.oldDomesticPrice', '旧国内价格'), key: 'matchedDomesticPrice', width: 100, render: (_, record) => {
          const old = record.matchedProduct?.domesticPrice
          const hasDiff = record.diffFields?.includes('domesticPrice')
          if (!old) return <span style={{ color: '#ccc' }}>-</span>
          return <span style={hasDiff ? { color: '#f97316', fontWeight: 600 } : {}}>{old.toFixed(2)}{hasDiff && record.newProduct.domesticPrice ? ` → ${record.newProduct.domesticPrice.toFixed(2)}` : ''}</span>
        }},
        { title: t('productImport.oldOemPrice', '旧贴牌价格'), key: 'matchedOemPrice', width: 100, render: (_, record) => {
          const old = record.matchedProduct?.oemPrice
          const hasDiff = record.diffFields?.includes('oemPrice')
          if (!old) return <span style={{ color: '#ccc' }}>-</span>
          return <span style={hasDiff ? { color: '#f97316', fontWeight: 600 } : {}}>{old.toFixed(2)}{hasDiff && record.newProduct.oemPrice ? ` → ${record.newProduct.oemPrice.toFixed(2)}` : ''}</span>
        }},
        { title: t('productImport.oldMiddlePack', '旧中包数'), key: 'matchedMiddlePackQuantity', width: 90, render: (_, record) => {
          const old = record.matchedProduct?.middlePackQuantity
          const hasDiff = record.diffFields?.includes('middlePackQuantity')
          if (typeof old !== 'number' || old <= 0) return <span style={{ color: '#ccc' }}>-</span>
          return <span style={hasDiff ? { color: '#f97316', fontWeight: 600 } : {}}>{old}{hasDiff && typeof record.newProduct.midPackQuantity === 'number' ? ` → ${record.newProduct.midPackQuantity}` : ''}</span>
        }},
        { title: t('productImport.oldPackingQty', '旧单件装箱数'), key: 'matchedPackingQuantity', width: 100, render: (_, record) => {
          const old = record.matchedProduct?.packingQuantity
          const hasDiff = record.diffFields?.includes('packingQuantity')
          if (typeof old !== 'number' || old <= 0) return <span style={{ color: '#ccc' }}>-</span>
          return <span style={hasDiff ? { color: '#f97316', fontWeight: 600 } : {}}>{old}{hasDiff && typeof record.newProduct.casePackQuantity === 'number' ? ` → ${record.newProduct.casePackQuantity}` : ''}</span>
        }},
        { title: t('productImport.oldUnitVolume', '旧单件体积'), key: 'matchedUnitVolume', width: 100, render: (_, record) => {
          const old = record.matchedProduct?.unitVolume
          const hasDiff = record.diffFields?.includes('unitVolume')
          if (typeof old !== 'number' || old < 0) return <span style={{ color: '#ccc' }}>-</span>
          return <span style={hasDiff ? { color: '#f97316', fontWeight: 600 } : {}}>{old.toFixed(3)}{hasDiff && typeof record.newProduct.volume === 'number' ? ` → ${record.newProduct.volume.toFixed(3)}` : ''}</span>
        }},
        { title: t('productImport.diffFields', '差异字段'), key: 'diffFields', width: 150, render: (_, record) => {
          if (record.diffFields && record.diffFields.length > 0) return <span style={{ color: '#f97316', fontSize: 12, fontWeight: 600 }}>{record.diffFields.map((f) => fieldNameMap[f] || f).join(', ')}</span>
          if (record.matchedProduct) return <span style={{ color: '#22c55e', fontSize: 12 }}>{t('productImport.exactMatch', '✓ 完全匹配')}</span>
          return <span style={{ color: '#ccc' }}>-</span>
        }},
      )
    }
    base.push({ title: t('common.action', '操作'), key: 'actions', width: 60, fixed: 'right', render: (_, record) => <Button type="text" danger size="small" icon={<DeleteOutlined />} onClick={() => setState((prev) => ({ ...prev, products: prev.products.filter((p) => p.id !== record.id) }))} /> })
    return base
  }, [showStatistics, updateProduct, selectedColumnKey, selectedColumnEditableKey, handleHeaderClick])

  return (
    <PageContainer title={t("productImport.pageTitle", "国内商品导入")} subtitle={t("productImport.pageSubtitle", "从Excel粘贴数据导入国内商品")}>
      <div style={{ marginBottom: 12, padding: '8px 12px', background: '#fff', borderRadius: 6, display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <span style={{ color: '#666', fontSize: 13 }}>{t('domesticProducts.supplier', '供应商')} <span style={{ color: 'red' }}>*</span></span>
        <Select
          style={{ width: 320 }}
          placeholder={loadingSuppliers ? t('common.loading', '加载中...') : t('productImport.searchSupplier', '输入名称/编码搜索')}
          value={state.supplier || undefined}
          onChange={(value) => {
            if (value === state.supplier) return
            const change = () => {
              setState({ supplier: value, mode: 'import', products: [createEmptyProduct()], selectedIds: [], statistics: { total: 0, duplicateCount: 0, newCount: 0, updateCount: 0, unchangedCount: 0, errorCount: 0, dbDuplicateCount: 0, selectedCount: 0, totalQuantity: 0, totalProducts: 0, totalVolume: 0 }, loading: false, detecting: false, saving: false, needsDetection: false })
              setShowStatistics(false)
              setDuplicateGroups([])
            }
            if (state.products.length > 0) { Modal.confirm({ title: t('productImport.switchSupplier', '切换供应商'), content: t('productImport.switchSupplierWarning', '切换供应商将清空当前表格数据，是否继续？'), onOk: change }) }
            else change()
          }}
          showSearch
          loading={loadingSuppliers}
          filterOption={(input, option) => {
            const keyword = input.trim().toLowerCase()
            if (!keyword) return true
            const searchText = String(option?.searchText || option?.label || option?.value || '').toLowerCase()
            return searchText.includes(keyword)
          }}
          options={suppliers.sort((a, b) => (a.supplierCode || '').localeCompare(b.supplierCode || '')).map((s) => ({
            label: `${s.supplierCode} - ${s.supplierName}${s.shopNumber ? ` - ${s.shopNumber}` : ''}`,
            value: s.supplierCode,
            // 供应商下拉搜索覆盖完整展示信息，店号/附加编码也能直接命中。
            searchText: [s.supplierCode, s.supplierName, s.shopNumber].filter(Boolean).join(' '),
          }))}
        />
      </div>

      <div style={{ marginBottom: 12, padding: '8px 12px', background: '#fff', borderRadius: 6 }}>
        <div style={{ marginBottom: 8, fontSize: 12, color: '#888' }}>
          {t('productImport.clickCellTip', '💡 点击单元格后按')} <kbd style={{ padding: '1px 4px', background: '#f0f0f0', borderRadius: 3, border: '1px solid #d9d9d9' }}>Ctrl+V</kbd> 可从 Excel 粘贴多列多行数据 | 点击列头选中整列，再粘贴可直接定位
        </div>
        <Space wrap size="small">
          <Button icon={<PlusOutlined />} onClick={() => addEmptyRows(10)}>{t('productImport.addEmptyRows', '添加空行(10)')}</Button>
          <Button onClick={deleteSelectedRows} disabled={state.selectedIds.length === 0}>{t('productImport.deleteSelected', '删除选中')}</Button>
          <Button icon={<TranslationOutlined />} onClick={handleBatchTranslate} disabled={translating || state.products.length === 0} loading={translating}>{t('productImport.batchTranslate', '批量翻译')}</Button>
          <Button type="primary" onClick={handleDetect} disabled={state.detecting || !state.supplier} loading={state.detecting}>{t('productImport.detectMatch', '检测匹配')}</Button>
          {!state.needsDetection && (
            <>
              <Button style={{ background: '#22c55e', color: '#fff', borderColor: '#22c55e' }} onClick={handleBatchCreate} disabled={state.statistics.newCount === 0 || state.saving} loading={state.saving}>{t('productImport.batchCreate', '批量新建')}({state.statistics.newCount})</Button>
              <Button style={{ background: '#f59e0b', color: '#fff', borderColor: '#f59e0b' }} onClick={handleBatchUpdate} disabled={state.statistics.updateCount === 0 || state.saving} loading={state.saving}>{t('productImport.batchUpdate', '批量更新')}({state.statistics.updateCount})</Button>
            </>
          )}
          {state.needsDetection && state.products.length > 0 && <span style={{ color: '#f97316', fontWeight: 600 }}>{t('productImport.dataModifiedDetect', '⚠️ 数据已修改，请先执行"检测匹配"')}</span>}
          <Select style={{ width: 300 }} placeholder={loadingContainers ? t('common.loading', '加载中...') : t('productImport.selectContainer', '请选择货柜')} value={selectedContainerId || undefined} onChange={(v) => setSelectedContainerId(v)} loading={loadingContainers} showSearch optionFilterProp="label" onDropdownVisibleChange={(open) => { if (open) void loadContainers() }} options={containers.map((c: any) => ({ label: `${c.货柜编号} | 装柜日期: ${formatDate(c.装柜日期)}`, value: c.货柜编号 }))} />
          <Button type="primary" onClick={() => { if (state.selectedIds.length === 0) { message.error(t('productImport.selectProductsFirst', '请先选择商品')); return } if (!selectedContainerId) { message.error('请先选择货柜'); return } if (invalidSelectedCount > 0) { message.error('请先修正已选商品的件数'); return } handleSendToContainer(selectedContainerId, '') }} disabled={state.selectedIds.length === 0}>发送货柜({state.selectedIds.length})</Button>
          <Button onClick={handleSyncToHBSales} disabled={state.selectedIds.length === 0}>{t('productImport.syncToHBSales', '同步到HBSales')}</Button>
          <Button style={{ background: '#7c3aed', color: '#fff', borderColor: '#7c3aed' }} onClick={handleSendToHq} disabled={state.selectedIds.length === 0}>{t('productImport.sendToHQ', '发送到HQ')}</Button>
          {selectedColumnKey && selectedColumnEditableKey && (
            <Tooltip title={t('productImport.clearColumnData', '清空「{{key}}」列所有数据', { key: selectedColumnEditableKey })}>
              <Button icon={<ClearOutlined />} danger onClick={handleClearColumn}>
                {t('productImport.clearColumn', '清空列')}({selectedColumnEditableKey})
              </Button>
            </Tooltip>
          )}
        </Space>
      </div>

      {showStatistics && (
        <div style={{ marginBottom: 12, padding: '8px 12px', background: '#fff', borderRadius: 6, display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', fontSize: 13 }}>
          {duplicateGroups.length > 0 && <span style={{ color: '#f97316' }}>{t('productImport.statDuplicate', '⚠️ 重复: ')}{duplicateGroups.length}</span>}
          <span style={{ color: '#22c55e', fontWeight: 500 }}>{t('productImport.statNew', '✨ 新: ')}{state.statistics.newCount}</span>
          <span style={{ color: '#f59e0b', fontWeight: 500 }}>{t('productImport.statUpdate', '📝 更新: ')}{state.statistics.updateCount}</span>
          <span style={{ color: '#3b82f6', fontWeight: 500 }}>{t('productImport.statUnchanged', '✅ 无变化: ')}{state.statistics.unchangedCount}</span>
          {state.statistics.dbDuplicateCount > 0 && <span style={{ color: '#a855f7', fontWeight: 500 }}>{t('productImport.statDbDuplicate', '⚠️ DB重复: ')}{state.statistics.dbDuplicateCount}</span>}
          {state.statistics.errorCount > 0 && <span style={{ color: '#ef4444', fontWeight: 500 }}>{t('productImport.statError', '❌ 错误: ')}{state.statistics.errorCount}</span>}
          <span style={{ color: '#888' }}>{t('productImport.statSelected', '📌 选中: ')}{state.statistics.selectedCount}</span>
          <span style={{ color: '#888' }}>{t('productImport.statTotal', '📦 总计: ')}{state.statistics.total} {t('productImport.records', '条')}</span>
          <span style={{ color: '#888' }}>{t('productImport.statQuantity', '件数: ')}{state.statistics.totalQuantity}</span>
          <span style={{ color: '#888' }}>{t('productImport.statVolume', '体积: ')}{state.statistics.totalVolume.toFixed(3)} m³</span>
        </div>
      )}

      <div ref={productImportTableRef}>
        <Table
          className="product-import-table"
          style={{ '--product-import-table-body-height': `${tableScrollY}px` } as React.CSSProperties}
          columns={columns}
          dataSource={state.products}
          rowKey="id"
          size="small"
          scroll={{ x: productImportTableScrollX, y: tableScrollY }}
          rowClassName={getRowClassName}
          rowSelection={{ selectedRowKeys: state.selectedIds, onChange: (keys) => setState((prev) => ({ ...prev, selectedIds: keys as string[], statistics: calculateStatistics(prev.products, keys as string[]) })) }}
          pagination={false}
        />
      </div>

      <DuplicateDialog open={duplicateDialogOpen} duplicateGroups={duplicateGroups} onClose={() => setDuplicateDialogOpen(false)} onConfirm={handleMergeDuplicates} />
      <ConflictResolutionDialog open={conflictDialogOpen} conflicts={conflictItems} onClose={() => setConflictDialogOpen(false)} onConfirm={handleResolveConflicts} />
    </PageContainer>
  )
}
