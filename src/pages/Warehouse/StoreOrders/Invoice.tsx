import { DownloadOutlined, FileExcelOutlined, PrinterOutlined, RollbackOutlined } from '@ant-design/icons'
import { Button, Empty, Image, Space, Spin, message } from 'antd'
import ExcelJS from 'exceljs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { getStores } from '../../../services/storeService'
import { getStoreOrderDetail } from '../../../services/storeOrderService'
import type { StoreDto } from '../../../types/store'
import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'
import { buildDocumentFileName, downloadElementAsPdf, formatCurrency, formatPrintDate } from './printUtils'
import './print.css'

const TRANSPARENT_IMAGE_FALLBACK = 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs='

function sortInvoiceItems(items: StoreOrderDetailLine[]) {
  return [...items].sort((left, right) => {
    const leftAllocQuantity = Number(left.allocQuantity ?? 0)
    const rightAllocQuantity = Number(right.allocQuantity ?? 0)
    const leftIsZero = leftAllocQuantity === 0
    const rightIsZero = rightAllocQuantity === 0

    if (leftIsZero !== rightIsZero) {
      return leftIsZero ? 1 : -1
    }

    const itemNumberCompare = (left.itemNumber || '').localeCompare(right.itemNumber || '', 'zh-CN', {
      numeric: true,
      sensitivity: 'base',
    })
    if (itemNumberCompare !== 0) {
      return itemNumberCompare
    }

    return (left.productCode || '').localeCompare(right.productCode || '', 'zh-CN', {
      numeric: true,
      sensitivity: 'base',
    })
  })
}

async function downloadInvoiceExcel(order: StoreOrderDetail, items: StoreOrderDetailLine[], storeName?: string) {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('Invoice')

  worksheet.columns = [
    { header: '#', key: 'index', width: 8 },
    { header: 'Item No', key: 'itemNumber', width: 18 },
    { header: 'Name', key: 'productName', width: 28 },
    { header: 'Barcode', key: 'barcode', width: 20 },
    { header: 'Cost', key: 'importPrice', width: 14 },
    { header: 'Order Qty', key: 'orderQuantity', width: 12 },
    { header: 'Ship Qty', key: 'allocQuantity', width: 12 },
    { header: 'Subtotal', key: 'subtotal', width: 16 },
  ]

  worksheet.getRow(1).font = { bold: true }

  items.forEach((item, index) => {
    const orderQuantity = Number(item.quantity || 0)
    const allocQuantity = Number(item.allocQuantity ?? 0)
    worksheet.addRow({
      index: index + 1,
      itemNumber: item.itemNumber || '',
      productName: item.productName || '',
      barcode: item.barcode || item.productCode,
      importPrice: Number(item.importPrice || 0),
      orderQuantity,
      allocQuantity,
      subtotal: Number((allocQuantity * Number(item.importPrice || 0)).toFixed(2)),
    })
  })

  const subTotal = Number(order.totalImportAmount || 0)
  const gst = Number((subTotal * 0.1).toFixed(2))
  const freight = Number(order.shippingFee || 0)
  const total = Number((subTotal + gst + freight).toFixed(2))

  worksheet.addRow({})
  worksheet.addRow({ productName: 'Sub-Total', subtotal: subTotal })
  worksheet.addRow({ productName: 'GST 10%', subtotal: gst })
  worksheet.addRow({ productName: 'Freight', subtotal: freight })
  worksheet.addRow({ productName: 'Total', subtotal: total })
  worksheet.addRow({})
  worksheet.addRow({ productName: '备注', barcode: '图片只做参考，以实物为准' })

  worksheet.getColumn('importPrice').numFmt = '$#,##0.00'
  worksheet.getColumn('subtotal').numFmt = '$#,##0.00'

  const buffer = await workbook.xlsx.writeBuffer()
  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = buildDocumentFileName('发票', storeName || order.storeCode, order.orderNo || order.orderGUID, 'xlsx')
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}

