import assert from 'node:assert/strict'
import {
  advanceActivePreorderFreshEpoch,
  canBypassPreorderGate,
  getActivePreorders,
  isPreorderDraftConflictError,
  isPreorderStatusTransitionConflictError,
  normalizePreorderActiveResult,
  resolveEffectivePreorderGateBlocked,
  submitShopPreorder,
} from './preorderService'
import { RequestError } from '../utils/request'
import { runPreorderSubmit } from '../pages/ShopPreorder/preorderSubmitFlow'
import type { PreorderActivationDetail, PreorderActiveResult } from '../types/preorder'

const createValidActiveRow = (overrides: Record<string, unknown> = {}) => ({
  activationGuid: '11111111111171118111111111111111',
  templateGuid: '22222222-2222-4222-8222-222222222222',
  templateName: 'Preorder A',
  periodNumber: 1,
  activationCode: 'PRE-001',
  startAtUtc: '2026-07-01T00:00:00Z',
  endAtUtc: '2026-08-01T00:00:00Z',
  estimatedArrivalDate: null,
  status: 'Active',
  respondedStoreCount: 0,
  targetStoreCount: 1,
  ...overrides,
})

const unavailableCases: unknown[] = [undefined, null, 0, 1, 'false', 'true', {}]

for (const normalOrderBlocked of unavailableCases) {
  const result = normalizePreorderActiveResult({ normalOrderBlocked, activations: [] })
  assert.equal(result.normalOrderBlocked, false, `门禁值异常时必须 fail-open：${String(normalOrderBlocked)}`)
}

const unlocked = normalizePreorderActiveResult({ normalOrderBlocked: false, activations: [] })
assert.equal(unlocked.normalOrderBlocked, false, '明确无待处理批次时必须允许提交普通订单')
assert.deepEqual(unlocked.activations, [])

const contradictoryUnlocked = normalizePreorderActiveResult({
  normalOrderBlocked: false,
  activations: [createValidActiveRow() as never],
})
assert.equal(contradictoryUnlocked.normalOrderBlocked, false, '明确未阻塞但携带批次列表时必须 fail-open')
assert.deepEqual(contradictoryUnlocked.activations, [], 'flag 与列表矛盾时不得残留批次弹窗数据')

const malformedBlocked = normalizePreorderActiveResult({
  normalOrderBlocked: true,
  activations: [{} as never],
})
assert.equal(malformedBlocked.normalOrderBlocked, false, '缺少可导航 activationGuid 的畸形批次不得阻塞普通订单')
assert.deepEqual(malformedBlocked.activations, [])

const confirmedBlocked = normalizePreorderActiveResult({
  normalOrderBlocked: true,
  activations: [createValidActiveRow() as never],
})
assert.equal(confirmedBlocked.normalOrderBlocked, true, '只有明确阻塞且存在有效待处理批次时才拦截普通订单')
assert.equal(confirmedBlocked.activations[0]?.activationGuid, '11111111111171118111111111111111', '后端 N 格式批次必须正常参与门禁')

const canonicalGuidBlocked = normalizePreorderActiveResult({
  normalOrderBlocked: true,
  activations: [createValidActiveRow({ activationGuid: '11111111-1111-7111-8111-111111111111' }) as never],
})
assert.equal(canonicalGuidBlocked.normalOrderBlocked, true, '标准带连字符 GUID 必须保持兼容')

const malformedActivationCases: Array<[string, Record<string, unknown>]> = [
  ['不安全导航标识', { activationGuid: '../orders' }],
  ['GUID 长度错误', { activationGuid: '1111111111117111811111111111111' }],
  ['GUID 包含非十六进制字符', { activationGuid: '1111111111117111811111111111111g' }],
  ['无法解析的开始时间', { startAtUtc: 'not-a-date' }],
  ['结束时间不晚于开始时间', { endAtUtc: '2026-06-01T00:00:00Z' }],
  ['已关闭状态', { status: 'Closed' }],
  ['未知状态', { status: 'Unexpected' }],
  ['非正整数期号', { periodNumber: 0 }],
  ['负数门店计数', { respondedStoreCount: -1 }],
  ['非整数门店计数', { targetStoreCount: 1.5 }],
  ['已响应数大于目标数', { respondedStoreCount: 2, targetStoreCount: 1 }],
]

