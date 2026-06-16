import {
  ArrowLeftOutlined,
  ArrowDownOutlined,
  ArrowUpOutlined,
  AppstoreOutlined,
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
  DndContext,
  PointerSensor,
  closestCenter,
  type DragEndEvent,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import {
  SortableContext,
  horizontalListSortingStrategy,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  Alert,
  Button,
  Card,
  Checkbox,
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
  notification,
} from 'antd'
import type { ColumnsType, TableRef } from 'antd/es/table'
import type { FilterDropdownProps, SorterResult } from 'antd/es/table/interface'
import type { TFunction } from 'i18next'
import type { Dayjs } from 'dayjs'
import dayjs from 'dayjs'
import { useKeepAliveContext } from 'keepalive-for-react'
import { useEffect, useMemo, useRef, useState, type CSSProperties, type HTMLAttributes, type Key, type KeyboardEvent, type ReactNode, type UIEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import {
  applyContainerFloatRateByScope,
  applyContainerPricesByScope,
  batchDeleteDetails,
  batchUpdateDetails,
  getContainerDomesticSetCodes,
  getContainerDetail,
  queryContainerProducts,
  recalculateContainerCostsByScope,
  translateHqProductNamesByContainerNumber,
  updateContainerDomesticSetCodePrices,
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
import {
  buildPushProductsToHqOperationId,
  createPushProductsToHqJob,
  getPushProductsToHqJob,
  type PushProductsToHqJobResult,
} from '../../../services/posProductService'
import { createHqSyncJobPoller } from '../../../services/productHqSyncPolling'
import { upsertForActiveStores as upsertMultiCodeForActiveStores } from '../../../services/storeMultiCodePriceService'
import { upsertForActiveStores as upsertRetailForActiveStores } from '../../../services/storeRetailPriceService'
import { batchTranslate } from '../../../services/translationService'
import {
  batchAssignProducts,
  getCategoryTree,
  type WarehouseCategoryNode,
} from '../../../services/warehouseCategoryService'
import {
  batchUpdateWarehouseProducts,
  bulkSetStatus,
  detectProducts,
} from '../../../services/warehouseProductService'
import { useAuthStore } from '../../../store/auth'
import type { ContainerDetail, ContainerDetailBatchScope, ContainerDomesticSetCodeItem, ContainerMain, HqTranslationResult, UpdateContainerDetailRequest, UpdateContainerRequest } from '../../../types/container'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'
import {
  applyContainerDetailEnglishNameUpdates,
  applyContainerDetailCategoryFilter,
  applyContainerDetailColumnState,
  applyContainerDetailLoadedTextFilters,
  applyContainerDetailWarehouseStatusByProductCodes,
  buildContainerDetailQuery,
  buildContainerDetailClearEnglishNameUpdates,
  buildContainerDetailDetectionItems,
  buildContainerDetailEnglishNameUpdates,
  buildContainerDetailExportRows,
  buildContainerDetailMatchedDomesticDataUpdates,
  buildContainerDetailMatchStatusUpdates,
  buildContainerDetailSaveFailureKeys,
  buildContainerDetailTagStats,
  buildContainerDetailFloatRateUpdates,
  buildContainerDetailHqPushSelection,
  canUseContainerDetailLocalTagFilters,
  getContainerDetailBatchCategoryProductCodes,
  buildContainerDetailTranslationUpdates,
  calculateContainerSetCodePurchasePrice,
  countContainerDetailInvalidTranslationResults,
  extractPushToHqErrorResult,
  findContainerDetailRowsMissingCreateProductRetailPrice,
  findContainerDetailRowsMissingProductName,
  getContainerDetailExportColumns,
  getContainerDetailBarcode,
  getContainerDetailCategoryName,
  getContainerDetailCategoryTooltipRecord,
  getContainerDetailEnglishName,
  getContainerDetailImageUrl,
  getContainerDetailItemNumber,
  getContainerDetailMatchType,
  getContainerDetailProductCode,
  getContainerDetailProductName,
  getContainerDetailProductType,
  getContainerDetailTranslationSource,
  getContainerDetailOemPriceSource,
  getContainerDetailWarehouseActionFailureMessage,
  getContainerDetailWarehouseStatusFilterKey,
  getNextContainerDetailEditableCell,
  isContainerDetailColumnOrderCustomized,
  isContainerDetailSortField,
  mergeContainerDetailColumnOrder,
  mergeContainerDetailLoadedItems,
  moveContainerDetailColumnOrder,
  mergeContainerDetailPatch,
  matchesContainerDetailSelectedTags,
  omitContainerDetailTextFilters,
  resolveContainerDetailOemPrice,
  rollbackContainerDetailWarehouseStatuses,
  CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY,
  CONTAINER_DETAIL_EXPORT_COLUMNS,
  CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY,
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  type ContainerDetailEditableCellDirection,
  type ContainerDetailColumnFilters,
  type ContainerDetailExportColumnKey,
  type ContainerDetailMatchTypeFilter,
  type ContainerDetailNewProductFilter,
  type ContainerDetailNumberRangeFilter,
  type ContainerDetailProductTypeFilter,
  type ContainerDetailSortField,
  type ContainerDetailSortState,
  type ContainerDetailTableColumnKey,
  type ContainerDetailTagFilter,
  type ContainerDetailTagStats,
  type ContainerDetailWarehouseStatusFilter,
} from './containerDetailLogic'
import { buildWarehouseCategoryLookup, formatWarehouseCategoryNodeName, getWarehouseProductCategoryTooltip, type WarehouseCategoryLookup } from '../Products/categoryPath'
import CategoryTreePicker from '../Products/CategoryTreePicker'
import type { PushProductsToHqResult } from '../../../types/posProduct'
import './index.css'

type TextColumnFilterKey = 'itemNumber' | 'barcode' | 'productName' | 'englishName' | 'remark'
type NumberColumnFilterKey = 'containerPieces' | 'middlePackQuantity' | 'containerQuantity' | 'domesticPrice' | 'floatRate' | 'transportCost' | 'warehouseImportPrice' | 'importPrice' | 'oemPrice'
type EnumColumnFilterKey = 'productTypes' | 'newProductStates' | 'matchTypes' | 'warehouseStatus'
type ContainerSetCodePriceEdits = Record<string, { retailPrice?: number | null; purchasePrice?: number | null }>

function formatDate(value?: string) {
  return value ? dayjs(value).format('YYYY-MM-DD') : '--'
}

function formatNumber(value?: number, digits = 2) {
  return value == null ? '--' : value.toLocaleString('zh-CN', { maximumFractionDigits: digits, minimumFractionDigits: digits })
}

function formatCurrency(value?: number, symbol = '$', digits = 2) {
  const formatted = formatNumber(value, digits)
  return formatted === '--' ? formatted : `${symbol}${formatted}`
}

function rowKey(row: ContainerDetail) {
  return row.hguid || String(row.id)
}

const containerStatusOptions = [
  { value: 0, color: 'blue', labelKey: 'loaded' },
  { value: 1, color: 'orange', labelKey: 'inTransit' },
  { value: 2, color: 'success', labelKey: 'completed' },
  { value: 7, color: 'error', labelKey: 'cancelled' },
] as const

function getStatusTag(status: number | undefined, t: TFunction) {
  if (status == null) return <Tag>{t('containers.status.unknown')}</Tag>
  const item = containerStatusOptions.find((option) => option.value === status)
  return item ? <Tag color={item.color}>{t(`containers.status.${item.labelKey}`)}</Tag> : <Tag>{t('containers.status.unknownWithCode', { status })}</Tag>
}

function getContainerStatusText(status: number | undefined, t: TFunction) {
  if (status == null) return t('containers.status.unknown')
  const item = containerStatusOptions.find((option) => option.value === status)
  return item ? t(`containers.status.${item.labelKey}`) : t('containers.status.unknownWithCode', { status })
}

function getProductTypeLabel(value: string | undefined, t: TFunction) {
  const type = value || '普通商品'
  const map: Record<string, string> = {
    全部: 'common.all',
    普通商品: 'containers.productTypes.normal',
    套装商品: 'containers.productTypes.set',
    套装子商品: 'containers.productTypes.setChild',
    多码商品: 'containers.productTypes.multiCode',
  }
  return map[type] ? t(map[type]) : type
}

function getProductTypeTagColor(value: string | undefined) {
  if (value === '套装商品') return 'blue'
  if (value === '多码商品') return 'purple'
  if (value === '套装子商品') return 'orange'
  return 'default'
}

function getSetCodeRowKey(item: ContainerDomesticSetCodeItem) {
  return item.setProductCode || item.barcode || item.setItemNumber || ''
}

function getProductTypeFilterLabel(value: ContainerDetailProductTypeFilter, t: TFunction) {
  const map: Record<ContainerDetailProductTypeFilter, string> = {
    normal: 'containers.productTypes.normal',
    set: 'containers.productTypes.set',
    multi: 'containers.productTypes.multiCode',
    setChild: 'containers.productTypes.setChild',
  }
  return t(map[value])
}

function getMatchTypeLabel(value: ContainerDetailMatchTypeFilter, t: TFunction) {
  const map: Record<ContainerDetailMatchTypeFilter, string> = {
    productCode: 'containers.matchTypes.productCode',
    supplierItem: 'containers.matchTypes.supplierItem',
    unmatched: 'containers.matchTypes.unmatched',
  }
  return t(map[value])
}

function getMatchTypeTagColor(value: ContainerDetailMatchTypeFilter) {
  if (value === 'productCode') return 'green'
  if (value === 'supplierItem') return 'gold'
  return 'red'
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

function getOemPriceSourceClassName(row: ContainerDetail) {
  const source = getContainerDetailOemPriceSource(row)
  return [
    source === 'warehouse' ? 'container-detail-oem-price-cell-warehouse' : '',
    source === 'detail' ? 'container-detail-oem-price-cell-fallback' : '',
  ].filter(Boolean).join(' ')
}

function renderOemPriceCell(row: ContainerDetail) {
  const className = [
    'container-detail-nowrap',
    'container-detail-numeric-cell',
    'container-detail-oem-price-cell',
    getOemPriceSourceClassName(row),
  ].filter(Boolean).join(' ')

  // 颜色用于区分贴牌价来源：绿色为仓库商品贴牌价，黄色为货柜明细兜底价。
  return <span className={className}>{formatCurrency(resolveContainerDetailOemPrice(row), '$')}</span>
}

function getImportPriceTrend(row: ContainerDetail): 'up' | 'down' | undefined {
  const warehouseImportPrice = row.warehouseImportPrice
  const importPrice = row.进口价格
  if (
    typeof warehouseImportPrice !== 'number' ||
    typeof importPrice !== 'number' ||
    !Number.isFinite(warehouseImportPrice) ||
    !Number.isFinite(importPrice) ||
    warehouseImportPrice === importPrice
  ) {
    return undefined
  }
  return warehouseImportPrice > importPrice ? 'up' : 'down'
}

function renderImportPriceTrend(row: ContainerDetail) {
  const trend = getImportPriceTrend(row)
  if (!trend) return null
  const className = trend === 'up' ? 'container-detail-import-price-trend-up' : 'container-detail-import-price-trend-down'
  const Icon = trend === 'up' ? ArrowUpOutlined : ArrowDownOutlined
  // 趋势箭头只对比仓库当前进货价和本行进口价，不参与筛选、排序或保存。
  return <Icon className={className} />
}

function renderImportPriceCell(row: ContainerDetail, input?: ReactNode) {
  return (
    <Space size={4} wrap={false} className="container-detail-import-price-cell">
      {input ?? renderNumericCell(formatCurrency(row.进口价格, '$'))}
      {renderImportPriceTrend(row)}
    </Space>
  )
}

function collectCategoryExpandedKeys(nodes: WarehouseCategoryNode[], maxLevel: number, level = 1): string[] {
  return nodes.flatMap((node) => [
    level <= maxLevel ? node.categoryGUID : '',
    ...collectCategoryExpandedKeys(node.children || [], maxLevel, level + 1),
  ]).filter(Boolean)
}

function findWarehouseCategory(nodes: WarehouseCategoryNode[], targetGuid?: string): WarehouseCategoryNode | undefined {
  if (!targetGuid) return undefined
  for (const node of nodes) {
    if (node.categoryGUID === targetGuid) return node
    const matched = findWarehouseCategory(node.children || [], targetGuid)
    if (matched) return matched
  }
  return undefined
}

function buildContainerDetailCategoryOptions(
  nodes: WarehouseCategoryNode[],
  t: ReturnType<typeof useTranslation>['t'],
  language?: string,
  level = 0,
): Array<{ value: string; label: string }> {
  if (level === 0) {
    return [
      { value: CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY, label: t('containers.filters.allCategories', '全部分类') },
      { value: CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY, label: t('containers.filters.uncategorized', '未分类') },
      ...buildContainerDetailCategoryOptions(nodes, t, language, level + 1),
    ]
  }

  return nodes.flatMap((node) => [
    {
      value: node.categoryGUID,
      label: `${level > 1 ? `${'--'.repeat(level - 1)} ` : ''}${formatWarehouseCategoryNodeName(node, language)}`,
    },
    ...buildContainerDetailCategoryOptions(node.children || [], t, language, level + 1),
  ])
}

function renderContainerDetailCategoryCell(record: ContainerDetail, categoryLookup: WarehouseCategoryLookup, language?: string) {
  const displayName = getContainerDetailCategoryName(record) || '--'
  const tooltipTitle = getWarehouseProductCategoryTooltip(getContainerDetailCategoryTooltipRecord(record), categoryLookup, language)

  return (
    <Tooltip title={tooltipTitle || displayName}>
      <span className="container-detail-two-line-text">{displayName}</span>
    </Tooltip>
  )
}

function renderCompactHeader(value: ReactNode) {
  return <span className="container-detail-header-title">{value}</span>
}

function renderColumnTitle(key: ContainerDetailSortField, value: ReactNode) {
  return <span data-column-key={key} className="container-detail-header-title">{value}</span>
}

const CONTAINER_DETAIL_TABLE_SCROLL_X = 2040
const CONTAINER_DETAIL_TABLE_SCROLL_Y = 620
const CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH = 56
const CONTAINER_DETAIL_PAGE_SIZE = 50
const CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.containerDetail.columnOrder.v2'
const DEFAULT_CONTAINER_DETAIL_SORT: ContainerDetailSortState = { field: 'itemNumber', order: 'ascend' }
const CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS = ['englishName', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const
const EMPTY_CONTAINER_DETAIL_TAG_STATS = {
  all: 0,
  new: 0,
  existing: 0,
  noOemPrice: 0,
  abnormalImport: 0,
  active: 0,
  inactive: 0,
  normal: 0,
  set: 0,
  multi: 0,
  setChild: 0,
}

type ContainerDetailEditableColumnKey = typeof CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS[number]
type ContainerDetailFocusableCell = {
  focus: (options?: FocusOptions) => void
  select?: () => void
}

const containerDetailEditableDirectionByKey: Partial<Record<string, ContainerDetailEditableCellDirection>> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowLeft: 'left',
  ArrowRight: 'right',
}

function buildContainerDetailEditableCellKey(rowKeyValue: string, columnKey: ContainerDetailEditableColumnKey) {
  return `${rowKeyValue}:${columnKey}`
}

function getContainerDetailViewport() {
  if (typeof window === 'undefined') {
    return {
      height: CONTAINER_DETAIL_TABLE_SCROLL_Y,
      isSmallLandscape: false,
      isSmallPortrait: false,
    }
  }

  return {
    height: window.innerHeight,
    isSmallLandscape: window.matchMedia('(max-height: 500px) and (orientation: landscape)').matches,
    isSmallPortrait: window.matchMedia('(max-width: 767px) and (orientation: portrait)').matches,
  }
}

function useContainerDetailViewport() {
  const [viewport, setViewport] = useState(getContainerDetailViewport)

  useEffect(() => {
    const updateViewport = () => setViewport(getContainerDetailViewport())

    window.addEventListener('resize', updateViewport)
    window.addEventListener('orientationchange', updateViewport)
    return () => {
      window.removeEventListener('resize', updateViewport)
      window.removeEventListener('orientationchange', updateViewport)
    }
  }, [])

  return viewport
}

interface DraggableHeaderCellProps extends HTMLAttributes<HTMLTableCellElement> {
  'data-column-key'?: string
}

function DraggableHeaderCell({ children, style, ...props }: DraggableHeaderCellProps) {
  const columnKey = props['data-column-key']
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: columnKey ?? '__container-detail-static-column__',
    disabled: !columnKey,
  })

  if (!columnKey) {
    return <th style={style} {...props}>{children}</th>
  }

  const headerStyle: CSSProperties = {
    ...style,
    transform: CSS.Translate.toString(transform),
    transition,
    cursor: 'move',
    zIndex: isDragging ? 3 : style?.zIndex,
    opacity: isDragging ? 0.85 : style?.opacity,
  }

  return (
    <th ref={setNodeRef} style={headerStyle} {...props} {...attributes} {...listeners}>
      <div className="container-detail-draggable-header">
        {children}
      </div>
    </th>
  )
}

export default function ContainerDetailPage() {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const route = useStableRouteContext()
  const { active } = useKeepAliveContext()
  // 移动端布局可能复用 route element，货柜 GUID 必须每次跟随当前 URL。
  const containerGuid = route?.params.containerGuid || ''
  const viewport = useContainerDetailViewport()
  const access = useAuthStore((state) => state.access)
  // 记录当前货柜已完成首次加载，保活 Tab 恢复时保留旧内容并静默刷新。
  const loadedContainerGuidRef = useRef<string | null>(null)
  const visibleContainerGuidRef = useRef<string | null>(null)
  const lastLoadedContainerDetailSuccessRef = useRef<{ containerGuid: string; queryKey: string } | null>(null)
  const headerLoadRequestIdRef = useRef(0)
  const containerDetailLoadRequestIdRef = useRef(0)
  const [loading, setLoading] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailLoadingMore, setDetailLoadingMore] = useState(false)
  const [detailItemsTotal, setDetailItemsTotal] = useState(0)
  const [detailHasMore, setDetailHasMore] = useState(false)
  const [detailPageNumber, setDetailPageNumber] = useState(1)
  const [remoteTagStats, setRemoteTagStats] = useState<ContainerDetailTagStats>(EMPTY_CONTAINER_DETAIL_TAG_STATS)
  const [savingHeader, setSavingHeader] = useState(false)
  const [container, setContainer] = useState<ContainerMain | null>(null)
  const [rows, setRows] = useState<ContainerDetail[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])
  const [itemNumberFilter, setItemNumberFilter] = useState('')
  const [selectedTagFilters, setSelectedTagFilters] = useState<ContainerDetailTagFilter[]>([])
  const [categories, setCategories] = useState<WarehouseCategoryNode[]>([])
  const [categoryLoading, setCategoryLoading] = useState(false)
  const [categoryFilterValue, setCategoryFilterValue] = useState(CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)
  const [categoryExpandedKeys, setCategoryExpandedKeys] = useState<string[]>([])
  const [columnFilters, setColumnFilters] = useState<ContainerDetailColumnFilters>({})
  // 默认按货号升序展示，保证每次打开货柜明细时列表顺序稳定。
  const [sortState, setSortState] = useState<ContainerDetailSortState>(DEFAULT_CONTAINER_DETAIL_SORT)
  const [columnOrder, setColumnOrder] = useState<ContainerDetailTableColumnKey[]>([])
  const [showReadonlyOemPrice, setShowReadonlyOemPrice] = useState(false)
  const [batchFloatRate, setBatchFloatRate] = useState<number | null>(null)
  const [batchImportPrice, setBatchImportPrice] = useState<number | null>(null)
  const [batchOemPrice, setBatchOemPrice] = useState<number | null>(null)
  const [batchEnglishName, setBatchEnglishName] = useState('')
  const [batchEnglishNameModalOpen, setBatchEnglishNameModalOpen] = useState(false)
  const [batchEnglishNameSaving, setBatchEnglishNameSaving] = useState(false)
  const [batchCategoryOpen, setBatchCategoryOpen] = useState(false)
  const [targetCategoryGuid, setTargetCategoryGuid] = useState<string>()
  const [batchCategorySaving, setBatchCategorySaving] = useState(false)
  const [editingProductNameRowKey, setEditingProductNameRowKey] = useState<string | null>(null)
  const [editingProductNameValue, setEditingProductNameValue] = useState('')
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportColumnModalOpen, setExportColumnModalOpen] = useState(false)
  const [selectedExportColumnKeys, setSelectedExportColumnKeys] = useState<ContainerDetailExportColumnKey[]>(DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS)
  const [hqTranslating, setHqTranslating] = useState(false)
  const [pushToHqLoading, setPushToHqLoading] = useState(false)
  const [recalculateCostsLoading, setRecalculateCostsLoading] = useState(false)
  const [matchDomesticDataLoading, setMatchDomesticDataLoading] = useState(false)
  const [createProductsLoading, setCreateProductsLoading] = useState(false)
  const [pendingDetailSaveCount, setPendingDetailSaveCount] = useState(0)
  const [pendingWarehouseStatusCodes, setPendingWarehouseStatusCodes] = useState<Set<string>>(() => new Set())
  const [setCodeModalOpen, setSetCodeModalOpen] = useState(false)
  const [setCodeModalRow, setSetCodeModalRow] = useState<ContainerDetail | null>(null)
  const [setCodeItems, setSetCodeItems] = useState<ContainerDomesticSetCodeItem[]>([])
  const [setCodeLoading, setSetCodeLoading] = useState(false)
  const [setCodeSaving, setSetCodeSaving] = useState(false)
  const [setCodePriceEdits, setSetCodePriceEdits] = useState<ContainerSetCodePriceEdits>({})
  const [setCodeManualPurchasePriceKeys, setSetCodeManualPurchasePriceKeys] = useState<Set<string>>(() => new Set())
  const [detailTableRenderKey, setDetailTableRenderKey] = useState(0)
  const pushToHqLoadingRef = useRef(false)
  const createProductsLoadingRef = useRef(false)
  const detailAbortControllerRef = useRef<AbortController | null>(null)
  const setCodeAbortControllerRef = useRef<AbortController | null>(null)
  const pendingDetailSavePromisesRef = useRef<Set<Promise<unknown>>>(new Set())
  const failedDetailSaveKeysRef = useRef<Set<string>>(new Set())
  const ignoreProductNameBlurRef = useRef(false)
  const detailTableRef = useRef<TableRef | null>(null)
  const lastDetailTableScrollTopRef = useRef(0)
  const wasContainerDetailTabActiveRef = useRef(active)
  const detailTableRestoreFrameRef = useRef<number | null>(null)
  const editableCellRefs = useRef<Map<string, ContainerDetailFocusableCell>>(new Map())
  const pendingEditableCellFocusKeyRef = useRef<string | null>(null)
  const [headerEditing, setHeaderEditing] = useState(false)
  const [headerForm, setHeaderForm] = useState<{
    实际到货日期?: Dayjs | null
    汇率?: number
    运费?: number
    备注?: string
    状态?: number
  }>({})
  const columnDragSensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 6,
      },
    }),
  )

  useDynamicTabTitle(container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : undefined)

  useEffect(() => () => {
    setCodeAbortControllerRef.current?.abort()
  }, [])

  useEffect(() => {
    setCategoryLoading(true)
    getCategoryTree()
      .then((tree) => {
        setCategories(tree)
        setCategoryExpandedKeys(collectCategoryExpandedKeys(tree, 1))
      })
      .catch((error) => {
        console.error(error)
        message.error(error instanceof Error ? error.message : t('warehouse.categories.loadTreeFailed', '加载分类树失败'))
      })
      .finally(() => setCategoryLoading(false))
  }, [t])

  const categoryFilterOptions = useMemo(() => buildContainerDetailCategoryOptions(categories, t, i18n.language), [categories, i18n.language, t])
  const categoryLookup = useMemo(() => buildWarehouseCategoryLookup(categories), [categories])
  const selectedTargetCategory = useMemo(() => findWarehouseCategory(categories, targetCategoryGuid), [categories, targetCategoryGuid])
  const selectedTargetCategoryPath = targetCategoryGuid ? getWarehouseProductCategoryTooltip({
    categoryName: selectedTargetCategory?.categoryName,
    warehouseCategoryGUID: targetCategoryGuid,
  }, categoryLookup, i18n.language) : undefined

  const remoteColumnFilters = useMemo<ContainerDetailColumnFilters>(() => omitContainerDetailTextFilters(columnFilters), [columnFilters])

  const baseDetailQuery = useMemo(() => buildContainerDetailQuery({
    containerGuid,
    filters: remoteColumnFilters,
    selectedTags: [],
    pageNumber: 1,
    pageSize: CONTAINER_DETAIL_PAGE_SIZE,
  }), [containerGuid, remoteColumnFilters])

  const scopedDetailQuery = useMemo(() => buildContainerDetailQuery({
    containerGuid,
    filters: remoteColumnFilters,
    selectedTags: selectedTagFilters,
    pageNumber: 1,
    pageSize: CONTAINER_DETAIL_PAGE_SIZE,
  }), [containerGuid, remoteColumnFilters, selectedTagFilters])

  const baseDetailQueryKey = useMemo(() => JSON.stringify(baseDetailQuery), [baseDetailQuery])
  const scopedDetailQueryKey = useMemo(() => JSON.stringify(scopedDetailQuery), [scopedDetailQuery])
  const loadedDetailQueryKey = lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid
    ? lastLoadedContainerDetailSuccessRef.current?.queryKey
    : null
  const canUseLocalTagFilters = canUseContainerDetailLocalTagFilters({
    loadedQueryKey: loadedDetailQueryKey,
    baseQueryKey: baseDetailQueryKey,
    loadedRowsLength: rows.length,
    itemsTotal: detailItemsTotal,
    hasMore: detailHasMore,
    loading: detailLoading,
    loadingMore: detailLoadingMore,
  })
  // 当前非标签查询已全量加载时，标签只在前端切换；未全量时仍用带标签查询交给后端兜底。
  const activeLoadQuery = canUseLocalTagFilters ? baseDetailQuery : scopedDetailQuery
  const activeLoadQueryKey = canUseLocalTagFilters ? baseDetailQueryKey : scopedDetailQueryKey

  const reconcileLoadedMatchStatus = async (products: ContainerDetail[], requestId: number) => {
    const rowsNeedingMatchStatus = products.filter((row) => getContainerDetailProductCode(row) || getContainerDetailItemNumber(row))
    const detectionItems = buildContainerDetailDetectionItems(rowsNeedingMatchStatus)
    if (!detectionItems.length) return

    try {
      const detected = await detectProducts(detectionItems)
      if (containerDetailLoadRequestIdRef.current !== requestId) {
        return
      }
      const updates = buildContainerDetailMatchStatusUpdates(rowsNeedingMatchStatus, detected)
      if (!updates.length) return
      // 加载态只校正表格展示状态，不写库，避免打开页面时产生业务字段副作用。
      setRows((items) =>
        items.map((item) => {
          const match = updates.find((update) => update.hguid === item.hguid)
          return match ? mergeContainerDetailPatch(item, match as Partial<ContainerDetail>) : item
        }),
      )
    } catch (error) {
      console.error('货柜明细匹配状态校正失败', error)
    }
  }

  const loadHeader = async (showLoading = true) => {
    if (!containerGuid) {
      return
    }
    const currentRequestId = headerLoadRequestIdRef.current + 1
    headerLoadRequestIdRef.current = currentRequestId
    if (showLoading) {
      setLoading(true)
    }
    try {
      const info = await getContainerDetail(containerGuid)
      if (headerLoadRequestIdRef.current !== currentRequestId) {
        return
      }
      loadedContainerGuidRef.current = containerGuid
      visibleContainerGuidRef.current = containerGuid
      setContainer(info)
      setHeaderForm({
        实际到货日期: info.实际到货日期 ? dayjs(info.实际到货日期) : null,
        汇率: info.汇率,
        运费: info.运费,
        备注: info.备注,
        状态: info.状态,
      })
    } catch (error) {
      if (headerLoadRequestIdRef.current !== currentRequestId) {
        return
      }
      const errorMessage = error instanceof Error ? error.message : t('containers.messages.loadDetailFailed')
      if (showLoading) {
        console.error(error)
        visibleContainerGuidRef.current = null
        message.error(errorMessage)
      } else {
        console.error('货柜详情静默刷新失败', error)
      }
    } finally {
      if (headerLoadRequestIdRef.current !== currentRequestId) {
        return
      }
      if (showLoading) {
        setLoading(false)
      }
    }
  }

  const loadDetailChunk = async (pageNumber: number, mode: 'reset' | 'append' = 'reset') => {
    if (!containerGuid) return
    if (mode === 'append' && (detailLoading || detailLoadingMore || !detailHasMore)) return

    const currentRequestId = containerDetailLoadRequestIdRef.current + 1
    containerDetailLoadRequestIdRef.current = currentRequestId
    const controller = new AbortController()

    if (mode === 'reset') {
      detailAbortControllerRef.current?.abort()
      detailAbortControllerRef.current = controller
      lastLoadedContainerDetailSuccessRef.current = null
      lastDetailTableScrollTopRef.current = 0
      setDetailLoading(true)
      setRows([])
      setSelectedRowKeys([])
    } else {
      setDetailLoadingMore(true)
    }

    try {
      const result = await queryContainerProducts(
        containerGuid,
        { ...activeLoadQuery, pageNumber, pageSize: CONTAINER_DETAIL_PAGE_SIZE },
        controller.signal,
      )
      if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId) {
        return
      }

      setRows((items) => mode === 'reset' ? result.items : mergeContainerDetailLoadedItems(items, result.items))
      setDetailItemsTotal(result.itemsTotal)
      setDetailPageNumber(result.pageNumber)
      setDetailHasMore(result.hasMore)
      setRemoteTagStats({ ...EMPTY_CONTAINER_DETAIL_TAG_STATS, ...result.tagStats })
      if (mode === 'reset') {
        lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: activeLoadQueryKey }
      }
      void reconcileLoadedMatchStatus(result.items, currentRequestId)
    } catch (error) {
      if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('containers.messages.loadDetailFailed'))
    } finally {
      if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId) {
        return
      }
      if (mode === 'reset') {
        setDetailLoading(false)
      } else {
        setDetailLoadingMore(false)
      }
    }
  }

  const loadData = async (showLoading = true) => {
    await Promise.all([
      loadHeader(showLoading),
      loadDetailChunk(1, 'reset'),
    ])
  }

  useEffect(() => {
    if (!active) return

    if (shouldSkipDetailAutoReload({
      requestedDetailId: containerGuid,
      loadedDetailId: loadedContainerGuidRef.current,
      visibleDetailId: visibleContainerGuidRef.current,
    })) {
      // 保活 Tab 切回同一货柜时复用缓存，避免自动请求导致页面闪动。
      return
    }

    // 隐藏的 KeepAlive 节点也会收到全局路由变化，必须只让当前激活节点发起请求。
    const shouldShowInitialLoading = shouldShowDetailInitialLoading({
      requestedDetailId: containerGuid,
      loadedDetailId: loadedContainerGuidRef.current,
      visibleDetailId: visibleContainerGuidRef.current,
    })
    void loadHeader(shouldShowInitialLoading)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, containerGuid])

  useEffect(() => {
    if (!active) return

    if (shouldSkipDetailAutoReload({
      requestedDetailId: containerGuid,
      loadedDetailId: loadedContainerGuidRef.current,
      visibleDetailId: visibleContainerGuidRef.current,
      requestedDetailQueryKey: activeLoadQueryKey,
      loadedDetailQueryKey: loadedDetailQueryKey,
    })) {
      return
    }

    void loadDetailChunk(1, 'reset')
    return () => {
      detailAbortControllerRef.current?.abort()
    }
    // activeLoadQueryKey 会在明细未全量加载时包含 tag；全量加载后标签切换只走前端过滤。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, activeLoadQueryKey])

  useEffect(() => {
    // 标签可能只在前端过滤，不再触发远程重载；主动清空选择，保持批量操作范围清晰。
    setSelectedRowKeys([])
  }, [selectedTagFilters])

  useEffect(() => {
    const wasActive = wasContainerDetailTabActiveRef.current
    wasContainerDetailTabActiveRef.current = active

    if (!active || wasActive || rows.length === 0) {
      return
    }

    const scrollTop = lastDetailTableScrollTopRef.current
    const renderFrame = window.requestAnimationFrame(() => {
      // KeepAlive 隐藏节点恢复时，AntD 虚拟表格可能沿用隐藏状态下的测量结果；切回后重挂载表格让 body 重新计算高度。
      setDetailTableRenderKey((value) => value + 1)
      detailTableRestoreFrameRef.current = window.requestAnimationFrame(() => {
        detailTableRef.current?.scrollTo?.({ top: scrollTop })
      })
    })
    detailTableRestoreFrameRef.current = renderFrame

    return () => {
      if (detailTableRestoreFrameRef.current !== null) {
        window.cancelAnimationFrame(detailTableRestoreFrameRef.current)
        detailTableRestoreFrameRef.current = null
      }
    }
  }, [active, containerGuid, rows.length])

  const baseFilteredRows = useMemo(() => {
    return rows
  }, [rows])

  const tagFilteredRows = useMemo(() => {
    // 后端已通过 scopedDetailQuery 返回过滤后数据时，跳过前端过滤避免冗余扫描
    if (!canUseLocalTagFilters) return baseFilteredRows
    return baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))
  }, [baseFilteredRows, selectedTagFilters, canUseLocalTagFilters])

  const textFilteredRows = useMemo(() => {
    return applyContainerDetailLoadedTextFilters(tagFilteredRows, itemNumberFilter, columnFilters)
  }, [columnFilters, itemNumberFilter, tagFilteredRows])

  const localTagStats = useMemo(() => buildContainerDetailTagStats(tagFilteredRows), [tagFilteredRows])

  const filteredRows = useMemo(() => {
    return applyContainerDetailCategoryFilter(textFilteredRows, categoryFilterValue, categoryLookup)
  }, [categoryFilterValue, categoryLookup, textFilteredRows])

  const displayRows = useMemo(
    () => {
      // 列头排序只调整当前已加载且已过滤的行，避免点击列头触发远程重载。
      return applyContainerDetailColumnState(filteredRows, {}, sortState)
    },
    [filteredRows, sortState],
  )

  const setEditableCellRef = (
    rowKeyValue: string,
    columnKey: ContainerDetailEditableColumnKey,
    cell: ContainerDetailFocusableCell | null,
  ) => {
    const cellKey = buildContainerDetailEditableCellKey(rowKeyValue, columnKey)
    if (cell) {
      editableCellRefs.current.set(cellKey, cell)
      if (pendingEditableCellFocusKeyRef.current === cellKey) {
        pendingEditableCellFocusKeyRef.current = null
        window.requestAnimationFrame(() => {
          cell.focus()
          cell.select?.()
        })
      }
      return
    }
    editableCellRefs.current.delete(cellKey)
  }

  const blurActiveContainerDetailEditableCell = () => {
    const activeElement = document.activeElement
    if (activeElement instanceof HTMLElement) {
      // 创建新商品前主动结束当前输入，确保 InputNumber 的 blur 保存链路先落库。
      activeElement.blur()
    }
  }

  const handleEditableCellKeyDown = (
    row: ContainerDetail,
    columnKey: ContainerDetailEditableColumnKey,
    event: KeyboardEvent<HTMLElement>,
  ) => {
    const direction = containerDetailEditableDirectionByKey[event.key]
    if (!direction || event.nativeEvent.isComposing) {
      return
    }

    const currentRowKey = rowKey(row)
    const nextCell = getNextContainerDetailEditableCell(
      currentRowKey,
      columnKey,
      displayRows.map(rowKey),
      CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS,
      direction,
    )
    if (!nextCell) {
      return
    }

    const nextCellKey = buildContainerDetailEditableCellKey(
      nextCell.rowKey,
      nextCell.columnKey as ContainerDetailEditableColumnKey,
    )
    event.preventDefault()
    event.currentTarget.blur()
    pendingEditableCellFocusKeyRef.current = nextCellKey
    detailTableRef.current?.scrollTo?.({ key: nextCell.rowKey })
    window.requestAnimationFrame(() => {
      // 方向键只切换焦点；当前输入值继续复用 blur 保存链路落库。
      const targetCell = editableCellRefs.current.get(nextCellKey)
      if (!targetCell) {
        return
      }
      pendingEditableCellFocusKeyRef.current = null
      targetCell.focus()
      targetCell.select?.()
    })
  }

  const localProductTypeStats = useMemo(() => buildContainerDetailTagStats(rows), [rows])
  const tagStats: ContainerDetailTagStats = useMemo(() => ({
    ...(canUseLocalTagFilters ? localTagStats : remoteTagStats),
    // 商品类型统计由前端按当前已加载明细实时计算，后端仍只负责已有业务 tag 统计。
    normal: canUseLocalTagFilters ? localTagStats.normal : localProductTypeStats.normal,
    set: canUseLocalTagFilters ? localTagStats.set : localProductTypeStats.set,
    multi: canUseLocalTagFilters ? localTagStats.multi : localProductTypeStats.multi,
    setChild: canUseLocalTagFilters ? localTagStats.setChild : localProductTypeStats.setChild,
  }), [canUseLocalTagFilters, localProductTypeStats, localTagStats, remoteTagStats])

  const tagStatOptions = useMemo<Array<{ value: ContainerDetailTagFilter; label: string; color?: string }>>(() => [
    { value: 'all', label: t('containers.filters.allTags'), color: 'blue' },
    { value: 'new', label: t('containers.tags.newProduct'), color: 'cyan' },
    { value: 'existing', label: t('containers.tags.existingProduct'), color: 'purple' },
    { value: 'normal', label: t('containers.productTypes.normal'), color: 'default' },
    { value: 'set', label: t('containers.productTypes.set'), color: 'blue' },
    { value: 'multi', label: t('containers.productTypes.multiCode'), color: 'purple' },
    { value: 'setChild', label: t('containers.productTypes.setChild'), color: 'orange' },
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

  const exportColumnOptions = useMemo(
    () => CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => ({
      label: t(column.labelKey, column.fallbackLabel),
      value: column.key,
    })),
    [t],
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
    () => displayRows.filter((row) => selectedRowKeys.includes(rowKey(row))),
    [displayRows, selectedRowKeys],
  )

  const buildSetCodeAutoPurchasePriceEdits = (
    items: ContainerDomesticSetCodeItem[],
    mainPurchasePrice: number | null | undefined,
    baseEdits: ContainerSetCodePriceEdits = {},
    manualPurchasePriceKeys: Set<string> = setCodeManualPurchasePriceKeys,
  ) => {
    const totalRetailPrice = items.reduce((sum, item) => {
      const key = getSetCodeRowKey(item)
      const edit = key ? baseEdits[key] : undefined
      const retailPrice = edit?.retailPrice !== undefined ? edit.retailPrice : item.retailPrice
      return typeof retailPrice === 'number' && Number.isFinite(retailPrice) && retailPrice > 0 ? sum + retailPrice : sum
    }, 0)

    return items.reduce<ContainerSetCodePriceEdits>((nextEdits, item) => {
      const key = getSetCodeRowKey(item)
      if (!key || manualPurchasePriceKeys.has(key)) return nextEdits

      const edit = nextEdits[key]
      const nextRetailPrice = edit?.retailPrice !== undefined ? edit.retailPrice : item.retailPrice
      const nextPurchasePrice = calculateContainerSetCodePurchasePrice(mainPurchasePrice, nextRetailPrice, totalRetailPrice)
      if (nextPurchasePrice === undefined) {
        if (edit && 'purchasePrice' in edit) {
          const rest = { ...edit }
          delete rest.purchasePrice
          if (Object.keys(rest).length > 0) {
            nextEdits[key] = rest
          } else {
            delete nextEdits[key]
          }
        }
        return nextEdits
      }

      nextEdits[key] = {
        ...edit,
        purchasePrice: nextPurchasePrice,
      }
      return nextEdits
    }, { ...baseEdits })
  }

  const changedSetCodePriceItems = useMemo(() => setCodeItems.flatMap((item) => {
    const key = getSetCodeRowKey(item)
    const edit = setCodePriceEdits[key]
    if (!item.setProductCode || !edit) return []
    const nextRetailPrice = edit.retailPrice !== undefined ? edit.retailPrice : item.retailPrice
    const nextPurchasePrice = edit.purchasePrice !== undefined ? edit.purchasePrice : item.purchasePrice
    if (nextRetailPrice === item.retailPrice && nextPurchasePrice === item.purchasePrice) return []
    return [{
      setProductCode: item.setProductCode,
      retailPrice: nextRetailPrice,
      purchasePrice: nextPurchasePrice,
    }]
  }), [setCodeItems, setCodePriceEdits])

  const hasHiddenSelectedRows = selectedRowKeys.length > 0 && selectedRows.length < selectedRowKeys.length
  const targetRows = selectedRowKeys.length ? selectedRows : displayRows

  const buildDetailBatchScope = (): ContainerDetailBatchScope => (
    selectedRowKeys.length
      ? { selectedHguids: selectedRowKeys.map(String) }
      : { query: scopedDetailQuery }
  )

  const fetchAllRowsForCurrentQuery = async () => {
    const allRows: ContainerDetail[] = []
    let pageNumber = 1
    let hasMore = true

    while (hasMore) {
      // 导出和上下架必须作用于当前筛选结果全量，不能只拿虚拟表格已加载块。
      const result = await queryContainerProducts(containerGuid, {
        ...scopedDetailQuery,
        pageNumber,
        pageSize: 500,
      })
      allRows.push(...result.items)
      hasMore = result.hasMore
      pageNumber += 1
    }

    return allRows
  }

  const ensureTargetRowsVisible = () => {
    if (!hasHiddenSelectedRows) return true
    message.warning(t('containers.messages.selectedRowsHidden', '已选明细不在当前筛选结果中，请重新选择后再操作'))
    return false
  }
  const canCreateContainerProducts = access.canEditContainer && access.canManagePosProducts
  const canBatchSetCategory = access.canEditContainer && access.canManagePosProducts

  const patchRow = (key: string, patch: Partial<ContainerDetail>) => {
    setRows((items) => items.map((item) => (rowKey(item) === key ? mergeContainerDetailPatch(item, patch) : item)))
  }

  const getDetailSaveFailedMessage = () => t('containers.messages.detailSaveFailed', '货柜明细保存失败，请稍后重试')

  const handleDetailSaveError = (error: unknown) => {
    message.error(error instanceof Error ? error.message : getDetailSaveFailedMessage())
  }

  const loadSetCodeItems = async (row: ContainerDetail, manualPurchasePriceKeys: Set<string> = new Set()) => {
    const productCode = getContainerDetailProductCode(row)
    if (!productCode) {
      message.warning('缺少商品编码，无法加载套装多码数据')
      return
    }

    setCodeAbortControllerRef.current?.abort()
    const abortController = new AbortController()
    setCodeAbortControllerRef.current = abortController
    setSetCodeLoading(true)
    setSetCodePriceEdits({})
    setSetCodeManualPurchasePriceKeys(manualPurchasePriceKeys)
    try {
      const items = await getContainerDomesticSetCodes(productCode, abortController.signal)
      setSetCodeItems(items)
      setSetCodePriceEdits(buildSetCodeAutoPurchasePriceEdits(items, row.进口价格, {}, manualPurchasePriceKeys))
    } catch (error) {
      if ((error as DOMException)?.name !== 'AbortError') {
        message.error(error instanceof Error ? error.message : '获取套装多码数据失败')
      }
    } finally {
      if (setCodeAbortControllerRef.current === abortController) {
        setSetCodeLoading(false)
        setCodeAbortControllerRef.current = null
      }
    }
  }

  const openSetCodeModal = (row: ContainerDetail) => {
    setSetCodeModalRow(row)
    setSetCodeModalOpen(true)
    setSetCodeItems([])
    void loadSetCodeItems(row)
  }

  const closeSetCodeModal = () => {
    setCodeAbortControllerRef.current?.abort()
    setCodeAbortControllerRef.current = null
    setSetCodeModalOpen(false)
    setSetCodeModalRow(null)
    setSetCodeItems([])
    setSetCodePriceEdits({})
    setSetCodeManualPurchasePriceKeys(new Set())
    setSetCodeLoading(false)
  }

  const patchSetCodeRetailPriceEdit = (item: ContainerDomesticSetCodeItem, retailPrice: number | null) => {
    const key = getSetCodeRowKey(item)
    if (!key) return
    setSetCodePriceEdits((current) => {
      const nextEdits: ContainerSetCodePriceEdits = {
        ...current,
        [key]: {
          ...current[key],
          retailPrice,
        },
      }
      const mainPurchasePrice = setCodeModalRow?.进口价格
      // 套装子项进货价只按货柜明细当前行进口价分摊，不读取仓库当前进货价。
      return buildSetCodeAutoPurchasePriceEdits(setCodeItems, mainPurchasePrice, nextEdits, setCodeManualPurchasePriceKeys)
    })
  }

  const patchSetCodePriceEdit = (
    item: ContainerDomesticSetCodeItem,
    patch: { retailPrice?: number | null; purchasePrice?: number | null },
  ) => {
    const key = getSetCodeRowKey(item)
    if (!key) return
    if ('purchasePrice' in patch) {
      setSetCodeManualPurchasePriceKeys((current) => new Set(current).add(key))
    }
    setSetCodePriceEdits((current) => ({
      ...current,
      [key]: {
        ...current[key],
        ...patch,
      },
    }))
  }

  const saveSetCodePrices = async () => {
    const productCode = setCodeModalRow ? getContainerDetailProductCode(setCodeModalRow) : undefined
    if (!productCode || changedSetCodePriceItems.length === 0) return

    setSetCodeSaving(true)
    try {
      await updateContainerDomesticSetCodePrices(productCode, changedSetCodePriceItems)
      message.success('保存成功')
      if (setCodeModalRow) {
        await loadSetCodeItems(setCodeModalRow, setCodeManualPurchasePriceKeys)
      }
    } catch (error) {
      message.error(error instanceof Error ? error.message : '保存套装多码价格失败')
    } finally {
      setSetCodeSaving(false)
    }
  }

  const trackDetailSavePromise = <T,>(saveKeys: string[], promise: Promise<T>) => {
    const trackedPromise = promise.catch((error) => {
      saveKeys.forEach((saveKey) => failedDetailSaveKeysRef.current.add(saveKey))
      throw error
    }).then((value) => {
      saveKeys.forEach((saveKey) => failedDetailSaveKeysRef.current.delete(saveKey))
      return value
    }).finally(() => {
      pendingDetailSavePromisesRef.current.delete(trackedPromise)
      setPendingDetailSaveCount(pendingDetailSavePromisesRef.current.size)
    })
    pendingDetailSavePromisesRef.current.add(trackedPromise)
    setPendingDetailSaveCount(pendingDetailSavePromisesRef.current.size)
    return trackedPromise
  }

  const flushPendingDetailSaves = async () => {
    const pendingSaves = Array.from(pendingDetailSavePromisesRef.current)
    if (pendingSaves.length) {
      await Promise.all(pendingSaves)
    }
    if (failedDetailSaveKeysRef.current.size > 0) {
      throw new Error(getDetailSaveFailedMessage())
    }
  }

  const saveRowPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {
    if (!access.canEditContainer || !row.hguid) return
    const saveKey = rowKey(row)
    patchRow(saveKey, patch)
    // 商品名称最终写回 DomesticProduct；创建新商品前必须等待这里落库，避免后台 job 读取旧中文名。
    await trackDetailSavePromise(
      buildContainerDetailSaveFailureKeys(saveKey, patch),
      batchUpdateDetails([{ hguid: row.hguid, ...patch } as UpdateContainerDetailRequest]),
    )
  }

  const startEditingProductName = (row: ContainerDetail) => {
    if (!access.canEditContainer) return
    ignoreProductNameBlurRef.current = false
    setEditingProductNameRowKey(rowKey(row))
    setEditingProductNameValue(getContainerDetailProductName(row) ?? '')
  }

  const cancelEditingProductName = () => {
    ignoreProductNameBlurRef.current = true
    setEditingProductNameRowKey(null)
    setEditingProductNameValue('')
  }

  const commitProductNameEdit = async (row: ContainerDetail) => {
    const productName = editingProductNameValue.trim()
    ignoreProductNameBlurRef.current = true
    cancelEditingProductName()
    await saveRowPatch(row, { 商品名称: productName })
  }

  const handleProductNameEditBlur = (row: ContainerDetail) => {
    if (ignoreProductNameBlurRef.current) {
      ignoreProductNameBlurRef.current = false
      return
    }
    void commitProductNameEdit(row).catch(handleDetailSaveError)
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
      状态: headerForm.状态,
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
    await trackDetailSavePromise(buildContainerDetailSaveFailureKeys(rowKey(row), update), batchUpdateDetails(updates))
  }

  const applyFloatRate = async () => {
    if (batchFloatRate == null) {
      message.warning(t('containers.messages.enterFloatRate'))
      return
    }
    const result = await applyContainerFloatRateByScope(containerGuid, buildDetailBatchScope(), batchFloatRate)
    await loadDetailChunk(1, 'reset')
    setBatchFloatRate(null)
    setSelectedRowKeys([])
    message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))
  }

  const handleRecalculateCosts = async () => {
    if (!access.canEditContainer) return
    if (container?.运费 == null) {
      message.warning('缺少运费，无法重算成本')
      return
    }
    if (!container?.总体积 || container.总体积 <= 0) {
      message.warning('缺少总体积，无法重算成本')
      return
    }
    setRecalculateCostsLoading(true)
    try {
      const result = await recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())
      await loadDetailChunk(1, 'reset')
      setSelectedRowKeys([])
      message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))
    } finally {
      setRecalculateCostsLoading(false)
    }
  }

  const handleMatchDomesticData = async () => {
    if (!access.canEditContainer) return
    setMatchDomesticDataLoading(true)
    try {
      if (!ensureTargetRowsVisible()) return
      // 未勾选时按当前远程筛选结果全量匹配，避免虚拟表格只处理已加载块。
      const scopedRows = selectedRowKeys.length ? targetRows : await fetchAllRowsForCurrentQuery()
      if (!scopedRows.length) {
        message.warning('没有可匹配的明细')
        return
      }

      const detectionItems = buildContainerDetailDetectionItems(scopedRows)

      if (!detectionItems.length) {
        message.warning('当前明细缺少可匹配的商品编码或货号')
        return
      }

      const detected = await detectProducts(detectionItems)
      const updates = buildContainerDetailMatchedDomesticDataUpdates(scopedRows, detected, container)
        .map((update) => ({ ...update, SkipRelatedProductSync: true }))
      if (!updates.length) {
        message.info('没有需要更新的国内数据')
        return
      }

      const writableUpdates = updates.filter((update) => (
        update.国内价格 != null ||
        update.贴牌价格 != null ||
        update.商品名称 != null ||
        update.英文名称 != null ||
        update.单件装箱数 != null ||
        update.装柜数量 != null ||
        update.单件体积 != null ||
        update.合计装柜体积 != null ||
        update.合计装柜金额 != null ||
        update.运输成本 != null ||
        update.进口价格 != null
      ))
      if (writableUpdates.length) {
        // 匹配方式只用于当前表格展示，批量保存接口只提交它支持的业务字段。
        await batchUpdateDetails(writableUpdates)
      }
      applyDetailUpdatesToRows(updates)
      if (!selectedRowKeys.length) {
        await loadDetailChunk(1, 'reset')
      }
      const pricePatchCount = updates.filter((update) => update.国内价格 != null || update.贴牌价格 != null).length
      message.success(`已更新 ${updates.length} 条明细，补齐价格 ${pricePatchCount} 条`)
    } catch (error) {
      message.error(error instanceof Error ? error.message : '匹配国内数据失败')
    } finally {
      setMatchDomesticDataLoading(false)
    }
  }

  const applyPrices = async () => {
    if (batchImportPrice == null && batchOemPrice == null) {
      message.warning(t('containers.messages.enterImportOrOemPrice'))
      return
    }
    const result = await applyContainerPricesByScope(containerGuid, buildDetailBatchScope(), {
      importPrice: batchImportPrice,
      oemPrice: batchOemPrice,
    })
    await loadDetailChunk(1, 'reset')
    setBatchImportPrice(null)
    setBatchOemPrice(null)
    setSelectedRowKeys([])
    message.success(t('containers.messages.detailPricesUpdated', { count: result.totalUpdated }))
  }

  const applyActive = async (isActive: boolean) => {
    if (!ensureTargetRowsVisible()) return
    const scopedRows = selectedRowKeys.length ? targetRows : await fetchAllRowsForCurrentQuery()
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    const productCodes = scopedRows
      .map(getContainerDetailProductCode)
      .filter((value): value is string => Boolean(value))
      .filter((value) => !pendingWarehouseStatusCodes.has(value))
    if (!productCodes.length) {
      message.warning(
        scopedRows.some((row) => {
          const productCode = getContainerDetailProductCode(row)
          return productCode ? pendingWarehouseStatusCodes.has(productCode) : false
        })
          ? t('containers.messages.warehouseStatusUpdating', '商品上下架正在提交，请稍后再试')
          : t('containers.messages.selectedProductsMissingCode'),
      )
      return
    }
    const result = await bulkSetStatus(productCodes, isActive)
    const failureMessage = getContainerDetailWarehouseActionFailureMessage(result, t('containers.messages.batchActiveFailed'))
    if (failureMessage) {
      message.error(failureMessage)
      return
    }
    setRows((items) => applyContainerDetailWarehouseStatusByProductCodes(items, productCodes, isActive))
    setSelectedRowKeys([])
    message.success(t(isActive ? 'containers.messages.productsActivated' : 'containers.messages.productsDeactivated', { count: productCodes.length }))
  }

  const translateNames = async () => {
    if (!ensureTargetRowsVisible()) return
    const names = Array.from(new Set(targetRows.map(getContainerDetailTranslationSource).filter((value): value is string => Boolean(value))))
    if (!names.length) {
      message.warning(t('containers.messages.noNamesToTranslate'))
      return
    }
    const translations = await batchTranslate(names)
    const updates = buildContainerDetailTranslationUpdates(targetRows, translations)
    const skippedInvalidCount = countContainerDetailInvalidTranslationResults(targetRows, translations)
    if (updates.length) {
      await batchUpdateDetails(updates)
      setRows((items) => applyContainerDetailEnglishNameUpdates(items, updates))
      message.success(t('containers.messages.namesTranslated', { count: updates.length }))
    }
    if (skippedInvalidCount > 0) {
      message.warning(t('containers.messages.invalidTranslatedNamesSkipped', { count: skippedInvalidCount }))
    }
    if (!updates.length && skippedInvalidCount === 0) {
      message.success(t('containers.messages.namesTranslated', { count: 0 }))
    }
  }

  const openBatchEditEnglishName = () => {
    if (!ensureTargetRowsVisible()) return
    if (!targetRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    setBatchEnglishName('')
    setBatchEnglishNameModalOpen(true)
  }

  const openBatchCategory = () => {
    if (!ensureTargetRowsVisible()) return
    if (!targetRows.length) {
      message.warning(t('containers.messages.noCategoryFilterRows', '当前没有可分类的明细'))
      return
    }
    setTargetCategoryGuid(undefined)
    // 每次打开批量分类弹窗都只展开到一级分类，避免默认露出过深的子分类。
    setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1))
    setBatchCategoryOpen(true)
  }

  const handleBatchCategorySave = async () => {
    if (!canBatchSetCategory) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    if (!targetCategoryGuid) {
      message.warning(t('warehouse.categories.selectTargetCategory', '请选择目标分类'))
      return
    }
    if (!ensureTargetRowsVisible()) return

    const batchCategoryTargetRows = selectedRowKeys.length ? selectedRows : displayRows
    const { productCodes, skippedMissingCodeCount } = getContainerDetailBatchCategoryProductCodes(batchCategoryTargetRows)
    if (!productCodes.length) {
      message.warning(t('containers.messages.noProductsForCategoryAssign', '当前目标明细没有可分类的已有商品'))
      return
    }

    setBatchCategorySaving(true)
    try {
      // 批量分类只写仓库商品分类，不修改货柜明细价格、数量等业务字段。
      await batchAssignProducts(targetCategoryGuid, productCodes)
      const productCodeSet = new Set(productCodes)
      const selectedTargetCategoryName = selectedTargetCategory
        ? formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)
        : undefined
      const nextCategoryPath = selectedTargetCategoryPath || selectedTargetCategoryName
      // 保存成功后只更新当前已加载行，避免重新查询整张明细表造成等待和 loading。
      setRows((items) =>
        items.map((item) => {
          const productCode = getContainerDetailProductCode(item)
          if (!productCode || !productCodeSet.has(productCode)) return item
          return {
            ...item,
            categoryName: selectedTargetCategoryName,
            CategoryName: selectedTargetCategoryName,
            productCategoryName: selectedTargetCategoryName,
            ProductCategoryName: selectedTargetCategoryName,
            categoryPath: nextCategoryPath,
            CategoryPath: nextCategoryPath,
            categoryFullPath: nextCategoryPath,
            CategoryFullPath: nextCategoryPath,
            warehouseCategoryGUID: targetCategoryGuid,
            WarehouseCategoryGUID: targetCategoryGuid,
            productCategoryGUID: targetCategoryGuid,
            ProductCategoryGUID: targetCategoryGuid,
            商品信息: item.商品信息
              ? {
                  ...item.商品信息,
                  categoryName: selectedTargetCategoryName,
                  CategoryName: selectedTargetCategoryName,
                  productCategoryName: selectedTargetCategoryName,
                  ProductCategoryName: selectedTargetCategoryName,
                  categoryPath: nextCategoryPath,
                  CategoryPath: nextCategoryPath,
                  categoryFullPath: nextCategoryPath,
                  CategoryFullPath: nextCategoryPath,
                  warehouseCategoryGUID: targetCategoryGuid,
                  WarehouseCategoryGUID: targetCategoryGuid,
                  productCategoryGUID: targetCategoryGuid,
                  ProductCategoryGUID: targetCategoryGuid,
                }
              : item.商品信息,
          }
        }),
      )
      setSelectedRowKeys([])
      setBatchCategoryOpen(false)
      setTargetCategoryGuid(undefined)
      message.success(t('containers.messages.batchCategoryUpdated', '已设置 {{count}} 个商品分类', { count: productCodes.length }))
      if (skippedMissingCodeCount > 0) {
        message.warning(t('containers.messages.batchCategorySkippedMissingCode', '已跳过 {{count}} 条缺少商品编码的明细', { count: skippedMissingCodeCount }))
      }
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('warehouse.categories.batchAssignFailed', '批量分类失败'))
    } finally {
      setBatchCategorySaving(false)
    }
  }

  const submitBatchEditEnglishName = async () => {
    const updates = buildContainerDetailEnglishNameUpdates(targetRows, batchEnglishName)
    if (!updates.length) {
      message.warning(t('containers.messages.enterEnglishName'))
      return
    }

    setBatchEnglishNameSaving(true)
    try {
      const result = await batchUpdateDetails(updates)
      if (result.totalUpdated > 0) {
        setRows((items) => applyContainerDetailEnglishNameUpdates(items, updates))
      }
      setBatchEnglishNameModalOpen(false)
      setBatchEnglishName('')
      message.success(t('containers.messages.englishNamesUpdated', { count: result.totalUpdated }))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.englishNamesUpdateFailed'))
    } finally {
      setBatchEnglishNameSaving(false)
    }
  }

  const clearEnglishNames = () => {
    if (!ensureTargetRowsVisible()) return
    const updates = buildContainerDetailClearEnglishNameUpdates(targetRows)
    if (!updates.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }

    Modal.confirm({
      title: t('containers.modals.clearEnglishNamesTitle'),
      content: t('containers.modals.clearEnglishNamesContent', { count: updates.length }),
      okText: t('containers.actions.clearEnglishNames'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: async () => {
        await batchUpdateDetails(updates)
        setRows((items) => applyContainerDetailEnglishNameUpdates(
          items,
          updates.map((update) => ({ hguid: update.hguid, 英文名称: undefined })),
        ))
        message.success(t('containers.messages.englishNamesCleared', { count: updates.length }))
      },
    })
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

  const renderPushToHqResultContent = (
    result: PushProductsToHqResult,
    selection: ReturnType<typeof buildContainerDetailHqPushSelection>,
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
    return (
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
  }

  const buildPushToHqFallbackResult = (job: PushProductsToHqJobResult): PushProductsToHqResult => ({
    successCount: 0,
    failedCount: job.errors?.length || 1,
    totalCount: job.errors?.length || 1,
    affectedRowCount: 0,
    errors: job.errors?.length ? job.errors : [job.message || t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败')],
    message: job.message,
  })

  const releasePushToHqLoading = () => {
    pushToHqLoadingRef.current = false
    setPushToHqLoading(false)
  }

  const notifyPushToHqJobFinished = (
    job: PushProductsToHqJobResult,
    selection: ReturnType<typeof buildContainerDetailHqPushSelection>,
    pushToHqNotificationKey: string,
  ) => {
    const result = job.result ?? buildPushToHqFallbackResult(job)
    const hasErrors = (result.errors ?? []).length > 0 || (result.failedCount ?? 0) > 0

    if (job.status === 'Failed') {
      notification.error({
        key: pushToHqNotificationKey,
        message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
        description: renderPushToHqResultContent(result, selection),
        duration: 0,
      })
      return
    }

    if (hasErrors) {
      notification.warning({
        key: pushToHqNotificationKey,
        message: t('posAdmin.products.pushToHqPartialSucceeded', '发送到 HQ 部分成功'),
        description: renderPushToHqResultContent(result, selection),
        duration: 0,
      })
      return
    }

    notification.success({
      key: pushToHqNotificationKey,
      message: t('posAdmin.products.pushToHqSucceeded', '发送到 HQ 完成'),
      description: renderPushToHqResultContent(result, selection),
      duration: 0,
    })
  }

  const pollPushToHqJob = (
    initialJob: PushProductsToHqJobResult,
    selection: ReturnType<typeof buildContainerDetailHqPushSelection>,
    pushToHqNotificationKey: string,
  ) => {
    if (initialJob.status === 'Succeeded' || initialJob.status === 'Failed') {
      notifyPushToHqJobFinished(initialJob, selection, pushToHqNotificationKey)
      return
    }

    const poller = createHqSyncJobPoller({
      jobId: initialJob.jobId,
      getJob: getPushProductsToHqJob,
    })

    void poller.promise
      .then((job) => {
        notifyPushToHqJobFinished(job, selection, pushToHqNotificationKey)
      })
      .catch((error) => {
        const errorResult = extractPushToHqErrorResult(error)
        if (errorResult) {
          notification.error({
            key: pushToHqNotificationKey,
            message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
            description: renderPushToHqResultContent(errorResult, selection),
            duration: 0,
          })
        } else {
          notification.error({
            key: pushToHqNotificationKey,
            message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
            description: error instanceof Error ? error.message : undefined,
            duration: 0,
          })
        }
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
      const job = await createPushProductsToHqJob({
        operationId: buildPushProductsToHqOperationId(containerGuid, selection.productCodes, selection.items.length),
        productCodes: selection.productCodes,
        items: selection.items,
      })
      const pushToHqNotificationKey = `container-push-to-hq:${job.jobId}`
      notification.info({
        key: pushToHqNotificationKey,
        message: t('containers.messages.pushToHqJobSubmitted', '发送到 HQ 任务已提交'),
        description: t('containers.messages.pushToHqJobRunning', '后台正在写入 HQ，完成后会显示各表新增/更新数量和错误摘要。'),
        duration: 0,
      })
      setSelectedRowKeys([])
      releasePushToHqLoading()
      pollPushToHqJob(job, selection, pushToHqNotificationKey)
    } catch (error) {
      const errorResult = extractPushToHqErrorResult(error)
      if (errorResult) {
        // 发送 HQ 的结果统一收敛到右上角通知，避免弹窗打断表格编辑状态。
        notification.error({
          message: t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'),
          description: renderPushToHqResultContent(errorResult, selection),
          duration: 0,
        })
      } else {
        message.error(error instanceof Error ? error.message : t('posAdmin.products.pushToHqFailed', '发送到 HQ 失败'))
      }
    } finally {
      releasePushToHqLoading()
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
    const description = (
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
      notification.error({
        message: t('containers.messages.createProductsJobFailed', '创建新商品失败'),
        description,
        duration: 0,
      })
      return
    }

    if (result.failedCount > 0 || result.errors.length > 0 || result.skippedCount > 0) {
      notification.warning({
        message: t('containers.messages.createProductsJobPartialSucceeded', '创建新商品部分完成'),
        description,
        duration: 0,
      })
      return
    }

    notification.success({
      message: t('containers.messages.createProductsJobSucceeded', '创建新商品完成'),
      description,
      duration: 0,
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
    blurActiveContainerDetailEditableCell()
    try {
      await flushPendingDetailSaves()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.detailSaveFailed', '货柜明细保存失败，请稍后重试'))
      return
    }
    const missingProductNameRows = findContainerDetailRowsMissingProductName(targetRows)
    if (missingProductNameRows.length) {
      message.warning(t(
        'containers.messages.createProductsMissingProductName',
        '请填写商品名称后再创建新商品：{{items}}',
        { items: missingProductNameRows.map((row) => row.label).join('、') },
      ))
      return
    }
    const missingRetailPriceRows = findContainerDetailRowsMissingCreateProductRetailPrice(targetRows)
    if (missingRetailPriceRows.length) {
      message.warning(t(
        'containers.messages.createProductsMissingRetailPrice',
        '请填写大于 0 的零售价后再创建新商品：{{items}}',
        { items: missingRetailPriceRows.map((row) => row.label).join('、') },
      ))
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
    const updates = candidates.filter((row) => {
      const code = row.商品编码 || row.商品信息?.商品编码 || ''
      const oemPrice = resolveContainerDetailOemPrice(row)
      // 更新已有商品时贴牌价使用仓库商品优先、明细兜底；编辑保存仍只写回明细贴牌价。
      return Boolean(code) && ((row.进口价格 ?? 0) > 0 || (oemPrice ?? 0) > 0)
    })
    if (!updates.length) {
      message.info(t('containers.messages.noPurchasePriceDiff'))
      return
    }
    try {
      await batchUpdateWarehouseProducts(updates.map((row) => {
        const oemPrice = resolveContainerDetailOemPrice(row)
        return {
          ProductCode: row.商品编码 || row.商品信息?.商品编码,
          ImportPrice: row.进口价格,
          OEMPrice: oemPrice,
          IsActive: true,
        }
      }))
      await upsertRetailForActiveStores(updates.map((row) => {
        const oemPrice = resolveContainerDetailOemPrice(row)
        return {
          ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
          PurchasePrice: row.进口价格,
          StoreRetailPriceValue: oemPrice,
          IsActive: true,
        }
      }))
      await upsertMultiCodeForActiveStores(updates.map((row) => {
        const oemPrice = resolveContainerDetailOemPrice(row)
        return {
          ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
          PurchasePrice: row.进口价格,
          MultiCodeRetailPrice: oemPrice,
          IsActive: true,
        }
      }))
      message.success(t('containers.messages.purchasePricesUpdated', { count: updates.length }))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '更新已有商品价格失败'))
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

  const confirmExportAllRows = () => new Promise<boolean>((resolve) => {
    Modal.confirm({
      title: t('containers.modals.exportAllDetailsTitle', '导出全部匹配商品'),
      content: t(
        'containers.modals.exportAllDetailsContent',
        '未选择商品，将按当前筛选和排序导出全部匹配商品，共 {{count}} 条。是否继续？',
        { count: detailItemsTotal },
      ),
      okText: t('common.confirm', '确认'),
      cancelText: t('common.cancel'),
      onOk: () => resolve(true),
      onCancel: () => resolve(false),
    })
  })

  const exportExcel = async (columnKeys: ContainerDetailExportColumnKey[] = DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS) => {
    if (!ensureTargetRowsVisible()) return
    if (!selectedRowKeys.length) {
      const confirmed = await confirmExportAllRows()
      if (!confirmed) return
    }
    const exportRows = selectedRowKeys.length ? targetRows : await fetchAllRowsForCurrentQuery()
    if (!exportRows.length) {
      message.warning(t('containers.messages.noDataToExport'))
      return
    }
    setExporting(true)
    try {
      const exportColumns = getContainerDetailExportColumns(columnKeys)
      const items: ContainerDetailExportItem[] = buildContainerDetailExportRows(exportRows)
      await exportContainerDetailsToExcel(items, {
        columns: exportColumns.map((column) => ({
          header: t(column.labelKey, column.fallbackLabel),
          key: column.key,
          width: column.width,
          valueType: column.valueType,
          currencySymbol: column.key === 'domesticPrice' ? '¥' : undefined,
        })),
        summary: {
          title: t('containers.export.summaryTitle', '货柜主表信息'),
          rows: [
            [
              { label: t('containers.fields.containerNumber'), value: container?.货柜编号 || '--' },
              { label: t('containers.fields.loadingDate'), value: formatDate(container?.装柜日期) },
              { label: t('containers.fields.estimatedArrival'), value: formatDate(container?.预计到岸日期) },
            ],
            [
              { label: t('containers.fields.status'), value: getContainerStatusText(container?.状态, t) },
              { label: t('containers.fields.actualArrival'), value: formatDate(container?.实际到货日期) },
              { label: t('containers.fields.exchangeRate'), value: container?.汇率 ?? '--', valueType: container?.汇率 == null ? 'text' : 'number' },
            ],
            [
              { label: t('containers.fields.freight'), value: container?.运费 ?? '--', valueType: container?.运费 == null ? 'text' : 'money' },
              { label: t('containers.fields.totalVolume'), value: container?.总体积 ?? '--', valueType: container?.总体积 == null ? 'text' : 'volume' },
              { label: t('containers.fields.remark'), value: container?.备注 || '--' },
            ],
          ],
        },
        fileName: `${container?.货柜编号 || t('containers.detailTitle')}_${t('containers.export.detailSuffix')}`,
        onProgress: (progress) => setExportProgress(progress),
      })
      message.success(t('containers.messages.detailsExported', { count: items.length }))
    } finally {
      setExporting(false)
      setExportProgress(0)
    }
  }

  const clearColumnFilter = (key: keyof ContainerDetailColumnFilters) => {
    setColumnFilters((current) => {
      const next = { ...current }
      delete next[key]
      return next
    })
  }

  const hasNumberRangeFilter = (value?: ContainerDetailNumberRangeFilter) => value?.min != null || value?.max != null
  const hasCustomSortState = sortState.field !== DEFAULT_CONTAINER_DETAIL_SORT.field || sortState.order !== DEFAULT_CONTAINER_DETAIL_SORT.order
  const hasActiveColumnState = Object.values(columnFilters).some((value) => {
    if (Array.isArray(value)) return value.length > 0
    if (value && typeof value === 'object') return hasNumberRangeFilter(value as ContainerDetailNumberRangeFilter)
    return typeof value === 'string' ? Boolean(value.trim()) : value != null
  }) || hasCustomSortState

  const filterIcon = (active?: boolean) => <SearchOutlined style={{ color: active ? '#1677ff' : undefined }} />

  const makeTextFilterDropdown = (key: TextColumnFilterKey, placeholder: string) => ({ confirm }: FilterDropdownProps) => (
    <div className="container-detail-column-filter" onKeyDown={(event) => event.stopPropagation()}>
      <Input
        value={(columnFilters[key] as string | undefined) ?? ''}
        allowClear
        placeholder={placeholder}
        onChange={(event) => setColumnFilters((current) => ({ ...current, [key]: event.target.value }))}
        onPressEnter={() => confirm()}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => confirm()}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => {
          clearColumnFilter(key)
          confirm()
        }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeNumberRangeFilterDropdown = (key: NumberColumnFilterKey) => ({ confirm }: FilterDropdownProps) => {
    const value = (columnFilters[key] as ContainerDetailNumberRangeFilter | undefined) ?? {}
    const updateRange = (patch: ContainerDetailNumberRangeFilter) => {
      const next = { ...value, ...patch }
      setColumnFilters((current) => ({ ...current, [key]: next }))
    }

    return (
      <div className="container-detail-column-filter" onKeyDown={(event) => event.stopPropagation()}>
        <Space.Compact>
          <InputNumber
            value={value.min}
            placeholder={t('containers.placeholders.minValue', '最小值')}
            controls={false}
            onChange={(nextValue) => updateRange({ min: nextValue == null ? undefined : Number(nextValue) })}
          />
          <InputNumber
            value={value.max}
            placeholder={t('containers.placeholders.maxValue', '最大值')}
            controls={false}
            onChange={(nextValue) => updateRange({ max: nextValue == null ? undefined : Number(nextValue) })}
          />
        </Space.Compact>
        <Space>
          <Button size="small" type="primary" onClick={() => confirm()}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
          <Button size="small" onClick={() => {
            clearColumnFilter(key)
            confirm()
          }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
        </Space>
      </div>
    )
  }

  const makeEnumFilterDropdown = (key: EnumColumnFilterKey, options: { value: string; label: string }[]) => ({ confirm }: FilterDropdownProps) => (
    <div className="container-detail-column-filter" onKeyDown={(event) => event.stopPropagation()}>
      <Select
        mode="multiple"
        value={(columnFilters[key] as string[] | undefined) ?? []}
        allowClear
        style={{ minWidth: 180 }}
        options={options}
        onChange={(values) => setColumnFilters((current) => ({ ...current, [key]: values } as ContainerDetailColumnFilters))}
      />
      <Space>
        <Button size="small" type="primary" onClick={() => confirm()}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
        <Button size="small" onClick={() => {
          clearColumnFilter(key)
          confirm()
        }}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
      </Space>
    </div>
  )

  const makeSortProps = (field: ContainerDetailSortField) => ({
    key: field,
    sorter: true,
    sortOrder: sortState?.field === field ? sortState.order : null,
  })

  const textFilterProps = (key: TextColumnFilterKey, placeholder: string) => ({
    filterDropdown: makeTextFilterDropdown(key, placeholder),
    filterIcon,
    filtered: Boolean((columnFilters[key] as string | undefined)?.trim()),
  })

  const numberFilterProps = (key: NumberColumnFilterKey) => ({
    filterDropdown: makeNumberRangeFilterDropdown(key),
    filterIcon,
    filtered: hasNumberRangeFilter(columnFilters[key] as ContainerDetailNumberRangeFilter | undefined),
  })

  const enumFilterProps = (key: EnumColumnFilterKey, options: { value: string; label: string }[]) => ({
    filterDropdown: makeEnumFilterDropdown(key, options),
    filterIcon,
    filtered: Boolean((columnFilters[key] as string[] | undefined)?.length),
  })

  const handleTableChange = (_pagination: unknown, _filters: Record<string, unknown>, sorter: SorterResult<ContainerDetail> | SorterResult<ContainerDetail>[]) => {
    const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
    const nextSortField = nextSorter?.columnKey ?? nextSorter?.field

    if (isContainerDetailSortField(nextSortField) && (nextSorter.order === 'ascend' || nextSorter.order === 'descend')) {
      setSortState({ field: nextSortField, order: nextSorter.order })
      return
    }

    setSortState(DEFAULT_CONTAINER_DETAIL_SORT)
  }

  const loadNextDetailChunk = async () => {
    if (detailLoading || detailLoadingMore || !detailHasMore) return
    await loadDetailChunk(detailPageNumber + 1, 'append')
  }

  const handleDetailTableScroll = (event: UIEvent<HTMLDivElement>) => {
    const target = event.currentTarget
    lastDetailTableScrollTopRef.current = target.scrollTop
    if (target.scrollTop + target.clientHeight >= target.scrollHeight - 96) {
      void loadNextDetailChunk()
    }
  }

  const handleWarehouseStatusChange = async (row: ContainerDetail, isActive: boolean) => {
    if (!access.canEditContainer) return
    const productCode = getContainerDetailProductCode(row)
    if (!productCode) {
      message.warning(t('containers.messages.selectedProductsMissingCode'))
      return
    }

    const previousStatuses = rows
      .filter((item) => getContainerDetailProductCode(item) === productCode)
      .map((item) => ({ key: rowKey(item), warehouseIsActive: item.warehouseIsActive }))

    setPendingWarehouseStatusCodes((codes) => new Set(codes).add(productCode))
    setRows((items) => applyContainerDetailWarehouseStatusByProductCodes(items, [productCode], isActive))

    try {
      const result = await bulkSetStatus([productCode], isActive)
      const failureMessage = getContainerDetailWarehouseActionFailureMessage(result, t('containers.messages.batchActiveFailed'))
      if (failureMessage) {
        setRows((items) => rollbackContainerDetailWarehouseStatuses(items, previousStatuses, rowKey))
        message.error(failureMessage)
        return
      }
      message.success(t(isActive ? 'containers.messages.productsActivated' : 'containers.messages.productsDeactivated', { count: 1 }))
    } catch (error) {
      setRows((items) => rollbackContainerDetailWarehouseStatuses(items, previousStatuses, rowKey))
      message.error(error instanceof Error ? error.message : t('containers.messages.batchActiveFailed'))
    } finally {
      setPendingWarehouseStatusCodes((codes) => {
        const next = new Set(codes)
        next.delete(productCode)
        return next
      })
    }
  }

  const renderProductTypeTag = (row: ContainerDetail) => {
    const productType = getContainerDetailProductType(row)
    const label = getProductTypeLabel(productType, t)
    const canOpenSetCodes = productType === '套装商品' && Boolean(getContainerDetailProductCode(row))

    return (
      <Tag
        color={getProductTypeTagColor(productType)}
        className={canOpenSetCodes ? 'container-detail-product-type-tag-clickable' : undefined}
        role={canOpenSetCodes ? 'button' : undefined}
        tabIndex={canOpenSetCodes ? 0 : undefined}
        onClick={canOpenSetCodes ? () => openSetCodeModal(row) : undefined}
        onKeyDown={canOpenSetCodes ? (event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            openSetCodeModal(row)
          }
        } : undefined}
      >
        {label}
      </Tag>
    )
  }

  const setCodeColumns: ColumnsType<ContainerDomesticSetCodeItem> = [
    {
      title: '套装货号',
      dataIndex: 'setItemNumber',
      width: 140,
      render: (value) => value || '--',
    },
    {
      title: '条码',
      dataIndex: 'barcode',
      width: 170,
      render: (value) => value || '--',
    },
    {
      title: '价格',
      dataIndex: 'retailPrice',
      width: 120,
      align: 'right',
      render: (_, item) => {
        const key = getSetCodeRowKey(item)
        const edit = setCodePriceEdits[key]
        return (
          <InputNumber
            min={0}
            prefix="$"
            precision={2}
            disabled={!access.canEditContainer}
            style={{ width: 104 }}
            value={edit?.retailPrice !== undefined ? edit.retailPrice : item.retailPrice}
            onChange={(value) => patchSetCodeRetailPriceEdit(item, value == null ? null : Number(value))}
          />
        )
      },
    },
    {
      title: '进货价',
      dataIndex: 'purchasePrice',
      width: 120,
      align: 'right',
      render: (_, item) => {
        const key = getSetCodeRowKey(item)
        const edit = setCodePriceEdits[key]
        return (
          <InputNumber
            min={0}
            prefix="$"
            precision={2}
            disabled={!access.canEditContainer}
            style={{ width: 104 }}
            value={edit?.purchasePrice !== undefined ? edit.purchasePrice : item.purchasePrice}
            onChange={(value) => patchSetCodePriceEdit(item, { purchasePrice: value == null ? null : Number(value) })}
          />
        )
      },
    },
  ]

  const readonlyOemPriceColumn: ColumnsType<ContainerDetail>[number] = {
    // 只读快览列使用有效贴牌价：仓库商品优先，明细贴牌价兜底。
    key: 'readonlyOemPrice',
    title: renderCompactHeader(t('containers.fields.oemPrice')),
    width: 96,
    align: 'right',
    render: (_, row) => renderOemPriceCell(row),
  }

  const baseColumns: ColumnsType<ContainerDetail> = [
    { key: 'index', title: renderCompactHeader(t('containers.columns.index')), width: 56, fixed: 'left', render: (_v, _r, index) => renderNumericCell(index + 1) },
    {
      key: 'image',
      title: renderCompactHeader(t('containers.columns.image')),
      width: 64,
      fixed: 'left',
      render: (_, row) => {
        const imageUrl = getContainerDetailImageUrl(row)

        return imageUrl ? (
          <Image
            className="container-detail-product-image"
            width={40}
            height={40}
            src={imageUrl}
            alt={row.商品信息?.货号 || row.商品信息?.商品名称 || ''}
            preview={{ mask: t('containers.actions.previewImage', '查看大图') }}
          />
        ) : (
          <span style={{ color: '#999' }}>{t('containers.empty.noImage')}</span>
        )
      },
    },
    {
      title: renderColumnTitle('itemNumber', t('containers.fields.itemNumber')),
      width: 130,
      fixed: 'left',
      ...makeSortProps('itemNumber'),
      ...textFilterProps('itemNumber', t('containers.placeholders.filterItemNumber')),
      render: (_, row) => <CopyableText value={getContainerDetailItemNumber(row)} maxWidth={90} />,
    },
    {
      title: renderColumnTitle('englishName', t('containers.fields.englishName')),
      width: 180,
      ...makeSortProps('englishName'),
      ...textFilterProps('englishName', t('containers.placeholders.filterEnglishName', '英文名称过滤')),
      render: (_, row) => access.canEditContainer ? (
        <Input.TextArea
          ref={(cell) => setEditableCellRef(rowKey(row), 'englishName', cell)}
          className="container-detail-english-name-input"
          value={getContainerDetailEnglishName(row) ?? ''}
          autoSize={{ minRows: 1, maxRows: 2 }}
          style={{ resize: 'none' }}
          onChange={(event) => patchRow(rowKey(row), { 英文名称: event.target.value })}
          onBlur={(event) => void saveRowPatch(row, { 英文名称: event.target.value }).catch(handleDetailSaveError)}
          onKeyDown={(event) => handleEditableCellKeyDown(row, 'englishName', event)}
        />
      ) : <TwoLineText value={getContainerDetailEnglishName(row)} />,
    },
    {
      key: 'categoryName',
      title: renderCompactHeader(t('containers.fields.category', '分类')),
      width: 120,
      render: (_, row) => renderContainerDetailCategoryCell(row, categoryLookup, i18n.language),
    },
    {
      title: renderColumnTitle('containerPieces', t('containers.fields.containerPieces')),
      dataIndex: '装柜件数',
      width: 76,
      align: 'right',
      ...makeSortProps('containerPieces'),
      ...numberFilterProps('containerPieces'),
      render: (v) => renderNumericCell(v ?? '--'),
    },
    {
      title: renderColumnTitle('containerQuantity', t('containers.fields.containerQuantity')),
      dataIndex: '装柜数量',
      width: 76,
      align: 'right',
      ...makeSortProps('containerQuantity'),
      ...numberFilterProps('containerQuantity'),
      render: (v) => renderNumericCell(v ?? '--'),
    },
    {
      title: renderColumnTitle('domesticPrice', t('containers.fields.domesticPrice')),
      dataIndex: '国内价格',
      width: 86,
      align: 'right',
      ...makeSortProps('domesticPrice'),
      ...numberFilterProps('domesticPrice'),
      render: (v) => renderNumericCell(formatCurrency(v, '¥')),
    },
    {
      title: renderColumnTitle('transportCost', t('containers.fields.transportCost')),
      dataIndex: '运输成本',
      width: 86,
      align: 'right',
      ...makeSortProps('transportCost'),
      ...numberFilterProps('transportCost'),
      render: (v) => renderNumericCell(formatCurrency(v, '$')),
    },
    {
      title: renderColumnTitle('floatRate', t('containers.fields.floatRate')),
      dataIndex: '调整浮率',
      width: 96,
      align: 'right',
      ...makeSortProps('floatRate'),
      ...numberFilterProps('floatRate'),
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(rowKey(row), 'floatRate', cell)}
            value={row.调整浮率}
            keyboard={false}
            precision={2}
            style={{ width: 78 }}
            onChange={(value) => patchRow(rowKey(row), { 调整浮率: value == null ? undefined : Number(value) })}
            onBlur={(event) => {
              const value = event.target.value ? Number(event.target.value) : undefined
              void saveFloatRatePatch(row, value).catch(handleDetailSaveError)
            }}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'floatRate', event)}
          />
        ) : renderNumericCell(formatNumber(row.调整浮率, 2)),
    },
    {
      title: renderColumnTitle('middlePackQuantity', t('containers.fields.middlePackQuantity', '中包数')),
      dataIndex: '中包数',
      width: 76,
      align: 'right',
      ...makeSortProps('middlePackQuantity'),
      ...numberFilterProps('middlePackQuantity'),
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(rowKey(row), 'middlePackQuantity', cell)}
            value={row.中包数}
            keyboard={false}
            min={0}
            precision={0}
            style={{ width: 68 }}
            onChange={(value) => patchRow(rowKey(row), { 中包数: value == null ? undefined : Number(value) })}
            onBlur={(event) => void saveRowPatch(row, { 中包数: event.target.value ? Number(event.target.value) : undefined }).catch(handleDetailSaveError)}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'middlePackQuantity', event)}
          />
        ) : renderNumericCell(row.中包数 ?? '--'),
    },
    {
      title: renderColumnTitle('warehouseImportPrice', t('containers.fields.warehouseImportPrice', '仓库当前进货价格')),
      dataIndex: 'warehouseImportPrice',
      width: 112,
      align: 'right',
      ...makeSortProps('warehouseImportPrice'),
      ...numberFilterProps('warehouseImportPrice'),
      render: (v) => renderNumericCell(formatCurrency(v, '$')),
    },
    {
      title: renderColumnTitle('importPrice', t('containers.fields.importPrice')),
      dataIndex: '进口价格',
      width: 96,
      align: 'right',
      ...makeSortProps('importPrice'),
      ...numberFilterProps('importPrice'),
      render: (_value, row) =>
        access.canEditContainer
          ? renderImportPriceCell(row, (
            <InputNumber
              ref={(cell) => setEditableCellRef(rowKey(row), 'importPrice', cell)}
              value={row.进口价格}
              keyboard={false}
              min={0}
              prefix="$"
              precision={2}
              style={{ width: 78 }}
              onChange={(value) => patchRow(rowKey(row), { 进口价格: value == null ? undefined : Number(value) })}
              onBlur={(event) => void saveRowPatch(row, { 进口价格: event.target.value ? Number(event.target.value) : undefined }).catch(handleDetailSaveError)}
              onKeyDown={(event) => handleEditableCellKeyDown(row, 'importPrice', event)}
            />
          ))
          : renderImportPriceCell(row),
    },
    {
      title: renderColumnTitle('oemPrice', t('containers.fields.oemPrice')),
      dataIndex: '贴牌价格',
      width: 96,
      align: 'right',
      ...makeSortProps('oemPrice'),
      ...numberFilterProps('oemPrice'),
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(rowKey(row), 'oemPrice', cell)}
            className={getOemPriceSourceClassName(row)}
            value={row.贴牌价格}
            keyboard={false}
            min={0}
            prefix="$"
            precision={2}
            style={{ width: 78 }}
            onChange={(value) => patchRow(rowKey(row), { 贴牌价格: value == null ? undefined : Number(value) })}
            onBlur={(event) => void saveRowPatch(row, { 贴牌价格: event.target.value ? Number(event.target.value) : undefined }).catch(handleDetailSaveError)}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'oemPrice', event)}
          />
        ) : renderOemPriceCell(row),
    },
    {
      title: renderColumnTitle('newProduct', t('containers.fields.newProduct')),
      width: 72,
      ...makeSortProps('newProduct'),
      ...enumFilterProps('newProductStates', (['new', 'existing'] as ContainerDetailNewProductFilter[]).map((value) => ({
        value,
        label: value === 'new' ? t('containers.tags.newProduct') : t('containers.tags.existingProduct'),
      }))),
      render: (_, row) => (row.是否新商品 ? <Tag color="blue">{t('containers.tags.new')}</Tag> : <Tag>{t('containers.tags.existing')}</Tag>),
    },
    {
      title: renderColumnTitle('productType', t('containers.fields.productType')),
      width: 92,
      ...makeSortProps('productType'),
      ...enumFilterProps('productTypes', (['normal', 'set', 'multi', 'setChild'] as ContainerDetailProductTypeFilter[]).map((value) => ({ value, label: getProductTypeFilterLabel(value, t) }))),
      render: (_, row) => renderProductTypeTag(row),
    },
    {
      title: renderColumnTitle('matchType', t('containers.fields.matchType')),
      width: 116,
      ...makeSortProps('matchType'),
      ...enumFilterProps('matchTypes', (['productCode', 'supplierItem', 'unmatched'] as ContainerDetailMatchTypeFilter[]).map((value) => ({
        value,
        label: getMatchTypeLabel(value, t),
      }))),
      render: (_, row) => {
        const matchType = getContainerDetailMatchType(row)
        return <Tag color={getMatchTypeTagColor(matchType)}>{getMatchTypeLabel(matchType, t)}</Tag>
      },
    },
    {
      title: renderColumnTitle('barcode', t('containers.fields.barcode')),
      width: 170,
      ...makeSortProps('barcode'),
      ...textFilterProps('barcode', t('containers.placeholders.filterBarcode', '条码过滤')),
      render: (_, row) => {
        const barcode = getContainerDetailBarcode(row)

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
    ...(showReadonlyOemPrice ? [readonlyOemPriceColumn] : []),
    {
      title: renderColumnTitle('productName', t('containers.fields.productName')),
      width: 180,
      ...makeSortProps('productName'),
      ...textFilterProps('productName', t('containers.placeholders.filterProductName', '商品名称过滤')),
      render: (_, row) => {
        const key = rowKey(row)
        if (access.canEditContainer && editingProductNameRowKey === key) {
          return (
            <Input.TextArea
              autoFocus
              className="container-detail-product-name-input"
              value={editingProductNameValue}
              autoSize={{ minRows: 1, maxRows: 2 }}
              style={{ resize: 'none' }}
              onChange={(event) => setEditingProductNameValue(event.target.value)}
              onBlur={() => handleProductNameEditBlur(row)}
              onKeyDown={(event) => {
                if (event.key === 'Escape') {
                  event.preventDefault()
                  cancelEditingProductName()
                  return
                }
                if (event.key === 'Enter' && !event.shiftKey) {
                  event.preventDefault()
                  void commitProductNameEdit(row).catch(handleDetailSaveError)
                }
              }}
            />
          )
        }

        return (
          <div
            className={access.canEditContainer ? 'container-detail-product-name-editable' : undefined}
            onDoubleClick={() => startEditingProductName(row)}
          >
            <TwoLineText value={getContainerDetailProductName(row)} />
          </div>
        )
      },
    },
    {
      title: renderColumnTitle('warehouseStatus', t('containers.fields.warehouseStatus')),
      width: 100,
      ...makeSortProps('warehouseStatus'),
      ...enumFilterProps('warehouseStatus', (['active', 'inactive'] as ContainerDetailWarehouseStatusFilter[]).map((value) => ({
        value,
        label: value === 'active' ? t('common.activeUpper') : t('common.inactiveUpper'),
      }))),
      render: (_, row) => {
        const isActive = getContainerDetailWarehouseStatusFilterKey(row) === 'active'
        const productCode = getContainerDetailProductCode(row)
        const isWarehouseStatusPending = productCode ? pendingWarehouseStatusCodes.has(productCode) : false

        return access.canEditContainer ? (
          <Tooltip title={!productCode ? t('containers.messages.selectedProductsMissingCode') : ''}>
            <Switch
              size="small"
              checked={isActive}
              checkedChildren={t('common.activeUpper')}
              unCheckedChildren={t('common.inactiveUpper')}
              loading={isWarehouseStatusPending}
              disabled={!productCode || isWarehouseStatusPending}
              onChange={(checked) => void handleWarehouseStatusChange(row, checked)}
            />
          </Tooltip>
        ) : (
          <Tag color={isActive ? 'success' : 'default'}>{isActive ? t('common.activeUpper') : t('common.inactiveUpper')}</Tag>
        )
      },
    },
    {
      title: renderColumnTitle('remark', t('containers.fields.remark')),
      width: 160,
      ...makeSortProps('remark'),
      ...textFilterProps('remark', t('containers.placeholders.filterRemark', '备注过滤')),
      render: (_, row) =>
        access.canEditContainer ? (
          <Input
            ref={(cell) => setEditableCellRef(rowKey(row), 'remark', cell)}
            value={row.备注 ?? ''}
            onChange={(event) => patchRow(rowKey(row), { 备注: event.target.value })}
            onBlur={(event) => void saveRowPatch(row, { 备注: event.target.value }).catch(handleDetailSaveError)}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'remark', event)}
          />
        ) : row.备注 || '--',
    },
  ]

  const draggableColumnKeys = baseColumns.map((column) => String(column.key) as ContainerDetailTableColumnKey)
  const isColumnOrderCustomized = isContainerDetailColumnOrderCustomized(columnOrder, draggableColumnKeys)

  useEffect(() => {
    setColumnOrder((current) => {
      let savedOrder: unknown[] | null = null
      if (!current.length && typeof window !== 'undefined') {
        try {
          const raw = localStorage.getItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)
          savedOrder = raw ? JSON.parse(raw) : null
        } catch {
          savedOrder = null
        }
      }

      // 列顺序持久化只影响业务列；选择列仍交给 rowSelection，新增/废弃列在这里自动兼容。
      const nextOrder = mergeContainerDetailColumnOrder(current.length ? current : savedOrder, draggableColumnKeys)
      if (current.length === nextOrder.length && current.every((key, index) => key === nextOrder[index])) {
        return current
      }
      return nextOrder
    })
  }, [draggableColumnKeys.join('|')])

  const handleColumnDragEnd = ({ active, over }: DragEndEvent) => {
    if (!over || active.id === over.id) return
    setColumnOrder((current) => {
      const nextOrder = moveContainerDetailColumnOrder(current, active.id, over.id)
      try {
        localStorage.setItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY, JSON.stringify(nextOrder))
      } catch {
        // localStorage 不可用时不影响当前页面内拖拽排序。
      }
      return nextOrder
    })
  }

  const resetColumnOrder = () => {
    setColumnOrder(draggableColumnKeys)
    try {
      localStorage.removeItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)
    } catch {
      // localStorage 不可用时仍恢复当前页面内的默认列顺序。
    }
    message.success(t('containers.messages.columnOrderReset', '列顺序已恢复默认'))
  }

  const orderedBaseColumns = useMemo(() => {
    const activeOrder = columnOrder.length ? columnOrder : draggableColumnKeys
    const columnMap = new Map(baseColumns.map((column) => [String(column.key), column]))
    return activeOrder
      .map((key) => columnMap.get(key))
      .filter((column): column is ColumnsType<ContainerDetail>[number] => Boolean(column))
  }, [baseColumns, columnOrder, draggableColumnKeys])

  // 小屏竖屏时取消 AntD 固定列，避免固定选择列和左侧列占掉主要阅读空间。
  const columns = (viewport.isSmallPortrait
    ? orderedBaseColumns.map((column) => ({ ...column, fixed: undefined }))
    : orderedBaseColumns
  ).map((column) => ({
    ...column,
    onHeaderCell: () => ({
      'data-column-key': String(column.key),
    }),
  })) as ColumnsType<ContainerDetail>
  const isSmallScreen = viewport.isSmallPortrait || viewport.isSmallLandscape
  const tableScrollY = viewport.isSmallLandscape
    ? Math.max(180, Math.min(CONTAINER_DETAIL_TABLE_SCROLL_Y, viewport.height - 208))
    : viewport.isSmallPortrait
      ? Math.max(320, Math.min(CONTAINER_DETAIL_TABLE_SCROLL_Y, viewport.height - 260))
      : CONTAINER_DETAIL_TABLE_SCROLL_Y
  const pageClassName = [
    'container-detail-page',
    isSmallScreen ? 'container-detail-page-small' : '',
    viewport.isSmallPortrait ? 'container-detail-page-small-portrait' : '',
    viewport.isSmallLandscape ? 'container-detail-page-small-landscape' : '',
  ].filter(Boolean).join(' ')

  return (
    <PageContainer title={container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : t('menu.containerDetail')}>
      <Modal
        title={t('containers.modals.batchEditEnglishNameTitle')}
        open={batchEnglishNameModalOpen}
        okText={t('containers.actions.batchEditEnglishName')}
        confirmLoading={batchEnglishNameSaving}
        onOk={() => void submitBatchEditEnglishName()}
        onCancel={() => {
          setBatchEnglishNameModalOpen(false)
          setBatchEnglishName('')
        }}
      >
        <Input
          autoFocus
          value={batchEnglishName}
          placeholder={t('containers.placeholders.batchEnglishName')}
          onChange={(event) => setBatchEnglishName(event.target.value)}
          onPressEnter={() => void submitBatchEditEnglishName()}
        />
      </Modal>
      <Modal
        title={t('containers.modals.batchCategoryTitle', '批量设置分类')}
        open={batchCategoryOpen}
        width={640}
        destroyOnClose
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={batchCategorySaving}
        okButtonProps={{ disabled: !targetCategoryGuid || categoryLoading || !categories.length }}
        onCancel={() => {
          setBatchCategoryOpen(false)
          setTargetCategoryGuid(undefined)
        }}
        onOk={() => void handleBatchCategorySave()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text type="secondary">
            {t('containers.modals.batchCategoryContent', '请选择目标分类，确认后会把当前目标明细对应的仓库商品设置到该分类。')}
          </Typography.Text>
          {selectedTargetCategory ? (
            <Tag color="blue">
              {t('warehouse.categories.targetCategory', '目标分类')}: {selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)}
            </Tag>
          ) : null}
          <CategoryTreePicker
            categories={categories}
            selectedKey={targetCategoryGuid}
            expandedKeys={categoryExpandedKeys}
            onExpand={setCategoryExpandedKeys}
            onSelect={setTargetCategoryGuid}
            language={i18n.language}
            t={t}
            maxHeight={360}
          />
        </Space>
      </Modal>
      <div className={pageClassName}>
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
              <Descriptions.Item label={t('containers.fields.status')}>
                {headerEditing ? (
                  <Select
                    value={headerForm.状态}
                    style={{ minWidth: 120 }}
                    options={containerStatusOptions.map((option) => ({
                      value: option.value,
                      label: t(`containers.status.${option.labelKey}`),
                    }))}
                    onChange={(value) => setHeaderForm((prev) => ({ ...prev, 状态: value }))}
                  />
                ) : getStatusTag(container?.状态, t)}
              </Descriptions.Item>
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
                    mode="multiple"
                    value={selectedTagFilters}
                    allowClear
                    maxTagCount="responsive"
                    placeholder={t('containers.filters.allTags')}
                    style={{ width: 220 }}
                    onChange={setTagFiltersFromSelect}
                    options={tagSelectOptions}
                  />
                  <Select
                    value={categoryFilterValue}
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    loading={categoryLoading}
                    placeholder={t('containers.filters.allCategories', '全部分类')}
                    style={{ width: 220 }}
                    options={categoryFilterOptions}
                    onChange={(value) => setCategoryFilterValue(value || CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)}
                  />
                  <Typography.Text type="secondary">
                    {t('containers.text.loadedRows', '已加载 {{loaded}} / 共 {{total}} 条', {
                      loaded: rows.length,
                      total: detailItemsTotal,
                    })}
                    {filteredRows.length !== rows.length ? ` ${t('containers.text.visibleRows', '当前可见 {{count}} 条', { count: filteredRows.length })}` : ''}
                    {detailLoadingMore ? ` ${t('common.loading', '加载中')}` : ''}
                  </Typography.Text>
                  {hasActiveColumnState ? (
                    <Button size="small" onClick={() => {
                      setColumnFilters({})
                      setSortState(DEFAULT_CONTAINER_DETAIL_SORT)
                    }}>
                      {t('containers.actions.clearColumnFilters', '清空列过滤')}
                    </Button>
                  ) : null}
                  <Space size={6}>
                    <Typography.Text type="secondary">{t('containers.actions.showReadonlyOemPrice', '只读贴牌价格')}</Typography.Text>
                    <Switch
                      size="small"
                      checked={showReadonlyOemPrice}
                      onChange={setShowReadonlyOemPrice}
                    />
                  </Space>
                </Space>
                <Space wrap>
                  <Button icon={<DownloadOutlined />} loading={exporting} onClick={() => void exportExcel()}>
                    {t('common.export')}
                  </Button>
                  <Dropdown
                    menu={{
                      items: [
                        { key: 'columns', label: t('containers.actions.selectExportColumns', '选择导出列') },
                      ],
                      onClick: ({ key }) => {
                        if (key === 'columns') setExportColumnModalOpen(true)
                      },
                    }}
                  >
                    <Button>{t('containers.actions.exportOptions', '导出选项')}</Button>
                  </Dropdown>
                  {isColumnOrderCustomized ? (
                    <Button icon={<ReloadOutlined />} onClick={resetColumnOrder}>
                      {t('containers.actions.resetColumns', '重置列')}
                    </Button>
                  ) : null}
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
                          { key: 'editEnglishName', label: t('containers.actions.batchEditEnglishName') },
                          { key: 'clearEnglishName', label: t('containers.actions.clearEnglishNames') },
                          ...(canBatchSetCategory
                            ? [{ key: 'batchCategory', icon: <AppstoreOutlined />, label: t('containers.actions.batchSetCategory', '批量分类') }]
                            : []),
                          ...(canCreateContainerProducts
                            ? [{ key: 'createNew', label: t('containers.actions.createNewProducts'), disabled: createProductsLoading || pendingDetailSaveCount > 0 }]
                            : []),
                          { key: 'updatePurchase', label: t('containers.actions.updateExistingPurchase') },
                          { key: 'active', label: t('containers.actions.batchActivate') },
                          { key: 'inactive', label: t('containers.actions.batchDeactivate') },
                        ],
                        onClick: ({ key }) => {
                          if (key === 'translate') void translateNames()
                          if (key === 'editEnglishName') openBatchEditEnglishName()
                          if (key === 'clearEnglishName') clearEnglishNames()
                          if (key === 'batchCategory') openBatchCategory()
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
                  <InputNumber value={batchFloatRate} placeholder={t('containers.fields.floatRate')} precision={2} onChange={setBatchFloatRate} />
                  <Button onClick={() => void applyFloatRate()}>{t('containers.actions.applyFloatRate')}</Button>
                  <Button loading={recalculateCostsLoading} onClick={() => void handleRecalculateCosts()}>重算成本</Button>
                  <Button loading={matchDomesticDataLoading} onClick={() => void handleMatchDomesticData()}>匹配国内数据</Button>
                  <InputNumber value={batchImportPrice} placeholder={t('containers.fields.importPrice')} min={0} prefix="$" precision={2} onChange={setBatchImportPrice} />
                  <InputNumber value={batchOemPrice} placeholder={t('containers.fields.oemPrice')} min={0} prefix="$" precision={2} onChange={setBatchOemPrice} />
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

              <DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>
                <SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>
                  <Table
                    key={`${containerGuid}-${detailTableRenderKey}`}
                    ref={detailTableRef}
                    className="container-detail-table"
                    rowKey={rowKey}
                    rowClassName={(_, index) => index % 2 === 1 ? 'container-detail-row-striped' : ''}
                    size="small"
                    components={{ header: { cell: DraggableHeaderCell } }}
                    columns={columns}
                    dataSource={displayRows}
                    loading={detailLoading && rows.length === 0}
                    rowSelection={{
                      selectedRowKeys,
                      onChange: setSelectedRowKeys,
                      fixed: !viewport.isSmallPortrait,
                      // 紧凑表格中默认选择列过窄，显式留出复选框点击空间。
                      columnWidth: CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH,
                    }}
                    pagination={false}
                    virtual
                    scroll={{ x: CONTAINER_DETAIL_TABLE_SCROLL_X, y: tableScrollY }}
                    onChange={handleTableChange}
                    onScroll={handleDetailTableScroll}
                    footer={() => (
                      <Space direction="vertical" size={2}>
                        <Typography.Text type="secondary">{t('containers.formulas.transportCost', '运输成本 = 运费 × 明细体积 ÷ 装柜数量 ÷ 总体积')}</Typography.Text>
                        <Typography.Text type="secondary">{t('containers.formulas.importPrice', '进口价格 = ((国内价格 ÷ 汇率 + 运输成本) × 调整浮率 × 10) ÷ 11')}</Typography.Text>
                      </Space>
                    )}
                  />
                </SortableContext>
              </DndContext>
            </Space>
          </Card>
        </Space>
      </Spin>
      <Modal
        title={`套装多码价格 - ${setCodeModalRow ? getContainerDetailItemNumber(setCodeModalRow) ?? getContainerDetailProductCode(setCodeModalRow) ?? '' : ''}`}
        open={setCodeModalOpen}
        width={680}
        okText="保存"
        cancelText={t('common.cancel')}
        okButtonProps={{
          disabled: !access.canEditContainer || changedSetCodePriceItems.length === 0,
          loading: setCodeSaving,
        }}
        onOk={() => void saveSetCodePrices()}
        onCancel={closeSetCodeModal}
        destroyOnClose
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          {setCodeModalRow ? (
            <Typography.Text type="secondary">
              {getContainerDetailProductName(setCodeModalRow) ?? '--'}
            </Typography.Text>
          ) : null}
          <Table
            rowKey={getSetCodeRowKey}
            size="small"
            columns={setCodeColumns}
            dataSource={setCodeItems}
            loading={setCodeLoading}
            pagination={false}
            scroll={{ x: 520 }}
          />
        </Space>
      </Modal>
      <Modal
        title={t('containers.modals.exportColumnsTitle', '选择导出列')}
        open={exportColumnModalOpen}
        okText={t('containers.actions.exportSelectedColumns', '按所选列导出')}
        cancelText={t('common.cancel')}
        okButtonProps={{ disabled: selectedExportColumnKeys.length === 0, loading: exporting }}
        onOk={() => {
          setExportColumnModalOpen(false)
          void exportExcel(selectedExportColumnKeys)
        }}
        onCancel={() => setExportColumnModalOpen(false)}
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Typography.Text type="secondary">
            {selectedRowKeys.length
              ? t('containers.text.exportSelectedRowsHint', '将导出已选择的 {{count}} 个商品。', { count: selectedRowKeys.length })
              : t('containers.text.exportAllRowsHint', '未选择商品时，将按当前筛选和排序导出全部匹配商品。')}
          </Typography.Text>
          <Space wrap>
            <Button size="small" onClick={() => setSelectedExportColumnKeys(CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => column.key))}>
              {t('containers.actions.selectAllExportColumns', '全选')}
            </Button>
            <Button size="small" onClick={() => setSelectedExportColumnKeys(DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS)}>
              {t('containers.actions.resetDefaultExportColumns', '恢复默认')}
            </Button>
          </Space>
          <Checkbox.Group
            value={selectedExportColumnKeys}
            options={exportColumnOptions}
            onChange={(values) => setSelectedExportColumnKeys(values as ContainerDetailExportColumnKey[])}
          />
        </Space>
      </Modal>
      </div>
    </PageContainer>
  )
}
