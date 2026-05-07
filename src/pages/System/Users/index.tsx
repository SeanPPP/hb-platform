import { EyeOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons'
import { Button, Card, Input, Space, Table, Tag, message } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import PageContainer from '../../../components/PageContainer'
import { getUsers } from '../../../services/userService'
import type { UserDto } from '../../../types/user'

export default function SystemUsersPage() {
  const [loading, setLoading] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [data, setData] = useState<UserDto[]>([])
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [total, setTotal] = useState(0)
  const navigate = useNavigate()

  const loadData = async (nextPage = page, nextPageSize = pageSize) => {
    setLoading(true)
    try {
      const result = await getUsers({
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
      message.error('加载用户列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData(1, pageSize)
  }, [])

  const columns = useMemo<ColumnsType<UserDto>>(
    () => [
      { title: '用户名', dataIndex: 'username', width: 180 },
      { title: '姓名', dataIndex: 'fullName', width: 160, render: (value) => value || '--' },
      { title: '邮箱', dataIndex: 'email', width: 220 },
      {
        title: '角色',
        dataIndex: 'roleNames',
        width: 220,
        render: (value: string[]) =>
          value?.length ? value.map((item) => <Tag key={item}>{item}</Tag>) : '--',
      },
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
        fixed: 'right',
        render: (_, record) => (
          <Button
            type="link"
            icon={<EyeOutlined />}
            onClick={() => navigate(`/system/users/${record.userGUID}`)}
          >
            详情
          </Button>
        ),
      },
    ],
    [navigate],
  )

  return (
    <PageContainer
      title="用户管理"
      subtitle="这一页已经接到新框架的菜单、Tabs 和权限体系。点击用户详情会以完整路径打开独立 Tab。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            placeholder="搜索用户名 / 姓名 / 邮箱"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            style={{ width: 280 }}
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
          rowKey="userGUID"
          loading={loading}
          columns={columns}
          dataSource={data}
          scroll={{ x: 980 }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            onChange: (nextPage, nextPageSize) => {
              void loadData(nextPage, nextPageSize)
            },
          }}
        />
      </Card>
    </PageContainer>
  )
}
