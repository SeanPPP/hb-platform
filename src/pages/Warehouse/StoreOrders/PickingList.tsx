import { DownloadOutlined, PrinterOutlined, RollbackOutlined } from '@ant-design/icons'
import { Button, Empty, Space, Spin, message } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import { getStores } from '../../../services/storeService'
import { getStoreOrderDetail, startPickingStoreOrder } from '../../../services/storeOrderService'
import { StoreOrderFlowStatus } from '../../../types/storeOrder'
import type { StoreDto } from '../../../types/store'
import type { StoreOrderDetail } from '../../../types/storeOrder'
import { useDynamicTabTitle } from '../../../hooks/useDynamicTabTitle'
import { buildDocumentFileName, downloadElementAsPdf, formatCurrency, formatPrintDate } from './printUtils'
import './print.css'

function getInnerPack(orderQty: number, sendQty: number, minQty: number) {
  if (!minQty) {
    return '-'
  }

  const baseQty = orderQty > 0 ? orderQty : sendQty
  if (!baseQty) {
    return '-'
  }

  const packs = baseQty / minQty
  return Number.isInteger(packs) ? `${packs} pk` : `${packs.toFixed(1)} pk`
}

export default function PickingListPage() {
  const route = useStableRouteContext()
  const id = route?.params.id || ''
  const navigate = useNavigate()
  const printRootRef = useRef<HTMLDivElement | null>(null)
  const [loading, setLoading] = useState(false)
  const [printing, setPrinting] = useState(false)
  const [downloading, setDownloading] = useState(false)
  const [order, setOrder] = useState<StoreOrderDetail | null>(null)
  const [store, setStore] = useState<StoreDto | null>(null)

  useDynamicTabTitle(
    order?.orderNo
      ? `配货单 - ${store?.storeName || order.storeCode || '未知分店'} - ${order.orderNo}`
      : '配货单',
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
        message.error(error instanceof Error ? error.message : '加载配货单失败')
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
      message.error(error instanceof Error ? error.message : '打印配货单失败')
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
        buildDocumentFileName('配货单', store?.storeName || order.storeCode, order.orderNo || order.orderGUID, 'pdf'),
      )
    } catch (error) {
      console.error(error)
      message.error(error instanceof Error ? error.message : '下载配货单 PDF 失败')
    } finally {
      setDownloading(false)
    }
  }

  if (loading) {
    return <Spin size="large" style={{ display: 'block', margin: '120px auto' }} />
  }

  if (!order) {
    return <Empty description="未找到订单数据" style={{ marginTop: 120 }} />
  }

  const displayStoreName = store?.storeName || order.storeCode || '--'
  const displayStoreText = store?.storeCode && store.storeCode !== displayStoreName
    ? `${displayStoreName} (${store.storeCode})`
    : displayStoreName

  return (
    <div className="store-order-print-page">
      <div className="store-order-print-toolbar no-print">
        <Space wrap>
          <Button icon={<RollbackOutlined />} onClick={() => navigate(-1)}>
            返回
          </Button>
          <Button icon={<DownloadOutlined />} loading={downloading} onClick={() => void handleDownload()}>
            下载 PDF
          </Button>
          <Button type="primary" icon={<PrinterOutlined />} loading={printing} onClick={() => void handlePrint()}>
            打印配货单
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
                  <div style={{ fontSize: 24, fontWeight: 700 }}>配货单</div>
                  <div
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'auto auto',
                      gap: '5px 20px',
                      fontSize: 14,
                    }}
                  >
                    <div>
                      <strong>订单号：</strong>
                      {order.orderNo || order.orderGUID}
                    </div>
                    <div>
                      <strong>打印时间：</strong>
                      {formatPrintDate(undefined)}
                    </div>
                    <div>
                      <strong>分店：</strong>
                      {displayStoreText}
                    </div>
                    <div>
                      <strong>订货日期：</strong>
                      {formatPrintDate(order.orderDate, false)}
                    </div>
                  </div>
                </div>
              </td>
            </tr>
            <tr>
              <th className="col-index">#</th>
              <th className="col-item">货号</th>
              <th className="col-location">货位</th>
              <th>商品名称</th>
              <th className="col-inner-pack">内包装</th>
              <th className="col-qty">订货数</th>
              <th className="col-send-qty">发货数</th>
              <th className="col-price">进口价</th>
              <th className="col-price">RRP</th>
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
                <td className="col-inner-pack">
                  {getInnerPack(item.quantity, item.allocQuantity || 0, item.minOrderQuantity)}
                </td>
                <td className="col-qty">{item.quantity}</td>
                <td className="col-send-qty">{item.allocQuantity || ''}</td>
                <td className="col-price">{formatCurrency(item.importPrice)}</td>
                <td className="col-price">{item.rrp ? formatCurrency(item.rrp) : '-'}</td>
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
                    <strong>订单号：</strong>
                    {order.orderNo || order.orderGUID}
                  </div>
                  <div>
                    <strong>打印时间：</strong>
                    {formatPrintDate(undefined)}
                  </div>
                  <div>
                    <strong>分店：</strong>
                    {displayStoreText}
                  </div>
                  <div>
                    <strong>订货日期：</strong>
                    {formatPrintDate(order.orderDate, false)}
                  </div>
                </div>
              </td>
            </tr>
          </tfoot>
        </table>

        <div className="store-order-picking-footer">
          {order.remarks ? (
            <div style={{ fontSize: 16, fontWeight: 700, paddingBottom: 10, borderBottom: '1px dashed #ccc' }}>
              备注：{order.remarks}
            </div>
          ) : null}
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <div>总 SKU：{order.totalSKU ?? order.items.length}</div>
            <div>订货总数：{order.totalQuantity}</div>
            <div>发货总数：{order.totalAllocQuantity ?? 0}</div>
          </div>
        </div>
      </div>
    </div>
  )
}
