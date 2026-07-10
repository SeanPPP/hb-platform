import { CopyOutlined, DownloadOutlined } from '@ant-design/icons'
import { Button, InputNumber, message, Modal, Space, Table, Tabs } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import BarcodePreview from '../../../components/BarcodePreview'
import { getBatchDetail, updatePrivateLabelPrice } from '../../../services/domesticProductCreationService'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { BatchDetail, BatchInfo, BatchProductItem, UpdatePriceItem } from '../../../types/domesticProductCreation'
import { copyTextToClipboard } from '../../../utils/clipboard'
import { exportProductCreationBatchToExcel, getExportableBatchItems } from './exportBatchDetail'

interface BatchDetailModalProps {
  visible: boolean
  batch: BatchInfo | null
  onClose: () => void
}

export default function BatchDetailModal({ visible, batch, onClose }: BatchDetailModalProps) {
  const { t } = useTranslation()
  const [loading, setLoading] = useState(false)
  const [detail, setDetail] = useState<BatchDetail | null>(null)
  const [activeTab, setActiveTab] = useState('all')
  const [editingPrices, setEditingPrices] = useState<Record<string, number>>({})
  const [saving, setSaving] = useState(false)

  const loadDetail = async (batchNumber: string) => {
    setLoading(true)
    try {
      const response = await getBatchDetail(batchNumber)
      setLoading(false)
      if (response.success && response.data) {
        setDetail(response.data)
        const prices: Record<string, number> = {}
        response.data.items.forEach((item) => {
          if (item.privateLabelPrice !== undefined) prices[item.itemNumber] = item.privateLabelPrice
        })
        setEditingPrices(prices)
      } else {
        message.error(response.message || t('productCreation.loadDetailFailed', '加载明细失败'))
      }
    } catch {
      setLoading(false)
      message.error(t('productCreation.loadDetailFailed', '加载明细失败'))
    }
  }

  useEffect(() => {
    if (visible && batch?.batchNumber) loadDetail(batch.batchNumber)
  }, [visible, batch])

  const handlePriceChange = useCallback((itemNumber: string, value: number | null) => {
    setEditingPrices((prev) => ({ ...prev, [itemNumber]: value || 0 }))
  }, [])

  const handleSavePrices = async () => {
    if (!batch?.batchNumber) return
    setSaving(true)
    try {
      const items: UpdatePriceItem[] = Object.entries(editingPrices).map(([itemNumber, privateLabelPrice]) => ({ itemNumber, privateLabelPrice }))
      const response = await updatePrivateLabelPrice(batch.batchNumber, items)
      setSaving(false)
      if (response.success) {
        message.success(t('productCreation.saveSuccess', '保存成功'))
        loadDetail(batch.batchNumber)
      } else {
        message.error(response.message || t('productCreation.saveFailed', '保存失败'))
      }
    } catch {
      setSaving(false)
      message.error(t('productCreation.saveFailed', '保存失败'))
    }
  }

  const handleExportExcel = useCallback(async () => {
    if (!detail?.items || !batch?.batchNumber) { message.warning(t('productCreation.noDataToExport', '无数据可导出')); return }
    if (getExportableBatchItems(detail.items).length === 0) { message.warning(t('productCreation.noDataToExport', '无数据可导出')); return }
    const messageKey = `product-creation-export-${batch.batchNumber}`
    message.loading({ content: t('productCreation.exporting', '导出中...'), key: messageKey })
    try {
      await exportProductCreationBatchToExcel(detail, { batchNumber: batch.batchNumber, t })
      message.success({ content: t('productCreation.exportSuccess', '导出成功'), key: messageKey })
    } catch (error) {
      console.error('导出失败:', error)
      message.error({ content: t('productCreation.exportFailed', '导出失败'), key: messageKey })
    }
  }, [detail, batch])

  const getCopyPrice = useCallback(
    (item: BatchProductItem) => {
      if (Object.prototype.hasOwnProperty.call(editingPrices, item.itemNumber)) {
        return editingPrices[item.itemNumber]
      }
      return item.privateLabelPrice
    },
    [editingPrices],
  )

  const hasCopyValue = (value: unknown) => value !== undefined && value !== null && value !== ''

  const formatCopyLine = useCallback(
    (item: BatchProductItem) => {
      const price = getCopyPrice(item)
      return [item.hbProductNo, item.barcode, hasCopyValue(price) ? String(price) : '']
        .filter(hasCopyValue)
        .join(' ')
    },
    [getCopyPrice],
  )

  const getCopyHeaderLine = useCallback(
    () => [
      t('productImport.hbProductNoCol', '货号'),
      t('domesticProducts.barcode', '条码'),
      t('productCreation.retailPrice', '零售价'),
    ].join(' '),
    [t],
  )

  const handleCopyRow = useCallback(
    (item: BatchProductItem) => {
      const text = formatCopyLine(item)
      if (!text) {
        message.warning(t('productCreation.noCopyData', '无可复制数据'))
        return
      }
      void copyTextToClipboard(text, {
        successMessage: t('productCreation.copyItemBarcodeSuccess', '已复制：{{value}}', { value: text }),
        failureMessage: t('productCreation.copyFailed', '复制失败，请手动复制'),
      })
    },
    [formatCopyLine, t],
  )

  const handleCopyAll = useCallback(() => {
    const lines = (detail?.items || []).map(formatCopyLine).filter(Boolean)
    if (!lines.length) {
      message.warning(t('productCreation.noCopyData', '无可复制数据'))
      return
    }
    void copyTextToClipboard([getCopyHeaderLine(), ...lines].join('\n'), {
      successMessage: t('productCreation.copyAllItemBarcodesSuccess', '已复制 {{count}} 行', { count: lines.length }),
      failureMessage: t('productCreation.copyFailed', '复制失败，请手动复制'),
    })
  }, [detail, formatCopyLine, getCopyHeaderLine, t])

  const filteredItems = useMemo(() => {
    if (!detail?.items) return []
    let result: typeof detail.items
    switch (activeTab) {
      case 'normal': result = detail.items.filter((item) => item.productType === ProductCreationType.NORMAL); break
      case 'set': result = detail.items.filter((item) => item.productType === ProductCreationType.SET || item.productType === ProductCreationType.SET_SUB_ITEM); break
      default: result = detail.items; break
    }
    return result.sort((a, b) => a.hbProductNo.localeCompare(b.hbProductNo))
  }, [detail, activeTab])

  const columns: ColumnsType<any> = [
    { title: t('productImport.hbProductNoCol', '货号'), dataIndex: 'hbProductNo', key: 'hbProductNo', width: 130, fixed: 'left', render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: t('domesticProducts.barcode', '条码'), dataIndex: 'barcode', key: 'barcode', width: 120, render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: t('domesticProducts.productName', '商品名称'), dataIndex: 'productName', key: 'productName', width: 180 },
    {
      title: t('productCreation.privateLabelPrice', '零售价'),
      dataIndex: 'privateLabelPrice',
      key: 'privateLabelPrice',
      width: 100,
      render: (text, record) => <InputNumber value={editingPrices[record.itemNumber] !== undefined ? editingPrices[record.itemNumber] : text} onChange={(value) => handlePriceChange(record.itemNumber, value)} min={0} precision={2} style={{ width: '100%' }} formatter={(value) => `$ ${value}`} parser={(value) => value?.replace(/\$\s?/g, '') as any} />,
    },
    { title: t('productCreation.setQuantity', '套装数量'), dataIndex: 'setQuantity', key: 'setQuantity', width: 100, align: 'center', render: (text) => text || '-' },
    { title: t('productCreation.setPrice', '套装价格'), dataIndex: 'setPrice', key: 'setPrice', width: 120, render: (text) => (text ? `$${text}` : '-') },
    {
      title: t('productCreation.barcodeImage', '条码图片'),
      key: 'barcodeImage',
      width: 150,
      align: 'center',
      render: (_, record) => {
        if (!record.barcode) return '-'
        return <BarcodePreview value={record.barcode} options={{ width: 1, height: 40, displayValue: true, fontSize: 10, margin: 4 }} showCopy={false} />
      },
    },
    {
      title: (
        <Button type="link" size="small" icon={<CopyOutlined />} onClick={handleCopyAll}>
          {t('productCreation.copyAllItemBarcodes', '复制全部')}
        </Button>
      ),
      key: 'actions',
      width: 130,
      fixed: 'right',
      render: (_, record) => (
        <Button type="link" size="small" icon={<CopyOutlined />} onClick={() => handleCopyRow(record)}>
          {t('productCreation.copyItemBarcode', '复制')}
        </Button>
      ),
    },
  ]

  const tabItems = [
    { key: 'all', label: t('productCreation.allTab', '全部 ({{count}})', { count: detail?.totalCount || 0 }) },
    { key: 'normal', label: t('productCreation.normalTab', '普通 ({{count}})', { count: detail?.normalCount || 0 }) },
    { key: 'set', label: t('productCreation.setTab', '套装 ({{count}})', { count: detail?.setCount || 0 }) },
  ]

  if (!batch) return null

  return (
    <Modal
      title={t('productCreation.batchDetailTitle', '批次明细 - {{batchNumber}}', { batchNumber: batch.batchNumber })}
      open={visible}
      onCancel={onClose}
      width={1200}
      footer={
        <Space>
          <Button onClick={onClose}>{t('common.close', '关闭')}</Button>
          <Button type="primary" loading={saving} onClick={handleSavePrices}>{t('productCreation.savePrices', '保存价格')}</Button>
        </Space>
      }
    >
      <div style={{ marginBottom: 16 }}>
        <Space size="large" style={{ width: '100%', justifyContent: 'space-between' }}>
          <Space size="large">
            <span><strong>{t('domesticProducts.supplier', '供应商')}:</strong> {detail?.supplierName || batch.supplierName}</span>
            <span><strong>{t('chinaSuppliers.createdAt', '创建时间')}:</strong> {detail?.createdAt || batch.createdAt}</span>
          </Space>
          <Button icon={<DownloadOutlined />} onClick={handleExportExcel}>{t('productCreation.exportExcel', '导出Excel')}</Button>
        </Space>
      </div>
      <Tabs activeKey={activeTab} onChange={setActiveTab} items={tabItems} style={{ marginBottom: 16 }} />
      <Table columns={columns} dataSource={filteredItems} rowKey="itemNumber" loading={loading} pagination={{ defaultPageSize: 20, showSizeChanger: true, showTotal: (total) => t('common.totalCount', '共 {{count}} 条', { count: total }) }} scroll={{ x: 1200, y: 400 }} size="small" />
    </Modal>
  )
}