export default function StoreOrderInvoicePage() {
  const route = useStableRouteContext()
  const id = route?.params.id || ''
  const navigate = useNavigate()
  const printRootRef = useRef<HTMLDivElement | null>(null)
  const [loading, setLoading] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [exportingExcel, setExportingExcel] = useState(false)
  const [order, setOrder] = useState<StoreOrderDetail | null>(null)
  const [store, setStore] = useState<StoreDto | null>(null)

  useDynamicTabTitle(
    order?.orderNo
      ? `发票 - ${store?.storeName || order.storeCode || '未知分店'} - ${order.orderNo}`
      : '发票',
  )

  useEffect(() => {
    if (!id) {
      return
    }

    const load = async () => {
      setLoading(true)
      try {
        const detail = await getStoreOrderDetail(id)
        if (!detail) {
          message.error('未找到订单明细')
          return
        }

        setOrder(detail)

        if (detail.storeCode) {
          const storeResult = await getStores({
            search: detail.storeCode,
            page: 1,
            pageSize: 1,
          })
          setStore(storeResult.items[0] ?? null)
        } else {
          setStore(null)
        }
      } catch (error) {
        console.error(error)
        message.error(error instanceof Error ? error.message : '加载发票失败')
      } finally {
        setLoading(false)
      }
    }

    void load()
  }, [id])

  const totals = useMemo(() => {
    const subTotal = Number(order?.totalImportAmount || 0)
    const gst = Number((subTotal * 0.1).toFixed(2))
    const freight = Number(order?.shippingFee || 0)
    return {
      subTotal,
      gst,
      freight,
      total: Number((subTotal + gst + freight).toFixed(2)),
    }
  }, [order?.shippingFee, order?.totalImportAmount])

  const sortedItems = useMemo(() => sortInvoiceItems(order?.items || []), [order?.items])

  const handlePrint = () => {
    window.print()
  }

  const handleDownloadPdf = async () => {
    if (!printRootRef.current || !order) {
      return
    }

    setDownloading(true)
    try {
      await downloadElementAsPdf(
        printRootRef.current,
        buildDocumentFileName('发票', store?.storeName || order.storeCode, order.orderNo || order.orderGUID, 'pdf'),
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '下载发票 PDF 失败')
    } finally {
      setDownloading(false)
    }
  }

  const handleExportExcel = async () => {
    if (!order) {
      return
    }

    setExportingExcel(true)
    try {
      await downloadInvoiceExcel(order, sortedItems, store?.storeName || order.storeCode)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '导出发票 Excel 失败')
    } finally {
      setExportingExcel(false)
    }
  }

  if (loading) {
    return <Spin size="large" style={{ display: 'block', margin: '120px auto' }} />
  }

  if (!order) {
    return <Empty description="未找到订单数据" style={{ marginTop: 120 }} />
  }

  const displayStoreName = store?.storeName || order.storeCode || '--'
  const storeAddress = order.storeAddress || store?.address || '--'

  return (
    <div className="store-order-print-page">
      <div className="store-order-print-toolbar no-print">
        <Space wrap>
          <Button icon={<RollbackOutlined />} onClick={() => navigate(-1)}>
            返回
          </Button>
          <Button icon={<FileExcelOutlined />} loading={exportingExcel} onClick={() => void handleExportExcel()}>
            导出 Excel
          </Button>
          <Button icon={<DownloadOutlined />} loading={downloading} onClick={() => void handleDownloadPdf()}>
            下载 PDF
          </Button>
          <Button type="primary" icon={<PrinterOutlined />} onClick={handlePrint}>
            打印发票
          </Button>
        </Space>
      </div>

      <div ref={printRootRef} className="store-order-print-root store-order-print-paper store-order-invoice-paper">
        <div className="store-order-invoice-header">
          <div className="store-order-invoice-logo">
            <Image src="/logo.svg" alt="HOT BARGAIN" preview={false} style={{ maxHeight: 120, objectFit: 'contain' }} />
          </div>
          <div className="store-order-invoice-company">
            <h4>WAREHOUSE ADDRESS:</h4>
            <p>3 Rogilla close Maryland, NSW, 2287, Australia</p>
            <p>
              <strong>A.B.N.</strong> 35 160 589 793
            </p>
            <p>
              <strong>WAREHOUSE EMAIL:</strong> dong@hotbargain.com.au
            </p>
          </div>
        </div>

        <div className="store-order-invoice-bar">
          <div>INVOICE NO. {order.orderNo || order.orderGUID}</div>
          <div>INVOICE DATE: {formatPrintDate(undefined, false)}</div>
        </div>

        <div className="store-order-invoice-customer">
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">CUSTOMER:</span>
            <span>{displayStoreName}</span>
          </div>
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">CUSTOMER CONTACT:</span>
            <span>{store?.contactPhone || '-'}</span>
          </div>
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">ADDRESS:</span>
            <span>{storeAddress}</span>
          </div>
        </div>

        <table className="store-order-invoice-table">
          <thead>
            <tr>
              <th className="col-index">#</th>
              <th className="col-image">图片</th>
              <th className="col-item">货号</th>
              <th className="col-barcode">条码</th>
              <th>名称</th>
              <th className="col-cost">成本</th>
              <th className="col-qty">订货数量</th>
              <th className="col-qty">发货数量</th>
              <th className="col-subtotal">小计</th>
            </tr>
          </thead>
          <tbody>
            {sortedItems.map((item, index) => {
              const orderQuantity = Number(item.quantity || 0)
              const allocQuantity = Number(item.allocQuantity ?? 0)
              return (
                <tr key={item.detailGUID}>
                  <td className="col-index">{index + 1}</td>
                  <td className="col-image">
                    {item.productImage ? (
                      <Image
                        src={item.productImage}
                        alt=""
                        width={40}
                        height={40}
                        preview={false}
                        fallback={TRANSPARENT_IMAGE_FALLBACK}
                        style={{ objectFit: 'contain' }}
                      />
                    ) : (
                      null
                    )}
                  </td>
                  <td className="col-item">{item.itemNumber || '--'}</td>
                  <td className="col-barcode">
                    {item.barcode ? (
                      <BarcodePreview
                        value={item.barcode}
                        showCopy={false}
                        textMaxWidth={110}
                        options={{ width: 1, height: 24, margin: 0 }}
                      />
                    ) : (
                      item.productCode
                    )}
                  </td>
                  <td>{item.productName || '--'}</td>
                  <td className="col-cost">{formatCurrency(item.importPrice)}</td>
                  <td className="col-qty">{orderQuantity}</td>
                  <td className="col-qty">{allocQuantity}</td>
                  <td className="col-subtotal">{formatCurrency(allocQuantity * Number(item.importPrice || 0))}</td>
                </tr>
              )
            })}
          </tbody>
        </table>

        <div className="store-order-invoice-footer">
          <div className="store-order-invoice-payment">
            <h4>PAYMENT DETAIL: DIRECT DEBIT</h4>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">NAME:</span>
              <span>HOT BARGAIN INTERNATIONAL</span>
            </div>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">BSB:</span>
              <span>12532</span>
            </div>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">ACCOUNT:</span>
              <span>208034605</span>
            </div>
            <div className="store-order-invoice-disclaimer">
              All products remain the property of Hot Bargain International Pty Ltd until payment is received in full
              for the invoiced amount. Payment strictly within 30 days of the invoice date.
            </div>
          </div>

          <div className="store-order-invoice-totals">
            <div className="store-order-invoice-total-row">
              <span>Sub-Total:</span>
              <span>{formatCurrency(totals.subTotal)}</span>
            </div>
            <div className="store-order-invoice-total-row">
              <span>GST 10%:</span>
              <span>{formatCurrency(totals.gst)}</span>
            </div>
            <div className="store-order-invoice-total-row">
              <span>Freight:</span>
              <span>{formatCurrency(totals.freight)}</span>
            </div>
            <div className="store-order-invoice-total-row is-grand">
              <span>Total Before Discount:</span>
              <span>{formatCurrency(totals.total)}</span>
            </div>
          </div>
        </div>

        <div className="store-order-print-footer">
          <div>Page document</div>
          <div>图片只做参考，以实物为准</div>
          <div>Date: {formatPrintDate(undefined)}</div>
        </div>
      </div>
    </div>
  )
}
