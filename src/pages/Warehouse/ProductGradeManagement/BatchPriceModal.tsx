import { DollarOutlined } from '@ant-design/icons'
import { Alert, Form, InputNumber, Modal, Radio, message } from 'antd'
import { useState } from 'react'
import { batchUpdateGradePrices } from '../../../services/productGradeService'

interface Props {
  open: boolean
  selectedCount: number
  productCodes: string[]
  onClose: () => void
  onSuccess: () => void
}

export default function BatchPriceModal({ open, selectedCount, productCodes, onClose, onSuccess }: Props) {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)

  const handleOk = async () => {
    try {
      const values = await form.validateFields()
      if (values.importPrice == null && values.oemPrice == null) {
        message.warning('至少需要填写一个价格')
        return
      }
      setLoading(true)
      const result = await batchUpdateGradePrices({
        productCodes,
        targetDatabase: values.targetDatabase,
        importPrice: values.importPrice ?? undefined,
        oemPrice: values.oemPrice ?? undefined,
      })
      if (result.success) {
        message.success(result.message || `成功更新 ${result.affectedCount} 条记录`)
        onSuccess()
        onClose()
      } else {
        message.error(result.message || '批量修改价格失败')
      }
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) return
      message.error('批量修改价格失败')
    } finally {
      setLoading(false)
    }
  }

  const handleCancel = () => {
    form.resetFields()
    onClose()
  }

  return (
    <Modal
      title="批量修改价格"
      open={open}
      onCancel={handleCancel}
      confirmLoading={loading}
      onOk={() => void handleOk()}
      okText="确认修改"
      cancelText="取消"
      width={520}
    >
      <Alert
        type="info"
        showIcon
        message={`已选 ${selectedCount} 个商品`}
        style={{ marginBottom: 16 }}
      />

      <Form
        form={form}
        layout="vertical"
        initialValues={{ targetDatabase: 'HBweb' }}
      >
        <Form.Item
          name="targetDatabase"
          label="目标数据库"
          rules={[{ required: true, message: '请选择目标数据库' }]}
        >
          <Radio.Group>
            <Radio value="HBweb">
              HBweb（DomesticProduct / WarehouseProduct / Product / StoreRetailPrice）
            </Radio>
            <Radio value="HQ">
              HQ（DIC_商品信息字典表 / DIC_商品零售价表 / CBP_DIC_商品库存表）
            </Radio>
          </Radio.Group>
        </Form.Item>

        <Form.Item
          name="importPrice"
          label="进货价格（Import Price）"
          extra="留空表示不修改"
        >
          <InputNumber
            min={0}
            precision={2}
            placeholder="输入进货价格"
            prefix={<DollarOutlined />}
            style={{ width: '100%' }}
          />
        </Form.Item>

        <Form.Item
          name="oemPrice"
          label="零售价格（Retail Price）"
          extra="留空表示不修改"
        >
          <InputNumber
            min={0}
            precision={2}
            placeholder="输入零售价格"
            prefix={<DollarOutlined />}
            style={{ width: '100%' }}
          />
        </Form.Item>
      </Form>

      <Alert
        type="warning"
        showIcon
        message="请确认目标数据库和价格无误后再提交，修改将直接影响数据库中的价格记录。"
      />
    </Modal>
  )
}