for (const [label, overrides] of malformedActivationCases) {
  const result = normalizePreorderActiveResult({
    normalOrderBlocked: true,
    activations: [createValidActiveRow(overrides) as never],
  })
  assert.equal(result.normalOrderBlocked, false, `${label}时 active 结果不可确认，必须 fail-open`)
  assert.deepEqual(result.activations, [])
}

const mixedMalformed = normalizePreorderActiveResult({
  normalOrderBlocked: true,
  activations: [
    createValidActiveRow() as never,
    createValidActiveRow({ activationGuid: 'unsafe/path' }) as never,
  ],
})
assert.equal(mixedMalformed.normalOrderBlocked, false, 'valid 与 invalid 行混合时必须整体 fail-open')
assert.deepEqual(mixedMalformed.activations, [], '畸形响应不得保留部分 activation 供页面渲染')

const warehouseStaffCanBypass = canBypassPreorderGate({
  isWarehouseStaffOnly: true,
  canManageWarehouseOrders: false,
  hasPermission: () => false,
})
assert.equal(
  resolveEffectivePreorderGateBlocked(true, warehouseStaffCanBypass),
  true,
  '纯 WarehouseStaff 未显式拥有 Orders.Create 时不能绕过客户端门禁',
)
assert.equal(
  canBypassPreorderGate({
    isWarehouseStaffOnly: true,
    canManageWarehouseOrders: false,
    hasPermission: (permission) => permission === 'Orders.Create',
  }),
  true,
  'WarehouseStaff 显式拥有 Orders.Create 时可以绕过客户端门禁',
)
assert.equal(
  canBypassPreorderGate({ isWarehouseStaffOnly: false, canManageWarehouseOrders: true, hasPermission: () => false }),
  true,
  '仓库订货管理员可以绕过客户端 Preorder 门禁',
)
assert.equal(
  resolveEffectivePreorderGateBlocked(true, false),
  true,
  '普通分店用户必须继续遵守 Preorder 门禁',
)

assert.equal(
  isPreorderDraftConflictError(new RequestError('草稿版本冲突', 409, {
    data: { errorCode: 'PREORDER_DRAFT_CONFLICT' },
  })),
  true,
  '草稿冲突必须按稳定错误码识别，页面才能刷新服务器版本',
)
assert.equal(
  isPreorderDraftConflictError(new RequestError('需要先完成预订', 409, {
    code: 'PREORDER_REQUIRED',
  })),
  false,
  '其他 409 不能误触发草稿协调流程',
)

assert.equal(
  isPreorderStatusTransitionConflictError(new RequestError('订单状态已变化', 409, {
    data: { errorCode: 'PREORDER_INVALID_STATUS_TRANSITION' },
  })),
  true,
  '仓库状态 CAS 冲突必须按稳定错误码识别并刷新最新批次',
)
assert.equal(isPreorderStatusTransitionConflictError(new RequestError('普通错误', 500)), false)

