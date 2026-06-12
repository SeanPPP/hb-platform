import {
  buildAdvertisementUpsertPayload,
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

console.log('advertisementService.test: ok')
