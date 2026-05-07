import { EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Button, Card, Input, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getStores } from '../../../services/storeService'
import type { StoreDto } from '../../../types/store'

export default function SystemStoresPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<StoreDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const navigate = useNavigate()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getStores({
        page: nextPage,
        pageSize: nextPageSize,
        search: keyword || undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error('加载分店列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const columns = useMemo<ColumnsType<StoreDto>>(
    () => [
      { title: '分店名称', dataIndex: 'storeName', width: 240 },
      { title: '分店编码', dataIndex: 'storeCode', width: 140 },
      { title: '品牌', dataIndex: 'brandName', width: 180, render: (value) => value || '--' },
      { title: '电话', dataIndex: 'contactPhone', width: 160, render: (value) => value || '--' },
      {
        title: '状态',
        dataIndex: 'isActive',
        width: 100,
        render: (value: boolean) => (
          <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
        ),
      },
      {
        title: '操作',
        key: 'action',
        width: 120,
        render: (_, record) => (
          <Button type="link" icon={<EyeOutlined />} onClick={() => navigate(`/system/stores/${record.storeGUID}`)}>
            详情
          </Button>
        ),
      },
    ],
    [navigate],
  )

  return (
    <PageContainer
      title="分店管理"
      subtitle="菜单、详情页和 Tabs 模式已经切换到当前新框架。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索分店名称 / 编码"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 260 }}
            allowClear
          />
          <Button type="primary" onClick={() => void loadData(1, pageSize)}>
            查询
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadData(page, pageSize)}>
            刷新
          </Button>
        </Space>

        <Table
          rowKey="storeGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 980 }}
          pagination={{
            current: page,
            pageSize,
            total,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>
    </PageContainer>
  )
}
