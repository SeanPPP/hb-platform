import { Button, Card, Input, List, Space, Typography } from 'antd'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../components/PageContainer'

export default function FormDemoPage() {
  const { t } = useTranslation()
  const [draft, setDraft] = useState(t('formDemo.draftContent', '这里是一个未保存的草稿内容。'))
  const [items, setItems] = useState<string[]>([t('formDemo.menuLink', '已完成菜单联动'), t('formDemo.tabsManage', '已完成 Tabs 管理')])
  const [keyword, setKeyword] = useState('')

  const filteredItems = useMemo(() => {
    return items.filter((item) => item.includes(keyword))
  }, [items, keyword])

  return (
    <PageContainer
      title={t('formDemo.title', '表单演示')}
      subtitle={t('formDemo.subtitle', '这个页面用来演示本地 state 的保活效果，比如草稿内容、待办列表和筛选关键字。')}
    >
      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Card title={t('formDemo.draftInput', '草稿输入')}>
          <Input.TextArea rows={5} value={draft} onChange={(event) => setDraft(event.target.value)} />
        </Card>

        <Card title={t('formDemo.todoList', '待办列表')}>
          <Space style={{ marginBottom: 12 }}>
            <Input
              placeholder={t('formDemo.filterTodo', '筛选待办')}
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              style={{ width: 240 }}
            />
            <Button onClick={() => setItems((value) => [...value, `t('formDemo.addItem', '新增事项') ${value.length + 1}`])}>
              t('formDemo.addItem', '新增事项')
            </Button>
          </Space>

          <List
            bordered
            dataSource={filteredItems}
            renderItem={(item) => <List.Item>{item}</List.Item>}
          />
        </Card>

        <Typography.Text type="secondary">
          试试在这个页面输入内容，然后切换到“用户列表”或“角色管理”，再切回来。
        </Typography.Text>
      </Space>
    </PageContainer>
  )
}
