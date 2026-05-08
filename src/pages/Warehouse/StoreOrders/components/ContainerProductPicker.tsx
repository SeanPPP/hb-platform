import { ArrowLeftOutlined } from '@ant-design/icons'
import { Button, Image, Input, InputNumber, Modal, Space, Steps, Table, Tag, Typography, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { SortOrder } from 'antd/es/table/interface'
import { useEffect, useMemo, useState } from 'react'
import { getContainerList, getContainerProducts } from '../../../../services/containerService'
import type { ContainerDetail, ContainerMain } from '../../../../types/container'

interface ContainerProductPickerProps {
  open: boolean
  loading?: boolean
  onClose: () => void
  onConfirm: (items: Array<{ productCode: string; quantity: number; importPrice?: number }>) => Promise<void>
  alreadySelectedCodes?: string[]
}

type ContainerRow = ContainerMain & { key: string }
type ProductRow = ContainerDetail & { key: string }

export default function ContainerProductPicker({
  open,
  loading,
  onClose,
  onConfirm,
  alreadySelectedCodes = [],
}: ContainerProductPickerProps) {
  const [step, setStep] = useState(0)
  const [fetching, setFetching] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [containers, setContainers] = useState<ContainerRow[]>([])
  const [selectedContainer, setSelectedContainer] = useState<ContainerRow | null>(null)
  const [products, setProducts] = useState<ProductRow[]>([])
  const [containerKeyword, setContainerKeyword] = useState('')
  const [productKeyword, setProductKeyword] = useState('')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
  const [productQuantities, setProductQuantities] = useState<Record<string, number | undefined>>({})
  const [productPage, setProductPage] = useState(1)
  const [productPageSize, setProductPageSize] = useState(50)
  const [productSortOrder, setProductSortOrder] = useState<SortOrder>('ascend')

  const resetState = () => {
    setStep(0)
    setContainers([])
    setSelectedContainer(null)
    setProducts([])
    setContainerKeyword('')
    setProductKeyword('')
    setSelectedRowKeys([])
    setProductQuantities({})
    setProductPage(1)
    setProductPageSize(50)
    setProductSortOrder('ascend')
  }

  useEffect(() => {
    if (!open) {
      return
    }

    resetState()

    const loadContainers = async () => {
      setFetching(true)
      try {
        const result = await getContainerList({
          page: 1,
          pageSize: 1000,
          sortBy: '预计到岸日期',
          sortDirection: 'desc',
        })
        setContainers(result.containers.map((item) => ({ ...item, key: item.hguid })))
      } catch (error) {
        console.error(error)
        message.error(error instanceof Error ? error.message : '加载货柜列表失败')
      } finally {
        setFetching(false)
      }
    }

    void loadContainers()
  }, [open])

  const handleSelectContainer = async (container: ContainerRow) => {
    setSelectedContainer(container)
    setStep(1)
    setSelectedRowKeys([])
    setProductQuantities({})
    setProductPage(1)
    setProductPageSize(50)
    setProductSortOrder('ascend')
    setFetching(true)
    try {
      const result = await getContainerProducts(container.hguid)
      const rows = result
        .filter((item) => item.商品编码 || item.商品信息?.商品编码)
        .map((item) => ({
          ...item,
          key: item.hguid,
        }))
      setProducts(rows)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载货柜商品失败')
      setStep(0)
    } finally {
      setFetching(false)
    }
  }

  const filteredContainers = useMemo(
    () =>
      containers.filter(
        (item) =>
          !containerKeyword.trim() ||
          item.货柜编号?.toLowerCase().includes(containerKeyword.trim().toLowerCase()),
      ),
    [containerKeyword, containers],
  )

  const filteredProducts = useMemo(() => {
    const matched = products.filter((item) => {
        if (!productKeyword.trim()) {
          return true
        }

        const keyword = productKeyword.trim().toLowerCase()
        return (
          item.商品信息?.货号?.toLowerCase().includes(keyword) ||
          item.商品信息?.商品名称?.toLowerCase().includes(keyword) ||
          item.商品编码?.toLowerCase().includes(keyword)
        )
      })

    const sorted = [...matched].sort((left, right) => {
      const leftValue = left.商品信息?.货号 || ''
      const rightValue = right.商品信息?.货号 || ''
      const compareResult = leftValue.localeCompare(rightValue, 'zh-CN', { numeric: true, sensitivity: 'base' })
      return productSortOrder === 'descend' ? -compareResult : compareResult
    })

    return sorted
  }, [productKeyword, productSortOrder, products])

  const pagedProducts = useMemo(() => {
    const start = (productPage - 1) * productPageSize
    return filteredProducts.slice(start, start + productPageSize)
  }, [filteredProducts, productPage, productPageSize])

  const handleConfirm = async () => {
    const items: Array<{ productCode: string; quantity: number; importPrice?: number }> = []

    selectedRowKeys.forEach((key) => {
      const row = products.find((item) => item.key === key)
      const productCode = row?.商品编码 || row?.商品信息?.商品编码
      const quantity = productQuantities[String(key)]
      if (!row || !productCode || quantity === undefined || quantity <= 0) {
        return
      }

      items.push({
        productCode,
        quantity,
        importPrice: row.进口价格,
      })
    })

    if (!selectedRowKeys.length) {
      message.warning('请先选择商品')
      return
    }

    if (!items.length) {
      message.warning('请先为已选商品填写大于 0 的数量')
      return
    }

    setSubmitting(true)
    try {
      await onConfirm(items)
      onClose()
    } finally {
      setSubmitting(false)
    }
  }

  const containerColumns: ColumnsType<ContainerRow> = [
    {
      title: '货柜编号',
      dataIndex: '货柜编号',
      width: 160,
      render: (value?: string) => value || '--',
    },
    {
      title: '预计到岸日期',
      dataIndex: '预计到岸日期',
      width: 140,
      render: (value?: string) => value || '--',
    },
    {
      title: '合计件数',
      dataIndex: '合计件数',
      width: 100,
      render: (value?: number) => value ?? 0,
    },
    {
      title: '合计数量',
      dataIndex: '合计数量',
      width: 100,
      render: (value?: number) => value ?? 0,
    },
  ]

  const productColumns: ColumnsType<ProductRow> = [
    {
      title: '图片',
      width: 72,
      render: (_, record) =>
        record.商品信息?.商品图片 ? (
          <Image
            src={record.商品信息.商品图片}
            alt={record.商品信息?.商品名称}
            width={48}
            height={48}
            style={{ borderRadius: 4, objectFit: 'cover' }}
            fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="
          />
        ) : (
          '--'
        ),
    },
    {
      title: '货号',
      dataIndex: '商品信息',
      width: 120,
      sorter: true,
      sortOrder: productSortOrder,
      render: (_, record) => record.商品信息?.货号 || '--',
    },
    {
      title: '商品名称',
      ellipsis: true,
      render: (_, record) => record.商品信息?.商品名称 || '--',
    },
    {
      title: '进口价',
      dataIndex: '进口价格',
      width: 90,
      render: (value?: number) => (value === undefined || value === null ? '--' : value.toFixed(2)),
    },
    {
      title: '货柜数量',
      dataIndex: '装柜数量',
      width: 100,
      render: (value?: number) => value ?? 0,
    },
    {
      title: '发货数量',
      width: 120,
      render: (_, record) => {
        const productCode = record.商品编码 || record.商品信息?.商品编码
        const disabled = productCode ? alreadySelectedCodes.includes(productCode) : false

        return (
          <InputNumber
            min={0}
            precision={0}
            disabled={disabled}
            placeholder="请输入"
            style={{ width: '100%' }}
            value={productQuantities[String(record.key)]}
            onChange={(value) =>
              setProductQuantities((current) => ({
                ...current,
                [String(record.key)]: value === null ? undefined : Number(value),
              }))
            }
          />
        )
      },
    },
    {
      title: '状态',
      width: 90,
      render: (_, record) => {
        const productCode = record.商品编码 || record.商品信息?.商品编码
        const disabled = productCode ? alreadySelectedCodes.includes(productCode) : false
        return disabled ? <Tag color="warning">已在订单中</Tag> : <Tag color="success">可添加</Tag>
      },
    },
  ]

  return (
    <Modal
      title="从货柜中选择商品"
      open={open}
      width={1000}
      destroyOnClose
      onCancel={onClose}
      footer={null}
    >
      <Steps
        current={step}
        style={{ marginBottom: 16 }}
        items={[{ title: '选择货柜' }, { title: '选择商品' }]}
      />

      {step === 0 ? (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Input
            allowClear
            value={containerKeyword}
            placeholder="按货柜编号搜索"
            onChange={(event) => setContainerKeyword(event.target.value)}
          />
          <Table<ContainerRow>
            rowKey="key"
            loading={fetching}
            dataSource={filteredContainers}
            columns={containerColumns}
            pagination={false}
            scroll={{ y: 420 }}
            onRow={(record) => ({
              onClick: () => void handleSelectContainer(record),
              style: { cursor: 'pointer' },
            })}
          />
        </Space>
      ) : (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          <Space wrap style={{ justifyContent: 'space-between', width: '100%' }}>
            <Space wrap>
              <Tag color="blue">{selectedContainer?.货柜编号 || '未选择货柜'}</Tag>
              <Typography.Text type="secondary">默认数量为空，只有填写发货数量后才会加入订单</Typography.Text>
            </Space>
            <Button icon={<ArrowLeftOutlined />} onClick={() => setStep(0)}>
              返回货柜列表
            </Button>
          </Space>

          <Input
            allowClear
            value={productKeyword}
            placeholder="按货号、商品名称或商品编码搜索"
            onChange={(event) => setProductKeyword(event.target.value)}
          />

          <Table<ProductRow>
            rowKey="key"
            loading={fetching}
            dataSource={pagedProducts}
            columns={productColumns}
            pagination={{
              current: productPage,
              pageSize: productPageSize,
              total: filteredProducts.length,
              showSizeChanger: true,
              pageSizeOptions: ['20', '50', '100', '500'],
              onChange: (page, pageSize) => {
                setProductPage(page)
                setProductPageSize(pageSize)
              },
            }}
            scroll={{ y: 420 }}
            onChange={(_, __, sorter) => {
              const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
              setProductSortOrder(nextSorter?.order === 'descend' ? 'descend' : 'ascend')
            }}
            rowSelection={{
              selectedRowKeys,
              onChange: (keys) => setSelectedRowKeys(keys),
              preserveSelectedRowKeys: true,
              getCheckboxProps: (record) => {
                const productCode = record.商品编码 || record.商品信息?.商品编码
                return {
                  disabled: productCode ? alreadySelectedCodes.includes(productCode) : true,
                }
              },
            }}
          />

          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <Space>
              <Button onClick={onClose}>取消</Button>
              <Button type="primary" loading={loading || submitting} onClick={() => void handleConfirm()}>
                加入已选商品 ({selectedRowKeys.length})
              </Button>
            </Space>
          </div>
        </Space>
      )}
    </Modal>
  )
}
