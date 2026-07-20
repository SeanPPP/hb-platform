import { ArrowLeftOutlined, DownloadOutlined, StopOutlined } from '@ant-design/icons'
import { Alert, App, Button, Card, Col, ConfigProvider, DatePicker, Descriptions, Empty, Image, Input, Modal, Popconfirm, Row, Select, Space, Statistic, Table, Tabs, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import enUS from 'antd/locale/en_US'
import zhCN from 'antd/locale/zh_CN'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import dayjs, { type Dayjs } from 'dayjs'
import PageContainer from '../../../components/PageContainer'
import { useStableRouteContext } from '../../../hooks/useStableRouteContext'
import {
  cancelPreorderActivation,
  closePreorderActivation,
  downloadPreorderActivationExport,
  getAdminPreorderActivation,
  getPreorderActivationStatistics,
  isPreorderActivationArrivalDateChangedError,
  isPreorderActivationStoresChangedError,
  isPreorderStatusTransitionConflictError,
  updatePreorderActivationStores,
  updatePreorderActivationEstimatedArrivalDate,
  updatePreorderOrderStatus,
} from '../../../services/preorderService'
import { getStores } from '../../../services/storeService'
import type { PreorderActivationDetail, PreorderActivationStatistics, PreorderMatrixCell, PreorderMatrixRow, PreorderOrderStatus, PreorderProductStatistic, PreorderWarehouseOrderSummary } from '../../../types/preorder'
import {
  beginActivationDetailRequest,
  createActivationDetailRequestGuard,
  getActivationActionAvailability,
  invalidateActivationDetailRequest,
  isCurrentActivationDetailRequest,
  isPreorderReturnContextCurrent,
} from './activationDetailRequestGuard'
import { getActivationProductStores } from './activationProductStores'
import { getActivationStoreChanges, mergeActivationStoreOptions } from './activationStoreSelection'
import { beginModalRequest, createModalRequestGuard, invalidateModalRequest, isCurrentModalRequest } from './modalRequestGuard'
import { getPreorderDateDisplay } from '../../ShopPreorder/preorderDate'
import './styles.css'

const { Text } = Typography

function getOrderStatusOptions(status: PreorderOrderStatus, getLabel: (status: PreorderOrderStatus) => string) {
  const targets = status === 'Submitted'
    ? ['Submitted', 'ReturnedForRevision', 'Processing', 'Completed', 'Cancelled']
    : status === 'NoDemand'
      ? ['NoDemand', 'ReturnedForRevision']
    : status === 'Processing'
      ? ['Processing', 'Completed', 'Cancelled']
      : [status]
  return targets.map((value) => ({ value, label: getLabel(value as PreorderOrderStatus) }))
}

export default function PreorderActivationDetailPage() {
  // 后台页由 AdminLayout 手工渲染，需从稳定路由上下文读取动态参数。
  const activationGuid = useStableRouteContext()?.params.activationGuid ?? ''
  const navigate = useNavigate()
  const { message, modal } = App.useApp()
  const { t, i18n } = useTranslation()
  const [detail, setDetail] = useState<PreorderActivationDetail | null>(null)
  const [statistics, setStatistics] = useState<PreorderActivationStatistics | null>(null)
  const [loading, setLoading] = useState(false)
  const [extendOpen, setExtendOpen] = useState(false)
  const [nextEndAt, setNextEndAt] = useState<Dayjs | null>(null)
  const [selectedProduct, setSelectedProduct] = useState<PreorderProductStatistic | null>(null)
  const [storeEditorOpen, setStoreEditorOpen] = useState(false)
  const [storeOptions, setStoreOptions] = useState<ReturnType<typeof mergeActivationStoreOptions>>([])
  const [selectedStoreGuids, setSelectedStoreGuids] = useState<string[]>([])
  const [storeOptionsLoading, setStoreOptionsLoading] = useState(false)
  const [storeOptionsLoadFailed, setStoreOptionsLoadFailed] = useState(false)
  const [storeSaving, setStoreSaving] = useState(false)
  const [arrivalEditorOpen, setArrivalEditorOpen] = useState(false)
  const [nextEstimatedArrivalDate, setNextEstimatedArrivalDate] = useState<Dayjs | null>(null)
  const [arrivalSaving, setArrivalSaving] = useState(false)
  const requestGuardRef = useRef(createActivationDetailRequestGuard())
  const storeEditorRequestGuardRef = useRef(createModalRequestGuard())
  const arrivalEditorRequestGuardRef = useRef(createModalRequestGuard())
  const returnConfirmDestroyRef = useRef<(() => void) | null>(null)
  const storeConfirmDestroyRef = useRef<(() => void) | null>(null)
  const storeConfirmSessionRef = useRef<number | null>(null)
  const nextStoreConfirmSessionRef = useRef(0)
  const arrivalSavingRef = useRef(false)
  const currentActivationGuidRef = useRef(activationGuid)
  currentActivationGuidRef.current = activationGuid
  const dateTimeFormatter = useMemo(
    () => new Intl.DateTimeFormat(i18n.resolvedLanguage || i18n.language, { dateStyle: 'medium', timeStyle: 'short' }),
    [i18n.language, i18n.resolvedLanguage],
  )
  const antdLocale = i18n.resolvedLanguage === 'en' ? enUS : zhCN

  const closeStoreEditor = useCallback(() => {
    // 关闭或切换批次时使门店列表/保存请求失效，慢响应不得回写其他批次。
    storeConfirmDestroyRef.current?.()
    storeConfirmDestroyRef.current = null
    storeConfirmSessionRef.current = null
    invalidateModalRequest(storeEditorRequestGuardRef.current)
    setStoreEditorOpen(false)
    setStoreOptions([])
    setSelectedStoreGuids([])
    setStoreOptionsLoading(false)
    setStoreOptionsLoadFailed(false)
    setStoreSaving(false)
  }, [])

  const closeArrivalEditor = useCallback(() => {
    invalidateModalRequest(arrivalEditorRequestGuardRef.current)
    arrivalSavingRef.current = false
    setArrivalEditorOpen(false)
    setNextEstimatedArrivalDate(null)
    setArrivalSaving(false)
  }, [])

  const load = useCallback(async () => {
    returnConfirmDestroyRef.current?.()
    returnConfirmDestroyRef.current = null
    closeStoreEditor()
    closeArrivalEditor()
    if (!activationGuid) {
      // 路由参数缺失时立即失效旧请求并清空页面，禁止请求空批次地址。
      invalidateActivationDetailRequest(requestGuardRef.current)
      setDetail(null)
      setStatistics(null)
      setExtendOpen(false)
      setNextEndAt(null)
      setSelectedProduct(null)
      setLoading(false)
      return
    }
    const requestToken = beginActivationDetailRequest(requestGuardRef.current, activationGuid)
    // 路由切换时立即清空旧批次，操作按钮不会短暂指向新路由。
    setDetail(null)
    setStatistics(null)
    setExtendOpen(false)
    setNextEndAt(null)
    setSelectedProduct(null)
    setLoading(true)
    try {
      const [nextDetail, nextStatistics] = await Promise.all([
        getAdminPreorderActivation(activationGuid, requestToken.signal),
        getPreorderActivationStatistics(activationGuid, requestToken.signal),
      ])
      if (!isCurrentActivationDetailRequest(requestGuardRef.current, requestToken, currentActivationGuidRef.current)) return
      setDetail(nextDetail)
      setStatistics(nextStatistics)
    } catch {
      if (isCurrentActivationDetailRequest(requestGuardRef.current, requestToken, currentActivationGuidRef.current)) {
        message.error(t('warehouse.preorders.activationDetail.loadFailed'))
      }
    } finally {
      if (isCurrentActivationDetailRequest(requestGuardRef.current, requestToken, currentActivationGuidRef.current)) {
        setLoading(false)
      }
    }
  }, [activationGuid, closeArrivalEditor, closeStoreEditor, message, t])

  useEffect(() => {
    void load()
    return () => {
      returnConfirmDestroyRef.current?.()
      returnConfirmDestroyRef.current = null
      invalidateActivationDetailRequest(requestGuardRef.current)
      closeStoreEditor()
      closeArrivalEditor()
    }
  }, [closeArrivalEditor, closeStoreEditor, load])

  const changeStatus = async (row: PreorderWarehouseOrderSummary, status: PreorderOrderStatus) => {
    const targetActivationGuid = activationGuid
    if (detail?.activationGuid !== targetActivationGuid) return
    if (status === 'ReturnedForRevision') {
      let returnReason = ''
      let confirmation: ReturnType<typeof modal.confirm>
      confirmation = modal.confirm({
        title: t('warehouse.preorders.activationDetail.returnTitle'),
        content: (
          <Input.TextArea
            autoFocus
            rows={4}
            maxLength={1000}
            showCount
            placeholder={t('warehouse.preorders.activationDetail.returnReasonPlaceholder')}
            onChange={(event) => {
              returnReason = event.target.value
              confirmation.update({ okButtonProps: { danger: true, disabled: !returnReason.trim() } })
            }}
          />
        ),
        okText: t('warehouse.preorders.activationDetail.confirmReturn'),
        cancelText: t('common.cancel'),
        okButtonProps: { danger: true, disabled: true },
        onOk: async () => {
          if (!isPreorderReturnContextCurrent(
            currentActivationGuidRef.current,
            targetActivationGuid,
            detail?.activationGuid,
          )) {
            confirmation.destroy()
            returnConfirmDestroyRef.current = null
            return
          }
          try {
            await updatePreorderOrderStatus(row.orderGuid, status, returnReason.trim(), row.status, row.draftRevision)
            if (currentActivationGuidRef.current !== targetActivationGuid) return
            returnConfirmDestroyRef.current = null
            message.success(t('warehouse.preorders.activationDetail.statusUpdated'))
            await load()
          } catch (error) {
            if (currentActivationGuidRef.current === targetActivationGuid) {
              if (isPreorderStatusTransitionConflictError(error)) {
                confirmation.destroy()
                returnConfirmDestroyRef.current = null
                message.warning(t('warehouse.preorders.activationDetail.statusConflictRefreshed'))
                await load()
                return
              }
              message.error(t('warehouse.preorders.activationDetail.statusUpdateFailed'))
            }
            throw error
          }
        },
        onCancel: () => {
          returnConfirmDestroyRef.current = null
        },
      })
      returnConfirmDestroyRef.current = confirmation.destroy
      return
    }
    try {
      await updatePreorderOrderStatus(row.orderGuid, status, undefined, row.status, row.draftRevision)
      if (currentActivationGuidRef.current !== targetActivationGuid) return
      message.success(t('warehouse.preorders.activationDetail.statusUpdated'))
      await load()
    } catch (error) {
      if (currentActivationGuidRef.current === targetActivationGuid) {
        if (isPreorderStatusTransitionConflictError(error)) {
          message.warning(t('warehouse.preorders.activationDetail.statusConflictRefreshed'))
          await load()
          return
        }
        message.error(t('warehouse.preorders.activationDetail.statusUpdateFailed'))
      }
    }
  }

  const orderColumns: ColumnsType<PreorderWarehouseOrderSummary> = [
    { title: t('warehouse.preorders.activationDetail.orderNumber'), dataIndex: 'orderNo', width: 160 },
    { title: t('warehouse.preorders.activationDetail.store'), width: 180, render: (_, row) => <><Text strong>{row.storeName || row.storeCode}</Text><br /><Text type="secondary">{row.storeCode}</Text></> },
    { title: t('warehouse.preorders.status'), dataIndex: 'status', width: 170, render: (value: PreorderOrderStatus, row) => <Select size="small" value={value} style={{ width: 150 }} onChange={(next) => void changeStatus(row, next)} options={getOrderStatusOptions(value, (status) => t(`warehouse.preorders.orderStatus.${status}`))} disabled={detail?.activationGuid !== activationGuid || (value !== 'Submitted' && value !== 'NoDemand' && value !== 'Processing')} /> },
    { title: 'SKU', dataIndex: 'skuCount', width: 75, align: 'right' },
    { title: t('warehouse.preorders.activationDetail.totalQuantity'), dataIndex: 'totalQuantity', width: 90, align: 'right' },
    { title: t('warehouse.preorders.activationDetail.importAmount'), dataIndex: 'totalImportAmount', width: 110, align: 'right', render: (value) => `$${(value ?? 0).toFixed(2)}` },
    { title: t('warehouse.preorders.activationDetail.submittedAt'), dataIndex: 'submittedAt', width: 180, render: (value) => value ? dateTimeFormatter.format(new Date(value)) : '--' },
    { title: t('warehouse.preorders.activationDetail.submittedBy'), dataIndex: 'submittedBy', width: 130 },
  ]

  const { hasCurrentDetail, canAdjust, canClose } = getActivationActionAvailability(
    detail?.activationGuid,
    activationGuid,
    detail?.status,
  )
  const productRows = useMemo(() => {
    const snapshotMap = new Map((detail?.items ?? []).map((item) => [item.productCode, item]))
    return (statistics?.products ?? []).map((item) => {
      const snapshot = snapshotMap.get(item.productCode)
      return {
        ...item,
        productImage: snapshot?.productImage,
        importPrice: snapshot?.importPrice ?? 0,
        retailPrice: snapshot?.retailPrice ?? 0,
        sortOrder: snapshot?.sortOrder ?? 0,
      }
    })
  }, [detail?.items, statistics?.products])
  const selectedProductStores = useMemo(
    () => selectedProduct
      ? getActivationProductStores(statistics?.storeProductQuantities ?? [], selectedProduct.activationItemGuid)
      : [],
    [selectedProduct, statistics?.storeProductQuantities],
  )
  const matrixRows = useMemo<PreorderMatrixRow[]>(() => {
    const quantities = statistics?.storeProductQuantities ?? []
    const storeCellsByItem = new Map<string, Map<string, PreorderMatrixCell>>()
    quantities.forEach((quantity) => {
      let storeCells = storeCellsByItem.get(quantity.activationItemGuid)
      if (!storeCells) {
        storeCells = new Map()
        storeCellsByItem.set(quantity.activationItemGuid, storeCells)
      }
      storeCells.set(quantity.storeCode, {
        storeCode: quantity.storeCode,
        storeName: quantity.storeName,
        packCount: quantity.packCount,
        quantity: quantity.orderedQuantity,
      })
    })

    return (detail?.items ?? []).map((item) => ({
      activationItemGuid: item.activationItemGuid,
      productCode: item.productCode,
      itemNumber: item.itemNumber,
      productName: item.productName,
      minimumOrderQuantity: item.minimumOrderQuantity,
      storeCells: storeCellsByItem.get(item.activationItemGuid) ?? new Map(),
    }))
  }, [detail?.items, statistics?.storeProductQuantities])
  const matrixStores = useMemo(() => {
    const stores = new Map<string, string>()
    ;(detail?.stores ?? []).forEach((store) => stores.set(store.storeCode, store.storeName || store.storeCode))
    return [...stores.entries()].map(([storeCode, storeName]) => ({ storeCode, storeName }))
  }, [detail?.stores])
  const matrixColumns = useMemo<ColumnsType<PreorderMatrixRow>>(() => [
    { title: t('warehouse.preorders.itemNumber'), dataIndex: 'itemNumber', width: 130, fixed: 'left' },
    { title: t('warehouse.preorders.productName'), dataIndex: 'productName', width: 220, fixed: 'left', ellipsis: true },
    { title: 'MOQ', dataIndex: 'minimumOrderQuantity', width: 70, align: 'right' },
    ...matrixStores.map((store) => ({
      title: store.storeName,
      key: store.storeCode,
      width: 120,
      align: 'right' as const,
      render: (_: unknown, row: PreorderMatrixRow) => {
        const cell = row.storeCells.get(store.storeCode)
        return cell?.quantity ? <><Text strong>{cell.quantity}</Text><br /><Text type="secondary">{t('warehouse.preorders.activationDetail.packCount', { count: cell.packCount })}</Text></> : '--'
      },
    })),
  ], [matrixStores, t])

  const extendActivation = async () => {
    const targetActivationGuid = activationGuid
    if (!detail || !hasCurrentDetail || !nextEndAt || !nextEndAt.isAfter(dayjs(detail.endAtUtc))) {
      message.warning(t('warehouse.preorders.activationDetail.newEndAfterCurrent'))
      return
    }
    try {
      await closePreorderActivation(targetActivationGuid, nextEndAt.toISOString())
      if (currentActivationGuidRef.current !== targetActivationGuid) return
      message.success(t('warehouse.preorders.activationDetail.extended'))
      setExtendOpen(false)
      await load()
    } catch {
      if (currentActivationGuidRef.current === targetActivationGuid) {
        message.error(t('warehouse.preorders.activationDetail.extendFailed'))
      }
    }
  }

  const closeNow = async () => {
    const targetActivationGuid = activationGuid
    if (!hasCurrentDetail) return
    try {
      await closePreorderActivation(targetActivationGuid)
      if (currentActivationGuidRef.current !== targetActivationGuid) return
      message.success(t('warehouse.preorders.activationDetail.closed'))
      await load()
    } catch {
      if (currentActivationGuidRef.current === targetActivationGuid) {
        message.error(t('warehouse.preorders.activationDetail.closeFailed'))
      }
    }
  }

  const cancelActivation = async () => {
    const targetActivationGuid = activationGuid
    if (!hasCurrentDetail) return
    try {
      await cancelPreorderActivation(targetActivationGuid)
      if (currentActivationGuidRef.current !== targetActivationGuid) return
      message.success(t('warehouse.preorders.activationDetail.cancelled'))
      await load()
    } catch {
      if (currentActivationGuidRef.current === targetActivationGuid) {
        message.error(t('warehouse.preorders.activationDetail.cancelFailed'))
      }
    }
  }

  const storeChanges = useMemo(
    () => getActivationStoreChanges((detail?.stores ?? []).map((store) => store.storeGuid), selectedStoreGuids),
    [detail?.stores, selectedStoreGuids],
  )

  const openStoreEditor = async () => {
    const targetActivationGuid = activationGuid
    const currentDetail = detail
    if (
      currentDetail?.activationGuid !== targetActivationGuid
      || (currentDetail.status !== 'Scheduled' && currentDetail.status !== 'Active')
    ) return

    const requestToken = beginModalRequest(storeEditorRequestGuardRef.current)
    setStoreEditorOpen(true)
    setStoreOptions([])
    setSelectedStoreGuids(currentDetail.stores.map((store) => store.storeGuid))
    setStoreOptionsLoadFailed(false)
    setStoreOptionsLoading(true)
    try {
      const activeStores: PreorderActivationDetail['stores'] = []
      let page = 1
      let total = 0
      do {
        const result = await getStores({ page, pageSize: 100, isActive: true, sortField: 'storeName', sortOrder: 'asc' })
        if (!isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
          || currentActivationGuidRef.current !== targetActivationGuid) return
        activeStores.push(...result.items.map((store) => ({
          storeGuid: store.storeGUID,
          storeCode: store.storeCode,
          storeName: store.storeName,
        })))
        total = result.total
        if (!result.items.length) break
        page += 1
      } while (activeStores.length < total)
      if (!isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
        || currentActivationGuidRef.current !== targetActivationGuid) return
      setStoreOptions(mergeActivationStoreOptions(activeStores, currentDetail.stores))
    } catch {
      if (isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
        && currentActivationGuidRef.current === targetActivationGuid) {
        setStoreOptionsLoadFailed(true)
        message.error(t('warehouse.preorders.activationDetail.storeOptionsLoadFailed'))
      }
    } finally {
      if (isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
        && currentActivationGuidRef.current === targetActivationGuid) {
        setStoreOptionsLoading(false)
      }
    }
  }

  const saveActivationStores = () => {
    // 同一编辑会话只能存在一个确认框，避免快速重复点击遗留旧批次确认。
    if (storeConfirmSessionRef.current !== null) return
    const targetActivationGuid = activationGuid
    const currentDetail = detail
    const nextStoreGuids = [...selectedStoreGuids]
    if (currentDetail?.activationGuid !== targetActivationGuid || storeOptionsLoadFailed || storeOptionsLoading) return
    if (!nextStoreGuids.length) {
      message.warning(t('warehouse.preorders.activationDetail.storeRequired'))
      return
    }
    const expectedStoreGuids = currentDetail.stores.map((store) => store.storeGuid)
    const changes = getActivationStoreChanges(expectedStoreGuids, nextStoreGuids)
    const confirmSession = nextStoreConfirmSessionRef.current + 1
    nextStoreConfirmSessionRef.current = confirmSession
    storeConfirmSessionRef.current = confirmSession
    const releaseConfirmation = () => {
      // 旧确认框的延迟回调不得释放后来批次创建的新确认锁。
      if (storeConfirmSessionRef.current !== confirmSession) return
      storeConfirmSessionRef.current = null
      storeConfirmDestroyRef.current = null
    }
    let confirmation: ReturnType<typeof modal.confirm>
    confirmation = modal.confirm({
      title: t('warehouse.preorders.activationDetail.storeChangeConfirmTitle'),
      content: t('warehouse.preorders.activationDetail.storeChangeConfirmContent', changes),
      okText: t('warehouse.preorders.activationDetail.confirmStoreChange'),
      cancelText: t('common.cancel'),
      okButtonProps: { danger: changes.removedCount > 0 },
      onOk: async () => {
        if (storeConfirmSessionRef.current !== confirmSession
          || currentActivationGuidRef.current !== targetActivationGuid) {
          confirmation.destroy()
          releaseConfirmation()
          return
        }
        // 保存期间锁住二次确认，避免用户关闭后误以为服务端没有提交。
        confirmation.update({
          cancelButtonProps: { disabled: true },
          closable: false,
          keyboard: false,
          maskClosable: false,
        })
        const requestToken = beginModalRequest(storeEditorRequestGuardRef.current)
        setStoreSaving(true)
        try {
          await updatePreorderActivationStores(targetActivationGuid, { expectedStoreGuids, storeGuids: nextStoreGuids }, requestToken.signal)
          if (!isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
            || currentActivationGuidRef.current !== targetActivationGuid) return
          message.success(t('warehouse.preorders.activationDetail.storesUpdated'))
          closeStoreEditor()
          await load()
        } catch (error) {
          if (!isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)
            || currentActivationGuidRef.current !== targetActivationGuid) return
          if (isPreorderActivationStoresChangedError(error)) {
            message.warning(t('warehouse.preorders.activationDetail.storesConflictRefreshed'))
            closeStoreEditor()
            await load()
            return
          }
          message.error(t('warehouse.preorders.activationDetail.storeUpdateFailed'))
          confirmation.update({
            cancelButtonProps: { disabled: false },
            closable: true,
            keyboard: true,
            maskClosable: true,
          })
        } finally {
          if (isCurrentModalRequest(storeEditorRequestGuardRef.current, requestToken)) setStoreSaving(false)
          releaseConfirmation()
        }
      },
      onCancel: releaseConfirmation,
    })
    storeConfirmDestroyRef.current = confirmation.destroy
  }

  const openArrivalEditor = () => {
    if (!canAdjust || detail?.activationGuid !== activationGuid) return
    invalidateModalRequest(arrivalEditorRequestGuardRef.current)
    const estimatedArrivalDate = getPreorderDateDisplay(detail.estimatedArrivalDate)
    setNextEstimatedArrivalDate(estimatedArrivalDate ? dayjs(estimatedArrivalDate) : null)
    setArrivalSaving(false)
    setArrivalEditorOpen(true)
  }

  const saveEstimatedArrivalDate = async () => {
    const targetActivationGuid = activationGuid
    const currentDetail = detail
    if (!canAdjust || currentDetail?.activationGuid !== targetActivationGuid || arrivalSavingRef.current) return
    arrivalSavingRef.current = true
    const requestToken = beginModalRequest(arrivalEditorRequestGuardRef.current)
    // DateOnly 直接格式化为年月日，不能调用 toISOString 经过时区换算。
    const estimatedArrivalDate = nextEstimatedArrivalDate?.format('YYYY-MM-DD') ?? null
    setArrivalSaving(true)
    try {
      await updatePreorderActivationEstimatedArrivalDate(targetActivationGuid, {
        expectedEstimatedArrivalDate: getPreorderDateDisplay(currentDetail.estimatedArrivalDate),
        estimatedArrivalDate,
      }, requestToken.signal)
      if (!isCurrentModalRequest(arrivalEditorRequestGuardRef.current, requestToken)
        || currentActivationGuidRef.current !== targetActivationGuid) return
      message.success(t('warehouse.preorders.activationDetail.arrivalDateUpdated'))
      closeArrivalEditor()
      await load()
    } catch (error) {
      if (!isCurrentModalRequest(arrivalEditorRequestGuardRef.current, requestToken)
        || currentActivationGuidRef.current !== targetActivationGuid) return
      if (isPreorderActivationArrivalDateChangedError(error)) {
        message.warning(t('warehouse.preorders.activationDetail.arrivalDateConflictRefreshed'))
        closeArrivalEditor()
        await load()
        return
      }
      message.error(t('warehouse.preorders.activationDetail.arrivalDateUpdateFailed'))
    } finally {
      if (isCurrentModalRequest(arrivalEditorRequestGuardRef.current, requestToken)) {
        arrivalSavingRef.current = false
        setArrivalSaving(false)
      }
    }
  }
  return (
    <ConfigProvider locale={antdLocale}>
      <PageContainer
      title={detail ? t('warehouse.preorders.activationDetail.periodTitle', { name: detail.templateName, sequence: detail.sequenceNumber }) : t('warehouse.preorders.activationDetail.title')}
      extra={<Space wrap><Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/warehouse/preorders')}>{t('warehouse.preorders.activationDetail.back')}</Button><Button icon={<DownloadOutlined />} disabled={!hasCurrentDetail} onClick={() => void downloadPreorderActivationExport(activationGuid).catch(() => message.error(t('warehouse.preorders.activationDetail.exportFailed')))}>{t('warehouse.preorders.activationDetail.exportExcel')}</Button>{canAdjust ? <Button onClick={openArrivalEditor}>{t('warehouse.preorders.activationDetail.changeArrivalDate')}</Button> : null}{canAdjust ? <Button onClick={() => void openStoreEditor()}>{t('warehouse.preorders.activationDetail.changeStores')}</Button> : null}{canAdjust && detail ? <Button onClick={() => { setNextEndAt(dayjs(detail.endAtUtc).add(1, 'day')); setExtendOpen(true) }}>{t('warehouse.preorders.activationDetail.extend')}</Button> : null}{canClose ? <Popconfirm title={t('warehouse.preorders.activationDetail.closeConfirm')} onConfirm={() => void closeNow()}><Button>{t('warehouse.preorders.activationDetail.close')}</Button></Popconfirm> : null}{canAdjust ? <Popconfirm title={t('warehouse.preorders.activationDetail.cancelConfirm')} onConfirm={() => void cancelActivation()}><Button danger icon={<StopOutlined />}>{t('warehouse.preorders.activationDetail.cancel')}</Button></Popconfirm> : null}</Space>}
    >
      <Card loading={loading} className="preorder-admin-card">
        {detail ? <Descriptions size="small" column={{ xs: 1, md: 3 }} items={[
          { key: 'number', label: t('warehouse.preorders.activationNumber'), children: detail.activationNumber },
          { key: 'status', label: t('warehouse.preorders.status'), children: <Tag color={detail.status === 'Active' ? 'processing' : 'default'}>{t(`warehouse.preorders.activationStatus.${detail.status}`)}</Tag> },
          { key: 'estimatedArrivalDate', label: t('warehouse.preorders.estimatedArrivalDate'), children: getPreorderDateDisplay(detail.estimatedArrivalDate) ?? '--' },
          { key: 'time', label: t('warehouse.preorders.effectiveTime'), children: `${dateTimeFormatter.format(new Date(detail.startAtUtc))} — ${dateTimeFormatter.format(new Date(detail.endAtUtc))}` },
        ]} /> : null}
      </Card>
      <Row gutter={[12, 12]} className="preorder-stat-row">
        <Col xs={12} md={4}><Card><Statistic title={t('warehouse.preorders.activationDetail.targetStores')} value={statistics?.targetStoreCount ?? detail?.targetStoreCount ?? 0} /></Card></Col>
        <Col xs={12} md={4}><Card><Statistic title={t('warehouse.preorders.activationDetail.ordered')} value={(statistics?.submittedCount ?? 0) + (statistics?.processingCount ?? 0) + (statistics?.completedCount ?? 0)} /></Card></Col>
        <Col xs={12} md={4}><Card><Statistic title={t('warehouse.preorders.activationDetail.noDemand')} value={statistics?.noDemandCount ?? 0} /></Card></Col>
        <Col xs={12} md={4}><Card><Statistic title={t('warehouse.preorders.activationDetail.pending')} value={statistics?.pendingCount ?? detail?.pendingCount ?? 0} valueStyle={{ color: (statistics?.pendingCount ?? detail?.pendingCount) ? '#cf1322' : undefined }} /></Card></Col>
        <Col xs={12} md={4}><Card><Statistic title={t('warehouse.preorders.activationDetail.cancelledCount')} value={statistics?.cancelledCount ?? 0} /></Card></Col>
      </Row>
      <Card className="preorder-admin-card">
        <Tabs items={[
          { key: 'products', label: t('warehouse.preorders.activationDetail.productSummary'), children: <Table size="small" rowKey="productCode" pagination={false} scroll={{ x: 900 }} dataSource={productRows} columns={[
            { title: t('warehouse.preorders.image'), dataIndex: 'productImage', width: 60, render: (src) => <Image src={src} width={36} height={36} style={{ objectFit: 'contain' }} /> },
            { title: t('warehouse.preorders.itemNumber'), dataIndex: 'itemNumber', width: 140 },
            { title: t('warehouse.preorders.name'), dataIndex: 'productName', ellipsis: true },
            { title: 'MOQ', dataIndex: 'minimumOrderQuantity', width: 70, align: 'right' },
            {
              title: t('warehouse.preorders.activationDetail.orderedStores'),
              dataIndex: 'orderedStoreCount',
              width: 90,
              align: 'right',
              render: (value: number, row) => value > 0
                ? <Button type="link" size="small" style={{ height: 'auto', padding: 0 }} aria-label={t('warehouse.preorders.activationDetail.viewStoreDetails', { itemNumber: row.itemNumber, count: value })} onClick={() => setSelectedProduct(row)}>{value}</Button>
                : value,
            },
            { title: t('warehouse.preorders.activationDetail.totalPacks'), dataIndex: 'totalPackCount', width: 90, align: 'right' },
            { title: t('warehouse.preorders.activationDetail.totalQuantity'), dataIndex: 'totalQuantity', width: 90, align: 'right' },
            { title: t('warehouse.preorders.activationDetail.importAmount'), dataIndex: 'totalImportAmount', width: 110, align: 'right', render: (value) => `$${(value ?? 0).toFixed(2)}` },
          ]} /> },
          { key: 'orders', label: t('warehouse.preorders.activationDetail.storeOrders', { count: statistics?.orders.length ?? 0 }), children: <Table size="small" rowKey="orderGuid" dataSource={statistics?.orders ?? []} columns={orderColumns} scroll={{ x: 1100 }} pagination={{ pageSize: 50, showSizeChanger: true }} /> },
          { key: 'matrix', label: t('warehouse.preorders.activationDetail.matrix'), children: matrixRows.length ? <Table size="small" rowKey="activationItemGuid" dataSource={matrixRows} columns={matrixColumns} pagination={false} scroll={{ x: 420 + matrixStores.length * 120 }} /> : <Empty description={t('warehouse.preorders.activationDetail.noMatrixData')} /> },
          { key: 'pending', label: t('warehouse.preorders.activationDetail.pendingStores', { count: statistics?.pendingStores.length ?? 0 }), children: <Table size="small" rowKey="storeGuid" pagination={false} dataSource={statistics?.pendingStores ?? []} columns={[{ title: t('warehouse.preorders.activationDetail.storeCode'), dataIndex: 'storeCode', width: 160 }, { title: t('warehouse.preorders.activationDetail.storeName'), dataIndex: 'storeName' }]} /> },
        ]} />
      </Card>
      <Modal
        title={t('warehouse.preorders.activationDetail.storeDetailTitle', { itemNumber: selectedProduct?.itemNumber ?? '' })}
        open={Boolean(selectedProduct)}
        onCancel={() => setSelectedProduct(null)}
        footer={null}
        width={680}
      >
        <Table
          size="small"
          rowKey="storeGuid"
          pagination={false}
          dataSource={selectedProductStores}
          scroll={{ x: 520, y: 400 }}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('warehouse.preorders.activationDetail.noStoreDetails')} /> }}
          columns={[
            { title: t('warehouse.preorders.activationDetail.storeCode'), dataIndex: 'storeCode', width: 130 },
            { title: t('warehouse.preorders.activationDetail.storeName'), dataIndex: 'storeName', width: 190 },
            { title: t('warehouse.preorders.activationDetail.packQuantity'), dataIndex: 'packCount', width: 90, align: 'right' },
            { title: t('warehouse.preorders.activationDetail.totalQuantity'), dataIndex: 'orderedQuantity', width: 90, align: 'right' },
          ]}
        />
      </Modal>
      <Modal title={t('warehouse.preorders.activationDetail.extendTitle')} open={extendOpen} onCancel={() => setExtendOpen(false)} onOk={() => void extendActivation()} okText={t('warehouse.preorders.activationDetail.confirmExtend')}>
        <DatePicker showTime value={nextEndAt} onChange={setNextEndAt} style={{ width: '100%' }} disabledDate={(date) => Boolean(detail && date.endOf('day').isBefore(dayjs(detail.endAtUtc)))} />
      </Modal>
      <Modal
        title={t('warehouse.preorders.activationDetail.changeArrivalDateTitle')}
        open={arrivalEditorOpen}
        onCancel={() => { if (!arrivalSavingRef.current) closeArrivalEditor() }}
        onOk={() => void saveEstimatedArrivalDate()}
        confirmLoading={arrivalSaving}
        cancelButtonProps={{ disabled: arrivalSaving }}
        closable={!arrivalSaving}
        keyboard={!arrivalSaving}
        maskClosable={!arrivalSaving}
        okText={t('common.save')}
      >
        <DatePicker allowClear format="YYYY-MM-DD" value={nextEstimatedArrivalDate} onChange={setNextEstimatedArrivalDate} style={{ width: '100%' }} />
      </Modal>
      <Modal
        title={t('warehouse.preorders.activationDetail.changeStoresTitle')}
        open={storeEditorOpen}
        onCancel={() => { if (!storeSaving) closeStoreEditor() }}
        onOk={saveActivationStores}
        okText={t('warehouse.preorders.activationDetail.saveStores')}
        okButtonProps={{ disabled: storeOptionsLoading || storeOptionsLoadFailed || !selectedStoreGuids.length || storeSaving }}
        cancelButtonProps={{ disabled: storeSaving }}
        closable={!storeSaving}
        keyboard={!storeSaving}
        maskClosable={!storeSaving}
        confirmLoading={storeSaving}
        width={680}
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Select
            mode="multiple"
            showSearch
            allowClear
            loading={storeOptionsLoading}
            disabled={storeOptionsLoadFailed || storeSaving}
            value={selectedStoreGuids}
            onChange={setSelectedStoreGuids}
            placeholder={t('warehouse.preorders.activationDetail.selectStores')}
            optionFilterProp="label"
            maxTagCount="responsive"
            style={{ width: '100%' }}
            options={storeOptions.map((store) => ({
              value: store.storeGuid,
              label: `${store.storeName || store.storeCode} (${store.storeCode})${store.isActive ? '' : ` · ${t('warehouse.preorders.activationDetail.inactiveStore')}`}`,
            }))}
          />
          <Text type="secondary">
            {t('warehouse.preorders.activationDetail.storeChangeSummary', storeChanges)}
          </Text>
          {storeChanges.removedCount > 0 ? <Alert type="warning" showIcon message={t('warehouse.preorders.activationDetail.storeRemovalWarning')} /> : null}
          {storeOptionsLoadFailed ? <Alert type="error" showIcon message={t('warehouse.preorders.activationDetail.storeOptionsLoadFailed')} /> : null}
        </Space>
      </Modal>
      </PageContainer>
    </ConfigProvider>
  )
}
