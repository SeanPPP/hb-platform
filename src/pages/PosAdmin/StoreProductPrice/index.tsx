import {
  Button,
  Card,
  Checkbox,
  DatePicker,
  Form,
  Image,
  Input,
  InputNumber,
  message,
  Modal,
  Progress,
  Select,
  Space,
  Spin,
  Switch,
  Table,
  Tag,
  Tooltip,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import BarcodePreview from '../../../components/BarcodePreview'
import PageContainer from '../../../components/PageContainer'
import { getActiveLocalSuppliers } from '../../../services/localSupplierService'
import {
  batchUpdateStoreRetailPrices,
  copyStoreData,
  getStoreProductPriceGrid,
  subscribeCopyProgress,
  syncFromHq,
  syncToOtherStores,
} from '../../../services/storeProductPriceService'
import { getActiveStores } from '../../../services/storeService'
import type {
  BatchUpdateStoreRetailPriceDto,
  CopyStoreDataDto,
  CopyProgressDto,
  StoreProductPriceListDto,
  StoreProductPriceQueryDto,
  SyncFromHqRequest,
  SyncToOtherStoresDto,
} from '../../../types/storeProductPrice'
import { copyTextToClipboard } from '../../../utils/clipboard'

type DataType = StoreProductPriceListDto & { key: string }

const productTypeMap: Record<number, { label: string; color: string }> = {
  0: { label: '普通', color: 'default' },
  1: { label: '称重', color: 'blue' },
  2: { label: '多码', color: 'purple' },
}

export default function StoreProductPricePage() {
  const [searchForm] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [data, setData] = useState<DataType[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(50)
  const [sortField, setSortField] = useState<string | undefined>()
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend' | undefined>()
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])

  const [storeOptions, setStoreOptions] = useState<{ label: string; value: string }[]>([])
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([])
  const [selectedStoreCode, setSelectedStoreCode] = useState<string | undefined>()

  const [batchModalOpen, setBatchModalOpen] = useState(false)
  const [batchForm] = Form.useForm()

  const [syncModalOpen, setSyncModalOpen] = useState(false)
  const [syncForm] = Form.useForm()

  const [copyModalOpen, setCopyModalOpen] = useState(false)
  const [copyForm] = Form.useForm()
  const [copyProgress, setCopyProgress] = useState<CopyProgressDto | null>(null)
  const [copying, setCopying] = useState(false)
  const eventSourceRef = useRef<EventSource | null>(null)

  const [hqSyncModalOpen, setHqSyncModalOpen] = useState(false)
  const [hqSyncForm] = Form.useForm()
  const [hqSyncing, setHqSyncing] = useState(false)

  const inFlightRef = useRef(false)

  const loadData = useCallback(async () => {
    if (!selectedStoreCode) {
      setData([])
      setTotal(0)
      return
    }
    if (inFlightRef.current) return
    inFlightRef.current = true
    try {
      setLoading(true)
      const values = searchForm.getFieldsValue()
      const query: StoreProductPriceQueryDto = {
        storeCode: selectedStoreCode,
        search: values.search || undefined,
        localSupplierCode: values.localSupplierCode || undefined,
        pageNumber: page,
        pageSize,
        sortBy: sortField,
        sortOrder: sortOrder === 'ascend' ? 'asc' : sortOrder === 'descend' ? 'desc' : undefined,
      }
      const result = await getStoreProductPriceGrid(query)
      const items = result?.items ?? []
      setTotal(result?.total ?? 0)
      setData(items.map((item, idx) => ({ ...item, key: item.productCode ?? String(idx) })))
      setSelectedRowKeys([])
    } catch {
      message.error('加载商品价格列表失败')
    } finally {
      setLoading(false)
      inFlightRef.current = false
    }
  }, [selectedStoreCode, page, pageSize, sortField, sortOrder, searchForm])

  useEffect(() => {
    ;(async () => {
      try {
        const stores = await getActiveStores()
        setStoreOptions(stores)
      } catch { /* ignore */ }
      try {
        const suppliers = await getActiveLocalSuppliers()
        setSupplierOptions(
          suppliers.map((s) => ({ label: s.name || s.localSupplierCode, value: s.localSupplierCode })),
        )
      } catch { /* ignore */ }
    })()
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  const onTableChange = (pagination: any, _filters: any, sorter: any) => {
    if (sorter?.field) {
      setSortField(String(sorter.field))
      setSortOrder(sorter.order)
    } else {
      setSortField(undefined)
      setSortOrder(undefined)
    }
    setPage(pagination.current)
    setPageSize(pagination.pageSize)
  }

  const handleStoreChange = (val: string | undefined) => {
    setSelectedStoreCode(val)
    setPage(1)
    setSelectedRowKeys([])
  }

  const handleSearch = () => {
    setPage(1)
    loadData()
  }

  const handleReset = () => {
    searchForm.resetFields()
    setPage(1)
    loadData()
  }

  const openBatchModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning('请先选择商品')
      return
    }
    batchForm.resetFields()
    batchForm.setFieldsValue({
      updatePurchasePrice: false,
      updateRetailPrice: false,
      updateAutoPricing: false,
      updateSpecialProduct: false,
      updateDiscountRate: false,
    })
    setBatchModalOpen(true)
  }

  const handleBatchUpdate = async () => {
    try {
      const values = await batchForm.validateFields()
      if (!values.updatePurchasePrice && !values.updateRetailPrice && !values.updateAutoPricing && !values.updateSpecialProduct && !values.updateDiscountRate) {
        message.warning('请至少选择一项要更新的字段')
        return
      }
      const dto: BatchUpdateStoreRetailPriceDto = {
        productCodes: selectedRowKeys as string[],
        storeCode: selectedStoreCode,
      }
      if (values.updatePurchasePrice && values.purchasePrice != null) {
        dto.purchasePrice = Number(values.purchasePrice)
      }
      if (values.updateRetailPrice && values.storeRetailPriceValue != null) {
        dto.storeRetailPriceValue = Number(values.storeRetailPriceValue)
      }
      if (values.updateAutoPricing) {
        dto.isAutoPricing = !!values.isAutoPricing
      }
      if (values.updateSpecialProduct) {
        dto.isSpecialProduct = !!values.isSpecialProduct
      }
      if (values.updateDiscountRate && values.discountRate != null) {
        dto.discountRate = Number(values.discountRate)
      }
      await batchUpdateStoreRetailPrices(dto)
      message.success('批量更新成功')
      setBatchModalOpen(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error('批量更新失败')
    }
  }

  const openSyncModal = () => {
    if (selectedRowKeys.length === 0) {
      message.warning('请先选择商品')
      return
    }
    syncForm.resetFields()
    syncForm.setFieldsValue({
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
      syncDiscountRate: false,
      syncMode: 'Overwrite',
    })
    setSyncModalOpen(true)
  }

  const handleSyncToOtherStores = async () => {
    try {
      const values = await syncForm.validateFields()
      if (!values.targetStoreCodes || values.targetStoreCodes.length === 0) {
        message.warning('请选择目标分店')
        return
      }
      const dto: SyncToOtherStoresDto = {
        productCodes: selectedRowKeys as string[],
        sourceStoreCode: selectedStoreCode!,
        targetStoreCodes: values.targetStoreCodes,
        syncPurchasePrice: !!values.syncPurchasePrice,
        syncRetailPrice: !!values.syncRetailPrice,
        syncIsAutoPricing: !!values.syncIsAutoPricing,
        syncIsSpecialProduct: !!values.syncIsSpecialProduct,
        syncDiscountRate: !!values.syncDiscountRate,
        syncMode: values.syncMode || 'Overwrite',
      }
      const count = await syncToOtherStores(dto)
      message.success(`同步完成，影响 ${count} 条记录`)
      setSyncModalOpen(false)
      setSelectedRowKeys([])
      await loadData()
    } catch {
      message.error('同步失败')
    }
  }

  const openCopyModal = () => {
    copyForm.resetFields()
    copyForm.setFieldsValue({
      mode: 'Overwrite',
      syncPurchasePrice: true,
      syncRetailPrice: true,
      syncIsAutoPricing: false,
      syncIsSpecialProduct: false,
      syncDiscountRate: false,
      syncMultiCode: false,
      syncMultiCodeRetailPrice: false,
    })
    setCopyProgress(null)
    setCopyModalOpen(true)
  }

  const handleCopyStoreData = async () => {
    try {
      const values = await copyForm.validateFields()
      if (!values.sourceStoreCode) {
        message.warning('请选择源分店')
        return
      }
      if (!values.targetStoreCodes || values.targetStoreCodes.length === 0) {
        message.warning('请选择目标分店')
        return
      }
      setCopying(true)
      setCopyProgress(null)
      const dto: CopyStoreDataDto = {
        sourceStoreCode: values.sourceStoreCode,
        targetStoreCodes: values.targetStoreCodes,
        mode: values.mode || 'Overwrite',
        syncPurchasePrice: !!values.syncPurchasePrice,
        syncRetailPrice: !!values.syncRetailPrice,
        syncIsAutoPricing: !!values.syncIsAutoPricing,
        syncIsSpecialProduct: !!values.syncIsSpecialProduct,
        syncDiscountRate: !!values.syncDiscountRate,
        syncMultiCode: !!values.syncMultiCode,
        syncMultiCodeRetailPrice: !!values.syncMultiCodeRetailPrice,
      }
      await copyStoreData(dto)
      message.success('复制任务已提交，正在推送进度...')
      if (eventSourceRef.current) {
        eventSourceRef.current.close()
      }
      const es = subscribeCopyProgress(
        {
          sourceStoreCode: dto.sourceStoreCode,
          targetStoreCodes: dto.targetStoreCodes,
          mode: dto.mode,
          syncMultiCode: dto.syncMultiCode,
        },
        (progress) => {
          setCopyProgress(progress)
        },
        (error) => {
          console.error('SSE error', error)
          setCopying(false)
          message.error('复制进度推送异常')
        },
        () => {
          setCopying(false)
          message.success('复制完成')
          loadData()
        },
      )
      eventSourceRef.current = es
    } catch {
      message.error('复制失败')
      setCopying(false)
    }
  }

  const openHqSyncModal = () => {
    hqSyncForm.resetFields()
    setHqSyncModalOpen(true)
  }

  const handleSyncFromHq = async () => {
    try {
      const values = await hqSyncForm.validateFields()
      setHqSyncing(true)
      const dto: SyncFromHqRequest = {}
      if (values.selectedStoreCodes && values.selectedStoreCodes.length > 0) {
        dto.selectedStoreCodes = values.selectedStoreCodes
      }
      if (values.dateRange && values.dateRange.length === 2) {
        dto.startDate = values.dateRange[0].format('YYYY-MM-DD')
      }
      const result = await syncFromHq(dto)
      setHqSyncModalOpen(false)
      Modal.info({
        title: 'HQ同步结果',
        width: 600,
        content: (
          <div>
            <p>新增：{result.addedCount} 条</p>
            <p>更新：{result.updatedCount} 条</p>
            <p>总处理：{result.totalProcessed} 条</p>
            <p>耗时：{(result.durationMs / 1000).toFixed(2)} 秒</p>
            {result.errors && result.errors.length > 0 && (
              <div>
                <p style={{ color: 'red' }}>错误信息：</p>
                <ul>
                  {result.errors.map((err, idx) => (
                    <li key={idx} style={{ color: 'red' }}>{err}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ),
      })
      await loadData()
    } catch {
      message.error('从HQ同步失败')
    } finally {
      setHqSyncing(false)
    }
  }

  const columns: ColumnsType<DataType> = useMemo(() => [
    {
      title: '商品图片',
      dataIndex: 'productImage',
      key: 'productImage',
      width: 80,
      render: (url: string) =>
        url ? (
          <Image src={url} width={48} height={48} style={{ objectFit: 'cover', borderRadius: 4 }} />
        ) : (
          <div style={{ width: 48, height: 48, background: '#f5f5f5', borderRadius: 4, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#ccc', fontSize: 12 }}>
            无图
          </div>
        ),
    },
    {
      title: '商品代码',
      dataIndex: 'productCode',
      key: 'productCode',
      width: 130,
      sorter: true,
      render: (v: string) => (
        <Tooltip title="点击复制">
          <span style={{ cursor: 'pointer' }} onClick={() => void copyTextToClipboard(v)}>
            {v}
          </span>
        </Tooltip>
      ),
    },
    {
      title: '商品名称',
      dataIndex: 'productName',
      key: 'productName',
      width: 180,
      ellipsis: { showTitle: false },
      sorter: true,
    },
    {
      title: '货号',
      dataIndex: 'itemNumber',
      key: 'itemNumber',
      width: 100,
      sorter: true,
    },
    {
      title: '条码',
      dataIndex: 'barcode',
      key: 'barcode',
      width: 160,
      render: (v: string) => <BarcodePreview value={v} compactCopy textMaxWidth={100} />,
    },
    {
      title: '供应商',
      dataIndex: 'localSupplierName',
      key: 'localSupplierName',
      width: 120,
      ellipsis: { showTitle: false },
    },
    {
      title: '商品类型',
      dataIndex: 'productType',
      key: 'productType',
      width: 90,
      render: (v: number) => {
        const info = productTypeMap[v] || { label: String(v), color: 'default' }
        return <Tag color={info.color}>{info.label}</Tag>
      },
    },
    {
      title: '中包数量',
      dataIndex: 'middlePackageQuantity',
      key: 'middlePackageQuantity',
      width: 100,
      sorter: true,
    },
    {
      title: '采购价',
      dataIndex: 'storePurchasePrice',
      key: 'storePurchasePrice',
      width: 100,
      sorter: true,
      render: (v: number) => (v != null ? v.toFixed(2) : '-'),
    },
    {
      title: '零售价',
      dataIndex: 'storeRetailPrice',
      key: 'storeRetailPrice',
      width: 100,
      sorter: true,
      render: (v: number) => (v != null ? v.toFixed(2) : '-'),
    },
    {
      title: '自动定价',
      dataIndex: 'isStoreAutoPricing',
      key: 'isStoreAutoPricing',
      width: 90,
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? '是' : '否'}</Tag>,
    },
    {
      title: '特殊商品',
      dataIndex: 'isStoreSpecialProduct',
      key: 'isStoreSpecialProduct',
      width: 90,
      render: (v: boolean) => <Tag color={v ? 'orange' : 'default'}>{v ? '是' : '否'}</Tag>,
    },
    {
      title: '折扣率',
      dataIndex: 'discountRate',
      key: 'discountRate',
      width: 90,
      sorter: true,
      render: (v: number) => (v != null ? (v * 100).toFixed(1) + '%' : '-'),
    },
    {
      title: '启用',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 70,
      render: (v: boolean) => <Tag color={v ? 'success' : 'error'}>{v ? '是' : '否'}</Tag>,
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      sorter: true,
      render: (v: string) => (v ? dayjs(v).format('YYYY-MM-DD HH:mm') : '-'),
    },
    {
      title: '更新人',
      dataIndex: 'updatedBy',
      key: 'updatedBy',
      width: 100,
    },
  ], [])

  const selectedCount = selectedRowKeys.length

  return (
    <PageContainer
      title="分店商品价格管理"
      subtitle="管理各分店的商品采购价、零售价、自动定价及折扣率"
    >
      <Card>
        <Form form={searchForm} layout="inline" style={{ marginBottom: 16 }} onFinish={handleSearch}>
          <Form.Item name="storeCode" label="分店">
            <Select
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder="请选择分店"
              style={{ width: 260 }}
              allowClear
              onChange={handleStoreChange}
            />
          </Form.Item>
          <Form.Item name="localSupplierCode" label="供应商">
            <Select
              showSearch
              optionFilterProp="label"
              options={supplierOptions}
              placeholder="全部供应商"
              style={{ width: 220 }}
              allowClear
            />
          </Form.Item>
          <Form.Item name="search" label="搜索">
            <Input allowClear placeholder="商品代码/名称/货号/条码" style={{ width: 240 }} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" disabled={!selectedStoreCode}>
                查询
              </Button>
              <Button onClick={handleReset}>重置</Button>
            </Space>
          </Form.Item>
        </Form>

        <div style={{ marginBottom: 12 }}>
          <Space wrap>
            <Button type="primary" disabled={selectedCount === 0} onClick={openBatchModal}>
              批量更新 {selectedCount > 0 ? `(${selectedCount})` : ''}
            </Button>
            <Button disabled={selectedCount === 0} onClick={openSyncModal}>
              同步到其他分店 {selectedCount > 0 ? `(${selectedCount})` : ''}
            </Button>
            <Button onClick={openCopyModal}>复制分店数据</Button>
            <Button onClick={openHqSyncModal}>从HQ更新零售价</Button>
          </Space>
        </div>

        <Table
          rowKey="key"
          loading={loading}
          dataSource={data}
          columns={columns}
          scroll={{ x: 2200, y: 600 }}
          virtual
          rowSelection={{
            selectedRowKeys,
            onChange: (keys) => setSelectedRowKeys(keys),
          }}
          pagination={{
            total,
            current: page,
            pageSize,
            showSizeChanger: true,
            pageSizeOptions: ['10', '20', '50', '100', '200'],
            showTotal: (t) => `共 ${t} 条`,
          }}
          onChange={onTableChange}
          size="small"
        />
      </Card>

      <Modal
        open={batchModalOpen}
        title="批量更新价格"
        onCancel={() => setBatchModalOpen(false)}
        onOk={handleBatchUpdate}
        width={600}
        forceRender
      >
        <p style={{ marginBottom: 16, color: '#666' }}>
          已选择 <strong>{selectedCount}</strong> 个商品，分店：<strong>{selectedStoreCode}</strong>
        </p>
        <Form form={batchForm} layout="vertical">
          <Form.Item name="updatePurchasePrice" valuePropName="checked" label="更新采购价">
            <Checkbox>启用</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updatePurchasePrice !== cur.updatePurchasePrice}>
            {({ getFieldValue }) =>
              getFieldValue('updatePurchasePrice') ? (
                <Form.Item name="purchasePrice" label="采购价" rules={[{ required: true, type: 'number', min: 0 }]}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateRetailPrice" valuePropName="checked" label="更新零售价">
            <Checkbox>启用</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateRetailPrice !== cur.updateRetailPrice}>
            {({ getFieldValue }) =>
              getFieldValue('updateRetailPrice') ? (
                <Form.Item name="storeRetailPriceValue" label="零售价" rules={[{ required: true, type: 'number', min: 0 }]}>
                  <InputNumber min={0} precision={2} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateAutoPricing" valuePropName="checked" label="更新自动定价">
            <Checkbox>启用</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateAutoPricing !== cur.updateAutoPricing}>
            {({ getFieldValue }) =>
              getFieldValue('updateAutoPricing') ? (
                <Form.Item name="isAutoPricing" label="自动定价" valuePropName="checked">
                  <Switch checkedChildren="是" unCheckedChildren="否" />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateSpecialProduct" valuePropName="checked" label="更新特殊商品">
            <Checkbox>启用</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateSpecialProduct !== cur.updateSpecialProduct}>
            {({ getFieldValue }) =>
              getFieldValue('updateSpecialProduct') ? (
                <Form.Item name="isSpecialProduct" label="特殊商品" valuePropName="checked">
                  <Switch checkedChildren="是" unCheckedChildren="否" />
                </Form.Item>
              ) : null
            }
          </Form.Item>

          <Form.Item name="updateDiscountRate" valuePropName="checked" label="更新折扣率">
            <Checkbox>启用</Checkbox>
          </Form.Item>
          <Form.Item noStyle shouldUpdate={(prev, cur) => prev.updateDiscountRate !== cur.updateDiscountRate}>
            {({ getFieldValue }) =>
              getFieldValue('updateDiscountRate') ? (
                <Form.Item name="discountRate" label="折扣率" rules={[{ required: true, type: 'number', min: 0, max: 1 }]}>
                  <InputNumber min={0} max={1} step={0.01} precision={4} style={{ width: '100%' }} />
                </Form.Item>
              ) : null
            }
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={syncModalOpen}
        title="同步到其他分店"
        onCancel={() => setSyncModalOpen(false)}
        onOk={handleSyncToOtherStores}
        width={650}
        forceRender
      >
        <p style={{ marginBottom: 16, color: '#666' }}>
          已选择 <strong>{selectedCount}</strong> 个商品，源分店：<strong>{selectedStoreCode}</strong>
        </p>
        <Form form={syncForm} layout="vertical">
          <Form.Item name="targetStoreCodes" label="目标分店" rules={[{ required: true }]}>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions.filter((s) => s.value !== selectedStoreCode)}
              placeholder="请选择目标分店"
            />
          </Form.Item>
          <Form.Item name="syncMode" label="同步模式" rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'Overwrite', label: '覆盖' },
                { value: 'OnlyUpdateNull', label: '仅更新空值' },
              ]}
            />
          </Form.Item>
          <Form.Item label="同步字段">
            <Space wrap>
              <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
                <Checkbox>采购价</Checkbox>
              </Form.Item>
              <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>零售价</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
                <Checkbox>自动定价</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
                <Checkbox>特殊商品</Checkbox>
              </Form.Item>
              <Form.Item name="syncDiscountRate" valuePropName="checked" noStyle>
                <Checkbox>折扣率</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={copyModalOpen}
        title="复制分店数据"
        onCancel={() => {
          if (eventSourceRef.current) {
            eventSourceRef.current.close()
            eventSourceRef.current = null
          }
          setCopying(false)
          setCopyModalOpen(false)
        }}
        footer={copying ? null : undefined}
        width={650}
        forceRender
      >
        <Form form={copyForm} layout="vertical">
          <Form.Item name="sourceStoreCode" label="源分店" rules={[{ required: true }]}>
            <Select showSearch optionFilterProp="label" options={storeOptions} placeholder="请选择源分店" />
          </Form.Item>
          <Form.Item name="targetStoreCodes" label="目标分店" rules={[{ required: true }]}>
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder="请选择目标分店"
            />
          </Form.Item>
          <Form.Item name="mode" label="复制模式" rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'Overwrite', label: '覆盖' },
                { value: 'OnlyUpdateNull', label: '仅更新空值' },
              ]}
            />
          </Form.Item>
          <Form.Item label="同步字段">
            <Space wrap>
              <Form.Item name="syncPurchasePrice" valuePropName="checked" noStyle>
                <Checkbox>采购价</Checkbox>
              </Form.Item>
              <Form.Item name="syncRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>零售价</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsAutoPricing" valuePropName="checked" noStyle>
                <Checkbox>自动定价</Checkbox>
              </Form.Item>
              <Form.Item name="syncIsSpecialProduct" valuePropName="checked" noStyle>
                <Checkbox>特殊商品</Checkbox>
              </Form.Item>
              <Form.Item name="syncDiscountRate" valuePropName="checked" noStyle>
                <Checkbox>折扣率</Checkbox>
              </Form.Item>
              <Form.Item name="syncMultiCode" valuePropName="checked" noStyle>
                <Checkbox>多码商品</Checkbox>
              </Form.Item>
              <Form.Item name="syncMultiCodeRetailPrice" valuePropName="checked" noStyle>
                <Checkbox>多码零售价</Checkbox>
              </Form.Item>
            </Space>
          </Form.Item>
        </Form>

        {copying && copyProgress && (
          <Card size="small" style={{ marginTop: 16 }}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <div>
                <Spin size="small" /> {copyProgress.message}
              </div>
              {copyProgress.totalStores > 0 && (
                <Progress
                  percent={Math.round((copyProgress.storeIndex / copyProgress.totalStores) * 100)}
                  status="active"
                />
              )}
              <div style={{ color: '#666', fontSize: 12 }}>
                零售价已复制：{copyProgress.retailPriceCopied} | 多码已复制：{copyProgress.multiCodeCopied}
              </div>
            </Space>
          </Card>
        )}
      </Modal>

      <Modal
        open={hqSyncModalOpen}
        title="从HQ更新零售价"
        onCancel={() => setHqSyncModalOpen(false)}
        onOk={handleSyncFromHq}
        width={550}
        confirmLoading={hqSyncing}
        forceRender
      >
        <Form form={hqSyncForm} layout="vertical">
          <Form.Item name="selectedStoreCodes" label="分店（不选则全部）">
            <Select
              mode="multiple"
              showSearch
              optionFilterProp="label"
              options={storeOptions}
              placeholder="不选则同步所有分店"
              allowClear
            />
          </Form.Item>
          <Form.Item name="dateRange" label="起始日期">
            <DatePicker.RangePicker style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </PageContainer>
  )
}
