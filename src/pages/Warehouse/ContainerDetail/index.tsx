import {
  ArrowLeftOutlined,
  CloudUploadOutlined,
  CopyOutlined,
  DeleteOutlined,
  DownloadOutlined,
  EditOutlined,
  ReloadOutlined,
  SaveOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  DatePicker,
  Descriptions,
  Dropdown,
  Image,
  Input,
  InputNumber,
  Modal,
  Progress,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { TFunction } from 'i18next'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useEffect, useMemo, useRef, useState, type Key, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import {
  batchDeleteDetails,
  batchUpdateDetails,
  getContainerDetail,
  getContainerProducts,
  translateHqProductNamesByContainerNumber,
  updateContainer,
} from '../../../services/containerService'
import {
  buildContainerCreateProductsOperationId,
  createContainerProductCreationJob,
  waitForContainerProductCreationJob,
  type ContainerProductCreationJob,
  type ContainerProductCreationResultItem,
} from '../../../services/containerProductCreationService'
import { exportContainerDetailsToExcel, type ContainerDetailExportItem } from '../../../services/exportService'
import { pushProductsToHq } from '../../../services/posProductService'
import { upsertForActiveStores as upsertMultiCodeForActiveStores } from '../../../services/storeMultiCodePriceService'
import { upsertForActiveStores as upsertRetailForActiveStores } from '../../../services/storeRetailPriceService'
import { batchTranslate } from '../../../services/translationService'
import {
  batchUpdateWarehouseProducts,
  bulkSetStatus,
  detectProducts,
} from '../../../services/warehouseProductService'
import { useAuthStore } from '../../../store/auth'
import type { ContainerDetail, ContainerMain, HqTranslationResult, UpdateContainerDetailRequest, UpdateContainerRequest } from '../../../types/container'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { shouldShowDetailInitialLoading } from '../../../utils/detailLoadState'
import {
  applyContainerDetailEnglishNameUpdates,
  buildContainerDetailTagStats,
  buildContainerDetailFloatRateUpdates,
  buildContainerDetailHqPushSelection,
  buildContainerDetailTranslationUpdates,
  extractPushToHqErrorResult,
  getContainerDetailEnglishName,
  getContainerDetailProductName,
  matchesContainerDetailSelectedTags,
  mergeContainerDetailPatch,
  type ContainerDetailTagFilter,
} from './containerDetailLogic'
import type { PushProductsToHqResult } from '../../../types/posProduct'
import './index.css'

type ProductTypeFilter = 'all' | 'normal' | 'set' | 'setChild'

function formatDate(value?: string) {
  return value ? dayjs(value).format('YYYY-MM-DD') : '--'
}

function formatNumber(value?: number, digits = 2) {
  return value == null ? '--' : value.toLocaleString('zh-CN', { maximumFractionDigits: digits, minimumFractionDigits: digits })
}

function rowKey(row: ContainerDetail) {
  return row.hguid || String(row.id)
}

function getStatusTag(status: number | undefined, t: TFunction) {
  if (status == null) return <Tag>{t('containers.status.unknown')}</Tag>
  const map: Record<number, { color: string; labelKey: string }> = {
    0: { color: 'blue', labelKey: 'loaded' },
    1: { color: 'orange', labelKey: 'inTransit' },
    2: { color: 'success', labelKey: 'completed' },
    7: { color: 'error', labelKey: 'cancelled' },
  }
  const item = map[status]
  return item ? <Tag color={item.color}>{t(`containers.status.${item.labelKey}`)}</Tag> : <Tag>{t('containers.status.unknownWithCode', { status })}</Tag>
}

function getProductTypeLabel(value: string | undefined, t: TFunction) {
  const type = value || '普通商品'
  const map: Record<string, string> = {
    全部: 'common.all',
    普通商品: 'containers.productTypes.normal',
    套装商品: 'containers.productTypes.set',
    套装子商品: 'containers.productTypes.setChild',
  }
  return map[type] ? t(map[type]) : type
}

function getProductTypeFilterKey(value: string | undefined): ProductTypeFilter {
  const type = value || '普通商品'
  if (type === '套装商品') return 'set'
  if (type === '套装子商品') return 'setChild'
  return 'normal'
}

function getProductTypeFilterLabel(value: ProductTypeFilter, t: TFunction) {
  const map: Record<ProductTypeFilter, string> = {
    all: 'common.all',
    normal: 'containers.productTypes.normal',
    set: 'containers.productTypes.set',
    setChild: 'containers.productTypes.setChild',
  }
  return t(map[value])
}

function CopyableText({ value, maxWidth }: { value?: string; maxWidth?: number }) {
  if (!value) {
    return <>--</>
  }

  return (
    <Space size={4} wrap={false} className="container-detail-nowrap container-detail-copyable">
      <Typography.Text style={maxWidth ? { maxWidth } : undefined} ellipsis={maxWidth ? { tooltip: value } : false}>
        {value}
      </Typography.Text>
      <Tooltip title="复制">
        <Button
          size="small"
          type="text"
          aria-label={`复制 ${value}`}
          icon={<CopyOutlined />}
          className="container-detail-copy-button"
          onClick={(event) => {
            event.stopPropagation()
            void copyTextToClipboard(value)
          }}
        />
      </Tooltip>
    </Space>
  )
}

// 表格密集显示专用：关键文本限制两行，数字列保持单行便于快速扫读。
function TwoLineText({ value }: { value?: string }) {
  if (!value) {
    return <>--</>
  }

  return (
    <Tooltip title={value}>
      <span className="container-detail-two-line-text">{value}</span>
    </Tooltip>
  )
}

function renderNumericCell(value: ReactNode) {
  return <span className="container-detail-nowrap container-detail-numeric-cell">{value}</span>
}

function renderCompactHeader(value: ReactNode) {
  return <span className="container-detail-header-title">{value}</span>
}

export default function ContainerDetailPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const route = useStableRouteContext()
  const [containerGuid] = useState(() => route?.params.containerGuid || '')
  const access = useAuthStore((state) => state.access)
  // 记录当前货柜已完成首次加载，保活 Tab 恢复时保留旧内容并静默刷新。
  const loadedContainerGuidRef = useRef<string | null>(null)
  const visibleContainerGuidRef = useRef<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [savingHeader, setSavingHeader] = useState(false)
  const [container, setContainer] = useState<ContainerMain | null>(null)
  const [rows, setRows] = useState<ContainerDetail[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])
  const [itemNumberFilter, setItemNumberFilter] = useState('')
  const [productTypeFilter, setProductTypeFilter] = useState<ProductTypeFilter>('all')
  const [selectedTagFilters, setSelectedTagFilters] = useState<ContainerDetailTagFilter[]>([])
  const [batchFloatRate, setBatchFloatRate] = useState<number | null>(null)
  const [batchImportPrice, setBatchImportPrice] = useState<number | null>(null)
  const [batchOemPrice, setBatchOemPrice] = useState<number | null>(null)
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [hqTranslating, setHqTranslating] = useState(false)
  const [pushToHqLoading, setPushToHqLoading] = useState(false)
  const [recalculateCostsLoading, setRecalculateCostsLoading] = useState(false)
  const [createProductsLoading, setCreateProductsLoading] = useState(false)
  const pushToHqLoadingRef = useRef(false)
  const createProductsLoadingRef = useRef(false)
  const [headerEditing, setHeaderEditing] = useState(false)
  const [headerForm, setHeaderForm] = useState<{
    实际到货日期?: Dayjs | null
    汇率?: number
    运费?: number
    备注?: string
  }>({})

  useDynamicTabTitle(container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : undefined)

  const loadData = async (showLoading = true) => {
    if (!containerGuid) {
      return
    }
    if (showLoading) {
      setLoading(true)
    }
    try {
      const [info, products] = await Promise.all([
        getContainerDetail(containerGuid),
        getContainerProducts(containerGuid),
      ])
      loadedContainerGuidRef.current = containerGuid
      visibleContainerGuidRef.current = containerGuid
      setContainer(info)
      setHeaderForm({
        实际到货日期: info.实际到货日期 ? dayjs(info.实际到货日期) : null,
        汇率: info.汇率,
        运费: info.运费,
        备注: info.备注,
      })
      setRows(products)
      setSelectedRowKeys([])
    } catch (error) {
      console.error(error)
      if (showLoading) {
        visibleContainerGuidRef.current = null
      }
      message.error(error instanceof Error ? error.message : t('containers.messages.loadDetailFailed'))
    } finally {
      if (showLoading) {
        setLoading(false)
      }
    }
  }

  useEffect(() => {
    const shouldShowInitialLoading = shouldShowDetailInitialLoading({
      requestedDetailId: containerGuid,
      loadedDetailId: loadedContainerGuidRef.current,
      visibleDetailId: visibleContainerGuidRef.current,
    })
    void loadData(shouldShowInitialLoading)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [containerGuid])

  useEffect(() => {
    setSelectedRowKeys([])
  }, [itemNumberFilter, productTypeFilter, selectedTagFilters])

  const baseFilteredRows = useMemo(() => {
    return rows.filter((row) => {
      const itemNumber = row.商品信息?.货号 || ''
      if (itemNumberFilter && !itemNumber.toLowerCase().includes(itemNumberFilter.toLowerCase())) return false
      if (productTypeFilter !== 'all' && getProductTypeFilterKey(row.商品类型 || row.商品信息?.商品类型) !== productTypeFilter) return false
      return true
    })
  }, [itemNumberFilter, productTypeFilter, rows])

  const filteredRows = useMemo(() => {
    return baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))
  }, [baseFilteredRows, selectedTagFilters])

  const tagStats = useMemo(() => buildContainerDetailTagStats(baseFilteredRows), [baseFilteredRows])

  const tagStatOptions = useMemo<Array<{ value: ContainerDetailTagFilter; label: string; color?: string }>>(() => [
    { value: 'all', label: t('containers.filters.allTags'), color: 'blue' },
    { value: 'new', label: t('containers.tags.newProduct'), color: 'cyan' },
    { value: 'existing', label: t('containers.tags.existingProduct'), color: 'purple' },
    { value: 'noOemPrice', label: t('containers.filters.missingOemPrice'), color: 'orange' },
    { value: 'abnormalImport', label: t('containers.filters.abnormalImportPrice'), color: 'red' },
    { value: 'active', label: t('common.activeUpper'), color: 'success' },
    { value: 'inactive', label: t('common.inactiveUpper'), color: 'volcano' },
  ], [t])

  const selectedTagOptions = useMemo(
    () => tagStatOptions.filter((option) => option.value !== 'all' && selectedTagFilters.includes(option.value)),
    [selectedTagFilters, tagStatOptions],
  )

  const tagSelectOptions = useMemo(
    () => tagStatOptions.filter((option) => option.value !== 'all').map(({ value, label }) => ({ value, label })),
    [tagStatOptions],
  )

  const setTagFiltersFromSelect = (values: ContainerDetailTagFilter[]) => {
    setSelectedTagFilters(values.includes('all') ? [] : values)
  }

  const toggleTagFilter = (value: ContainerDetailTagFilter) => {
    if (value === 'all') {
      setSelectedTagFilters([])
      return
    }

    setSelectedTagFilters((current) => (
      current.includes(value)
        ? current.filter((item) => item !== value)
        : [...current, value]
    ))
  }

  const selectedRows = useMemo(
    () => filteredRows.filter((row) => selectedRowKeys.includes(rowKey(row))),
    [filteredRows, selectedRowKeys],
  )

  const hasHiddenSelectedRows = selectedRowKeys.length > 0 && selectedRows.length === 0
  const targetRows = selectedRowKeys.length ? selectedRows : filteredRows

  const ensureTargetRowsVisible = () => {
    if (!hasHiddenSelectedRows) return true
    message.warning(t('containers.messages.selectedRowsHidden', '已选明细不在当前筛选结果中，请重新选择后再操作'))
    return false
  }
  const canCreateContainerProducts = access.canEditContainer && access.canManagePosProducts

  const patchRow = (key: string, patch: Partial<ContainerDetail>) => {
    setRows((items) => items.map((item) => (rowKey(item) === key ? mergeContainerDetailPatch(item, patch) : item)))
  }

  const saveRowPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {
    if (!access.canEditContainer || !row.hguid) return
    patchRow(rowKey(row), patch)
    await batchUpdateDetails([{ hguid: row.hguid, ...patch } as UpdateContainerDetailRequest])
  }

  const applyDetailUpdatesToRows = (updates: UpdateContainerDetailRequest[]) => {
    setRows((items) =>
      items.map((item) => {
        const match = updates.find((update) => update.hguid === item.hguid)
        return match ? mergeContainerDetailPatch(item, match as Partial<ContainerDetail>) : item
      }),
    )
  }

  const saveHeader = async () => {
    if (!containerGuid || !access.canEditContainer) return
    setSavingHeader(true)
    const updatePayload: UpdateContainerRequest = {
      实际到货日期: headerForm.实际到货日期 ? headerForm.实际到货日期.format('YYYY-MM-DD') : undefined,
      汇率: headerForm.汇率,
      运费: headerForm.运费,
      备注: headerForm.备注,
    }
    try {
      await updateContainer(containerGuid, updatePayload)
      message.success(t('containers.messages.headerSaveSuccess'))
      setHeaderEditing(false)
      await loadData()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('containers.messages.headerSaveFailed'))
    } finally {
      setSavingHeader(false)
    }
  }

  const saveFloatRatePatch = async (row: ContainerDetail, value?: number) => {
    if (value == null) {
      await saveRowPatch(row, { 调整浮率: undefined })
      return
    }

    const updates = buildContainerDetailFloatRateUpdates([row], container, value)
    const update = updates[0]
    if (!update) return
    applyDetailUpdatesToRows(updates)
    await batchUpdateDetails(updates)
  }

  const applyFloatRate = async () => {
    if (!ensureTargetRowsVisible()) return
    if (batchFloatRate == null) {
      message.warning(t('containers.messages.enterFloatRate'))
      return
    }
    const updates = buildContainerDetailFloatRateUpdates(targetRows, container, batchFloatRate)
    if (!updates.length) return
    await batchUpdateDetails(updates)
    applyDetailUpdatesToRows(updates)
    setBatchFloatRate(null)
    message.success(t('containers.messages.detailsUpdated', { count: updates.length }))
  }

  const handleRecalculateCosts = async () => {
    if (!access.canEditContainer) return
    if (!ensureTargetRowsVisible()) return
    if (container?.运费 == null) {
      message.warning('缺少运费，无法重算成本')
      return
    }
    if (!container?.总体积 || container.总体积 <= 0) {
      message.warning('缺少总体积，无法重算成本')
      return
    }
    if (!targetRows.length) {
      message.warning('没有可重算的明细')
      return
    }

    const updates = buildContainerDetailFloatRateUpdates(targetRows, container)
    if (!updates.length) {
      message.warning('没有可写回的成本更新')
      return
    }

    setRecalculateCostsLoading(true)
    try {
      // 手动入口只处理当前目标行，避免页面加载或头部保存时意外批量写库。
      await batchUpdateDetails(updates)
      applyDetailUpdatesToRows(updates)
      message.success(t('containers.messages.detailsUpdated', { count: updates.length }))
    } finally {
      setRecalculateCostsLoading(false)
    }
  }

  const applyPrices = async () => {
    if (batchImportPrice == null && batchOemPrice == null) {
      message.warning(t('containers.messages.enterImportOrOemPrice'))
      return
    }
    if (!selectedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    const updates = selectedRows
      .filter((row) => row.hguid)
      .map((row) => ({ hguid: row.hguid, 进口价格: batchImportPrice ?? row.进口价格, 贴牌价格: batchOemPrice ?? row.贴牌价格 }))
    await batchUpdateDetails(updates)
    setRows((items) =>
      items.map((item) => {
        const match = updates.find((update) => update.hguid === item.hguid)
        return match ? { ...item, 进口价格: match.进口价格, 贴牌价格: match.贴牌价格 } : item
      }),
    )
    setBatchImportPrice(null)
    setBatchOemPrice(null)
    setSelectedRowKeys([])
    message.success(t('containers.messages.detailPricesUpdated', { count: updates.length }))
  }

  const applyActive = async (isActive: boolean) => {
    if (!selectedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    const productCodes = selectedRows
      .map((row) => row.商品编码 || row.商品信息?.商品编码)
      .filter((value): value is string => Boolean(value))
    if (!productCodes.length) {
      message.warning(t('containers.messages.selectedProductsMissingCode'))
      return
    }
    const result = await bulkSetStatus(productCodes, isActive)
    if (result.success === false) {
      message.error(result.message || t('containers.messages.batchActiveFailed'))
      return
    }
    setRows((items) =>
      items.map((item) => (productCodes.includes(item.商品编码 || item.商品信息?.商品编码 || '') ? { ...item, warehouseIsActive: isActive } : item)),
    )
    setSelectedRowKeys([])
    message.success(t(isActive ? 'containers.messages.productsActivated' : 'containers.messages.productsDeactivated', { count: productCodes.length }))
  }

  const translateNames = async () => {
    if (!ensureTargetRowsVisible()) return
    const names = Array.from(new Set(targetRows.map(getContainerDetailProductName).filter((value): value is string => Boolean(value))))
    if (!names.length) {
      message.warning(t('containers.messages.noNamesToTranslate'))
      return
    }
    const translations = await batchTranslate(names)
    const updates = buildContainerDetailTranslationUpdates(targetRows, translations)
    if (updates.length) {
      await batchUpdateDetails(updates)
      setRows((items) => applyContainerDetailEnglishNameUpdates(items, updates))
    }
    message.success(t('containers.messages.namesTranslated', { count: updates.length }))
  }

  const showHqTranslationResult = (result: HqTranslationResult) => {
    const samples = Object.entries(result.Samples ?? {}).slice(0, 10)

    Modal.info({
      title: t('containers.modals.hqTranslationResultTitle'),
      width: 640,
      content: (
        <Space direction="vertical" size={8} style={{ width: '100%' }}>
          <Typography.Text>{t('containers.messages.hqTranslationResultSummary', {
            candidates: result.TotalCandidates ?? 0,
            translated: result.TotalTranslated ?? 0,
            skipped: result.TotalSkipped ?? 0,
            failed: result.TotalFailed ?? 0,
          })}</Typography.Text>
          {samples.length ? (
            <>
              <Typography.Text strong>{t('containers.text.hqTranslationSamples')}</Typography.Text>
              <Space direction="vertical" size={4} style={{ width: '100%' }}>
                {samples.map(([chinese, english]) => (
                  <Typography.Text key={chinese}>
                    {`${chinese} -> ${english}`}
                  </Typography.Text>
                ))}
              </Space>
            </>
          ) : null}
        </Space>
      ),
    })
  }

  const translateHqData = async () => {
    const containerNumber = container?.货柜编号?.trim()
    if (!containerNumber) {
      message.warning(t('containers.messages.missingContainerNumberForHqTranslation'))
      return
    }

    setHqTranslating(true)
    message.loading({
      content: t('containers.messages.hqTranslationInProgress'),
      key: 'hq-translation',
      duration: 0,
    })

    try {
      const result = await translateHqProductNamesByContainerNumber(containerNumber)
      message.success({
        content: t('containers.messages.hqTranslationCompleted'),
        key: 'hq-translation',
      })
      showHqTranslationResult(result)
      await loadData()
    } catch (error) {
      console.error(error)
      message.error({
        content: error instanceof Error ? error.message : t('containers.messages.hqTranslationFailed'),
        key: 'hq-translation',
      })
    } finally {
      setHqTranslating(false)
    }
  }

  const showPushToHqResult = (
    result: PushProductsToHqResult,
    selection: ReturnType<typeof buildContainerDetailHqPushSelection>,
    resultKind: 'success' | 'failed' = 'success',
  ) => {
    const errors = result.errors ?? []
    const detailStats = [
      { label: t('posAdmin.products.pushToHqAffectedRows', 'HQ影响记录'), value: result.affectedRowCount ?? 0 },
      { label: t('posAdmin.products.productsAdded', '商品新增'), value: result.productsAdded ?? 0 },
      { label: t('posAdmin.products.productsUpdated', '商品更新'), value: result.productsUpdated ?? 0 },
      { label: t('containers.text.warehouseInventoriesCreated', '仓库库存新增'), value: result.warehouseInventoriesCreated ?? 0 },
      { label: t('containers.text.warehouseInventoriesUpdated', '仓库库存更新'), value: result.warehouseInventoriesUpdated ?? 0 },
      { label: t('posAdmin.products.storeRetailPricesCreated', '门店零售价新增'), value: result.storeRetailPricesCreated ?? 0 },
      { label: t('posAdmin.products.storeRetailPricesUpdated', '门店零售价更新'), value: result.storeRetailPricesUpdated ?? 0 },
      { label: t('posAdmin.products.productSetCodesCreated', '套装编码新增'), value: result.productSetCodesCreated ?? 0 },
      { label: t('posAdmin.products.productSetCodesUpdated', '套装编码更新'), value: result.productSetCodesUpdated ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesCreated', '门店多码新增'), value: result.storeMultiCodesCreated ?? 0 },
      { label: t('posAdmin.products.storeMultiCodesUpdated', '门店多码更新'), value: result.storeMultiCodesUpdated ?? 0 },
      { label: t('containers.text.skippedNewProducts', '跳过新商品'), value: selection.skippedNewProductCount },
      { label: t('containers.text.missingProductCodeRows', '缺商品编码'), value: selection.missingProductCodeCount },
    ].filter((item) => item.value > 0)
    const content = (
      <Space direction="vertical" size={6}>
        {result.message ? <div>{result.message}</div> : null}
        <div>
          {t('posAdmin.products.pushToHqResult', '发送完成：商品成功 {{success}}，失败 {{failed}}，合计 {{total}}', {
            success: result.successCount ?? 0,
            failed: result.failedCount ?? 0,
            total: result.totalCount ?? (result.successCount ?? 0) + (result.failedCount ?? 0),
          })}
        </div>
        <div>{t('containers.text.pushToHqSelectedLocalProducts', '本次发送候选商品：{{count}} 个', { count: selection.items.length })}</div>
        {detailStats.map((item) => (
          <div key={item.label}>{item.label}: {item.value}</div>
        ))}
        {errors.length ? (
          <div style={{ whiteSpace: 'pre-wrap' }}>
            {t('posAdmin.products.partialSyncError', '部分同步错误')}：{errors.join('\n')}
          </div>
        ) : null}
      </Space>
    )

    if (resultKind === 'failed') {
      Modal.error({
        title: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
        content,
      })
      return
    }

    if (errors.length || (result.failedCount ?? 0) > 0) {
      Modal.warning({
        title: t('posAdmin.products.pushToHqPartialSucceeded', '发送到 HQ 部分成功'),
        content,
      })
      return
    }

    Modal.success({
      title: t('posAdmin.products.pushToHqSucceeded', '发送到 HQ 完成'),
      content,
    })
  }

  const handlePushSelectedProductsToHq = async () => {
    if (pushToHqLoadingRef.current) return
    if (!access.canManagePosProducts) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    if (!selectedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }

    const selection = buildContainerDetailHqPushSelection(selectedRows)
    if (!selection.items.length) {
      message.warning(t('containers.messages.noExistingLocalProductsToPushHq', '选中明细没有可发送到 HQ 的本地已有商品'))
      return
    }

    try {
      // 写 HQ 是跨库操作，使用即时锁防止连续点击造成重复提交。
      pushToHqLoadingRef.current = true
      setPushToHqLoading(true)
      const result = await pushProductsToHq({
        productCodes: selection.productCodes,
        items: selection.items,
      })
      showPushToHqResult(result, selection)
      setSelectedRowKeys([])
      await loadData()
    } catch (error) {
      const errorResult = extractPushToHqErrorResult(error)
      if (errorResult) {
        showPushToHqResult(errorResult, selection, 'failed')
      } else {
        message.error(error instanceof Error ? error.message : t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'))
      }
    } finally {
      pushToHqLoadingRef.current = false
      setPushToHqLoading(false)
    }
  }

  const renderCreateProductResultItems = (items: ContainerProductCreationResultItem[]) => {
    if (!items.length) return null

    return (
      <ul style={{ margin: '4px 0 0', paddingInlineStart: 20 }}>
        {items.slice(0, 10).map((item, index) => (
          <li key={`${item.detailHguid || item.productCode || item.itemNumber || index}`}>
            <Typography.Text>
              {[item.productCode, item.itemNumber, item.reasonCode, item.message].filter(Boolean).join(' / ')}
            </Typography.Text>
          </li>
        ))}
        {items.length > 10 ? (
          <li>
            <Typography.Text type="secondary">
              {t('containers.text.moreCreateProductResultItems', '还有 {{count}} 条未显示', { count: items.length - 10 })}
            </Typography.Text>
          </li>
        ) : null}
      </ul>
    )
  }

  const showCreateProductsJobResult = (job: ContainerProductCreationJob) => {
    const result = job.result
    const content = (
      <Space direction="vertical" size={8}>
        <Typography.Text>
          {t('containers.text.createProductsJobSummary', '创建 {{created}}，跳过 {{skipped}}，失败 {{failed}}', {
            created: result.createdCount,
            skipped: result.skippedCount,
            failed: result.failedCount,
          })}
        </Typography.Text>
        {job.message ? <Typography.Text type="secondary">{job.message}</Typography.Text> : null}
        {result.skipped.length ? (
          <div>
            <Typography.Text strong>{t('containers.text.skippedRows', '跳过明细')}</Typography.Text>
            {renderCreateProductResultItems(result.skipped)}
          </div>
        ) : null}
        {result.errors.length ? (
          <div>
            <Typography.Text strong>{t('containers.text.failedRows', '失败明细')}</Typography.Text>
            {renderCreateProductResultItems(result.errors)}
          </div>
        ) : null}
      </Space>
    )

    if (job.status === 'Failed') {
      Modal.error({
        title: t('containers.messages.createProductsJobFailed', '创建新商品失败'),
        content,
      })
      return
    }

    if (result.failedCount > 0 || result.errors.length > 0 || result.skippedCount > 0) {
      Modal.warning({
        title: t('containers.messages.createProductsJobPartialSucceeded', '创建新商品部分完成'),
        content,
      })
      return
    }

    Modal.success({
      title: t('containers.messages.createProductsJobSucceeded', '创建新商品完成'),
      content,
    })
  }

  const createNewProducts = async () => {
    if (createProductsLoadingRef.current) return
    if (!ensureTargetRowsVisible()) return
    if (!access.canEditContainer || !access.canManagePosProducts) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    const detailHguids = targetRows.map((row) => row.hguid).filter((value): value is string => Boolean(value))
    if (!detailHguids.length) {
      message.warning(t('containers.messages.noEligibleNewProducts'))
      return
    }
    const operationId = buildContainerCreateProductsOperationId(containerGuid, detailHguids)

    try {
      // 创建新商品是跨库后台任务，使用即时锁配合 operationId 防止重复提交。
      createProductsLoadingRef.current = true
      setCreateProductsLoading(true)
      const job = await createContainerProductCreationJob({
        operationId,
        containerGuid,
        detailHguids,
      })
      message.info(t('containers.messages.createProductsJobSubmitted', '创建新商品任务已提交，正在后台处理'))
      const finalJob = job.status === 'Queued' || job.status === 'Running'
        ? await waitForContainerProductCreationJob(job.jobId)
        : job
      showCreateProductsJobResult(finalJob)
      setSelectedRowKeys([])
      await loadData()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.createProductFailed', '创建新商品失败'))
    } finally {
      createProductsLoadingRef.current = false
      setCreateProductsLoading(false)
    }
  }

  const updateExistingPurchase = async () => {
    if (!ensureTargetRowsVisible()) return
    const candidates = targetRows.filter((row) => !row.是否新商品)
    if (!candidates.length) {
      message.info(t('containers.messages.noExistingProductsToUpdate'))
      return
    }
    const detection = await detectProducts(candidates.map((row) => ({
      ProductCode: row.商品编码 || row.商品信息?.商品编码,
      ItemNumber: row.商品信息?.货号,
      Barcode: row.商品信息?.条形码,
    })))
    const importMap = new Map<string, number | undefined>()
    detection.forEach((item) => {
      const code = item.ProductCode || item.productCode
      if (code) importMap.set(code, item.WarehouseImportPrice ?? item.warehouseImportPrice ?? item.importPrice)
    })
    const updates = candidates.filter((row) => {
      const code = row.商品编码 || row.商品信息?.商品编码 || ''
      const current = importMap.get(code)
      return code && (row.进口价格 ?? 0) > 0 && (current == null || Math.abs(current - (row.进口价格 ?? 0)) > 0.000001)
    })
    if (!updates.length) {
      message.info(t('containers.messages.noPurchasePriceDiff'))
      return
    }
    try {
      await batchUpdateWarehouseProducts(updates.map((row) => ({
        ProductCode: row.商品编码 || row.商品信息?.商品编码,
        ImportPrice: row.进口价格,
        IsActive: true,
      })))
      await upsertRetailForActiveStores(updates.map((row) => ({
        ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
        PurchasePrice: row.进口价格,
        IsActive: true,
      })))
      await upsertMultiCodeForActiveStores(updates.map((row) => ({
        ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
        PurchasePrice: row.进口价格,
        IsActive: true,
      })))
      message.success(t('containers.messages.purchasePricesUpdated', { count: updates.length }))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '更新已有商品进货价失败'))
    }
  }

  const deleteSelected = () => {
    if (!selectedRowKeys.length) {
      message.warning(t('containers.messages.selectDetails'))
      return
    }
    Modal.confirm({
      title: t('containers.modals.deleteDetailsTitle'),
      content: t('containers.modals.deleteDetailsContent', { count: selectedRowKeys.length }),
      okText: t('containers.actions.confirmDelete'),
      okButtonProps: { danger: true },
      onOk: async () => {
        const hguids = selectedRows.map((row) => row.hguid).filter((value): value is string => Boolean(value))
        await batchDeleteDetails(hguids)
        setRows((items) => items.filter((item) => !hguids.includes(item.hguid)))
        setSelectedRowKeys([])
        message.success(t('containers.messages.detailsDeleted', { count: hguids.length }))
      },
    })
  }

  const exportExcel = async () => {
    if (!ensureTargetRowsVisible()) return
    const exportRows = targetRows
    if (!exportRows.length) {
      message.warning(t('containers.messages.noDataToExport'))
      return
    }
    setExporting(true)
    try {
      const items: ContainerDetailExportItem[] = exportRows.map((row) => ({
        imageUrl: row.商品信息?.商品图片,
        itemNumber: row.商品信息?.货号 || '',
        barcode: row.商品信息?.条形码 || '',
        productName: getContainerDetailProductName(row) || '',
        isNewProduct: Boolean(row.是否新商品),
        containerQuantity: row.装柜数量 || 0,
        domesticPrice: row.国内价格 || 0,
        transportCost: row.运输成本 || 0,
        importPrice: row.进口价格 || 0,
        oemPrice: row.贴牌价格 || 0,
      }))
      await exportContainerDetailsToExcel(items, {
        fileName: `${container?.货柜编号 || t('containers.detailTitle')}_${t('containers.export.detailSuffix')}`,
        onProgress: (progress) => setExportProgress(progress),
      })
      message.success(t('containers.messages.detailsExported', { count: items.length }))
    } finally {
      setExporting(false)
      setExportProgress(0)
    }
  }

  const columns: ColumnsType<ContainerDetail> = [
    { title: renderCompactHeader(t('containers.columns.index')), width: 56, fixed: 'left', render: (_v, _r, index) => renderNumericCell(index + 1) },
    {
      title: renderCompactHeader(t('containers.columns.image')),
      width: 64,
      fixed: 'left',
      render: (_, row) =>
        row.商品信息?.商品图片 ? (
          <Image width={40} height={40} src={row.商品信息.商品图片} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <span style={{ color: '#999' }}>{t('containers.empty.noImage')}</span>
        ),
    },
    { title: t('containers.fields.itemNumber'), width: 130, fixed: 'left', sorter: (a, b) => (a.商品信息?.货号 || '').localeCompare(b.商品信息?.货号 || ''), render: (_, row) => <CopyableText value={row.商品信息?.货号} maxWidth={90} /> },
    {
      title: renderCompactHeader(t('containers.fields.barcode')),
      width: 170,
      render: (_, row) => {
        const barcode = row.商品信息?.条形码

        return barcode ? (
          <Space size={4} wrap={false} className="container-detail-barcode-cell">
            <BarcodePreview value={barcode} showText showCopy={false} options={{ height: 24 }} />
            <Tooltip title="复制">
              <Button
                size="small"
                type="text"
                aria-label={`复制 ${barcode}`}
                icon={<CopyOutlined />}
                className="container-detail-icon-button"
                onClick={(event) => {
                  event.stopPropagation()
                  void copyTextToClipboard(barcode)
                }}
              />
            </Tooltip>
          </Space>
        ) : '--'
      },
    },
    { title: renderCompactHeader(t('containers.fields.productName')), width: 180, render: (_, row) => <TwoLineText value={getContainerDetailProductName(row)} /> },
    {
      title: renderCompactHeader(t('containers.fields.englishName')),
      width: 180,
      render: (_, row) => access.canEditContainer ? (
        <Input.TextArea
          className="container-detail-english-name-input"
          value={getContainerDetailEnglishName(row) ?? ''}
          autoSize={{ minRows: 1, maxRows: 2 }}
          style={{ resize: 'none' }}
          onChange={(event) => patchRow(rowKey(row), { 英文名称: event.target.value })}
          onBlur={(event) => void saveRowPatch(row, { 英文名称: event.target.value })}
        />
      ) : <TwoLineText value={getContainerDetailEnglishName(row)} />,
    },
    { title: renderCompactHeader(t('containers.fields.productType')), width: 92, render: (_, row) => getProductTypeLabel(row.商品类型 || row.商品信息?.商品类型, t) },
    { title: renderCompactHeader(t('containers.fields.newProduct')), width: 72, render: (_, row) => (row.是否新商品 ? <Tag color="blue">{t('containers.tags.new')}</Tag> : <Tag>{t('containers.tags.existing')}</Tag>) },
    { title: renderCompactHeader(t('containers.fields.containerPieces')), dataIndex: '装柜件数', width: 76, align: 'right', render: (v) => renderNumericCell(v ?? '--') },
    { title: renderCompactHeader(t('containers.fields.containerQuantity')), dataIndex: '装柜数量', width: 76, align: 'right', render: (v) => renderNumericCell(v ?? '--') },
    { title: renderCompactHeader(t('containers.fields.domesticPrice')), dataIndex: '国内价格', width: 86, align: 'right', render: (v) => renderNumericCell(formatNumber(v)) },
    {
      title: renderCompactHeader(t('containers.fields.floatRate')),
      dataIndex: '调整浮率',
      width: 96,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            value={row.调整浮率}
            precision={4}
            style={{ width: 78 }}
            onChange={(value) => patchRow(rowKey(row), { 调整浮率: value == null ? undefined : Number(value) })}
            onBlur={(event) => {
              const value = event.target.value ? Number(event.target.value) : undefined
              void saveFloatRatePatch(row, value)
            }}
          />
        ) : renderNumericCell(formatNumber(row.调整浮率, 4)),
    },
    { title: renderCompactHeader(t('containers.fields.transportCost')), dataIndex: '运输成本', width: 86, align: 'right', render: (v) => renderNumericCell(formatNumber(v)) },
    {
      title: renderCompactHeader(t('containers.fields.importPrice')),
      dataIndex: '进口价格',
      width: 96,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            value={row.进口价格}
            min={0}
            precision={2}
            style={{ width: 78 }}
            onChange={(value) => patchRow(rowKey(row), { 进口价格: value == null ? undefined : Number(value) })}
            onBlur={(event) => void saveRowPatch(row, { 进口价格: event.target.value ? Number(event.target.value) : undefined })}
          />
        ) : renderNumericCell(formatNumber(row.进口价格)),
    },
    {
      title: renderCompactHeader(t('containers.fields.oemPrice')),
      dataIndex: '贴牌价格',
      width: 96,
      align: 'right',
      render: (_value, row) =>
        access.canEditContainer ? <InputNumber defaultValue={row.贴牌价格} min={0} precision={2} style={{ width: 78 }} onBlur={(event) => void saveRowPatch(row, { 贴牌价格: event.target.value ? Number(event.target.value) : undefined })} /> : renderNumericCell(formatNumber(row.贴牌价格)),
    },
    { title: renderCompactHeader(t('containers.fields.warehouseStatus')), width: 86, render: (_, row) => <Tag color={row.warehouseIsActive ? 'success' : 'default'}>{row.warehouseIsActive ? t('common.activeUpper') : t('common.inactiveUpper')}</Tag> },
    {
      title: renderCompactHeader(t('containers.fields.remark')),
      width: 160,
      render: (_, row) =>
        access.canEditContainer ? <Input defaultValue={row.备注} onBlur={(event) => void saveRowPatch(row, { 备注: event.target.value })} /> : row.备注 || '--',
    },
  ]

  return (
    <PageContainer title={container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : t('menu.containerDetail')}>
      <Spin spinning={loading}>
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {!containerGuid ? <Alert type="warning" showIcon message={t('containers.messages.missingContainerGuid')} /> : null}
          <Card>
            <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
              <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/warehouse/containers')}>{t('containers.actions.backToList')}</Button>
              <Space wrap>
                <Button icon={<ReloadOutlined />} onClick={() => void loadData()}>{t('common.refresh')}</Button>
                {access.canEditContainer ? (
                  headerEditing ? (
                    <Button type="primary" icon={<SaveOutlined />} loading={savingHeader} onClick={() => void saveHeader()}>{t('containers.actions.saveContainer')}</Button>
                  ) : (
                    <Button icon={<EditOutlined />} onClick={() => setHeaderEditing(true)}>{t('containers.actions.editContainer')}</Button>
                  )
                ) : null}
              </Space>
            </Space>
            <Descriptions bordered size="small" column={4} style={{ marginTop: 16 }}>
              <Descriptions.Item label={t('containers.fields.containerNumber')}>{container?.货柜编号 || '--'}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.loadingDate')}>{formatDate(container?.装柜日期)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.estimatedArrival')}>{formatDate(container?.预计到岸日期)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.status')}>{getStatusTag(container?.状态, t)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.actualArrival')}>
                {headerEditing ? <DatePicker value={headerForm.实际到货日期} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 实际到货日期: value }))} /> : formatDate(container?.实际到货日期)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.exchangeRate')}>
                {headerEditing ? <InputNumber value={headerForm.汇率} precision={4} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 汇率: value ?? undefined }))} /> : formatNumber(container?.汇率, 4)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.freight')}>
                {headerEditing ? <InputNumber value={headerForm.运费} precision={2} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 运费: value ?? undefined }))} /> : formatNumber(container?.运费)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.totalVolume')}>{formatNumber(container?.总体积, 4)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.remark')} span={4}>
                {headerEditing ? <Input.TextArea value={headerForm.备注} rows={2} onChange={(event) => setHeaderForm((prev) => ({ ...prev, 备注: event.target.value }))} /> : container?.备注 || '--'}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card>
            <Space direction="vertical" size={12} style={{ width: '100%' }}>
              <Space wrap style={{ justifyContent: 'space-between', width: '100%' }}>
                <Space wrap>
                  <Input value={itemNumberFilter} allowClear prefix={<SearchOutlined />} placeholder={t('containers.placeholders.filterItemNumber')} style={{ width: 180 }} onChange={(event) => setItemNumberFilter(event.target.value)} />
                  <Select
                    value={productTypeFilter}
                    style={{ width: 140 }}
                    onChange={setProductTypeFilter}
                    options={(['all', 'normal', 'set', 'setChild'] as ProductTypeFilter[]).map((value) => ({ value, label: getProductTypeFilterLabel(value, t) }))}
                  />
                  <Select
                    mode="multiple"
                    value={selectedTagFilters}
                    allowClear
                    maxTagCount="responsive"
                    placeholder={t('containers.filters.allTags')}
                    style={{ width: 220 }}
                    onChange={setTagFiltersFromSelect}
                    options={tagSelectOptions}
                  />
                  <Typography.Text type="secondary">{t('containers.text.showingRows', { filtered: filteredRows.length, total: rows.length })}</Typography.Text>
                </Space>
                <Space wrap>
                  <Button icon={<DownloadOutlined />} loading={exporting} onClick={() => void exportExcel()}>{t('common.export')}</Button>
                  {access.canEditContainer ? (
                    <Button loading={hqTranslating} onClick={() => void translateHqData()}>
                      {t('containers.actions.translateHqData')}
                    </Button>
                  ) : null}
                  {access.canManagePosProducts ? (
                    <Tooltip title={!selectedRowKeys.length ? t('containers.messages.selectProducts') : ''}>
                      <Button
                        icon={<CloudUploadOutlined />}
                        loading={pushToHqLoading}
                        disabled={!selectedRowKeys.length || pushToHqLoading}
                        onClick={() => void handlePushSelectedProductsToHq()}
                      >
                        {t('containers.actions.pushToHq', '发送到 HQ')}
                      </Button>
                    </Tooltip>
                  ) : null}
                  {access.canEditContainer ? (
                    <Dropdown
                      menu={{
                        items: [
                          { key: 'translate', label: t('containers.actions.batchTranslate') },
                          ...(canCreateContainerProducts
                            ? [{ key: 'createNew', label: t('containers.actions.createNewProducts'), disabled: createProductsLoading }]
                            : []),
                          { key: 'updatePurchase', label: t('containers.actions.updateExistingPurchase') },
                          { key: 'active', label: t('containers.actions.batchActivate') },
                          { key: 'inactive', label: t('containers.actions.batchDeactivate') },
                        ],
                        onClick: ({ key }) => {
                          if (key === 'translate') void translateNames()
                          if (key === 'createNew') void createNewProducts()
                          if (key === 'updatePurchase') void updateExistingPurchase()
                          if (key === 'active') void applyActive(true)
                          if (key === 'inactive') void applyActive(false)
                        },
                      }}
                    >
                      <Button>{t('containers.actions.batchActions')}</Button>
                    </Dropdown>
                  ) : null}
                  {access.canDeleteContainer ? <Button danger icon={<DeleteOutlined />} onClick={deleteSelected}>{t('containers.actions.deleteDetails')}</Button> : null}
                </Space>
              </Space>

              {access.canEditContainer ? (
                <Space wrap>
                  <InputNumber value={batchFloatRate} placeholder={t('containers.fields.floatRate')} precision={4} onChange={setBatchFloatRate} />
                  <Button onClick={() => void applyFloatRate()}>{t('containers.actions.applyFloatRate')}</Button>
                  <Button loading={recalculateCostsLoading} onClick={() => void handleRecalculateCosts()}>重算成本</Button>
                  <InputNumber value={batchImportPrice} placeholder={t('containers.fields.importPrice')} min={0} precision={2} onChange={setBatchImportPrice} />
                  <InputNumber value={batchOemPrice} placeholder={t('containers.fields.oemPrice')} min={0} precision={2} onChange={setBatchOemPrice} />
                  <Button onClick={() => void applyPrices()}>{t('containers.actions.applyPrices')}</Button>
                  <Switch checkedChildren={t('containers.text.selectedFirst')} unCheckedChildren={t('containers.text.allDisplayed')} checked={selectedRowKeys.length > 0} disabled />
                </Space>
              ) : null}

              <Space className="container-detail-stats" wrap size={[8, 8]}>
                {tagStatOptions.map((option) => {
                  const active = option.value === 'all' ? !selectedTagFilters.length : selectedTagFilters.includes(option.value)
                  return (
                    <Tag
                      key={option.value}
                      className={`container-detail-stat-tag ${active ? 'container-detail-stat-tag-active' : 'container-detail-stat-tag-muted'}`}
                      color={option.color}
                      role="button"
                      tabIndex={0}
                      aria-pressed={active}
                      onClick={() => toggleTagFilter(option.value)}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault()
                          toggleTagFilter(option.value)
                        }
                      }}
                    >
                      <span>{option.label}</span>
                      <Typography.Text strong className="container-detail-stat-count">
                        {tagStats[option.value]}
                      </Typography.Text>
                    </Tag>
                  )
                })}
              </Space>

              {selectedTagOptions.length ? (
                <Space className="container-detail-selected-filters" wrap size={[6, 6]}>
                  <Typography.Text type="secondary">{t('containers.text.selectedFilters')}</Typography.Text>
                  {selectedTagOptions.map((option) => (
                    <Tag
                      key={option.value}
                      color={option.color}
                      closable
                      onClose={(event) => {
                        event.preventDefault()
                        toggleTagFilter(option.value)
                      }}
                    >
                      {option.label}
                    </Tag>
                  ))}
                  <Button type="link" size="small" onClick={() => setSelectedTagFilters([])}>
                    {t('containers.actions.clearFilters')}
                  </Button>
                </Space>
              ) : null}

              {exporting ? <Progress percent={exportProgress} size="small" /> : null}

              <Table
                className="container-detail-table"
                rowKey={rowKey}
                size="small"
                columns={columns}
                dataSource={filteredRows}
                rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys, fixed: true }}
                pagination={{ pageSize: 50, showSizeChanger: true, showTotal: (total) => t('common.total', { count: total }) }}
                scroll={{ x: 1840, y: 620 }}
                footer={() => (
                  <Space direction="vertical" size={2}>
                    <Typography.Text type="secondary">{t('containers.formulas.transportCost', '运输成本 = 运费 × 明细体积 ÷ 装柜数量 ÷ 总体积')}</Typography.Text>
                    <Typography.Text type="secondary">{t('containers.formulas.importPrice', '进口价格 = ((国内价格 ÷ 汇率 + 运输成本) × 调整浮率 × 10) ÷ 11')}</Typography.Text>
                  </Space>
                )}
              />
            </Space>
          </Card>
        </Space>
      </Spin>
    </PageContainer>
  )
}
