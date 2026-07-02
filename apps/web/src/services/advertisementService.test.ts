import {
  buildAdvertisementUpsertPayload,
  createAdvertisement,
  resolveAdvertisementMediaType,
  stripAdvertisementMediaUrlQuery,
} from './advertisementService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function assertRejectsWithMessage(
  action: () => Promise<unknown>,
  expectedParts: string[],
  message: string,
) {
  try {
    await action()
  } catch (error) {
    if (!(error instanceof Error)) {
      throw new Error(`${message}. Expected Error instance, received: ${String(error)}`)
    }

    for (const part of expectedParts) {
      if (!error.message.includes(part)) {
        throw new Error(`${message}. Expected error message to include: ${part}, received: ${error.message}`)
      }
    }
    return
  }

  throw new Error(`${message}. Expected promise to reject`)
}

assertEqual(
  stripAdvertisementMediaUrlQuery(
    'https://cdn.example.com/ads/banner.png?signature=abc123&expires=123',
  ),
  'https://cdn.example.com/ads/banner.png',
  'Signed advertisement media URLs should drop query parameters before persistence',
)

assertEqual(
  resolveAdvertisementMediaType({ type: 'video/mp4', name: 'demo.mp4' }),
  'Video',
  'Video uploads should resolve to Video media type',
)

assertEqual(
  resolveAdvertisementMediaType({ type: 'image/png', name: 'banner.png' }),
  'Image',
  'Image uploads should resolve to Image media type',
)

assertDeepEqual(
  buildAdvertisementUpsertPayload({
    title: '  首页横幅  ',
    description: '  新店开业  ',
    mediaType: 'Image',
    mediaUrl: 'https://cdn.example.com/ads/banner.png?signature=abc',
    thumbnailUrl: ' https://cdn.example.com/ads/thumb.png ',
    objectKey: ' ads/banner.png ',
    originalFileName: ' banner.png ',
    contentType: ' image/png ',
    fileSize: 2048,
    effectiveStart: { toISOString: () => '2026-05-27T10:00:00.000Z' },
    effectiveEnd: '2026-06-01T10:00:00.000Z',
    isEnabled: false,
    sortOrder: 12,
    stores: ['S001', { storeCode: 'S002' }],
  }),
  {
    title: '首页横幅',
    description: '新店开业',
    mediaType: 'Image',
    mediaUrl: 'https://cdn.example.com/ads/banner.png',
    thumbnailUrl: 'https://cdn.example.com/ads/thumb.png',
    objectKey: 'ads/banner.png',
    originalFileName: 'banner.png',
    contentType: 'image/png',
    fileSize: 2048,
    effectiveStart: '2026-05-27T10:00:00.000Z',
    effectiveEnd: '2026-06-01T10:00:00.000Z',
    isEnabled: false,
    sortOrder: 12,
    stores: [{ storeCode: 'S001' }, { storeCode: 'S002' }],
  },
  'Advertisement payload helper should normalize trimmed fields and store scope shape',
)

const originalFetch = globalThis.fetch
const createFailureCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  createFailureCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  return new Response(JSON.stringify({
    success: false,
    code: 'ADVERTISEMENT_STORE_SCOPE_INVALID',
    message: '分店不存在或未启用: 1042',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => createAdvertisement({
      title: '首页横幅',
      mediaType: 'Image',
      mediaUrl: 'https://cdn.example.com/ads/banner.png',
      objectKey: 'ads/banner.png',
      originalFileName: 'banner.png',
      contentType: 'image/png',
      fileSize: 2048,
      effectiveStart: '2026-05-27T10:00:00.000Z',
      effectiveEnd: '2026-06-01T10:00:00.000Z',
      isEnabled: true,
      sortOrder: 1,
      stores: [{ storeCode: '1042' }],
    }),
    ['ADVERTISEMENT_STORE_SCOPE_INVALID', '分店不存在或未启用: 1042'],
    'Create advertisement should throw backend code and message when ApiResponse.success is false',
  )
  assertEqual(
    createFailureCalls[0]?.url,
    '/api/react/v1/advertisements',
    'Create advertisement failure request should use the advertisements API contract',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('advertisementService.test: ok')
