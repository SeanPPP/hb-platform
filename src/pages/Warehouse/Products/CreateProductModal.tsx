import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  Form,
  Input,
  InputNumber,
  message,
  Modal,
  Radio,
  Row,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  TreeSelect,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useEffect, useMemo, useState } from 'react'
import { getCategoryTree, type WarehouseCategoryNode, batchAssignProducts } from '../../../services/warehouseCategoryService'
import {
  createSingleWarehouseProduct,
  type CreateSingleMultiCodeDetailInput,
  type CreateSingleSetDetailInput,
  type CreateSingleStorePriceInput,
} from '../../../services/warehouseProductService'
import { getActiveStores, type StoreOption } from '../../../services/storeService'
import type { ProductType, SupplierOption } from '../../../types/domesticProduct'

interface CreateProductModalProps {
  open: boolean
  suppliers: SupplierOption[]
  onCancel: () => void
  onSuccess: () => void
}

interface SetDetailFormRow {
  key: string
  productCode: string
  quantity: number
}

interface MultiCodeFormRow {
  key: string
  code: string
  name?: string
  barcode?: string
  quantity?: number
}

interface RetailPriceFormRow {
  key: string
  storeCode: string
  storeName?: string
  price: number
  cost: number
}

interface CreateProductFormValues {
  productType: ProductType
  itemNumber?: string
  barcode?: string
  chineseName: string
  englishName?: string
  productSpecification?: string
  domesticPrice?: number
  oemPrice: number
  importPrice: number
  volume?: number
  packingQuantity?: number
  middlePackQuantity?: number
  packingSize?: string
  material?: string
  remarks?: string
  categoryGuid?: string
  supplierCode: string
  isActive: boolean
  imageUrl?: string
  setProductType?: 'combination' | 'fixed' | 'variable'
}

interface CategoryTreeOption {
  title: string
  value: string
  key: string
  children?: CategoryTreeOption[]
}

const defaultSetRows = (): SetDetailFormRow[] => [{ key: '1', productCode: '', quantity: 1 }]
const defaultMultiCodeRows = (): MultiCodeFormRow[] => [{ key: '1', code: '' }]
const defaultRetailPriceRows = (): RetailPriceFormRow[] => [{ key: '1', storeCode: '', price: 0, cost: 0 }]

function createTreeData(nodes: WarehouseCategoryNode[]): CategoryTreeOption[] {
  return nodes.map((node) => ({
    title: node.categoryName || node.chineseName || node.categoryGUID,
    value: node.categoryGUID,
    key: node.categoryGUID,
    children: Array.isArray(node.children) ? createTreeData(node.children) : undefined,
  }))
}

function mapSetType(value?: 'combination' | 'fixed' | 'variable'): 1 | 2 | 3 | undefined {
  if (value === 'fixed') {
    return 2
  }

  if (value === 'variable') {
    return 3
  }

  if (value === 'combination') {
    return 1
  }

  return undefined
}

