import {
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  SettingOutlined,
} from '@ant-design/icons'
import { useTranslation } from 'react-i18next'
import {
  Button,
  Col,
  Form,
  Input,
  InputNumber,
  message,
  Modal,
  Row,
  Select,
  Space,
  Steps,
  Table,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import React, { useCallback, useEffect, useMemo, useState } from 'react'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import { createBatch, getActivePrefixes } from '../../../services/domesticProductCreationService'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { CreateBatchRequest } from '../../../types/domesticProductCreation'
import PrefixCodeManageModal from './PrefixCodeManageModal'

interface ProductItem {
  key: string
  productName: string
  productType: ProductCreationType
  privateLabelPrice?: number
  setQuantity?: number
  setPrice?: number
  parentItemNumber?: string
}

interface BatchCreateModalProps {
  visible: boolean
  onClose: () => void
  onSuccess: () => void
}

export default function BatchCreateModal({ visible, onClose, onSuccess }: BatchCreateModalProps) {
  const { t } = useTranslation()
  const [form] = Form.useForm()
  const [currentStep, setCurrentStep] = useState(0)
  const [loading, setLoading] = useState(false)
  const [suppliers, setSuppliers] = useState<Array<{ supplierCode: string; supplierName: string }>>([])
  const [prefixCodes, setPrefixCodes] = useState<Array<{ prefixCode: string; prefixName: string; prefixDescription?: string }>>([])
  const [products, setProducts] = useState<ProductItem[]>([
    { key: `temp_${Date.now()}_0`, productName: '', productType: ProductCreationType.NORMAL },
  ])
  const [submitting, setSubmitting] = useState(false)
  const [manageModalVisible, setManageModalVisible] = useState(false)
  const [selectedSupplier, setSelectedSupplier] = useState<{ code: string; name: string } | null>(null)
  const [batchAddVisible, setBatchAddVisible] = useState(false)
  const [batchAddCount, setBatchAddCount] = useState(5)
  const [batchAddPrice, setBatchAddPrice] = useState<number | null>(null)
  const [batchAddMode, setBatchAddMode] = useState<'append' | 'overwrite'>('overwrite')
  const [batchEditNameVisible, setBatchEditNameVisible] = useState(false)
  const [batchEditNameMode, setBatchEditNameMode] = useState<'replace' | 'prefix' | 'suffix'>('replace')
  const [batchEditNameValue, setBatchEditNameValue] = useState('')
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  const loadSuppliers = async () => {
    try {
      setLoading(true)
      const response = await getActiveChinaSuppliers()
      setLoading(false)
      setSuppliers(response || [])
    } catch {
      setLoading(false)
      message.error(t('productCreation.loadSupplierFailed', '加载供应商失败'))
    }
  }

  const handleReset = useCallback(() => {
    setCurrentStep(0)
    form.resetFields()
    setSuppliers([])
    setPrefixCodes([])
    setSelectedSupplier(null)
    setProducts([{ key: `temp_${Date.now()}_0`, productName: '', productType: ProductCreationType.NORMAL }])
    setManageModalVisible(false)
    setBatchAddVisible(false)
    setBatchAddCount(5)
    setBatchAddPrice(null)
    setBatchAddMode('overwrite')
    setBatchEditNameVisible(false)
    setBatchEditNameMode('replace')
    setBatchEditNameValue('')
    setSelectedRowKeys([])
  }, [form])

  useEffect(() => {
    if (visible) {
      handleReset()
      loadSuppliers()
    }
  }, [visible, handleReset])

  const loadPrefixes = async (supplierCode: string) => {
    try {
      const response = await getActivePrefixes(supplierCode)
      if (response.success) {
        setPrefixCodes(response.data || [])
      }
    } catch {
      // ignore
    }
  }

  const handleAddProduct = useCallback(
    (type: ProductCreationType) => {
      const newProduct: ProductItem = { key: `temp_${Date.now()}_${products.length}`, productName: '', productType: type }
      if (type === ProductCreationType.SET_SUB_ITEM && products.length > 0) {
        for (let i = products.length - 1; i >= 0; i--) {
          if (products[i].productType === ProductCreationType.SET) {
            newProduct.parentItemNumber = `SET_${products[i].key}`
            break
          }
        }
      }
      setProducts([...products, newProduct])
    },
    [products],
  )

  const handleBatchAdd = useCallback(
    (type: ProductCreationType, count: number, price?: number | null, mode: 'append' | 'overwrite' = 'append') => {
      const createProduct = (index: number): ProductItem => ({
        key: `temp_${Date.now()}_${index}`,
        productName: '',
        productType: type,
        privateLabelPrice: price ?? undefined,
      })
      if (mode === 'append') {
        const newProducts: ProductItem[] = []
        for (let i = 0; i < count; i++) {
          const newProduct = createProduct(products.length + i)
          if (type === ProductCreationType.SET_SUB_ITEM && products.length > 0) {
            for (let j = products.length - 1; j >= 0; j--) {
              if (products[j].productType === ProductCreationType.SET) {
                newProduct.parentItemNumber = `SET_${products[j].key}`
                break
              }
            }
          }
          newProducts.push(newProduct)
        }
        setProducts([...products, ...newProducts])
      } else {
        const diff = count - products.length
        if (diff > 0) {
          const newProducts: ProductItem[] = []
          for (let i = 0; i < diff; i++) newProducts.push(createProduct(products.length + i))
          setProducts([...products, ...newProducts])
        } else if (diff < 0) {
          setProducts(products.slice(0, count))
        }
      }
      setBatchAddVisible(false)
    },
    [products],
  )

  const handleDeleteProduct = useCallback(
    (key: string) => {
      if (products.length <= 1) {
        message.warning(t('productCreation.keepAtLeastOneRow', '至少保留一行'))
        return
      }
      setProducts(products.filter((item) => item.key !== key))
    },
    [products],
  )

  const handleUpdateProduct = useCallback(
    (key: string, field: keyof ProductItem, value: unknown) => {
      setProducts(products.map((item) => (item.key === key ? { ...item, [field]: value } : item)))
    },
    [products],
  )

  const handleBatchEditName = useCallback(() => {
    if (!batchEditNameValue.trim()) {
      message.warning(t('productCreation.enterName', '请输入名称'))
      return
    }
    const targetKeys = selectedRowKeys.length > 0 ? selectedRowKeys.map(String) : products.map((p) => p.key)
    setProducts(
      products.map((item) => {
        if (!targetKeys.includes(item.key)) return item
        let newName = item.productName
        switch (batchEditNameMode) {
          case 'replace': newName = batchEditNameValue; break
          case 'prefix': newName = batchEditNameValue + newName; break
          case 'suffix': newName = newName + batchEditNameValue; break
        }
        return { ...item, productName: newName }
      }),
    )
    setBatchEditNameVisible(false)
    setBatchEditNameValue('')
    setBatchEditNameMode('replace')
  }, [products, selectedRowKeys, batchEditNameMode, batchEditNameValue])

  const previewData = useMemo(() => {
    const prefixCode = form.getFieldValue('prefixCode') || ''
    let itemIndex = 1
    return products
      .map((p) => ({ ...p, itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, '0')}` }))
      .sort((a, b) => a.itemNumber.localeCompare(b.itemNumber))
  }, [products, form])

  const handleNext = useCallback(async () => {
    if (currentStep === 0) {
      try {
        await form.validateFields(['supplierCode'])
        setCurrentStep(1)
      } catch { return }
    } else if (currentStep === 1) {
      if (products.length === 0) {
        message.error(t('productCreation.addAtLeastOneProduct', '请至少添加一行商品'))
        return
      }
      setCurrentStep(2)
    }
  }, [currentStep, form, products])

  const handlePrev = useCallback(() => setCurrentStep(currentStep - 1), [currentStep])

  const handleSubmit = async () => {
    const supplierCode = form.getFieldValue('supplierCode')
    if (!supplierCode) { message.error(t('domesticProducts.selectSupplier', '请选择供应商')); return }
    setSubmitting(true)
    try {
      const requestData: CreateBatchRequest = {
        supplierCode,
        prefixCode: form.getFieldValue('prefixCode'),
        prefixName: form.getFieldValue('prefixCode'),
        items: products.map((p) => ({
          productName: p.productName.trim() || undefined,
          productType: p.productType,
          privateLabelPrice: p.privateLabelPrice,
          setQuantity: p.setQuantity,
          setPrice: p.setPrice,
          parentItemNumber: p.parentItemNumber,
        })),
      }
      const response = await createBatch(requestData)
      setSubmitting(false)
      if (response.success) {
        message.success(t('productCreation.createSuccess', '创建成功'))
        onSuccess()
      } else {
        message.error(response.message || t('productCreation.createFailed', '创建失败'))
      }
    } catch {
      setSubmitting(false)
      message.error(t('productCreation.createFailed', '创建失败'))
    }
  }

  const handleClose = useCallback(() => { handleReset(); onClose() }, [handleReset, onClose])

  const handleSupplierChange = useCallback(
    (supplierCode: string) => {
      form.setFieldValue('prefixCode', undefined)
      setPrefixCodes([])
      const supplier = suppliers.find((s) => s.supplierCode === supplierCode)
      setSelectedSupplier(supplier ? { code: supplier.supplierCode, name: supplier.supplierName } : null)
      if (supplierCode) loadPrefixes(supplierCode)
    },
    [form, suppliers],
  )

  const productColumns: ColumnsType<ProductItem> = [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => index + 1,
    },
    {
      title: t('productCreation.type', '类型'),
      dataIndex: 'productType',
      key: 'productType',
      width: 120,
      render: (type: ProductCreationType) => {
        const typeMap: Record<ProductCreationType, { text: string; color: string }> = {
          [ProductCreationType.NORMAL]: { text: t('productCreation.normal', '普通'), color: 'blue' },
          [ProductCreationType.SET]: { text: t('productCreation.set', '套装'), color: 'green' },
          [ProductCreationType.SET_SUB_ITEM]: { text: t('productCreation.setSubItem', '套装子项'), color: 'orange' },
        }
        const config = typeMap[type] || typeMap[ProductCreationType.NORMAL]
        return <span style={{ color: config.color }}>{config.text}</span>
      },
    },
    {
      title: t('domesticProducts.productName', '商品名称'),
      dataIndex: 'productName',
      key: 'productName',
      render: (text, record) => <Input value={text} onChange={(e) => handleUpdateProduct(record.key, 'productName', e.target.value)} placeholder={t('domesticProducts.productName', '商品名称')} />,
    },
    {
      title: t('productCreation.privateLabelPrice', '贴牌价格'),
      dataIndex: 'privateLabelPrice',
      key: 'privateLabelPrice',
      width: 120,
      render: (text, record) => <InputNumber value={text} onChange={(value) => handleUpdateProduct(record.key, 'privateLabelPrice', value)} placeholder={t('productCreation.privateLabelPrice', '贴牌价格')} style={{ width: '100%' }} min={0} precision={2} />,
    },
    {
      title: t('productCreation.setQuantity', '套装数量'),
      dataIndex: 'setQuantity',
      key: 'setQuantity',
      width: 100,
      render: (text, record) =>
        record.productType === ProductCreationType.SET || record.productType === ProductCreationType.SET_SUB_ITEM ? <InputNumber value={text} onChange={(value) => handleUpdateProduct(record.key, 'setQuantity', value)} style={{ width: '100%' }} min={1} /> : '-',
    },
    {
      title: t('productCreation.setPrice', '套装价格'),
      dataIndex: 'setPrice',
      key: 'setPrice',
      width: 120,
      render: (text, record) =>
        record.productType === ProductCreationType.SET || record.productType === ProductCreationType.SET_SUB_ITEM ? <InputNumber value={text} onChange={(value) => handleUpdateProduct(record.key, 'setPrice', value)} style={{ width: '100%' }} min={0} precision={2} /> : '-',
    },
    {
      title: t('common.action', '操作'),
      key: 'actions',
      width: 80,
      render: (_, record) => <Button type="text" danger icon={<DeleteOutlined />} onClick={() => handleDeleteProduct(record.key)} disabled={products.length <= 1} />,
    },
  ]

  const previewColumns: ColumnsType<any> = [
    {
      title: '#',
      key: '_index',
      width: 50,
      align: 'center',
      render: (_, __, index) => index + 1,
    },
    { title: t('productImport.hbProductNoCol', '货号'), dataIndex: 'itemNumber', key: 'itemNumber', width: 150, render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: t('domesticProducts.productName', '商品名称'), dataIndex: 'productName', key: 'productName' },
    {
      title: t('productCreation.type', '类型'),
      dataIndex: 'productType',
      key: 'productType',
      width: 100,
      render: (type: ProductCreationType) => {
        const typeMap: Record<ProductCreationType, string> = { [ProductCreationType.NORMAL]: t('productCreation.normal', '普通'), [ProductCreationType.SET]: t('productCreation.set', '套装'), [ProductCreationType.SET_SUB_ITEM]: t('productCreation.setSubItem', '套装子项') }
        return typeMap[type] || type
      },
    },
    { title: t('productCreation.privateLabelPrice', '贴牌价格'), dataIndex: 'privateLabelPrice', key: 'privateLabelPrice', width: 120, render: (text) => (text ? `$${text}` : '-') },
  ]

  const steps = [{ title: t('productCreation.basicInfo', '基本信息') }, { title: t('productCreation.productDetail', '商品明细') }, { title: t('productCreation.previewConfirm', '预览确认') }]

  return (
    <Modal title={t('productCreation.createBatch', '创建批次')} open={visible} onCancel={handleClose} width={900} footer={null} destroyOnClose>
      <Form form={form} layout="vertical">
        <Steps current={currentStep} items={steps} style={{ marginBottom: 24 }} />

        {currentStep === 0 && (
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="supplierCode" label={t('domesticProducts.supplier', '供应商')} rules={[{ required: true, message: t('domesticProducts.selectSupplier', '请选择供应商') }]}>
                <Select showSearch placeholder={t('domesticProducts.selectSupplier', '请选择供应商')} optionFilterProp="label" loading={loading} onChange={handleSupplierChange} options={suppliers.map((s) => ({ label: `${s.supplierCode} - ${s.supplierName}`, value: s.supplierCode }))} />
              </Form.Item>
            </Col>
            <Col span={12} style={{ position: 'relative' }}>
              <Form.Item name="prefixCode" label={t('productCreation.prefixCode', '前缀码')}>
                <Select placeholder={t('productCreation.selectPrefixCode', '请选择前缀码')} allowClear showSearch optionFilterProp="label" style={{ width: 'calc(100% - 80px)' }} options={prefixCodes.map((p) => ({ label: p.prefixDescription ? `${p.prefixName} - ${p.prefixDescription}` : p.prefixName, value: p.prefixName }))} />
              </Form.Item>
              <Button type="link" size="small" icon={<SettingOutlined />} disabled={!selectedSupplier} onClick={() => setManageModalVisible(true)} style={{ position: 'absolute', right: 0, top: 6 }} />
            </Col>
          </Row>
        )}

        {currentStep === 1 && (
          <div>
            <Space style={{ marginBottom: 16 }}>
              <Button icon={<PlusOutlined />} onClick={() => handleAddProduct(ProductCreationType.NORMAL)}>{t('productCreation.normal', '普通')}</Button>
              <Button icon={<PlusOutlined />} onClick={() => handleAddProduct(ProductCreationType.SET)}>{t('productCreation.set', '套装')}</Button>
              <Button icon={<PlusOutlined />} onClick={() => handleAddProduct(ProductCreationType.SET_SUB_ITEM)}>{t('productCreation.setSubItem', '套装子项')}</Button>
              <Button type="dashed" onClick={() => setBatchAddVisible(true)}>{t('productCreation.batchAdd', '批量添加')}</Button>
              <Button type="dashed" icon={<EditOutlined />} onClick={() => setBatchEditNameVisible(true)}>{t('productCreation.batchName', '批量命名')}</Button>
            </Space>
            <Modal title={t('productCreation.batchAdd', '批量添加')} open={batchAddVisible} onOk={() => { handleBatchAdd(ProductCreationType.NORMAL, batchAddCount, batchAddPrice, batchAddMode); setBatchAddVisible(false) }} onCancel={() => setBatchAddVisible(false)} okText={t('common.confirm', '确定')} cancelText={t('common.cancel', '取消')}>
              <Space direction="vertical" style={{ width: '100%' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <span>{t('productCreation.quantity', '数量')}:</span>
                  <InputNumber min={1} max={100} value={batchAddCount} onChange={(v) => setBatchAddCount(v || 5)} />
                  <span>{t('productCreation.mode', '模式')}:</span>
                  <Select value={batchAddMode} onChange={(v) => setBatchAddMode(v)} style={{ width: 140 }} options={[{ label: t('productCreation.adjustToCount', '调整到指定数量'), value: 'overwrite' }, { label: t('productCreation.appendCount', '追加指定数量'), value: 'append' }]} />
                </div>
                <div>
                  {t('productCreation.uniformPrice', '统一贴牌价格')}:
                  <InputNumber min={0} precision={2} placeholder={t('productCreation.optional', '可选')} style={{ marginLeft: 8, width: 160 }} onChange={(v) => setBatchAddPrice(v)} />
                </div>
              </Space>
            </Modal>
            <Modal title={t('productCreation.batchName', '批量命名')} open={batchEditNameVisible} onOk={handleBatchEditName} onCancel={() => { setBatchEditNameVisible(false); setBatchEditNameValue(''); setBatchEditNameMode('replace') }} okText={t('common.confirm', '确定')} cancelText={t('common.cancel', '取消')}>
              <Space direction="vertical" style={{ width: '100%' }} size="middle">
                <div>
                  {t('productCreation.mode', '模式')}:
                  <Select value={batchEditNameMode} onChange={(v) => setBatchEditNameMode(v)} style={{ marginLeft: 8, width: 160 }} options={[{ label: t('productCreation.replace', '替换'), value: 'replace' }, { label: t('productCreation.addPrefix', '加前缀'), value: 'prefix' }, { label: t('productCreation.addSuffix', '加后缀'), value: 'suffix' }]} />
                </div>
                <div>
                  {t('productCreation.value', '值')}:
                  <Input value={batchEditNameValue} onChange={(e) => setBatchEditNameValue(e.target.value)} style={{ marginLeft: 8, width: 280 }} placeholder={t('productCreation.enterName', '请输入名称')} />
                </div>
                <div style={{ color: '#999', fontSize: 12 }}>
                  {selectedRowKeys.length > 0 ? t('productCreation.applyToSelectedRows', { count: selectedRowKeys.length }) : t('productCreation.applyToAll', '将应用到所有行')}
                </div>
              </Space>
            </Modal>
            <Table columns={productColumns} dataSource={products} rowKey="key" pagination={false} size="small" scroll={{ y: 300 }} rowSelection={{ selectedRowKeys, onChange: (keys) => setSelectedRowKeys(keys) }} />
          </div>
        )}

        {currentStep === 2 && (
          <div>
            <div style={{ marginBottom: 16, padding: '12px 16px', background: '#f5f5f5', borderRadius: 4 }}>
              <Space size="large">
                <span><strong>{t('domesticProducts.supplier', '供应商')}:</strong> {selectedSupplier?.name || form.getFieldValue('supplierCode')}</span>
                <span><strong>{t('productCreation.prefixCode', '前缀码')}:</strong> {form.getFieldValue('prefixCode') || '-'}</span>
                <span><strong>{t('productCreation.productCount', '商品数量')}:</strong> {previewData.length}</span>
              </Space>
            </div>
            <Table columns={previewColumns} dataSource={previewData} rowKey="key" pagination={false} size="small" scroll={{ y: 300 }} />
          </div>
        )}

        <div style={{ marginTop: 24, textAlign: 'right' }}>
          <Space>
            {currentStep > 0 && <Button onClick={handlePrev}>{t('productCreation.prevStep', '上一步')}</Button>}
            {currentStep < 2 && <Button type="primary" onClick={handleNext}>{t('productCreation.nextStep', '下一步')}</Button>}
            {currentStep === 2 && <Button type="primary" loading={submitting} onClick={handleSubmit}>{t('productCreation.confirmCreate', '确认创建')}</Button>}
            <Button onClick={handleClose}>{t('common.cancel', '取消')}</Button>
          </Space>
        </div>

        <PrefixCodeManageModal
          visible={manageModalVisible}
          supplierCode={selectedSupplier?.code || ''}
          supplierName={selectedSupplier?.name || ''}
          onClose={() => setManageModalVisible(false)}
          onSuccess={() => {
            setManageModalVisible(false)
            if (selectedSupplier?.code) loadPrefixes(selectedSupplier.code)
          }}
        />
      </Form>
    </Modal>
  )
}
