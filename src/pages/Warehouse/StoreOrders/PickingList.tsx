import { DownloadOutlined, FileExcelOutlined, PrinterOutlined, RollbackOutlined } from '@ant-design/icons'
import { Button, Empty, Space, Spin, message } from 'antd'
import ExcelJS from 'exceljs'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { getStores } from '../../../services/storeService'
import { getStoreOrderDetail, startPickingStoreOrder } from '../../../services/storeOrderService'
import { StoreOrderFlowStatus } from '../../../types/storeOrder'
import type { StoreDto } from '../../../types/store'
import type { StoreOrderDetail } from '../../../types/storeOrder'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { buildDocumentFileName, downloadElementAsPdf, formatCurrency, formatPrintDate } from './printUtils'
import { buildPickingListExcelData, formatInnerPackCount } from './pickingListLogic'
import './print.css'

function formatVolume(value?: number) {
  if (value === undefined || value === null) {
    return '--'
  }

  return value.toFixed(4)
}

export default function PickingListPage() {
  const { t, i18n } = useTranslation()
  const route = useStableRouteContext()
  const id = route?.params.id || ''
  const navigate = useNavigate()
  const printRootRef = useRef<HTMLDivElement | null>(null)
  const [loading, setLoading] = useState(false)
  const [printing, setPrinting] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [exportingExcel, setExportingExcel] = useState(false)
  const [order, setOrder] = useState<StoreOrderDetail | null>(null)
  const [store, setStore] = useState<StoreDto | null>(null)
  // 打印页日期格式跟随当前语言，但只限定本次需求中的中英文区域设置。
  const printLocale = i18n.resolvedLanguage?.toLowerCase().startsWith('en') ? 'en-US' : 'zh-CN'

  useDynamicTabTitle(
    order?.orderNo
      ? t('warehouse.pickingList.titleWithStore', { storeName: store?.storeName || order.storeCode || t('warehouse.pickingList.unknownStore'), orderNo: order.orderNo })
      : t('warehouse.pickingList.title'),
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
          message.error(t('storeOrders.detail.notFound'))
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
        message.error(error instanceof Error ? error.message : t('warehouse.pickingList.loadFailed'))
      } finally {
        setLoading(false)
      }
    }

    void load()
  }, [id])

  const sortedItems = useMemo(() => {
    if (!order?.items) {
      return []
    }

    return [...order.items].sort((left, right) => {
      const leftLocation = left.locationCode || ''
      const rightLocation = right.locationCode || ''
      const locationCompare = leftLocation.localeCompare(rightLocation, 'zh-CN', {
        numeric: true,
        sensitivity: 'base',
      })
      if (locationCompare !== 0) {
        return locationCompare
      }

      return (left.itemNumber || '').localeCompare(right.itemNumber || '', 'zh-CN', {
        numeric: true,
        sensitivity: 'base',
      })
    })
  }, [order?.items])

  const totalOrderVolume = useMemo(() => {
    if (!order) {
      return 0
    }

    if (typeof order.totalOrderVolume === 'number') {
      return order.totalOrderVolume
    }

    if (typeof order.totalVolume === 'number') {
      return order.totalVolume
    }

    return (order.items ?? []).reduce((sum, item) => {
      return sum + (item.orderVolume ?? item.totalVolume ?? ((item.volume ?? 0) * Number(item.quantity ?? 0)))
    }, 0)
  }, [order])

  const handleBeforePrint = async () => {
    if (!order) {
      return true
    }

    if (order.flowStatus === StoreOrderFlowStatus.Submitted) {
      await startPickingStoreOrder(order.orderGUID)
      setOrder((current) =>
        current
          ? {
              ...current,
              flowStatus: StoreOrderFlowStatus.Picking,
            }
          : current,
      )
    }

    return true
  }

  const handlePrint = async () => {
    setPrinting(true)
    try {
      await handleBeforePrint()
      window.print()
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.pickingList.printFailed'))
    } finally {
      setPrinting(false)
    }
  }

  const handleDownload = async () => {
    if (!printRootRef.current || !order) {
      return
    }

    setDownloading(true)
    try {
      await handleBeforePrint()
      await downloadElementAsPdf(
        printRootRef.current,
        buildDocumentFileName(
          t('warehouse.pickingList.fileName'),
          store?.storeName || order.storeCode,
          order.orderNo || order.orderGUID,
          'pdf',
          {
            unknownStore: t('warehouse.pickingList.unknownStore'),
            unknownOrder: t('warehouse.pickingList.unknownOrder'),
          },
        ),
        {
          createCanvasContextErrorMessage: t('warehouse.pickingList.createPdfCanvasFailed'),
        },
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.pickingList.downloadPdfFailed'))
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
      const workbook = new ExcelJS.Workbook()
      const excelData = buildPickingListExcelData(
        order,
        sortedItems,
        {
          sheetName: t('warehouse.pickingList.excel.sheetName'),
          orderNoLabel: t('warehouse.pickingList.excel.orderNo'),
          storeLabel: t('warehouse.pickingList.excel.store'),
          orderDateLabel: t('warehouse.pickingList.excel.orderDate'),
          printTimeLabel: t('warehouse.pickingList.excel.printTime'),
          remarksLabel: t('warehouse.pickingList.excel.remarks'),
          totalSKULabel: t('warehouse.pickingList.excel.totalSKU'),
          totalOrderQtyLabel: t('warehouse.pickingList.excel.totalOrderQty'),
          totalShipQtyLabel: t('warehouse.pickingList.excel.totalShipQty'),
          totalOrderVolumeLabel: t('warehouse.pickingList.excel.totalOrderVolume'),
          detailHeaders: {
            index: '#',
            itemNumber: t('column.itemNumber'),
            location: t('column.location'),
            productName: t('column.productName'),
            importPrice: t('column.importPrice'),
            rrp: t('column.rrp'),
            innerPackCount: t('column.innerPackCount'),
            orderQuantity: t('column.orderQuantity'),
            allocQuantity: t('column.allocQuantity'),
          },
        },
        {
          orderNoText: orderNoText,
          storeText: displayStoreText,
          orderDateText: formatPrintDate(order.orderDate, false, printLocale),
          printTimeText: formatPrintDate(undefined, true, printLocale),
          totalOrderVolumeText: formatVolume(totalOrderVolume),
        },
      )

      const worksheet = workbook.addWorksheet(excelData.sheetName)
      worksheet.columns = [
        { width: 8 },
        { width: 18 },
        { width: 14 },
        { width: 32 },
        { width: 14 },
        { width: 14 },
        { width: 16 },
        { width: 14 },
        { width: 14 },
      ]

      excelData.overviewRows.forEach(([label, value]) => {
        const row = worksheet.addRow([label, value])
        row.getCell(1).font = { bold: true }
      })

      worksheet.addRow([])

      const detailHeaderRow = worksheet.addRow(excelData.detailHeader)
      detailHeaderRow.font = { bold: true }

      excelData.detailRows.forEach((row) => {
        worksheet.addRow(row)
      })

      if (excelData.remarksRow || excelData.totalRows.length > 0) {
        worksheet.addRow([])
      }

      if (excelData.remarksRow) {
        const remarksRow = worksheet.addRow(excelData.remarksRow)
        remarksRow.getCell(1).font = { bold: true }
      }

      excelData.totalRows.forEach(([label, value]) => {
        const row = worksheet.addRow([label, value])
        row.getCell(1).font = { bold: true }
      })

      worksheet.getColumn(5).numFmt = '$#,##0.00'
      worksheet.getColumn(6).numFmt = '$#,##0.00'

      const buffer = await workbook.xlsx.writeBuffer()
      const blob = new Blob([buffer], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      })
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = buildDocumentFileName(
        t('warehouse.pickingList.fileName'),
        store?.storeName || order.storeCode,
        order.orderNo || order.orderGUID,
        'xlsx',
        {
          unknownStore: t('warehouse.pickingList.unknownStore'),
          unknownOrder: t('warehouse.pickingList.unknownOrder'),
        },
      )
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      URL.revokeObjectURL(url)
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : t('warehouse.pickingList.exportExcelFailed'))
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

  const displayStoreName = store?.storeName || order.storeCode || t('warehouse.pickingList.unknownStore')
  const displayStoreText = store?.storeCode && store.storeCode !== displayStoreName
    ? `${displayStoreName} (${store.storeCode})`
    : displayStoreName
  const orderNoText = order.orderNo || order.orderGUID || t('warehouse.pickingList.unknownOrder')

  return (
    <div className="store-order-print-page">
      <div className="store-order-print-toolbar no-print">
        <Space wrap>
          <Button icon={<RollbackOutlined />} onClick={() => navigate(-1)}>
            {t('common.back')}
          </Button>
          <Button icon={<FileExcelOutlined />} loading={exportingExcel} onClick={() => void handleExportExcel()}>
            {t('warehouse.pickingList.exportExcel')}
          </Button>
          <Button icon={<DownloadOutlined />} loading={downloading} onClick={() => void handleDownload()}>
            {t('warehouse.pickingList.downloadPdf')}
          </Button>
          <Button type="primary" icon={<PrinterOutlined />} loading={printing} onClick={() => void handlePrint()}>
            {t('warehouse.pickingList.printPickingList')}
          </Button>
        </Space>
      </div>

      <div ref={printRootRef} className="store-order-print-root store-order-print-paper store-order-picking-paper">
        <table className="store-order-picking-table">
          <thead>
            <tr>
              <td colSpan={9} style={{ border: 'none', padding: 0 }}>
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    marginBottom: 10,
                    paddingBottom: 10,
                    borderBottom: '2px solid #000',
                  }}
                >
                  <div style={{ fontSize: 24, fontWeight: 700 }}>{t('warehouse.pickingList.title')}</div>
                  <div
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'auto auto',
                      gap: '5px 20px',
                      fontSize: 14,
                    }}
                  >
                    <div>
                      <strong>{t('warehouse.pickingList.orderNoLabel')}</strong>
                      {orderNoText}
                    </div>
                    <div>
                      <strong>{t('warehouse.pickingList.printTime')}</strong>
                      {formatPrintDate(undefined, true, printLocale)}
                    </div>
                    <div>
                      <strong>{t('warehouse.pickingList.storeLabel')}</strong>
                      {displayStoreText}
                    </div>
                    <div>
                      <strong>{t('warehouse.pickingList.orderDate')}</strong>
                      {formatPrintDate(order.orderDate, false, printLocale)}
                    </div>
                  </div>
                </div>
              </td>
            </tr>
            <tr>
              <th className="col-index">#</th>
              <th className="col-item">{t('column.itemNumber')}</th>
              <th className="col-location">{t('column.location')}</th>
              <th>{t('column.productName')}</th>
              <th className="col-price">{t('column.importPrice')}</th>
              <th className="col-price">{t('column.rrp')}</th>
              <th className="col-inner-pack">{t('column.innerPackCount')}</th>
              <th className="col-qty">{t('column.orderQuantity')}</th>
              <th className="col-send-qty">{t('column.allocQuantity')}</th>
            </tr>
          </thead>
          <tbody>
            {sortedItems.map((item, index) => (
              <tr key={item.detailGUID}>
                <td className="col-index">{index + 1}</td>
                <td className="col-item">{item.itemNumber || '--'}</td>
                <td className="col-location">{item.locationCode || '--'}</td>
                <td>
                  <div className="store-order-picking-name">{item.productName || '--'}</div>
                </td>
                <td className="col-price">{formatCurrency(item.importPrice)}</td>
                <td className="col-price">{item.rrp !== undefined && item.rrp !== null ? formatCurrency(item.rrp) : ''}</td>
                <td className="col-inner-pack">
                  {/* 内包装数量严格使用订货数量计算，不再回退发货数。 */}
                  {formatInnerPackCount(item.quantity, item.minOrderQuantity)}
                </td>
                <td className="col-qty">{item.quantity}</td>
                <td className="col-send-qty">{item.allocQuantity || ''}</td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr>
              <td colSpan={9} style={{ border: 'none', padding: 0 }}>
                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    marginTop: 10,
                    paddingTop: 10,
                    borderTop: '2px solid #000',
                    fontSize: 12,
                  }}
                >
                  <div>
                    <strong>{t('warehouse.pickingList.orderNoLabel')}</strong>
                    {orderNoText}
                  </div>
                  <div>
                    <strong>{t('warehouse.pickingList.printTime')}</strong>
                    {formatPrintDate(undefined, true, printLocale)}
                  </div>
                  <div>
                    <strong>{t('warehouse.pickingList.storeLabel')}</strong>
                    {displayStoreText}
                  </div>
                  <div>
                    <strong>{t('warehouse.pickingList.orderDate')}</strong>
                    {formatPrintDate(order.orderDate, false, printLocale)}
                  </div>
                </div>
              </td>
            </tr>
          </tfoot>
        </table>

        <div className="store-order-picking-footer">
          {order.remarks ? (
            <div style={{ fontSize: 16, fontWeight: 700, paddingBottom: 10, borderBottom: '1px dashed #ccc' }}>
              {t('warehouse.pickingList.remarks', { remarks: order.remarks })}
            </div>
          ) : null}
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <div>{t('warehouse.pickingList.totalSKU', { count: order.totalSKU ?? order.items.length })}</div>
            <div>{t('warehouse.pickingList.totalOrderQty', { count: order.totalQuantity })}</div>
            <div>{t('warehouse.pickingList.totalShipQty', { count: order.totalAllocQuantity ?? 0 })}</div>
            <div>{t('warehouse.pickingList.totalOrderVolume', { volume: formatVolume(totalOrderVolume) })}</div>
          </div>
        </div>
      </div>
    </div>
  )
}
