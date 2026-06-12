import { batchAssignProducts } from './warehouseCategoryService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    assertEqual(error instanceof Error ? error.message : String(error), expectedMessage, label)
    return
  }

  throw new Error(`${label}。Expected promise to reject`)
}

const originalFetch = globalThis.fetch

try {
  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: false,
    message: '后端拒绝批量更新',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  await assertRejects(
    () => batchAssignProducts('cat-guid-1', ['product-1']),
    '后端拒绝批量更新',
    '批量分配接口应抛出后端业务失败消息',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    isSuccess: false,
    message: '后端返回 isSuccess 失败',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  await assertRejects(
    () => batchAssignProducts('cat-guid-1', ['product-1']),
    '后端返回 isSuccess 失败',
    '批量分配接口应兼容 isSuccess=false 的业务失败消息',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseCategoryService.batchAssign.test: ok')
