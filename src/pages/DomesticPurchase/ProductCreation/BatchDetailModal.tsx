import { DownloadOutlined } from '@ant-design/icons'
import { Button, InputNumber, message, Modal, Space, Table, Tabs } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import ExcelJS from 'exceljs'
import { useCallback, useEffect, useMemo, useState } from 'react'
import BarcodePreview from '../../../components/BarcodePreview'
import { getBatchDetail, updatePrivateLabelPrice } from '../../../services/domesticProductCreationService'
import { ProductCreationType } from '../../../types/domesticProductCreation'
import type { BatchDetail, BatchInfo, BatchProductItem, UpdatePriceItem } from '../../../types/domesticProductCreation'
import { generateBarcodeImages } from '../../../utils/barcode'

interface BatchDetailModalProps {
  visible: boolean
  batch: BatchInfo | null
  onClose: () => void
}

export default function BatchDetailModal({ visible, batch, onClose }: BatchDetailModalProps) {
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
        message.error(response.message || '加载明细失败')
      }
    } catch {
      setLoading(false)
      message.error('加载明细失败')
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
        message.success('保存成功')
        loadDetail(batch.batchNumber)
      } else {
        message.error(response.message || '保存失败')
      }
    } catch {
      setSaving(false)
      message.error('保存失败')
    }
  }

  const handleExportExcel = useCallback(async () => {
    if (!detail?.items || !batch?.batchNumber) { message.warning('无数据可导出'); return }
    message.loading({ content: '导出中...', key: 'export' })
    try {
      const workbook = new ExcelJS.Workbook()
      const worksheet = workbook.addWorksheet('批次明细')
      worksheet.columns = [
        { header: '货号', key: 'itemNumber', width: 20 },
        { header: '条码', key: 'barcode', width: 18 },
        { header: '商品名称', key: 'productName', width: 30 },
        { header: '贴牌价格', key: 'privateLabelPrice', width: 12 },
        { header: '套装数量', key: 'setQuantity', width: 10 },
        { header: '套装价格', key: 'setPrice', width: 12 },
        { header: '条码图片', key: 'barcodeImage', width: 25 },
      ]
      const headerRow = worksheet.getRow(1)
      headerRow.height = 25
      headerRow.eachCell((cell: any) => {
        cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
        cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4472C4' } }
        cell.alignment = { horizontal: 'center', vertical: 'middle' }
      })
      const allItems: Array<{ itemNumber: string; barcode: string; productName: string; privateLabelPrice: number | string; setQuantity: number | string; setPrice: number | string }> = []
      detail.items.forEach((item: BatchProductItem) => {
        if (item.productType === ProductCreationType.SET || item.productType === ProductCreationType.NORMAL) {
          allItems.push({
            itemNumber: item.hbProductNo,
            barcode: item.barcode,
            productName: item.productName,
            privateLabelPrice: item.privateLabelPrice ?? '',
            setQuantity: item.setQuantity ?? '',
            setPrice: item.setPrice ?? '',
          })
        }
      })
      const barcodes = allItems.map((item) => item.barcode).filter(Boolean)
      const barcodeMap = await generateBarcodeImages(barcodes, { width: 1, height: 40, displayValue: true })
      allItems.forEach((item, index) => {
        const currentRow = worksheet.getRow(index + 2)
        currentRow.values = [item.itemNumber, item.barcode, item.productName, item.privateLabelPrice, item.setQuantity, item.setPrice, '']
        currentRow.height = 50
        if (index % 2 === 0) {
          currentRow.eachCell((cell: any) => { cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFF9F9F9' } } })
        }
        if (item.barcode) {
          const barcodeData = barcodeMap.get(item.barcode)
          if (barcodeData) {
            const base64Image = barcodeData.split(',')[1]
            const imageId = workbook.addImage({ base64: base64Image, extension: 'png' })
            worksheet.addImage(imageId, { tl: { col: 6, row: index + 1 }, br: { col: 7, row: index + 2 }, editAs: 'oneCell' } as any)
          }
        }
      })
      worksheet.eachRow((row, rowNumber) => {
        if (rowNumber > 1) row.eachCell({ includeEmpty: false }, (cell) => { cell.alignment = { vertical: 'middle' } })
      })
      const buffer = await workbook.xlsx.writeBuffer()
      const blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      const today = new Date()
      const dateStr = `${today.getFullYear()}${String(today.getMonth() + 1).padStart(2, '0')}${String(today.getDate()).padStart(2, '0')}`
      link.download = `批次明细_${batch.batchNumber}_${dateStr}.xlsx`
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      URL.revokeObjectURL(url)
      message.success({ content: '导出成功', key: 'export' })
    } catch (error) {
      console.error('导出失败:', error)
      message.error({ content: '导出失败', key: 'export' })
    }
  }, [detail, batch])

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
    { title: '货号', dataIndex: 'hbProductNo', key: 'hbProductNo', width: 100, fixed: 'left', render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: '条码', dataIndex: 'barcode', key: 'barcode', width: 120, render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span> },
    { title: '商品名称', dataIndex: 'productName', key: 'productName', width: 180 },
    {
      title: '贴牌价格',
      dataIndex: 'privateLabelPrice',
      key: 'privateLabelPrice',
      width: 100,
      render: (text, record) => <InputNumber value={editingPrices[record.itemNumber] !== undefined ? editingPrices[record.itemNumber] : text} onChange={(value) => handlePriceChange(record.itemNumber, value)} min={0} precision={2} style={{ width: '100%' }} formatter={(value) => `$ ${value}`} parser={(value) => value?.replace(/\$\s?/g, '') as any} />,
    },
    { title: '套装数量', dataIndex: 'setQuantity', key: 'setQuantity', width: 100, align: 'center', render: (text) => text || '-' },
    { title: '套装价格', dataIndex: 'setPrice', key: 'setPrice', width: 120, render: (text) => (text ? `$${text}` : '-') },
    {
      title: '条码图片',
      key: 'barcodeImage',
      width: 150,
      align: 'center',
      render: (_, record) => {
        if (!record.barcode) return '-'
        return <BarcodePreview value={record.barcode} options={{ width: 1, height: 40, displayValue: true, fontSize: 10, margin: 4 }} showCopy={false} />
      },
    },
  ]

  const tabItems = [
    { key: 'all', label: `全部 (${detail?.totalCount || 0})` },
    { key: 'normal', label: `普通 (${detail?.normalCount || 0})` },
    { key: 'set', label: `套装 (${detail?.setCount || 0})` },
  ]

  if (!batch) return null

  return (
    <Modal
      title={`批次明细 - ${batch.batchNumber}`}
      open={visible}
      onCancel={onClose}
      width={1100}
      footer={
        <Space>
          <Button onClick={onClose}>关闭</Button>
          <Button type="primary" loading={saving} onClick={handleSavePrices}>保存价格</Button>
        </Space>
      }
    >
      <div style={{ marginBottom: 16 }}>
        <Space size="large" style={{ width: '100%', justifyContent: 'space-between' }}>
          <Space size="large">
            <span><strong>供应商:</strong> {detail?.supplierName || batch.supplierName}</span>
            <span><strong>创建时间:</strong> {detail?.createdAt || batch.createdAt}</span>
          </Space>
          <Button icon={<DownloadOutlined />} onClick={handleExportExcel}>导出Excel</Button>
        </Space>
      </div>
      <Tabs activeKey={activeTab} onChange={setActiveTab} items={tabItems} style={{ marginBottom: 16 }} />
      <Table columns={columns} dataSource={filteredItems} rowKey="itemNumber" loading={loading} pagination={{ defaultPageSize: 20, showSizeChanger: true, showTotal: (total) => `共 ${total} 条` }} scroll={{ x: 1000, y: 400 }} size="small" />
    </Modal>
  )
}
