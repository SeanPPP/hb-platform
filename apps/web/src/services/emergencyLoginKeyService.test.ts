import {
  activateEmergencyLoginKey,
  generateEmergencyLoginKey,
  getEmergencyLoginKeys,
  retireEmergencyLoginKey,
} from './emergencyLoginKeyService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
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

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({ url, init })

  if (url.endsWith('/api/react/v1/emergency-login-keys')) {
    return new Response(JSON.stringify({
      success: true,
      data: {
        activeKeyId: 'elk-active',
        coverageKeyId: 'elk-staged',
        version: 7,
        dataProtectionHealthy: true,
        dataProtectionStatus: 'Healthy',
        coverage: { totalDevices: 4, acknowledgedDevices: 3, percentage: 75 },
        missingDevices: [{
          deviceRegistrationId: 10,
          storeCode: '001',
          deviceNumber: 'POS-01',
          hardwareId: 'hardware-01',
          lastOnlineAtUtc: '2026-07-15T01:00:00Z',
          lastSyncAtUtc: null,
        }],
        keys: [{
          keyId: 'elk-staged',
          status: 'Staged',
          publicKeyFingerprint: 'AABBCCDDEEFF',
          createdAtUtc: '2026-07-15T00:00:00Z',
          createdBy: 'admin',
          createdReason: 'rotate',
          encryptedPrivateKey: 'must-not-leak',
          protectedPrivateKey: 'must-not-leak',
        }],
      },
    }), { status: 200, headers: { 'content-type': 'application/json' } })
  }

  return new Response(JSON.stringify({
    success: true,
    data: {
      version: 8,
      activeKeyId: url.includes('/activate') ? 'elk-staged' : 'elk-active',
      key: {
        keyId: 'elk-staged',
        status: url.includes('/retire') ? 'Retired' : url.includes('/activate') ? 'Active' : 'Staged',
        publicKeyFingerprint: 'AABBCCDDEEFF',
        createdAtUtc: '2026-07-15T00:00:00Z',
        createdBy: 'admin',
        createdReason: 'rotate',
        encryptedPrivateKey: 'must-not-leak',
      },
    },
  }), { status: 200, headers: { 'content-type': 'application/json' } })
}) as typeof fetch

try {
  const list = await getEmergencyLoginKeys()
  assertEqual(calls[0]?.url, '/api/react/v1/emergency-login-keys', 'GET should use the fixed list endpoint')
  assertEqual(calls[0]?.init?.method, 'GET', 'List should use GET')
  assertEqual(list.version, 7, 'List should unwrap ApiResponse data')
  assertEqual(list.keys[0]?.keyId, 'elk-staged', 'List should normalize safe key fields')
  assert(!JSON.stringify(list).includes('must-not-leak'), 'List must discard every private key field')

  await generateEmergencyLoginKey({ reason: ' rotate ', expectedVersion: 7 })
  assertEqual(calls[1]?.url, '/api/react/v1/emergency-login-keys/generate', 'Generate URL should be fixed')
  assertEqual(calls[1]?.init?.method, 'POST', 'Generate should use POST')
  assertEqual(readBody(calls[1]).expectedVersion, 7, 'Generate should send expectedVersion')

  await activateEmergencyLoginKey('elk staged/1', { reason: 'activate', expectedVersion: 8, force: false })
  assertEqual(
    calls[2]?.url,
    '/api/react/v1/emergency-login-keys/elk%20staged%2F1/activate',
    'Activate URL should encode KID',
  )
  assertEqual(readBody(calls[2]).force, false, 'Normal activation should send force=false')

  await activateEmergencyLoginKey('elk-staged', { reason: 'force', expectedVersion: 8, force: true })
  assertEqual(readBody(calls[3]).force, true, 'Forced activation should send force=true')

  await retireEmergencyLoginKey('elk-staged', { reason: 'discard', expectedVersion: 8 })
  assertEqual(calls[4]?.url, '/api/react/v1/emergency-login-keys/elk-staged/retire', 'Retire URL should be fixed')
  assertEqual(readBody(calls[4]).reason, 'discard', 'Retire should send reason')

  console.log('emergencyLoginKeyService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
