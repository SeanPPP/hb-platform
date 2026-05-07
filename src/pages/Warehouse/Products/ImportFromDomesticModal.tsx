import { CheckCircleOutlined } from '@ant-design/icons'
import { Button, Checkbox, Input, InputNumber, Modal, Result, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import {
  getDomesticProductsNotInWarehouse,
  importFromDomestic,
  type DomesticProductNotInWarehouseItem,
  type ImportFromDomesticItem,
} from '../../../services/warehouseProductService'
import { ProductTypeLabels } from '../../../types/domesticProduct'

function formatPrice(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }

  return value.toFixed(2)
}

interface EditablePriceData {
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  volume?: number
}

interface ImportFromDomesticModalProps {
  open: boolean
  onCancel: () => void
  onSuccess: () => void
}

function sortByItemNumber<T extends { itemNumber?: string }>(items: T[]) {
  return [...items].sort((left, right) =>
    (left.itemNumber || '').localeCompare(right.itemNumber || '', 'zh-CN', { numeric: true }),
  )
}

export default function ImportFromDomesticModal({
  open,
  onCancel,
  onSuccess,
}: ImportFromDomesticModalProps) {
  const [loading, setLoading] = useState(false)
  const [importing, setImporting] = useState(false)
  const [items, setItems] = useState<DomesticProductNotInWarehouseItem[]>([])
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [searchText, setSearchText] = useState('')
  const [syncStorePrices, setSyncStorePrices] = useState(true)
  const [syncMultiCodes, setSyncMultiCodes] = useState(true)
  const [editablePrices, setEditablePrices] = useState<Record<string, EditablePriceData>>({})

  const loadItems = async (overrides?: { page?: number; pageSize?: number; searchText?: string }) => {
    const nextPage = overrides?.page ?? page
    const nextPageSize = overrides?.pageSize ?? pageSize
    const nextSearchText = overrides?.searchText ?? searchText

    setLoading(true)
    try {
      const result = await getDomesticProductsNotInWarehouse({
        page: nextPage,
        pageSize: nextPageSize,
        globalSearch: nextSearchText.trim() || undefined,
      })

      setItems(sortByItemNumber(result.data || []))
      setTotal(result.total || 0)
      setPage(nextPage)
      setPageSize(nextPageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载国内商品失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (!open) {
      return
    }

    void loadItems({ page: 1 })
  }, [open])

  useEffect(() => {
    if (!open) {
      setSelectedRowKeys([])
      setEditablePrices({})
      setSearchText('')
      setPage(1)
      setPageSize(20)
      setTotal(0)
      setSyncStorePrices(true)
      setSyncMultiCodes(true)
    }
  }, [open])

  const columns = useMemo<ColumnsType<DomesticProductNotInWarehouseItem>>(
    () => [
      {
        title: '货号',
        dataIndex: 'itemNumber',
        width: 160,
      },
      {
        title: '条码',
        dataIndex: 'barcode',
        width: 160,
        render: (value?: string) => value || '--',
      },
      {
        title: '商品名称',
        dataIndex: 'productName',
        width: 220,
        ellipsis: true,
      },
      {
        title: '类型',
        dataIndex: 'productType',
        width: 100,
        render: (value: number) => ProductTypeLabels[value as keyof typeof ProductTypeLabels] || '--',
      },
      {
        title: '供应商',
        dataIndex: 'supplierName',
        width: 180,
        ellipsis: true,
        render: (value?: string) => value || '--',
      },
      {
        title: '国内价',
        dataIndex: 'domesticPrice',
        width: 120,
        render: (_value, record) => (
          <InputNumber
            min={0}
            precision={2}
            style={{ width: '100%' }}
            value={editablePrices[record.productCode]?.domesticPrice ?? record.domesticPrice}
            onChange={(value) =>
              setEditablePrices((current) => ({
                ...current,
                [record.productCode]: {
                  ...current[record.productCode],
                  domesticPrice: value ?? undefined,
                },
              }))
            }
          />
        ),
      },
      {
        title: '零售',
        dataIndex: 'oemPrice',
        width: 120,
        render: (_value, record) => (
          <InputNumber
            min={0}
            precision={2}
            style={{ width: '100%' }}
            value={editablePrices[record.productCode]?.oemPrice ?? record.oemPrice}
            onChange={(value) =>
              setEditablePrices((current) => ({
                ...current,
                [record.productCode]: {
                  ...current[record.productCode],
                  oemPrice: value ?? undefined,
                },
              }))
            }
          />
        ),
      },
      {
        title: '进口价',
        dataIndex: 'importPrice',
        width: 120,
        render: (_value, record) => (
          <InputNumber
            min={0}
            precision={2}
            style={{ width: '100%' }}
            value={editablePrices[record.productCode]?.importPrice ?? record.importPrice}
            onChange={(value) =>
              setEditablePrices((current) => ({
                ...current,
                [record.productCode]: {
                  ...current[record.productCode],
                  importPrice: value ?? undefined,
                },
              }))
            }
          />
        ),
      },
      {
        title: '体积',
        dataIndex: 'volume',
        width: 120,
        render: (_value, record) => (
          <InputNumber
            min={0}
            precision={4}
            style={{ width: '100%' }}
            value={editablePrices[record.productCode]?.volume ?? record.volume}
            onChange={(value) =>
              setEditablePrices((current) => ({
                ...current,
                [record.productCode]: {
                  ...current[record.productCode],
                  volume: value ?? undefined,
                },
              }))
            }
          />
        ),
      },
      {
        title: '结构',
        key: 'flags',
        width: 140,
        render: (_, record) => (
          <Space size={[4, 4]} wrap>
            {record.hasSetProducts ? <Tag color="gold">套装</Tag> : null}
            {record.hasMultiCodes ? <Tag color="blue">多码</Tag> : null}
            {!record.hasSetProducts && !record.hasMultiCodes ? <Tag>普通</Tag> : null}
          </Space>
        ),
      },
    ],
    [editablePrices],
  )

  const handleImport = async () => {
    if (!selectedRowKeys.length) {
      message.warning('请至少选择一个商品')
      return
    }

    const selectedItems: ImportFromDomesticItem[] = selectedRowKeys.map((key) => {
      const productCode = String(key)
      const source = items.find((item) => item.productCode === productCode)
      const override = editablePrices[productCode]

      return {
        productCode,
        domesticPrice: override?.domesticPrice ?? source?.domesticPrice,
        oemPrice: override?.oemPrice ?? source?.oemPrice,
        importPrice: override?.importPrice ?? source?.importPrice,
        volume: override?.volume ?? source?.volume,
      }
    })

    const invalidItems = selectedItems.filter(
      (item) =>
        !item.domesticPrice ||
        item.domesticPrice <= 0 ||
        !item.oemPrice ||
        item.oemPrice <= 0 ||
        !item.importPrice ||
        item.importPrice <= 0,
    )

    if (invalidItems.length) {
      message.warning(`有 ${invalidItems.length} 个商品的国内价/零售价/进口价未正确填写`)
      return
    }

    try {
      setImporting(true)
      const result = await importFromDomestic({
        items: selectedItems,
        syncBranchPrice: syncStorePrices,
        syncMultiCodePrice: syncMultiCodes,
      })

      if (!result.success) {
        message.error(result.message || '导入失败')
        return
      }

      const successCount = result.successCount ?? selectedItems.length
      const failedCount = result.failedCount ?? 0
      const failedMessages = result.errors?.length
        ? result.errors
        : (result.results ?? []).filter((item) => !item.success).map((item) => item.message || item.productCode)

      Modal.success({
        title: failedCount ? '部分导入成功' : '导入成功',
        icon: <CheckCircleOutlined style={{ color: failedCount ? '#faad14' : '#52c41a' }} />,
        content: (
          <Result
            status={failedCount ? 'warning' : 'success'}
            title={failedCount ? '部分商品导入成功' : '已成功导入商品'}
            subTitle={`成功 ${successCount} 个，失败 ${failedCount} 个`}
            extra={
              failedMessages.length ? (
                <div style={{ textAlign: 'left', maxHeight: 180, overflow: 'auto' }}>
                  {failedMessages.map((item, index) => (
                    <div key={`${item}_${index}`}>- {item}</div>
                  ))}
                </div>
              ) : null
            }
          />
        ),
        onOk: () => {
          onSuccess()
          onCancel()
        },
      })
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '导入失败')
    } finally {
      setImporting(false)
    }
  }

  return (
    <Modal
      title="从国内导入"
      open={open}
      width={1400}
      destroyOnClose
      okText={`导入选中 (${selectedRowKeys.length})`}
      cancelText="关闭"
      confirmLoading={importing}
      onCancel={onCancel}
      onOk={() => void handleImport()}
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Space wrap>
          <Input.Search
            allowClear
            placeholder="搜索货号 / 条码 / 商品名称"
            style={{ width: 320 }}
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            onSearch={(value) => void loadItems({ page: 1, searchText: value })}
          />
          <Checkbox checked={syncStorePrices} onChange={(event) => setSyncStorePrices(event.target.checked)}>
            同步分店价格
          </Checkbox>
          <Checkbox checked={syncMultiCodes} onChange={(event) => setSyncMultiCodes(event.target.checked)}>
            同步多码价格
          </Checkbox>
          <Button onClick={() => void loadItems({ page: 1 })}>刷新</Button>
        </Space>

        <Space size={24}>
          <span>待导入: {total}</span>
          <span>已选中: {selectedRowKeys.length}</span>
          {selectedRowKeys.length ? (
            <span>
              当前零售列取值: {
                formatPrice(
                  editablePrices[String(selectedRowKeys[0])]?.oemPrice ??
                    items.find((item) => item.productCode === String(selectedRowKeys[0]))?.oemPrice,
                )
              }
            </span>
          ) : null}
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
          scroll={{ x: 1250, y: 520 }}
        />
      </Space>
    </Modal>
  )
}
