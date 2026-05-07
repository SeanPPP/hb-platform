import { EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Button, Card, Input, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getRoles } from '../../../services/roleService'
import type { RoleDto } from '../../../types/role'

export default function SystemRolesPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<RoleDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const navigate = useNavigate()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getRoles({
        page: nextPage,
        pageSize: nextPageSize,
        searchKeyword: keyword || undefined,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
    } catch (error) {
      console.error(error)
      message.error('加载角色列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const columns = useMemo<ColumnsType<RoleDto>>(
    () => [
      { title: '角色名称', dataIndex: 'roleName', width: 220 },
      { title: '描述', dataIndex: 'description', render: (value) => value || '--' },
      {
        title: '状态',
        dataIndex: 'isActive',
        width: 100,
        render: (value: boolean) => (
          <Tag color={value ? 'success' : 'default'}>{value ? '启用' : '停用'}</Tag>
        ),
      },
      { title: '关联用户数', dataIndex: 'userCount', width: 140 },
      {
        title: '操作',
        key: 'action',
        width: 120,
        render: (_, record) => (
          <Button type="link" icon={<EyeOutlined />} onClick={() => navigate(`/system/roles/${record.roleGUID}`)}>
            详情
          </Button>
        ),
      },
    ],
    [navigate],
  )

  return (
    <PageContainer
      title="角色管理"
      subtitle="先迁移系统管理页，后续再继续搬运旧项目中更复杂的业务页。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索角色名称"
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
          rowKey="roleGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
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
