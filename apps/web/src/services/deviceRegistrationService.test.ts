import {
  activateDevice,
  buildUpdateDeviceRegistrationPayload,
  disableDevice,
  getAppDeviceStatuses,
  getAppDeviceStatusSummary,
  getDeviceRegistrations,
  lockDevice,
  normalizeAppDeviceStatusListResponse,
  normalizeAppDeviceStatusSummary,
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

const normalizedAppDevices = normalizeAppDeviceStatusListResponse({
  success: true,
  data: {
    items: [{
      Id: 'F53AF6E9-A4C6-4C31-9B19-83E93B120D93',
      HardwareId: 'HW-APP-01',
      SystemDeviceNumber: 'DEV202607080001',
      DeviceSystem: 'Android',
      AppVersion: '1.2.3',
      AppBuildVersion: '45',
      RuntimeVersion: '1.2.3',
      Channel: 'production',
      UpdateId: '12345678-90ab-cdef-1234-567890abcdef',
      UpdateSource: 'ota',
      LastSeenAtUtc: '2026-07-08T09:00:00Z',
      IsOnline: 'true',
      LastSeenUsername: 'ada',
    }],
    Total: 1,
    Page: 1,
    PageSize: 20,
  },
})

assertEqual(normalizedAppDevices.total, 1, 'Should normalize App device total')
assertEqual(normalizedAppDevices.devices[0]?.hardwareId, 'HW-APP-01', 'Should normalize App hardware ID')
assertEqual(normalizedAppDevices.devices[0]?.isOnline, true, 'Should normalize App online state')
assertEqual(normalizedAppDevices.devices[0]?.appVersion, '1.2.3', 'Should normalize App package version')
assertEqual(normalizedAppDevices.devices[0]?.appBuildVersion, '45', 'Should normalize App build version')
assertEqual(normalizedAppDevices.devices[0]?.runtimeVersion, '1.2.3', 'Should normalize App runtime version')
assertEqual(normalizedAppDevices.devices[0]?.channel, 'production', 'Should normalize App channel')
assertEqual(
  normalizedAppDevices.devices[0]?.updateId,
  '12345678-90ab-cdef-1234-567890abcdef',
  'Should preserve full App update ID',
)
assertEqual(normalizedAppDevices.devices[0]?.updateSource, 'ota', 'Should normalize App update source')
assertEqual(normalizedAppDevices.devices[0]?.lastSeenUsername, 'ada', 'Should normalize App recent user')

assertDeepEqual(
  normalizeAppDeviceStatusSummary({
    success: true,
    data: {
      Total: 3,
      Online: 1,
      Offline: 2,
      Android: 2,
      Ios: 1,
      UnknownSystem: 0,
    },
  }),
  {
    total: 3,
    online: 1,
    offline: 2,
    android: 2,
    ios: 1,
    unknownSystem: 0,
  },
  'Should normalize App device summary',
)

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; method?: string; body?: string }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({
    url,
    method: init?.method,
    body: typeof init?.body === 'string' ? init.body : undefined,
  })

  if (url.includes('/api/mobile/app-device-status/paged')) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [{ id: 'app-1', hardwareId: 'HW-APP-URL', isOnline: true }],
        total: 1,
        page: 1,
        pageSize: 20,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  if (url.includes('/api/mobile/app-device-status/summary')) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        total: 1,
        online: 1,
        offline: 0,
        android: 1,
        ios: 0,
        unknownSystem: 0,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

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
  await getAppDeviceStatuses({
    page: 1,
    pageSize: 20,
    storeCode: 'S01',
    deviceSystem: 'Android',
    onlineState: 'online',
    keyword: 'Ada',
  })
  await getAppDeviceStatusSummary({
    storeCode: 'S01',
    deviceSystem: 'Android',
    keyword: 'Ada',
  })

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
  assertEqual(
    calls[4]?.url,
    '/api/mobile/app-device-status/paged?page=1&pageSize=20&storeCode=S01&deviceSystem=Android&onlineState=online&keyword=Ada',
    'App device list should use mobile app-device-status paged API',
  )
  assertEqual(calls[4]?.method, 'GET', 'App device list should use GET')
  assertEqual(
    calls[5]?.url,
    '/api/mobile/app-device-status/summary?storeCode=S01&deviceSystem=Android&keyword=Ada',
    'App device summary should use mobile app-device-status summary API',
  )
  assertEqual(calls[5]?.method, 'GET', 'App device summary should use GET')
} finally {
  globalThis.fetch = originalFetch
}

console.log('deviceRegistrationService.test: ok')
