import {
  buildWpfReleaseObjectKey,
  createWpfAppRelease,
  getWpfAppReleases,
  getWpfTargetDevices,
  getWpfTargetStores,
  initWpfReleaseUpload,
  normalizeWpfAppRelease,
  normalizeWpfReleaseUploadInitResult,
  saveWpfReleasePolicy,
  updateWpfAppRelease,
  uploadWpfReleaseFile,
} from './wpfVersionService'

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

const normalizedRelease = normalizeWpfAppRelease({
  id: 'release-1',
  version: '1.2.3',
  channel: 'production',
  fileName: 'hbpos-1.2.3.msi',
  fileSize: '1048576',
  sha256: 'ABCDEF',
  installerType: 'msi',
  installerArguments: '/qn',
  downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
  objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
  releaseNotes: 'Stable release',
  isCurrent: true,
  forceUpdate: 'true',
  minimumSupportedVersion: '1.0.0',
  targetVersion: '1.2.3',
  targetScope: 'Devices',
  targetStoreGuids: ['ignored-store'],
  targetDeviceRegistrationIds: ['9', 3, 9],
  targetStoreSummaries: [{
    storeGuid: 'store-guid',
    storeCode: 'S01',
    storeName: 'Store One',
  }],
  targetDeviceSummaries: [{
    deviceRegistrationId: 9,
    systemDeviceNumber: 'POS-009',
    storeCode: 'S01',
    storeName: 'Store One',
    remarks: 'Front counter',
    hardwareId: 'must-not-reach-ui',
    authorizationCode: 'must-not-reach-ui',
  }],
  policyUpdatedAt: '2026-07-22T02:00:00Z',
  policyUpdatedBy: 'admin',
  createdAt: '2026-06-25T01:00:00Z',
  updatedAt: '2026-06-25T02:00:00Z',
})

assertEqual(normalizedRelease.fileSize, 1048576, 'WPF release normalizer should parse fileSize as number')
assertEqual(normalizedRelease.forceUpdate, true, 'WPF release normalizer should parse forceUpdate boolean')
assertEqual(normalizedRelease.sha256, 'ABCDEF', 'WPF release normalizer should keep sha256')
assertEqual(normalizedRelease.targetScope, 'devices', 'WPF release normalizer should normalize target scope')
assertDeepEqual(
  normalizedRelease.targetDeviceRegistrationIds,
  [3, 9],
  'WPF release normalizer should deduplicate and sort target device IDs',
)
assertEqual(normalizedRelease.policyUpdatedBy, 'admin', 'WPF release normalizer should preserve policy audit metadata')
assertDeepEqual(
  normalizedRelease.targetStoreSummaries,
  [{ storeGuid: 'store-guid', storeCode: 'S01', storeName: 'Store One' }],
  'WPF release normalizer should preserve safe target store summaries',
)
assertDeepEqual(
  normalizedRelease.targetDeviceSummaries,
  [{
    deviceRegistrationId: 9,
    systemDeviceNumber: 'POS-009',
    storeCode: 'S01',
    storeName: 'Store One',
    remarks: 'Front counter',
  }],
  'WPF release normalizer should preserve safe target device summaries without credentials',
)

assertEqual(
  normalizeWpfAppRelease({
    id: 'release-unsupported',
    version: '1.2.4',
    channel: 'production',
    fileName: 'hbpos-1.2.4.zip',
    installerType: 'zip',
  }).installerType,
  null,
  'Unsupported installerType values should be normalized to null instead of leaking raw backend values',
)

assertEqual(
  normalizeWpfAppRelease({
    id: 'release-unsupported-2',
    version: '1.2.5',
    channel: 'production',
    fileName: 'hbpos-1.2.5.bat',
    installerType: 'bat',
  }).installerType,
  null,
  'Unsupported installerType values should consistently normalize to null',
)

assertEqual(
  buildWpfReleaseObjectKey({ channel: 'Preview', version: ' v1.2.3 ', fileName: ' Hb POS Setup 1.2.3.msi ' }),
  'wpf-releases/preview/1.2.3/Hb-POS-Setup-1.2.3.msi',
  'COS object key should follow the fixed wpf-releases/{channel}/{version}/{fileName} contract',
)

