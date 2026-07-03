import {
  ArrowLeftOutlined,
  AppstoreOutlined,
  CheckCircleOutlined,
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
  Radio,
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
import { useEffect, useMemo, useRef, useState, type CSSProperties, type HTMLAttributes, type Key, type KeyboardEvent, type PointerEvent as ReactPointerEvent, type ReactNode, type UIEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import {
  applyContainerFloatRateByScope,
  applyContainerPricesByScope,
  backfillContainerLastPricesByScope,
  batchDeleteDetails,
  batchUpdateDetails,
  getContainerDetail,
  queryContainerProducts,
  recalculateContainerCostsByScope,
  translateHqProductNamesByContainerNumber,
  updateContainer,
} from '../../../services/containerService'
import {
  buildContainerCreateProductsOperationId,
  buildContainerSubmitOperationId,
  createContainerProductCreationJob,
  createContainerSubmitJob,
  waitForContainerProductCreationJob,
  waitForContainerSubmitJob,
  type ContainerProductCreationJob,
  type ContainerProductCreationResultItem,
} from '../../../services/containerProductCreationService'
import { exportContainerDetailsToExcel, exportContainerDetailsToPdf, type ContainerDetailExportItem, type ContainerExportOptions } from '../../../services/exportService'
import {
  buildPushProductsToHqOperationId,
  createPushProductsToHqJob,
  getPushProductsToHqJob,
  type PushProductsToHqJobResult,
} from '../../../services/posProductService'
import { createHqSyncJobPoller } from '../../../services/productHqSyncPolling'
import { upsertForActiveStores as upsertMultiCodeForActiveStores, type StoreMultiCodePriceUpsertActiveItem } from '../../../services/storeMultiCodePriceService'
import { upsertForActiveStores as upsertRetailForActiveStores, type StoreRetailPriceUpsertActiveItem } from '../../../services/storeRetailPriceService'
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
  type WarehouseProductBatchUpdateItem,
} from '../../../services/warehouseProductService'
import { useAuthStore } from '../../../store/auth'
import { useTabsStore } from '../../../store/tabs'
import type { ContainerDetail, ContainerDetailBatchScope, ContainerDomesticSetCodeItem, ContainerMain, HqTranslationResult, UpdateContainerDetailRequest, UpdateContainerRequest } from '../../../types/container'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'
import {
  applyContainerDetailEnglishNameUpdates,
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
  calculateContainerDetailImportPrice,
  calculateContainerDetailTotalAmount,
  calculateContainerDetailTotalVolume,
  calculateContainerDetailTransportCost,
  calculateContainerDetailUnitTransportCost,
  canUseContainerDetailLocalTagFilters,
  DEFAULT_CONTAINER_DETAIL_FLOAT_RATE,
  getContainerDetailCostMissingFields,
  getContainerDetailBatchCategoryProductCodes,
  buildContainerDetailTranslationUpdates,
  calculateContainerDetailTableScrollY,
  countContainerDetailInvalidTranslationResults,
  extractPushToHqErrorResult,
  findContainerDetailRowsMissingCreateProductRetailPrice,
  findContainerDetailRowsMissingProductName,
  getContainerDetailEditableColumnKeysInOrder,
  getContainerDetailExportColumns,
  getContainerDetailBarcode,
  getContainerDetailCategoryGuid,
  getContainerDetailEnglishName,
  getContainerDetailImageUrl,
  getContainerDetailItemNumber,
  getContainerDetailLastImportPrice,
  getContainerDetailLastOemPrice,
  getContainerDetailMatchType,
  getContainerDetailProductCode,
  getContainerDetailProductName,
  getContainerDetailProductType,
  getContainerDetailTranslationSource,
  getContainerDetailWarehouseActionFailureMessage,
  getContainerDetailWarehouseStatusFilterKey,
  getNextUpdateFieldSelection,
  getNextContainerDetailEditableCell,
  getUpdateFieldSelectionState,
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
  CONTAINER_DETAIL_EXPORT_COLUMNS,
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS,
  type ContainerDetailEditableCellDirection,
  type ContainerDetailColumnFilters,
  type ContainerDetailCostMissingField,
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
import { buildWarehouseCategoryLookup, formatWarehouseCategoryNodeName, getWarehouseProductCategoryTooltip } from '../Products/categoryPath'
import CategoryTreePicker from '../Products/CategoryTreePicker'
import type { PushProductsToHqResult, PushProductsToHqUpdateField } from '../../../types/posProduct'
import ContainerTagFilters from './ContainerTagFilters'
import useContainerSetCode from './useContainerSetCode'
import {
  collectCategoryExpandedKeys,
  findWarehouseCategory,
  renderColumnTitle,
  renderCompactHeader,
  renderContainerDetailCategoryCell,
  renderImportPriceCell,
  renderNumericCell,
  renderOemPriceCell,
} from './ContainerDetailColumns'
import './index.css'

type TextColumnFilterKey = 'itemNumber' | 'barcode' | 'productName' | 'englishName' | 'remark'
type NumberColumnFilterKey = 'containerPieces' | 'middlePackQuantity' | 'containerQuantity' | 'packingQuantity' | 'unitVolume' | 'domesticPrice' | 'floatRate' | 'transportCost' | 'unitTransportCost' | 'warehouseImportPrice' | 'lastOEMPrice' | 'importPrice' | 'oemPrice'
type EnumColumnFilterKey = 'productTypes' | 'newProductStates' | 'matchTypes' | 'warehouseStatus'
type PendingContainerDetailPricePatch = Pick<UpdateContainerDetailRequest, 'hguid'> & Partial<Pick<UpdateContainerDetailRequest, '进口价格' | '贴牌价格'>>
type PendingContainerDetailPricePatchMap = Record<string, PendingContainerDetailPricePatch>
type ContainerDetailExportFormat = 'excel' | 'pdf'
type ContainerExistingProductUpdateField =
  | 'domesticPrice'
  | 'importPrice'
  | 'oemPrice'
  | 'volume'
  | 'storePurchasePrice'
  | 'storeRetailPrice'
  | 'storeMultiCodePurchasePrice'
  | 'storeMultiCodeRetailPrice'

interface UpdateFieldOption<T extends string> {
  value: T
  labelKey: string
  fallbackLabel: string
}

type BatchActionConfirmOptions = {
  danger?: boolean
  extra?: ReactNode
  beforeConfirm?: () => boolean
}

interface UpdateFieldSelectorProps<T extends string> {
  t: TFunction
  fields: readonly UpdateFieldOption<T>[]
  defaultFields: T[]
  onChange: (values: T[]) => void
  hint?: ReactNode
}

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

const containerExistingProductUpdateFields: readonly UpdateFieldOption<ContainerExistingProductUpdateField>[] = [
  { value: 'domesticPrice', labelKey: 'containers.updateFields.domesticPrice', fallbackLabel: '国内价格（仓库主表）' },
  { value: 'importPrice', labelKey: 'containers.updateFields.importPrice', fallbackLabel: '进口价（商品/仓库主表）' },
  { value: 'oemPrice', labelKey: 'containers.updateFields.oemPrice', fallbackLabel: '贴牌价（仓库主表）' },
  { value: 'volume', labelKey: 'containers.updateFields.volume', fallbackLabel: '单件体积（仓库主表）' },
  { value: 'storePurchasePrice', labelKey: 'containers.updateFields.storePurchasePrice', fallbackLabel: '分店进货价' },
  { value: 'storeRetailPrice', labelKey: 'containers.updateFields.storeRetailPrice', fallbackLabel: '分店零售价' },
  { value: 'storeMultiCodePurchasePrice', labelKey: 'containers.updateFields.storeMultiCodePurchasePrice', fallbackLabel: '分店多码进货价' },
  { value: 'storeMultiCodeRetailPrice', labelKey: 'containers.updateFields.storeMultiCodeRetailPrice', fallbackLabel: '分店多码零售价' },
]

const defaultContainerExistingProductUpdateFields: ContainerExistingProductUpdateField[] = [
  'importPrice',
  'oemPrice',
  'storePurchasePrice',
  'storeRetailPrice',
  'storeMultiCodePurchasePrice',
  'storeMultiCodeRetailPrice',
]

const pushToHqUpdateFields = [
  { value: 'itemNumber', labelKey: 'containers.updateFields.hqItemNumber', fallbackLabel: '货号' },
  { value: 'barcode', labelKey: 'containers.updateFields.hqBarcode', fallbackLabel: '条码' },
  { value: 'productName', labelKey: 'containers.updateFields.hqProductName', fallbackLabel: '商品名称' },
  { value: 'englishName', labelKey: 'containers.updateFields.hqEnglishName', fallbackLabel: '英文名称' },
  { value: 'image', labelKey: 'containers.updateFields.hqImage', fallbackLabel: '商品图片' },
  { value: 'purchasePrice', labelKey: 'containers.updateFields.hqPurchasePrice', fallbackLabel: '商品字典进货价' },
  { value: 'retailPrice', labelKey: 'containers.updateFields.hqRetailPrice', fallbackLabel: '商品字典零售价' },
  { value: 'middlePackQuantity', labelKey: 'containers.updateFields.hqMiddlePackQuantity', fallbackLabel: '中包数量' },
  { value: 'supplierCode', labelKey: 'containers.updateFields.hqSupplierCode', fallbackLabel: '供应商编码' },
  { value: 'storePurchasePrice', labelKey: 'containers.updateFields.hqStorePurchasePrice', fallbackLabel: 'HQ 分店进货价' },
  { value: 'storeRetailPrice', labelKey: 'containers.updateFields.hqStoreRetailPrice', fallbackLabel: 'HQ 分店零售价' },
  { value: 'inventoryDomesticPrice', labelKey: 'containers.updateFields.hqInventoryDomesticPrice', fallbackLabel: 'HQ 库存国内价' },
  { value: 'inventoryImportPrice', labelKey: 'containers.updateFields.hqInventoryImportPrice', fallbackLabel: 'HQ 库存进口价' },
  { value: 'inventoryOemPrice', labelKey: 'containers.updateFields.hqInventoryOemPrice', fallbackLabel: 'HQ 库存贴牌价' },
  { value: 'productSetCodes', labelKey: 'containers.updateFields.hqProductSetCodes', fallbackLabel: 'HQ 一品多码' },
  { value: 'storeMultiCodes', labelKey: 'containers.updateFields.hqStoreMultiCodes', fallbackLabel: 'HQ 分店一品多码' },
] as const satisfies readonly UpdateFieldOption<PushProductsToHqUpdateField>[]

type PushToHqUpdateFieldOptionValue = (typeof pushToHqUpdateFields)[number]['value']
type MissingPushToHqUpdateFieldOption = Exclude<PushProductsToHqUpdateField, PushToHqUpdateFieldOptionValue>
const assertAllPushToHqUpdateFieldsCovered: Record<MissingPushToHqUpdateFieldOption, never> = {}
void assertAllPushToHqUpdateFieldsCovered

const defaultPushToHqUpdateFields: PushProductsToHqUpdateField[] = pushToHqUpdateFields.map((field) => field.value)

function UpdateFieldSelector<T extends string>({
  t,
  fields,
  defaultFields,
  onChange,
  hint,
}: UpdateFieldSelectorProps<T>) {
  const allValues = useMemo(() => fields.map((field) => field.value), [fields])
  const [selectedFields, setSelectedFields] = useState<T[]>(defaultFields)
  const { isAllSelected, isPartiallySelected } = getUpdateFieldSelectionState(selectedFields, allValues)

  const updateSelectedFields = (values: T[]) => {
    setSelectedFields(values)
    onChange(values)
  }

  return (
    <Space direction="vertical" size={8}>
      <Typography.Text strong>
        {t('containers.updateFields.title', '选择要更新的字段')}
      </Typography.Text>
      {hint}
      <Checkbox
        indeterminate={isPartiallySelected}
        checked={isAllSelected}
        onChange={(event) => updateSelectedFields(getNextUpdateFieldSelection(event.target.checked, allValues))}
      >
        {t('common.selectAll', '全选')}
      </Checkbox>
      <Checkbox.Group
        value={selectedFields}
        onChange={(values) => updateSelectedFields(values.map(String) as T[])}
      >
        <Space direction="vertical" size={4}>
          {fields.map((field) => (
            <Checkbox key={field.value} value={field.value}>
              {t(field.labelKey, field.fallbackLabel)}
            </Checkbox>
          ))}
        </Space>
      </Checkbox.Group>
    </Space>
  )
}

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
  const { t } = useTranslation()

  if (!value) {
    return <>--</>
  }

  return (
    <Space size={4} wrap={false} className="container-detail-nowrap container-detail-copyable">
      <Typography.Text style={maxWidth ? { maxWidth } : undefined} ellipsis={maxWidth ? { tooltip: value } : false}>
        {value}
      </Typography.Text>
      <Tooltip title={t('common.copy', 'Copy')}>
        <Button
          size="small"
          type="text"
          aria-label={t('common.copyValue', 'Copy {{value}}', { value })}
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

// TwoLineText — 表格密集显示专用：关键文本限制两行，数字列保持单行便于快速扫读。
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



const CONTAINER_DETAIL_TABLE_SCROLL_X = 2440
const CONTAINER_DETAIL_TABLE_SCROLL_Y = 620
const CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH = 56
const CONTAINER_DETAIL_PAGE_SIZE = 50
const CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.containerDetail.columnOrder.v3'
const CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY = 'hbweb_rv.containerDetail.columnWidths.v1'
const CONTAINER_DETAIL_MIN_COLUMN_WIDTH = 48
const CONTAINER_DETAIL_MAX_COLUMN_WIDTH = 420
const DEFAULT_CONTAINER_DETAIL_SORT: ContainerDetailSortState = { field: 'itemNumber', order: 'ascend' }
const CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS = ['englishName', 'packingQuantity', 'unitVolume', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const
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
type ContainerDetailColumnWidthMap = Partial<Record<ContainerDetailTableColumnKey, number>>
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

function clampContainerDetailColumnWidth(width: number) {
  return Math.max(CONTAINER_DETAIL_MIN_COLUMN_WIDTH, Math.min(CONTAINER_DETAIL_MAX_COLUMN_WIDTH, Math.round(width)))
}

function normalizeContainerDetailColumnWidths(value: unknown, allowedKeys: readonly ContainerDetailTableColumnKey[]): ContainerDetailColumnWidthMap {
  if (!value || typeof value !== 'object') {
    return {}
  }

  const allowedSet = new Set(allowedKeys)
  const nextWidths: ContainerDetailColumnWidthMap = {}
  Object.entries(value as Record<string, unknown>).forEach(([key, width]) => {
    if (!allowedSet.has(key as ContainerDetailTableColumnKey) || typeof width !== 'number' || !Number.isFinite(width)) {
      return
    }
    nextWidths[key as ContainerDetailTableColumnKey] = clampContainerDetailColumnWidth(width)
  })
  return nextWidths
}

function areContainerDetailColumnWidthsEqual(a: ContainerDetailColumnWidthMap, b: ContainerDetailColumnWidthMap) {
  const aKeys = Object.keys(a)
  const bKeys = Object.keys(b)
  return aKeys.length === bKeys.length && aKeys.every((key) => a[key as ContainerDetailTableColumnKey] === b[key as ContainerDetailTableColumnKey])
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
  'data-column-width'?: number
  onColumnResizeStart?: (columnKey: ContainerDetailTableColumnKey, width: number, event: ReactPointerEvent<HTMLSpanElement>) => void
}

function DraggableHeaderCell({ children, style, onColumnResizeStart, ...props }: DraggableHeaderCellProps) {
  const columnKey = props['data-column-key']
  const columnWidth = props['data-column-width']
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
    position: 'relative',
    zIndex: isDragging ? 3 : style?.zIndex,
    opacity: isDragging ? 0.85 : style?.opacity,
  }

  return (
    <th ref={setNodeRef} style={headerStyle} {...props}>
      <div className="container-detail-draggable-header" {...attributes} {...listeners}>
        {children}
      </div>
      <span
        className="container-detail-column-resize-handle"
        aria-hidden="true"
        onPointerDown={(event) => {
          event.preventDefault()
          event.stopPropagation()
          if (!columnKey || typeof columnWidth !== 'number') return
          onColumnResizeStart?.(columnKey as ContainerDetailTableColumnKey, columnWidth, event)
        }}
      />
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
  const updateTabTitle = useTabsStore((state) => state.updateTabTitle)
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
  const [pendingPricePatches, setPendingPricePatches] = useState<PendingContainerDetailPricePatchMap>({})
  const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])
  const [selectedTagFilters, setSelectedTagFilters] = useState<ContainerDetailTagFilter[]>([])
  const [categories, setCategories] = useState<WarehouseCategoryNode[]>([])
  const [categoryLoading, setCategoryLoading] = useState(false)
  const [categoryExpandedKeys, setCategoryExpandedKeys] = useState<string[]>([])
  const [columnFilters, setColumnFilters] = useState<ContainerDetailColumnFilters>({})
  // 默认按货号升序展示，保证每次打开货柜明细时列表顺序稳定。
  const [sortState, setSortState] = useState<ContainerDetailSortState>(DEFAULT_CONTAINER_DETAIL_SORT)
  const [columnOrder, setColumnOrder] = useState<ContainerDetailTableColumnKey[]>([])
  const [columnWidths, setColumnWidths] = useState<ContainerDetailColumnWidthMap>({})
  const [showReadonlyOemPrice, setShowReadonlyOemPrice] = useState(false)
  const [batchFloatRate, setBatchFloatRate] = useState<number | null>(null)
  const [batchImportPrice, setBatchImportPrice] = useState<number | null>(null)
  const [batchOemPrice, setBatchOemPrice] = useState<number | null>(null)
  const [batchFloatRateModalOpen, setBatchFloatRateModalOpen] = useState(false)
  const [batchFloatRateSaving, setBatchFloatRateSaving] = useState(false)
  const [batchPricesModalOpen, setBatchPricesModalOpen] = useState(false)
  const [batchPricesSaving, setBatchPricesSaving] = useState(false)
  const [batchModalTargetCount, setBatchModalTargetCount] = useState(0)
  const [batchModalScopeRows, setBatchModalScopeRows] = useState<ContainerDetail[]>([])
  const [batchEnglishName, setBatchEnglishName] = useState('')
  const [batchEnglishNameModalOpen, setBatchEnglishNameModalOpen] = useState(false)
  const [batchEnglishNameSaving, setBatchEnglishNameSaving] = useState(false)
  const [batchCategoryOpen, setBatchCategoryOpen] = useState(false)
  const [targetCategoryGuid, setTargetCategoryGuid] = useState<string>()
  const [batchCategorySaving, setBatchCategorySaving] = useState(false)
  const [rowCategoryOpen, setRowCategoryOpen] = useState(false)
  const [rowCategoryEditingRow, setRowCategoryEditingRow] = useState<ContainerDetail | null>(null)
  const [rowTargetCategoryGuid, setRowTargetCategoryGuid] = useState<string>()
  const [rowCategorySaving, setRowCategorySaving] = useState(false)
  const [editingProductNameRowKey, setEditingProductNameRowKey] = useState<string | null>(null)
  const [editingProductNameValue, setEditingProductNameValue] = useState('')
  const [exporting, setExporting] = useState(false)
  const [exportProgress, setExportProgress] = useState(0)
  const [exportColumnModalOpen, setExportColumnModalOpen] = useState(false)
  const [exportFormat, setExportFormat] = useState<ContainerDetailExportFormat>('excel')
  const [selectedExportColumnKeys, setSelectedExportColumnKeys] = useState<ContainerDetailExportColumnKey[]>(DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS)
  const [hqTranslating, setHqTranslating] = useState(false)
  const [pushToHqLoading, setPushToHqLoading] = useState(false)
  const [backfillLastPricesLoading, setBackfillLastPricesLoading] = useState(false)
  const [priceDetailsSaving, setPriceDetailsSaving] = useState(false)
  const [matchDomesticDataLoading, setMatchDomesticDataLoading] = useState(false)
  const [createProductsLoading, setCreateProductsLoading] = useState(false)
  const [submitContainerLoading, setSubmitContainerLoading] = useState(false)
  const [pendingDetailSaveCount, setPendingDetailSaveCount] = useState(0)
  const [pendingWarehouseStatusCodes, setPendingWarehouseStatusCodes] = useState<Set<string>>(() => new Set())
  // 套装码弹窗状态 — 由 useContainerSetCode hook 管理
  const {
    setCodeModalOpen,
    setCodeModalRow,
    setCodeItems,
    setCodeLoading,
    setCodeSaving,
    changedSetCodePriceItems,
    setCodeColumns,
    openSetCodeModal,
    closeSetCodeModal,
    saveSetCodePrices,
  } = useContainerSetCode({ canEditContainer: access.canEditContainer })
  const [detailTableRenderKey, setDetailTableRenderKey] = useState(0)
  const [gridContentElement, setGridContentElement] = useState<HTMLDivElement | null>(null)
  const [toolbarElement, setToolbarElement] = useState<HTMLDivElement | null>(null)
  const [tableRegionElement, setTableRegionElement] = useState<HTMLDivElement | null>(null)
  const [detailLayoutMetrics, setDetailLayoutMetrics] = useState({
    toolbarHeight: 0,
    tableChromeHeight: 0,
  })
  const pushToHqLoadingRef = useRef(false)
  const createProductsLoadingRef = useRef(false)
  const submitContainerLoadingRef = useRef(false)
  const detailAbortControllerRef = useRef<AbortController | null>(null)
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
    货柜编号?: string
    装柜日期?: Dayjs | null
    预计到岸日期?: Dayjs | null
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

  const containerDetailTabTitle = container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : undefined
  const containerDetailTabKey = containerGuid ? `/warehouse/container/detail/${containerGuid}` : undefined

  useEffect(() => {
    if (!active || !containerDetailTabKey || !containerDetailTabTitle) {
      return
    }

    // 隐藏的 KeepAlive 旧实例也会收到全局 URL 变化；只有当前激活页能写自己的 Tab 标题。
    updateTabTitle(containerDetailTabKey, containerDetailTabTitle)
  }, [active, containerDetailTabKey, containerDetailTabTitle, updateTabTitle])

  useEffect(() => {
    let frameId: number | null = null

    const measureDetailLayout = () => {
      const toolbarHeight = Math.ceil(toolbarElement?.getBoundingClientRect().height ?? 0)
      const tableHeaderHeight = Math.ceil(tableRegionElement?.querySelector('.ant-table-thead')?.getBoundingClientRect().height ?? 0)
      const tableFooterHeight = Math.ceil(tableRegionElement?.querySelector('.ant-table-footer')?.getBoundingClientRect().height ?? 0)
      const tableBodyElement = tableRegionElement?.querySelector('.ant-table-body') as HTMLElement | null
      const horizontalScrollbarHeight = tableBodyElement ? Math.max(0, tableBodyElement.offsetHeight - tableBodyElement.clientHeight) : 0
      const tableChromeHeight = tableHeaderHeight + tableFooterHeight + horizontalScrollbarHeight

      setDetailLayoutMetrics((current) => {
        if (
          current.toolbarHeight === toolbarHeight &&
          current.tableChromeHeight === tableChromeHeight
        ) {
          return current
        }

        return { toolbarHeight, tableChromeHeight }
      })
    }

    const scheduleMeasure = () => {
      if (frameId != null) {
        window.cancelAnimationFrame(frameId)
      }
      frameId = window.requestAnimationFrame(measureDetailLayout)
    }

    measureDetailLayout()
    scheduleMeasure()

    if (typeof ResizeObserver === 'undefined') {
      window.addEventListener('resize', scheduleMeasure)
      return () => {
        if (frameId != null) window.cancelAnimationFrame(frameId)
        window.removeEventListener('resize', scheduleMeasure)
      }
    }

    const observer = new ResizeObserver(scheduleMeasure)
    if (gridContentElement) observer.observe(gridContentElement)
    if (toolbarElement) observer.observe(toolbarElement)
    if (tableRegionElement) observer.observe(tableRegionElement)
    window.addEventListener('resize', scheduleMeasure)
    return () => {
      if (frameId != null) window.cancelAnimationFrame(frameId)
      observer.disconnect()
      window.removeEventListener('resize', scheduleMeasure)
    }
  }, [gridContentElement, tableRegionElement, toolbarElement, detailTableRenderKey, exporting, exportProgress])

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

  const categoryLookup = useMemo(() => buildWarehouseCategoryLookup(categories), [categories])
  const selectedTargetCategory = useMemo(() => findWarehouseCategory(categories, targetCategoryGuid), [categories, targetCategoryGuid])
  const selectedTargetCategoryPath = targetCategoryGuid ? getWarehouseProductCategoryTooltip({
    categoryName: selectedTargetCategory?.categoryName,
    warehouseCategoryGUID: targetCategoryGuid,
  }, categoryLookup, i18n.language) : undefined
  const selectedRowTargetCategory = useMemo(() => findWarehouseCategory(categories, rowTargetCategoryGuid), [categories, rowTargetCategoryGuid])
  const selectedRowTargetCategoryPath = rowTargetCategoryGuid ? getWarehouseProductCategoryTooltip({
    categoryName: selectedRowTargetCategory?.categoryName,
    warehouseCategoryGUID: rowTargetCategoryGuid,
  }, categoryLookup, i18n.language) : undefined

  const remoteColumnFilters = useMemo<ContainerDetailColumnFilters>(() => omitContainerDetailTextFilters(columnFilters), [columnFilters])

  const baseDetailQuery = useMemo(() => buildContainerDetailQuery({
    containerGuid,
    filters: remoteColumnFilters,
    pageNumber: 1,
    pageSize: CONTAINER_DETAIL_PAGE_SIZE,
  }), [containerGuid, remoteColumnFilters])

  const baseDetailQueryKey = useMemo(() => JSON.stringify(baseDetailQuery), [baseDetailQuery])
  const loadedDetailQueryKey = lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid
    ? lastLoadedContainerDetailSuccessRef.current?.queryKey
    : null
  const hasLoadedFullBaseDetailQuery = canUseContainerDetailLocalTagFilters({
    loadedQueryKey: loadedDetailQueryKey,
    baseQueryKey: baseDetailQueryKey,
    loadedRowsLength: rows.length,
    itemsTotal: detailItemsTotal,
    hasMore: detailHasMore,
    loading: detailLoading,
    loadingMore: detailLoadingMore,
  })
  // 标签过滤不再进入后端查询；未全量加载时由前端继续补齐 base 查询后再过滤。
  const detailQuery = baseDetailQuery
  const detailQueryKey = baseDetailQueryKey
  // 远程加载 effect 只依赖这个稳定 key，避免视口状态变化触发重复拉取。
  const activeLoadQueryKey = detailQueryKey

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
        货柜编号: info.货柜编号,
        装柜日期: info.装柜日期 ? dayjs(info.装柜日期) : null,
        预计到岸日期: info.预计到岸日期 ? dayjs(info.预计到岸日期) : null,
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
      setPendingPricePatches({})
      setSelectedRowKeys([])
    } else {
      setDetailLoadingMore(true)
    }

    try {
      const shouldComputeDetailMeta = mode === 'reset'
      const result = await queryContainerProducts(
        containerGuid,
        {
          ...detailQuery,
          pageNumber,
          pageSize: CONTAINER_DETAIL_PAGE_SIZE,
          includeTotal: shouldComputeDetailMeta,
          includeStats: shouldComputeDetailMeta,
        },
        controller.signal,
      )
      if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId) {
        return
      }

      setRows((items) => mode === 'reset' ? result.items : mergeContainerDetailLoadedItems(items, result.items))
      if (result.totalComputed !== false) {
        setDetailItemsTotal(result.itemsTotal)
      }
      setDetailPageNumber(result.pageNumber)
      setDetailHasMore(result.hasMore)
      if (result.statsComputed !== false) {
        setRemoteTagStats({ ...EMPTY_CONTAINER_DETAIL_TAG_STATS, ...result.tagStats })
      }
      if (mode === 'reset') {
        lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: detailQueryKey }
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
      loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid
        ? lastLoadedContainerDetailSuccessRef.current?.queryKey
        : null,
    })) {
      return
    }

    void loadDetailChunk(1, 'reset')
    return () => {
      detailAbortControllerRef.current?.abort()
    }
    // 标签不进入 detailQueryKey；只有非标签远程筛选变化才重置懒加载结果。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, activeLoadQueryKey])

  useEffect(() => {
    // 标签只在前端过滤；切换标签时主动清空选择，保持批量操作范围清晰。
    setSelectedRowKeys([])
  }, [selectedTagFilters])

  useEffect(() => {
    if (!active || !selectedTagFilters.length || !detailHasMore || detailLoading || detailLoadingMore) {
      return
    }

    // 标签需要针对当前 base 查询的完整结果生效，点选标签后后台补齐剩余懒加载块。
    void loadDetailChunk(detailPageNumber + 1, 'append')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, selectedTagFilters, detailHasMore, detailLoading, detailLoadingMore, detailPageNumber, activeLoadQueryKey])

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
    return baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))
  }, [baseFilteredRows, selectedTagFilters])

  const textFilteredRows = useMemo(() => {
    // 顶部筛选条已移除，文字筛选统一走列头筛选，避免同一条件出现两个入口。
    return applyContainerDetailLoadedTextFilters(tagFilteredRows, '', columnFilters)
  }, [columnFilters, tagFilteredRows])

  const localBaseTagStats = useMemo(() => buildContainerDetailTagStats(baseFilteredRows), [baseFilteredRows])
  const filteredRows = textFilteredRows

  const displayRows = useMemo(
    () => {
      // 列头排序只调整当前已加载且已过滤的行，避免点击列头触发远程重载。
      return applyContainerDetailColumnState(filteredRows, {}, sortState)
    },
    [filteredRows, sortState],
  )

  const applyCurrentClientFilters = (sourceRows: ContainerDetail[]) => {
    const nextTagFilteredRows = sourceRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))
    const nextTextFilteredRows = applyContainerDetailLoadedTextFilters(nextTagFilteredRows, '', columnFilters)
    return applyContainerDetailColumnState(nextTextFilteredRows, {}, sortState)
  }

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
      orderedEditableColumnKeys,
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
      // 方向键只切换焦点；自动保存列仍走 blur，价格列统一由“保存明细”落库。
      const targetCell = editableCellRefs.current.get(nextCellKey)
      if (!targetCell) {
        return
      }
      pendingEditableCellFocusKeyRef.current = null
      targetCell.focus()
      targetCell.select?.()
    })
  }

  const tagStats: ContainerDetailTagStats = useMemo(() => ({
    ...(hasLoadedFullBaseDetailQuery ? localBaseTagStats : remoteTagStats),
    // 商品类型统计后端不返回，始终由当前已加载 base 明细补充；全量加载完成后即为准确统计。
    normal: localBaseTagStats.normal,
    set: localBaseTagStats.set,
    multi: localBaseTagStats.multi,
    setChild: localBaseTagStats.setChild,
  }), [hasLoadedFullBaseDetailQuery, localBaseTagStats, remoteTagStats])

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

  const exportColumnOptions = useMemo(
    () => CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => ({
      label: t(column.labelKey, column.fallbackLabel),
      value: column.key,
    })),
    [t],
  )

  const setExportFormatWithDefaults = (format: ContainerDetailExportFormat) => {
    setExportFormat(format)
    setSelectedExportColumnKeys(
      format === 'pdf'
        ? DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS
        : DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
    )
  }

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

  const hasHiddenSelectedRows = selectedRowKeys.length > 0 && selectedRows.length < selectedRowKeys.length
  const targetRows = selectedRowKeys.length ? selectedRows : displayRows

  const getRowsHguids = (scopeRows: ContainerDetail[]) => scopeRows
    .map((row) => row.hguid)
    .filter((value): value is string => Boolean(value))

  const buildDetailBatchScope = (scopeRows: ContainerDetail[] = targetRows): ContainerDetailBatchScope => ({
    selectedHguids: getRowsHguids(scopeRows),
  })

  const buildWholeContainerDetailBatchScope = (): ContainerDetailBatchScope => ({
    query: buildContainerDetailQuery({
      containerGuid,
      filters: {},
      pageNumber: 1,
      pageSize: CONTAINER_DETAIL_PAGE_SIZE,
    }),
  })

  const getCostMissingFieldMessage = (field: ContainerDetailCostMissingField) => {
    if (field === 'exchangeRate') return t('containers.messages.missingExchangeRateForCost', '缺少汇率，无法重算成本')
    if (field === 'freight') return t('containers.messages.missingFreightForCost', '缺少运费，无法重算成本')
    return t('containers.messages.missingTotalVolumeForCost', '缺少总体积，无法重算成本')
  }

  const showCostRecalculateWarning = (fields: ContainerDetailCostMissingField[]) => {
    if (!fields.length) return false
    Modal.warning({
      title: t('containers.messages.costRecalculateMissingTitle', '无法重算成本'),
      content: (
        <Space direction="vertical" size={4}>
          {fields.map((field) => (
            <span key={field}>{getCostMissingFieldMessage(field)}</span>
          ))}
        </Space>
      ),
    })
    return true
  }

  const fetchAllRowsForCurrentQuery = async () => {
    const allRows: ContainerDetail[] = []
    let pageNumber = 1
    let hasMore = true

    while (hasMore) {
      // 导出和批量写操作先拉取非标签远程筛选的完整结果，再在前端套用标签、文字筛选和排序。
      const result = await queryContainerProducts(containerGuid, {
        ...baseDetailQuery,
        pageNumber,
        pageSize: 500,
        includeTotal: false,
        includeStats: false,
      })
      allRows.push(...result.items)
      hasMore = result.hasMore
      pageNumber += 1
    }

    return applyCurrentClientFilters(allRows)
  }

  const fetchAllRowsForWholeContainer = async () => {
    const allRows: ContainerDetail[] = []
    let pageNumber = 1
    let hasMore = true

    while (hasMore) {
      // 整柜提交确认必须按当前货柜完整明细统计，不能沿用页面筛选或勾选范围。
      const result = await queryContainerProducts(containerGuid, buildContainerDetailQuery({
        containerGuid,
        filters: {},
        pageNumber,
        pageSize: 500,
        includeTotal: false,
        includeStats: false,
      }))
      allRows.push(...result.items)
      hasMore = result.hasMore
      pageNumber += 1
    }

    return allRows
  }

  const confirmSubmitContainer = (submitRows: ContainerDetail[]) => new Promise<boolean>((resolve) => {
    const createCount = submitRows.filter((row) => row.是否新商品).length
    const updateCount = submitRows.length - createCount

    Modal.confirm({
      title: t('containers.modals.submitContainerTitle', '提交货柜'),
      content: (
        <Space direction="vertical" size={8}>
          <Typography.Text>
            {t(
              'containers.modals.submitContainerContent',
              '确认提交当前货柜全部 {{total}} 条明细？本次将创建 {{created}} 个新商品，并更新 {{updated}} 个已有商品价格。',
              { total: submitRows.length, created: createCount, updated: updateCount },
            )}
          </Typography.Text>
          <Typography.Text type="secondary">
            {t(
              'containers.modals.submitContainerScopeHint',
              '提交范围为当前货柜全部未删除明细，不受当前筛选或勾选影响；成功后货柜状态会改为已完成。',
            )}
          </Typography.Text>
        </Space>
      ),
      okText: t('containers.actions.submitContainer', '提交货柜'),
      cancelText: t('common.cancel'),
      onOk: () => resolve(true),
      onCancel: () => resolve(false),
    })
  })

  const ensureTargetRowsVisible = () => {
    if (!hasHiddenSelectedRows) return true
    message.warning(t('containers.messages.selectedRowsHidden', '已选明细不在当前筛选结果中，请重新选择后再操作'))
    return false
  }

  const renderBatchActionContent = (
    count: number,
    extra?: ReactNode,
  ) => (
    <Space direction="vertical" size={6}>
      <Typography.Text>
        {t('containers.modals.batchActionContent', '确认对 {{count}} 条明细执行该批量操作？', { count })}
      </Typography.Text>
      {!selectedRowKeys.length ? (
        <Typography.Text type="warning">
          {t('containers.modals.batchActionAllHint', '当前未选择商品，确认后将按当前筛选范围执行全部匹配明细。')}
        </Typography.Text>
      ) : null}
      {extra}
    </Space>
  )

  const confirmBatchAction = (
    actionName: string,
    count: number,
    options: BatchActionConfirmOptions = {},
  ) => new Promise<boolean>((resolve) => {
    Modal.confirm({
      title: actionName,
      content: renderBatchActionContent(count, options.extra),
      okText: actionName,
      cancelText: t('common.cancel'),
      okButtonProps: options.danger ? { danger: true } : undefined,
      onOk: () => {
        if (options.beforeConfirm && !options.beforeConfirm()) {
          return Promise.reject()
        }
        resolve(true)
        return undefined
      },
      onCancel: () => resolve(false),
    })
  })

  const resolveBatchActionTargetRows = async () => {
    if (selectedRowKeys.length) return targetRows
    try {
      // 未勾选时确认数量和实际批量 scope 都以前端完整可见结果为准。
      return await fetchAllRowsForCurrentQuery()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.loadDetailFailed', '加载货柜明细失败'))
      return null
    }
  }

  const confirmBatchScope = async (
    actionName: string,
    options: BatchActionConfirmOptions = {},
  ) => {
    if (!ensureTargetRowsVisible()) return null
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return null
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectBatchProducts', '请先选择要批量操作的商品'))
      return null
    }
    const confirmed = await confirmBatchAction(actionName, scopedRows.length, options)
    return confirmed ? buildDetailBatchScope(scopedRows) : null
  }

  const confirmBatchRows = async (
    actionName: string,
    options: BatchActionConfirmOptions = {},
  ) => {
    if (!ensureTargetRowsVisible()) return null
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return null
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectBatchProducts', '请先选择要批量操作的商品'))
      return null
    }
    const confirmed = await confirmBatchAction(actionName, scopedRows.length, options)
    if (!confirmed) return null
    return scopedRows
  }

  const renderUpdateFieldSelector = <T extends string>(
    fields: readonly UpdateFieldOption<T>[],
    defaultFields: T[],
    onChange: (values: T[]) => void,
    hint?: ReactNode,
  ) => (
    <UpdateFieldSelector
      t={t}
      fields={fields}
      defaultFields={defaultFields}
      onChange={onChange}
      hint={hint}
    />
  )

  const requireSelectedUpdateFields = (selectedFields: string[]) => {
    if (selectedFields.length > 0) return true
    message.warning(t('containers.updateFields.selectAtLeastOne', '请至少选择一个更新字段'))
    return false
  }

  const confirmBatchRowsWithUpdateFields = async <T extends string>(
    actionName: string,
    fields: readonly UpdateFieldOption<T>[],
    defaultFields: T[],
    hint?: ReactNode,
  ): Promise<{ rows: ContainerDetail[]; fields: T[] } | null> => {
    let selectedFields = [...defaultFields]
    const rows = await confirmBatchRows(actionName, {
      extra: renderUpdateFieldSelector(fields, defaultFields, (values) => {
        selectedFields = values
      }, hint),
      beforeConfirm: () => requireSelectedUpdateFields(selectedFields),
    })
    return rows ? { rows, fields: selectedFields } : null
  }

  const confirmPushToHqUpdateFields = async (
    count: number,
  ): Promise<PushProductsToHqUpdateField[] | null> => {
    const actionName = t('containers.actions.pushToHq', '发送到 HQ')
    let selectedFields = [...defaultPushToHqUpdateFields]
    const confirmed = await confirmBatchAction(actionName, count, {
      extra: renderUpdateFieldSelector(
        pushToHqUpdateFields,
        defaultPushToHqUpdateFields,
        (values) => {
          selectedFields = values
        },
        <Typography.Text type="secondary">
          {t(
            'containers.updateFields.hqCreateHint',
            '字段选择主要限制已有 HQ 记录更新；如果目标表需要新增记录，系统仍会写入创建该记录所需的完整字段。',
          )}
        </Typography.Text>,
      ),
      beforeConfirm: () => requireSelectedUpdateFields(selectedFields),
    })
    return confirmed ? selectedFields : null
  }

  const canCreateContainerProducts = access.canEditContainer && access.canManagePosProducts
  const canSubmitContainer = access.canEditContainer && access.canManagePosProducts
  const canBatchSetCategory = access.canEditContainer && access.canManagePosProducts
  const canBackfillLastPrices = access.isAdmin || access.isWarehouseManager
  const pendingPricePatchList = useMemo(() => Object.values(pendingPricePatches), [pendingPricePatches])
  const pendingPricePatchCount = pendingPricePatchList.length

  const ensureNoPendingPriceDetails = () => {
    if (!pendingPricePatchCount) return true
    // 价格列改为手动保存后，跨库/后台动作必须先阻止未落库价格继续流转。
    message.warning(t('containers.messages.savePendingPriceDetailsFirst', '请先点击“保存明细”保存进口价格/贴牌价格'))
    return false
  }

  const patchRow = (key: string, patch: Partial<ContainerDetail>) => {
    setRows((items) => items.map((item) => (rowKey(item) === key ? mergeContainerDetailPatch(item, patch) : item)))
  }

  const markPendingPricePatch = (row: ContainerDetail, patch: Pick<Partial<ContainerDetail>, '进口价格' | '贴牌价格'>) => {
    const key = rowKey(row)
    patchRow(key, patch)
    if (!row.hguid) return

    setPendingPricePatches((current) => {
      const nextPatch: PendingContainerDetailPricePatch = { ...(current[key] ?? { hguid: row.hguid }) }
      if ('进口价格' in patch) {
        if (patch.进口价格 == null) {
          delete nextPatch.进口价格
        } else {
          nextPatch.进口价格 = patch.进口价格
        }
      }
      if ('贴牌价格' in patch) {
        if (patch.贴牌价格 == null) {
          delete nextPatch.贴牌价格
        } else {
          nextPatch.贴牌价格 = patch.贴牌价格
        }
      }

      const next = { ...current }
      // 价格列改为手动保存：清空输入只影响本地展示，不新增后端清空价格语义。
      if (nextPatch.进口价格 == null && nextPatch.贴牌价格 == null) {
        delete next[key]
      } else {
        next[key] = nextPatch
      }
      return next
    })
  }

  const getDetailSaveFailedMessage = () => t('containers.messages.detailSaveFailed', '货柜明细保存失败，请稍后重试')

  const handleDetailSaveError = (error: unknown) => {
    message.error(error instanceof Error ? error.message : getDetailSaveFailedMessage())
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

  const savePendingPriceDetails = async () => {
    if (!access.canEditContainer) return
    const updates = pendingPricePatchList
      .map((patch) => {
        const update: UpdateContainerDetailRequest = { hguid: patch.hguid }
        if (patch.进口价格 != null) update.进口价格 = patch.进口价格
        if (patch.贴牌价格 != null) update.贴牌价格 = patch.贴牌价格
        return update
      })
      .filter((update) => update.进口价格 != null || update.贴牌价格 != null)

    if (!updates.length) {
      message.warning(t('containers.messages.noPendingPriceDetails', '没有待保存的明细价格'))
      return
    }

    const rowKeyByHguid = new Map(rows.map((row) => [row.hguid, rowKey(row)]))
    const saveKeys = updates.flatMap((update) => buildContainerDetailSaveFailureKeys(rowKeyByHguid.get(update.hguid) ?? update.hguid, update))

    setPriceDetailsSaving(true)
    try {
      // 保存明细只提交逐行改过的价格字段，不复用批量统一改价入口，避免覆盖未编辑行。
      await trackDetailSavePromise(saveKeys, batchUpdateDetails(updates))
      setPendingPricePatches((current) => {
        const next = { ...current }
        updates.forEach((update) => {
          delete next[rowKeyByHguid.get(update.hguid) ?? update.hguid]
        })
        return next
      })
      message.success(t('containers.messages.detailPricesSaved', { count: updates.length, defaultValue: '已保存 {{count}} 条明细价格' }))
    } catch (error) {
      handleDetailSaveError(error)
    } finally {
      setPriceDetailsSaving(false)
    }
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
    const nextContainerNumber = headerForm.货柜编号?.trim()
    if (!nextContainerNumber) {
      message.error(t('containers.placeholders.enterContainerNumber', '请输入货柜编号'))
      return
    }
    const nextCostContainer = {
      ...container,
      汇率: headerForm.汇率,
      运费: headerForm.运费,
    }
    const shouldRecalculateCosts =
      (container?.汇率 ?? undefined) !== (headerForm.汇率 ?? undefined) ||
      (container?.运费 ?? undefined) !== (headerForm.运费 ?? undefined)
    if (shouldRecalculateCosts && showCostRecalculateWarning(getContainerDetailCostMissingFields(nextCostContainer))) {
      return
    }

    const updatePayload: UpdateContainerRequest = {
      货柜编号: nextContainerNumber,
      装柜日期: headerForm.装柜日期 ? headerForm.装柜日期.format('YYYY-MM-DD') : undefined,
      预计到岸日期: headerForm.预计到岸日期 ? headerForm.预计到岸日期.format('YYYY-MM-DD') : undefined,
      实际到货日期: headerForm.实际到货日期 ? headerForm.实际到货日期.format('YYYY-MM-DD') : undefined,
      汇率: headerForm.汇率,
      运费: headerForm.运费,
      备注: headerForm.备注,
      状态: headerForm.状态,
    }
    setSavingHeader(true)
    try {
      try {
        await updateContainer(containerGuid, updatePayload)
      } catch (error) {
        console.error(error)
        message.error(error instanceof Error ? error.message : t('containers.messages.headerSaveFailed'))
        return
      }

      if (shouldRecalculateCosts) {
        try {
          const result = await recalculateContainerCostsByScope(containerGuid, buildWholeContainerDetailBatchScope())
          message.success(t('containers.messages.headerSaveAndCostsRecalculated', '货柜信息已保存，已重算 {{count}} 条明细成本', { count: result.totalUpdated }))
        } catch (error) {
          console.error(error)
          const errorMessage = error instanceof Error ? error.message : t('containers.messages.costRecalculateFailed', '成本重算失败')
          message.warning(t('containers.messages.headerSavedCostsRecalculateFailed', { message: errorMessage, defaultValue: '货柜信息已保存，但成本重算失败：{{message}}' }))
        }
      } else {
        message.success(t('containers.messages.headerSaveSuccess'))
      }
      setHeaderEditing(false)
      try {
        await loadData()
      } catch (error) {
        console.error(error)
        message.error(error instanceof Error ? error.message : t('containers.messages.loadDetailFailed'))
      }
    } finally {
      setSavingHeader(false)
    }
  }

  const saveFloatRatePatch = async (row: ContainerDetail, value?: number) => {
    if (showCostRecalculateWarning(getContainerDetailCostMissingFields(container))) {
      return
    }

    const updates = buildContainerDetailFloatRateUpdates([row], container, value)
    const update = updates[0]
    if (!update) return
    applyDetailUpdatesToRows(updates)
    await trackDetailSavePromise(buildContainerDetailSaveFailureKeys(rowKey(row), update), batchUpdateDetails(updates))
  }

  const savePackageMetricPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {
    if (!access.canEditContainer || !row.hguid) return
    // 数量/体积会联动成本字段，缺少主表成本参数时必须阻止静默写入旧成本。
    if (showCostRecalculateWarning(getContainerDetailCostMissingFields(container))) {
      return
    }
    const nextRow = mergeContainerDetailPatch(row, patch)
    const update: UpdateContainerDetailRequest = { hguid: row.hguid, ...patch }

    if (nextRow.装柜件数 != null && nextRow.单件装箱数 != null) {
      update.装柜数量 = Number((nextRow.装柜件数 * nextRow.单件装箱数).toFixed(2))
    }

    const volumeRow = mergeContainerDetailPatch(row, update as Partial<ContainerDetail>)
    update.合计装柜体积 = calculateContainerDetailTotalVolume(volumeRow)
    update.合计装柜金额 = calculateContainerDetailTotalAmount(volumeRow)

    const pricedRow = mergeContainerDetailPatch(row, update as Partial<ContainerDetail>)
    const transportCost = calculateContainerDetailTransportCost(pricedRow, container)
    update.运输成本 = transportCost
    update.进口价格 = calculateContainerDetailImportPrice(
      { ...pricedRow, 运输成本: transportCost },
      container,
      pricedRow.调整浮率 ?? DEFAULT_CONTAINER_DETAIL_FLOAT_RATE,
      transportCost,
    )

    applyDetailUpdatesToRows([update])
    await trackDetailSavePromise(buildContainerDetailSaveFailureKeys(rowKey(row), update), batchUpdateDetails([update]))
  }

  const openBatchFloatRateModal = async () => {
    if (!ensureTargetRowsVisible()) return
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectBatchProducts', '请先选择要批量操作的商品'))
      return
    }
    setBatchModalTargetCount(scopedRows.length)
    setBatchModalScopeRows(scopedRows)
    setBatchFloatRate(DEFAULT_CONTAINER_DETAIL_FLOAT_RATE)
    setBatchFloatRateModalOpen(true)
  }

  const submitBatchFloatRate = async () => {
    if (batchFloatRate == null) {
      message.warning(t('containers.messages.enterFloatRate', '请输入调整浮率'))
      return
    }
    if (showCostRecalculateWarning(getContainerDetailCostMissingFields(container))) {
      return
    }
    setBatchFloatRateSaving(true)
    try {
      const result = await applyContainerFloatRateByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows), batchFloatRate)
      await loadDetailChunk(1, 'reset')
      setBatchFloatRateModalOpen(false)
      setBatchModalTargetCount(0)
      setBatchModalScopeRows([])
      setBatchFloatRate(null)
      setSelectedRowKeys([])
      message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))
    } finally {
      setBatchFloatRateSaving(false)
    }
  }

  const handleBackfillLastPrices = async () => {
    if (!canBackfillLastPrices) return
    const scope = await confirmBatchScope(t('containers.actions.backfillLastPrices', '回填上次价格'))
    if (!scope) return
    setBackfillLastPricesLoading(true)
    try {
      const result = await backfillContainerLastPricesByScope(containerGuid, scope)
      await loadDetailChunk(1, 'reset')
      setSelectedRowKeys([])
      message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))
    } finally {
      setBackfillLastPricesLoading(false)
    }
  }

  const handleMatchDomesticData = async () => {
    if (!access.canEditContainer) return
    const scopedRows = await confirmBatchRows(t('containers.actions.matchDomesticData'))
    if (!scopedRows) return
    setMatchDomesticDataLoading(true)
    try {
      if (!scopedRows.length) {
        message.warning(t('containers.messages.noMatchableDetails'))
        return
      }

      const detectionItems = buildContainerDetailDetectionItems(scopedRows)

      if (!detectionItems.length) {
        message.warning(t('containers.messages.missingMatchableProductIdentity'))
        return
      }

      const detected = await detectProducts(detectionItems)
      const updates = buildContainerDetailMatchedDomesticDataUpdates(scopedRows, detected, container)
        .map((update) => ({ ...update, SkipRelatedProductSync: true }))
      if (!updates.length) {
        message.info(t('containers.messages.noDomesticDataToUpdate'))
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
      message.success(t('containers.messages.domesticDataMatched', { count: updates.length, priceCount: pricePatchCount }))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.matchDomesticDataFailed'))
    } finally {
      setMatchDomesticDataLoading(false)
    }
  }

  const openBatchPricesModal = async () => {
    if (!ensureTargetRowsVisible()) return
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectBatchProducts', '请先选择要批量操作的商品'))
      return
    }
    setBatchModalTargetCount(scopedRows.length)
    setBatchModalScopeRows(scopedRows)
    setBatchImportPrice(null)
    setBatchOemPrice(null)
    setBatchPricesModalOpen(true)
  }

  const submitBatchPrices = async () => {
    if (batchImportPrice == null && batchOemPrice == null) {
      message.warning(t('containers.messages.enterImportOrOemPrice'))
      return
    }
    setBatchPricesSaving(true)
    try {
      const result = await applyContainerPricesByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows), {
        importPrice: batchImportPrice,
        oemPrice: batchOemPrice,
      })
      await loadDetailChunk(1, 'reset')
      setBatchPricesModalOpen(false)
      setBatchModalTargetCount(0)
      setBatchModalScopeRows([])
      setBatchImportPrice(null)
      setBatchOemPrice(null)
      setSelectedRowKeys([])
      message.success(t('containers.messages.detailPricesUpdated', { count: result.totalUpdated }))
    } finally {
      setBatchPricesSaving(false)
    }
  }

  const applyActive = async (isActive: boolean) => {
    const scopedRows = await confirmBatchRows(t(isActive ? 'containers.actions.batchActivate' : 'containers.actions.batchDeactivate'))
    if (!scopedRows) return
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    // 新商品尚未写入仓库商品表，不能调用仓库商品上下架接口。
    const newProductCount = scopedRows.filter((row) => row.是否新商品).length
    const eligibleRows = scopedRows.filter((row) => !row.是否新商品)
    if (!eligibleRows.length) {
      message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架'))
      return
    }
    if (newProductCount > 0) {
      message.warning(t('containers.messages.newProductsSkippedForWarehouseStatus', { count: newProductCount, defaultValue: '已跳过 {{count}} 条新商品，请先创建后再上下架' }))
    }

    const productCodes = eligibleRows
      .map(getContainerDetailProductCode)
      .filter((value): value is string => Boolean(value))
      .filter((value) => !pendingWarehouseStatusCodes.has(value))
    if (!productCodes.length) {
      message.warning(
        eligibleRows.some((row) => {
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
    const scopedRows = await confirmBatchRows(t('containers.actions.batchTranslate'))
    if (!scopedRows) return
    const names = Array.from(new Set(scopedRows.map(getContainerDetailTranslationSource).filter((value): value is string => Boolean(value))))
    if (!names.length) {
      message.warning(t('containers.messages.noNamesToTranslate'))
      return
    }
    const translations = await batchTranslate(names)
    const updates = buildContainerDetailTranslationUpdates(scopedRows, translations)
    const skippedInvalidCount = countContainerDetailInvalidTranslationResults(scopedRows, translations)
    if (updates.length) {
      await batchUpdateDetails(updates)
      setRows((items) => applyContainerDetailEnglishNameUpdates(items, updates))
      if (!selectedRowKeys.length) {
        await loadDetailChunk(1, 'reset')
      }
      message.success(t('containers.messages.namesTranslated', { count: updates.length }))
    }
    if (skippedInvalidCount > 0) {
      message.warning(t('containers.messages.invalidTranslatedNamesSkipped', { count: skippedInvalidCount }))
    }
    if (!updates.length && skippedInvalidCount === 0) {
      message.success(t('containers.messages.namesTranslated', { count: 0 }))
    }
  }

  const openBatchEditEnglishName = async () => {
    if (!ensureTargetRowsVisible()) return
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return
    if (!scopedRows.length) {
      message.warning(t('containers.messages.selectBatchProducts', '请先选择要批量操作的商品'))
      return
    }
    setBatchModalTargetCount(scopedRows.length)
    setBatchModalScopeRows(scopedRows)
    setBatchEnglishName('')
    setBatchEnglishNameModalOpen(true)
  }

  const openBatchCategory = async () => {
    if (!ensureTargetRowsVisible()) return
    const scopedRows = await resolveBatchActionTargetRows()
    if (scopedRows == null) return
    if (!scopedRows.length) {
      message.warning(t('containers.messages.noCategoryFilterRows', '当前没有可分类的明细'))
      return
    }
    setBatchModalTargetCount(scopedRows.length)
    setBatchModalScopeRows(scopedRows)
    setTargetCategoryGuid(undefined)
    // 每次打开批量分类弹窗都只展开到一级分类，避免默认露出过深的子分类。
    setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1))
    setBatchCategoryOpen(true)
  }

  const buildContainerDetailCategoryPatch = (
    item: ContainerDetail,
    categoryGuid: string,
    category?: WarehouseCategoryNode,
    categoryPath?: string,
  ): Partial<ContainerDetail> => {
    const categoryName = category ? formatWarehouseCategoryNodeName(category, i18n.language) : undefined
    const nextCategoryPath = categoryPath || categoryName

    return {
      categoryName,
      CategoryName: categoryName,
      productCategoryName: categoryName,
      ProductCategoryName: categoryName,
      categoryPath: nextCategoryPath,
      CategoryPath: nextCategoryPath,
      categoryFullPath: nextCategoryPath,
      CategoryFullPath: nextCategoryPath,
      warehouseCategoryGUID: categoryGuid,
      WarehouseCategoryGUID: categoryGuid,
      productCategoryGUID: categoryGuid,
      ProductCategoryGUID: categoryGuid,
      商品信息: item.商品信息
        ? {
            ...item.商品信息,
            categoryName,
            CategoryName: categoryName,
            productCategoryName: categoryName,
            ProductCategoryName: categoryName,
            categoryPath: nextCategoryPath,
            CategoryPath: nextCategoryPath,
            categoryFullPath: nextCategoryPath,
            CategoryFullPath: nextCategoryPath,
            warehouseCategoryGUID: categoryGuid,
            WarehouseCategoryGUID: categoryGuid,
            productCategoryGUID: categoryGuid,
            ProductCategoryGUID: categoryGuid,
          }
        : item.商品信息,
    }
  }

  const openRowCategoryModal = (row: ContainerDetail) => {
    if (!canBatchSetCategory) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }

    setRowCategoryEditingRow(row)
    setRowTargetCategoryGuid(getContainerDetailCategoryGuid(row))
    // 单行修改也沿用分类树，只默认展开一级，避免打开时树过深难扫。
    setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1))
    setRowCategoryOpen(true)
  }

  const closeRowCategoryModal = () => {
    setRowCategoryOpen(false)
    setRowCategoryEditingRow(null)
    setRowTargetCategoryGuid(undefined)
  }

  const handleRowCategorySave = async () => {
    if (!canBatchSetCategory) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    if (!rowCategoryEditingRow?.hguid) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }
    if (!rowTargetCategoryGuid) {
      message.warning(t('warehouse.categories.selectTargetCategory', '请选择目标分类'))
      return
    }

    setRowCategorySaving(true)
    try {
      // 单行目标分类只提交当前明细 GUID，不影响批量分类入口和其他已加载行。
      await batchUpdateDetails([{ hguid: rowCategoryEditingRow.hguid, ProductCategoryGUID: rowTargetCategoryGuid }])
      const categoryPatch = buildContainerDetailCategoryPatch(
        rowCategoryEditingRow,
        rowTargetCategoryGuid,
        selectedRowTargetCategory,
        selectedRowTargetCategoryPath,
      )
      setRows((items) =>
        items.map((item) => (
          rowKey(item) !== rowKey(rowCategoryEditingRow)
            ? item
            : mergeContainerDetailPatch(item, categoryPatch)
        )),
      )
      setRowCategoryOpen(false)
      setRowCategoryEditingRow(null)
      setRowTargetCategoryGuid(undefined)
      message.success(t('containers.messages.rowCategoryUpdated', '目标分类已更新'))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('warehouse.categories.batchAssignFailed', '分类保存失败'))
    } finally {
      setRowCategorySaving(false)
    }
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

    const batchCategoryTargetRows = batchModalScopeRows
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
      // 保存成功后只更新当前已加载行，避免重新查询整张明细表造成等待和 loading。
      setRows((items) =>
        items.map((item) => {
          const productCode = getContainerDetailProductCode(item)
          if (!productCode || !productCodeSet.has(productCode)) return item
          return mergeContainerDetailPatch(item, buildContainerDetailCategoryPatch(item, targetCategoryGuid, selectedTargetCategory, selectedTargetCategoryPath))
        }),
      )
      setSelectedRowKeys([])
      setBatchCategoryOpen(false)
      setBatchModalTargetCount(0)
      setBatchModalScopeRows([])
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
    const scopedRows = batchModalScopeRows
    const updates = buildContainerDetailEnglishNameUpdates(scopedRows, batchEnglishName)
    if (!updates.length) {
      message.warning(t('containers.messages.enterEnglishName'))
      return
    }

    setBatchEnglishNameSaving(true)
    try {
      const result = await batchUpdateDetails(updates)
      if (result.totalUpdated > 0) {
        setRows((items) => applyContainerDetailEnglishNameUpdates(items, updates))
        if (!selectedRowKeys.length) {
          await loadDetailChunk(1, 'reset')
        }
      }
      setBatchEnglishNameModalOpen(false)
      setBatchModalTargetCount(0)
      setBatchModalScopeRows([])
      setBatchEnglishName('')
      message.success(t('containers.messages.englishNamesUpdated', { count: result.totalUpdated }))
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.englishNamesUpdateFailed'))
    } finally {
      setBatchEnglishNameSaving(false)
    }
  }

  const clearEnglishNames = async () => {
    const scopedRows = await confirmBatchRows(t('containers.actions.clearEnglishNames'), { danger: true })
    if (!scopedRows) return
    const updates = buildContainerDetailClearEnglishNameUpdates(scopedRows)
    if (!updates.length) {
      message.warning(t('containers.messages.selectProducts'))
      return
    }

    await batchUpdateDetails(updates)
    setRows((items) => applyContainerDetailEnglishNameUpdates(
      items,
      updates.map((update) => ({ hguid: update.hguid, 英文名称: undefined })),
    ))
    if (!selectedRowKeys.length) {
      await loadDetailChunk(1, 'reset')
    }
    message.success(t('containers.messages.englishNamesCleared', { count: updates.length }))
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
    if (!ensureNoPendingPriceDetails()) return

    const selection = buildContainerDetailHqPushSelection(selectedRows)
    if (!selection.items.length) {
      message.warning(t('containers.messages.noExistingLocalProductsToPushHq', '选中明细没有可发送到 HQ 的本地已有商品'))
      return
    }

    const updateFields = await confirmPushToHqUpdateFields(selection.items.length)
    if (!updateFields) return

    try {
      // 写 HQ 是跨库操作，使用即时锁防止连续点击造成重复提交。
      pushToHqLoadingRef.current = true
      setPushToHqLoading(true)
      const job = await createPushProductsToHqJob({
        operationId: buildPushProductsToHqOperationId(containerGuid, selection.productCodes, selection.items.length, updateFields),
        productCodes: selection.productCodes,
        items: selection.items,
        updateFields,
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

  const showSubmitContainerJobResult = (job: ContainerProductCreationJob) => {
    const result = job.result
    const description = (
      <Space direction="vertical" size={8}>
        <Typography.Text>
          {t('containers.text.submitContainerJobSummary', '提交完成：创建 {{created}}，更新 {{updated}}，跳过 {{skipped}}，失败 {{failed}}', {
            created: result.createdCount,
            updated: result.updatedCount,
            skipped: result.skippedCount,
            failed: result.failedCount,
          })}
        </Typography.Text>
        <Typography.Text type={result.containerCompleted ? 'success' : 'secondary'}>
          {result.containerCompleted
            ? t('containers.messages.containerMarkedCompleted', '货柜已标记为已完成')
            : t('containers.messages.containerNotMarkedCompleted', '货柜状态未变更')}
        </Typography.Text>
        {job.message ? <Typography.Text type="secondary">{job.message}</Typography.Text> : null}
        {result.updated.length ? (
          <div>
            <Typography.Text strong>{t('containers.text.updatedRows', '更新明细')}</Typography.Text>
            {renderCreateProductResultItems(result.updated)}
          </div>
        ) : null}
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

    if (job.status === 'Failed' || result.failedCount > 0 || result.errors.length > 0) {
      notification.error({
        message: t('containers.messages.submitContainerJobFailed', '提交货柜失败'),
        description,
        duration: 0,
      })
      return
    }

    notification.success({
      message: t('containers.messages.submitContainerJobSucceeded', '提交货柜完成'),
      description,
      duration: 0,
    })
  }

  const submitContainer = async () => {
    if (submitContainerLoadingRef.current) return
    if (!canSubmitContainer) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    if (!containerGuid) {
      message.warning(t('containers.messages.missingContainerGuid', '缺少货柜 GUID'))
      return
    }
    if (!ensureNoPendingPriceDetails()) return
    blurActiveContainerDetailEditableCell()
    try {
      await flushPendingDetailSaves()
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.detailSaveFailed', '货柜明细保存失败，请稍后重试'))
      return
    }

    try {
      submitContainerLoadingRef.current = true
      setSubmitContainerLoading(true)
      const submitRows = await fetchAllRowsForWholeContainer()
      const confirmed = await confirmSubmitContainer(submitRows)
      if (!confirmed) return
      // 整柜提交由后端按当前货柜加载全部未删除明细，前端不传筛选或勾选范围。
      const job = await createContainerSubmitJob({
        operationId: buildContainerSubmitOperationId(containerGuid),
        containerGuid,
      })
      message.info(t('containers.messages.submitContainerJobSubmitted', '提交货柜任务已提交，正在后台处理'))
      const finalJob = job.status === 'Queued' || job.status === 'Running'
        ? await waitForContainerSubmitJob(job.jobId)
        : job
      showSubmitContainerJobResult(finalJob)
      if (finalJob.result.containerCompleted) {
        setSelectedRowKeys([])
        await loadData(false)
      }
    } catch (error) {
      message.error(error instanceof Error ? error.message : t('containers.messages.submitContainerFailed', '提交货柜失败'))
    } finally {
      submitContainerLoadingRef.current = false
      setSubmitContainerLoading(false)
    }
  }

  const createNewProducts = async () => {
    if (createProductsLoadingRef.current) return
    if (!access.canEditContainer || !access.canManagePosProducts) {
      message.warning(t('posAdmin.products.noManagePermission', '无权限管理商品'))
      return
    }
    if (!ensureNoPendingPriceDetails()) return
    const scopedRows = await confirmBatchRows(t('containers.actions.createNewProducts'))
    if (!scopedRows) return
    const detailHguids = scopedRows.map((row) => row.hguid).filter((value): value is string => Boolean(value))
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
    const missingProductNameRows = findContainerDetailRowsMissingProductName(scopedRows)
    if (missingProductNameRows.length) {
      message.warning(t(
        'containers.messages.createProductsMissingProductName',
        '请填写商品名称后再创建新商品：{{items}}',
        { items: missingProductNameRows.map((row) => row.label).join('、') },
      ))
      return
    }
    const missingRetailPriceRows = findContainerDetailRowsMissingCreateProductRetailPrice(scopedRows)
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
    if (!ensureNoPendingPriceDetails()) return
    const confirmed = await confirmBatchRowsWithUpdateFields(
      t('containers.actions.updateExistingPurchase'),
      containerExistingProductUpdateFields,
      defaultContainerExistingProductUpdateFields,
    )
    if (!confirmed) return
    const { rows: scopedRows, fields: updateFields } = confirmed
    const candidates = scopedRows.filter((row) => !row.是否新商品)
    if (!candidates.length) {
      message.info(t('containers.messages.noExistingProductsToUpdate'))
      return
    }
    const shouldUpdate = (field: ContainerExistingProductUpdateField) => updateFields.includes(field)
    const hasPositiveImportPrice = (row: ContainerDetail) => (row.进口价格 ?? 0) > 0
    const hasPositiveOemPrice = (row: ContainerDetail) => (resolveContainerDetailOemPrice(row) ?? 0) > 0
    const updates = candidates.filter((row) => {
      const code = row.商品编码 || row.商品信息?.商品编码 || ''
      // 更新已有商品时贴牌价只使用货柜明细业务价，上次贴牌价格仅作只读快照展示。
      return Boolean(code) && (
        (shouldUpdate('domesticPrice') && row.国内价格 != null)
        || (shouldUpdate('importPrice') && hasPositiveImportPrice(row))
        || (shouldUpdate('oemPrice') && hasPositiveOemPrice(row))
        || (shouldUpdate('volume') && row.单件体积 != null)
        || (shouldUpdate('storePurchasePrice') && hasPositiveImportPrice(row))
        || (shouldUpdate('storeRetailPrice') && hasPositiveOemPrice(row))
        || (shouldUpdate('storeMultiCodePurchasePrice') && hasPositiveImportPrice(row))
        || (shouldUpdate('storeMultiCodeRetailPrice') && hasPositiveOemPrice(row))
      )
    })
    if (!updates.length) {
      message.info(t('containers.messages.noPurchasePriceDiff'))
      return
    }
    try {
      const warehouseUpdates = updates.map((row) => {
        const item: WarehouseProductBatchUpdateItem = {
          ProductCode: row.商品编码 || row.商品信息?.商品编码,
        }
        const oemPrice = resolveContainerDetailOemPrice(row)
        if (shouldUpdate('domesticPrice') && row.国内价格 != null) item.DomesticPrice = row.国内价格
        if (shouldUpdate('importPrice') && hasPositiveImportPrice(row)) item.ImportPrice = row.进口价格
        if (shouldUpdate('oemPrice') && hasPositiveOemPrice(row)) item.OEMPrice = oemPrice
        if (shouldUpdate('volume') && row.单件体积 != null) item.Volume = row.单件体积
        return item
      }).filter((item) => Object.keys(item).length > 1)

      if (warehouseUpdates.length) {
        await batchUpdateWarehouseProducts(warehouseUpdates, {
          // 货柜页已把分店进货价拆成独立勾选项，避免主表进口价默认联动分店表。
          syncStorePurchasePrice: shouldUpdate('storePurchasePrice'),
        })
      }

      if (shouldUpdate('storePurchasePrice') || shouldUpdate('storeRetailPrice')) {
        const retailUpdates = updates.map((row) => {
          const item: StoreRetailPriceUpsertActiveItem = {
            ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
          }
          const oemPrice = resolveContainerDetailOemPrice(row)
          if (shouldUpdate('storePurchasePrice') && hasPositiveImportPrice(row)) item.PurchasePrice = row.进口价格
          if (shouldUpdate('storeRetailPrice') && hasPositiveOemPrice(row)) item.StoreRetailPriceValue = oemPrice
          return item
        }).filter((item) => Object.keys(item).length > 1)
        if (retailUpdates.length) {
          await upsertRetailForActiveStores(retailUpdates)
        }
      }

      if (shouldUpdate('storeMultiCodePurchasePrice') || shouldUpdate('storeMultiCodeRetailPrice')) {
        const multiCodeUpdates = updates.map((row) => {
          const item: StoreMultiCodePriceUpsertActiveItem = {
            ProductCode: row.商品编码 || row.商品信息?.商品编码 || '',
          }
          const oemPrice = resolveContainerDetailOemPrice(row)
          if (shouldUpdate('storeMultiCodePurchasePrice') && hasPositiveImportPrice(row)) item.PurchasePrice = row.进口价格
          if (shouldUpdate('storeMultiCodeRetailPrice') && hasPositiveOemPrice(row)) item.MultiCodeRetailPrice = oemPrice
          return item
        }).filter((item) => Object.keys(item).length > 1)
        if (multiCodeUpdates.length) {
          await upsertMultiCodeForActiveStores(multiCodeUpdates)
        }
      }
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

  const exportDetails = async (
    columnKeys: ContainerDetailExportColumnKey[] = DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
    format: ContainerDetailExportFormat = 'excel',
  ) => {
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
      const baseSummaryRows: NonNullable<ContainerExportOptions['summary']>['rows'] = [
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
      ]
      // PDF 对外分享时不展示汇率和运费金额；Excel 仍保留完整核对信息。
      const summaryRows: NonNullable<ContainerExportOptions['summary']>['rows'] = format === 'pdf'
        ? [
            [
              { label: t('containers.fields.containerNumber'), value: container?.货柜编号 || '--' },
              { label: t('containers.fields.loadingDate'), value: formatDate(container?.装柜日期) },
              { label: t('containers.fields.estimatedArrival'), value: formatDate(container?.预计到岸日期) },
            ],
            [
              { label: t('containers.fields.status'), value: getContainerStatusText(container?.状态, t) },
              { label: t('containers.fields.actualArrival'), value: formatDate(container?.实际到货日期) },
              { label: t('containers.fields.totalVolume'), value: container?.总体积 ?? '--', valueType: container?.总体积 == null ? 'text' : 'volume' },
            ],
            [
              { label: t('containers.fields.remark'), value: container?.备注 || '--' },
            ],
          ]
        : baseSummaryRows
      const exportOptions: ContainerExportOptions = {
        columns: exportColumns.map((column) => ({
          header: t(column.labelKey, column.fallbackLabel),
          key: column.key,
          width: column.width,
          valueType: column.valueType,
          currencySymbol: column.key === 'domesticPrice' ? '¥' as const : undefined,
        })),
        summary: {
          title: t('containers.export.summaryTitle', '货柜主表信息'),
          rows: summaryRows,
        },
        fileName: `${container?.货柜编号 || t('containers.detailTitle')}_${t('containers.export.detailSuffix')}`,
        onProgress: (progress: number) => setExportProgress(progress),
      }
      if (format === 'pdf') {
        await exportContainerDetailsToPdf(items, exportOptions)
      } else {
        await exportContainerDetailsToExcel(items, exportOptions)
      }
      message.success(
        format === 'pdf'
          ? t('containers.messages.detailsPdfExported', '已导出 {{count}} 条明细 PDF', { count: items.length })
          : t('containers.messages.detailsExported', { count: items.length }),
      )
    } catch {
      message.error(t('containers.messages.detailsExportFailed', '导出失败，请稍后重试'))
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
    // 新商品尚未写入仓库商品表，不能调用仓库商品上下架接口。
    if (row.是否新商品) {
      message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架'))
      return
    }
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
      render: (_, row) => {
        const categoryCell = renderContainerDetailCategoryCell(row, categoryLookup, i18n.language)
        if (!canBatchSetCategory) return categoryCell

        return (
          <button
            type="button"
            className="container-detail-category-button"
            onClick={(event) => {
              event.stopPropagation()
              openRowCategoryModal(row)
            }}
          >
            {categoryCell}
          </button>
        )
      },
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
      title: renderColumnTitle('packingQuantity', t('containers.fields.packingQuantity', '单件装箱数')),
      dataIndex: '单件装箱数',
      width: 88,
      align: 'right',
      ...makeSortProps('packingQuantity'),
      ...numberFilterProps('packingQuantity'),
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(rowKey(row), 'packingQuantity', cell)}
            value={row.单件装箱数}
            keyboard={false}
            min={0}
            precision={0}
            step={1}
            controls={false}
            style={{ width: 72 }}
            onChange={(value) => patchRow(rowKey(row), { 单件装箱数: value == null ? undefined : Number(value) })}
            onBlur={(event) => {
              if (!event.target.value.trim()) {
                patchRow(rowKey(row), { 单件装箱数: row.单件装箱数 })
                return
              }
              void savePackageMetricPatch(row, { 单件装箱数: Number(event.target.value) }).catch(handleDetailSaveError)
            }}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'packingQuantity', event)}
          />
        ) : renderNumericCell(formatNumber(row.单件装箱数, 0)),
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
      title: renderColumnTitle('unitVolume', t('containers.fields.unitVolume', '单件体积')),
      dataIndex: '单件体积',
      width: 86,
      align: 'right',
      ...makeSortProps('unitVolume'),
      ...numberFilterProps('unitVolume'),
      render: (_value, row) =>
        access.canEditContainer ? (
          <InputNumber
            ref={(cell) => setEditableCellRef(rowKey(row), 'unitVolume', cell)}
            value={row.单件体积}
            keyboard={false}
            min={0}
            precision={3}
            controls={false}
            style={{ width: 72 }}
            onChange={(value) => patchRow(rowKey(row), { 单件体积: value == null ? undefined : Number(value) })}
            onBlur={(event) => {
              if (!event.target.value.trim()) {
                patchRow(rowKey(row), { 单件体积: row.单件体积 })
                return
              }
              void savePackageMetricPatch(row, { 单件体积: Number(event.target.value) }).catch(handleDetailSaveError)
            }}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'unitVolume', event)}
          />
        ) : renderNumericCell(formatNumber(row.单件体积, 3)),
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
      title: renderColumnTitle('unitTransportCost', t('containers.fields.unitTransportCost', '单件运输成本')),
      width: 104,
      align: 'right',
      ...makeSortProps('unitTransportCost'),
      ...numberFilterProps('unitTransportCost'),
      render: (_, row) => renderNumericCell(formatCurrency(calculateContainerDetailUnitTransportCost(row), '$')),
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
            controls={false}
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
            controls={false}
            style={{ width: 68 }}
            onChange={(value) => patchRow(rowKey(row), { 中包数: value == null ? undefined : Number(value) })}
            onBlur={(event) => void saveRowPatch(row, { 中包数: event.target.value ? Number(event.target.value) : undefined }).catch(handleDetailSaveError)}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'middlePackQuantity', event)}
          />
        ) : renderNumericCell(row.中包数 ?? '--'),
    },
    {
      title: renderColumnTitle('warehouseImportPrice', t('containers.fields.warehouseImportPrice', '上次进货价格')),
      dataIndex: 'warehouseImportPrice',
      width: 112,
      align: 'right',
      ...makeSortProps('warehouseImportPrice'),
      ...numberFilterProps('warehouseImportPrice'),
      render: (_value, row) => renderNumericCell(formatCurrency(getContainerDetailLastImportPrice(row), '$')),
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
              controls={false}
              style={{ width: 78 }}
              onChange={(value) => markPendingPricePatch(row, { 进口价格: value == null ? undefined : Number(value) })}
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
            value={row.贴牌价格}
            keyboard={false}
            min={0}
            prefix="$"
            precision={2}
            controls={false}
            style={{ width: 78 }}
            onChange={(value) => markPendingPricePatch(row, { 贴牌价格: value == null ? undefined : Number(value) })}
            onKeyDown={(event) => handleEditableCellKeyDown(row, 'oemPrice', event)}
          />
        ) : renderOemPriceCell(row),
    },
    {
      title: renderColumnTitle('lastOEMPrice', t('containers.fields.lastOEMPrice', '上次贴牌价格')),
      width: 104,
      align: 'right',
      ...makeSortProps('lastOEMPrice'),
      ...numberFilterProps('lastOEMPrice'),
      render: (_, row) => renderNumericCell(formatCurrency(getContainerDetailLastOemPrice(row), '$')),
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
            <Tooltip title={t('common.copy', 'Copy')}>
              <Button
                size="small"
                type="text"
                aria-label={t('common.copyValue', 'Copy {{value}}', { value: barcode })}
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
        const warehouseStatusDisabledMessage = isWarehouseStatusPending
          ? ''
          : row.是否新商品
            ? t('containers.messages.newProductCannotToggleWarehouseStatus', '新商品请先创建后再上下架')
            : !productCode
              ? t('containers.messages.selectedProductsMissingCode')
              : ''

        return access.canEditContainer ? (
          <Tooltip title={warehouseStatusDisabledMessage}>
            <Switch
              size="small"
              checked={isActive}
              checkedChildren={t('common.activeUpper')}
              unCheckedChildren={t('common.inactiveUpper')}
              loading={isWarehouseStatusPending}
              disabled={row.是否新商品 || !productCode || isWarehouseStatusPending}
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
  const isColumnWidthCustomized = Object.keys(columnWidths).length > 0
  const isColumnSettingsCustomized = isColumnOrderCustomized || isColumnWidthCustomized

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

  useEffect(() => {
    setColumnWidths((current) => {
      let savedWidths: unknown = null
      if (!Object.keys(current).length && typeof window !== 'undefined') {
        try {
          const raw = localStorage.getItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY)
          savedWidths = raw ? JSON.parse(raw) : null
        } catch {
          savedWidths = null
        }
      }

      // 列宽持久化只保留当前业务列，避免旧列或临时列污染新表头布局。
      const nextWidths = normalizeContainerDetailColumnWidths(Object.keys(current).length ? current : savedWidths, draggableColumnKeys)
      if (areContainerDetailColumnWidthsEqual(current, nextWidths)) {
        return current
      }
      return nextWidths
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

  const persistColumnWidths = (nextWidths: ContainerDetailColumnWidthMap) => {
    try {
      localStorage.setItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY, JSON.stringify(nextWidths))
    } catch {
      // localStorage 不可用时不影响当前页面内拖拽列宽。
    }
  }

  const handleColumnResizeStart = (columnKey: ContainerDetailTableColumnKey, startWidth: number, event: ReactPointerEvent<HTMLSpanElement>) => {
    const startX = event.clientX
    const previousCursor = document.body.style.cursor
    const previousUserSelect = document.body.style.userSelect

    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'

    const handlePointerMove = (pointerEvent: PointerEvent) => {
      const nextWidth = clampContainerDetailColumnWidth(startWidth + pointerEvent.clientX - startX)
      setColumnWidths((current) => {
        const nextWidths = { ...current, [columnKey]: nextWidth }
        persistColumnWidths(nextWidths)
        return nextWidths
      })
    }

    const stopResize = () => {
      document.removeEventListener('pointermove', handlePointerMove)
      document.removeEventListener('pointerup', stopResize)
      document.removeEventListener('pointercancel', stopResize)
      document.body.style.cursor = previousCursor
      document.body.style.userSelect = previousUserSelect
    }

    // 使用 document 级监听，指针拖出表头区域时仍能连续调整列宽。
    document.addEventListener('pointermove', handlePointerMove)
    document.addEventListener('pointerup', stopResize, { once: true })
    document.addEventListener('pointercancel', stopResize, { once: true })
  }

  const resetColumnOrder = () => {
    setColumnOrder(draggableColumnKeys)
    setColumnWidths({})
    try {
      localStorage.removeItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)
      localStorage.removeItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY)
    } catch {
      // localStorage 不可用时仍恢复当前页面内的默认列设置。
    }
    message.success(t('containers.messages.columnOrderReset', '列设置已恢复默认'))
  }

  const orderedBaseColumns = useMemo(() => {
    const activeOrder = columnOrder.length ? columnOrder : draggableColumnKeys
    const columnMap = new Map(baseColumns.map((column) => [String(column.key), column]))
    return activeOrder
      .map((key) => columnMap.get(key))
      .filter((column): column is ColumnsType<ContainerDetail>[number] => Boolean(column))
  }, [baseColumns, columnOrder, draggableColumnKeys])

  const orderedEditableColumnKeys = useMemo(
    () => getContainerDetailEditableColumnKeysInOrder(
      // 方向键导航必须跟随当前页面列顺序，而不是固定的默认可编辑列顺序。
      orderedBaseColumns.map((column) => String(column.key)),
      CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS,
    ),
    [orderedBaseColumns],
  )

  // 小屏竖屏时取消 AntD 固定列，避免固定选择列和左侧列占掉主要阅读空间。
  const columns = (viewport.isSmallPortrait
    ? orderedBaseColumns.map((column) => ({ ...column, fixed: undefined }))
    : orderedBaseColumns
  ).map((column) => ({
    ...column,
    width: columnWidths[String(column.key) as ContainerDetailTableColumnKey] ?? column.width,
    onHeaderCell: () => {
      const columnKey = String(column.key) as ContainerDetailTableColumnKey
      const width = columnWidths[columnKey] ?? column.width
      return {
        'data-column-key': columnKey,
        'data-column-width': typeof width === 'number' ? width : CONTAINER_DETAIL_MIN_COLUMN_WIDTH,
        onColumnResizeStart: handleColumnResizeStart,
      } as DraggableHeaderCellProps
    },
  })) as ColumnsType<ContainerDetail>
  const tableScrollX = Math.max(
    CONTAINER_DETAIL_TABLE_SCROLL_X,
    CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH + columns.reduce((total, column) => {
      const width = typeof column.width === 'number' ? column.width : Number(column.width)
      return total + (Number.isFinite(width) ? width : 0)
    }, 0),
  )
  const isSmallScreen = viewport.isSmallPortrait || viewport.isSmallLandscape
  const tableScrollY = calculateContainerDetailTableScrollY({
    viewportHeight: viewport.height,
    toolbarHeight: detailLayoutMetrics.toolbarHeight,
    tableChromeHeight: detailLayoutMetrics.tableChromeHeight,
    isSmallLandscape: viewport.isSmallLandscape,
    isSmallPortrait: viewport.isSmallPortrait,
    maxScrollY: CONTAINER_DETAIL_TABLE_SCROLL_Y,
  })
  const pageClassName = [
    'container-detail-page',
    isSmallScreen ? 'container-detail-page-small' : '',
    viewport.isSmallPortrait ? 'container-detail-page-small-portrait' : '',
    viewport.isSmallLandscape ? 'container-detail-page-small-landscape' : '',
  ].filter(Boolean).join(' ')

  return (
    <PageContainer title={container?.货柜编号 ? t('containers.detailTitleWithNumber', { number: container.货柜编号 }) : t('menu.containerDetail')}>
      <Modal
        title={t('containers.modals.batchUpdateFloatRateTitle', '批量修改浮率')}
        open={batchFloatRateModalOpen}
        okText={t('containers.actions.batchUpdateFloatRate', '批量修改浮率')}
        cancelText={t('common.cancel')}
        confirmLoading={batchFloatRateSaving}
        onOk={() => void submitBatchFloatRate()}
        onCancel={() => {
          setBatchFloatRateModalOpen(false)
          setBatchModalTargetCount(0)
          setBatchModalScopeRows([])
          setBatchFloatRate(null)
        }}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {renderBatchActionContent(batchModalTargetCount)}
          <InputNumber
            autoFocus
            value={batchFloatRate}
            placeholder={t('containers.fields.floatRate')}
            precision={2}
            controls={false}
            style={{ width: '100%' }}
            onChange={setBatchFloatRate}
          />
        </Space>
      </Modal>
      <Modal
        title={t('containers.modals.batchUpdatePricesTitle', '批量修改价格')}
        open={batchPricesModalOpen}
        okText={t('containers.actions.batchUpdatePrices', '批量修改价格')}
        cancelText={t('common.cancel')}
        confirmLoading={batchPricesSaving}
        onOk={() => void submitBatchPrices()}
        onCancel={() => {
          setBatchPricesModalOpen(false)
          setBatchModalTargetCount(0)
          setBatchModalScopeRows([])
          setBatchImportPrice(null)
          setBatchOemPrice(null)
        }}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {renderBatchActionContent(batchModalTargetCount)}
          <InputNumber
            autoFocus
            value={batchImportPrice}
            placeholder={t('containers.fields.importPrice')}
            min={0}
            prefix="$"
            precision={2}
            controls={false}
            style={{ width: '100%' }}
            onChange={setBatchImportPrice}
          />
          <InputNumber
            value={batchOemPrice}
            placeholder={t('containers.fields.oemPrice')}
            min={0}
            prefix="$"
            precision={2}
            controls={false}
            style={{ width: '100%' }}
            onChange={setBatchOemPrice}
          />
        </Space>
      </Modal>
      <Modal
        title={t('containers.modals.batchEditEnglishNameTitle')}
        open={batchEnglishNameModalOpen}
        okText={t('containers.actions.batchEditEnglishName')}
        confirmLoading={batchEnglishNameSaving}
        onOk={() => void submitBatchEditEnglishName()}
        onCancel={() => {
          setBatchEnglishNameModalOpen(false)
          setBatchModalTargetCount(0)
          setBatchModalScopeRows([])
          setBatchEnglishName('')
        }}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {renderBatchActionContent(batchModalTargetCount)}
          <Input
            autoFocus
            value={batchEnglishName}
            placeholder={t('containers.placeholders.batchEnglishName')}
            onChange={(event) => setBatchEnglishName(event.target.value)}
            onPressEnter={() => void submitBatchEditEnglishName()}
          />
        </Space>
      </Modal>
      <Modal
        title={t('containers.modals.batchCategoryTitle', '批量设置分类')}
        open={batchCategoryOpen}
        width={640}
        destroyOnHidden
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={batchCategorySaving}
        okButtonProps={{ disabled: !targetCategoryGuid || categoryLoading || !categories.length }}
        onCancel={() => {
          setBatchCategoryOpen(false)
          setBatchModalTargetCount(0)
          setBatchModalScopeRows([])
          setTargetCategoryGuid(undefined)
        }}
        onOk={() => void handleBatchCategorySave()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {renderBatchActionContent(batchModalTargetCount)}
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
      <Modal
        title={t('containers.modals.rowCategoryTitle', '目标分类修改')}
        open={rowCategoryOpen}
        width={640}
        destroyOnHidden
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        confirmLoading={rowCategorySaving}
        okButtonProps={{ disabled: !rowTargetCategoryGuid || categoryLoading || !categories.length }}
        onCancel={closeRowCategoryModal}
        onOk={() => void handleRowCategorySave()}
      >
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {rowCategoryEditingRow ? (
            <Typography.Text type="secondary">
              {getContainerDetailItemNumber(rowCategoryEditingRow) ?? getContainerDetailProductName(rowCategoryEditingRow) ?? '--'}
            </Typography.Text>
          ) : null}
          {selectedRowTargetCategory ? (
            <Tag color="blue">
              {t('warehouse.categories.targetCategory', '目标分类')}: {selectedRowTargetCategoryPath || formatWarehouseCategoryNodeName(selectedRowTargetCategory, i18n.language)}
            </Tag>
          ) : null}
          <CategoryTreePicker
            categories={categories}
            selectedKey={rowTargetCategoryGuid}
            expandedKeys={categoryExpandedKeys}
            onExpand={setCategoryExpandedKeys}
            onSelect={setRowTargetCategoryGuid}
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
              <Descriptions.Item label={t('containers.fields.containerNumber')}>
                {headerEditing ? (
                  <Input
                    value={headerForm.货柜编号}
                    onChange={(event) => setHeaderForm((prev) => ({ ...prev, 货柜编号: event.target.value }))}
                  />
                ) : container?.货柜编号 || '--'}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.loadingDate')}>
                {headerEditing ? (
                  <DatePicker allowClear={false} value={headerForm.装柜日期} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 装柜日期: value }))} />
                ) : formatDate(container?.装柜日期)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.estimatedArrival')}>
                {headerEditing ? (
                  <DatePicker allowClear={false} value={headerForm.预计到岸日期} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 预计到岸日期: value }))} />
                ) : formatDate(container?.预计到岸日期)}
              </Descriptions.Item>
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
                {headerEditing ? <InputNumber value={headerForm.汇率} precision={4} controls={false} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 汇率: value ?? undefined }))} /> : formatNumber(container?.汇率, 4)}
              </Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.freight')}>
                {headerEditing ? <InputNumber value={headerForm.运费} precision={2} controls={false} onChange={(value) => setHeaderForm((prev) => ({ ...prev, 运费: value ?? undefined }))} /> : formatNumber(container?.运费)}
              </Descriptions.Item>
              {/* 合计金额来自货柜主表汇总，编辑态也保持只读。 */}
              <Descriptions.Item label={t('containers.fields.domesticPriceTotal')}>{formatCurrency(container?.合计金额, '¥')}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.totalVolume')}>{formatNumber(container?.总体积, 4)}</Descriptions.Item>
              <Descriptions.Item label={t('containers.fields.remark')} span={3}>
                {headerEditing ? <Input.TextArea value={headerForm.备注} rows={2} onChange={(event) => setHeaderForm((prev) => ({ ...prev, 备注: event.target.value }))} /> : container?.备注 || '--'}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card className="container-detail-grid-card">
            <div ref={setGridContentElement} className="container-detail-grid-content">
              <div ref={setToolbarElement} className="container-detail-sticky-controls">
                <div className="container-detail-toolbar">
                  <div className="container-detail-action-row">
                    <Space wrap size={[6, 6]} className="container-detail-action-group">
                      <Dropdown
                        disabled={exporting}
                        menu={{
                          items: [
                            { key: 'excel', label: t('containers.actions.exportExcel', '导出 Excel') },
                            { key: 'pdf', label: t('containers.actions.exportPdf', '导出 PDF') },
                          ],
                          onClick: ({ key }) => {
                            if (key === 'excel') {
                              void exportDetails(DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS, 'excel')
                            }
                            if (key === 'pdf') {
                              void exportDetails(DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS, 'pdf')
                            }
                          },
                        }}
                      >
                        <Button size="small" icon={<DownloadOutlined />} loading={exporting}>
                          {t('common.export')}
                        </Button>
                      </Dropdown>
                      <Dropdown
                        menu={{
                          items: [
                            { key: 'columns', label: t('containers.actions.selectExportColumns', '选择导出列') },
                          ],
                          onClick: ({ key }) => {
                            if (key === 'columns') {
                              setExportFormatWithDefaults('excel')
                              setExportColumnModalOpen(true)
                            }
                          },
                        }}
                      >
                        <Button size="small">{t('containers.actions.exportOptions', '导出选项')}</Button>
                      </Dropdown>
                      {isColumnSettingsCustomized ? (
                        <Button size="small" icon={<ReloadOutlined />} onClick={resetColumnOrder}>
                          {t('containers.actions.resetColumns', '重置列')}
                        </Button>
                      ) : null}
                      {access.canEditContainer ? (
                        <Button size="small" loading={hqTranslating} onClick={() => void translateHqData()}>
                          {t('containers.actions.translateHqData')}
                        </Button>
                      ) : null}
                      {canSubmitContainer ? (
                        <Tooltip title={pendingDetailSaveCount > 0 || pendingPricePatchCount > 0 ? t('containers.messages.savePendingPriceDetailsFirst', '请先点击“保存明细”保存进口价格/贴牌价格') : ''}>
                          <Button
                            size="small"
                            type="primary"
                            icon={<CheckCircleOutlined />}
                            loading={submitContainerLoading}
                            disabled={submitContainerLoading || pendingDetailSaveCount > 0 || pendingPricePatchCount > 0}
                            onClick={() => void submitContainer()}
                          >
                            {t('containers.actions.submitContainer', '提交货柜')}
                          </Button>
                        </Tooltip>
                      ) : null}
                      {access.canManagePosProducts ? (
                        <Tooltip title={!selectedRowKeys.length ? t('containers.messages.selectProducts') : ''}>
                          <Button
                            size="small"
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
                              { key: 'batchFloatRate', label: t('containers.actions.batchUpdateFloatRate', '批量修改浮率'), disabled: batchFloatRateSaving },
                              { key: 'batchPrices', label: t('containers.actions.batchUpdatePrices', '批量修改价格'), disabled: batchPricesSaving },
                              ...(canBackfillLastPrices
                                ? [{ key: 'backfillLastPrices', label: t('containers.actions.backfillLastPrices', '回填上次价格'), disabled: backfillLastPricesLoading }]
                                : []),
                              { key: 'matchDomesticData', label: t('containers.actions.matchDomesticData'), disabled: matchDomesticDataLoading },
                              { type: 'divider' },
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
                              if (key === 'batchFloatRate') void openBatchFloatRateModal()
                              if (key === 'batchPrices') void openBatchPricesModal()
                              if (key === 'backfillLastPrices') void handleBackfillLastPrices()
                              if (key === 'matchDomesticData') void handleMatchDomesticData()
                              if (key === 'translate') void translateNames()
                              if (key === 'editEnglishName') void openBatchEditEnglishName()
                              if (key === 'clearEnglishName') void clearEnglishNames()
                              if (key === 'batchCategory') void openBatchCategory()
                              if (key === 'createNew') void createNewProducts()
                              if (key === 'updatePurchase') void updateExistingPurchase()
                              if (key === 'active') void applyActive(true)
                              if (key === 'inactive') void applyActive(false)
                            },
                          }}
                        >
                          <Button size="small">{t('containers.actions.batchActions')}</Button>
                        </Dropdown>
                      ) : null}
                      {access.canDeleteContainer ? <Button size="small" danger icon={<DeleteOutlined />} onClick={deleteSelected}>{t('containers.actions.deleteDetails')}</Button> : null}
                    </Space>
                    <Space wrap size={[8, 4]} className="container-detail-action-meta">
                      <Typography.Text type="secondary" className="container-detail-loaded-count">
                        {t('containers.text.loadedRows', '已加载 {{loaded}} / 共 {{total}} 条', {
                          loaded: rows.length,
                          total: detailItemsTotal,
                        })}
                        {filteredRows.length !== rows.length ? ` ${t('containers.text.visibleRows', '当前可见 {{count}} 条', { count: filteredRows.length })}` : ''}
                        {detailLoadingMore ? ` ${t('common.loading', '加载中')}` : ''}
                      </Typography.Text>
                      {hasActiveColumnState ? (
                        <Button size="small" className="container-detail-compact-button" onClick={() => {
                          setColumnFilters({})
                          setSortState(DEFAULT_CONTAINER_DETAIL_SORT)
                        }}>
                          {t('containers.actions.clearColumnFilters', '清空列过滤')}
                        </Button>
                      ) : null}
                      <Space size={6} className="container-detail-readonly-toggle">
                        <Typography.Text type="secondary">{t('containers.actions.showReadonlyOemPrice', '只读贴牌价格')}</Typography.Text>
                        <Switch
                          size="small"
                          checked={showReadonlyOemPrice}
                          onChange={setShowReadonlyOemPrice}
                        />
                      </Space>
                    </Space>
                  </div>

                  {access.canEditContainer ? (
                    <Space wrap size={[6, 6]} className="container-detail-bulk-row">
                      <Button
                        size="small"
                        icon={<SaveOutlined />}
                        loading={priceDetailsSaving}
                        disabled={!pendingPricePatchCount || priceDetailsSaving}
                        onClick={() => void savePendingPriceDetails()}
                      >
                        {t('containers.actions.saveDetails', '保存明细')}{pendingPricePatchCount ? ` (${pendingPricePatchCount})` : ''}
                      </Button>
                    </Space>
                  ) : null}

                  <ContainerTagFilters
                    tagStatOptions={tagStatOptions}
                    tagStats={tagStats}
                    selectedTagFilters={selectedTagFilters}
                    selectedTagOptions={selectedTagOptions}
                    onToggleTagFilter={toggleTagFilter}
                    onSetTagFilters={setTagFiltersFromSelect}
                  />

                  {exporting ? <Progress percent={exportProgress} size="small" /> : null}
                </div>
              </div>

              <div ref={setTableRegionElement} className="container-detail-table-region">
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
                      scroll={{ x: tableScrollX, y: tableScrollY }}
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
              </div>
            </div>
          </Card>
        </Space>
      </Spin>
      <Modal
        title={t('containers.setCode.pricesTitle', {
          item: setCodeModalRow ? getContainerDetailItemNumber(setCodeModalRow) ?? getContainerDetailProductCode(setCodeModalRow) ?? '' : '',
        })}
        open={setCodeModalOpen}
        width={680}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        okButtonProps={{
          disabled: !access.canEditContainer || changedSetCodePriceItems.length === 0,
          loading: setCodeSaving,
        }}
        onOk={() => void saveSetCodePrices()}
        onCancel={closeSetCodeModal}
        destroyOnHidden
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
        okText={exportFormat === 'pdf' ? t('containers.actions.exportPdf', '导出 PDF') : t('containers.actions.exportExcel', '导出 Excel')}
        cancelText={t('common.cancel')}
        okButtonProps={{ disabled: selectedExportColumnKeys.length === 0, loading: exporting }}
        onOk={() => {
          setExportColumnModalOpen(false)
          void exportDetails(selectedExportColumnKeys, exportFormat)
        }}
        onCancel={() => setExportColumnModalOpen(false)}
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Space size={8}>
            <Typography.Text type="secondary">{t('containers.actions.exportFormat', '导出格式')}</Typography.Text>
            <Radio.Group
              size="small"
              value={exportFormat}
              onChange={(event) => setExportFormatWithDefaults(event.target.value)}
              optionType="button"
              buttonStyle="solid"
              options={[
                { label: t('containers.actions.exportExcel', '导出 Excel'), value: 'excel' },
                { label: t('containers.actions.exportPdf', '导出 PDF'), value: 'pdf' },
              ]}
            />
          </Space>
          <Typography.Text type="secondary">
            {selectedRowKeys.length
              ? t('containers.text.exportSelectedRowsHint', '将导出已选择的 {{count}} 个商品。', { count: selectedRowKeys.length })
              : t('containers.text.exportAllRowsHint', '未选择商品时，将按当前筛选和排序导出全部匹配商品。')}
          </Typography.Text>
          <Space wrap>
            <Button size="small" onClick={() => setSelectedExportColumnKeys(CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => column.key))}>
              {t('containers.actions.selectAllExportColumns', '全选')}
            </Button>
            <Button size="small" onClick={() => setSelectedExportColumnKeys(
              exportFormat === 'pdf' ? DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS : DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
            )}>
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
