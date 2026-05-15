import {
  DeleteOutlined,
  DollarOutlined,
  FileExcelOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Image,
  Input,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import {
  batchUpdateGrades,
  createOrUpdateProductGrade,
  deleteProductGrade,
  getProductGradeList,
} from '../../../services/productGradeService'
import { PRODUCT_GRADE_CONFIG, type ProductGradeListItem } from '../../../types/productGrade'
import BatchPriceModal from './BatchPriceModal'
import PasteImportModal from './PasteImportModal'

const GRADE_TAG_COLOR: Record<string, string> = {
  A: 'purple',
  B: 'blue',
  C: 'orange',
  D: 'red',
}

const gradeFilterOptions = [
  { label: '全部等级', value: '' },
  ...Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => ({
    label: cfg.label,
    value: key,
  })),
]

interface SupplierOption {
  label: string
  value: string
}

function formatPrice(value?: number, prefix = '¥') {
  if (value === undefined || value === null) return '--'
  return `${prefix}${value.toFixed(2)}`
}

export default function ProductGradeManagementPage() {
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<ProductGradeListItem[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [total, setTotal] = useState(0)
  const [search, setSearch] = useState('')
  const [gradeFilter, setGradeFilter] = useState('')
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([])
  const [batchGrade, setBatchGrade] = useState<string | undefined>(undefined)
  const [pasteImportOpen, setPasteImportOpen] = useState(false)
  const [batchPriceOpen, setBatchPriceOpen] = useState(false)

  const loadSuppliers = async () => {
    setSupplierLoading(true)
    try {
      const result = await getActiveChinaSuppliers()
      setSuppliers(
        result.map((item) => ({
          label: `${item.supplierCode} - ${item.supplierName}`,
          value: item.supplierCode,
        })),
      )
    } catch (error) {
      console.error(error)
      message.error('加载供应商列表失败')
    } finally {
      setSupplierLoading(false)
    }
  }

  const loadList = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getProductGradeList({
        page: nextPage,
        pageSize: nextPageSize,
        search: search || undefined,
        grade: gradeFilter || undefined,
        supplierCode,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error('加载等级列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadSuppliers()
    void loadList(1, pageSize)
  }, [])

  const handleDelete = async (id: string) => {
    try {
      await deleteProductGrade(id)
      message.success('删除成功')
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error('删除失败')
    }
  }

  const handleBatchUpdate = async () => {
    if (selectedRowKeys.length === 0) {
      message.warning('请先选择商品')
      return
    }
    if (!batchGrade) {
      message.warning('请选择目标等级')
      return
    }
    try {
      await batchUpdateGrades({
        items: selectedRowKeys.map((productCode) => ({
          productCode,
          grade: batchGrade,
        })),
      })
      message.success(`批量更新 ${selectedRowKeys.length} 条成功`)
      setSelectedRowKeys([])
      setBatchGrade(undefined)
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error('批量更新失败')
    }
  }

  const handleInlineGradeChange = async (productCode: string, newGrade: string) => {
    try {
      await createOrUpdateProductGrade({ productCode, grade: newGrade })
      message.success('等级更新成功')
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error('等级更新失败')
    }
  }

  const columns = useMemo<ColumnsType<ProductGradeListItem>>(
    () => [
      {
        title: '#',
        width: 50,
        render: (_v, _r, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: '供应商',
        width: 150,
        render: (_, record) => record.supplierName || record.supplierCode || '--',
      },
      {
        title: '供应商代码',
        dataIndex: 'supplierCode',
        width: 110,
        render: (value?: string) => value || '--',
      },
      {
        title: '货号',
        dataIndex: 'hbProductNo',
        width: 140,
        render: (value?: string) => value || '--',
      },
      {
        title: '图片',
        dataIndex: 'productImage',
        width: 80,
        render: (value?: string) =>
          value ? (
            <Image src={value} width={48} height={48} style={{ objectFit: 'contain' }} preview={{ mask: '' }} fallback="data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNDgiIGhlaWdodD0iNDgiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3Qgd2lkdGg9IjQ4IiBoZWlnaHQ9IjQ4IiBmaWxsPSIjZjBmMGYwIi8+PHRleHQgeD0iMjQiIHk9IjI4IiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmb250LXNpemU9IjEyIiBmaWxsPSIjY2NjIj7ml6DnvKnnlaXimLQ8L3RleHQ+PC9zdmc+" />
          ) : (
            '--'
          ),
      },
      {
        title: '等级',
        dataIndex: 'grade',
        width: 100,
        render: (grade: string, record) => (
          <Select
            value={grade}
            size="small"
            style={{ width: 80 }}
            onChange={(value) => void handleInlineGradeChange(record.productCode, value)}
          >
            {Object.keys(PRODUCT_GRADE_CONFIG).map((key) => (
              <Select.Option key={key} value={key}>
                <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 0 }}>
                  {key}
                </Tag>
              </Select.Option>
            ))}
          </Select>
        ),
      },
      {
        title: '国内价(RMB)',
        dataIndex: 'domesticPrice',
        width: 110,
        render: (value?: number) => formatPrice(value),
      },
      {
        title: '进货价(AUD)',
        dataIndex: 'importPrice',
        width: 110,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: '零售价(AUD)',
        dataIndex: 'oemPrice',
        width: 110,
        render: (value?: number) => formatPrice(value, 'A$'),
      },
      {
        title: '操作',
        key: 'action',
        width: 80,
        fixed: 'right',
        render: (_, record) => (
          <Popconfirm
            title="确认删除该等级记录？"
            description="删除后该商品将无等级标记。"
            onConfirm={() => void handleDelete(record.id)}
          >
            <Tooltip title="删除等级">
              <Button type="link" danger icon={<DeleteOutlined />} size="small" />
            </Tooltip>
          </Popconfirm>
        ),
      },
    ],
    [page, pageSize],
  )

  return (
    <PageContainer title="商品等级管理" subtitle="管理商品的等级分类（A/B/C/D），支持批量修改和粘贴导入。">
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            showSearch
            allowClear
            placeholder="筛选供应商"
            value={supplierCode}
            onChange={(value) => setSupplierCode(value)}
            options={suppliers}
            loading={supplierLoading}
            style={{ width: 220 }}
            optionFilterProp="label"
          />
          <Select
            value={gradeFilter}
            onChange={setGradeFilter}
            options={gradeFilterOptions}
            style={{ width: 180 }}
          />
          <Input
            placeholder="搜索货号 / 商品名称 / 供应商"
            prefix={<SearchOutlined />}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            allowClear
            style={{ width: 260 }}
          />
          <Button type="primary" onClick={() => void loadList(1, pageSize)}>
            查询
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadList(page, pageSize)}>
            刷新
          </Button>
          <Button
            type="dashed"
            icon={<FileExcelOutlined />}
            onClick={() => setPasteImportOpen(true)}
          >
            粘贴导入
          </Button>
          <Button
            icon={<DollarOutlined />}
            disabled={selectedRowKeys.length === 0}
            onClick={() => setBatchPriceOpen(true)}
          >
            批量改价
          </Button>
        </Space>

        {selectedRowKeys.length > 0 && (
          <Card size="small" style={{ marginBottom: 12, background: '#fafafa' }}>
            <Space>
              <span>已选 {selectedRowKeys.length} 项</span>
              <Select
                placeholder="选择目标等级"
                value={batchGrade}
                onChange={setBatchGrade}
                style={{ width: 160 }}
                allowClear
              >
                {Object.entries(PRODUCT_GRADE_CONFIG).map(([key, cfg]) => (
                  <Select.Option key={key} value={key}>
                    <Tag color={GRADE_TAG_COLOR[key]} style={{ marginRight: 4 }}>
                      {key}
                    </Tag>
                    {cfg.label}
                  </Select.Option>
                ))}
              </Select>
              <Button type="primary" size="small" onClick={() => void handleBatchUpdate()}>
                批量修改
              </Button>
              <Button size="small" onClick={() => setSelectedRowKeys([])}>
                取消选择
              </Button>
            </Space>
          </Card>
        )}

        <Table<ProductGradeListItem>
          rowKey="productCode"
          virtual
          loading={loading}
          columns={columns}
          dataSource={data}
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys as string[]),
            columnWidth: 48,
            fixed: true,
          }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            pageSizeOptions: [20, 50, 100, 200, 500, 1000],
            showQuickJumper: true,
            showTotal: (t) => `共 ${t} 条`,
            onChange: (nextPage, nextPageSize) => {
              void loadList(nextPage, nextPageSize)
            },
          }}
          scroll={{ x: 800, y: 600 }}
        />
      </Card>

      <PasteImportModal
        open={pasteImportOpen}
        onClose={() => setPasteImportOpen(false)}
        onSuccess={() => void loadList(page, pageSize)}
      />

      <BatchPriceModal
        open={batchPriceOpen}
        selectedCount={selectedRowKeys.length}
        productCodes={selectedRowKeys}
        onClose={() => setBatchPriceOpen(false)}
        onSuccess={() => {
          setSelectedRowKeys([])
          setBatchGrade(undefined)
        }}
      />
    </PageContainer>
  )
}
