import { Button, Card, Checkbox, Form, Input, Select, Space, Switch, Typography, message } from 'antd'
import { useState } from 'react'
import PageContainer from '../../components/PageContainer'

const permissionOptions = [
  'dashboard:view',
  'user:list',
  'user:create',
  'role:list',
  'role:update',
  'system:config',
]

export default function RoleManagePage() {
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)

  const handleSubmit = async () => {
    setSaving(true)
    await new Promise((resolve) => setTimeout(resolve, 600))
    message.success(`已保存角色：${form.getFieldValue('roleName') || '未命名角色'}`)
    setSaving(false)
  }

  return (
    <PageContainer
      title="角色管理"
      subtitle="这是一个 keepAlive 表单页。切换标签后，输入中的表单数据不会丢失。"
    >
      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={{
            roleName: '运营管理员',
            roleCode: 'ops_admin',
            dataScope: 'dept',
            enabled: true,
            permissions: ['dashboard:view', 'user:list', 'role:list'],
          }}
        >
          <Form.Item label="角色名称" name="roleName" rules={[{ required: true, message: '请输入角色名称' }]}>
            <Input placeholder="请输入角色名称" />
          </Form.Item>
          <Form.Item label="角色编码" name="roleCode" rules={[{ required: true, message: '请输入角色编码' }]}>
            <Input placeholder="请输入角色编码" />
          </Form.Item>
          <Form.Item label="数据权限范围" name="dataScope">
            <Select
              options={[
                { label: '全部数据', value: 'all' },
                { label: '本部门数据', value: 'dept' },
                { label: '仅本人数据', value: 'self' },
              ]}
            />
          </Form.Item>
          <Form.Item label="菜单权限" name="permissions">
            <Checkbox.Group options={permissionOptions} />
          </Form.Item>
          <Form.Item label="角色状态" name="enabled" valuePropName="checked">
            <Switch checkedChildren="启用" unCheckedChildren="停用" />
          </Form.Item>

          <Space>
            <Button type="primary" loading={saving} onClick={handleSubmit}>
              保存
            </Button>
            <Button onClick={() => form.resetFields()}>重置</Button>
          </Space>
        </Form>

        <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
          你可以在这里改动任意字段，再切换到其他 Tab，返回后仍能看到保留的输入状态。
        </Typography.Paragraph>
      </Card>
    </PageContainer>
  )
}
