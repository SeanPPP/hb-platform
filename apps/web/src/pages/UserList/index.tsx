import { SearchOutlined } from '@ant-design/icons'
import { Button, Card, Input, Space, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type { TFunction } from 'i18next'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../components/PageContainer'

interface UserRow {
  key: string
  name: string
  dept: string
  status: string
  email: string
}

function getSourceData(t: TFunction): UserRow[] {
  return Array.from({ length: 42 }).map((_, index) => ({
    key: String(index + 1),
    name: t('userList.userN', { count: index + 1 }),
    dept: [t('userList.productDept', '产品部'), t('userList.devDept', '研发部'), t('userList.opsDept', '运营部')][index % 3],
    status: index % 4 === 0 ? t('common.disabled', '停用') : t('common.enabled', '启用'),
    email: `user${index + 1}@demo.com`,
  }))
}

function getColumns(t: TFunction): ColumnsType<UserRow> {
  return [
    { title: t('userList.name', '姓名'), dataIndex: 'name' },
    { title: t('userList.dept', '部门'), dataIndex: 'dept' },
    {
      title: t('common.status', '状态'),
      dataIndex: 'status',
      render: (value: string) =>
        value === t('common.enabled', '启用') ? <Tag color="success">{t('common.enabled', '启用')}</Tag> : <Tag color="default">{t('common.disabled', '停用')}</Tag>,
    },
    { title: t('userList.email', '邮箱'), dataIndex: 'email' },
  ]
}

export default function UserListPage() {
  const { t } = useTranslation()
  const [keyword, setKeyword] = useState('')
  const [selectedDept, setSelectedDept] = useState(t('common.all', '全部'))
  const [clickCount, setClickCount] = useState(0)

  const sourceData = useMemo(() => getSourceData(t), [t])
  const columns = useMemo(() => getColumns(t), [t])

  const data = useMemo(() => {
    return sourceData.filter((item) => {
      const matchKeyword =
        keyword.trim() === '' ||
        item.name.includes(keyword.trim()) ||
        item.email.includes(keyword.trim())
      const matchDept = selectedDept === t('common.all', '全部') || item.dept === selectedDept
      return matchKeyword && matchDept
    })
  }, [keyword, selectedDept, sourceData, t])

  const deptOptions = [t('common.all', '全部'), t('userList.productDept', '产品部'), t('userList.devDept', '研发部'), t('userList.opsDept', '运营部')]

  return (
    <PageContainer
      title={t('userList.title', '用户列表')}
      subtitle={t('userList.subtitle', '试试输入关键字、切换部门、翻页后再切到其他 Tab，回来时页面状态仍会保留。')}
    >
      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Input
            allowClear
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            prefix={<SearchOutlined />}
            placeholder={t('userList.searchPlaceholder', '按姓名或邮箱搜索')}
            style={{ width: 240 }}
          />
          {deptOptions.map((dept) => (
            <Button
              key={dept}
              type={dept === selectedDept ? 'primary' : 'default'}
              onClick={() => setSelectedDept(dept)}
            >
              {dept}
            </Button>
          ))}
          <Button onClick={() => setClickCount((value) => value + 1)}>{t('userList.localCountPlus1', '局部状态计数 +1')}</Button>
          <Typography.Text type="secondary">{t('userList.currentCount', '当前计数')}：{clickCount}</Typography.Text>
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
