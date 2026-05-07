import { SearchOutlined } from '@ant-design/icons'
import { Button, Card, Input, Space, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMemo, useState } from 'react'
import PageContainer from '../../components/PageContainer'

interface UserRow {
  key: string
  name: string
  dept: string
  status: '启用' | '停用'
  email: string
}

const sourceData: UserRow[] = Array.from({ length: 42 }).map((_, index) => ({
  key: String(index + 1),
  name: `用户 ${index + 1}`,
  dept: ['产品部', '研发部', '运营部'][index % 3],
  status: index % 4 === 0 ? '停用' : '启用',
  email: `user${index + 1}@demo.com`,
}))

const columns: ColumnsType<UserRow> = [
  { title: '姓名', dataIndex: 'name' },
  { title: '部门', dataIndex: 'dept' },
  {
    title: '状态',
    dataIndex: 'status',
    render: (value: UserRow['status']) =>
      value === '启用' ? <Tag color="success">启用</Tag> : <Tag color="default">停用</Tag>,
  },
  { title: '邮箱', dataIndex: 'email' },
]

export default function UserListPage() {
  const [keyword, setKeyword] = useState('')
  const [selectedDept, setSelectedDept] = useState('全部')
  const [clickCount, setClickCount] = useState(0)

  const data = useMemo(() => {
    return sourceData.filter((item) => {
      const matchKeyword =
        keyword.trim() === '' ||
        item.name.includes(keyword.trim()) ||
        item.email.includes(keyword.trim())
      const matchDept = selectedDept === '全部' || item.dept === selectedDept
      return matchKeyword && matchDept
    })
  }, [keyword, selectedDept])

  return (
    <PageContainer
      title="用户列表"
      subtitle="试试输入关键字、切换部门、翻页后再切到其他 Tab，回来时页面状态仍会保留。"
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            allowClear
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            placeholder="按姓名或邮箱搜索"
            style={{ width: 240 }}
          />
          {['全部', '产品部', '研发部', '运营部'].map((dept) => (
            <Button
              key={dept}
              type={dept === selectedDept ? 'primary' : 'default'}
              onClick={() => setSelectedDept(dept)}
            >
              {dept}
            </Button>
          ))}
          <Button onClick={() => setClickCount((value) => value + 1)}>局部状态计数 +1</Button>
          <Typography.Text type="secondary">当前计数：{clickCount}</Typography.Text>
        </Space>

        <Table
          rowKey="key"
          columns={columns}
          dataSource={data}
          pagination={{ pageSize: 6, showSizeChanger: false }}
          scroll={{ x: 720 }}
        />
      </Card>
    </PageContainer>
  )
}
