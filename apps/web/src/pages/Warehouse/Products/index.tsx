import { AppstoreOutlined, CloudSyncOutlined, CopyOutlined, DownloadOutlined, EditOutlined, GiftOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons';
import { DndContext, PointerSensor, closestCenter, type DragEndEvent, useSensor, useSensors, } from '@dnd-kit/core';
import { SortableContext, horizontalListSortingStrategy, useSortable, } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { Button, Card, Checkbox, Form, Image, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, Tooltip, TreeSelect, Typography, message, notification, } from 'antd';
import type { DefaultOptionType } from 'antd/es/select';
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table';
import type { FilterDropdownProps, FilterValue, SorterResult } from 'antd/es/table/interface';
import type { CSSProperties, HTMLAttributes, ReactNode } from 'react';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BarcodePreview from '../../../components/BarcodePreview';
import PageContainer from '../../../components/PageContainer';
import { getSupplierOptions, } from '../../../services/domesticProductService';
import { exportDomesticProductsToExcel, type ExportResult } from '../../../services/exportService';
import { getActiveLocalSuppliers as getActiveAustralianSuppliers } from '../../../services/localSupplierService';
import { batchCreateSetCodes, batchDelete as batchDeleteSetCodes, batchUpdateBarcodes as batchUpdateSetBarcodes, batchUpdatePrices as batchUpdateSetPrices, batchUpdateStatus as batchUpdateSetStatus, getGridData as getSetCodeGridData, } from '../../../services/multiCodeSetService';
import { HqProductSyncPollingCancelledError, HqProductSyncPollingTimeoutError, batchToggleWarehouseProductsActive, batchUpdateWarehouseProducts, createWarehouseProductHqSyncJob, createWarehouseProductHqSyncJobPoller, getWarehouseProductHqSyncJob, getWarehouseProductsTable, updateWarehouseProductFull, type WarehouseProductBatchUpdateItem, type WarehouseProductHqSyncJobResult, type WarehouseProductHqSyncJobStatus, type WarehouseProductListItem, type WarehouseProductsTableQuery, } from '../../../services/warehouseProductService';
import { batchAssignProducts, getCategoryTree, type WarehouseCategoryNode, } from '../../../services/warehouseCategoryService';
import { useAuthStore } from '../../../store/auth';
import type { SupplierOption, } from '../../../types/domesticProduct';
import { ProductType, ProductTypeLabels } from '../../../types/domesticProduct';
import type { MulticodeSetItem } from '../../../types/multiCodeSet';
import { copyTextToClipboard } from '../../../utils/clipboard';
import { isWarehouseProductColumnOrderCustomized, mergeWarehouseProductColumnOrder, moveWarehouseProductColumnOrder, type WarehouseProductTableColumnKey, } from './columnOrder';
import CreateProductModal from './CreateProductModal';
import CategoryTreePicker from './CategoryTreePicker';
import ImportFromDomesticModal from './ImportFromDomesticModal';
import ImportNonHbModal from './ImportNonHbModal';
import { buildWarehouseCategoryLookup, formatWarehouseCategoryNodeName, getWarehouseProductCategoryTooltip, type WarehouseCategoryLookup, } from './categoryPath';
import { ALL_PRODUCTS_FILTER_KEY, UNCATEGORIZED_PRODUCTS_FILTER_KEY, buildFilterCategoryOptions, buildFilterCategoryTreeOptions, } from '../Categories/categoryProductFilters';
import { buildCategoryQueryValue, buildComparableFilterTokens, buildTextFilterTokens, getSingleFilterValue, normalizeTableFilters, parseComparableFilterTokens, parseTextFilterTokens, resolveCategoryFilterValueFromTableFilters, setFilterValues, type ComparableFilterMode, type TextFilterMode, type WarehouseProductColumnFilters, } from './columnFilters';
interface ProductFormValues {
    supplierCode?: string;
    productName: string;
    englishProductName?: string;
    hbProductNo?: string;
    barcode?: string;
    productSpecification?: string;
    productType: ProductType;
    domesticPrice?: number;
    oemPrice?: number;
    importPrice?: number;
    packingQuantity?: number;
    unitVolume?: number;
    middlePackQuantity?: number;
    packingSize?: string;
    material?: string;
    remarks?: string;
    productImage?: string;
    isActive: boolean;
}
interface BatchEditFormValues {
    domesticPrice?: number;
    oemPrice?: number;
    importPrice?: number;
    packingQuantity?: number;
    minOrderQuantity?: number;
    unitVolume?: number;
    isActive?: boolean;
}
const getStatusOptions = (t: ReturnType<typeof useTranslation>['t']) => [
    { value: true, label: getShelfStatusLabel(true, t) },
    { value: false, label: getShelfStatusLabel(false, t) },
];
function getShelfStatusLabel(isActive: boolean, t: ReturnType<typeof useTranslation>['t']) {
    return isActive ? t('warehouse.onShelf', '上架') : t('warehouse.offShelf', '下架');
}
function getProductTypeLabel(value: ProductType, t: ReturnType<typeof useTranslation>['t']) {
    const keyMap: Record<ProductType, string> = {
        [0]: 'warehouse.normal',
        [1]: 'warehouse.hasSet',
        [2]: 'warehouse.hasMultiCode',
    };
    return t(keyMap[value]) || ProductTypeLabels[value] || '--';
}
function getProductTypeTagColor(value: ProductType) {
    if (value === ProductType.SET) return 'gold';
    if (value === ProductType.MULTICODE) return 'blue';
    return 'default';
}
function canManageProductDetails(productType: ProductType) {
    return productType === ProductType.SET || productType === ProductType.MULTICODE;
}
function getProductDetailsActionLabel(productType: ProductType, t: ReturnType<typeof useTranslation>['t']) {
    return productType === ProductType.MULTICODE
        ? t('warehouse.multiCodeManagement', '多码管理')
        : t('warehouse.setSubItems');
}
function getProductDetailsDisabledHint(t: ReturnType<typeof useTranslation>['t']) {
    return t('warehouse.normalProductNoDetails', '普通商品没有套装或多码明细');
}
function getProductDetailsModalTitle(product: WarehouseProductListItem | null, t: ReturnType<typeof useTranslation>['t']) {
    const name = product?.itemNumber || product?.name;
    if (!product) return t('warehouse.setDetails');
    return product.productType === ProductType.MULTICODE
        ? t('warehouse.multiCodeDetailsTitle', '多码管理 - {{name}}', { name })
        : t('warehouse.setDetailsTitle', { name });
}
function getProductDetailsHint(productType: ProductType | undefined, t: ReturnType<typeof useTranslation>['t']) {
    return productType === ProductType.MULTICODE
        ? t('warehouse.multiCodeEditHint', '多码商品可维护多码条码、价格和分店同步使用的明细。')
        : t('warehouse.setEditHint');
}
function getProductTypeOptions(t: ReturnType<typeof useTranslation>['t']) {
    return Object.keys(ProductTypeLabels).map((value) => ({
        value: Number(value),
        label: getProductTypeLabel(Number(value) as ProductType, t),
    }));
}
const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 60;
const warehouseProductsTableStyle = `
  /* 主表紧凑模式压缩行高、间距和媒体尺寸，减少首屏横向滚动。 */
  .warehouse-products-table .ant-table-thead > tr > th,
  .warehouse-products-table .ant-table-tbody > tr > td {
    padding: 4px 6px !important;
    line-height: 1.2;
  }

  .warehouse-products-table .ant-table-tbody > tr > td {
    height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    max-height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    vertical-align: middle;
  }

  .warehouse-products-table .ant-table-cell {
    white-space: nowrap;
  }

  .warehouse-products-table .ant-table-column-title {
    display: -webkit-box;
    overflow: hidden;
    white-space: normal;
    line-height: 1.15;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
  }

  .warehouse-products-table .ant-table-filter-column {
    display: flex;
    align-items: center;
    gap: 4px;
    min-width: 0;
  }

  .warehouse-products-table .ant-table-column-sorters {
    gap: 4px;
    padding: 0;
  }

  .warehouse-products-table .ant-table-filter-column-title,
  .warehouse-products-table .ant-table-column-title {
    min-width: 0;
  }

  .warehouse-products-table .ant-table-filter-trigger {
    flex: 0 0 auto;
    margin-inline: 0;
  }

  .warehouse-products-table .warehouse-products-image-cell,
  .warehouse-products-table .warehouse-products-barcode-cell {
    min-height: 48px;
    max-height: 48px;
    overflow: hidden;
    display: flex;
    align-items: center;
  }

  .warehouse-products-table .warehouse-products-image-cell .ant-image,
  .warehouse-products-table .warehouse-products-image-cell img {
    width: 36px;
    height: 36px;
    display: block;
  }

  .warehouse-products-table .warehouse-products-barcode-cell svg,
  .warehouse-products-table .warehouse-products-barcode-cell canvas,
  .warehouse-products-table .warehouse-products-barcode-cell img {
    max-height: 42px !important;
  }

  .warehouse-products-table .warehouse-products-supplier-cell {
    white-space: normal;
    overflow: hidden;
  }

  .warehouse-products-table .warehouse-products-supplier-cell .ant-tag {
    max-width: 100%;
    margin-inline-end: 0;
    white-space: normal;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    line-height: 18px;
    padding-block: 1px;
  }

  .warehouse-products-table .warehouse-products-text-2line {
    white-space: normal;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    line-height: 18px;
    word-break: break-word;
  }

  .warehouse-products-table .warehouse-products-category-cell {
    max-width: 100%;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .warehouse-products-table .warehouse-products-draggable-header {
    display: flex;
    align-items: center;
    width: 100%;
    min-width: 0;
    cursor: move;
  }

  .warehouse-products-table .warehouse-products-draggable-header > * {
    min-width: 0;
  }

  .warehouse-products-column-filter-panel {
    width: 240px;
    padding: 12px;
  }

  .warehouse-products-column-filter-body {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }

  .warehouse-products-column-filter-body .ant-select,
  .warehouse-products-column-filter-body .ant-input,
  .warehouse-products-column-filter-body .ant-input-number {
    width: 100%;
  }

  .warehouse-products-column-filter-body .ant-space-compact {
    width: 100%;
  }

  .warehouse-products-column-filter-body .ant-space-compact .ant-input-number {
    flex: 1 1 0;
    min-width: 0;
  }

  .warehouse-products-column-filter-actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 12px;
    padding-top: 10px;
    border-top: 1px solid #f0f0f0;
  }
`;
function formatDateTime(value?: string, language?: string) {
    if (!value) {
        return '--';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }
    const locale = language === 'en' ? 'en-AU' : 'zh-CN';
    return date.toLocaleString(locale, { hour12: false });
}
function formatPrice(value?: number) {
    if (value === undefined || value === null) {
        return '--';
    }
    return value.toFixed(2);
}
type SupplierSelectOption = DefaultOptionType & {
    searchText?: string;
};
type ActiveWarehouseProductHqSyncJob = {
    jobId: string;
    operationId: string;
    createdAt: string;
    status?: WarehouseProductHqSyncJobStatus | string;
    message?: string;
};
const WAREHOUSE_PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY = 'warehouse.products.activeHqSyncJob';
const WAREHOUSE_PRODUCT_HQ_SYNC_OPERATION_ID = 'warehouse-products-hq-sync';
const WAREHOUSE_PRODUCT_HQ_SYNC_POLL_INTERVAL_MS = 2000;
const WAREHOUSE_PRODUCT_HQ_SYNC_TIMEOUT_MS = 10 * 60 * 1000;
// 仓库商品主表数据量大，默认展示 100 条并保留大分页选项，继续依赖现有虚拟表格和服务端分页。
const WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE = 100;
const WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS = ['50', '100', '200', '500', '1000'];
const WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.warehouseProducts.columnOrder.v1';
const WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER = [
    'rowNumber',
    'itemNumber',
    'productImage',
    'domesticSupplierCode',
    'categoryName',
    'nameEn',
    'minOrderQuantity',
    'domesticPrice',
    'importPrice',
    'labelPrice',
    'isActive',
    'productType',
    'barcode',
    'name',
    'packingQty',
    'volume',
    'localSupplierCode',
    'updatedAt',
    'updatedBy',
    'action',
] as const;
interface DraggableHeaderCellProps extends HTMLAttributes<HTMLTableCellElement> {
    'data-column-key'?: string;
}
function DraggableHeaderCell({ children, style, ...props }: DraggableHeaderCellProps) {
    const columnKey = props['data-column-key'];
    const { attributes, listeners, setNodeRef, transform, transition, isDragging, } = useSortable({
        id: columnKey ?? '__warehouse-product-static-column__',
        disabled: !columnKey,
    });
    if (!columnKey) {
        return <th style={style} {...props}>{children}</th>;
    }
    const headerStyle: CSSProperties = {
        ...style,
        transform: CSS.Translate.toString(transform),
        transition,
        cursor: 'move',
        zIndex: isDragging ? 3 : style?.zIndex,
        opacity: isDragging ? 0.85 : style?.opacity,
    };
    return (<th ref={setNodeRef} style={headerStyle} {...props} {...attributes} {...listeners}>
      <div className="warehouse-products-draggable-header">
        {children}
      </div>
    </th>);
}
function readActiveWarehouseProductHqSyncJob(): ActiveWarehouseProductHqSyncJob | null {
    if (typeof window === 'undefined') {
        return null;
    }
    try {
        const raw = window.localStorage.getItem(WAREHOUSE_PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY);
        if (!raw) {
            return null;
        }
        const parsed = JSON.parse(raw) as Partial<ActiveWarehouseProductHqSyncJob>;
        if (!parsed.jobId || !parsed.operationId || !parsed.createdAt) {
            return null;
        }
        return parsed as ActiveWarehouseProductHqSyncJob;
    }
    catch {
        return null;
    }
}
function saveActiveWarehouseProductHqSyncJob(job: ActiveWarehouseProductHqSyncJob | null) {
    if (typeof window === 'undefined') {
        return;
    }
    if (!job) {
        window.localStorage.removeItem(WAREHOUSE_PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY);
        return;
    }
    window.localStorage.setItem(WAREHOUSE_PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY, JSON.stringify(job));
}
function buildSupplierOptions(suppliers: SupplierOption[]): SupplierSelectOption[] {
    return suppliers.map((item) => ({
        value: item.code,
        label: `${item.code} - ${item.name}`,
        searchText: `${item.code} ${item.name} ${item.shopNumber ?? ''}`.toLowerCase(),
    }));
}
function filterSupplierOption(input: string, option?: DefaultOptionType) {
    return String((option as SupplierSelectOption | undefined)?.searchText ?? '')
        .includes(input.trim().toLowerCase());
}
function collectCategoryExpandedKeys(nodes: WarehouseCategoryNode[], maxLevel: number, level = 1): string[] {
    if (level > maxLevel) {
        return [];
    }
    return nodes.flatMap((node) => [
        node.categoryGUID,
        ...collectCategoryExpandedKeys(node.children || [], maxLevel, level + 1),
    ]);
}
function findWarehouseCategory(nodes: WarehouseCategoryNode[], targetGuid?: string): WarehouseCategoryNode | undefined {
    if (!targetGuid) {
        return undefined;
    }
    for (const node of nodes) {
        if (node.categoryGUID === targetGuid) {
            return node;
        }
        const matched = findWarehouseCategory(node.children || [], targetGuid);
        if (matched) {
            return matched;
        }
    }
    return undefined;
}
function renderWarehouseProductCategoryCell(record: WarehouseProductListItem, categoryLookup: WarehouseCategoryLookup, language?: string) {
    const displayName = record.categoryName || '--';
    const tooltipTitle = getWarehouseProductCategoryTooltip(record, categoryLookup, language);
    // 单元格只显示分类名称，完整多级路径放到 Tooltip，保持主表紧凑可扫读。
    return (<Tooltip title={tooltipTitle}>
      <div className="warehouse-products-category-cell">{displayName}</div>
    </Tooltip>);
}
function ProductFormModal({ open, saving, editingItem, suppliers, form, onCancel, onSubmit, }: {
    open: boolean;
    saving: boolean;
    editingItem: WarehouseProductListItem | null;
    suppliers: SupplierOption[];
    form: ReturnType<typeof Form.useForm<ProductFormValues>>[0];
    onCancel: () => void;
    onSubmit: () => void;
}) {
    const { t } = useTranslation();
    const productTypeOptions = getProductTypeOptions(t);
    return (<Modal title={editingItem ? t('warehouse.editProductTitle', { name: editingItem.itemNumber || editingItem.name }) : t('warehouse.editProduct')} open={open} width={920} destroyOnClose okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={saving} onCancel={onCancel} onOk={onSubmit}>
      <Form form={form} layout="vertical" preserve={false}>
        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="supplierCode" label={t('domesticProducts.supplier')} style={{ flex: 1 }} rules={editingItem ? [] : [{ required: true, message: t('domesticProducts.selectSupplier') }]}>
            <Select disabled={Boolean(editingItem)} placeholder={t('domesticProducts.selectSupplier')} showSearch filterOption={filterSupplierOption} options={buildSupplierOptions(suppliers)}/>
          </Form.Item>
          <Form.Item name="productType" label={t('warehouse.productType')} style={{ width: 180 }} rules={[{ required: true, message: t('warehouse.selectProductType') }]}>
            <Select placeholder={t('warehouse.selectProductType')} options={productTypeOptions}/>
          </Form.Item>
          <Form.Item name="isActive" label={t('warehouse.isListed')} valuePropName="checked" style={{ width: 120 }}>
            <Switch checkedChildren={getShelfStatusLabel(true, t)} unCheckedChildren={getShelfStatusLabel(false, t)}/>
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="productName" label={t('domesticProducts.productName')} style={{ flex: 1 }} rules={[{ required: true, message: t('warehouse.enterProductName') }]}>
            <Input placeholder={t('warehouse.enterProductName')}/>
          </Form.Item>
          <Form.Item name="englishProductName" label={t('warehouse.englishName')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterEnglishName')}/>
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="hbProductNo" label={t('warehouse.hbProductNo')} style={{ flex: 1 }}>
            <Input disabled={Boolean(editingItem)} placeholder={t('warehouse.autoGenerate')}/>
          </Form.Item>
          <Form.Item name="barcode" label={t('domesticProducts.barcode')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.autoGenerate')}/>
          </Form.Item>
          <Form.Item name="productSpecification" label={t('domesticProducts.specification')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterSpec')}/>
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="domesticPrice" label={t('domesticProducts.domesticPrice')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }}/>
          </Form.Item>
          <Form.Item name="oemPrice" label={t('productCreation.privateLabelPrice')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }}/>
          </Form.Item>
          <Form.Item name="importPrice" label={t('warehouse.importPrice')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={2} style={{ width: '100%' }}/>
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingQuantity" label={t('warehouse.packingQuantity')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }}/>
          </Form.Item>
          <Form.Item name="middlePackQuantity" label={t('warehouse.middlePackQuantity')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={0} style={{ width: '100%' }}/>
          </Form.Item>
          <Form.Item name="unitVolume" label={t('warehouse.volume')} style={{ flex: 1 }}>
            <InputNumber min={0} precision={4} style={{ width: '100%' }}/>
          </Form.Item>
        </Space>

        <Space size={16} style={{ display: 'flex' }} align="start">
          <Form.Item name="packingSize" label={t('warehouse.packingSize')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterPackingSize')}/>
          </Form.Item>
          <Form.Item name="material" label={t('warehouse.material')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterMaterial')}/>
          </Form.Item>
          <Form.Item name="productImage" label={t('warehouse.imageUrl')} style={{ flex: 1 }}>
            <Input placeholder={t('warehouse.enterImageUrl')}/>
          </Form.Item>
        </Space>

        <Form.Item name="remarks" label={t('common.remarks')}>
          <Input.TextArea rows={3} placeholder={t('common.enterRemarks')}/>
        </Form.Item>
      </Form>
    </Modal>);
}
function SetItemsModal({ open, loading, saving, product, items, canEdit, onCancel, onAddRow, onRemoveRow, onChangeField, onSubmit, }: {
    open: boolean;
    loading: boolean;
    saving: boolean;
    product: WarehouseProductListItem | null;
    items: MulticodeSetItem[];
    canEdit: boolean;
    onCancel: () => void;
    onAddRow: () => void;
    onRemoveRow: (rowId: string) => void;
    onChangeField: (rowId: string, field: keyof MulticodeSetItem, value: string | number | boolean | undefined) => void;
    onSubmit: () => void;
}) {
    const { t } = useTranslation();
    const isNewSetCode = (record: MulticodeSetItem) => record.id?.startsWith('new_') ?? false;
    const columns: ColumnsType<MulticodeSetItem> = [
        {
            title: t('warehouse.setProductNo'),
            dataIndex: 'setItemNumber',
            width: 180,
            render: (_, record) => (<Input value={record.setItemNumber} disabled={!canEdit || !isNewSetCode(record)} onChange={(event) => onChangeField(record.id!, 'setItemNumber', event.target.value)}/>),
        },
        {
            title: t('domesticProducts.barcode'),
            dataIndex: 'setBarcode',
            width: 180,
            render: (_, record) => (<Input value={record.setBarcode} disabled={!canEdit} onChange={(event) => onChangeField(record.id!, 'setBarcode', event.target.value)}/>),
        },
        {
            title: t('posAdmin.invoiceDetail.purchasePrice', '进货价'),
            dataIndex: 'setPurchasePrice',
            width: 120,
            render: (_, record) => (<InputNumber min={0} precision={2} value={record.setPurchasePrice} disabled={!canEdit} style={{ width: '100%' }} onChange={(value) => onChangeField(record.id!, 'setPurchasePrice', value ?? undefined)}/>),
        },
        {
            title: t('posAdmin.invoiceDetail.retailPrice', '零售价'),
            dataIndex: 'setRetailPrice',
            width: 120,
            render: (_, record) => (<InputNumber min={0} precision={2} value={record.setRetailPrice} disabled={!canEdit} style={{ width: '100%' }} onChange={(value) => onChangeField(record.id!, 'setRetailPrice', value ?? undefined)}/>),
        },
        {
            title: t('column.status', '状态'),
            dataIndex: 'isActive',
            width: 120,
            render: (_, record) => (<Switch checked={record.isActive ?? true} disabled={!canEdit} checkedChildren={getShelfStatusLabel(true, t)} unCheckedChildren={getShelfStatusLabel(false, t)} onChange={(checked) => onChangeField(record.id!, 'isActive', checked)}/>),
        },
        {
            title: t('common.action'),
            key: 'action',
            width: 90,
            render: (_, record) => canEdit ? (<Button danger type="link" onClick={() => onRemoveRow(record.id!)}>
            {t('common.delete')}
          </Button>) : null,
        },
    ];
    return (<Modal title={getProductDetailsModalTitle(product, t)} open={open} width={1100} destroyOnClose onCancel={onCancel} onOk={onSubmit} okText={t('common.save')} cancelText={t('common.close')} confirmLoading={saving} okButtonProps={{ disabled: !canEdit }}>
      <Space style={{ marginBottom: 16 }}>
        <Typography.Text type="secondary">
          {getProductDetailsHint(product?.productType, t)}
        </Typography.Text>
        {canEdit ? (<Button type="dashed" onClick={onAddRow}>
            {product?.productType === ProductType.MULTICODE ? t('warehouse.addMultiCodeDetail', '新增多码') : t('warehouse.addSubItem')}
          </Button>) : null}
      </Space>
      <Table rowKey="id" loading={loading} columns={columns} dataSource={items} pagination={false} scroll={{ x: 980, y: 420 }}/>
    </Modal>);
}
export default function WarehouseProductsPage() {
    const { t, i18n } = useTranslation();
    const productTypeOptions = getProductTypeOptions(t);
    const [form] = Form.useForm<ProductFormValues>();
    const [batchEditForm] = Form.useForm<BatchEditFormValues>();
    const [loading, setLoading] = useState(false);
    const [saving, setSaving] = useState(false);
    const [modalOpen, setModalOpen] = useState(false);
    const [createModalOpen, setCreateModalOpen] = useState(false);
    const [editingItem, setEditingItem] = useState<WarehouseProductListItem | null>(null);
    const [suppliers, setSuppliers] = useState<SupplierOption[]>([]);
    const [localSupplierNameMap, setLocalSupplierNameMap] = useState<Record<string, string>>({});
    const [categories, setCategories] = useState<WarehouseCategoryNode[]>([]);
    const [categoryLoading, setCategoryLoading] = useState(false);
    const [categoryFilterValue, setCategoryFilterValue] = useState<string>(ALL_PRODUCTS_FILTER_KEY);
    const [categoryFilterExpandedKeys, setCategoryFilterExpandedKeys] = useState<string[]>([]);
    const [categoryFilterSearchText, setCategoryFilterSearchText] = useState('');
    const [columnFilters, setColumnFilters] = useState<WarehouseProductColumnFilters>({});
    const [categoryExpandedKeys, setCategoryExpandedKeys] = useState<string[]>([]);
    const [batchCategoryOpen, setBatchCategoryOpen] = useState(false);
    const [targetCategoryGuid, setTargetCategoryGuid] = useState<string>();
    const [batchCategorySaving, setBatchCategorySaving] = useState(false);
    const [data, setData] = useState<WarehouseProductListItem[]>([]);
    const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);
    const [searchText, setSearchText] = useState('');
    const [supplierCode, setSupplierCode] = useState<string>();
    const [productType, setProductType] = useState<ProductType>();
    const [isActive, setIsActive] = useState<boolean>();
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(WAREHOUSE_PRODUCTS_DEFAULT_PAGE_SIZE);
    const [total, setTotal] = useState(0);
    const [sortField, setSortField] = useState('createdAt');
    const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend');
    const [exportConfigOpen, setExportConfigOpen] = useState(false);
    const [exporting, setExporting] = useState(false);
    const [includeLabelPrice, setIncludeLabelPrice] = useState(false);
    const [includeBarcodeImage, setIncludeBarcodeImage] = useState(true);
    const [includeProductImage, setIncludeProductImage] = useState(false);
    const [exportProgress, setExportProgress] = useState(0);
    const [exportMessage, setExportMessage] = useState('');
    const [importFromDomesticOpen, setImportFromDomesticOpen] = useState(false);
    const [importNonHbOpen, setImportNonHbOpen] = useState(false);
    const [setItemsOpen, setSetItemsOpen] = useState(false);
    const [setItemsLoading, setSetItemsLoading] = useState(false);
    const [setItemsSaving, setSetItemsSaving] = useState(false);
    const [currentSetProduct, setCurrentSetProduct] = useState<WarehouseProductListItem | null>(null);
    const [setItemsDraft, setSetItemsDraft] = useState<MulticodeSetItem[]>([]);
    const [deletedSetCodeIds, setDeletedSetCodeIds] = useState<string[]>([]);
    const [batchActionLoading, setBatchActionLoading] = useState(false);
    const [batchEditOpen, setBatchEditOpen] = useState(false);
    const [batchEditSaving, setBatchEditSaving] = useState(false);
    const [togglingProductCodes, setTogglingProductCodes] = useState<string[]>([]);
    const [exportFailDetailOpen, setExportFailDetailOpen] = useState(false);
    const [exportFailDetail, setExportFailDetail] = useState<ExportResult['failedProductImages']>([]);
    const [syncingFromHq, setSyncingFromHq] = useState(false);
    const [activeHqSyncJob, setActiveHqSyncJob] = useState<ActiveWarehouseProductHqSyncJob | null>(null);
    const [columnOrder, setColumnOrder] = useState<WarehouseProductTableColumnKey[]>([]);
    const stopHqSyncJobPollingRef = useRef<(() => void) | null>(null);
    const isMountedRef = useRef(true);
    const loadDataRef = useRef<((overrides?: Partial<WarehouseProductsTableQuery>) => Promise<void>) | null>(null);
    const columnDragSensors = useSensors(useSensor(PointerSensor, {
        activationConstraint: {
            distance: 6,
        },
    }));
    const { access } = useAuthStore();
    const canImportNonHbProducts = access.isAdmin || access.isWarehouseManager;
    const categoryFilterOptions = useMemo(() => buildFilterCategoryOptions(categories, t, i18n.language), [categories, i18n.language, t]);
    const categoryFilterTreeOptions = useMemo(() => buildFilterCategoryTreeOptions(categories, t, i18n.language), [categories, i18n.language, t]);
    const hasCategoryFilterSearchText = categoryFilterSearchText.trim().length > 0;
    const domesticSupplierFilterOptions = useMemo(() => buildSupplierOptions(suppliers).map((item) => ({
        text: String(item.label),
        value: String(item.value),
    })), [suppliers]);
    const localSupplierFilterOptions = useMemo(() => Object.entries(localSupplierNameMap)
        .sort(([leftCode, leftName], [rightCode, rightName]) => `${leftCode} ${leftName}`.localeCompare(`${rightCode} ${rightName}`))
        .map(([code, name]) => ({
        text: `${code} - ${name}`,
        value: code,
    })), [localSupplierNameMap]);
    const categoryColumnFilterOptions = useMemo(() => categoryFilterOptions
        .filter((option) => option.value !== ALL_PRODUCTS_FILTER_KEY)
        .map((option) => ({
        text: option.label,
        value: option.value,
    })), [categoryFilterOptions]);
    const categoryLookup = useMemo(() => buildWarehouseCategoryLookup(categories), [categories]);
    const selectedTargetCategory = useMemo(() => findWarehouseCategory(categories, targetCategoryGuid), [categories, targetCategoryGuid]);
    const selectedTargetCategoryPath = targetCategoryGuid ? getWarehouseProductCategoryTooltip({
        categoryName: selectedTargetCategory?.categoryName,
        warehouseCategoryGUID: targetCategoryGuid,
    }, categoryLookup, i18n.language) : undefined;
    const buildGridQuery = (overrides: Partial<WarehouseProductsTableQuery> = {}): WarehouseProductsTableQuery => {
        const categoryQuery = buildCategoryQueryValue(categoryFilterValue);
        const mergedFilters = overrides.filters ?? columnFilters;
        return {
            page,
            pageSize,
            searchText,
            supplierCode,
            productType,
            isActive,
            filters: Object.keys(mergedFilters).length ? mergedFilters : undefined,
            sortField,
            sortOrder,
            // 列头筛选走后端 Filters，分类仍走顶层字段，避免未分类语义被普通 Filters 吞掉。
            ...categoryQuery,
            ...overrides,
        };
    };
    const filterIcon = (filtered?: boolean) => <SearchOutlined style={{ color: filtered ? '#1677ff' : undefined }}/>;
    const textFilterModeOptions: Array<{ label: string; value: TextFilterMode; }> = [
        { label: t('warehouse.filterMode.contains', '包含'), value: 'contains' },
        { label: t('warehouse.filterMode.equals', '等于'), value: 'eq' },
        { label: t('warehouse.filterMode.startsWith', '开头是'), value: 'starts' },
        { label: t('warehouse.filterMode.endsWith', '结尾是'), value: 'ends' },
    ];
    const comparableFilterModeOptions: Array<{ label: string; value: ComparableFilterMode; }> = [
        { label: t('warehouse.filterMode.equals', '等于'), value: 'eq' },
        { label: t('warehouse.filterMode.range', '范围'), value: 'range' },
        { label: t('warehouse.filterMode.greaterOrEqual', '大于等于'), value: 'gte' },
        { label: t('warehouse.filterMode.lessOrEqual', '小于等于'), value: 'lte' },
    ];
    // 统一列头筛选面板骨架，文本/数字/日期只替换输入区，应用和重置语义保持一致。
    const renderColumnFilterPanel = (content: ReactNode, onApply: () => void, onReset: () => void) => (
        <div className="warehouse-products-column-filter-panel" onKeyDown={(event) => event.stopPropagation()} onMouseDown={(event) => event.stopPropagation()}>
          <div className="warehouse-products-column-filter-body">
            {content}
          </div>
          <div className="warehouse-products-column-filter-actions">
            <Button size="small" onClick={onReset}>{t('containers.actions.resetColumnFilter', '重置')}</Button>
            <Button size="small" type="primary" onClick={onApply}>{t('containers.actions.applyColumnFilter', '应用')}</Button>
          </div>
        </div>
    );
    const buildTextFilterDropdown = (filterKey: string, placeholder: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
        const values = (selectedKeys.length ? selectedKeys : columnFilters[filterKey] ?? []).map((value) => String(value));
        const parsedFilter = parseTextFilterTokens(values);
        const updateFilter = (mode: TextFilterMode, value?: string) => {
            setSelectedKeys(buildTextFilterTokens(mode, value));
        };
        return renderColumnFilterPanel((<>
            <Select size="small" value={parsedFilter.mode} options={textFilterModeOptions} onChange={(mode) => updateFilter(mode, parsedFilter.value)}/>
            <Input value={parsedFilter.value} placeholder={placeholder} allowClear onChange={(event) => {
                const nextValue = event.target.value;
                updateFilter(parsedFilter.mode, nextValue);
            }} onPressEnter={() => confirm()}/>
          </>), () => confirm(), () => {
                setSelectedKeys([]);
                clearFilters?.();
                confirm();
            });
    };
    const buildNumberRangeFilterDropdown = (filterKey: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
        const values = (selectedKeys.length ? selectedKeys : columnFilters[filterKey] ?? []).map((value) => String(value));
        const parsedFilter = parseComparableFilterTokens(values);
        const updateFilter = (mode: ComparableFilterMode, nextValue: { value?: string | number; min?: string | number; max?: string | number; }) => {
            setSelectedKeys(buildComparableFilterTokens(mode, nextValue));
        };
        return renderColumnFilterPanel((<>
            <Select size="small" value={parsedFilter.mode} options={comparableFilterModeOptions} onChange={(mode) => updateFilter(mode, {
                value: parsedFilter.value || parsedFilter.min || parsedFilter.max,
                min: parsedFilter.min,
                max: parsedFilter.max,
            })}/>
            {parsedFilter.mode === 'range' ? (<Space.Compact>
              <InputNumber value={parsedFilter.min !== '' ? Number(parsedFilter.min) : undefined} placeholder={t('containers.placeholders.minValue', '最小值')} controls={false} onChange={(nextValue) => updateFilter('range', {
                min: nextValue == null ? undefined : Number(nextValue),
                max: parsedFilter.max || undefined,
            })}/>
              <InputNumber value={parsedFilter.max !== '' ? Number(parsedFilter.max) : undefined} placeholder={t('containers.placeholders.maxValue', '最大值')} controls={false} onChange={(nextValue) => updateFilter('range', {
                min: parsedFilter.min || undefined,
                max: nextValue == null ? undefined : Number(nextValue),
            })}/>
            </Space.Compact>) : (<InputNumber value={parsedFilter.value !== '' ? Number(parsedFilter.value) : undefined} placeholder={t('containers.placeholders.filterValue', '筛选值')} controls={false} onChange={(nextValue) => updateFilter(parsedFilter.mode, {
                value: nextValue == null ? undefined : Number(nextValue),
            })}/>)}
          </>), () => confirm(), () => {
                setSelectedKeys([]);
                clearFilters?.();
                confirm();
            });
    };
    const buildDateRangeFilterDropdown = (filterKey: string) => ({ confirm, selectedKeys, setSelectedKeys, clearFilters }: FilterDropdownProps) => {
        const values = (selectedKeys.length ? selectedKeys : columnFilters[filterKey] ?? []).map((value) => String(value));
        const parsedFilter = parseComparableFilterTokens(values);
        const updateFilter = (mode: ComparableFilterMode, nextValue: { value?: string; min?: string; max?: string; }) => {
            setSelectedKeys(buildComparableFilterTokens(mode, nextValue));
        };
        return renderColumnFilterPanel((<>
            <Select size="small" value={parsedFilter.mode} options={comparableFilterModeOptions} onChange={(mode) => updateFilter(mode, {
                value: parsedFilter.value || parsedFilter.min || parsedFilter.max,
                min: parsedFilter.min,
                max: parsedFilter.max,
            })}/>
            {parsedFilter.mode === 'range' ? (<>
            <Input type="date" value={parsedFilter.min} onChange={(event) => updateFilter('range', {
                min: event.target.value || undefined,
                max: parsedFilter.max || undefined,
            })}/>
            <Input type="date" value={parsedFilter.max} onChange={(event) => updateFilter('range', {
                min: parsedFilter.min || undefined,
                max: event.target.value || undefined,
            })}/>
            </>) : (<Input type="date" value={parsedFilter.value} onChange={(event) => updateFilter(parsedFilter.mode, {
                value: event.target.value || undefined,
            })}/>)}
          </>), () => confirm(), () => {
                setSelectedKeys([]);
                clearFilters?.();
                confirm();
            });
    };
    const textFilterProps = (filterKey: string, placeholder: string) => ({
        filterDropdown: buildTextFilterDropdown(filterKey, placeholder),
        filterIcon,
        filtered: Boolean(columnFilters[filterKey]?.[0]?.trim()),
        filteredValue: columnFilters[filterKey] ?? null,
    });
    const numberRangeFilterProps = (filterKey: string) => ({
        filterDropdown: buildNumberRangeFilterDropdown(filterKey),
        filterIcon,
        filtered: Boolean(columnFilters[filterKey]?.length),
        filteredValue: columnFilters[filterKey] ?? null,
    });
    const dateRangeFilterProps = (filterKey: string) => ({
        filterDropdown: buildDateRangeFilterDropdown(filterKey),
        filterIcon,
        filtered: Boolean(columnFilters[filterKey]?.length),
        filteredValue: columnFilters[filterKey] ?? null,
    });
    const enumFilterProps = (filterKey: string, options: Array<{ text: string; value: string; }>) => ({
        filters: options,
        filterIcon,
        filtered: Boolean(columnFilters[filterKey]?.length),
        filteredValue: columnFilters[filterKey] ?? null,
        filterMultiple: true,
    });
    const loadData = async (overrides: Partial<WarehouseProductsTableQuery> = {}) => {
        const query = buildGridQuery(overrides);
        setLoading(true);
        try {
            const result = await getWarehouseProductsTable(query);
            setData(result.items);
            setTotal(result.total);
            setPage(result.page);
            setPageSize(result.pageSize);
            setSelectedRowKeys([]);
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.loadProductsFailed'));
        }
        finally {
            setLoading(false);
        }
    };
    loadDataRef.current = loadData;
    const stopHqSyncJobPolling = useCallback(() => {
        stopHqSyncJobPollingRef.current?.();
        stopHqSyncJobPollingRef.current = null;
    }, []);
    const saveActiveHqSyncJob = useCallback((job: ActiveWarehouseProductHqSyncJob) => {
        saveActiveWarehouseProductHqSyncJob(job);
        if (isMountedRef.current) {
            setActiveHqSyncJob(job);
        }
    }, []);
    const clearActiveHqSyncJob = useCallback(() => {
        saveActiveWarehouseProductHqSyncJob(null);
        if (isMountedRef.current) {
            setActiveHqSyncJob(null);
        }
    }, []);
    const buildHqSyncResultDescription = useCallback((result: WarehouseProductHqSyncJobResult) => {
        const syncResult = result.result ?? result;
        const messageText = syncResult.message ?? syncResult.Message ?? result.message ?? t('warehouse.hqSyncSuccess', '从HQ同步库存成功');
        const addedCount = syncResult.addedCount ?? syncResult.AddedCount ?? result.addedCount ?? 0;
        const updatedCount = syncResult.updatedCount ?? syncResult.UpdatedCount ?? result.updatedCount ?? 0;
        const errorCount = syncResult.errorCount ?? syncResult.ErrorCount ?? result.errorCount ?? 0;
        return (<Space direction="vertical" size={4}>
        <div>{messageText}</div>
        <div>{t('warehouse.hqSyncJobResultStats', '新增 {{added}} 条，更新 {{updated}} 条，错误 {{errors}} 条', { added: addedCount, updated: updatedCount, errors: errorCount })}</div>
      </Space>);
    }, [t]);
    const showHqSyncJobResult = useCallback((result: WarehouseProductHqSyncJobResult) => {
        const syncResult = result.result ?? result;
        const success = result.status !== 'Failed' && (syncResult.isSuccess ?? syncResult.IsSuccess ?? true);
        if (!success) {
            notification.error({
                message: t('warehouse.hqSyncJobFailed', '仓库商品 HQ 同步失败'),
                description: syncResult.message ?? syncResult.Message ?? result.message ?? t('warehouse.hqSyncFailed', '从HQ同步库存失败'),
                duration: 0,
                placement: 'topRight',
            });
            return;
        }
        const errorCount = syncResult.errorCount ?? syncResult.ErrorCount ?? result.errorCount ?? 0;
        if (errorCount > 0) {
            notification.warning({
                message: t('warehouse.hqSyncJobPartialSucceeded', '仓库商品 HQ 同步部分完成'),
                description: buildHqSyncResultDescription(result),
                duration: 0,
                placement: 'topRight',
            });
        }
        else {
            notification.success({
                message: t('warehouse.hqSyncJobSucceeded', '仓库商品 HQ 同步完成'),
                description: buildHqSyncResultDescription(result),
                duration: 6,
                placement: 'topRight',
            });
        }
        void loadDataRef.current?.({ page: 1 });
    }, [buildHqSyncResultDescription, t]);
    const startHqSyncJobPolling = useCallback((job: ActiveWarehouseProductHqSyncJob) => {
        stopHqSyncJobPolling();
        saveActiveHqSyncJob(job);
        const poller = createWarehouseProductHqSyncJobPoller({
            jobId: job.jobId,
            getJob: async (jobId) => {
                const result = await getWarehouseProductHqSyncJob(jobId);
                saveActiveHqSyncJob({
                    ...job,
                    status: result.status,
                    message: result.message,
                });
                return result;
            },
            pollIntervalMs: WAREHOUSE_PRODUCT_HQ_SYNC_POLL_INTERVAL_MS,
            timeoutMs: WAREHOUSE_PRODUCT_HQ_SYNC_TIMEOUT_MS,
        });
        stopHqSyncJobPollingRef.current = poller.stop;
        void poller.promise
            .then((result) => {
            if (!isMountedRef.current) {
                return;
            }
            clearActiveHqSyncJob();
            stopHqSyncJobPollingRef.current = null;
            showHqSyncJobResult(result);
        })
            .catch((error) => {
            if (!isMountedRef.current) {
                return;
            }
            if (error instanceof HqProductSyncPollingCancelledError) {
                return;
            }
            clearActiveHqSyncJob();
            stopHqSyncJobPollingRef.current = null;
            if (error instanceof HqProductSyncPollingTimeoutError) {
                notification.warning({
                    message: t('warehouse.hqSyncJobTimeoutTitle', '仓库商品 HQ 同步仍在后台执行'),
                    description: t('warehouse.hqSyncJobTimeout', '前端已停止轮询该同步任务。你可以稍后刷新列表，或重新提交以接管后端已有任务。'),
                    duration: 0,
                    placement: 'topRight',
                });
                return;
            }
            notification.error({
                message: t('warehouse.hqSyncJobFailed', '仓库商品 HQ 同步失败'),
                description: error instanceof Error ? error.message : t('warehouse.hqSyncFailed', '从HQ同步库存失败'),
                duration: 0,
                placement: 'topRight',
            });
        });
    }, [clearActiveHqSyncJob, saveActiveHqSyncJob, showHqSyncJobResult, stopHqSyncJobPolling, t]);
    const showActiveHqSyncJobStatus = useCallback((job: ActiveWarehouseProductHqSyncJob | null = activeHqSyncJob) => {
        if (!job) {
            return;
        }
        notification.info({
            message: t('warehouse.hqSyncJobStatusTitle', '仓库商品 HQ 同步正在后台执行'),
            description: (<Space direction="vertical" size={4}>
          <div>{t('warehouse.hqSyncJobId', '任务 ID')}: {job.jobId}</div>
          <div>{t('warehouse.hqSyncJobStatus', '任务状态')}: {job.status ?? 'Running'}</div>
          <div>{t('warehouse.hqSyncJobStartedAt', '提交时间')}: {formatDateTime(job.createdAt, i18n.language)}</div>
          {job.message ? <div>{job.message}</div> : null}
        </Space>),
            duration: 5,
            placement: 'topRight',
        });
    }, [activeHqSyncJob, i18n.language, t]);
    useEffect(() => {
        setCategoryLoading(true);
        void Promise.all([
            loadData({ page: 1 }),
            getSupplierOptions()
                .then(setSuppliers)
                .catch((error) => {
                console.error(error);
                message.error(t('productCreation.loadSupplierListFailed'));
            }),
            getActiveAustralianSuppliers()
                .then((items) => {
                const map: Record<string, string> = {};
                items.forEach((item) => {
                    if (item.localSupplierCode && item.name) {
                        map[item.localSupplierCode] = item.name;
                    }
                });
                setLocalSupplierNameMap(map);
            })
                .catch((error) => {
                console.error(error);
                message.error(t('warehouse.loadLocalSupplierFailed', '加载澳洲供应商失败'));
            }),
            getCategoryTree()
                .then((tree) => {
                setCategories(tree);
                const firstLevelExpandedKeys = collectCategoryExpandedKeys(tree, 1);
                setCategoryExpandedKeys(firstLevelExpandedKeys);
                setCategoryFilterExpandedKeys(firstLevelExpandedKeys);
            })
                .catch((error) => {
                console.error(error);
                message.error(error instanceof Error ? error.message : t('warehouse.categories.loadTreeFailed', '加载分类树失败'));
            })
                .finally(() => setCategoryLoading(false)),
        ]);
    }, []);
    useEffect(() => {
        isMountedRef.current = true;
        const restoredJob = readActiveWarehouseProductHqSyncJob();
        if (restoredJob?.jobId) {
            startHqSyncJobPolling(restoredJob);
        }
        return () => {
            isMountedRef.current = false;
            stopHqSyncJobPolling();
        };
    }, [startHqSyncJobPolling, stopHqSyncJobPolling]);
    const handleOpenCreate = () => {
        setCreateModalOpen(true);
    };
    const handleOpenEdit = (record: WarehouseProductListItem) => {
        setEditingItem(record);
        form.setFieldsValue({
            supplierCode: record.domesticSupplierCode,
            productName: record.name,
            englishProductName: record.nameEn,
            hbProductNo: record.itemNumber,
            barcode: record.barcode,
            productType: record.productType,
            domesticPrice: record.domesticPrice,
            oemPrice: record.labelPrice,
            importPrice: record.importPrice,
            packingQuantity: record.packingQty,
            unitVolume: record.volume,
            middlePackQuantity: record.middlePackQty,
            productImage: record.productImage,
            isActive: record.isActive,
        });
        setModalOpen(true);
    };
    const handleCloseModal = () => {
        setModalOpen(false);
        setEditingItem(null);
        form.resetFields();
    };
    const handleSave = async () => {
        if (!editingItem) {
            return;
        }
        try {
            const values = await form.validateFields();
            setSaving(true);
            await updateWarehouseProductFull(editingItem.productCode, {
                productName: values.productName,
                englishName: values.englishProductName,
                productSpecification: values.productSpecification,
                productType: values.productType,
                domesticPrice: values.domesticPrice,
                oemPrice: values.oemPrice,
                importPrice: values.importPrice,
                packingQuantity: values.packingQuantity,
                unitVolume: values.unitVolume,
                middlePackQuantity: values.middlePackQuantity,
                packingSize: values.packingSize,
                material: values.material,
                remark: values.remarks,
                productImage: values.productImage,
                isActive: values.isActive,
                supplierCode: values.supplierCode,
            });
            message.success(t('warehouse.updateProductSuccess'));
            handleCloseModal();
            void loadData({ page });
        }
        catch (error) {
            if (typeof error === 'object' && error !== null && 'errorFields' in error) {
                return;
            }
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.saveProductFailed'));
        }
        finally {
            setSaving(false);
        }
    };
    const handleBatchToggleActive = async (nextIsActive: boolean) => {
        if (!selectedRowKeys.length) {
            return;
        }
        try {
            setBatchActionLoading(true);
            const result = await batchToggleWarehouseProductsActive({
                productCodes: selectedRowKeys.map(String),
                isActive: nextIsActive,
            });
            if (!result.success) {
                message.error(result.message || t('warehouse.batchStatusUpdateFailed'));
                return;
            }
            message.success(result.message || t('warehouse.batchStatusUpdated', { status: getShelfStatusLabel(nextIsActive, t), count: selectedRowKeys.length }));
            void loadData({ page });
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.batchStatusUpdateFailed'));
        }
        finally {
            setBatchActionLoading(false);
        }
    };
    const openBatchEdit = () => {
        if (!selectedRowKeys.length) {
            message.warning(t('warehouse.selectProductsFirst', '请先选择商品'));
            return;
        }
        batchEditForm.resetFields();
        setBatchEditOpen(true);
    };
    const openBatchCategory = () => {
        if (!selectedRowKeys.length) {
            message.warning(t('warehouse.selectProductsFirst', '请先选择商品'));
            return;
        }
        setTargetCategoryGuid(undefined);
        // 每次打开批量分类弹窗都只展开到一级分类，避免默认露出过深的子分类。
        setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1));
        setBatchCategoryOpen(true);
    };
    const handleBatchCategorySave = async () => {
        if (!selectedRowKeys.length) {
            message.warning(t('warehouse.selectProductsFirst', '请先选择商品'));
            return;
        }
        if (!targetCategoryGuid) {
            message.warning(t('warehouse.categories.selectTargetFirst', '请选择目标分类'));
            return;
        }
        try {
            setBatchCategorySaving(true);
            const selectedProductCodes = selectedRowKeys.map(String);
            const selectedProductCodeSet = new Set(selectedProductCodes);
            // 批量分类只提交商品编码和目标分类，避免误改价格、状态等其他业务字段。
            await batchAssignProducts(targetCategoryGuid, selectedProductCodes);
            // 保存成功后只更新当前页分类显示，不触发表格重新查询。
            setData((items) => items.map((item) => selectedProductCodeSet.has(item.productCode)
                ? {
                    ...item,
                    warehouseCategoryGUID: targetCategoryGuid,
                    categoryName: selectedTargetCategory
                        ? formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)
                        : item.categoryName,
                    categoryPath: selectedTargetCategoryPath || item.categoryPath,
                }
                : item));
            message.success(t('warehouse.categories.batchUpdateSuccess', {
                count: selectedRowKeys.length,
                categoryName: selectedTargetCategory?.categoryName || t('warehouse.categories.targetCategory', '目标分类'),
            }));
            setBatchCategoryOpen(false);
            setTargetCategoryGuid(undefined);
            setSelectedRowKeys([]);
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.categories.batchUpdateFailed', '批量更新商品分类失败'));
        }
        finally {
            setBatchCategorySaving(false);
        }
    };
    const handleBatchEditSave = async () => {
        if (!selectedRowKeys.length) {
            message.warning(t('warehouse.selectProductsFirst', '请先选择商品'));
            return;
        }
        try {
            const values = await batchEditForm.validateFields();
            const hasChanges = Object.values(values).some((value) => value !== undefined && value !== null);
            if (!hasChanges) {
                message.warning(t('warehouse.batchEditNoChanges', '请至少填写一个修改字段'));
                return;
            }
            setBatchEditSaving(true);
            const items = selectedRowKeys.map((code) => {
                const rawItem: WarehouseProductBatchUpdateItem = {
                    ProductCode: String(code),
                    DomesticPrice: values.domesticPrice,
                    OEMPrice: values.oemPrice,
                    ImportPrice: values.importPrice,
                    PackingQuantity: values.packingQuantity,
                    // 只传用户填写的字段，避免批量误覆盖；中包数来源为 WarehouseProduct.MinOrderQuantity。
                    MinOrderQuantity: values.minOrderQuantity,
                    Volume: values.unitVolume,
                    IsActive: values.isActive,
                };
                return Object.fromEntries(Object.entries(rawItem).filter(([, value]) => value !== undefined && value !== null)) as WarehouseProductBatchUpdateItem;
            });
            const result = await batchUpdateWarehouseProducts(items);
            message.success(result.message || t('warehouse.batchEditSuccess', '批量修改成功'));
            if ((result.failedCount ?? result.FailedCount ?? result.failed ?? result.Failed ?? 0) > 0) {
                message.warning(t('warehouse.batchUpdatePartialFailed', '{{count}} 个商品更新失败', { count: result.failedCount ?? result.FailedCount ?? result.failed ?? result.Failed ?? 0 }));
            }
            setBatchEditOpen(false);
            batchEditForm.resetFields();
            setSelectedRowKeys([]);
            void loadData({ page });
        }
        catch (error) {
            if (typeof error === 'object' && error !== null && 'errorFields' in error) {
                return;
            }
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.batchEditFailed', '批量修改失败'));
        }
        finally {
            setBatchEditSaving(false);
        }
    };
    const handleToggleSingleActive = async (record: WarehouseProductListItem, nextIsActive: boolean) => {
        try {
            setTogglingProductCodes((current) => [...current, record.productCode]);
            const result = await batchToggleWarehouseProductsActive({
                productCodes: [record.productCode],
                isActive: nextIsActive,
            });
            if (!result.success) {
                message.error(result.message || t('warehouse.toggleStatusFailed'));
                return;
            }
            setData((current) => current.map((item) => (item.productCode === record.productCode ? { ...item, isActive: nextIsActive } : item)));
            message.success(result.message || t('warehouse.statusToggled', { status: getShelfStatusLabel(nextIsActive, t) }));
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.toggleStatusFailed'));
        }
        finally {
            setTogglingProductCodes((current) => current.filter((code) => code !== record.productCode));
        }
    };
    const handleOpenSetItems = async (record: WarehouseProductListItem) => {
        setCurrentSetProduct(record);
        setSetItemsOpen(true);
        setSetItemsLoading(true);
        setSetItemsDraft([]);
        setDeletedSetCodeIds([]);
        try {
            const result = await getSetCodeGridData({ productCode: record.productCode, pageIndex: 1, pageSize: 200 });
            setSetItemsDraft(result.items ?? []);
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.loadSetItemsFailed'));
            setSetItemsOpen(false);
            setCurrentSetProduct(null);
        }
        finally {
            setSetItemsLoading(false);
        }
    };
    const handleSaveSetItems = async () => {
        if (!currentSetProduct) {
            return;
        }
        const invalidSetCodeItem = setItemsDraft.find((item) => !item.setBarcode?.trim() ||
            item.setPurchasePrice === undefined ||
            item.setPurchasePrice === null ||
            item.setRetailPrice === undefined ||
            item.setRetailPrice === null);
        if (invalidSetCodeItem) {
            message.error(t('warehouse.invalidSetCodeDetail', '套装明细条码、进货价和零售价不能为空'));
            return;
        }
        try {
            setSetItemsSaving(true);
            const newItems = setItemsDraft.filter((item) => item.id?.startsWith('new_'));
            const existingItems = setItemsDraft.filter((item) => item.id && !item.id.startsWith('new_'));
            // 仓库套装明细按 product-set-codes 分批保存，避免继续写入国内采购商品的 set-items 表。
            if (newItems.length) {
                await batchCreateSetCodes({
                    items: newItems.map((item) => ({
                        productCode: currentSetProduct.productCode,
                        setItemNumber: item.setItemNumber?.trim() || undefined,
                        setBarcode: item.setBarcode!.trim(),
                        setPurchasePrice: item.setPurchasePrice,
                        setRetailPrice: item.setRetailPrice,
                        isActive: item.isActive ?? true,
                    })),
                });
            }
            if (existingItems.length) {
                await batchUpdateSetBarcodes({
                    items: existingItems.map((item) => ({
                        id: item.id!,
                        setBarcode: item.setBarcode!.trim(),
                    })),
                });
                await batchUpdateSetPrices({
                    items: existingItems.map((item) => ({
                        id: item.id!,
                        setPurchasePrice: item.setPurchasePrice,
                        setRetailPrice: item.setRetailPrice,
                    })),
                });
                await batchUpdateSetStatus({
                    items: existingItems.map((item) => ({
                        id: item.id!,
                        isActive: item.isActive ?? true,
                    })),
                });
            }
            if (deletedSetCodeIds.length) {
                await batchDeleteSetCodes({ ids: deletedSetCodeIds });
            }
            message.success(t('warehouse.setItemsUpdated'));
            setSetItemsOpen(false);
            setCurrentSetProduct(null);
            setSetItemsDraft([]);
            setDeletedSetCodeIds([]);
            void loadData();
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.saveSetItemsFailed'));
        }
        finally {
            setSetItemsSaving(false);
        }
    };
    const handleExport = async () => {
        try {
            setExporting(true);
            setExportProgress(0);
            setExportMessage(t('warehouse.preparingExport'));
            const selectedProducts = selectedRowKeys.length
                ? data.filter((item) => selectedRowKeys.includes(item.id))
                : [];
            let productsToExport = selectedProducts;
            if (!productsToExport.length) {
                if (!total) {
                    message.warning(t('warehouse.noDataToExport'));
                    return;
                }
                const exportQuery = buildGridQuery({
                    page: 1,
                    pageSize: Math.max(total, 1),
                });
                const result = await getWarehouseProductsTable(exportQuery);
                productsToExport = result.items;
            }
            if (!productsToExport.length) {
                message.warning(t('warehouse.noDataToExport'));
                return;
            }
            const exportResult = await exportDomesticProductsToExcel(productsToExport.map((item) => ({
                itemNumber: item.itemNumber,
                barcode: item.barcode,
                name: item.name,
                labelPrice: item.labelPrice,
                productImage: item.productImage,
            })), {
                includeLabelPrice,
                includeBarcodeImage,
                includeProductImage,
                fileName: t('warehouse.warehouseProducts'),
                onProgress: (progress, nextMessage) => {
                    setExportProgress(progress);
                    setExportMessage(nextMessage);
                },
            });
            if (exportResult.failedProductImages.length > 0) {
                setExportFailDetail(exportResult.failedProductImages);
                setExportFailDetailOpen(true);
            }
            else {
                message.success(t('warehouse.exportSuccess'));
            }
            setExportConfigOpen(false);
            setExportProgress(0);
            setExportMessage('');
        }
        catch (error) {
            console.error(error);
            message.error(error instanceof Error ? error.message : t('warehouse.exportFailed'));
        }
        finally {
            setExporting(false);
        }
    };
    const handleSyncWarehouseProductsFromHq = () => {
        if (activeHqSyncJob) {
            showActiveHqSyncJobStatus(activeHqSyncJob);
            return;
        }
        Modal.confirm({
            title: t('warehouse.hqSyncTitle', '从HQ同步库存'),
            content: t('warehouse.hqSyncContent', '该操作会从 HQ 按商品编码匹配新增/更新库存业务字段，不会删除本地缺失商品。确认继续同步吗？'),
            okText: t('warehouse.hqSyncConfirm', '确认同步'),
            cancelText: t('common.cancel'),
            okButtonProps: { danger: true },
            onOk: async () => {
                setSyncingFromHq(true);
                try {
                    const job = await createWarehouseProductHqSyncJob({
                        operationId: WAREHOUSE_PRODUCT_HQ_SYNC_OPERATION_ID,
                    });
                    if (!job.jobId) {
                        notification.error({
                            message: t('warehouse.hqSyncJobCreateFailed', '创建仓库商品 HQ 同步任务失败'),
                            description: job.message ?? t('warehouse.hqSyncFailed', '从HQ同步库存失败'),
                            duration: 0,
                            placement: 'topRight',
                        });
                        return;
                    }
                    const activeJob: ActiveWarehouseProductHqSyncJob = {
                        jobId: job.jobId,
                        operationId: job.operationId ?? WAREHOUSE_PRODUCT_HQ_SYNC_OPERATION_ID,
                        createdAt: job.createdAt ?? new Date().toISOString(),
                        status: job.status,
                        message: job.message,
                    };
                    notification.info({
                        message: t('warehouse.hqSyncJobSubmitted', '仓库商品同步任务已提交，正在后台执行。完成后会自动提示结果。'),
                        description: job.isDuplicateRequest
                            ? t('warehouse.hqSyncJobStatusContent', '同步任务已在后台执行，请等待完成提示。')
                            : t('warehouse.hqSyncJobSubmittedDescription', '已提交到后台执行，完成后会在右上角通知结果。'),
                        duration: 3,
                        placement: 'topRight',
                    });
                    startHqSyncJobPolling(activeJob);
                }
                catch (error) {
                    console.error(error);
                    notification.error({
                        message: t('warehouse.hqSyncJobCreateFailed', '创建仓库商品 HQ 同步任务失败'),
                        description: error instanceof Error ? error.message : t('warehouse.hqSyncFailed', '从HQ同步库存失败'),
                        duration: 0,
                        placement: 'topRight',
                    });
                }
                finally {
                    setSyncingFromHq(false);
                }
            },
        });
    };
    const baseColumns = useMemo<ColumnsType<WarehouseProductListItem>>(() => [
        { key: 'rowNumber', title: '#', dataIndex: 'rowNumber', width: 30, fixed: 'left' },
        {
            key: 'itemNumber',
            title: t('column.hbItemNumber'),
            dataIndex: 'itemNumber',
            width: 122,
            fixed: 'left',
            sorter: true,
            ...textFilterProps('itemNumber', t('warehouse.searchProductByItemNumber', '按货号筛选')),
            render: (value: string) => value ? (<Space size={4}>
              <span>{value}</span>
              <Tooltip title={t('common.copy')}>
                <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => void copyTextToClipboard(value)}/>
              </Tooltip>
            </Space>) : ('--'),
        },
        {
            key: 'productImage',
            title: t('column.image'),
            dataIndex: 'productImage',
            width: 64,
            render: (value: string | undefined) => (<div className="warehouse-products-image-cell">
            <Image src={value} alt="" width={36} height={36} style={{ borderRadius: 4, objectFit: 'cover' }} fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="/>
          </div>),
        },
        {
            key: 'domesticSupplierCode',
            title: t('warehouse.domesticSupplier', '国内供应商'),
            dataIndex: 'domesticSupplierCode',
            width: 132,
            sorter: true,
            ...enumFilterProps('domesticSupplierCode', domesticSupplierFilterOptions),
            render: (_value, record) => {
                const supplierDisplayName = record.domesticSupplierName || record.domesticSupplierCode;
                return supplierDisplayName ? (<div className="warehouse-products-supplier-cell">
              <Tag color="blue">{supplierDisplayName}</Tag>
            </div>) : ('--');
            },
        },
        {
            key: 'categoryName',
            title: t('column.category'),
            dataIndex: 'categoryName',
            width: 128,
            filters: categoryColumnFilterOptions,
            filterIcon,
            filtered: categoryFilterValue !== ALL_PRODUCTS_FILTER_KEY,
            filteredValue: categoryFilterValue === ALL_PRODUCTS_FILTER_KEY ? null : [categoryFilterValue],
            filterMultiple: false,
            render: (_value, record) => renderWarehouseProductCategoryCell(record, categoryLookup, i18n.language),
        },
        {
            key: 'nameEn',
            title: t('column.englishName'),
            dataIndex: 'nameEn',
            width: 176,
            ...textFilterProps('nameEn', t('warehouse.searchProductByEnglishName', '按英文名筛选')),
            render: (value: string | undefined) => value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
        },
        {
            key: 'minOrderQuantity',
            title: t('warehouse.middlePackQuantity', '中包数'),
            dataIndex: 'minOrderQuantity',
            width: 96,
            ...numberRangeFilterProps('minOrderQuantity'),
            render: (value: number | undefined) => value !== undefined && value !== null ? value : '--',
        },
        {
            key: 'domesticPrice',
            title: t('column.domesticPrice'),
            dataIndex: 'domesticPrice',
            width: 96,
            ...numberRangeFilterProps('domesticPrice'),
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            key: 'importPrice',
            title: t('column.importPrice'),
            dataIndex: 'importPrice',
            width: 96,
            ...numberRangeFilterProps('importPrice'),
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            key: 'labelPrice',
            title: t('column.oemPrice'),
            dataIndex: 'labelPrice',
            width: 96,
            ...numberRangeFilterProps('oemPrice'),
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            key: 'isActive',
            title: t('column.status'),
            dataIndex: 'isActive',
            width: 104,
            ...enumFilterProps('isActive', [
                { text: getShelfStatusLabel(true, t), value: 'true' },
                { text: getShelfStatusLabel(false, t), value: 'false' },
            ]),
            render: (value: boolean, record) => (<Switch checked={value} checkedChildren={getShelfStatusLabel(true, t)} unCheckedChildren={getShelfStatusLabel(false, t)} disabled={!access.canWriteProduct || togglingProductCodes.includes(record.productCode)} loading={togglingProductCodes.includes(record.productCode)} onChange={(nextChecked) => void handleToggleSingleActive(record, nextChecked)}/>),
        },
        {
            key: 'productType',
            title: t('column.productType'),
            dataIndex: 'productType',
            width: 104,
            ...enumFilterProps('productType', productTypeOptions.map((option) => ({
                text: String(option.label),
                value: String(option.value),
            }))),
            render: (value: ProductType) => <Tag color={getProductTypeTagColor(value)}>{getProductTypeLabel(value, t)}</Tag>,
        },
        {
            key: 'barcode',
            title: t('column.barcode'),
            dataIndex: 'barcode',
            width: 156,
            ...textFilterProps('barcode', t('warehouse.searchProductByBarcode', '按条码筛选')),
            render: (value: string | undefined) => value ? (<div className="warehouse-products-barcode-cell">
              <BarcodePreview value={value} textMaxWidth={150} compactCopy/>
            </div>) : ('--'),
        },
        {
            key: 'name',
            title: t('column.productName'),
            dataIndex: 'name',
            width: 176,
            sorter: true,
            ...textFilterProps('productName', t('warehouse.searchProductByName', '按商品名筛选')),
            render: (value: string | undefined) => value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
        },
        {
            key: 'packingQty',
            title: t('column.packingQuantity'),
            dataIndex: 'packingQty',
            width: 108,
            ...numberRangeFilterProps('packingQty'),
            render: (value: number | undefined, record) => value !== undefined && value !== null ? (<Space size={4}>
              <span>{value}</span>
              {record.isPackingQtyFallback ? <Tag color="gold">{t('warehouse.domestic')}</Tag> : <Tag color="green">{t('warehouse.warehouse')}</Tag>}
            </Space>) : ('--'),
        },
        {
            key: 'volume',
            title: t('column.volume'),
            dataIndex: 'volume',
            width: 108,
            ...numberRangeFilterProps('volume'),
            render: (value: number | undefined, record) => value !== undefined && value !== null ? (<Space size={4}>
              <span>{value}</span>
              {record.isVolumeFallback ? <Tag color="gold">{t('warehouse.domestic')}</Tag> : <Tag color="green">{t('warehouse.warehouse')}</Tag>}
            </Space>) : ('--'),
        },
        {
            key: 'localSupplierCode',
            title: t('column.australianSupplier', '澳洲供应商'),
            dataIndex: 'localSupplierCode',
            width: 150,
            sorter: true,
            ...enumFilterProps('localSupplierCode', localSupplierFilterOptions),
            render: (_value, record) => {
                // 表格接口只返回澳洲供应商代码时，用活跃供应商列表补齐名称。
                const supplierDisplayName = record.localSupplierName || localSupplierNameMap[record.localSupplierCode || ''] || record.localSupplierCode;
                return supplierDisplayName ? (<div className="warehouse-products-supplier-cell">
              <Tag color="purple">{supplierDisplayName}</Tag>
            </div>) : ('--');
            },
        },
        {
            key: 'updatedAt',
            title: t('column.updateTime'),
            dataIndex: 'updatedAt',
            width: 164,
            sorter: true,
            ...dateRangeFilterProps('updatedAt'),
            render: (value: string | undefined) => formatDateTime(value, i18n.language),
        },
        {
            key: 'updatedBy',
            title: t('column.updater'),
            dataIndex: 'updatedBy',
            width: 96,
            render: (value: string | undefined) => value || '--',
        },
        {
            key: 'action',
            title: t('column.action'),
            width: 190,
            fixed: 'right',
            render: (_, record) => (<Space size={0}>
            {access.canWriteProduct ? (<Button type="link" icon={<EditOutlined />} onClick={() => handleOpenEdit(record)}>
                {t('common.edit')}
              </Button>) : null}
            {canManageProductDetails(record.productType) ? (<Button type="link" icon={<GiftOutlined />} onClick={() => void handleOpenSetItems(record)}>
                {getProductDetailsActionLabel(record.productType, t)}
              </Button>) : (<Tooltip title={getProductDetailsDisabledHint(t)}>
                <Button type="link" icon={<GiftOutlined />} disabled>
                  {getProductDetailsActionLabel(ProductType.SET, t)}
                </Button>
              </Tooltip>)}
          </Space>),
        },
    ], [access.canWriteProduct, categoryColumnFilterOptions, categoryFilterValue, categoryLookup, columnFilters, domesticSupplierFilterOptions, i18n.language, localSupplierFilterOptions, localSupplierNameMap, productTypeOptions, t, togglingProductCodes]);
    const draggableColumnKeys = [...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER];
    useEffect(() => {
        setColumnOrder((current) => {
            let savedOrder: unknown[] | null = null;
            if (!current.length && typeof window !== 'undefined') {
                try {
                    const raw = localStorage.getItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY);
                    savedOrder = raw ? JSON.parse(raw) : null;
                }
                catch {
                    savedOrder = null;
                }
            }
            // 列顺序只管理业务列；选择列继续由 rowSelection 管理，新增/删除列在这里自动兼容。
            const nextOrder = mergeWarehouseProductColumnOrder(current.length ? current : savedOrder, WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER);
            if (current.length === nextOrder.length && current.every((key, index) => key === nextOrder[index])) {
                return current;
            }
            return nextOrder;
        });
    }, [draggableColumnKeys.join('|')]);
    const handleColumnDragEnd = ({ active, over }: DragEndEvent) => {
        if (!over || active.id === over.id)
            return;
        setColumnOrder((current) => {
            const nextOrder = moveWarehouseProductColumnOrder(current, active.id, over.id);
            try {
                localStorage.setItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY, JSON.stringify(nextOrder));
            }
            catch {
                // localStorage 不可用时不影响当前页面内拖拽排序。
            }
            return nextOrder;
        });
    };
    const isColumnOrderCustomized = isWarehouseProductColumnOrderCustomized(columnOrder, WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER);
    const handleResetColumnOrder = () => {
        try {
            localStorage.removeItem(WAREHOUSE_PRODUCT_COLUMN_ORDER_STORAGE_KEY);
        }
        catch {
            // localStorage 不可用时仍恢复当前页面内的默认列顺序。
        }
        // 重置只影响业务列顺序；选择列仍由 Ant Design rowSelection 管理。
        setColumnOrder([...WAREHOUSE_PRODUCT_DEFAULT_COLUMN_ORDER]);
    };
    const orderedColumns = useMemo(() => {
        const activeOrder = columnOrder.length ? columnOrder : draggableColumnKeys;
        const columnMap = new Map(baseColumns.map((column) => [String(column.key), column]));
        return activeOrder
            .map((key) => columnMap.get(key))
            .filter((column): column is ColumnsType<WarehouseProductListItem>[number] => Boolean(column))
            .map((column) => ({
            ...column,
            onHeaderCell: () => ({
                'data-column-key': String(column.key),
            }),
        })) as ColumnsType<WarehouseProductListItem>;
    }, [baseColumns, columnOrder, draggableColumnKeys.join('|')]);
    return (<>
      <style>{warehouseProductsTableStyle}</style>
      <PageContainer title={t('warehouse.productManagement')} subtitle={t('warehouse.productManagementSubtitle')} extra={<Space wrap>
          {access.isAdmin ? (<Button icon={<CloudSyncOutlined />} loading={syncingFromHq || Boolean(activeHqSyncJob)} disabled={syncingFromHq} onClick={handleSyncWarehouseProductsFromHq}>
            {t('warehouse.hqSync', '从HQ同步库存')}
          </Button>) : null}
          <Button icon={<DownloadOutlined />} loading={exporting} disabled={exporting} onClick={() => setExportConfigOpen(true)}>
            {t('warehouse.exportExcel')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportFromDomesticOpen(true)}>
            {t('warehouse.importFromDomestic')}
          </Button>
          {canImportNonHbProducts ? (<Button icon={<UploadOutlined />} onClick={() => setImportNonHbOpen(true)}>
              {t('warehouse.importNonHb.title')}
            </Button>) : null}
          <Button icon={<GiftOutlined />} onClick={() => message.info(t('warehouse.batchSetMigrated'))}>
            {t('warehouse.batchCreateSet')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => message.info(t('warehouse.batchImageUploadMigrated'))}>
            {t('warehouse.batchImageUpload')}
          </Button>
          {access.canWriteProduct ? (<Popconfirm title={t('warehouse.confirmBatchActivate')} okText={getShelfStatusLabel(true, t)} cancelText={t('common.cancel')} disabled={!selectedRowKeys.length} onConfirm={() => void handleBatchToggleActive(true)}>
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchActivate')}
              </Button>
            </Popconfirm>) : null}
          {access.canWriteProduct ? (<Popconfirm title={t('warehouse.confirmBatchDeactivate')} okText={getShelfStatusLabel(false, t)} cancelText={t('common.cancel')} disabled={!selectedRowKeys.length} onConfirm={() => void handleBatchToggleActive(false)}>
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchDeactivate')}
              </Button>
            </Popconfirm>) : null}
          {access.canWriteProduct ? (<Button loading={batchEditSaving} disabled={!selectedRowKeys.length || batchEditSaving} onClick={openBatchEdit}>
              {t('warehouse.batchEdit', '批量修改')}
            </Button>) : null}
          {access.canWriteProduct ? (<Button icon={<AppstoreOutlined />} loading={batchCategorySaving} disabled={!selectedRowKeys.length || batchCategorySaving} onClick={openBatchCategory}>
              {t('warehouse.batchSetCategory', '批量分类')}
            </Button>) : null}
          {access.canWriteProduct ? (<Button type="primary" icon={<PlusOutlined />} onClick={handleOpenCreate}>
              {t('warehouse.createProduct')}
            </Button>) : null}
          {exporting ? (<Typography.Text type="secondary">
              {exportMessage} ({exportProgress}%)
            </Typography.Text>) : null}
          </Space>}>
        <Card>
          <Space wrap style={{ marginBottom: 16 }}>
          <Input value={searchText} onChange={(event) => setSearchText(event.target.value)} prefix={<SearchOutlined />} placeholder={t('warehouse.searchProductFull')} style={{ width: 300 }} allowClear/>
          <Select value={supplierCode} onChange={(value) => {
            setSupplierCode(value);
            setColumnFilters((current) => setFilterValues(current, 'domesticSupplierCode', value ? [value] : undefined));
        }} options={buildSupplierOptions(suppliers)} placeholder={t('warehouse.allDomesticSuppliers')} style={{ width: 240 }} showSearch filterOption={filterSupplierOption} allowClear/>
          {/* 顶部筛选使用真实分类树，避免多级分类继续依赖 -- 前缀伪缩进。 */}
          <TreeSelect
            value={categoryFilterValue}
            onChange={(value) => {
              setCategoryFilterValue(value || ALL_PRODUCTS_FILTER_KEY);
              setCategoryFilterSearchText('');
            }}
            treeData={categoryFilterTreeOptions}
            placeholder={t('warehouse.categories.category', '分类')}
            style={{ width: 220 }}
            showSearch
            searchValue={categoryFilterSearchText}
            onSearch={setCategoryFilterSearchText}
            treeNodeFilterProp="searchText"
            treeExpandedKeys={hasCategoryFilterSearchText ? undefined : categoryFilterExpandedKeys}
            onTreeExpand={(keys) => {
              // 搜索时交给 TreeSelect 自动展开命中路径；非搜索时保留用户手动展开状态。
              if (!hasCategoryFilterSearchText) {
                setCategoryFilterExpandedKeys(keys.map(String));
              }
            }}
            popupMatchSelectWidth={320}
            listHeight={360}
            allowClear
            notFoundContent={categoryLoading ? t('common.loading', '加载中') : t('warehouse.categories.noCategoryData', '暂无分类数据')}
          />
          <Select value={productType} onChange={(value) => {
            setProductType(value);
            setColumnFilters((current) => setFilterValues(current, 'productType', value === undefined ? undefined : [String(value)]));
        }} options={productTypeOptions} placeholder={t('warehouse.allProductTypes')} style={{ width: 160 }} allowClear/>
          <Select value={isActive} onChange={(value) => {
            setIsActive(value);
            setColumnFilters((current) => setFilterValues(current, 'isActive', value === undefined ? undefined : [String(value)]));
        }} options={getStatusOptions(t)} placeholder={t('warehouse.allStatus')} style={{ width: 140 }} allowClear/>
          <Button onClick={() => {
            setCategoryFilterValue(UNCATEGORIZED_PRODUCTS_FILTER_KEY);
            void loadData({
                page: 1,
                filters: columnFilters,
                categoryGuid: undefined,
                uncategorizedOnly: true,
            });
        }}>
            {t('warehouse.categories.uncategorizedOption', '未分类商品')}
          </Button>
          <Button type="primary" onClick={() => void loadData({ page: 1 })}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => {
            setSearchText('');
            setSupplierCode(undefined);
            setCategoryFilterValue(ALL_PRODUCTS_FILTER_KEY);
            setProductType(undefined);
            setIsActive(undefined);
            setColumnFilters({});
            setSortField('createdAt');
            setSortOrder('descend');
            void loadData({
                page: 1,
                searchText: '',
                supplierCode: undefined,
                filters: {},
                categoryGuid: undefined,
                uncategorizedOnly: false,
                productType: undefined,
                isActive: undefined,
                sortField: 'createdAt',
                sortOrder: 'descend',
            });
        }}>
            {t('common.reset')}
          </Button>
          <Button icon={<ReloadOutlined />} disabled={!isColumnOrderCustomized} onClick={handleResetColumnOrder}>
            {t('warehouse.resetColumns', '重置列')}
          </Button>
        </Space>

          <DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>
            <SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>
              <Table className="warehouse-products-table" rowKey="productCode" virtual loading={loading} components={{ header: { cell: DraggableHeaderCell } }} columns={orderedColumns} dataSource={data} rowSelection={{
                fixed: true,
                columnWidth: 56,
                selectedRowKeys,
                onChange: setSelectedRowKeys,
            }} scroll={{ x: 2260, y: 620 }} pagination={{
                current: page,
                pageSize,
                total,
                showSizeChanger: true,
                pageSizeOptions: WAREHOUSE_PRODUCTS_PAGE_SIZE_OPTIONS,
            }} onChange={(pagination: TablePaginationConfig, filters: Record<string, FilterValue | null>, sorter: SorterResult<WarehouseProductListItem> | SorterResult<WarehouseProductListItem>[], extra) => {
                const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter;
                const nextSortField = typeof nextSorter?.field === 'string' ? nextSorter.field : sortField;
                const nextSortOrder = nextSorter?.order === 'ascend' || nextSorter?.order === 'descend'
                    ? nextSorter.order
                    : sortOrder;
                const nextColumnFilters = normalizeTableFilters(filters);
                const nextCategoryFilterValue = resolveCategoryFilterValueFromTableFilters(filters);
                const nextSupplierCode = getSingleFilterValue(nextColumnFilters.domesticSupplierCode);
                const nextProductType = getSingleFilterValue(nextColumnFilters.productType);
                const nextIsActive = getSingleFilterValue(nextColumnFilters.isActive);
                // 列头筛选统一走后端 filters，分类列头则继续映射成顶层分类字段。
                const categoryQuery = buildCategoryQueryValue(nextCategoryFilterValue);
                setColumnFilters(nextColumnFilters);
                setCategoryFilterValue(nextCategoryFilterValue);
                setSupplierCode(nextSupplierCode);
                setProductType(nextProductType === undefined ? undefined : Number(nextProductType) as ProductType);
                setIsActive(nextIsActive === undefined ? undefined : nextIsActive === 'true');
                setSortField(nextSortField);
                setSortOrder(nextSortOrder);
                void loadData({
                    page: extra.action === 'paginate' ? pagination.current || 1 : 1,
                    pageSize: pagination.pageSize || pageSize,
                    supplierCode: nextSupplierCode,
                    productType: nextProductType === undefined ? undefined : Number(nextProductType) as ProductType,
                    isActive: nextIsActive === undefined ? undefined : nextIsActive === 'true',
                    sortField: nextSortField,
                    sortOrder: nextSortOrder,
                    filters: nextColumnFilters,
                    ...categoryQuery,
                });
            }}/>
            </SortableContext>
          </DndContext>
        </Card>

      <ProductFormModal open={modalOpen} saving={saving} editingItem={editingItem} suppliers={suppliers} form={form} onCancel={handleCloseModal} onSubmit={() => void handleSave()}/>

      <CreateProductModal open={createModalOpen} suppliers={suppliers} onCancel={() => setCreateModalOpen(false)} onSuccess={() => {
            setCreateModalOpen(false);
            void loadData({ page: 1 });
        }}/>

      <SetItemsModal open={setItemsOpen} loading={setItemsLoading} saving={setItemsSaving} product={currentSetProduct} items={setItemsDraft} canEdit={access.canWriteProduct} onCancel={() => {
            setSetItemsOpen(false);
            setCurrentSetProduct(null);
            setSetItemsDraft([]);
            setDeletedSetCodeIds([]);
        }} onAddRow={() => {
            setSetItemsDraft((current) => [
                ...current,
                {
                    id: `new_${Date.now()}_${Math.random()}`,
                    productCode: currentSetProduct?.productCode || '',
                    setItemNumber: '',
                    setBarcode: '',
                    setPurchasePrice: currentSetProduct?.importPrice,
                    setRetailPrice: currentSetProduct?.labelPrice,
                    isActive: true,
                },
            ]);
        }} onRemoveRow={(rowId) => {
            if (!rowId.startsWith('new_')) {
                setDeletedSetCodeIds((current) => [...current, rowId]);
            }
            setSetItemsDraft((current) => current.filter((item) => item.id !== rowId));
        }} onChangeField={(rowId, field, value) => {
            setSetItemsDraft((current) => current.map((item) => (item.id === rowId ? { ...item, [field]: value } : item)));
        }} onSubmit={() => void handleSaveSetItems()}/>

      <Modal title={t('warehouse.batchEditTitle', '批量修改 ({{count}} 个商品)', { count: selectedRowKeys.length })} open={batchEditOpen} width={680} destroyOnClose okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={batchEditSaving} onCancel={() => {
            setBatchEditOpen(false);
            batchEditForm.resetFields();
        }} onOk={() => void handleBatchEditSave()}>
        <Form form={batchEditForm} layout="vertical" preserve={false}>
          <Typography.Paragraph type="secondary">
            {t('warehouse.batchEditEmptyHint', '留空不修改')}
          </Typography.Paragraph>
          <Space size={16} style={{ display: 'flex' }} align="start">
            <Form.Item name="domesticPrice" label={t('domesticProducts.domesticPrice')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
            <Form.Item name="oemPrice" label={t('productCreation.privateLabelPrice')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
            <Form.Item name="importPrice" label={t('warehouse.importPrice')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
          </Space>
          <Space size={16} style={{ display: 'flex' }} align="start">
            <Form.Item name="packingQuantity" label={t('warehouse.packingQuantity')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={0} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
            <Form.Item name="minOrderQuantity" label={t('warehouse.middlePackQuantity', '中包数')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={0} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
            <Form.Item name="unitVolume" label={t('warehouse.volume')} style={{ flex: 1 }}>
              <InputNumber min={0} precision={4} style={{ width: '100%' }} placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')}/>
            </Form.Item>
          </Space>
          <Form.Item name="isActive" label={t('warehouse.isListed')}>
            <Select placeholder={t('warehouse.batchEditKeepEmpty', '留空不修改')} allowClear>
              <Select.Option value={true}>{getShelfStatusLabel(true, t)}</Select.Option>
              <Select.Option value={false}>{getShelfStatusLabel(false, t)}</Select.Option>
            </Select>
          </Form.Item>
        </Form>
      </Modal>

      <Modal title={t('warehouse.batchCategoryTitle', '批量设置分类 ({{count}} 个商品)', { count: selectedRowKeys.length })} open={batchCategoryOpen} width={640} destroyOnClose okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={batchCategorySaving} okButtonProps={{ disabled: !targetCategoryGuid || !categories.length }} onCancel={() => {
            setBatchCategoryOpen(false);
            setTargetCategoryGuid(undefined);
        }} onOk={() => void handleBatchCategorySave()}>
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('warehouse.batchCategoryHint', '请选择目标分类，确认后会把已选商品设置到该分类。')}
          </Typography.Paragraph>
          {selectedTargetCategory ? (<Tag color="blue">
              {t('warehouse.categories.targetCategory', '目标分类')}: {selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)}
            </Tag>) : null}
          <CategoryTreePicker categories={categories} selectedKey={targetCategoryGuid} expandedKeys={categoryExpandedKeys} onExpand={setCategoryExpandedKeys} onSelect={setTargetCategoryGuid} language={i18n.language} t={t} maxHeight={420}/>
        </Space>
      </Modal>

      <ImportFromDomesticModal open={importFromDomesticOpen} onCancel={() => setImportFromDomesticOpen(false)} onSuccess={() => void loadData({ page: 1 })}/>

      <ImportNonHbModal open={importNonHbOpen} onCancel={() => setImportNonHbOpen(false)} onSuccess={() => void loadData({ page: 1 })}/>

      <Modal title={t('warehouse.exportExcel')} open={exportConfigOpen} okText={t('warehouse.startExport')} cancelText={t('common.cancel')} confirmLoading={exporting} onCancel={() => {
            if (!exporting) {
                setExportConfigOpen(false);
            }
        }} onOk={() => void handleExport()}>
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Typography.Text>
            {selectedRowKeys.length
            ? t('warehouse.exportSelected', { count: selectedRowKeys.length })
            : t('warehouse.exportFiltered', { count: total })}
          </Typography.Text>
          <Checkbox checked={includeLabelPrice} onChange={(event) => setIncludeLabelPrice(event.target.checked)}>
            {t('warehouse.includeLabelPrice')}
          </Checkbox>
          <Checkbox checked={includeBarcodeImage} onChange={(event) => setIncludeBarcodeImage(event.target.checked)}>
            {t('warehouse.includeBarcodeImage')}
          </Checkbox>
          <Checkbox checked={includeProductImage} onChange={(event) => setIncludeProductImage(event.target.checked)}>
            {t('warehouse.includeProductImage')}
          </Checkbox>
          {exporting ? (<Typography.Text type="secondary">
              {exportMessage} ({exportProgress}%)
            </Typography.Text>) : null}
        </Space>
      </Modal>

      <Modal title={t('warehouse.exportCompleteFailed', { count: exportFailDetail.length })} open={exportFailDetailOpen} width={700} footer={null} onCancel={() => setExportFailDetailOpen(false)}>
        <Typography.Paragraph type="secondary" style={{ marginBottom: 12 }}>
          {t('warehouse.imageDownloadFailedMsg')}
        </Typography.Paragraph>
        <Table size="small" pagination={false} scroll={{ y: 360 }} dataSource={exportFailDetail} rowKey="itemNumber" columns={[
            { title: t('productImport.hbProductNoCol'), dataIndex: 'itemNumber', width: 120 },
            { title: t('warehouse.failureReason'), dataIndex: 'reason', width: 200 },
            {
                title: t('warehouse.imageUrl'),
                dataIndex: 'url',
                ellipsis: true,
                render: (val: string) => (<Tooltip title={val}>
                  <span style={{ fontSize: 12, wordBreak: 'break-all' }}>{val}</span>
                </Tooltip>),
            },
        ]}/>
      </Modal>
      </PageContainer>
    </>);
}
