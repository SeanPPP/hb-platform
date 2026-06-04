import { DownloadOutlined, FileExcelOutlined, PrinterOutlined, RollbackOutlined } from '@ant-design/icons'
import { Button, Empty, Image, Space, Spin, message } from 'antd'
import ExcelJS from 'exceljs'
import type { TFunction } from 'i18next'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import BarcodePreview from '../../../components/BarcodePreview'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { getStores } from '../../../services/storeService'
import { getStoreOrderDetail } from '../../../services/storeOrderService'
import type { StoreDto } from '../../../types/store'
import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'
import { shouldShowStoreOrderDetailInitialLoading } from './detailLoadState'
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

async function downloadInvoiceExcel(order: StoreOrderDetail, items: StoreOrderDetailLine[], storeName: string | undefined, t: TFunction) {
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet(t('warehouse.invoice.excel.sheetName'))

  worksheet.columns = [
    { header: '#', key: 'index', width: 8 },
    { header: t('warehouse.invoice.excel.itemNo'), key: 'itemNumber', width: 18 },
    { header: t('warehouse.invoice.excel.name'), key: 'productName', width: 28 },
    { header: t('warehouse.invoice.excel.barcode'), key: 'barcode', width: 20 },
    { header: t('warehouse.invoice.excel.cost'), key: 'importPrice', width: 14 },
    { header: t('warehouse.invoice.excel.orderQty'), key: 'orderQuantity', width: 12 },
    { header: t('warehouse.invoice.excel.shipQty'), key: 'allocQuantity', width: 12 },
    { header: t('warehouse.invoice.excel.subtotal'), key: 'subtotal', width: 16 },
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
  worksheet.addRow({ productName: t('warehouse.invoice.subTotal'), subtotal: subTotal })
  worksheet.addRow({ productName: t('warehouse.invoice.gst'), subtotal: gst })
  worksheet.addRow({ productName: t('warehouse.invoice.freight'), subtotal: freight })
  worksheet.addRow({ productName: t('warehouse.invoice.total'), subtotal: total })
  worksheet.addRow({})
  worksheet.addRow({ productName: t('warehouse.invoice.remarks'), barcode: t('warehouse.invoice.imageRefNote') })

  worksheet.getColumn('importPrice').numFmt = '$#,##0.00'
  worksheet.getColumn('subtotal').numFmt = '$#,##0.00'

  const buffer = await workbook.xlsx.writeBuffer()
  const blob = new Blob([buffer], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = buildDocumentFileName(
    t('warehouse.invoice.fileName'),
    storeName || order.storeCode,
    order.orderNo || order.orderGUID,
    'xlsx',
    {
      unknownStore: t('warehouse.invoice.unknownStore'),
      unknownOrder: t('warehouse.invoice.unknownOrder'),
    },
  )
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}

export default function StoreOrderInvoicePage() {
  const { t } = useTranslation()
  const route = useStableRouteContext()
  const id = route?.params.id || ''
  const navigate = useNavigate()
  const printRootRef = useRef<HTMLDivElement | null>(null)
  // 记录当前发票已完成首次加载，保活 Tab 恢复时保留打印内容并静默刷新。
  const loadedOrderIdRef = useRef<string | null>(null)
  const visibleOrderIdRef = useRef<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [exportingExcel, setExportingExcel] = useState(false)
  const [order, setOrder] = useState<StoreOrderDetail | null>(null)
  const [store, setStore] = useState<StoreDto | null>(null)

  useDynamicTabTitle(
    order?.orderNo
      ? t('warehouse.invoice.titleWithStore', { storeName: store?.storeName || order.storeCode || t('warehouse.invoice.unknownStore'), orderNo: order.orderNo })
      : t('warehouse.invoice.title'),
  )

  useEffect(() => {
    if (!id) {
      return
    }

    const load = async (showLoading = true) => {
      if (showLoading) {
        setLoading(true)
      }
      try {
        const detail = await getStoreOrderDetail(id)
        if (!detail) {
          if (showLoading) {
            loadedOrderIdRef.current = null
            visibleOrderIdRef.current = null
            setOrder(null)
            setStore(null)
          }
          message.error(t('storeOrders.detail.notFound'))
          return
        }

        loadedOrderIdRef.current = detail.orderGUID || id
        visibleOrderIdRef.current = detail.orderGUID || id
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
        const errorMessage = error instanceof Error ? error.message : t('warehouse.invoice.loadFailed')
        if (showLoading) {
          visibleOrderIdRef.current = null
          setOrder(null)
          setStore(null)
        }
        message.error(errorMessage)
      } finally {
        if (showLoading) {
          setLoading(false)
        }
      }
    }

    const shouldShowInitialLoading = shouldShowStoreOrderDetailInitialLoading({
      requestedOrderId: id,
      loadedOrderId: loadedOrderIdRef.current,
      visibleDetailId: visibleOrderIdRef.current,
    })
    void load(shouldShowInitialLoading)
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
        buildDocumentFileName(
          t('warehouse.invoice.fileName'),
          store?.storeName || order.storeCode,
          order.orderNo || order.orderGUID,
          'pdf',
          {
            unknownStore: t('warehouse.invoice.unknownStore'),
            unknownOrder: t('warehouse.invoice.unknownOrder'),
          },
        ),
        {
          createCanvasContextErrorMessage: t('warehouse.invoice.createPdfCanvasFailed'),
        },
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.invoice.downloadPdfFailed'))
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
      await downloadInvoiceExcel(order, sortedItems, store?.storeName || order.storeCode, t)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.invoice.exportExcelFailed'))
    } finally {
      setExportingExcel(false)
    }
  }

  if (loading) {
    return <Spin size="large" style={{ display: 'block', margin: '120px auto' }} />
  }

  if (!order) {
    return <Empty description={t('storeOrders.detail.orderDataNotFound')} style={{ marginTop: 120 }} />
  }

  const displayStoreName = store?.storeName || order.storeCode || '--'
  const storeAddress = order.storeAddress || store?.address || '--'

  return (
    <div className="store-order-print-page">
      <div className="store-order-print-toolbar no-print">
        <Space wrap>
          <Button icon={<RollbackOutlined />} onClick={() => navigate(-1)}>
            {t('common.back')}
          </Button>
          <Button icon={<FileExcelOutlined />} loading={exportingExcel} onClick={() => void handleExportExcel()}>
            {t('warehouse.invoice.exportExcel')}
          </Button>
          <Button icon={<DownloadOutlined />} loading={downloading} onClick={() => void handleDownloadPdf()}>
            {t('warehouse.invoice.downloadPdf')}
          </Button>
          <Button type="primary" icon={<PrinterOutlined />} onClick={handlePrint}>
            {t('warehouse.invoice.printInvoice')}
          </Button>
        </Space>
      </div>

      <div ref={printRootRef} className="store-order-print-root store-order-print-paper store-order-invoice-paper">
        <div className="store-order-invoice-header">
          <div className="store-order-invoice-logo">
            <Image src="/invoice-logo.png" alt="HOT BARGAIN" preview={false} style={{ maxHeight: 120, objectFit: 'contain' }} />
          </div>
          <div className="store-order-invoice-company">
            <h4>{t('warehouse.invoice.warehouseAddress')}</h4>
            <p>3 Rogilla close Maryland, NSW, 2287, Australia</p>
            <p>
              <strong>A.B.N.</strong> 35 160 589 793
            </p>
            <p>
              <strong>{t('warehouse.invoice.warehouseEmail')}</strong> dong@hotbargain.com.au
            </p>
          </div>
        </div>

        <div className="store-order-invoice-bar">
          <div>{t('warehouse.invoice.invoiceNo', { orderNo: order.orderNo || order.orderGUID })}</div>
          <div>{t('warehouse.invoice.invoiceDate', { date: formatPrintDate(undefined, false) })}</div>
        </div>

        <div className="store-order-invoice-customer">
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">{t('warehouse.invoice.customer')}</span>
            <span>{displayStoreName}</span>
          </div>
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">{t('warehouse.invoice.customerContact')}</span>
            <span>{store?.contactPhone || '-'}</span>
          </div>
          <div className="store-order-invoice-customer-row">
            <span className="store-order-invoice-label">{t('warehouse.invoice.address')}</span>
            <span>{storeAddress}</span>
          </div>
        </div>

        <table className="store-order-invoice-table">
          <thead>
            <tr>
              <th className="col-index">#</th>
              <th className="col-image">{t('column.image')}</th>
              <th className="col-item">{t('column.itemNumber')}</th>
              <th className="col-barcode">{t('column.barcode')}</th>
              <th>{t('column.name')}</th>
              <th className="col-cost">{t('column.cost')}</th>
              <th className="col-qty">{t('column.orderQuantity')}</th>
              <th className="col-qty">{t('column.shipQuantity')}</th>
              <th className="col-subtotal">{t('column.subtotal')}</th>
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
            <h4>{t('warehouse.invoice.paymentDetail')}</h4>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">{t('warehouse.invoice.paymentName')}</span>
              <span>HOT BARGAIN INTERNATIONAL</span>
            </div>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">{t('warehouse.invoice.bsb')}</span>
              <span>12532</span>
            </div>
            <div className="store-order-invoice-payment-row">
              <span className="store-order-invoice-label">{t('warehouse.invoice.account')}</span>
              <span>208034605</span>
            </div>
            <div className="store-order-invoice-disclaimer">
              {t('warehouse.invoice.paymentDisclaimer')}
            </div>
          </div>

          <div className="store-order-invoice-totals">
            <div className="store-order-invoice-total-row">
              <span>{t('warehouse.invoice.subTotal')}</span>
              <span>{formatCurrency(totals.subTotal)}</span>
            </div>
            <div className="store-order-invoice-total-row">
              <span>{t('warehouse.invoice.gst')}</span>
              <span>{formatCurrency(totals.gst)}</span>
            </div>
            <div className="store-order-invoice-total-row">
              <span>{t('warehouse.invoice.freight')}</span>
              <span>{formatCurrency(totals.freight)}</span>
            </div>
            <div className="store-order-invoice-total-row is-grand">
              <span>{t('warehouse.invoice.totalBeforeDiscount')}</span>
              <span>{formatCurrency(totals.total)}</span>
            </div>
          </div>
        </div>

        <div className="store-order-print-footer">
          <div>{t('warehouse.invoice.pageDocument')}</div>
          <div>{t('warehouse.invoice.imageRefNote')}</div>
          <div>{t('warehouse.invoice.dateLabel')} {formatPrintDate(undefined)}</div>
        </div>
      </div>
    </div>
  )
}
