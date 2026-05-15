import { PlusOutlined, EyeOutlined } from '@ant-design/icons'
import { Button, DatePicker, message, Select, Space, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { getBatchList } from '../../../services/domesticProductCreationService'
import type { BatchInfo } from '../../../types/domesticProductCreation'
import BatchCreateModal from './BatchCreateModal'
import BatchDetailModal from './BatchDetailModal'

const { RangePicker } = DatePicker

export default function ProductCreationPage() {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<BatchInfo[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [suppliers, setSuppliers] = useState<Array<{ supplierCode: string; supplierName: string }>>([])
  const [supplierFilter, setSupplierFilter] = useState<string | undefined>()
  const [dateRange, setDateRange] = useState<[string, string] | undefined>()
  const [createModalVisible, setCreateModalVisible] = useState(false)
  const [detailModalVisible, setDetailModalVisible] = useState(false)
  const [selectedBatch, setSelectedBatch] = useState<BatchInfo | null>(null)

  const loadData = useCallback(async () => {
    setLoading(true)
    try {
      const params: Record<string, unknown> = { page, pageSize }
      if (supplierFilter) params.supplierCode = supplierFilter
      if (dateRange) {
        params.startDate = dateRange[0]
        params.endDate = dateRange[1]
      }
      const response = await getBatchList(params as any)
      setLoading(false)
      if (response.success && response.data) {
        setData(response.data.items || [])
        setTotal(response.data.total || 0)
      } else {
        setData([])
        setTotal(0)
      }
    } catch {
      setLoading(false)
      message.error(t('productCreation.loadBatchListFailed', '加载批次列表失败'))
    }
  }, [page, pageSize, supplierFilter, dateRange])

  const loadSuppliers = useCallback(async () => {
    try {
      const response = await getActiveChinaSuppliers()
      setSuppliers(response || [])
    } catch {
      // ignore
    }
  }, [])

  useEffect(() => {
    loadSuppliers()
  }, [loadSuppliers])

  useEffect(() => {
    loadData()
  }, [loadData])

  const handleCreateSuccess = () => {
    setCreateModalVisible(false)
    loadData()
  }

  const handleViewDetail = (record: BatchInfo) => {
    setSelectedBatch(record)
    setDetailModalVisible(true)
  }

  const columns: ColumnsType<BatchInfo> = [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => (page - 1) * pageSize + index + 1,
    },
    {
      title: t('productCreation.batchNumber', '批次号'),
      dataIndex: 'batchNumber',
      key: 'batchNumber',
      width: 180,
      render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span>,
    },
    {
      title: t('domesticProducts.supplier', '供应商'),
      dataIndex: 'supplierName',
      key: 'supplierName',
      width: 150,
      render: (text, record) => `${record.supplierCode} - ${text}`,
    },
    {
      title: t('productCreation.prefixCode', '前缀码'),
      dataIndex: 'prefixCode',
      key: 'prefixCode',
      width: 100,
      render: (text) => text || '-',
    },
    {
      title: t('productCreation.normalProduct', '普通商品'),
      dataIndex: 'normalCount',
      key: 'normalCount',
      width: 100,
      align: 'center',
    },
    {
      title: t('productCreation.setProduct', '套装商品'),
      dataIndex: 'setCount',
      key: 'setCount',
      width: 100,
      align: 'center',
    },
    {
      title: t('productCreation.totalCount', '总数量'),
      dataIndex: 'totalCount',
      key: 'totalCount',
      width: 100,
      align: 'center',
    },
    {
      title: t('chinaSuppliers.createdAt', '创建时间'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 180,
      render: (text) => (text ? new Date(text).toLocaleString('zh-CN') : '-'),
    },
    {
      title: t('productCreation.createdBy', '创建人'),
      dataIndex: 'createdBy',
      key: 'createdBy',
      width: 120,
      render: (text) => text || '-',
    },
    {
      title: t('common.action', '操作'),
      key: 'actions',
      width: 100,
      fixed: 'right',
      render: (_, record) => (
        <Button type="link" size="small" icon={<EyeOutlined />} onClick={() => handleViewDetail(record)}>
          {t('productCreation.detail', '明细')}
        </Button>
      ),
    },
  ]

  return (
    <PageContainer
      title={t('productCreation.batchCreateTitle', '货号条码批量创建')}
      subtitle={t('productCreation.batchCreateSubtitle', '管理货号条码创建批次')}
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateModalVisible(true)}>
          {t('productCreation.createBatch', '创建批次')}
        </Button>
      }
    >
      <div style={{ marginBottom: 16 }}>
        <Space wrap>
          <Select
            placeholder={t('productCreation.filterSupplier', '筛选供应商')}
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 200 }}
            value={supplierFilter}
            onChange={(v) => {
              setSupplierFilter(v)
              setPage(1)
            }}
            options={suppliers.map((s) => ({
              label: `${s.supplierCode} - ${s.supplierName}`,
              value: s.supplierCode,
            }))}
          />
          <RangePicker
            onChange={(_, dateStrings) => {
              setDateRange(dateStrings[0] && dateStrings[1] ? (dateStrings as [string, string]) : undefined)
              setPage(1)
            }}
          />
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="batchNumber"
        loading={loading}
        pagination={{
          current: page,
          pageSize,
          total,
          showSizeChanger: true,
          showQuickJumper: true,
          showTotal: (total) => t('common.totalCount', '共 {{count}} 条', { count: total }),
          onChange: (p, ps) => {
            setPage(p)
            setPageSize(ps)
          },
        }}
        scroll={{ x: 1150 }}
        size="small"
      />

      <BatchCreateModal
        visible={createModalVisible}
        onClose={() => setCreateModalVisible(false)}
        onSuccess={handleCreateSuccess}
      />

      <BatchDetailModal
        visible={detailModalVisible}
        batch={selectedBatch}
        onClose={() => {
          setDetailModalVisible(false)
          setSelectedBatch(null)
        }}
      />
    </PageContainer>
  )
}
