import { syncLocationsFromHq } from './locationService'

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

type FetchCall = {
  url: string
  method?: string
  body?: string
}

function jsonResponse(payload: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get: (name: string) => (name.toLowerCase() === 'content-type' ? 'application/json' : ''),
    },
    json: async () => payload,
  } as Response
}

function installFetchMock(payloads: unknown[]) {
  const calls: FetchCall[] = []
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({
      url: String(input),
      method: init?.method,
      body: typeof init?.body === 'string' ? init.body : undefined,
    })
    const payload = payloads.shift()
    return jsonResponse(payload)
  }) as typeof fetch
  return calls
}

async function main() {
  const failures: string[] = []

  const orderFailure = await runTest('syncLocationsFromHq 应先同步货位再同步商品货位并返回两段结果', async () => {
    const calls = installFetchMock([
      { success: true, data: { isSuccess: true, message: '货位完成', addedCount: 1, updatedCount: 2 } },
      { success: true, data: { isSuccess: true, message: '商品货位完成', addedCount: 3, updatedCount: 4 } },
    ])

    const result = await syncLocationsFromHq()

    assert(calls.length === 2, `应调用两个同步接口，实际 ${calls.length}`)
    assert(calls[0].url.endsWith('/api/react/v1/sync/locations-incremental'), '第一个接口应同步货位')
    assert(calls[1].url.endsWith('/api/react/v1/sync/product-locations-incremental'), '第二个接口应同步商品货位')
    assert(calls.every((call) => call.method === 'POST'), '两个同步接口都应使用 POST')
    assert(result.locationResult.message === '货位完成', '应返回货位同步结果')
    assert(result.productLocationResult.message === '商品货位完成', '应返回商品货位同步结果')
  })
  if (orderFailure) failures.push(orderFailure)

  const shortCircuitFailure = await runTest('syncLocationsFromHq 货位同步失败时不应继续同步商品货位', async () => {
    const calls = installFetchMock([
      { success: false, message: '货位同步失败', data: { isSuccess: false, message: '货位同步失败' } },
      { success: true, data: { isSuccess: true, message: '不应调用' } },
    ])

    let errorMessage = ''
    try {
      await syncLocationsFromHq()
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : String(error)
    }

    assert(errorMessage === '货位同步失败', `应透传货位失败消息，实际 ${errorMessage}`)
    assert(calls.length === 1, `货位失败后不应调用商品货位接口，实际调用 ${calls.length} 次`)
  })
  if (shortCircuitFailure) failures.push(shortCircuitFailure)

  const productLocationFailure = await runTest('syncLocationsFromHq 商品货位失败时应透传第二段错误', async () => {
    installFetchMock([
      { success: true, data: { isSuccess: true, message: '货位完成' } },
      { success: true, data: { isSuccess: false, message: '商品货位失败' } },
    ])

    let errorMessage = ''
    try {
      await syncLocationsFromHq()
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : String(error)
    }

    assert(errorMessage === '商品货位失败', `应透传商品货位失败消息，实际 ${errorMessage}`)
  })
  if (productLocationFailure) failures.push(productLocationFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('locationService.hqSync.test: ok')
}

await main()
