import { translateHqProductNamesByContainerNumber } from './containerService'

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
} finally {
  globalThis.fetch = originalFetch
}
