import {
  activateLinklyConfiguration,
  createLinklyTerminal,
  getLinklyTerminals,
  updateLinklyDeviceSelection,
  updateLinklyTerminal,
} from './paymentTerminalSettingsService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function readBody(call: { init?: RequestInit }) {
  return JSON.parse(String(call.init?.body)) as Record<string, unknown>
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []
const management = {
  storeCode: '001',
  environment: 'Production',
  mode: 'Draft',
  terminals: [{
    terminalId: 'terminal-1',
    storeCode: '001',
    environment: 'Production',
    laneNo: 1,
    displayName: 'Front Counter',
    usernameMasked: '7457•••••001',
    hasPassword: true,
    pairingState: 'Unpaired',
    selectedDeviceCount: 0,
    updatedAtUtc: '2026-09-02T00:00:00Z',
  }],
  devices: [],
}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({ url: String(input), init })
  return new Response(JSON.stringify({ success: true, data: management }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}) as typeof fetch

try {
  const listed = await getLinklyTerminals('001', 'Production')
  assertEqual(
    calls[0]?.url,
    '/api/react/v1/payment-terminal-settings/linkly-terminals?storeCode=001&environment=Production',
    'terminal list should use scoped query',
  )
  assertEqual(calls[0]?.init?.method, 'GET', 'terminal list should use GET')
  const listedTerminal = listed.terminals[0] as unknown as Record<string, unknown>
  for (const sensitiveField of ['username', 'password', 'secret', 'posId', 'pairCode']) {
    assert(!(sensitiveField in listedTerminal), `safe terminal response must not expose ${sensitiveField}`)
  }

  await createLinklyTerminal({
    storeCode: '001',
    environment: 'Production',
    laneNo: 1,
    displayName: 'Front Counter',
    username: 'test-user-001',
    password: 'lane-secret',
  })
  assertEqual(calls[1]?.init?.method, 'POST', 'terminal create should use POST')
  assertEqual(readBody(calls[1]).laneNo, 1, 'terminal create should send lane')

  await updateLinklyTerminal('terminal /1', {
    storeCode: '001',
    environment: 'Production',
    laneNo: 1,
    displayName: 'Front Counter',
  })
  assertEqual(
    calls[2]?.url,
    '/api/react/v1/payment-terminal-settings/linkly-terminals/terminal%20%2F1',
    'terminal update should encode terminal id',
  )
  assertEqual(calls[2]?.init?.method, 'PUT', 'terminal update should use PUT')

  await updateLinklyDeviceSelection('POS /01', {
    storeCode: '001',
    environment: 'Production',
    terminalId: 'terminal-1',
    expectedRevision: 2,
  })
  assertEqual(
    calls[3]?.url,
    '/api/react/v1/payment-terminal-settings/linkly-device-selections/POS%20%2F01',
    'device selection should encode device code',
  )
  assertEqual(readBody(calls[3]).expectedRevision, 2, 'device selection should send revision')

  await activateLinklyConfiguration({ storeCode: '001', environment: 'Production' })
  assertEqual(
    calls[4]?.url,
    '/api/react/v1/payment-terminal-settings/linkly-activation',
    'activation should use the fixed endpoint',
  )
  assertEqual(calls[4]?.init?.method, 'POST', 'activation should use POST')

  console.log('paymentTerminalSettingsService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
