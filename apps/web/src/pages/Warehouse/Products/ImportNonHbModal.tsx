import { Image, Input, Modal, Select, Space, Table, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getActiveLocalSuppliers,
  getNonHotbargainProductsNotInWarehouse,
  importNonHotbargainProducts,
  type LocalSupplierOption,
  type NonHotbargainProductNotInWarehouseItem,
} from '../../../services/warehouseProductService'
import { ProductTypeLabels } from '../../../types/domesticProduct'

function formatPrice(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }

  return value.toFixed(2)
}

interface ImportNonHbModalProps {
  open: boolean
  onCancel: () => void
  onSuccess: () => void
}

function sortByItemNumber<T extends { itemNumber?: string }>(items: T[]) {
  return [...items].sort((left, right) =>
    (left.itemNumber || '').localeCompare(right.itemNumber || '', 'zh-CN', { numeric: true }),
  )
}

export default function ImportNonHbModal({ open, onCancel, onSuccess }: ImportNonHbModalProps) {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [importing, setImporting] = useState(false)
  const [items, setItems] = useState<NonHotbargainProductNotInWarehouseItem[]>([])
  const [supplierOptions, setSupplierOptions] = useState<LocalSupplierOption[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [searchText, setSearchText] = useState('')
  const [supplierCode, setSupplierCode] = useState<string>()

  const loadItems = async (overrides?: {
    page?: number
    pageSize?: number
    searchText?: string
    supplierCode?: string
  }) => {
    const nextPage = overrides?.page ?? page
    const nextPageSize = overrides?.pageSize ?? pageSize
    const nextSearchText = overrides?.searchText ?? searchText
    const nextSupplierCode = overrides?.supplierCode ?? supplierCode

    setLoading(true)
    try {
      const result = await getNonHotbargainProductsNotInWarehouse({
        page: nextPage,
        pageSize: nextPageSize,
        globalSearch: nextSearchText.trim() || undefined,
        filters: nextSupplierCode ? { localSupplierCode: [nextSupplierCode] } : undefined,
      })

      setItems(sortByItemNumber(result.data || []))
      setTotal(result.total || 0)
      setPage(nextPage)
      setPageSize(nextPageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.importNonHb.loadFailed', '加载非国内商品失败'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!open) {
      return
    }

    void Promise.all([
      loadItems({ page: 1 }),
      getActiveLocalSuppliers()
        .then((result) => {
          setSupplierOptions(
            result
              .sort((left, right) => left.name.localeCompare(right.name, 'zh-CN')),
          )
        })
        .catch((error) => {
          console.error(error)
          message.error(t('warehouse.importNonHb.loadSupplierFailed', '加载本地供应商失败'))
        }),
    ])
  }, [open])

  useEffect(() => {
    if (!open) {
      setSelectedRowKeys([])
      setItems([])
      setSupplierOptions([])
      setSearchText('')
      setSupplierCode(undefined)
      setPage(1)
      setPageSize(20)
      setTotal(0)
    }
  }, [open])

  const columns = useMemo<ColumnsType<NonHotbargainProductNotInWarehouseItem>>(
    () => [
      {
        title: t('warehouse.importNonHb.image', '图片'),
        dataIndex: 'productImage',
        width: 90,
        render: (value: string | undefined, record) => (
          <Image
            src={value}
            alt={record.productName}
            width={44}
            height={44}
            style={{ borderRadius: 4, objectFit: 'cover' }}
            fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
          />
        ),
      },
      {
        title: t('warehouse.importNonHb.itemNumber', '货号'),
        dataIndex: 'itemNumber',
        width: 160,
      },
      {
        title: t('domesticProducts.barcode', '条码'),
        dataIndex: 'barcode',
        width: 160,
        render: (value?: string) => value || '--',
      },
      {
        title: t('warehouse.importNonHb.productName', '商品名称'),
        dataIndex: 'productName',
        width: 240,
        ellipsis: true,
      },
      {
        title: t('domesticProducts.supplier', '供应商'),
        dataIndex: 'localSupplierName',
        width: 180,
        ellipsis: true,
        render: (value?: string) => value || '--',
      },
      {
        title: t('warehouse.importNonHb.type', '类型'),
        dataIndex: 'productType',
        width: 100,
        render: (value: number) => ProductTypeLabels[value as keyof typeof ProductTypeLabels] || '--',
      },
      {
        title: t('warehouse.importNonHb.purchasePrice', '进货价'),
        dataIndex: 'purchasePrice',
        width: 100,
        render: (value?: number) => formatPrice(value),
      },
      {
        title: t('warehouse.importNonHb.retail', '零售'),
        dataIndex: 'retailPrice',
        width: 100,
        render: (value?: number) => formatPrice(value),
      },
    ],
    [t],
  )

  const handleImport = async () => {
    if (!selectedRowKeys.length) {
      message.warning(t('warehouse.importNonHb.selectFirst', '请先选择要导入的商品'))
      return
    }

    try {
      setImporting(true)
      const result = await importNonHotbargainProducts(selectedRowKeys.map(String))

      if (!result.success) {
        message.error(result.message || t('warehouse.importFailed', '导入失败'))
        return
      }

      message.success(t('warehouse.importNonHb.importSuccess', '成功导入 {{count}} 个商品', { count: result.successCount ?? selectedRowKeys.length }))
      onSuccess()
      onCancel()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.importFailed', '导入失败'))
    } finally {
      setImporting(false)
    }
  }

  return (
    <Modal
      title={t('warehouse.importNonHb.title', '导入非国内商品')}
      open={open}
      width={1280}
      destroyOnHidden
      okText={t('warehouse.importNonHb.importSelected', '导入选中 ({{count}})', { count: selectedRowKeys.length })}
      cancelText={t('common.close', '关闭')}
      confirmLoading={importing}
      onCancel={onCancel}
      onOk={() => void handleImport()}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space wrap>
          <Input.Search
            allowClear
            placeholder={t('warehouse.importNonHb.searchPlaceholder', '搜索货号 / 条码 / 商品名称')}
            style={{ width: 320 }}
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            onSearch={(value) => void loadItems({ page: 1, searchText: value })}
          />
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('warehouse.importNonHb.filterSupplier', '筛选供应商')}
            style={{ width: 260 }}
            value={supplierCode}
            options={supplierOptions.map((item) => ({
              value: item.localSupplierCode,
              label: item.name,
            }))}
            onChange={(value) => {
              setSupplierCode(value)
              void loadItems({ page: 1, supplierCode: value })
            }}
          />
        </Space>

        <Table
          rowKey="productCode"
          virtual
          loading={loading}
          columns={columns}
          dataSource={items}
          rowSelection={{
            selectedRowKeys,
            onChange: setSelectedRowKeys,
            preserveSelectedRowKeys: true,
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => void loadItems({ page: nextPage, pageSize: nextPageSize }),
          }}
          scroll={{ x: 1100, y: 520 }}
        />
      </Space>
    </Modal>
  )
}
