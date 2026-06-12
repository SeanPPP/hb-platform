import {
  DeleteOutlined,
  EditOutlined,
  ExpandOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Form,
  Input,
  Popconfirm,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
  message,
} from 'antd'
import type { ColumnsType, TablePaginationConfig } from 'antd/es/table'
import type { SorterResult } from 'antd/es/table/interface'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import PageContainer from '../../../components/PageContainer'
import { getActiveChinaSuppliers } from '../../../services/chinaSupplierService'
import {
  createPrefixCode,
  deletePrefixCode,
  getPrefixCodeList,
  getProductsByPrefix,
  updatePrefixCode,
} from '../../../services/productPrefixCodeService'
import { ProductTypeLabels, type ProductType } from '../../../types/domesticProduct'
import type {
  PrefixCodeProductItem,
  ProductPrefixCodeItem,
  SavePrefixCodePayload,
} from '../../../types/productPrefixCode'

interface SupplierOption {
  label: string
  value: string
}

interface ExpandedProductState {
  products: PrefixCodeProductItem[]
  loading: boolean
  expanded: boolean
  total: number
  page: number
  pageSize: number
}

const getStatusFilterOptions = (t: ReturnType<typeof useTranslation>['t']) => [
  { label: t('productImport.allStatus', '全部状态'), value: 'all' },
  { label: t('common.enable', '启用'), value: 'true' },
  { label: t('chinaSuppliers.disableStatus', '禁用'), value: 'false' },
]

function formatDateTime(value?: string) {
  if (!value) {
    return '--'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }

  return date.toLocaleString('zh-CN', { hour12: false })
}

function formatPrice(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }

  return `¥${value.toFixed(2)}`
}