const nestedUploadInit = normalizeWpfReleaseUploadInitResult({
  objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
  directUpload: {
    url: 'https://cos-upload.example/upload',
    objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
    headers: { 'content-type': 'application/x-msi' },
  },
})
assertEqual(
  nestedUploadInit.uploadUrl,
  'https://cos-upload.example/upload',
  'Upload init normalizer should read backend directUpload.url',
)
assertDeepEqual(
  nestedUploadInit.headers,
  { 'content-type': 'application/x-msi' },
  'Upload init normalizer should read backend directUpload.headers',
)

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  const data = calls.length === 1
    ? {
        objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
        downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
        directUpload: {
          uploadUrl: 'https://cos-upload.example/upload',
          headers: { 'x-cos-meta': 'wpf' },
        },
      }
    : calls.length === 2
      ? normalizedRelease
      : calls.length === 3
        ? {
            items: [normalizedRelease],
            total: 1,
            page: 2,
            pageSize: 20,
          }
      : { success: true }

  return new Response(JSON.stringify({ success: true, data }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const uploadInit = await initWpfReleaseUpload({
    channel: 'production',
    version: '1.2.3',
    fileName: 'hbpos-1.2.3.msi',
    fileSize: 1048576,
    sha256: 'ABCDEF',
    contentType: 'application/x-msi',
  })

  const release = await createWpfAppRelease({
    version: '1.2.3',
    channel: 'production',
    fileName: 'hbpos-1.2.3.msi',
    fileSize: 1048576,
    sha256: 'ABCDEF',
    installerType: 'msi',
    installerArguments: '/qn',
    objectKey: uploadInit.objectKey,
    downloadUrl: uploadInit.downloadUrl,
    releaseNotes: 'Stable release',
  })

  const paged = await getWpfAppReleases({
    page: 2,
    pageSize: 20,
    channel: 'preview',
    includeDisabled: true,
  })

  await saveWpfReleasePolicy({
    channel: 'production',
    targetVersion: '1.2.3',
    minimumSupportedVersion: '1.0.0',
    forceUpdate: true,
    isRollback: false,
    targetScope: 'devices',
    targetStoreGuids: ['ignored-store'],
    targetDeviceRegistrationIds: [9, 3, 9],
  })

  await updateWpfAppRelease('release-1', { isActive: false })

  assertEqual(calls[0]?.url, '/api/wpf-app-releases/upload/init', 'Upload init should call fixed backend endpoint')
  assertEqual(calls[0]?.method, 'POST', 'Upload init should use POST')
  assertDeepEqual(
    calls[0]?.body,
    {
      channel: 'production',
      version: '1.2.3',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      contentType: 'application/x-msi',
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
    },
    'Upload init payload should include deterministic COS object key',
  )
  assertEqual(release.id, 'release-1', 'Create release should normalize response payload')
  assertEqual(paged.page, 2, 'Release list should normalize page from backend payload')
  assertEqual(
    (calls[1]?.body as { cosObjectKey?: string } | undefined)?.cosObjectKey,
    'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
    'Create release should submit backend cosObjectKey field',
  )
  assertEqual(
    calls[2]?.url,
    '/api/wpf-app-releases?page=2&pageSize=20&channel=preview&includeDisabled=true',
    'Release list query should include includeDisabled when requested',
  )
  assertDeepEqual(
    calls[3]?.body,
    {
      channel: 'production',
      targetVersion: '1.2.3',
      minimumSupportedVersion: '1.0.0',
      forceUpdate: true,
      isRollback: false,
      targetScope: 'devices',
      targetStoreGuids: [],
      targetDeviceRegistrationIds: [3, 9],
    },
    'Policy save should normalize target scope and submit only active target values',
  )
  assertEqual(
    calls[3]?.url,
    '/api/wpf-app-releases/policy/production',
    'Policy save should bind the fixed channel in the route',
  )
  assertEqual(calls[4]?.url, '/api/wpf-app-releases/release-1', 'Update release should use release id route')
  assertEqual(calls[4]?.method, 'PUT', 'Update release should use PUT')
  assertDeepEqual(
    calls[4]?.body,
    {
      isActive: false,
    },
    'Update release should submit status mutation payload',
  )
} finally {
  globalThis.fetch = originalFetch
}

const targetOptionCalls: Array<{ url: string; method?: string }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  targetOptionCalls.push({ url, method: init?.method })
  const data = url.includes('/devices')
    ? {
        items: [{
          deviceRegistrationId: 7,
          systemDeviceNumber: 'POS-007',
          storeGuid: 'store-guid',
          storeCode: 'S01',
          storeName: 'Store One',
          remarks: 'Front counter',
        }],
        total: 1,
        page: 2,
        pageSize: 25,
      }
    : { items: [{ storeGuid: 'store-guid', storeCode: 'S01', storeName: 'Store One' }] }

  return new Response(JSON.stringify({ success: true, data }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const stores = await getWpfTargetStores()
  const devices = await getWpfTargetDevices({ page: 2, pageSize: 25, keyword: ' POS 007 ' })

  assertEqual(stores[0]?.storeGuid, 'store-guid', 'Target store options should preserve stable StoreGUID')
  assertDeepEqual(
    stores[0],
    { storeGuid: 'store-guid', storeCode: 'S01', storeName: 'Store One' },
    'Target store options should expose only GUID, code, and name',
  )
  assertEqual(devices.items[0]?.deviceRegistrationId, 7, 'Target devices should use registration ID')
  assertEqual(devices.page, 2, 'Target device search should preserve backend pagination')
  assertEqual(
    targetOptionCalls[0]?.url,
    '/api/wpf-app-releases/target-options/stores',
    'Target stores should use the WPF-scoped endpoint',
  )
  assertEqual(
    targetOptionCalls[1]?.url,
    '/api/wpf-app-releases/target-options/devices?page=2&pageSize=25&keyword=POS+007',
    'Target device search should be paged and trim the keyword',
  )
} finally {
  globalThis.fetch = originalFetch
}

const multipartOnlyCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  multipartOnlyCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
      multipartUpload: {
        uploadId: 'multipart-1',
        partSize: 5242880,
      },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: 'production',
      version: '1.2.3',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      contentType: 'application/x-msi',
    }),
    ['uploadUrl'],
    'Upload init should reject multipart-like responses that do not include a direct uploadUrl',
  )
  assertEqual(
    multipartOnlyCalls.length,
    1,
    'Upload init should stop after the invalid init response instead of continuing as a successful upload init',
  )
} finally {
  globalThis.fetch = originalFetch
}

const multipartUploadUrlCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  multipartUploadUrlCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
      downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
      multipartUpload: {
        uploadUrl: 'https://cos-upload.example/multipart-only',
        uploadMethod: 'PUT',
        headers: { 'x-cos-meta': 'multipart-only' },
      },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: 'production',
      version: '1.2.3',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      contentType: 'application/x-msi',
    }),
    ['uploadUrl'],
    'Upload init should reject multipart-only responses even when multipartUpload exposes uploadUrl and downloadUrl',
  )
  assertEqual(
    multipartUploadUrlCalls.length,
    1,
    'Upload init should treat multipart-only responses as invalid and stop immediately',
  )
} finally {
  globalThis.fetch = originalFetch
}

const missingDownloadUrlCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  missingDownloadUrlCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  return new Response(JSON.stringify({
    success: true,
    data: {
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
      directUpload: {
        url: 'https://cos-upload.example/upload',
        headers: { 'content-type': 'application/x-msi' },
      },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: 'production',
      version: '1.2.3',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      contentType: 'application/x-msi',
    }),
    ['downloadUrl'],
    'Upload init should reject responses that do not include a downloadable release URL',
  )
  assertEqual(
    missingDownloadUrlCalls.length,
    1,
    'Upload init should stop after missing downloadUrl instead of continuing as a successful release init',
  )
} finally {
  globalThis.fetch = originalFetch
}

const emptyUploadUrlCalls: string[] = []

globalThis.fetch = (async (input: RequestInfo | URL) => {
  emptyUploadUrlCalls.push(String(input))
  return new Response(null, { status: 200 })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => uploadWpfReleaseFile(
      new File(['installer'], 'hbpos-1.2.3.msi', { type: 'application/x-msi' }),
      {
        uploadUrl: '',
        uploadMethod: 'PUT',
        objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
        downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
        headers: {},
      },
    ),
    ['uploadUrl'],
    'Upload file should reject empty uploadUrl before calling fetch',
  )
  assertDeepEqual(emptyUploadUrlCalls, [], 'Upload file should not call fetch with an empty uploadUrl')
} finally {
  globalThis.fetch = originalFetch
}

const uploadHeaderCalls: Array<{ url: string; method?: string; headers?: HeadersInit; body?: BodyInit | null }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  uploadHeaderCalls.push({
    url: String(input),
    method: init?.method,
    headers: init?.headers,
    body: init?.body,
  })

  return new Response(null, { status: 200 })
}) as typeof fetch

try {
  await uploadWpfReleaseFile(
    new File(['installer'], 'hbpos-1.2.3.msi', { type: 'application/x-msi' }),
    {
      uploadUrl: 'https://cos-upload.example/upload',
      uploadMethod: 'PUT',
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
      downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
      headers: {
        'Content-Type': 'application/x-msi',
        'x-cos-meta-sha256': 'ABCDEF',
      },
    },
  )

  assertEqual(uploadHeaderCalls.length, 1, 'Upload file should issue exactly one direct upload request')
  assertEqual(uploadHeaderCalls[0]?.url, 'https://cos-upload.example/upload', 'Upload file should use direct upload URL')
  assertEqual(uploadHeaderCalls[0]?.method, 'PUT', 'Upload file should preserve direct upload method')
  assertDeepEqual(
    uploadHeaderCalls[0]?.headers,
    {
      'Content-Type': 'application/x-msi',
      'x-cos-meta-sha256': 'ABCDEF',
    },
    'Upload file should pass direct upload headers to fetch without dropping metadata headers',
  )
} finally {
  globalThis.fetch = originalFetch
}

