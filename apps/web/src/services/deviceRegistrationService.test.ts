import {
  activateDevice,
  buildUpdateDeviceRegistrationPayload,
  disableDevice,
  getDeviceRegistrations,
  isDeviceRuntimeOnline,
  lockDevice,
  normalizeDeviceRegistrationDetail,
} from './deviceRegistrationService'

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

const normalizedChineseDetail = normalizeDeviceRegistrationDetail({
  ID: 12,
  设备硬件识别码: 'HW-CN-01',
  系统设备编号: 'POS-CN-01',
  分店代码: 'S001',
  分店名称: '示例分店',
  设备类型: 'POS',
  设备系统: 'Windows',
  设备状态: 1,
  设备状态描述: '已启用',
  备注: '中文字段',
  创建时间: '2026-05-01T10:00:00Z',
  最后修改时间: '2026-05-02T11:00:00Z',
  最后修改人: 'editor-cn',
  创建人: 'creator-cn',
})

assertEqual(normalizedChineseDetail.id, 12, 'Should normalize Chinese ID field')
assertEqual(
  normalizedChineseDetail.hardwareId,
  'HW-CN-01',
  'Should normalize Chinese hardware identifier field',
)
assertEqual(
  normalizedChineseDetail.storeName,
  '示例分店',
  'Should normalize Chinese store name field',
)
assertEqual(
  normalizedChineseDetail.remark,
  '中文字段',
  'Should normalize Chinese remark field',
)
assertEqual(
  normalizedChineseDetail.lastModifiedBy,
  'editor-cn',
  'Should normalize Chinese last modified by field',
)

const normalizedCamelDetail = normalizeDeviceRegistrationDetail({
  id: 13,
  hardwareId: 'HW-EN-01',
  systemDeviceNumber: 'POS-EN-01',
  storeCode: 'S002',
  storeName: 'Example Store',
  deviceType: 'Admin',
  deviceSystem: 'Mac',
  status: 3,
  statusDescription: 'Locked',
  remarks: 'camel field',
  createdAt: '2026-05-03T10:00:00Z',
  lastModified: '2026-05-04T11:00:00Z',
  lastModifiedBy: 'editor-en',
  createdBy: 'creator-en',
})

assertEqual(normalizedCamelDetail.id, 13, 'Should normalize camelCase ID field')
assertEqual(
  normalizedCamelDetail.statusDescription,
  'Locked',
  'Should normalize camelCase status description field',
)
assertEqual(
  normalizedCamelDetail.remark,
  'camel field',
  'Should normalize camelCase remark field',
)
assertEqual(
  normalizedCamelDetail.isOnline,
  false,
  'Should normalize missing runtime online field as false'
)

const normalizedRuntimeDetail = normalizeDeviceRegistrationDetail({
  id: 14,
  hardwareId: 'HW-RUN-01',
  systemDeviceNumber: 'POS-RUN-01',
  deviceType: 'POS',
  deviceSystem: 'Windows',
  status: 1,
  isOnline: true,
  lastHeartbeatAt: '2026-07-01T10:00:00Z',
  currentCashierId: 'CASHIER-1',
  currentCashierName: 'Alice',
  cashierLoginAt: '2026-07-01T09:55:00Z',
})

assertEqual(normalizedRuntimeDetail.isOnline, true, 'Should normalize runtime online status')
assertEqual(
  normalizedRuntimeDetail.lastHeartbeatAt,
  '2026-07-01T10:00:00Z',
  'Should normalize last heartbeat time',
)
assertEqual(
  normalizedRuntimeDetail.currentCashierName,
  'Alice',
  'Should normalize current cashier name',
)
assertEqual(
  isDeviceRuntimeOnline(
    normalizedRuntimeDetail,
    Date.parse('2026-07-01T10:00:44Z')
  ),
  true,
  'Runtime status should stay online inside the 45 second heartbeat window',
)
assertEqual(
  isDeviceRuntimeOnline(
    normalizedRuntimeDetail,
    Date.parse('2026-07-01T10:00:46Z')
  ),
  false,
  'Runtime status should become offline after the 45 second heartbeat window',
)

const updateValuesWithRuntimeExtras = {
  deviceType: 'Mobile',
  deviceSystem: 'Android',
  remark: 'updated',
  status: 2,
  statusDescription: 'Disabled',
}

assertDeepEqual(
  buildUpdateDeviceRegistrationPayload(updateValuesWithRuntimeExtras),
  {
    设备类型: 'Mobile',
    设备系统: 'Android',
    备注: 'updated',
  },
  'Update payload should only include editable Chinese DTO fields',
)

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; method?: string; body?: string }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({
    url: String(input),
    method: init?.method,
    body: typeof init?.body === 'string' ? init.body : undefined,
  })
  return new Response(JSON.stringify({
    success: true,
    data: {
      devices: [],
      pagination: {
        page: 2,
        pageSize: 30,
        total: 0,
        totalPages: 1,
      },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await getDeviceRegistrations({
    page: 2,
    pageSize: 30,
    storeCode: 'S01',
    deviceType: 'POS',
    deviceSystem: 'Windows',
  })
  await activateDevice(12)
  await disableDevice(12)
  await lockDevice(12)

  assertEqual(
    calls[0]?.url,
    '/api/paged?page=2&pageSize=30&storeCode=S01&deviceType=POS&deviceSystem=Windows',
    'Device registration list should use legacy device API base path',
  )
  assertEqual(calls[0]?.method, 'GET', 'Device registration list should use GET')
  assertEqual(
    calls[1]?.url,
    '/api/12/activate',
    'Device activation should use legacy device API base path',
  )
  assertEqual(calls[1]?.method, 'POST', 'Device activation should use POST')
  assertEqual(
    calls[2]?.url,
    '/api/12/disable',
    'Device disable should use legacy device API base path',
  )
  assertEqual(
    calls[3]?.url,
    '/api/12/lock',
    'Device lock should use legacy device API base path',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('deviceRegistrationService.test: ok')