const originalFetch = globalThis.fetch
let submitRequest: RequestInit | undefined
let activeFetchCount = 0
let mutationScenarioActiveCount = 0
let reconciliationScenarioActiveCount = 0
let resolvePreCommitActive!: (response: Response) => void
const preCommitActiveResponse = new Promise<Response>((resolve) => {
  resolvePreCommitActive = resolve
})
let resolvePreReconciliationActive!: (response: Response) => void
const preReconciliationActiveResponse = new Promise<Response>((resolve) => {
  resolvePreReconciliationActive = resolve
})
globalThis.fetch = async (input, init) => {
  if (String(input).includes('/active')) {
    if (String(input).includes('STORE-MUTATION')) {
      mutationScenarioActiveCount += 1
      if (mutationScenarioActiveCount === 1) return preCommitActiveResponse
      return new Response(JSON.stringify({ data: { normalOrderBlocked: false, activations: [] } }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }
    if (String(input).includes('STORE-RECONCILIATION')) {
      reconciliationScenarioActiveCount += 1
      if (reconciliationScenarioActiveCount === 1) return preReconciliationActiveResponse
      return new Response(JSON.stringify({ data: { normalOrderBlocked: false, activations: [] } }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      })
    }
    activeFetchCount += 1
    return new Response(JSON.stringify({ data: { normalOrderBlocked: false, activations: [] } }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }
  submitRequest = init
  return new Response(JSON.stringify({ data: {} }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}
try {
  const payload = {
    storeCode: 'STORE-A',
    expectedDraftRevision: 1,
    confirmNoDemand: false,
    items: [],
  }
  await submitShopPreorder('activation-a', payload, 'submission-a')
  assert.equal((submitRequest?.headers as Record<string, string>)['X-Preorder-Submission-Id'], 'submission-a')
  await submitShopPreorder('activation-a', payload)
  assert.equal('X-Preorder-Submission-Id' in (submitRequest?.headers as Record<string, string>), false, '未提供 submissionId 时不得发送空 header')

  let actualActiveRequestCount = 0
  const firstActive = getActivePreorders('STORE-SINGLE-FLIGHT', undefined, () => {
    actualActiveRequestCount += 1
  })
  const secondActive = getActivePreorders('STORE-SINGLE-FLIGHT', undefined, () => {
    actualActiveRequestCount += 1
  })
  await Promise.all([firstActive, secondActive])
  assert.equal(activeFetchCount, 1, '同店 active gate 只能实际发起一次 GET')
  assert.equal(actualActiveRequestCount, 1, 'activeGet telemetry 只能由真正启动网络的 single-flight task 计数')

  const mutationPayload = { ...payload, storeCode: 'STORE-MUTATION' }
  const preCommitActive = getActivePreorders('STORE-MUTATION')
  await Promise.resolve()
  await submitShopPreorder('activation-mutation', mutationPayload, 'submission-mutation')
  assert.equal(mutationScenarioActiveCount, 1, 'POST service 成功本身不得提前开启第二条 lane')
  advanceActivePreorderFreshEpoch('STORE-MUTATION')
  const postCommitActive = getActivePreorders('STORE-MUTATION')
  const focusedPostCommitActive = getActivePreorders('STORE-MUTATION')
  assert.equal(mutationScenarioActiveCount, 2, 'POST 完成后必须启动第二次 active GET，且同一 fresh epoch 的 focus 不得增发请求')
  assert.notEqual(preCommitActive, postCommitActive, '旧 Promise 不能作为 post-commit 成功事实')
  resolvePreCommitActive(new Response(JSON.stringify({ data: {
    normalOrderBlocked: true,
    activations: [createValidActiveRow({ activationGuid: '33333333-3333-4333-8333-333333333333' })],
  } }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  }))
  assert.equal((await preCommitActive).normalOrderBlocked, true)
  assert.equal((await postCommitActive).normalOrderBlocked, false, '提交后门禁只能接受 fresh lane 的新结果')
  assert.equal((await focusedPostCommitActive).normalOrderBlocked, false)

  const preReconciliationActive = getActivePreorders('STORE-RECONCILIATION')
  await Promise.resolve()
  const reconciliationRequests: { post?: Promise<PreorderActiveResult> } = {}
  const reconciliationOutcome = await runPreorderSubmit({
    submit: async () => {
      // 服务端已提交但响应丢失，客户端只能通过一次 detail GET 确认终态。
      throw new Error('POST response lost after commit')
    },
    loadDetail: async () => ({ orderStatus: 'Submitted' }) as PreorderActivationDetail,
    isConflict: () => false,
    onTerminal: () => {
      advanceActivePreorderFreshEpoch('STORE-RECONCILIATION')
      reconciliationRequests.post = getActivePreorders('STORE-RECONCILIATION')
      void getActivePreorders('STORE-RECONCILIATION')
    },
    coordinateConflict: () => undefined,
  })
  assert.equal(reconciliationOutcome, 'terminal')
  assert.equal(reconciliationScenarioActiveCount, 2, 'terminal reconciliation 必须越过 pre-POST GET，且同 epoch 只增发一次')
  resolvePreReconciliationActive(new Response(JSON.stringify({ data: {
    normalOrderBlocked: true,
    activations: [createValidActiveRow({ activationGuid: '44444444-4444-4444-8444-444444444444' })],
  } }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  }))
  assert.equal((await preReconciliationActive).normalOrderBlocked, true)
  assert(reconciliationRequests.post)
  assert.equal((await reconciliationRequests.post).normalOrderBlocked, false)
} finally {
  globalThis.fetch = originalFetch
}

console.log('preorderActiveResult tests passed')
