import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const zhFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const enFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')

const detailSource = readFileSync(detailFile, 'utf8')
const zhSource = readFileSync(zhFile, 'utf8')
const enSource = readFileSync(enFile, 'utf8')

async function main() {
  const failures: string[] = []

  const auxiliaryWarningFailure = await runTest('分店下拉加载失败应降级为非阻断提示', () => {
    assert(
      detailSource.includes("message.warning(t('storeOrders.detail.loadStoreOptionsFailed'"),
      'loadStores 失败时应使用非阻断 warning 文案，避免误提示整张订货明细失败',
    )
    assert(
      !detailSource.includes("message.error(error instanceof Error ? error.message : t('storeOrders.loadStoresFailed'))"),
      'loadStores 失败时不应直接透传后端错误 message',
    )
  })
  if (auxiliaryWarningFailure) failures.push(auxiliaryWarningFailure)

  const translationFailure = await runTest('分店下拉非阻断提示应有中英文文案', () => {
    assert(
      zhSource.includes('"loadStoreOptionsFailed": "分店下拉加载失败，订单明细可继续查看"'),
      '中文文案缺少分店下拉非阻断提示',
    )
    assert(
      enSource.includes('"loadStoreOptionsFailed": "Store selector failed to load. Order details remain available."'),
      '英文文案缺少分店下拉非阻断提示',
    )
  })
  if (translationFailure) failures.push(translationFailure)

  const editabilityStateFailure = await runTest('详情页应复用订单状态权限派生函数', () => {
    assert(
      detailSource.includes("import { deriveStoreOrderDetailPermissions } from './storeOrderDetailPermissions'") &&
        detailSource.includes('} = deriveStoreOrderDetailPermissions(detail?.flowStatus)'),
      '详情页尚未复用 deriveStoreOrderDetailPermissions 派生状态权限',
    )
  })
  if (editabilityStateFailure) failures.push(editabilityStateFailure)

  const editGuardFailure = await runTest('不可编辑订单的写入口应先走统一 guard', () => {
    assert(
      detailSource.includes('function ensureOrderEditable') &&
        detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))") &&
        detailSource.includes('if (!ensureOrderEditable())') &&
        detailSource.includes('handleSaveLine') &&
        detailSource.includes('handleConfirmPaste'),
      '详情页写操作尚未统一拦截不可编辑订单',
    )
  })
  if (editGuardFailure) failures.push(editGuardFailure)

  const flowGuardFailure = await runTest('状态流转写入口应有函数内二次门禁', () => {
    assert(
      detailSource.includes('if (!canStartPicking)') &&
        detailSource.includes('if (!canCompleteOrder)') &&
        detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))"),
      '开始配货/完成订单函数入口尚未按状态二次拦截',
    )
  })
  if (flowGuardFailure) failures.push(flowGuardFailure)

  const disabledUiFailure = await runTest('不可编辑订单应禁用表头和明细写控件但保留只读动作', () => {
    assert(
      detailSource.includes('disabled={isReadonlyOrder}') &&
        detailSource.includes('disabled={isReadonlyOrder || !selectedLineKeys.length}') &&
        detailSource.includes('disabled={isReadonlyOrder || validPastePreviewCount === 0}') &&
        detailSource.includes('disabled={isReadonlyOrder || !canStartPicking}') &&
        detailSource.includes('disabled={!canCompleteOrder}') &&
        detailSource.includes('navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)') &&
        detailSource.includes('navigate(`/warehouse/store-order/invoice/${detail.orderGUID}`)'),
      '详情页尚未禁用写控件或误影响配货单/发票只读动作',
    )
  })
  if (disabledUiFailure) failures.push(disabledUiFailure)

  const statusChangeFailure = await runTest('详情页应提供三状态订单状态更改入口', () => {
    assert(
      detailSource.includes('updateStoreOrderStatus') &&
        detailSource.includes('handleChangeOrderStatus') &&
        detailSource.includes('orderStatusChangeOptions') &&
        detailSource.includes('StoreOrderFlowStatus.Submitted') &&
        detailSource.includes('StoreOrderFlowStatus.Picking') &&
        detailSource.includes('StoreOrderFlowStatus.Completed') &&
        detailSource.includes("t('storeOrders.detail.changeOrderStatus'") &&
        detailSource.includes("t('storeOrders.detail.statusChangeSuccess'"),
      '详情页尚未提供三状态订单状态更改入口',
    )
  })
  if (statusChangeFailure) failures.push(statusChangeFailure)

  const readonlyCopyFailure = await runTest('只读状态应提供中英文提示文案', () => {
    assert(
      zhSource.includes('"orderReadonlyTitle": "当前订单为只读状态"') &&
        zhSource.includes('"orderReadonlyDescription": "已完成订单不可编辑，请更改状态后再操作。"') &&
        zhSource.includes('"orderReadonlyRefresh": "当前订单状态不可编辑，请刷新确认状态。"'),
      '中文文案缺少订单只读提示',
    )
    assert(
      enSource.includes('"orderReadonlyTitle": "Order is read-only"') &&
        enSource.includes('"orderReadonlyDescription": "Completed orders cannot be edited. Change the status before editing."') &&
        enSource.includes('"orderReadonlyRefresh": "The current order status is not editable. Please refresh and confirm the status."'),
      '英文文案缺少订单只读提示',
    )
  })
  if (readonlyCopyFailure) failures.push(readonlyCopyFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('detailAuxiliaryLoads.logic.test: ok')
}

await main()
