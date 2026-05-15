import { CopyOutlined, DownloadOutlined, EditOutlined, GiftOutlined, PlusOutlined, ReloadOutlined, SearchOutlined, UploadOutlined } from '@ant-design/icons';
import { Button, Card, Checkbox, Form, Image, Input, InputNumber, Modal, Popconfirm, Select, Space, Switch, Table, Tag, Tooltip, Typography, message, } from 'antd';
import type { DefaultOptionType } from 'antd/es/select';
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table';
import type { SorterResult } from 'antd/es/table/interface';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BarcodePreview from '../../../components/BarcodePreview';
import PageContainer from '../../../components/PageContainer';
import { getDomesticProductSetItems, getSupplierOptions, updateDomesticProductSetItems, } from '../../../services/domesticProductService';
import { exportDomesticProductsToExcel, type ExportResult } from '../../../services/exportService';
import { batchToggleWarehouseProductsActive, getWarehouseProductsTable, updateWarehouseProductFull, type WarehouseProductListItem, type WarehouseProductsTableQuery, } from '../../../services/warehouseProductService';
import { useAuthStore } from '../../../store/auth';
import type { DomesticProductSetItem, ProductType, SupplierOption, } from '../../../types/domesticProduct';
import { ProductTypeLabels } from '../../../types/domesticProduct';
import { copyTextToClipboard } from '../../../utils/clipboard';
import CreateProductModal from './CreateProductModal';
import ImportFromDomesticModal from './ImportFromDomesticModal';
import ImportNonHbModal from './ImportNonHbModal';
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
const getStatusOptions = (t: ReturnType<typeof useTranslation>['t']) => [
    { value: true, label: t('warehouse.active') },
    { value: false, label: t('warehouse.inactive') },
];
function getProductTypeLabel(value: ProductType, t: ReturnType<typeof useTranslation>['t']) {
    const keyMap: Record<ProductType, string> = {
        [0]: 'warehouse.normal',
        [1]: 'warehouse.hasSet',
        [2]: 'warehouse.hasMultiCode',
    };
    return t(keyMap[value]) || ProductTypeLabels[value] || '--';
}
function getProductTypeOptions(t: ReturnType<typeof useTranslation>['t']) {
    return Object.keys(ProductTypeLabels).map((value) => ({
        value: Number(value),
        label: getProductTypeLabel(Number(value) as ProductType, t),
    }));
}
const WAREHOUSE_TABLE_ROW_MAX_HEIGHT = 80;
const warehouseProductsTableStyle = `
  .warehouse-products-table .ant-table-tbody > tr > td {
    height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    max-height: ${WAREHOUSE_TABLE_ROW_MAX_HEIGHT}px;
    vertical-align: middle;
  }

  .warehouse-products-table .ant-table-cell {
    white-space: nowrap;
  }

  .warehouse-products-table .warehouse-products-image-cell,
  .warehouse-products-table .warehouse-products-barcode-cell {
    min-height: 64px;
    max-height: 64px;
    overflow: hidden;
    display: flex;
    align-items: center;
  }

  .warehouse-products-table .warehouse-products-image-cell .ant-image,
  .warehouse-products-table .warehouse-products-image-cell img {
    width: 44px;
    height: 44px;
    display: block;
  }

  .warehouse-products-table .warehouse-products-barcode-cell svg,
  .warehouse-products-table .warehouse-products-barcode-cell canvas,
  .warehouse-products-table .warehouse-products-barcode-cell img {
    max-height: 56px !important;
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
    line-height: 20px;
    padding-block: 2px;
  }

  .warehouse-products-table .warehouse-products-text-2line {
    white-space: normal;
    overflow: hidden;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    line-height: 20px;
    word-break: break-word;
  }
`;
function formatDateTime(value?: string) {
    if (!value) {
        return '--';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }
    return date.toLocaleString('zh-CN', { hour12: false });
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
          <Form.Item name="isActive" label={t('domesticProducts.status')} valuePropName="checked" style={{ width: 120 }}>
            <Switch checkedChildren={t('warehouse.active')} unCheckedChildren={t('warehouse.inactive')}/>
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
    items: DomesticProductSetItem[];
    canEdit: boolean;
    onCancel: () => void;
    onAddRow: () => void;
    onRemoveRow: (rowId: string) => void;
    onChangeField: (rowId: string, field: keyof DomesticProductSetItem, value: string | number | undefined) => void;
    onSubmit: () => void;
}) {
    const { t } = useTranslation();
    const columns: ColumnsType<DomesticProductSetItem> = [
        {
            title: t('domesticProducts.productName'),
            dataIndex: 'productName',
            render: (_, record) => (<Input value={record.productName} disabled={!canEdit} onChange={(event) => onChangeField(record.id, 'productName', event.target.value)}/>),
        },
        {
            title: t('warehouse.setProductNo'),
            dataIndex: 'setProductNo',
            width: 180,
            render: (_, record) => (<Input value={record.setProductNo} disabled={!canEdit} onChange={(event) => onChangeField(record.id, 'setProductNo', event.target.value)}/>),
        },
        {
            title: t('domesticProducts.barcode'),
            dataIndex: 'setBarcode',
            width: 180,
            render: (_, record) => (<Input value={record.setBarcode} disabled={!canEdit} onChange={(event) => onChangeField(record.id, 'setBarcode', event.target.value)}/>),
        },
        {
            title: t('domesticProducts.domesticPrice'),
            dataIndex: 'domesticPrice',
            width: 120,
            render: (_, record) => (<InputNumber min={0} precision={2} value={record.domesticPrice} disabled={!canEdit} style={{ width: '100%' }} onChange={(value) => onChangeField(record.id, 'domesticPrice', value ?? undefined)}/>),
        },
        {
            title: t('warehouse.importPrice'),
            dataIndex: 'importPrice',
            width: 120,
            render: (_, record) => (<InputNumber min={0} precision={2} value={record.importPrice} disabled={!canEdit} style={{ width: '100%' }} onChange={(value) => onChangeField(record.id, 'importPrice', value ?? undefined)}/>),
        },
        {
            title: t('productCreation.privateLabelPrice'),
            dataIndex: 'oemPrice',
            width: 120,
            render: (_, record) => (<InputNumber min={0} precision={2} value={record.oemPrice} disabled={!canEdit} style={{ width: '100%' }} onChange={(value) => onChangeField(record.id, 'oemPrice', value ?? undefined)}/>),
        },
        {
            title: t('common.action'),
            key: 'action',
            width: 90,
            render: (_, record) => canEdit ? (<Button danger type="link" onClick={() => onRemoveRow(record.id)}>
            {t('common.delete')}
          </Button>) : null,
        },
    ];
    return (<Modal title={product ? t('warehouse.setDetailsTitle', { name: product.itemNumber || product.name }) : t('warehouse.setDetails')} open={open} width={1100} destroyOnClose onCancel={onCancel} onOk={onSubmit} okText={t('common.save')} cancelText={t('common.close')} confirmLoading={saving} okButtonProps={{ disabled: !canEdit }}>
      <Space style={{ marginBottom: 16 }}>
        <Typography.Text type="secondary">
          {t('warehouse.setEditHint')}
        </Typography.Text>
        {canEdit ? (<Button type="dashed" onClick={onAddRow}>
            {t('warehouse.addSubItem')}
          </Button>) : null}
      </Space>
      <Table rowKey="id" loading={loading} columns={columns} dataSource={items} pagination={false} scroll={{ x: 980, y: 420 }}/>
    </Modal>);
}
export default function WarehouseProductsPage() {
    const { t } = useTranslation();
    const productTypeOptions = getProductTypeOptions(t);
    const [form] = Form.useForm<ProductFormValues>();
    const [loading, setLoading] = useState(false);
    const [saving, setSaving] = useState(false);
    const [modalOpen, setModalOpen] = useState(false);
    const [createModalOpen, setCreateModalOpen] = useState(false);
    const [editingItem, setEditingItem] = useState<WarehouseProductListItem | null>(null);
    const [suppliers, setSuppliers] = useState<SupplierOption[]>([]);
    const [data, setData] = useState<WarehouseProductListItem[]>([]);
    const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);
    const [searchText, setSearchText] = useState('');
    const [supplierCode, setSupplierCode] = useState<string>();
    const [productType, setProductType] = useState<ProductType>();
    const [isActive, setIsActive] = useState<boolean>();
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(20);
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
    const [setItemsDraft, setSetItemsDraft] = useState<DomesticProductSetItem[]>([]);
    const [batchActionLoading, setBatchActionLoading] = useState(false);
    const [togglingProductCodes, setTogglingProductCodes] = useState<string[]>([]);
    const [exportFailDetailOpen, setExportFailDetailOpen] = useState(false);
    const [exportFailDetail, setExportFailDetail] = useState<ExportResult['failedProductImages']>([]);
    const { access } = useAuthStore();
    const buildGridQuery = (overrides: Partial<WarehouseProductsTableQuery> = {}): WarehouseProductsTableQuery => ({
        page,
        pageSize,
        searchText,
        supplierCode,
        productType,
        isActive,
        sortField,
        sortOrder,
        ...overrides,
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
    useEffect(() => {
        void Promise.all([
            loadData({ page: 1 }),
            getSupplierOptions()
                .then(setSuppliers)
                .catch((error) => {
                console.error(error);
                message.error(t('productCreation.loadSupplierListFailed'));
            }),
        ]);
    }, []);
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
            message.success(result.message || t('warehouse.batchStatusUpdated', { status: nextIsActive ? t('warehouse.active') : t('warehouse.inactive'), count: selectedRowKeys.length }));
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
            message.success(result.message || t('warehouse.statusToggled', { status: nextIsActive ? t('warehouse.active') : t('warehouse.inactive') }));
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
        try {
            const items = await getDomesticProductSetItems(record.productCode);
            setSetItemsDraft(items);
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
        try {
            setSetItemsSaving(true);
            await updateDomesticProductSetItems(currentSetProduct.productCode, setItemsDraft);
            message.success(t('warehouse.setItemsUpdated'));
            setSetItemsOpen(false);
            setCurrentSetProduct(null);
            setSetItemsDraft([]);
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
    const columns = useMemo<ColumnsType<WarehouseProductListItem>>(() => [
        { title: '#', dataIndex: 'rowNumber', width: 30, fixed: 'left' },
        {
            title: t('warehouse.hbProductNo'),
            dataIndex: 'itemNumber',
            width: 120,
            fixed: 'left',
            sorter: true,
            render: (value: string) => value ? (<Space size={4}>
              <span>{value}</span>
              <Tooltip title={t('common.copy')}>
                <Button size="small" type="text" icon={<CopyOutlined />} onClick={() => void copyTextToClipboard(value)}/>
              </Tooltip>
            </Space>) : ('--'),
        },
        {
            title: t('warehouse.productImage'),
            dataIndex: 'productImage',
            width: 80,
            render: (value: string | undefined) => (<div className="warehouse-products-image-cell">
            <Image src={value} alt="" width={44} height={44} style={{ borderRadius: 4, objectFit: 'cover' }} fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="/>
          </div>),
        },
        {
            title: t('warehouse.domesticSupplier'),
            dataIndex: 'domesticSupplierCode',
            width: 150,
            sorter: true,
            render: (_value, record) => record.domesticSupplierCode || record.domesticSupplierName ? (<div className="warehouse-products-supplier-cell">
              <Tag color="blue">
                {[record.domesticSupplierCode, record.domesticSupplierName].filter(Boolean).join(' - ')}
              </Tag>
            </div>) : ('--'),
        },
        {
            title: t('domesticProducts.productName'),
            dataIndex: 'name',
            width: 200,
            sorter: true,
            render: (value: string | undefined) => value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
        },
        {
            title: t('warehouse.englishName'),
            dataIndex: 'nameEn',
            width: 200,
            render: (value: string | undefined) => value ? <div className="warehouse-products-text-2line">{value}</div> : '--',
        },
        {
            title: t('domesticProducts.barcode'),
            dataIndex: 'barcode',
            width: 180,
            render: (value: string | undefined) => value ? (<div className="warehouse-products-barcode-cell">
              <BarcodePreview value={value} textMaxWidth={180} compactCopy/>
            </div>) : ('--'),
        },
        {
            title: t('domesticProducts.status'),
            dataIndex: 'isActive',
            width: 110,
            render: (value: boolean, record) => (<Switch checked={value} checkedChildren={t('warehouse.active')} unCheckedChildren={t('warehouse.inactive')} disabled={!access.canWriteProduct || togglingProductCodes.includes(record.productCode)} loading={togglingProductCodes.includes(record.productCode)} onChange={(nextChecked) => void handleToggleSingleActive(record, nextChecked)}/>),
        },
        {
            title: t('warehouse.productType'),
            dataIndex: 'productType',
            width: 120,
            render: (value: ProductType) => getProductTypeLabel(value, t),
        },
        {
            title: t('domesticProducts.domesticPrice'),
            dataIndex: 'domesticPrice',
            width: 100,
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            title: t('productCreation.privateLabelPrice'),
            dataIndex: 'labelPrice',
            width: 100,
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            title: t('warehouse.importPrice'),
            dataIndex: 'importPrice',
            width: 100,
            render: (value: number | undefined) => formatPrice(value),
        },
        {
            title: t('warehouse.packingQuantity'),
            dataIndex: 'packingQty',
            width: 140,
            render: (value: number | undefined, record) => value !== undefined && value !== null ? (<Space size={4}>
              <span>{value}</span>
              {record.isPackingQtyFallback ? <Tag color="gold">{t('warehouse.domestic')}</Tag> : <Tag color="green">{t('warehouse.warehouse')}</Tag>}
            </Space>) : ('--'),
        },
        {
            title: t('warehouse.volume'),
            dataIndex: 'volume',
            width: 140,
            render: (value: number | undefined, record) => value !== undefined && value !== null ? (<Space size={4}>
              <span>{value}</span>
              {record.isVolumeFallback ? <Tag color="gold">{t('warehouse.domestic')}</Tag> : <Tag color="green">{t('warehouse.warehouse')}</Tag>}
            </Space>) : ('--'),
        },
        {
            title: t('warehouse.australianSupplier'),
            dataIndex: 'localSupplierCode',
            width: 180,
            sorter: true,
            render: (_value, record) => record.localSupplierName ? (<div className="warehouse-products-supplier-cell">
              <Tag color="purple">{record.localSupplierName}</Tag>
            </div>) : ('--'),
        },
        {
            title: t('warehouse.updatedAt'),
            dataIndex: 'updatedAt',
            width: 180,
            sorter: true,
            render: (value: string | undefined) => formatDateTime(value),
        },
        {
            title: t('warehouse.updatedBy'),
            dataIndex: 'updatedBy',
            width: 140,
            render: (value: string | undefined) => value || '--',
        },
        {
            title: t('common.action'),
            key: 'action',
            width: 220,
            fixed: 'right',
            render: (_, record) => (<Space size={0}>
            {access.canWriteProduct ? (<Button type="link" icon={<EditOutlined />} onClick={() => handleOpenEdit(record)}>
                {t('common.edit')}
              </Button>) : null}
            {record.productType === 1 ? (<Button type="link" icon={<GiftOutlined />} onClick={() => void handleOpenSetItems(record)}>
                {t('warehouse.setSubItems')}
              </Button>) : (<Tooltip title={t('warehouse.setEditOnlyHint')}>
                <Button type="link" icon={<GiftOutlined />} disabled>
                  {t('warehouse.setSubItems')}
                </Button>
              </Tooltip>)}
          </Space>),
        },
    ], [access.canWriteProduct, togglingProductCodes]);
    return (<>
      <style>{warehouseProductsTableStyle}</style>
      <PageContainer title={t('warehouse.productManagement')} subtitle={t('warehouse.productManagementSubtitle')} extra={<Space wrap>
          <Button icon={<DownloadOutlined />} loading={exporting} disabled={exporting} onClick={() => setExportConfigOpen(true)}>
            {t('warehouse.exportExcel')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportFromDomesticOpen(true)}>
            {t('warehouse.importFromDomestic')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => setImportNonHbOpen(true)}>
            {t('warehouse.importNonHb.title')}
          </Button>
          <Button icon={<GiftOutlined />} onClick={() => message.info(t('warehouse.batchSetMigrated'))}>
            {t('warehouse.batchCreateSet')}
          </Button>
          <Button icon={<UploadOutlined />} onClick={() => message.info(t('warehouse.batchImageUploadMigrated'))}>
            {t('warehouse.batchImageUpload')}
          </Button>
          {access.canWriteProduct ? (<Popconfirm title={t('warehouse.confirmBatchActivate')} okText={t('warehouse.active')} cancelText={t('common.cancel')} disabled={!selectedRowKeys.length} onConfirm={() => void handleBatchToggleActive(true)}>
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchActivate')}
              </Button>
            </Popconfirm>) : null}
          {access.canWriteProduct ? (<Popconfirm title={t('warehouse.confirmBatchDeactivate')} okText={t('warehouse.inactive')} cancelText={t('common.cancel')} disabled={!selectedRowKeys.length} onConfirm={() => void handleBatchToggleActive(false)}>
              <Button loading={batchActionLoading} disabled={!selectedRowKeys.length || batchActionLoading}>
                {t('warehouse.batchDeactivate')}
              </Button>
            </Popconfirm>) : null}
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
          <Select value={supplierCode} onChange={setSupplierCode} options={buildSupplierOptions(suppliers)} placeholder={t('warehouse.allDomesticSuppliers')} style={{ width: 240 }} showSearch filterOption={filterSupplierOption} allowClear/>
          <Select value={productType} onChange={setProductType} options={productTypeOptions} placeholder={t('warehouse.allProductTypes')} style={{ width: 160 }} allowClear/>
          <Select value={isActive} onChange={setIsActive} options={getStatusOptions(t)} placeholder={t('warehouse.allStatus')} style={{ width: 140 }} allowClear/>
          <Button type="primary" onClick={() => void loadData({ page: 1 })}>
            {t('common.query')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => {
            setSearchText('');
            setSupplierCode(undefined);
            setProductType(undefined);
            setIsActive(undefined);
            setSortField('createdAt');
            setSortOrder('descend');
            void loadData({
                page: 1,
                searchText: '',
                supplierCode: undefined,
                productType: undefined,
                isActive: undefined,
                sortField: 'createdAt',
                sortOrder: 'descend',
            });
        }}>
            {t('common.reset')}
          </Button>
        </Space>

          <Table className="warehouse-products-table" rowKey="productCode" virtual loading={loading} columns={columns} dataSource={data} rowSelection={{
            fixed: true,
            columnWidth: 56,
            selectedRowKeys,
            onChange: setSelectedRowKeys,
        }} scroll={{ x: 2200, y: 620 }} pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
        }} onChange={(pagination: TablePaginationConfig, _, sorter: SorterResult<WarehouseProductListItem> | SorterResult<WarehouseProductListItem>[]) => {
            const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter;
            const nextSortField = typeof nextSorter?.field === 'string' ? nextSorter.field : sortField;
            const nextSortOrder = nextSorter?.order === 'ascend' || nextSorter?.order === 'descend'
                ? nextSorter.order
                : sortOrder;
            setSortField(nextSortField);
            setSortOrder(nextSortOrder);
            void loadData({
                page: pagination.current || 1,
                pageSize: pagination.pageSize || pageSize,
                sortField: nextSortField,
                sortOrder: nextSortOrder,
            });
        }}/>
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
        }} onAddRow={() => {
            setSetItemsDraft((current) => [
                ...current,
                {
                    id: `temp_${Date.now()}_${Math.random()}`,
                },
            ]);
        }} onRemoveRow={(rowId) => {
            setSetItemsDraft((current) => current.filter((item) => item.id !== rowId));
        }} onChangeField={(rowId, field, value) => {
            setSetItemsDraft((current) => current.map((item) => (item.id === rowId ? { ...item, [field]: value } : item)));
        }} onSubmit={() => void handleSaveSetItems()}/>

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