export default function ProductPrefixCodeManagementPage() {
  const { t } = useTranslation()
  const [createForm] = Form.useForm<SavePrefixCodePayload>()
  const [editForm] = Form.useForm<SavePrefixCodePayload>()
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [supplierLoading, setSupplierLoading] = useState(false)
  const [suppliers, setSuppliers] = useState<SupplierOption[]>([])
  const [data, setData] = useState<ProductPrefixCodeItem[]>([])
  const [expandedProducts, setExpandedProducts] = useState<Record<string, ExpandedProductState>>({})
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [total, setTotal] = useState(0)
  const [search, setSearch] = useState('')
  const [supplierCode, setSupplierCode] = useState<string | undefined>(undefined)
  const [statusFilter, setStatusFilter] = useState<'all' | 'true' | 'false'>('all')
  const [editingKey, setEditingKey] = useState('')
  const [sortField, setSortField] = useState<string | undefined>(undefined)
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc' | undefined>(undefined)
  const statusFilterOptions = getStatusFilterOptions(t)

  const loadSuppliers = async () => {
    setSupplierLoading(true)
    try {
      const result = await getActiveChinaSuppliers()
      setSuppliers(
        result.map((item) => ({
          label: `${item.supplierCode} - ${item.supplierName}`,
          value: item.supplierCode,
        })),
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productCreation.loadSupplierListFailed', '加载供应商列表失败'))
    } finally {
      setSupplierLoading(false)
    }
  }

  const loadList = async (
    nextPage = page,
    nextPageSize = pageSize,
    nextSortField = sortField,
    nextSortDirection = sortDirection,
  ) => {
    setLoading(true)
    try {
      const result = await getPrefixCodeList({
        page: nextPage,
        pageSize: nextPageSize,
        search: search || undefined,
        supplierCode,
        isActive:
          statusFilter === 'all' ? undefined : statusFilter === 'true',
        sortField: nextSortField,
        sortDirection: nextSortDirection,
      })
      setData(result.items)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setSortField(nextSortField)
      setSortDirection(nextSortDirection)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productCreation.loadPrefixListFailed', '加载前缀列表失败'))
    } finally {
      setLoading(false)
    }
  }

  const loadProducts = async (prefixCode: string, nextPage = 1, nextPageSize = 10) => {
    setExpandedProducts((current) => ({
      ...current,
      [prefixCode]: {
        ...current[prefixCode],
        loading: true,
        expanded: true,
        products: current[prefixCode]?.products ?? [],
        total: current[prefixCode]?.total ?? 0,
        page: nextPage,
        pageSize: nextPageSize,
      },
    }))

    try {
      const result = await getProductsByPrefix(prefixCode, {
        page: nextPage,
        pageSize: nextPageSize,
      })
      setExpandedProducts((current) => ({
        ...current,
        [prefixCode]: {
          products: result.items,
          loading: false,
          expanded: true,
          total: result.total,
          page: result.page,
          pageSize: result.pageSize,
        },
      }))
    } catch (error) {
      console.error(error)
      setExpandedProducts((current) => ({
        ...current,
        [prefixCode]: {
          ...current[prefixCode],
          loading: false,
          expanded: true,
          products: current[prefixCode]?.products ?? [],
          total: current[prefixCode]?.total ?? 0,
          page: current[prefixCode]?.page ?? nextPage,
          pageSize: current[prefixCode]?.pageSize ?? nextPageSize,
        },
      }))
      message.error(error instanceof Error ? error.message : t('productCreation.loadRelatedProductsFailed', '加载关联商品失败'))
    }
  }

  useEffect(() => {
    void loadSuppliers()
    void loadList(1, pageSize)
  }, [])

  const handleAdd = async () => {
    try {
      const values = await createForm.validateFields()
      setSubmitting(true)
      await createPrefixCode(values)
      message.success(t('productCreation.addPrefixSuccess', '新增前缀成功'))
      createForm.resetFields()
      createForm.setFieldValue('isActive', true)
      void loadList(1, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productCreation.addPrefixFailed', '新增前缀失败'))
    } finally {
      setSubmitting(false)
    }
  }

  const startEdit = (record: ProductPrefixCodeItem) => {
    setEditingKey(record.prefixCode)
    editForm.setFieldsValue({
      prefixName: record.prefixName,
      prefixDescription: record.prefixDescription,
      isActive: record.isActive,
      sortOrder: record.sortOrder,
    })
  }

  const cancelEdit = () => {
    setEditingKey('')
    editForm.resetFields()
  }

  const handleEdit = async () => {
    if (!editingKey) {
      return
    }

    try {
      const values = await editForm.validateFields()
      setSubmitting(true)
      await updatePrefixCode(editingKey, values)
      message.success(t('productCreation.updatePrefixSuccess', '更新前缀成功'))
      setEditingKey('')
      editForm.resetFields()
      void loadList(page, pageSize)
    } catch (error) {
      if (typeof error === 'object' && error !== null && 'errorFields' in error) {
        return
      }
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productCreation.updatePrefixFailed', '更新前缀失败'))
    } finally {
      setSubmitting(false)
    }
  }

  const handleDelete = async (prefixCode: string) => {
    try {
      await deletePrefixCode(prefixCode)
      message.success(t('productCreation.deletePrefixSuccess', '删除前缀成功'))
      void loadList(page, pageSize)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('productCreation.deletePrefixFailed', '删除前缀失败'))
    }
  }

  const toggleExpand = (prefixCode: string, expanded: boolean) => {
    if (expanded) {
      void loadProducts(prefixCode, 1, 10)
      return
    }

    setExpandedProducts((current) => ({
      ...current,
      [prefixCode]: {
        ...current[prefixCode],
        expanded: false,
        loading: false,
        products: current[prefixCode]?.products ?? [],
        total: current[prefixCode]?.total ?? 0,
        page: current[prefixCode]?.page ?? 1,
        pageSize: current[prefixCode]?.pageSize ?? 10,
      },
    }))
  }

  const productColumns: ColumnsType<PrefixCodeProductItem> = [
    {
      title: t('productImport.hbProductNoCol', '货号'),
      dataIndex: 'hbProductNo',
      width: 140,
      render: (value?: string) => value || '--',
    },
    {
      title: t('domesticProducts.productName', '商品名称'),
      dataIndex: 'productName',
      width: 220,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: t('domesticProducts.barcode', '条码'),
      dataIndex: 'barcode',
      width: 160,
      render: (value?: string) => value || '--',
    },
    {
      title: t('domesticProducts.specification', '规格'),
      dataIndex: 'productSpecification',
      width: 180,
      ellipsis: true,
      render: (value?: string) => value || '--',
    },
    {
      title: t('productCreation.type', '类型'),
      dataIndex: 'productType',
      width: 120,
      render: (value: ProductType) => ProductTypeLabels[value] || '--',
    },
    {
      title: t('domesticProducts.domesticPrice', '国内价'),
      dataIndex: 'domesticPrice',
      width: 100,
      render: (value?: number) => formatPrice(value),
    },
    {
      title: t('domesticProducts.status', '状态'),
      dataIndex: 'isActive',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>{value ? t('common.enable', '启用') : t('common.disable', '停用')}</Tag>
      ),
    },
  ]

  const expandedRowRender = (record: ProductPrefixCodeItem) => {
    const expanded = expandedProducts[record.prefixCode]
    if (!expanded) {
      return null
    }

    return (
      <div style={{ padding: '8px 0' }}>
        <Spin spinning={expanded.loading}>
          <Table
            rowKey="productCode"
            size="small"
            columns={productColumns}
            dataSource={expanded.products}
            pagination={{
              current: expanded.page,
              pageSize: expanded.pageSize,
              total: expanded.total,
              showSizeChanger: true,
              showQuickJumper: true,
              showTotal: (total) => t('common.totalCount', '共 {{count}} 条', { count: total }),
              onChange: (nextPage, nextPageSize) => {
                void loadProducts(record.prefixCode, nextPage, nextPageSize)
              },
            }}
            scroll={{ x: 980 }}
          />
        </Spin>
      </div>
    )
  }

  const columns = useMemo<ColumnsType<ProductPrefixCodeItem>>(
    () => [
      {
        title: '#',
        width: 30,
        render: (_v, _r, index) => (page - 1) * pageSize + index + 1,
      },
      {
        title: t('prefixCode.prefixName', '前缀名'),
        dataIndex: 'prefixName',
        width: 100,
        sorter: true,
        sortOrder:
          sortField === 'prefixName'
            ? sortDirection === 'asc'
              ? 'ascend'
              : 'descend'
            : null,
        render: (value: string, record) => {
          if (editingKey === record.prefixCode) {
            return (
              <Form.Item
                name="prefixName"
                style={{ marginBottom: 0 }}
                rules={[
                  { required: true, message: t('prefixCode.enterPrefixName', '请输入前缀名') },
                  { pattern: /^[A-Za-z0-9]+$/, message: t('prefixCode.prefixNameAlphaNum', '前缀名只能包含字母和数字') },
                ]}
              >
                <Input maxLength={10} />
              </Form.Item>
            )
          }

          return <Tag color="blue">{value}</Tag>
        },
      },
      {
        title: t('prefixCode.prefixDescription', '前缀说明'),
        dataIndex: 'prefixDescription',
        width: 120,
        ellipsis: true,
        render: (value: string | undefined, record) => {
          if (editingKey === record.prefixCode) {
            return (
              <Form.Item name="prefixDescription" style={{ marginBottom: 0 }}>
                <Input />
              </Form.Item>
            )
          }

          return value || '--'
        },
      },
      {
        title: t('domesticProducts.supplier', '供应商'),
        dataIndex: 'supplierName',
        width: 120,
        sorter: true,
        sortOrder:
          sortField === 'supplierName'
            ? sortDirection === 'asc'
              ? 'ascend'
              : 'descend'
            : null,
        render: (_value: string | undefined, record) => record.supplierName || record.supplierCode,
      },
      {
        title: t('chinaSuppliers.supplierCode', '供应商代码'),
        dataIndex: 'supplierCode',
        width: 100,
        sorter: true,
        sortOrder:
          sortField === 'supplierCode'
            ? sortDirection === 'asc'
              ? 'ascend'
              : 'descend'
            : null,
      },
      {
        title: t('domesticProducts.status', '状态'),
        dataIndex: 'isActive',
        width: 100,
        render: (value: boolean, record) => {
          if (editingKey === record.prefixCode) {
            return (
              <Form.Item name="isActive" valuePropName="checked" style={{ marginBottom: 0 }}>
                <Switch checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} />
              </Form.Item>
            )
          }

          return <Switch checked={value} disabled checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} />
        },
      },
      {
        title: t('chinaSuppliers.createdAt', '创建时间'),
        dataIndex: 'createdAt',
        width: 120,
        render: (value?: string) => formatDateTime(value),
      },
      {
        title: t('common.action', '操作'),
        key: 'action',
        width: 150,
        fixed: 'right',
        render: (_, record) => {
          if (editingKey === record.prefixCode) {
            return (
              <Space size={0}>
                <Button type="link" onClick={() => void handleEdit()} loading={submitting}>
                  {t('common.save', '保存')}
                </Button>
                <Button type="link" onClick={cancelEdit}>
                  {t('common.cancel', '取消')}
                </Button>
              </Space>
            )
          }

          return (
            <Space size={0}>
              <Tooltip title={t('productCreation.viewRelatedProducts', '查看关联商品')}>
                <Button
                  type="link"
                  icon={<ExpandOutlined />}
                  onClick={() => toggleExpand(record.prefixCode, !expandedProducts[record.prefixCode]?.expanded)}
                >
                  {t('domesticProducts.product', '商品')}
                </Button>
              </Tooltip>
              <Button type="link" icon={<EditOutlined />} onClick={() => startEdit(record)}>
                {t('common.edit', '编辑')}
              </Button>
              <Popconfirm
                title={t('prefixCode.confirmDeletePrefix', '确认删除该前缀？')}
                description={t('common.irreversible', '删除后不可恢复。')}
                onConfirm={() => void handleDelete(record.prefixCode)}
                disabled={!record.prefixCode}
              >
                <Button type="link" danger icon={<DeleteOutlined />} disabled={!record.prefixCode}>
                  {t('common.delete', '删除')}
                </Button>
              </Popconfirm>
            </Space>
          )
        },
      },
    ],
    [editingKey, expandedProducts, page, pageSize, sortDirection, sortField, submitting],
  )

  return (
    <PageContainer
      title={t('prefixCode.prefixManagement', '前缀管理')}
      subtitle={t('prefixCode.prefixManagementSubtitle', '管理前缀信息并查看该前缀关联的国内商品，保留旧系统页顶新增、行内编辑和展开商品交互。')}
    >
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={createForm}
          layout="inline"
          initialValues={{ isActive: true }}
        >
          <Form.Item
            name="supplierCode"
            rules={[{ required: true, message: t('domesticProducts.selectSupplier', '请选择供应商') }]}
          >
            <Select
              showSearch
              allowClear
              placeholder={t('domesticProducts.selectSupplier', '选择供应商')}
              options={suppliers}
              loading={supplierLoading}
              style={{ width: 240 }}
              optionFilterProp="label"
            />
          </Form.Item>
          <Form.Item
            name="prefixName"
            rules={[
              { required: true, message: t('prefixCode.enterPrefixName', '请输入前缀名') },
              { pattern: /^[A-Za-z0-9]+$/, message: t('prefixCode.prefixNameAlphaNum', '前缀名只能包含字母和数字') },
            ]}
          >
            <Input placeholder={t('prefixCode.prefixName', '前缀名')} maxLength={10} style={{ width: 140 }} />
          </Form.Item>
          <Form.Item name="prefixDescription">
            <Input placeholder={t('prefixCode.prefixDescription', '前缀说明')} style={{ width: 220 }} />
          </Form.Item>
          <Form.Item name="isActive" valuePropName="checked">
            <Switch checkedChildren={t('common.enable', '启用')} unCheckedChildren={t('common.disable', '停用')} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" icon={<PlusOutlined />} loading={submitting} onClick={() => void handleAdd()}>
              {t('prefixCode.addPrefix', '新增前缀')}
            </Button>
          </Form.Item>
        </Form>
      </Card>

      <Card>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            showSearch
            allowClear
            placeholder={t('productCreation.filterSupplier', '筛选供应商')}
            value={supplierCode}
            onChange={(value) => setSupplierCode(value)}
            options={suppliers}
            loading={supplierLoading}
            style={{ width: 220 }}
            optionFilterProp="label"
          />
          <Select
            value={statusFilter}
            onChange={setStatusFilter}
            options={statusFilterOptions}
            style={{ width: 140 }}
          />
          <Input
            placeholder={t('prefixCode.searchPlaceholder', '搜索前缀名 / 说明 / 供应商名称 / 供应商代码 / 店铺号')}
            prefix={<SearchOutlined />}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            allowClear
            style={{ width: 240 }}
          />
          <Button type="primary" onClick={() => void loadList(1, pageSize)}>
            {t('common.query', '查询')}
          </Button>
          <Button icon={<ReloadOutlined />} onClick={() => void loadList(page, pageSize)}>
            {t('common.refresh', '刷新')}
          </Button>
        </Space>

        <Form form={editForm} component={false}>
          <Table
            rowKey="prefixCode"
            loading={loading}
            columns={columns}
            dataSource={data}
            expandable={{
              expandedRowRender,
              expandedRowKeys: Object.keys(expandedProducts).filter((key) => expandedProducts[key]?.expanded),
              onExpand: (expanded, record) => toggleExpand(record.prefixCode, expanded),
            }}
            pagination={{
              current: page,
              pageSize,
              total,
              showSizeChanger: true,
              showQuickJumper: true,
              onChange: (nextPage, nextPageSize) => {
                void loadList(nextPage, nextPageSize)
              },
            }}
            onChange={(
              pagination: TablePaginationConfig,
              _filters,
              sorter: SorterResult<ProductPrefixCodeItem> | SorterResult<ProductPrefixCodeItem>[],
            ) => {
              const nextSorter = Array.isArray(sorter) ? sorter[0] : sorter
              const nextSortField =
                typeof nextSorter?.field === 'string' ? nextSorter.field : undefined
              const nextSortDirection =
                nextSorter?.order === 'ascend'
                  ? 'asc'
                  : nextSorter?.order === 'descend'
                    ? 'desc'
                    : undefined

              void loadList(
                pagination.current ?? 1,
                pagination.pageSize ?? pageSize,
                nextSortField,
                nextSortDirection,
              )
            }}
            scroll={{ x: 1100 }}
          />
        </Form>
      </Card>
    </PageContainer>
  )
}