globalThis.fetch = (async () => {
  return new Response(JSON.stringify({
    data: [
      {
        ...normalizedRelease,
        id: 'release-top-level-data',
        version: '1.2.4',
        isCurrent: false,
      },
    ],
    total: 9,
    page: 3,
    pageSize: 5,
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const topLevelPaged = await getWpfAppReleases({
    page: 3,
    pageSize: 5,
  })

  assertEqual(
    topLevelPaged.items.length,
    1,
    'Release list should keep top-level data array items when pagination metadata sits beside data',
  )
  assertEqual(topLevelPaged.items[0]?.id, 'release-top-level-data', 'Top-level data release should be normalized')
  assertEqual(topLevelPaged.total, 9, 'Top-level data release list should keep sibling total')
  assertEqual(topLevelPaged.page, 3, 'Top-level data release list should keep sibling page')
  assertEqual(topLevelPaged.pageSize, 5, 'Top-level data release list should keep sibling pageSize')
} finally {
  globalThis.fetch = originalFetch
}

const listFailureCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  listFailureCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  return new Response(JSON.stringify({
    isSuccess: false,
    errorCode: 'WPF_RELEASE_LIST_REJECTED',
    message: 'Release list is temporarily unavailable',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => getWpfAppReleases({
      page: 1,
      pageSize: 10,
      channel: 'production',
      includeDisabled: true,
    }),
    ['WPF_RELEASE_LIST_REJECTED', 'Release list is temporarily unavailable'],
    'Release list should throw backend code and message when ApiResponse.success is false',
  )
  assertEqual(
    listFailureCalls[0]?.url,
    '/api/wpf-app-releases?page=1&pageSize=10&channel=production&includeDisabled=true',
    'Release list failure request should still use the expected query contract',
  )
} finally {
  globalThis.fetch = originalFetch
}

const failureCalls: Array<{ url: string; method?: string; body?: unknown }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  failureCalls.push({
    url: String(input),
    method: init?.method,
    body: init?.body ? JSON.parse(String(init.body)) : undefined,
  })

  let payload: Record<string, unknown>
  if (failureCalls.length === 1) {
    payload = {
      success: false,
      code: 'WPF_UPLOAD_INIT_REJECTED',
      message: 'Upload init denied by release policy',
    }
  } else if (failureCalls.length === 2) {
    payload = {
      success: false,
      code: 'WPF_RELEASE_CREATE_REJECTED',
      message: 'Release version already exists',
    }
  } else if (failureCalls.length === 3) {
    payload = {
      success: false,
      code: 'WPF_RELEASE_UPDATE_REJECTED',
      message: 'Release is referenced by active policy',
    }
  } else {
    payload = {
      success: false,
      code: 'WPF_POLICY_SAVE_REJECTED',
      message: 'Rollback confirmation required',
    }
  }

  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await assertRejectsWithMessage(
    () => initWpfReleaseUpload({
      channel: 'production',
      version: '1.2.3',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      contentType: 'application/x-msi',
    }),
    ['WPF_UPLOAD_INIT_REJECTED', 'Upload init denied by release policy'],
    'Upload init should throw backend code and message when ApiResponse.success is false',
  )

  await assertRejectsWithMessage(
    () => createWpfAppRelease({
      version: '1.2.3',
      channel: 'production',
      fileName: 'hbpos-1.2.3.msi',
      fileSize: 1048576,
      sha256: 'ABCDEF',
      installerType: 'msi',
      installerArguments: '/qn',
      objectKey: 'wpf-releases/production/1.2.3/hbpos-1.2.3.msi',
      downloadUrl: 'https://cos.example/hbpos-1.2.3.msi',
      releaseNotes: 'Stable release',
    }),
    ['WPF_RELEASE_CREATE_REJECTED', 'Release version already exists'],
    'Create release should throw backend code and message when ApiResponse.success is false',
  )

  await assertRejectsWithMessage(
    () => updateWpfAppRelease('release-1', {
      isActive: false,
    }),
    ['WPF_RELEASE_UPDATE_REJECTED', 'Release is referenced by active policy'],
    'Update release should throw backend code and message when ApiResponse.success is false',
  )

  await assertRejectsWithMessage(
    () => saveWpfReleasePolicy({
      channel: 'production',
      targetVersion: '1.2.3',
      minimumSupportedVersion: '1.0.0',
      forceUpdate: true,
      isRollback: true,
      rollbackConfirmed: false,
      targetScope: 'all',
      targetStoreGuids: [],
      targetDeviceRegistrationIds: [],
    }),
    ['WPF_POLICY_SAVE_REJECTED', 'Rollback confirmation required'],
    'Save policy should throw backend code and message when ApiResponse.success is false',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('wpfVersionService.test: ok')
