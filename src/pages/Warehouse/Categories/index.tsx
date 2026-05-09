import {
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Empty,
  Form,
  Image,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tree,
  Typography,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { DataNode } from 'antd/es/tree'
import { useEffect, useMemo, useState } from 'react'
import PageContainer from '../../../components/PageContainer'
import {
  batchAssignProducts,
  createWarehouseCategory,
  deleteWarehouseCategory,
  getCategoryTree,
  getWarehouseCategoryProducts,
  type SaveWarehouseCategoryPayload,
  type WarehouseCategoryNode,
  type WarehouseCategoryProductItem,
  updateWarehouseCategory,
} from '../../../services/warehouseCategoryService'
import {
  getWarehouseProductsTable,
  type WarehouseProductListItem,
  type WarehouseProductsTableQuery,
} from '../../../services/warehouseProductService'

type FormMode = 'idle' | 'create' | 'edit'

interface WarehouseCategoryFormValues extends SaveWarehouseCategoryPayload {
  isActive: boolean
}

interface ProductFilterValues {
  itemNumber?: string
  supplierCode?: string
  filterCategoryGuid?: string
  targetCategoryGuid?: string
}

const ALL_PRODUCTS_FILTER_KEY = '__ALL_PRODUCTS__'
const DEFAULT_TREE_EXPAND_LEVEL = 2
const IMAGE_FALLBACK = 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs='

function collectExpandedKeysToLevel(nodes: WarehouseCategoryNode[], maxLevel: number, level = 1): string[] {
  if (level > maxLevel) {
    return []
  }

  return nodes.flatMap((node) => [
    node.categoryGUID,
    ...collectExpandedKeysToLevel(node.children || [], maxLevel, level + 1),
  ])
}

function findCategory(nodes: WarehouseCategoryNode[], targetGuid?: string): WarehouseCategoryNode | undefined {
  if (!targetGuid) {
    return undefined
  }

  for (const node of nodes) {
    if (node.categoryGUID === targetGuid) {
      return node
    }

    const matched = findCategory(node.children || [], targetGuid)
    if (matched) {
      return matched
    }
  }

  return undefined
}

function collectDescendantKeys(node?: WarehouseCategoryNode): string[] {
  if (!node) {
    return []
  }

  return (node.children || []).flatMap((child) => [child.categoryGUID, ...collectDescendantKeys(child)])
}

function buildParentOptions(nodes: WarehouseCategoryNode[], level = 0): Array<{ label: string; value: string }> {
  return nodes.flatMap((node) => [
    {
      value: node.categoryGUID,
      label: `${level > 0 ? `${'--'.repeat(level)} ` : ''}${node.categoryName}${node.chineseName ? ` / ${node.chineseName}` : ''}`,
    },
    ...buildParentOptions(node.children || [], level + 1),
  ])
}

function buildTreeData(nodes: WarehouseCategoryNode[]): DataNode[] {
  return nodes.map((node) => ({
    key: node.categoryGUID,
    title: (
      <Space size={6}>
        <Typography.Text>{node.categoryName}</Typography.Text>
        {node.chineseName ? <Typography.Text type="secondary">{node.chineseName}</Typography.Text> : null}
        {node.isActive ? (
          <Tag color="success">启用</Tag>
        ) : (
          <Tag>停用</Tag>
        )}
      </Space>
    ),
    children: buildTreeData(node.children || []),
  }))
}

function mapWarehouseTableItemToCategoryProduct(item: WarehouseProductListItem): WarehouseCategoryProductItem {
  return {
    productCode: item.productCode,
    productBaseName: item.name,
    itemNumber: item.itemNumber,
    domesticSupplierCode: item.domesticSupplierCode,
    domesticSupplierName: item.domesticSupplierName,
    localSupplierCode: item.localSupplierCode,
    localSupplierName: item.localSupplierName,
    productCategoryName: item.categoryName,
    productBarcode: item.barcode,
    productImage: item.productImage,
    domesticPrice: item.domesticPrice,
    volume: item.volume,
    isActive: item.isActive,
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
  }
}

function formatDomesticSupplier(record: WarehouseCategoryProductItem): string {
  return [record.domesticSupplierCode || record.localSupplierCode, record.domesticSupplierName || record.localSupplierName]
    .filter(Boolean)
    .join(' - ')
}

export default function WarehouseCategoriesPage() {
  const [form] = Form.useForm<WarehouseCategoryFormValues>()
  const [productFilterForm] = Form.useForm<ProductFilterValues>()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [productLoading, setProductLoading] = useState(false)
  const [assigning, setAssigning] = useState(false)
  const [categories, setCategories] = useState<WarehouseCategoryNode[]>([])
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])
  const [selectedCategoryGuid, setSelectedCategoryGuid] = useState<string>()
  const [formMode, setFormMode] = useState<FormMode>('idle')
  const [modalOpen, setModalOpen] = useState(false)
  const [productFilterCategoryGuid, setProductFilterCategoryGuid] = useState<string>()
  const [productItemNumber, setProductItemNumber] = useState('')
  const [productSupplierCode, setProductSupplierCode] = useState('')
  const [productPage, setProductPage] = useState(1)
  const [productPageSize, setProductPageSize] = useState(20)
  const [productTotal, setProductTotal] = useState(0)
  const [products, setProducts] = useState<WarehouseCategoryProductItem[]>([])
  const [selectedProductCodes, setSelectedProductCodes] = useState<React.Key[]>([])

  const selectedCategory = useMemo(
    () => findCategory(categories, selectedCategoryGuid),
    [categories, selectedCategoryGuid],
  )

  const treeData = useMemo(() => buildTreeData(categories), [categories])

  const disallowedParentKeys = useMemo(() => {
    if (!selectedCategory || formMode !== 'edit') {
      return new Set<string>()
    }

    return new Set([selectedCategory.categoryGUID, ...collectDescendantKeys(selectedCategory)])
  }, [formMode, selectedCategory])

  const parentOptions = useMemo(
    () => buildParentOptions(categories).filter((item) => !disallowedParentKeys.has(item.value)),
    [categories, disallowedParentKeys],
  )
  const categoryOptions = useMemo(() => buildParentOptions(categories), [categories])

  const loadProducts = async (
    nextCategoryGuid = productFilterCategoryGuid,
    nextPage = productPage,
    nextPageSize = productPageSize,
    nextItemNumber = productItemNumber,
    nextSupplierCode = productSupplierCode,
  ) => {
    setProductLoading(true)
    try {
      if (nextCategoryGuid && nextCategoryGuid !== ALL_PRODUCTS_FILTER_KEY) {
        const result = await getWarehouseCategoryProducts({
          categoryGuid: nextCategoryGuid,
          page: nextPage,
          pageSize: nextPageSize,
          itemNumber: nextItemNumber || undefined,
          supplierCode: nextSupplierCode || undefined,
        })
        setProducts(result.items)
        setProductTotal(result.total)
        setProductPage(result.page)
        setProductPageSize(result.pageSize)
        setProductFilterCategoryGuid(nextCategoryGuid)
        return
      }

      const result = await getWarehouseProductsTable({
        page: nextPage,
        pageSize: nextPageSize,
        searchText: nextItemNumber || undefined,
        supplierCode: nextSupplierCode || undefined,
      } satisfies WarehouseProductsTableQuery)

      setProducts(result.items.map(mapWarehouseTableItemToCategoryProduct))
      setProductTotal(result.total)
      setProductPage(result.page)
      setProductPageSize(result.pageSize)
      setProductFilterCategoryGuid(ALL_PRODUCTS_FILTER_KEY)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载分类商品失败')
    } finally {
      setProductLoading(false)
    }
  }

  const loadTree = async (nextSelectedGuid?: string, autoSelectFirst = false) => {
    setLoading(true)
    try {
      const tree = await getCategoryTree()
      setCategories(tree)
      setExpandedKeys(collectExpandedKeysToLevel(tree, DEFAULT_TREE_EXPAND_LEVEL))

      const targetGuid =
        nextSelectedGuid === undefined
          ? selectedCategoryGuid
          : nextSelectedGuid || undefined

      const targetCategory = findCategory(tree, targetGuid)

      if (targetCategory) {
        setSelectedCategoryGuid(targetCategory.categoryGUID)
        productFilterForm.setFieldValue('targetCategoryGuid', targetCategory.categoryGUID)
        form.setFieldsValue({
          categoryName: targetCategory.categoryName,
          chineseName: targetCategory.chineseName,
          parentGUID: targetCategory.parentGUID,
          isActive: targetCategory.isActive,
          remarks: targetCategory.remarks,
        })
        return
      }

      if (autoSelectFirst && tree[0]) {
        const firstCategory = tree[0]
        setSelectedCategoryGuid(firstCategory.categoryGUID)
        productFilterForm.setFieldValue('targetCategoryGuid', firstCategory.categoryGUID)
        form.setFieldsValue({
          categoryName: firstCategory.categoryName,
          chineseName: firstCategory.chineseName,
          parentGUID: firstCategory.parentGUID,
          isActive: firstCategory.isActive,
          remarks: firstCategory.remarks,
        })
        return
      }

      setSelectedCategoryGuid(undefined)
      setProductFilterCategoryGuid(undefined)
      setFormMode('idle')
      setModalOpen(false)
      form.resetFields()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '加载分类树失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadTree(undefined, true)
  }, [])

  useEffect(() => {
    if (!selectedCategoryGuid) {
      setProducts([])
      setProductTotal(0)
      setSelectedProductCodes([])
      return
    }
  }, [selectedCategoryGuid])

  const handleSelectCategory = (categoryGuid?: string) => {
    if (!categoryGuid) {
      setSelectedCategoryGuid(undefined)
      setFormMode('idle')
      form.resetFields()
      return
    }

    const targetCategory = findCategory(categories, categoryGuid)
    if (!targetCategory) {
      return
    }

    setSelectedCategoryGuid(targetCategory.categoryGUID)
    productFilterForm.setFieldValue('targetCategoryGuid', categoryGuid)
    form.setFieldsValue({
      categoryName: targetCategory.categoryName,
      chineseName: targetCategory.chineseName,
      parentGUID: targetCategory.parentGUID,
      isActive: targetCategory.isActive,
      remarks: targetCategory.remarks,
    })
  }

  const handleCreateRoot = () => {
    setFormMode('create')
    setModalOpen(true)
    form.setFieldsValue({
      categoryName: '',
      chineseName: '',
      parentGUID: undefined,
      isActive: true,
      remarks: '',
    })
  }

  const handleCreateChild = () => {
    if (!selectedCategory) {
      message.warning('请先选择父分类')
      return
    }

    setFormMode('create')
    setModalOpen(true)
    form.setFieldsValue({
      categoryName: '',
      chineseName: '',
      parentGUID: selectedCategory.categoryGUID,
      isActive: true,
      remarks: '',
    })
  }

  const handleEditCategory = () => {
    if (!selectedCategory) {
      message.warning('请先选择要编辑的分类')
      return
    }

    setFormMode('edit')
    setModalOpen(true)
    form.setFieldsValue({
      categoryName: selectedCategory.categoryName,
      chineseName: selectedCategory.chineseName,
      parentGUID: selectedCategory.parentGUID,
      isActive: selectedCategory.isActive,
      remarks: selectedCategory.remarks,
    })
  }

  const handleCloseModal = () => {
    setModalOpen(false)
    if (selectedCategory) {
      setFormMode('edit')
      form.setFieldsValue({
        categoryName: selectedCategory.categoryName,
        chineseName: selectedCategory.chineseName,
        parentGUID: selectedCategory.parentGUID,
        isActive: selectedCategory.isActive,
        remarks: selectedCategory.remarks,
      })
      return
    }

    setFormMode('idle')
    form.resetFields()
  }

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      setSaving(true)

      if (formMode === 'create') {
        const created = await createWarehouseCategory(values)
        message.success('创建分类成功')
        setModalOpen(false)
        await loadTree(created.categoryGUID)
        return
      }

      if (!selectedCategoryGuid) {
        message.warning('请先选择要编辑的分类')
        return
      }

      const updated = await updateWarehouseCategory(selectedCategoryGuid, values)
      message.success('更新分类成功')
      setModalOpen(false)
      await loadTree(updated.categoryGUID)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }

      console.error(error)
      message.error(error instanceof Error ? error.message : '保存分类失败')
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!selectedCategory) {
      return
    }

    try {
      setSaving(true)
      const parentGuid = selectedCategory.parentGUID
      await deleteWarehouseCategory(selectedCategory.categoryGUID)
      message.success('删除分类成功')
      await loadTree(parentGuid || '', false)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '删除分类失败')
    } finally {
      setSaving(false)
    }
  }

  const handleSearchProducts = async () => {
    const values = productFilterForm.getFieldsValue()
    const nextFilterCategoryGuid = values.filterCategoryGuid || ALL_PRODUCTS_FILTER_KEY

    setProductItemNumber(values.itemNumber?.trim() || '')
    setProductSupplierCode(values.supplierCode?.trim() || '')
    setProductPage(1)
    setSelectedProductCodes([])
    await loadProducts(nextFilterCategoryGuid, 1, productPageSize, values.itemNumber?.trim() || '', values.supplierCode?.trim() || '')
  }

  const handleResetProducts = async () => {
    productFilterForm.setFieldsValue({
      itemNumber: '',
      supplierCode: '',
      filterCategoryGuid: undefined,
      targetCategoryGuid: selectedCategoryGuid,
    })
    setProductItemNumber('')
    setProductSupplierCode('')
    setProductFilterCategoryGuid(undefined)
    setProductPage(1)
    setProducts([])
    setProductTotal(0)
    setSelectedProductCodes([])
  }

  const handleProductTableChange = (pagination: TablePaginationConfig) => {
    const nextPage = pagination.current ?? 1
    const nextPageSize = pagination.pageSize ?? productPageSize
    if (productFilterCategoryGuid === undefined) {
      return
    }

    void loadProducts(productFilterCategoryGuid, nextPage, nextPageSize, productItemNumber, productSupplierCode)
  }

  const handleBatchAssign = async () => {
    const targetCategoryGuid = productFilterForm.getFieldValue('targetCategoryGuid') as string | undefined

    if (!targetCategoryGuid) {
      message.warning('请先选择目标分类')
      return
    }

    if (!selectedProductCodes.length) {
      message.warning('请先选择要更新分类的商品')
      return
    }

    try {
      setAssigning(true)
      await batchAssignProducts(targetCategoryGuid, selectedProductCodes.map(String))
      const targetCategory = findCategory(categories, targetCategoryGuid)
      message.success(`已将 ${selectedProductCodes.length} 个商品更新到“${targetCategory?.categoryName || '目标分类'}”`)
      setSelectedProductCodes([])
      await Promise.all([
        loadTree(selectedCategoryGuid),
        productFilterCategoryGuid
          ? loadProducts(productFilterCategoryGuid, productPage, productPageSize, productItemNumber, productSupplierCode)
          : Promise.resolve(),
      ])
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '批量更新商品分类失败')
    } finally {
      setAssigning(false)
    }
  }

  const productColumns: ColumnsType<WarehouseCategoryProductItem> = [
    {
      title: '图片',
      dataIndex: 'productImage',
      width: 88,
      render: (value?: string) => (
        <Image
          src={value}
          alt=""
          width={44}
          height={44}
          style={{ borderRadius: 4, objectFit: 'cover' }}
          fallback={IMAGE_FALLBACK}
          preview={Boolean(value)}
        />
      ),
    },
    {
      title: '货号',
      dataIndex: 'itemNumber',
      width: 160,
      render: (value?: string) => value || '--',
    },
    {
      title: '商品名称',
      dataIndex: 'productBaseName',
      width: 240,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: '国内供应商',
      key: 'domesticSupplier',
      width: 220,
      render: (_value, record) => {
        const supplierText = formatDomesticSupplier(record)
        return supplierText || '--'
      },
    },
    {
      title: '当前分类',
      dataIndex: 'productCategoryName',
      width: 160,
      render: (value?: string) => value || '--',
    },
    {
      title: '状态',
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (value ? <Tag color="success">启用</Tag> : <Tag>停用</Tag>),
    },
  ]

  return (
    <PageContainer
      title="分类管理"
      subtitle="维护仓库分类树，支持新增顶级分类、子分类、修改父类与启停状态。"
    >
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: '360px minmax(0, 1fr)',
          gap: 16,
        }}
      >
        <Card
          title="分类树"
          extra={
            <Space size={8}>
              <Button icon={<ReloadOutlined />} onClick={() => void loadTree()}>
                刷新
              </Button>
              <Button type="primary" icon={<PlusOutlined />} onClick={handleCreateRoot}>
                新增顶级分类
              </Button>
            </Space>
          }
        >
          <Space wrap size={[8, 8]} style={{ marginBottom: 12, width: '100%' }}>
            <Button onClick={handleCreateChild} disabled={!selectedCategory}>
              新增子分类
            </Button>
            <Button icon={<EditOutlined />} onClick={handleEditCategory} disabled={!selectedCategory}>
              编辑分类
            </Button>
            <Popconfirm
              title="确认删除该分类？"
              description="若该分类存在子分类，后端会阻止删除。"
              onConfirm={() => void handleDelete()}
              disabled={!selectedCategory}
            >
              <Button danger icon={<DeleteOutlined />} disabled={!selectedCategory} loading={saving}>
                删除分类
              </Button>
            </Popconfirm>
          </Space>

          <Spin spinning={loading}>
            {categories.length ? (
              <div
                style={{
                  minHeight: 320,
                  maxHeight: 'calc(100vh - 280px)',
                  overflowY: 'auto',
                  overflowX: 'hidden',
                  paddingRight: 4,
                }}
              >
                <Tree
                  blockNode
                  selectedKeys={selectedCategoryGuid ? [selectedCategoryGuid] : []}
                  expandedKeys={expandedKeys}
                  onExpand={(keys) => setExpandedKeys(keys as string[])}
                  onSelect={(keys) => handleSelectCategory(typeof keys[0] === 'string' ? keys[0] : undefined)}
                  treeData={treeData}
                />
              </div>
            ) : (
              <Empty description="暂无分类数据" />
            )}
          </Spin>
        </Card>

        <div style={{ display: 'grid', gap: 16 }}>
          <Card
            title="商品分类管理"
            extra={
              <Space>
                <Button
                  type="primary"
                  disabled={!productFilterForm.getFieldValue('targetCategoryGuid') || !selectedProductCodes.length}
                  loading={assigning}
                  onClick={() => void handleBatchAssign()}
                >
                  批量更新到目标分类
                </Button>
                <Button
                  icon={<ReloadOutlined />}
                  disabled={productFilterCategoryGuid === undefined}
                  onClick={() =>
                    productFilterCategoryGuid !== undefined
                      ? void loadProducts(
                          productFilterCategoryGuid,
                          productPage,
                          productPageSize,
                          productItemNumber,
                          productSupplierCode,
                        )
                      : undefined
                  }
                >
                  刷新
                </Button>
              </Space>
            }
          >
            {!categories.length ? (
              <Empty description="暂无分类数据" />
            ) : (
              <Space direction="vertical" size={16} style={{ width: '100%' }}>
                <Alert
                  type="info"
                  showIcon
                  message="操作步骤：可直接查询全部商品，或先选择筛选分类再查询；勾选商品后选择目标分类，最后执行批量更新。"
                />

                <Form
                  form={productFilterForm}
                  layout="inline"
                  initialValues={{ filterCategoryGuid: undefined, targetCategoryGuid: selectedCategoryGuid }}
                >
                  <Form.Item label="货号" name="itemNumber">
                    <Input allowClear placeholder="请输入货号" style={{ width: 180 }} />
                  </Form.Item>
                  <Form.Item label="供应商代码" name="supplierCode">
                    <Input allowClear placeholder="请输入国内供应商代码" style={{ width: 180 }} />
                  </Form.Item>
                  <Form.Item label="分类" name="filterCategoryGuid">
                    <Select
                      style={{ width: 260 }}
                      options={categoryOptions}
                      placeholder="不选则查询全部商品"
                      showSearch
                      optionFilterProp="label"
                      allowClear
                    />
                  </Form.Item>
                  <Form.Item label="目标分类" name="targetCategoryGuid">
                    <Select
                      style={{ width: 260 }}
                      options={categoryOptions}
                      placeholder="请选择批量更新目标分类"
                      showSearch
                      optionFilterProp="label"
                      allowClear
                    />
                  </Form.Item>
                  <Form.Item>
                    <Space>
                      <Button type="primary" icon={<SearchOutlined />} onClick={() => void handleSearchProducts()}>
                        查询
                      </Button>
                      <Button onClick={() => void handleResetProducts()}>
                        重置
                      </Button>
                    </Space>
                  </Form.Item>
                </Form>

                <Table
                  rowKey="productCode"
                  loading={productLoading}
                  columns={productColumns}
                  dataSource={products}
                  rowSelection={{
                    selectedRowKeys: selectedProductCodes,
                    onChange: setSelectedProductCodes,
                    preserveSelectedRowKeys: true,
                  }}
                  onChange={handleProductTableChange}
                  scroll={{ x: 950 }}
                  pagination={{
                    current: productPage,
                    pageSize: productPageSize,
                    total: productTotal,
                    showSizeChanger: true,
                    showQuickJumper: true,
                  }}
                />
              </Space>
            )}
          </Card>
        </div>
      </div>
      <Modal
        title={formMode === 'create' ? '新建分类' : '编辑分类'}
        open={modalOpen}
        confirmLoading={saving}
        onOk={() => void handleSave()}
        onCancel={handleCloseModal}
        destroyOnClose
        width={720}
      >
        <Space direction="vertical" size={16} style={{ width: '100%' }}>
          {formMode === 'create' ? (
            <Alert
              type="info"
              showIcon
              message={
                selectedCategory && form.getFieldValue('parentGUID') === selectedCategory.categoryGUID
                  ? `正在为“${selectedCategory.categoryName}”新增子分类`
                  : '正在新增顶级分类'
              }
            />
          ) : null}

          <Form
            form={form}
            layout="vertical"
            initialValues={{ isActive: true }}
          >
            <Form.Item
              label="分类名称"
              name="categoryName"
              rules={[
                { required: true, message: '请输入分类名称' },
                { max: 100, message: '分类名称不能超过 100 个字符' },
              ]}
            >
              <Input maxLength={100} placeholder="请输入分类名称" />
            </Form.Item>

            <Form.Item label="中文名称" name="chineseName" rules={[{ max: 100, message: '中文名称不能超过 100 个字符' }]}>
              <Input maxLength={100} placeholder="请输入中文名称" />
            </Form.Item>

            <Form.Item label="父类" name="parentGUID">
              <Select
                allowClear
                showSearch
                placeholder="不选择则为顶级分类"
                options={parentOptions}
                optionFilterProp="label"
              />
            </Form.Item>

            <Form.Item label="状态" name="isActive" valuePropName="checked">
              <Switch checkedChildren="启用" unCheckedChildren="停用" />
            </Form.Item>

            <Form.Item label="备注" name="remarks" rules={[{ max: 500, message: '备注不能超过 500 个字符' }]}>
              <Input.TextArea rows={4} maxLength={500} showCount placeholder="请输入备注" />
            </Form.Item>
          </Form>
        </Space>
      </Modal>
    </PageContainer>
  )
}
