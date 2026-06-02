import { syncContainersFromHq, translateHqProductNamesByContainerNumber } from './containerService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, label)
    return
  }

  throw new Error(`${label}. Expected promise to reject`)
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(JSON.stringify({
    success: true,
    data: {
      TotalCandidates: 1,
      TotalTranslated: 1,
      TotalSkipped: 0,
      TotalFailed: 0,
      Samples: { 自动脱毛梳: 'Pet Grooming Comb' },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await translateHqProductNamesByContainerNumber('CSNU6601647')

  assertEqual(
    capturedUrl,
    '/api/react/v1/hq-products/translate-names/by-container-number',
    'HQ container translation should call the body-based endpoint',
  )
  assertEqual(capturedInit?.method, 'POST', 'HQ container translation should use POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { ContainerNumbers: ['CSNU6601647'], OverwriteExisting: false },
    'HQ container translation should send container numbers in the request body',
  )

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init

    return new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步失败：业务错误',
      data: {
        isSuccess: false,
        message: '不应返回成功结果',
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await assertRejects(
    () => syncContainersFromHq('2026-05-01'),
    'HQ 同步失败：业务错误',
    'syncContainersFromHq should throw backend message when success is false',
  )
  assertEqual(capturedUrl, '/api/react/v1/containers/sync-from-hq', 'syncContainersFromHq should keep the sync URL unchanged')
  assertEqual(capturedInit?.method, 'POST', 'syncContainersFromHq should use POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { startDate: '2026-05-01' },
    'syncContainersFromHq should keep the request body unchanged',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: false,
    message: 'HQ 同步失败：HTTP 错误',
  }), {
    status: 500,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  await assertRejects(
    () => syncContainersFromHq(),
    'HQ 同步失败：HTTP 错误',
    'syncContainersFromHq should throw backend message when HTTP status is not 2xx',
  )
} finally {
  globalThis.fetch = originalFetch
}