export default function CreateProductModal({ open, suppliers, onCancel, onSuccess }: CreateProductModalProps) {
  const [form] = Form.useForm<CreateProductFormValues>()
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [categoryTree, setCategoryTree] = useState<WarehouseCategoryNode[]>([])
  const [stores, setStores] = useState<StoreOption[]>([])
  const [autoGenerateItemNumber, setAutoGenerateItemNumber] = useState(true)
  const [autoGenerateBarcode, setAutoGenerateBarcode] = useState(true)
  const [setDetails, setSetDetails] = useState<SetDetailFormRow[]>(defaultSetRows)
  const [multiCodeDetails, setMultiCodeDetails] = useState<MultiCodeFormRow[]>(defaultMultiCodeRows)
  const [retailPrices, setRetailPrices] = useState<RetailPriceFormRow[]>(defaultRetailPriceRows)

  const productType = Form.useWatch('productType', form) ?? 0
  const oemPrice = Form.useWatch('oemPrice', form) ?? 0
  const importPrice = Form.useWatch('importPrice', form) ?? 0
  const supplierCode = Form.useWatch('supplierCode', form)

  useEffect(() => {
    if (!open) {
      return
    }

    let cancelled = false

    const loadOptions = async () => {
      setLoading(true)
      try {
        const [categories, storeOptions] = await Promise.all([
          getCategoryTree(),
          getActiveStores(),
        ])

        if (!cancelled) {
          setCategoryTree(categories)
          setStores(storeOptions)
        }
      } catch (error) {
        console.error(error)
        message.error(error instanceof Error ? error.message : '加载新建商品所需数据失败')
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    form.setFieldsValue({
      productType: 0,
      oemPrice: 0,
      importPrice: 0,
      isActive: true,
      setProductType: 'combination',
    })
    setAutoGenerateItemNumber(true)
    setAutoGenerateBarcode(true)
    setSetDetails(defaultSetRows())
    setMultiCodeDetails(defaultMultiCodeRows())
    setRetailPrices(defaultRetailPriceRows())
    void loadOptions()

    return () => {
      cancelled = true
    }
  }, [form, open])

  useEffect(() => {
    if (!open) {
      return
    }

    setRetailPrices((current) =>
      current.map((item) => ({
        ...item,
        price: oemPrice || 0,
        cost: importPrice || 0,
      })),
    )
  }, [importPrice, oemPrice, open])

  const supplierOptions = useMemo(
    () =>
      suppliers.map((item) => ({
        value: item.code,
        label: `${item.code} - ${item.name}`,
      })),
    [suppliers],
  )

  const setColumns = useMemo<ColumnsType<SetDetailFormRow>>(
    () => [
      {
        title: '商品编码',
        dataIndex: 'productCode',
        render: (_, record) => (
          <Input
            value={record.productCode}
            placeholder="请输入商品编码"
            onChange={(event) => {
              const value = event.target.value
              setSetDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, productCode: value } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '数量',
        dataIndex: 'quantity',
        width: 120,
        render: (_, record) => (
          <InputNumber
            min={1}
            precision={0}
            value={record.quantity}
            style={{ width: '100%' }}
            onChange={(value) => {
              setSetDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, quantity: value ?? 1 } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '操作',
        key: 'action',
        width: 80,
        render: (_, record) => (
          <Button
            type="link"
            danger
            icon={<DeleteOutlined />}
            onClick={() => {
              setSetDetails((current) => {
                if (current.length === 1) {
                  message.warning('至少保留一条套装明细')
                  return current
                }
                return current.filter((item) => item.key !== record.key)
              })
            }}
          >
            删除
          </Button>
        ),
      },
    ],
    [],
  )

  const multiCodeColumns = useMemo<ColumnsType<MultiCodeFormRow>>(
    () => [
      {
        title: '编码',
        dataIndex: 'code',
        width: 140,
        render: (_, record) => (
          <Input
            value={record.code}
            placeholder="请输入编码"
            onChange={(event) => {
              const value = event.target.value
              setMultiCodeDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, code: value } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '名称',
        dataIndex: 'name',
        width: 160,
        render: (_, record) => (
          <Input
            value={record.name}
            placeholder="请输入名称"
            onChange={(event) => {
              const value = event.target.value
              setMultiCodeDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, name: value } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '条码',
        dataIndex: 'barcode',
        width: 180,
        render: (_, record) => (
          <Input
            value={record.barcode}
            placeholder="可留空自动生成"
            onChange={(event) => {
              const value = event.target.value
              setMultiCodeDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, barcode: value } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '数量',
        dataIndex: 'quantity',
        width: 120,
        render: (_, record) => (
          <InputNumber
            min={1}
            precision={0}
            value={record.quantity}
            style={{ width: '100%' }}
            onChange={(value) => {
              setMultiCodeDetails((current) =>
                current.map((item) => (item.key === record.key ? { ...item, quantity: value ?? 1 } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '操作',
        key: 'action',
        width: 80,
        render: (_, record) => (
          <Button
            type="link"
            danger
            icon={<DeleteOutlined />}
            onClick={() => {
              setMultiCodeDetails((current) => {
                if (current.length === 1) {
                  message.warning('至少保留一条多码明细')
                  return current
                }
                return current.filter((item) => item.key !== record.key)
              })
            }}
          >
            删除
          </Button>
        ),
      },
    ],
    [],
  )

  const retailPriceColumns = useMemo<ColumnsType<RetailPriceFormRow>>(
    () => [
      {
        title: '分店',
        dataIndex: 'storeCode',
        width: 220,
        render: (_, record) => (
          <Select
            value={record.storeCode || undefined}
            placeholder="请选择分店"
            showSearch
            optionFilterProp="label"
            options={stores}
            onChange={(value, option) => {
              setRetailPrices((current) =>
                current.map((item) =>
                  item.key === record.key
                    ? {
                        ...item,
                        storeCode: value,
                        storeName: typeof option === 'object' && option && 'label' in option ? String(option.label) : undefined,
                      }
                    : item,
                ),
              )
            }}
          />
        ),
      },
      {
        title: '贴牌价格',
        dataIndex: 'price',
        width: 140,
        render: (_, record) => (
          <InputNumber
            min={0}
            precision={2}
            value={record.price}
            style={{ width: '100%' }}
            onChange={(value) => {
              setRetailPrices((current) =>
                current.map((item) => (item.key === record.key ? { ...item, price: value ?? 0 } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '成本价格',
        dataIndex: 'cost',
        width: 140,
        render: (_, record) => (
          <InputNumber
            min={0}
            precision={2}
            value={record.cost}
            style={{ width: '100%' }}
            onChange={(value) => {
              setRetailPrices((current) =>
                current.map((item) => (item.key === record.key ? { ...item, cost: value ?? 0 } : item)),
              )
            }}
          />
        ),
      },
      {
        title: '操作',
        key: 'action',
        width: 80,
        render: (_, record) => (
          <Button
            type="link"
            danger
            icon={<DeleteOutlined />}
            onClick={() => {
              setRetailPrices((current) => {
                if (current.length === 1) {
                  message.warning('至少保留一条分店价格')
                  return current
                }
                return current.filter((item) => item.key !== record.key)
              })
            }}
          >
            删除
          </Button>
        ),
      },
    ],
    [stores],
  )

  const handleClose = () => {
    if (submitting) {
      return
    }

    form.resetFields()
    setAutoGenerateItemNumber(true)
    setAutoGenerateBarcode(true)
    setSetDetails(defaultSetRows())
    setMultiCodeDetails(defaultMultiCodeRows())
    setRetailPrices(defaultRetailPriceRows())
    onCancel()
  }

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()

      const normalizedSetItems = setDetails
        .map((item) => ({
          ...item,
          productCode: item.productCode.trim(),
        }))
        .filter((item) => item.productCode)

      if (values.productType === 1 && !normalizedSetItems.length) {
        message.error('请至少添加一条有效的套装明细')
        return
      }

      const normalizedMultiCodeItems = multiCodeDetails
        .map((item) => ({
          ...item,
          code: item.code.trim(),
          barcode: item.barcode?.trim(),
        }))
        .filter((item) => item.code || item.barcode)

      if (values.productType === 2 && !normalizedMultiCodeItems.length) {
        message.error('请至少添加一条有效的多码明细')
        return
      }

      const normalizedStorePrices = retailPrices.filter((item) => item.storeCode)

      const setItems: CreateSingleSetDetailInput[] | undefined =
        values.productType === 1
          ? normalizedSetItems.map((item) => ({
              productCode: item.productCode,
              quantity: item.quantity || 1,
            }))
          : undefined

      const multiCodeItems: CreateSingleMultiCodeDetailInput[] | undefined =
        values.productType === 2
          ? normalizedMultiCodeItems.map((item) => ({
              barcode: item.barcode || item.code || undefined,
              purchasePrice: values.importPrice,
              retailPrice: values.oemPrice,
              isActive: true,
              autoPricing: false,
              isSpecialProduct: false,
            }))
          : undefined

      const storePrices: CreateSingleStorePriceInput[] | undefined = normalizedStorePrices.length
        ? normalizedStorePrices.map((item) => ({
            storeCode: item.storeCode,
            purchasePrice: item.cost,
            retailPrice: item.price,
            discountRate: 0,
            autoPricing: false,
            isSpecialProduct: false,
            isActive: true,
          }))
        : undefined

      setSubmitting(true)

      const result = await createSingleWarehouseProduct({
        productType: values.productType,
        itemNumber: autoGenerateItemNumber ? undefined : values.itemNumber?.trim() || undefined,
        barcode: autoGenerateBarcode ? undefined : values.barcode?.trim() || undefined,
        chineseName: values.chineseName.trim(),
        englishName: values.englishName?.trim() || undefined,
        productSpecification: values.productSpecification?.trim() || undefined,
        domesticPrice: values.domesticPrice,
        oemPrice: values.oemPrice,
        importPrice: values.importPrice,
        volume: values.volume,
        packingQuantity: values.packingQuantity,
        middlePackQuantity: values.middlePackQuantity,
        packingSize: values.packingSize?.trim() || undefined,
        material: values.material?.trim() || undefined,
        remarks: values.remarks?.trim() || undefined,
        categoryGuid: values.categoryGuid,
        supplierCode: values.supplierCode,
        isActive: values.isActive,
        imageUrl: values.imageUrl?.trim() || undefined,
        setType: mapSetType(values.setProductType),
        setItems,
        multiCodeItems,
        storePrices,
      })

      if (!result.success || !result.productCode) {
        message.error(result.message || '创建商品失败')
        return
      }

      if (values.categoryGuid) {
        await batchAssignProducts(values.categoryGuid, [result.productCode])
      }

      if (result.warnings?.length) {
        message.warning(result.warnings.join('；'))
      }

      message.success(result.message || '创建商品成功')
      handleClose()
      onSuccess()
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : '创建商品失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      title="新建商品"
      open={open}
      width={980}
      destroyOnClose
      okText="保存"
      cancelText="取消"
      confirmLoading={submitting}
      onCancel={handleClose}
      onOk={() => void handleSubmit()}
    >
      <Spin spinning={loading}>
        <Form form={form} layout="vertical" preserve={false}>
          <Card title="商品类型" size="small" style={{ marginBottom: 16 }}>
            <Form.Item name="productType" initialValue={0}>
              <Radio.Group>
                <Radio value={0}>普通商品</Radio>
                <Radio value={1}>套装商品</Radio>
                <Radio value={2}>多码商品</Radio>
              </Radio.Group>
            </Form.Item>
          </Card>

          <Card title="基础信息" size="small" style={{ marginBottom: 16 }}>
            <Row gutter={16}>
              <Col span={12}>
                <Form.Item label="货号生成方式">
                  <Radio.Group
                    value={autoGenerateItemNumber ? 'auto' : 'manual'}
                    onChange={(event) => {
                      const auto = event.target.value === 'auto'
                      setAutoGenerateItemNumber(auto)
                      if (auto) {
                        form.setFieldValue('itemNumber', undefined)
                      }
                    }}
                  >
                    <Radio value="auto">自动生成</Radio>
                    <Radio value="manual">手动输入</Radio>
                  </Radio.Group>
                </Form.Item>
                {!autoGenerateItemNumber ? (
                  <Form.Item
                    name="itemNumber"
                    label="货号"
                    rules={[{ required: true, message: '请输入货号' }]}
                  >
                    <Input placeholder="请输入货号" />
                  </Form.Item>
                ) : null}
              </Col>
              <Col span={12}>
                <Form.Item label="条码生成方式">
                  <Radio.Group
                    value={autoGenerateBarcode ? 'auto' : 'manual'}
                    onChange={(event) => {
                      const auto = event.target.value === 'auto'
                      setAutoGenerateBarcode(auto)
                      if (auto) {
                        form.setFieldValue('barcode', undefined)
                      }
                    }}
                  >
                    <Radio value="auto">自动生成</Radio>
                    <Radio value="manual">手动输入</Radio>
                  </Radio.Group>
                </Form.Item>
                {!autoGenerateBarcode ? (
                  <Form.Item name="barcode" label="条码">
                    <Input placeholder="请输入条码" />
                  </Form.Item>
                ) : null}
              </Col>
              <Col span={12}>
                <Form.Item
                  name="chineseName"
                  label="中文名称"
                  rules={[{ required: true, message: '请输入中文名称' }]}
                >
                  <Input placeholder="请输入中文名称" />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item name="englishName" label="英文名称">
                  <Input placeholder="请输入英文名称" />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="domesticPrice" label="国内价格">
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item
                  name="oemPrice"
                  label="贴牌价格"
                  rules={[{ required: true, message: '请输入贴牌价格' }]}
                >
                  <InputNumber min={0.01} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item
                  name="importPrice"
                  label="进口价格"
                  rules={[{ required: true, message: '请输入进口价格' }]}
                >
                  <InputNumber min={0.01} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="volume" label="体积">
                  <InputNumber min={0} precision={3} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="packingQuantity" label="装箱数">
                  <InputNumber min={0} precision={0} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="middlePackQuantity" label="中包数量">
                  <InputNumber min={0} precision={0} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="productSpecification" label="规格">
                  <Input placeholder="请输入规格" />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="packingSize" label="包装尺寸">
                  <Input placeholder="请输入包装尺寸" />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item name="material" label="材质">
                  <Input placeholder="请输入材质" />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item name="categoryGuid" label="分类">
                  <TreeSelect
                    allowClear
                    placeholder="请选择分类"
                    treeData={createTreeData(categoryTree)}
                    treeDefaultExpandAll
                  />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item name="imageUrl" label="图片地址">
                  <Input placeholder="请输入图片 URL" />
                </Form.Item>
              </Col>
              <Col span={24}>
                <Form.Item name="remarks" label="备注">
                  <Input.TextArea rows={3} placeholder="请输入备注" />
                </Form.Item>
              </Col>
            </Row>
          </Card>

          <Card title="供应商信息" size="small" style={{ marginBottom: 16 }}>
            <Row gutter={16}>
              <Col span={12}>
                <Form.Item
                  name="supplierCode"
                  label="国内供应商"
                  rules={[{ required: true, message: '请选择国内供应商' }]}
                >
                  <Select
                    placeholder="请选择国内供应商"
                    showSearch
                    optionFilterProp="label"
                    options={supplierOptions}
                  />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item name="isActive" label="是否上架" valuePropName="checked" initialValue>
                  <Switch />
                </Form.Item>
              </Col>
            </Row>
            {autoGenerateItemNumber && supplierCode ? (
              <Alert message={`货号将基于供应商 ${supplierCode} 自动生成`} type="info" showIcon />
            ) : null}
          </Card>

          {productType === 1 ? (
            <Card title="套装配置" size="small" style={{ marginBottom: 16 }}>
              <Form.Item name="setProductType" label="套装类型" initialValue="combination">
                <Select
                  options={[
                    { value: 'combination', label: '组合套装' },
                    { value: 'fixed', label: '固定套装' },
                    { value: 'variable', label: '变量套装' },
                  ]}
                />
              </Form.Item>
              <Table rowKey="key" dataSource={setDetails} columns={setColumns} pagination={false} size="small" />
              <Button
                block
                type="dashed"
                icon={<PlusOutlined />}
                style={{ marginTop: 8 }}
                onClick={() => {
                  setSetDetails((current) => [
                    ...current,
                    {
                      key: `set_${Date.now()}_${Math.random()}`,
                      productCode: '',
                      quantity: 1,
                    },
                  ])
                }}
              >
                添加明细
              </Button>
            </Card>
          ) : null}

          {productType === 2 ? (
            <Card title="多码配置" size="small" style={{ marginBottom: 16 }}>
              <Table rowKey="key" dataSource={multiCodeDetails} columns={multiCodeColumns} pagination={false} size="small" />
              <Button
                block
                type="dashed"
                icon={<PlusOutlined />}
                style={{ marginTop: 8 }}
                onClick={() => {
                  setMultiCodeDetails((current) => [
                    ...current,
                    {
                      key: `multi_${Date.now()}_${Math.random()}`,
                      code: '',
                    },
                  ])
                }}
              >
                添加明细
              </Button>
            </Card>
          ) : null}

          <Card title="分店零售价格" size="small">
            <Table rowKey="key" dataSource={retailPrices} columns={retailPriceColumns} pagination={false} size="small" />
            <Space direction="vertical" style={{ width: '100%', marginTop: 8 }}>
              <Button
                block
                type="dashed"
                icon={<PlusOutlined />}
                onClick={() => {
                  setRetailPrices((current) => [
                    ...current,
                    {
                      key: `store_${Date.now()}_${Math.random()}`,
                      storeCode: '',
                      price: oemPrice || 0,
                      cost: importPrice || 0,
                    },
                  ])
                }}
              >
                添加价格
              </Button>
              <Button
                block
                type="primary"
                icon={<PlusOutlined />}
                onClick={() => {
                  if (!stores.length) {
                    message.warning('暂无可用分店')
                    return
                  }

                  setRetailPrices(
                    stores.map((store, index) => ({
                      key: `store_all_${store.value}_${index}`,
                      storeCode: store.value,
                      storeName: store.label,
                      price: oemPrice || 0,
                      cost: importPrice || 0,
                    })),
                  )
                }}
              >
                添加所有分店
              </Button>
            </Space>
          </Card>
        </Form>
      </Spin>
    </Modal>
  )
}
