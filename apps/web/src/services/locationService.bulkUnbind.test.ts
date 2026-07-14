import { batchUnbindLocationProducts } from './locationService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

type FetchCall = {
  url: string
  method?: string
}

function jsonResponse(payload: unknown, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

async function main() {
  const originalFetch = globalThis.fetch
  const calls: FetchCall[] = []

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    calls.push({ url, method: init?.method })

    if (url.includes('FAIL%2F002')) {
      return jsonResponse({ success: false, message: '解绑失败' }, 500)
    }

    if (url.includes('BUSINESS%2F003')) {
      return jsonResponse({ success: false, message: '业务解绑失败' })
    }

    if (url.includes('THROW%2F004')) {
      throw '非标准异常'
    }

    return jsonResponse({ success: true, data: { locationGuid: 'unused', products: [] } })
  }) as typeof fetch

  try {
    const bindings = [
      { locationGuid: 'LOC A/01', productCode: 'OK 001' },
      { locationGuid: 'LOC B/02', productCode: 'FAIL/002' },
    ]

    const result = await batchUnbindLocationProducts(bindings)

    assert(calls.length === 2, `应发出两次解绑请求，实际 ${calls.length}`)
    assert(calls.every((call) => call.method === 'DELETE'), '每个解绑请求都应使用 DELETE')
    assert(
      calls[0].url.endsWith('/api/react/v1/locations/LOC%20A%2F01/products/OK%20001'),
      `locationGuid 和 productCode 都应进行路径编码，实际 ${calls[0].url}`,
    )
    assert(
      calls[1].url.endsWith('/api/react/v1/locations/LOC%20B%2F02/products/FAIL%2F002'),
      `失败项路径也应正确编码，实际 ${calls[1].url}`,
    )
    assertDeepEqual(result.succeeded, [bindings[0]], '成功结果应仅包含成功解绑项')
    assertDeepEqual(
      result.failed,
      [{ ...bindings[1], message: '解绑失败' }],
      '失败结果应保留原绑定信息和错误消息',
    )

    calls.length = 0
    const businessFailureBinding = { locationGuid: 'LOC C/03', productCode: 'BUSINESS/003' }
    const businessFailureResult = await batchUnbindLocationProducts([businessFailureBinding])
    assertDeepEqual(businessFailureResult.succeeded, [], 'HTTP 200 的业务失败不应计入成功项')
    assertDeepEqual(
      businessFailureResult.failed,
      [{ ...businessFailureBinding, message: '业务解绑失败' }],
      'HTTP 200 且 success=false 时应记录业务失败消息',
    )

    calls.length = 0
    const nonErrorBinding = { locationGuid: 'LOC D/04', productCode: 'THROW/004' }
    const nonErrorResult = await batchUnbindLocationProducts([nonErrorBinding])
    assertDeepEqual(
      nonErrorResult.failed,
      [{ ...nonErrorBinding, message: '解绑商品失败' }],
      '非 Error 异常应使用稳定的中文兜底消息',
    )

    calls.length = 0
    const emptyResult = await batchUnbindLocationProducts([])
    assertDeepEqual(emptyResult, { succeeded: [], failed: [] }, '空数组应直接返回空汇总')
    assert(calls.length === 0, `空数组不应发请求，实际 ${calls.length}`)

    let active = 0
    let maxActive = 0
    const concurrentCalls: string[] = []
    globalThis.fetch = (async (input: RequestInfo | URL) => {
      const url = String(input)
      const requestIndex = concurrentCalls.length
      concurrentCalls.push(url)
      active += 1
      maxActive = Math.max(maxActive, active)

      // 让请求以不同顺序完成，验证汇总仍保持输入顺序。
      await new Promise((resolve) => setTimeout(resolve, (12 - requestIndex) * 2))
      active -= 1
      return jsonResponse({ success: true, data: { locationGuid: 'unused', products: [] } })
    }) as typeof fetch

    const concurrentBindings = Array.from({ length: 12 }, (_, index) => ({
      locationGuid: `LOC-${String(index).padStart(2, '0')}`,
      productCode: `PRODUCT-${String(index).padStart(2, '0')}`,
    }))
    const concurrentResult = await batchUnbindLocationProducts(concurrentBindings)

    assert(maxActive <= 5, `批量解绑最多允许 5 个并发请求，实际 ${maxActive}`)
    assert(concurrentCalls.length === concurrentBindings.length, '并发受限时仍应执行全部解绑项')
    assertDeepEqual(concurrentResult.succeeded, concurrentBindings, '成功汇总应保持输入顺序且包含全部解绑项')
    assertDeepEqual(concurrentResult.failed, [], '全部请求成功时失败汇总应为空')
  } finally {
    globalThis.fetch = originalFetch
  }

  console.log('locationService.bulkUnbind.test: ok')
}

await main()
