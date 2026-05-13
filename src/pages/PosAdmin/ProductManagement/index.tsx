import {
  CloudDownloadOutlined,
  CloudSyncOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  ReloadOutlined,
  SafetyCertificateOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Cascader,
  Checkbox,
  Col,
  DatePicker,
  Descriptions,
  Divider,
  Form,
  Image,
  Input,
  InputNumber,
  message,
  Modal,
  Pagination,
  Popconfirm,
  Radio,
  Row,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tabs,
  Tag,
  Tooltip,
  Tree,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import {
  batchCreateSetCodes,
  batchDelete as batchDeleteSetCodes,
  batchUpdateBarcodes as batchUpdateSetBarcodes,
  batchUpdatePrices as batchUpdateSetPrices,
  getGridData,
} from '../../../services/multiCodeSetService'
import {
  batchUpdateProducts,
  getProducts,
  syncProductsFromHq,
  syncProductsToStores,
  updateProduct,
} from '../../../services/posProductService'
import { evaluatePricing } from '../../../services/pricingStrategyService'
import {
  createProductCategory,
  deleteProductCategory,
  getProductCategoryTree,
  updateProductCategory,
} from '../../../services/productCategoryService'
import { checkIntegrity, fixIntegrity } from '../../../services/productIntegrityService'
import { getActiveStores } from '../../../services/storeService'
import { useAuthStore } from '../../../store/auth'
import { isValidEAN13 } from '../../../utils/barcode'
import { copyTextToClipboard } from '../../../utils/clipboard'
import type { BatchUpdatePosProductDto, HqProductSyncResult, PosProductDto, PosProductFilterParams, SyncProductsToStoresRequest, SyncProductsToStoresResult } from '../../../types/posProduct'
import type { ProductCategoryDto } from '../../../types/productCategory'
import type { ProductIntegrityCheckResultDto, ProductIntegrityFixResultDto } from '../../../types/productIntegrity'
import type { MulticodeSetItem } from '../../../types/multiCodeSet'
import type { StoreOption } from '../../../services/storeService'

type ProductRow = PosProductDto & { key: string }

const SORT_FIELD_MAP: Record<string, string> = {
  productCode: 'productcode',
  barcode: 'barcode',
  productName: 'productname',
  localSupplierCode: 'localsuppliercode',
  categoryName: 'categoryname',
  purchasePrice: 'purchaseprice',
  retailPrice: 'retailprice',
  isActive: 'isactive',
  updatedAt: 'updatedat',
}

export default function ProductManagementPage() {
  const currentUser = useAuthStore((s) => s.currentUser)

  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<ProductRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [keyword, setKeyword] = useState('')
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [categoryGuid, setCategoryGuid] = useState<string | undefined>(undefined)
  const [isActiveFilter, setIsActiveFilter] = useState<boolean | undefined>(undefined)
  const [isSetFilter, setIsSetFilter] = useState<boolean | undefined>(undefined)
  const [sortBy, setSortBy] = useState('productCode')
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('ascend')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])
  const [categoryTree, setCategoryTree] = useState<ProductCategoryDto[]>([])
  const [storeOptions, setStoreOptions] = useState<StoreOption[]>([])

  const [editVisible, setEditVisible] = useState(false)
  const [editingProduct, setEditingProduct] = useState<PosProductDto | null>(null)
  const [editForm] = Form.useForm()

  const [batchEditVisible, setBatchEditVisible] = useState(false)
  const [batchEditForm] = Form.useForm()

  const [syncToStoreVisible, setSyncToStoreVisible] = useState(false)
  const [syncToStoreForm] = Form.useForm()
  const [syncToStoreLoading, setSyncToStoreLoading] = useState(false)
  const [syncSelectAll, setSyncSelectAll] = useState(false)

  const productTypeWatch = Form.useWatch('productType', editForm)
  const [editSetCodes, setEditSetCodes] = useState<any[]>([])
  const [editSetCodesLoading, setEditSetCodesLoading] = useState(false)
  const [editSetPriceEdits, setEditSetPriceEdits] = useState<Record<string, { setItemNumber?: string; setBarcode?: string; setPurchasePrice?: number; setRetailPrice?: number }>>({})
  const [editPendingDeletes, setEditPendingDeletes] = useState<Record<string, any>>({})

  const [setCodeVisible, setSetCodeVisible] = useState(false)
  const [setCodeProduct, setSetCodeProduct] = useState<PosProductDto | null>(null)
  const [setCodeData, setSetCodeData] = useState<MulticodeSetItem[]>([])
  const [setCodeLoading, setSetCodeLoading] = useState(false)
  const [setCodeEditingKey, setSetCodeEditingKey] = useState<string | null>(null)

  const [categoryModalVisible, setCategoryModalVisible] = useState(false)
  const [categoryEditForm] = Form.useForm()
  const [editingCategory, setEditingCategory] = useState<ProductCategoryDto | null>(null)

  const [integrityVisible, setIntegrityVisible] = useState(false)
  const [integrityLoading, setIntegrityLoading] = useState(false)
  const [integrityResult, setIntegrityResult] = useState<ProductIntegrityCheckResultDto | null>(null)
  const [fixLoading, setFixLoading] = useState(false)

  const wrapRef = useRef<HTMLDivElement>(null)
  const toolbarRef = useRef<HTMLDivElement>(null)
  const pagerRef = useRef<HTMLDivElement>(null)
  const [tableScrollY, setTableScrollY] = useState<number | undefined>(undefined)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const params: PosProductFilterParams = {
        pageIndex: page,
        pageSize,
        keyword: keyword || undefined,
        supplierCode: supplierCode || undefined,
        categoryGuid: categoryGuid || undefined,
        isActive: isActiveFilter,
        isSet: isSetFilter,
        sortBy: SORT_FIELD_MAP[sortBy] || sortBy,
        sortOrder,
      }
      const result = await getProducts(params)
      const items = (result?.items ?? []).map((it) => ({ ...it, key: it.productCode }))
      setData(items)
      setTotal(result?.total ?? 0)
    } catch {
      message.error('加载商品列表失败')
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, keyword, supplierCode, categoryGuid, isActiveFilter, isSetFilter, sortBy, sortOrder])

  const loadOptions = useCallback(async () => {
    try {
      const suppliers = await getActiveLocalSuppliers()
      setSupplierOptions(suppliers.map((s) => ({ label: `${s.name} (${s.localSupplierCode})`, value: s.localSupplierCode })))
    } catch { /* ignore */ }
    try {
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
    } catch { /* ignore */ }
    try {
      const stores = await getActiveStores()
      setStoreOptions(stores)
    } catch { /* ignore */ }
  }, [])

  useEffect(() => {
    loadOptions()
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  useLayoutEffect(() => {
    const calc = () => {
      const containerH = wrapRef.current?.clientHeight || window.innerHeight
      const tbarH = toolbarRef.current?.getBoundingClientRect().height || 0
      const pagerH = pagerRef.current?.getBoundingClientRect().height || 0
      const available = containerH - tbarH - pagerH - 8
      setTableScrollY(available > 200 ? available : 200)
    }
    calc()
    window.addEventListener('resize', calc)
    return () => window.removeEventListener('resize', calc)
  }, [pageSize, total])

  const handleSearch = (value: string) => {
    setKeyword(value)
    setPage(1)
  }

  const handleReset = () => {
    setKeyword('')
    setSupplierCode(undefined)
    setCategoryGuid(undefined)
    setIsActiveFilter(undefined)
    setIsSetFilter(undefined)
    setSortBy('productCode')
    setSortOrder('ascend')
    setPage(1)
  }

  const buildCategoryCascaderOptions = (nodes: ProductCategoryDto[]): any[] => {
    return nodes.map((node) => ({
      value: node.guid,
      label: node.name,
      children: node.children?.length ? buildCategoryCascaderOptions(node.children) : undefined,
    }))
  }

  const getCategoryValueFromGuid = (guid: string | undefined, nodes: ProductCategoryDto[]): string[] | undefined => {
    if (!guid) return undefined
    const findPath = (items: ProductCategoryDto[], path: string[]): string[] | undefined => {
      for (const item of items) {
        const currentPath = [...path, item.guid]
        if (item.guid === guid) return currentPath
        if (item.children?.length) {
          const found = findPath(item.children, currentPath)
          if (found) return found
        }
      }
      return undefined
    }
    return findPath(nodes, [])
  }

  const openEdit = (record: PosProductDto) => {
    setEditingProduct(record)
    editForm.setFieldsValue({
      productCode: record.productCode,
      barcode: record.barcode,
      productName: record.productName,
      productNameCn: record.productNameCn,
      itemNumber: record.itemNumber,
      localSupplierCode: record.localSupplierCode,
      productType: record.productType ?? 0,
      purchasePrice: record.purchasePrice,
      retailPrice: record.retailPrice,
      unitWeight: record.unitWeight ?? 1,
      middlePackageQuantity: record.middlePackageQuantity,
      isAutoPricing: record.isAutoPricing ?? false,
      isSpecialProduct: record.isSpecialProduct ?? false,
      isActive: record.isActive,
      categoryGuid: getCategoryValueFromGuid(record.categoryGuid, categoryTree),
    })
    setEditVisible(true)
  }

  const handleEditSave = async () => {
    if (!editingProduct) return
    try {
      const values = await editForm.validateFields()
      if (!validateEditSetCodes()) return
      const catValue = values.categoryGuid
      const resolvedCategoryGuid = Array.isArray(catValue) ? catValue[catValue.length - 1] : catValue
      const updateData: Partial<PosProductDto> = {
        productName: values.productName,
        itemNumber: values.itemNumber,
        barcode: values.barcode,
        purchasePrice: values.purchasePrice,
        retailPrice: values.retailPrice,
        unitWeight: values.unitWeight,
        middlePackageQuantity: values.middlePackageQuantity,
        productType: values.productType,
        isAutoPricing: values.isAutoPricing,
        isSpecialProduct: values.isSpecialProduct,
        isActive: values.isActive,
        categoryGuid: resolvedCategoryGuid,
      }
      await updateProduct(editingProduct.productCode, updateData)

      if (productTypeWatch === 1 || productTypeWatch === 2) {
        const deleteIds = Object.values(editPendingDeletes).filter((c: any) => c.id).map((c: any) => c.id)
        if (deleteIds.length) {
          await batchDeleteSetCodes({ ids: deleteIds })
        }

        const newRows = editSetCodes.filter((r: any) => !r.id)
        if (newRows.length) {
          await batchCreateSetCodes({
            items: newRows.map((r: any) => {
              const rowId = r._rowId
              const edit = editSetPriceEdits[rowId] || {}
              return {
                productCode: editingProduct.productCode,
                setItemNumber: (edit.setItemNumber ?? r.setItemNumber) || undefined,
                setBarcode: (edit.setBarcode ?? r.setBarcode) || undefined,
                setPurchasePrice: edit.setPurchasePrice ?? r.setPurchasePrice,
                setRetailPrice: edit.setRetailPrice ?? r.setRetailPrice,
                isActive: true,
              }
            }).filter((x: any) => (x.setItemNumber ?? '') !== '' || (x.setBarcode ?? '') !== '' || x.setPurchasePrice !== undefined || x.setRetailPrice !== undefined),
          })
        }

        const barcodeUpdates = Object.entries(editSetPriceEdits)
          .filter(([id, p]: [string, any]) => p.setBarcode !== undefined && editSetCodes.find((r: any) => (r.id || r._rowId) === id && r.id))
          .map(([id, p]: [string, any]) => ({ id, setBarcode: p.setBarcode }))
        if (barcodeUpdates.length) {
          await batchUpdateSetBarcodes({ items: barcodeUpdates })
        }

        const priceUpdates = Object.entries(editSetPriceEdits)
          .filter(([id, p]: [string, any]) => (p.setPurchasePrice !== undefined || p.setRetailPrice !== undefined) && editSetCodes.find((r: any) => (r.id || r._rowId) === id && r.id))
          .map(([id, p]: [string, any]) => ({ id, setPurchasePrice: p.setPurchasePrice, setRetailPrice: p.setRetailPrice }))
        if (priceUpdates.length) {
          await batchUpdateSetPrices({ items: priceUpdates })
        }
      }

      message.success('保存成功')
      setEditVisible(false)
      setEditingProduct(null)
      setEditSetCodes([])
      setEditSetPriceEdits({})
      setEditPendingDeletes({})
      await loadData()
    } catch {
      message.error('保存失败')
    }
  }

  const handleEvaluatePricing = async () => {
    try {
      const values = await editForm.validateFields(['purchasePrice', 'localSupplierCode'])
      const result = await evaluatePricing({
        purchasePrice: Number(values.purchasePrice),
        supplierCode: values.localSupplierCode,
      })
      if (result?.retailPrice) {
        editForm.setFieldsValue({ retailPrice: result.retailPrice })
        message.success(`建议零售价: ${result.retailPrice.toFixed(2)}`)
      }
    } catch {
      message.error('计算建议零售价失败')
    }
  }

  const handleBatchEnable = async (enable: boolean) => {
    if (!selectedRowKeys.length) {
      message.warning('请先选择商品')
      return
    }
    try {
      const items: BatchUpdatePosProductDto[] = selectedRowKeys.map((code) => ({
        productCode: String(code),
        isActive: enable,
      }))
      const result = await batchUpdateProducts(items)
      message.success(`成功更新 ${result.successCount} 个商品`)
      if (result.failedCount > 0) {
        message.warning(`${result.failedCount} 个商品更新失败`)
      }
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error('批量更新失败')
    }
  }

  const openBatchEdit = () => {
    if (!selectedRowKeys.length) {
      message.warning('请先选择商品')
      return
    }
    batchEditForm.resetFields()
    setBatchEditVisible(true)
  }

  const handleBatchEditSave = async () => {
    try {
      const values = await batchEditForm.validateFields()
      const catValue = values.categoryGuid
      const resolvedCategoryGuid = Array.isArray(catValue) ? catValue[catValue.length - 1] : catValue
      const items: BatchUpdatePosProductDto[] = selectedRowKeys.map((code) => ({
        productCode: String(code),
        retailPrice: values.retailPrice ?? undefined,
        purchasePrice: values.purchasePrice ?? undefined,
        middlePackageQuantity: values.middlePackageQuantity ?? undefined,
        isAutoPricing: values.isAutoPricing,
        isSpecialProduct: values.isSpecialProduct,
        isActive: values.isActive,
        categoryGuid: resolvedCategoryGuid ?? undefined,
        localSupplierCode: values.localSupplierCode ?? undefined,
      }))
      const result = await batchUpdateProducts(items)
      message.success(`成功更新 ${result.successCount} 个商品`)
      if (result.failedCount > 0) {
        message.warning(`${result.failedCount} 个商品更新失败`)
      }
      setBatchEditVisible(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error('批量编辑失败')
    }
  }

  const handleSyncFromHq = async () => {
    try {
      const result: HqProductSyncResult = await syncProductsFromHq()
      message.success(
        `同步完成：新增 ${result.createdCount ?? 0}，更新 ${result.updatedCount ?? 0}，跳过 ${result.skippedCount ?? 0}`,
      )
      if (result.errors?.length) {
        Modal.error({
          title: '部分同步错误',
          content: result.errors.join('\n'),
        })
      }
      await loadData()
    } catch {
      message.error('从HQ同步失败')
    }
  }

  const handleSyncToStores = async () => {
    try {
      const values = await syncToStoreForm.validateFields()
      setSyncToStoreLoading(true)
      const req: SyncProductsToStoresRequest = {
        productCodes: values.productCodes || [],
        storeCodes: values.storeCodes || [],
        overwrite: values.overwrite ?? false,
      }
      if (!req.productCodes.length && selectedRowKeys.length) {
        req.productCodes = selectedRowKeys.map(String)
      }
      if (!req.productCodes.length) {
        message.warning('请选择要同步的商品')
        return
      }
      if (!req.storeCodes.length) {
        message.warning('请选择目标分店')
        return
      }
      const result: SyncProductsToStoresResult = await syncProductsToStores(req)
      message.success(`同步完成：成功 ${result.successCount ?? 0}，失败 ${result.failedCount ?? 0}`)
      if (result.errors?.length) {
        Modal.error({
          title: '部分同步错误',
          content: result.errors.join('\n'),
        })
      }
      setSyncToStoreVisible(false)
      syncToStoreForm.resetFields()
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error('同步到分店失败')
    } finally {
      setSyncToStoreLoading(false)
    }
  }

  useEffect(() => {
    if (editVisible && (productTypeWatch === 1 || productTypeWatch === 2) && editingProduct) {
      setEditSetCodesLoading(true)
      getGridData({ productCode: editingProduct.productCode, pageIndex: 1, pageSize: 200 })
        .then((result) => {
          const items = (result?.items ?? []).map((r: any) => ({ ...r, _rowId: r.id || `loaded_${Date.now()}_${Math.random().toString(36).slice(2)}` }))
          setEditSetCodes(items)
        })
        .catch(() => message.error('加载条码数据失败'))
        .finally(() => setEditSetCodesLoading(false))
    } else {
      setEditSetCodes([])
      setEditSetPriceEdits({})
      setEditPendingDeletes({})
    }
  }, [editVisible, productTypeWatch])

  const handleProductTypeChange = (newType: number) => {
    if (newType === 0 && editSetCodes.length > 0) {
      message.warning('请先删除所有条码后再切换为普通商品')
      const current = editForm.getFieldValue('productType')
      setTimeout(() => editForm.setFieldValue('productType', current), 0)
    }
  }

  const editAddSetCodeRow = () => {
    const tempId = `tmp_${Date.now()}_${Math.random().toString(36).slice(2)}`
    let defaults: any = { setPurchasePrice: undefined, setRetailPrice: undefined }
    if (editingProduct) {
      if (productTypeWatch === 2) {
        defaults = { setPurchasePrice: editingProduct.purchasePrice, setRetailPrice: editingProduct.retailPrice }
      }
    }
    const newRow = { _rowId: tempId, id: undefined, productCode: editingProduct?.productCode || '', setItemNumber: '', setBarcode: '', ...defaults, isActive: true }
    setEditSetCodes((prev) => [...prev, newRow])
    setEditSetPriceEdits((prev) => ({ ...prev, [tempId]: { setItemNumber: '', setBarcode: '', ...defaults } }))
  }

  const editDeleteSetCodeRow = (row: any) => {
    const rowId = row.id || row._rowId
    if (row.id) {
      setEditPendingDeletes((prev) => ({ ...prev, [rowId]: row }))
    }
    setEditSetCodes((prev) => prev.filter((r) => (r.id || r._rowId) !== rowId))
    setEditSetPriceEdits((prev) => { const next = { ...prev }; delete next[rowId]; return next })
  }

  const editHandleRetailPriceChange = (row: any, retailPrice: number) => {
    const rowId = row.id || row._rowId
    if (productTypeWatch === 1 && editingProduct) {
      const mainPP = editingProduct.purchasePrice || 0
      const mainRP = editingProduct.retailPrice || 0
      let calcPP: number | undefined
      if (mainRP > 0 && retailPrice > 0) {
        calcPP = parseFloat((retailPrice * (mainPP / mainRP)).toFixed(2))
      }
      setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setRetailPrice: retailPrice, setPurchasePrice: calcPP } }))
    } else {
      setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setRetailPrice: retailPrice } }))
    }
  }

  const editHandlePurchasePriceChange = (row: any, purchasePrice: number) => {
    const rowId = row.id || row._rowId
    setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setPurchasePrice: purchasePrice } }))
  }

  const editHandleBarcodeChange = (row: any, barcode: string) => {
    const rowId = row.id || row._rowId
    setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setBarcode } }))
  }

  const editHandleItemNumberChange = (row: any, itemNumber: string) => {
    const rowId = row.id || row._rowId
    setEditSetPriceEdits((prev) => ({ ...prev, [rowId]: { ...prev[rowId], setItemNumber: itemNumber } }))
  }

  const validateEditSetCodes = (): boolean => {
    if (productTypeWatch !== 1 && productTypeWatch !== 2) return true
    for (const code of editSetCodes) {
      const rowId = code.id || code._rowId
      const edit = editSetPriceEdits[rowId] || {}
      const barcode = edit.setBarcode ?? code.setBarcode
      const retailPrice = edit.setRetailPrice ?? code.setRetailPrice
      if (!barcode || barcode.trim() === '') { message.error('条码不能为空'); return false }
      if (retailPrice === undefined || retailPrice === null) { message.error('零售价不能为空'); return false }
    }
    return true
  }

  const openSetCodeManager = async (product: PosProductDto) => {
    setSetCodeProduct(product)
    setSetCodeVisible(true)
    setSetCodeLoading(true)
    try {
      const result = await getGridData({ productCode: product.productCode, pageIndex: 1, pageSize: 200 })
      setSetCodeData(result?.items ?? [])
    } catch {
      message.error('加载套装条码失败')
    } finally {
      setSetCodeLoading(false)
    }
  }

  const handleAddSetCode = () => {
    const newItem: MulticodeSetItem = {
      id: `new_${Date.now()}`,
      productCode: setCodeProduct?.productCode || '',
      setItemNumber: '',
      setBarcode: '',
      setPurchasePrice: 0,
      setRetailPrice: 0,
      isActive: true,
    }
    setSetCodeData((prev) => [...prev, newItem])
  }

  const handleSetCodeChange = (id: string, field: keyof MulticodeSetItem, value: any) => {
    setSetCodeData((prev) =>
      prev.map((item) => (item.id === id ? { ...item, [field]: value } : item)),
    )
  }

  const handleDeleteSetCode = async (item: MulticodeSetItem) => {
    if (!item.id || item.id.startsWith('new_')) {
      setSetCodeData((prev) => prev.filter((i) => i.id !== item.id))
      return
    }
    try {
      await batchDeleteSetCodes({ ids: [item.id!] })
      message.success('删除成功')
      setSetCodeData((prev) => prev.filter((i) => i.id !== item.id))
    } catch {
      message.error('删除失败')
    }
  }

  const handleSaveSetCodes = async () => {
    try {
      const newItems = setCodeData.filter((i) => i.id?.startsWith('new_'))
      const existingItems = setCodeData.filter((i) => !i.id?.startsWith('new_'))

      if (newItems.length) {
        await batchCreateSetCodes({
          items: newItems.map((i) => ({
            productCode: i.productCode,
            setItemNumber: i.setItemNumber || undefined,
            setBarcode: i.setBarcode || undefined,
            setPurchasePrice: i.setPurchasePrice,
            setRetailPrice: i.setRetailPrice,
            isActive: i.isActive ?? true,
          })),
        })
      }

      if (existingItems.length) {
        await batchUpdateSetBarcodes({
          items: existingItems.map((i) => ({
            id: i.id!,
            setBarcode: i.setBarcode || undefined,
          })),
        })
        await batchUpdateSetPrices({
          items: existingItems.map((i) => ({
            id: i.id!,
            setPurchasePrice: i.setPurchasePrice,
            setRetailPrice: i.setRetailPrice,
          })),
        })
      }

      message.success('保存成功')
      if (setCodeProduct) {
        const result = await getGridData({ productCode: setCodeProduct.productCode, pageIndex: 1, pageSize: 200 })
        setSetCodeData(result?.items ?? [])
      }
    } catch {
      message.error('保存套装条码失败')
    }
  }

  const handleOpenCategoryModal = () => {
    setEditingCategory(null)
    categoryEditForm.resetFields()
    setCategoryModalVisible(true)
  }

  const handleEditCategory = (node: ProductCategoryDto) => {
    setEditingCategory(node)
    categoryEditForm.setFieldsValue({
      name: node.name,
      parentGuid: node.parentGuid,
      sortOrder: node.sortOrder,
    })
    setCategoryModalVisible(true)
  }

  const handleSaveCategory = async () => {
    try {
      const values = await categoryEditForm.validateFields()
      if (editingCategory) {
        await updateProductCategory(editingCategory.guid, {
          name: values.name,
          parentGuid: values.parentGuid,
          sortOrder: values.sortOrder,
        })
        message.success('更新分类成功')
      } else {
        await createProductCategory({
          name: values.name,
          parentGuid: values.parentGuid,
          sortOrder: values.sortOrder,
        })
        message.success('创建分类成功')
      }
      setCategoryModalVisible(false)
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
    } catch {
      message.error('保存分类失败')
    }
  }

  const handleDeleteCategory = async (guid: string) => {
    try {
      await deleteProductCategory(guid)
      message.success('删除分类成功')
      const tree = await getProductCategoryTree()
      setCategoryTree(tree ?? [])
    } catch {
      message.error('删除分类失败')
    }
  }

  const handleCheckIntegrity = async () => {
    setIntegrityLoading(true)
    setIntegrityResult(null)
    try {
      const result = await checkIntegrity()
      setIntegrityResult(result)
      if (!result.issues?.length) {
        message.success('数据一致性检查通过，没有发现问题')
      } else {
        message.warning(`发现 ${result.failedCount} 个问题`)
      }
    } catch {
      message.error('一致性检查失败')
    } finally {
      setIntegrityLoading(false)
    }
  }

  const handleFixIntegrity = async () => {
    setFixLoading(true)
    try {
      const result: ProductIntegrityFixResultDto = await fixIntegrity({ fixAll: true })
      message.success(`修复完成：成功 ${result.fixedCount ?? 0}，失败 ${result.failedCount ?? 0}`)
      if (result.errors?.length) {
        Modal.error({
          title: '部分修复错误',
          content: result.errors.join('\n'),
        })
      }
      await handleCheckIntegrity()
    } catch {
      message.error('修复失败')
    } finally {
      setFixLoading(false)
    }
  }

  const buildCategoryTreeData = (nodes: ProductCategoryDto[]): any[] => {
    return nodes.map((node) => ({
      key: node.guid,
      title: (
        <Space>
          <span>{node.name}</span>
          <Button size="small" type="link" icon={<EditOutlined />} onClick={() => handleEditCategory(node)} />
          <Popconfirm title="确认删除该分类？" onConfirm={() => handleDeleteCategory(node.guid)} okText="删除" cancelText="取消">
            <Button size="small" type="link" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
      children: node.children?.length ? buildCategoryTreeData(node.children) : [],
    }))
  }

  const columns: ColumnsType<ProductRow> = [
    {
      title: '序号',
      width: 60,
      align: 'right',
      fixed: 'left',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: '货号',
      dataIndex: 'itemNumber',
      width: 180,
      fixed: 'left',
      sorter: true,
      sortOrder: sortBy === 'productCode' ? sortOrder : undefined,
      render: (v: string, record) => (
        <Space size={4}>
          <a onClick={() => copyTextToClipboard(v || record.productCode)}>{v || record.productCode}</a>
          <Tooltip title="复制">
            <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => copyTextToClipboard(v || record.productCode)} />
          </Tooltip>
        </Space>
      ),
    },
    {
      title: '图片',
      dataIndex: 'productImage',
      width: 70,
      align: 'center',
      render: (v: string) =>
        v ? (
          <Image src={v} width={40} height={40} style={{ objectFit: 'contain' }} preview={{ mask: '' }} fallback="data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDAiIGhlaWdodD0iNDAiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjQwIiBoZWlnaHQ9IjQwIiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjAiIHk9IjI0IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmb250LXNpemU9IjEwIiBmaWxsPSIjY2NjIj7ml6DnvKk8L3RleHQ+PC9zdmc+" />
        ) : (
          <span style={{ color: '#ccc' }}>-</span>
        ),
    },
    {
      title: '条码',
      dataIndex: 'barcode',
      width: 180,
      render: (v: string) => <BarcodePreview value={v} compactCopy />,
    },
    {
      title: '商品名称',
      dataIndex: 'productName',
      width: 200,
      sorter: true,
      sortOrder: sortBy === 'productName' ? sortOrder : undefined,
      render: (v: string) => (
        <div style={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden', lineHeight: '20px' }}>
          {v}
        </div>
      ),
    },
    {
      title: '商品编码',
      dataIndex: 'productCode',
      width: 60,
      align: 'center',
      render: (v: string) => (
        <Tooltip title="复制商品编码">
          <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => copyTextToClipboard(v)} />
        </Tooltip>
      ),
    },
    {
      title: '供应商',
      dataIndex: 'localSupplierName',
      width: 140,
      sorter: true,
      sortOrder: sortBy === 'localSupplierCode' ? sortOrder : undefined,
      render: (v: string, record) => v || record.localSupplierCode || '-',
    },
    {
      title: '分类',
      dataIndex: 'categoryName',
      width: 120,
      sorter: true,
      sortOrder: sortBy === 'categoryName' ? sortOrder : undefined,
      render: (v: string) => v || '-',
    },
    {
      title: '进货价',
      dataIndex: 'purchasePrice',
      width: 110,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'purchasePrice' ? sortOrder : undefined,
      render: (v: number) => (v != null ? Number(v).toFixed(2) : '-'),
    },
    {
      title: '零售价',
      dataIndex: 'retailPrice',
      width: 110,
      align: 'right',
      sorter: true,
      sortOrder: sortBy === 'retailPrice' ? sortOrder : undefined,
      render: (v: number) => (v != null ? Number(v).toFixed(2) : '-'),
    },
    {
      title: '重量',
      dataIndex: 'unitWeight',
      width: 80,
      align: 'right',
      render: (v: number) => v ?? '-',
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 80,
      align: 'center',
      sorter: true,
      sortOrder: sortBy === 'isActive' ? sortOrder : undefined,
      render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? '启用' : '禁用'}</Tag>,
    },
    {
      title: '套装',
      dataIndex: 'isSet',
      width: 80,
      align: 'center',
      render: (v: boolean, record) =>
        v ? (
          <Tooltip title={`套装 (${record.setCount ?? 0} 个子码)`}>
            <Tag color="blue" style={{ cursor: 'pointer' }} onClick={() => openSetCodeManager(record)}>
              套装
            </Tag>
          </Tooltip>
        ) : (
          '-'
        ),
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      width: 160,
      sorter: true,
      sortOrder: sortBy === 'updatedAt' ? sortOrder : undefined,
      render: (v: string) => (v ? dayjs(v).format('YYYY-MM-DD HH:mm') : '-'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 200,
      fixed: 'right',
      render: (_, record) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          {record.isSet && (
            <Button type="link" size="small" onClick={() => openSetCodeManager(record)}>
              套装管理
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <PageContainer
      title="商品管理"
      subtitle={`共 ${total} 条记录`}
      extra={
        <Space>
          <Button icon={<SafetyCertificateOutlined />} onClick={() => { setIntegrityVisible(true); setIntegrityResult(null) }}>
            一致性检查
          </Button>
          <Button icon={<SettingOutlined />} onClick={handleOpenCategoryModal}>
            分类管理
          </Button>
        </Space>
      }
    >
      <div
        ref={wrapRef}
        style={{
          height: 'calc(100vh - 200px)',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
        }}
      >
        <div ref={toolbarRef} style={{ padding: '12px 0' }}>
          <Space wrap>
            <Input.Search
              allowClear
              placeholder="搜索代码/名称/条码"
              style={{ width: 260 }}
              onSearch={handleSearch}
              onClear={() => handleSearch('')}
            />
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="供应商"
              style={{ width: 200 }}
              value={supplierCode}
              onChange={(v) => { setSupplierCode(v); setPage(1) }}
              options={supplierOptions}
            />
            <Cascader
              allowClear
              placeholder="分类"
              style={{ width: 200 }}
              value={getCategoryValueFromGuid(categoryGuid, categoryTree)}
              onChange={(v) => { setCategoryGuid(v?.[v.length - 1]); setPage(1) }}
              options={buildCategoryCascaderOptions(categoryTree)}
              changeOnSelect
            />
            <Select
              allowClear
              placeholder="状态"
              style={{ width: 100 }}
              value={isActiveFilter}
              onChange={(v) => { setIsActiveFilter(v); setPage(1) }}
              options={[
                { label: '启用', value: true },
                { label: '禁用', value: false },
              ]}
            />
            <Select
              allowClear
              placeholder="套装"
              style={{ width: 100 }}
              value={isSetFilter}
              onChange={(v) => { setIsSetFilter(v); setPage(1) }}
              options={[
                { label: '套装', value: true },
                { label: '单品', value: false },
              ]}
            />
            <Button icon={<ReloadOutlined />} onClick={handleReset}>
              重置
            </Button>
            <Divider type="vertical" style={{ height: 32 }} />
            <Button icon={<CloudDownloadOutlined />} onClick={handleSyncFromHq}>
              从HQ同步
            </Button>
            <Button
              icon={<CloudUploadOutlined />}
              onClick={() => {
                syncToStoreForm.resetFields()
                setSyncSelectAll(false)
                syncToStoreForm.setFieldsValue({
                  syncPurchasePrice: true,
                  syncRetailPrice: true,
                  syncIsAutoPricing: true,
                  syncIsSpecialProduct: true,
                })
                if (selectedRowKeys.length) {
                  syncToStoreForm.setFieldsValue({
                    productCodes: selectedRowKeys.map(String),
                  })
                }
                setSyncToStoreVisible(true)
              }}
            >
              同步到分店
            </Button>
            <Button onClick={openBatchEdit} disabled={!selectedRowKeys.length}>
              批量编辑
            </Button>
            <Popconfirm title="确认启用选中的商品？" onConfirm={() => handleBatchEnable(true)}>
              <Button disabled={!selectedRowKeys.length}>批量启用</Button>
            </Popconfirm>
            <Popconfirm title="确认禁用选中的商品？" onConfirm={() => handleBatchEnable(false)}>
              <Button danger disabled={!selectedRowKeys.length}>
                批量禁用
              </Button>
            </Popconfirm>
          </Space>
        </div>

        <div style={{ flex: 1, minHeight: 0 }}>
          <Table
            virtual
            rowKey="key"
            loading={loading}
            dataSource={data}
            columns={columns}
            pagination={false}
            scroll={{ x: 1800, y: tableScrollY }}
            rowSelection={{
              selectedRowKeys,
              onChange: (keys) => setSelectedRowKeys(keys),
              columnWidth: 48,
            }}
            rowClassName={(_, index) => (index % 2 === 1 ? 'table-row-striped' : '')}
            onChange={(_pagination, _filters, sorter) => {
              const s = Array.isArray(sorter) ? sorter[0] : sorter
              const field = String(s?.field || s?.column?.dataIndex || 'productCode')
              const order = s?.order as 'ascend' | 'descend' | undefined
              if (order) {
                setSortBy(field)
                setSortOrder(order)
              } else {
                setSortBy('productCode')
                setSortOrder('ascend')
              }
            }}
          />
        </div>

        <div
          ref={pagerRef}
          style={{
            padding: '8px 0',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            width: '100%',
            background: '#fff',
            zIndex: 1,
          }}
        >
          <Space>
            <span>
              已选 {selectedRowKeys.length} 项
            </span>
            {selectedRowKeys.length > 0 && (
              <Button type="link" size="small" onClick={() => setSelectedRowKeys([])}>
                清空选择
              </Button>
            )}
          </Space>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={total}
            onChange={(p, ps) => {
              setPage(p)
              setPageSize(ps)
            }}
            showSizeChanger
            responsive={false}
            pageSizeOptions={[10, 20, 50, 100, 200]}
            showTotal={(t) => `共 ${t} 条`}
          />
        </div>
      </div>

      <Modal
        open={editVisible}
        title={editingProduct ? `编辑商品 - ${editingProduct.productCode}` : '编辑商品'}
        onCancel={() => { setEditVisible(false); setEditingProduct(null) }}
        onOk={handleEditSave}
        width={900}
        destroyOnClose
      >
        <Form form={editForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="productName" label="商品名称" rules={[{ required: true, message: '请输入商品名称' }]}>
            <Input />
          </Form.Item>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item label="货号">
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="itemNumber" noStyle>
                    <Input style={{ flex: 1 }} />
                  </Form.Item>
                  <Button icon={<CopyOutlined />} onClick={() => { const v = editForm.getFieldValue('itemNumber'); if (v) { copyTextToClipboard(v); message.success('复制成功') } }} />
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label="条码">
                <Space.Compact style={{ width: '100%' }}>
                  <Form.Item name="barcode" noStyle>
                    <Input style={{ flex: 1 }} />
                  </Form.Item>
                  <Button icon={<CopyOutlined />} onClick={() => { const v = editForm.getFieldValue('barcode'); if (v) { copyTextToClipboard(v); message.success('复制成功') } }} />
                </Space.Compact>
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="localSupplierCode" label="供应商">
                <Select allowClear showSearch optionFilterProp="label" options={supplierOptions} disabled />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="productType" label="商品类型">
                <Radio.Group onChange={(e) => handleProductTypeChange(e.target.value)}>
                  <Radio.Button value={0}>普通商品</Radio.Button>
                  <Radio.Button value={1}>套装商品</Radio.Button>
                  <Radio.Button value={2}>多条码商品</Radio.Button>
                </Radio.Group>
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="purchasePrice" label="采购价" rules={[{ required: true, message: '请输入采购价' }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="retailPrice" label="零售价" rules={[{ required: true, message: '请输入零售价' }]}>
                <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="middlePackageQuantity" label="中包数">
                <InputNumber min={1} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item shouldUpdate={(prev, cur) => prev.purchasePrice !== cur.purchasePrice || prev.retailPrice !== cur.retailPrice} noStyle>
                {({ getFieldValue }) => {
                  const pp = getFieldValue('purchasePrice')
                  const rp = getFieldValue('retailPrice')
                  const rate = pp > 0 ? (rp / pp) : undefined
                  return rate !== undefined ? (
                    <Form.Item label="当前倍率">
                      <Input value={rate.toFixed(2)} disabled />
                    </Form.Item>
                  ) : null
                }}
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="isAutoPricing" label="自动定价" valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren="是" unCheckedChildren="否" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="isSpecialProduct" label="特殊商品" valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren="是" unCheckedChildren="否" />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="isActive" label="是否启用" valuePropName="checked" labelCol={{ span: 12 }} wrapperCol={{ span: 12 }}>
                <Switch checkedChildren="启用" unCheckedChildren="禁用" />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="unitWeight" label="重量">
                <InputNumber min={0} precision={3} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="categoryGuid" label="分类">
                <Cascader
                  allowClear
                  showSearch
                  changeOnSelect
                  options={buildCategoryCascaderOptions(categoryTree)}
                  placeholder="选择分类"
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
          </Row>
        </Form>
        {productTypeWatch === 1 && (
          <div style={{ marginTop: 12 }}>
            <Space style={{ marginBottom: 8 }}>
              <Button type="dashed" onClick={editAddSetCodeRow}>添加条码</Button>
              <span style={{ fontSize: 12, color: '#52c41a' }}>套装条码采购价和零售价和主条码不一致</span>
            </Space>
            <Table
              rowKey={(r: any) => r.id || r._rowId}
              loading={editSetCodesLoading}
              dataSource={editSetCodes}
              pagination={false}
              size="small"
              locale={{ emptyText: '暂无套装条码' }}
              columns={[
                { title: '套装条码 *', dataIndex: 'setBarcode', width: 220, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return (<Space.Compact style={{ width: '100%' }}><Input style={{ flex: 1 }} value={edit.setBarcode ?? row.setBarcode} placeholder="请输入条码" onChange={(e) => editHandleBarcodeChange(row, e.target.value)} /><Button type="text" size="small" icon={<CopyOutlined />} onClick={() => { const v = editSetPriceEdits[rowId]?.setBarcode ?? row.setBarcode; if (v) { copyTextToClipboard(v); message.success('复制成功') } }} style={{ padding: '0 4px' }} /></Space.Compact>) } },
                { title: '采购价', dataIndex: 'setPurchasePrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setPurchasePrice !== undefined ? edit.setPurchasePrice : row.setPurchasePrice} placeholder="根据零售价自动计算" onChange={(v) => v !== undefined && editHandlePurchasePriceChange(row, v)} /> } },
                { title: '零售价', dataIndex: 'setRetailPrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setRetailPrice !== undefined ? edit.setRetailPrice : row.setRetailPrice} onChange={(v) => v !== undefined && editHandleRetailPriceChange(row, v!)} /> } },
                { title: '状态', dataIndex: 'isActive', width: 80, render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? '启用' : '禁用'}</Tag> },
                { title: '操作', width: 80, render: (_: any, row: any) => <Button type="link" danger size="small" onClick={() => editDeleteSetCodeRow(row)}>删除</Button> },
              ]}
            />
          </div>
        )}
        {productTypeWatch === 2 && (
          <div style={{ marginTop: 12 }}>
            <Space style={{ marginBottom: 8 }}>
              <Button type="dashed" onClick={editAddSetCodeRow}>添加条码</Button>
              <span style={{ fontSize: 12, color: '#52c41a' }}>多条码零售价和采购价和主条码一致</span>
            </Space>
            <Table
              rowKey={(r: any) => r.id || r._rowId}
              loading={editSetCodesLoading}
              dataSource={editSetCodes}
              pagination={false}
              size="small"
              locale={{ emptyText: '暂无多码条码' }}
              columns={[
                { title: '多码条码 *', dataIndex: 'setBarcode', width: 220, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return (<Space.Compact style={{ width: '100%' }}><Input style={{ flex: 1 }} value={edit.setBarcode ?? row.setBarcode} placeholder="请输入条码" onChange={(e) => editHandleBarcodeChange(row, e.target.value)} /><Button type="text" size="small" icon={<CopyOutlined />} onClick={() => { const v = editSetPriceEdits[rowId]?.setBarcode ?? row.setBarcode; if (v) { copyTextToClipboard(v); message.success('复制成功') } }} style={{ padding: '0 4px' }} /></Space.Compact>) } },
                { title: '采购价', dataIndex: 'setPurchasePrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setPurchasePrice !== undefined ? edit.setPurchasePrice : row.setPurchasePrice} onChange={(v) => v !== undefined && editHandlePurchasePriceChange(row, v)} /> } },
                { title: '零售价', dataIndex: 'setRetailPrice', width: 120, render: (_: any, row: any) => { const rowId = row.id || row._rowId; const edit = editSetPriceEdits[rowId] || {}; return <InputNumber style={{ width: '100%' }} min={0} step={0.01} value={edit.setRetailPrice !== undefined ? edit.setRetailPrice : row.setRetailPrice} onChange={(v) => v !== undefined && editHandleRetailPriceChange(row, v!)} /> } },
                { title: '状态', dataIndex: 'isActive', width: 80, render: (v: boolean) => <Tag color={v ? 'green' : 'red'}>{v ? '启用' : '禁用'}</Tag> },
                { title: '操作', width: 80, render: (_: any, row: any) => <Button type="link" danger size="small" onClick={() => editDeleteSetCodeRow(row)}>删除</Button> },
              ]}
            />
          </div>
        )}
      </Modal>

      <Modal
        open={batchEditVisible}
        title={`批量编辑 (${selectedRowKeys.length} 个商品)`}
        onCancel={() => setBatchEditVisible(false)}
        onOk={handleBatchEditSave}
        width={600}
        destroyOnClose
      >
        <Form form={batchEditForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item name="categoryGuid" label="商品分类">
            <Cascader
              allowClear
              showSearch
              changeOnSelect
              options={buildCategoryCascaderOptions(categoryTree)}
              placeholder="留空不修改"
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item name="localSupplierCode" label="供应商">
            <Select allowClear showSearch optionFilterProp="label" options={supplierOptions} placeholder="留空不修改" />
          </Form.Item>
          <Form.Item name="purchasePrice" label="采购价">
            <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} placeholder="留空不修改" />
          </Form.Item>
          <Form.Item name="retailPrice" label="零售价">
            <InputNumber min={0} precision={2} prefix="$" style={{ width: '100%' }} placeholder="留空不修改" />
          </Form.Item>
          <Form.Item name="middlePackageQuantity" label="中包数">
            <InputNumber min={1} style={{ width: '100%' }} placeholder="留空不修改" />
          </Form.Item>
          <Form.Item name="isAutoPricing" label="自动定价">
            <Select placeholder="留空不修改" allowClear>
              <Select.Option value={true}>是</Select.Option>
              <Select.Option value={false}>否</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="isSpecialProduct" label="特殊商品">
            <Select placeholder="留空不修改" allowClear>
              <Select.Option value={true}>是</Select.Option>
              <Select.Option value={false}>否</Select.Option>
            </Select>
          </Form.Item>
          <Form.Item name="isActive" label="是否启用">
            <Select placeholder="留空不修改" allowClear>
              <Select.Option value={true}>启用</Select.Option>
              <Select.Option value={false}>禁用</Select.Option>
            </Select>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={syncToStoreVisible}
        title={`同步到分店 (已选 ${selectedRowKeys.length} 个商品)`}
        onCancel={() => setSyncToStoreVisible(false)}
        onOk={handleSyncToStores}
        confirmLoading={syncToStoreLoading}
        width={600}
        destroyOnClose
      >
        <Form form={syncToStoreForm} labelCol={{ span: 6 }} wrapperCol={{ span: 18 }}>
          <Form.Item label="同步字段" required>
            <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
              <Checkbox>进货价</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
              <Checkbox>零售价</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
              <Checkbox>是否自动定价</Checkbox>
            </Form.Item>
            <div />
            <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
              <Checkbox>是否特殊商品</Checkbox>
            </Form.Item>
          </Form.Item>
          <Form.Item name="storeCodes" label="目标分店" rules={[{ required: true, message: '请选择目标分店' }]}>
            <Select
              mode="multiple"
              showSearch
              allowClear
              optionFilterProp="label"
              placeholder="选择目标分店"
              options={storeOptions}
              onChange={(values) => setSyncSelectAll(values.length === storeOptions.length)}
            />
          </Form.Item>
          <Form.Item label=" " colon={false}>
            <Checkbox
              checked={syncSelectAll}
              onChange={(e) => {
                const checked = e.target.checked
                setSyncSelectAll(checked)
                if (checked) {
                  syncToStoreForm.setFieldValue('storeCodes', storeOptions.map((s) => s.value))
                } else {
                  syncToStoreForm.setFieldValue('storeCodes', [])
                }
              }}
            >
              全选所有分店 ({storeOptions.length} 个)
            </Checkbox>
          </Form.Item>
          <div style={{ marginTop: 16, color: '#888' }}>
            <p>
              已选商品: <strong>{selectedRowKeys.length}</strong> 个
            </p>
            <p>说明：</p>
            <ul style={{ margin: 0, paddingLeft: 20 }}>
              <li>如果分店不存在该商品，将创建包含所有字段的新记录</li>
              <li>如果分店已存在该商品，将只更新选中的字段</li>
            </ul>
          </div>
        </Form>
      </Modal>

      <Modal
        open={setCodeVisible}
        title={`套装条码管理 - ${setCodeProduct?.productCode || ''}`}
        onCancel={() => { setSetCodeVisible(false); setSetCodeProduct(null); setSetCodeData([]) }}
        onOk={handleSaveSetCodes}
        width={900}
        destroyOnClose
      >
        <Spin spinning={setCodeLoading}>
          <div style={{ marginBottom: 12 }}>
            <Button type="dashed" icon={<PlusOutlined />} onClick={handleAddSetCode}>
              添加子码
            </Button>
          </div>
          <Table
            rowKey={(r) => r.id || ''}
            dataSource={setCodeData}
            pagination={false}
            size="small"
            scroll={{ y: 400 }}
            columns={[
              {
                title: '套装货号',
                dataIndex: 'setItemNumber',
                width: 150,
                render: (v: string, record) =>
                  setCodeEditingKey === record.id ? (
                    <Input
                      size="small"
                      value={v}
                      onChange={(e) => handleSetCodeChange(record.id!, 'setItemNumber', e.target.value)}
                    />
                  ) : (
                    v || '-'
                  ),
              },
              {
                title: '条码',
                dataIndex: 'setBarcode',
                width: 200,
                render: (v: string, record) =>
                  setCodeEditingKey === record.id ? (
                    <Input
                      size="small"
                      value={v}
                      onChange={(e) => handleSetCodeChange(record.id!, 'setBarcode', e.target.value)}
                    />
                  ) : (
                    <BarcodePreview value={v} compactCopy />
                  ),
              },
              {
                title: '进货价',
                dataIndex: 'setPurchasePrice',
                width: 120,
                render: (v: number, record) =>
                  setCodeEditingKey === record.id ? (
                    <InputNumber
                      size="small"
                      min={0}
                      precision={2}
                      value={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'setPurchasePrice', val)}
                    />
                  ) : (
                    (v ?? 0).toFixed(2)
                  ),
              },
              {
                title: '零售价',
                dataIndex: 'setRetailPrice',
                width: 120,
                render: (v: number, record) =>
                  setCodeEditingKey === record.id ? (
                    <InputNumber
                      size="small"
                      min={0}
                      precision={2}
                      value={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'setRetailPrice', val)}
                    />
                  ) : (
                    (v ?? 0).toFixed(2)
                  ),
              },
              {
                title: '启用',
                dataIndex: 'isActive',
                width: 80,
                render: (v: boolean, record) =>
                  setCodeEditingKey === record.id ? (
                    <Switch
                      size="small"
                      checked={v}
                      onChange={(val) => handleSetCodeChange(record.id!, 'isActive', val)}
                    />
                  ) : (
                    <Tag color={v ? 'green' : 'red'}>{v ? '启用' : '禁用'}</Tag>
                  ),
              },
              {
                title: '操作',
                width: 120,
                render: (_, record) => (
                  <Space>
                    {setCodeEditingKey === record.id ? (
                      <Button size="small" type="link" onClick={() => setSetCodeEditingKey(null)}>
                        完成
                      </Button>
                    ) : (
                      <Button size="small" type="link" onClick={() => setSetCodeEditingKey(record.id ?? null)}>
                        编辑
                      </Button>
                    )}
                    <Popconfirm title="确认删除？" onConfirm={() => handleDeleteSetCode(record)}>
                      <Button size="small" type="link" danger>
                        删除
                      </Button>
                    </Popconfirm>
                  </Space>
                ),
              },
            ]}
          />
        </Spin>
      </Modal>

      <Modal
        open={categoryModalVisible}
        title="商品分类管理"
        onCancel={() => setCategoryModalVisible(false)}
        footer={[
          <Button key="close" onClick={() => setCategoryModalVisible(false)}>
            关闭
          </Button>,
          <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => { setEditingCategory(null); categoryEditForm.resetFields() }}>
            新建分类
          </Button>,
        ]}
        width={700}
      >
        <div style={{ display: 'flex', gap: 16 }}>
          <div style={{ flex: 1 }}>
            {categoryTree.length > 0 ? (
              <Tree
                defaultExpandAll
                treeData={buildCategoryTreeData(categoryTree)}
              />
            ) : (
              <div style={{ textAlign: 'center', padding: 24, color: '#999' }}>暂无分类数据</div>
            )}
          </div>
          <div style={{ width: 280 }}>
            <Card title={editingCategory ? '编辑分类' : '新建分类'} size="small">
              <Form form={categoryEditForm} layout="vertical">
                <Form.Item name="name" label="名称" rules={[{ required: true, message: '请输入分类名称' }]}>
                  <Input />
                </Form.Item>
                <Form.Item name="parentGuid" label="父分类">
                  <Cascader
                    allowClear
                    showSearch
                    changeOnSelect
                    options={buildCategoryCascaderOptions(categoryTree)}
                    placeholder="无（顶级分类）"
                  />
                </Form.Item>
                <Form.Item name="sortOrder" label="排序">
                  <InputNumber min={0} style={{ width: '100%' }} />
                </Form.Item>
                <Form.Item>
                  <Button type="primary" onClick={handleSaveCategory}>
                    {editingCategory ? '更新' : '创建'}
                  </Button>
                </Form.Item>
              </Form>
            </Card>
          </div>
        </div>
      </Modal>

      <Modal
        open={integrityVisible}
        title="数据一致性检查"
        onCancel={() => setIntegrityVisible(false)}
        footer={[
          <Button key="close" onClick={() => setIntegrityVisible(false)}>
            关闭
          </Button>,
          <Button key="check" type="primary" loading={integrityLoading} onClick={handleCheckIntegrity} icon={<SafetyCertificateOutlined />}>
            检查
          </Button>,
          integrityResult && integrityResult.issues?.length > 0 ? (
            <Button key="fix" type="primary" danger loading={fixLoading} onClick={handleFixIntegrity} icon={<CloudSyncOutlined />}>
              自动修复
            </Button>
          ) : null,
        ]}
        width={800}
        destroyOnClose
      >
        <Spin spinning={integrityLoading}>
          {integrityResult ? (
            <div>
              <Descriptions bordered size="small" column={3} style={{ marginBottom: 16 }}>
                <Descriptions.Item label="总商品数">{integrityResult.totalProducts}</Descriptions.Item>
                <Descriptions.Item label="通过">
                  <Tag color="green">{integrityResult.passedCount}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label="问题">
                  <Tag color="red">{integrityResult.failedCount}</Tag>
                </Descriptions.Item>
              </Descriptions>
              {integrityResult.issues?.length > 0 ? (
                <Table
                  rowKey={(r, i) => `${r.productCode}_${r.issueType}_${i}`}
                  dataSource={integrityResult.issues}
                  pagination={false}
                  size="small"
                  scroll={{ y: 300 }}
                  columns={[
                    { title: '商品代码', dataIndex: 'productCode', width: 140 },
                    { title: '问题类型', dataIndex: 'issueType', width: 140 },
                    { title: '描述', dataIndex: 'description' },
                    {
                      title: '严重程度',
                      dataIndex: 'severity',
                      width: 100,
                      render: (v: string) => (
                        <Tag color={v === 'Error' ? 'red' : 'orange'}>{v === 'Error' ? '错误' : '警告'}</Tag>
                      ),
                    },
                  ]}
                />
              ) : (
                <div style={{ textAlign: 'center', padding: 24, color: '#52c41a' }}>
                  所有商品数据一致性检查通过
                </div>
              )}
            </div>
          ) : (
            <div style={{ textAlign: 'center', padding: 24, color: '#999' }}>
              点击"检查"按钮开始数据一致性检查
            </div>
          )}
        </Spin>
      </Modal>
    </PageContainer>
  )
}
