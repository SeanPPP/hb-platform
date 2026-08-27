import { readFileSync } from 'node:fs'

const source = readFileSync(
  'src/pages/PosAdmin/DeviceRegistration/DeviceActivationCodePanel.tsx',
  'utf8',
)
const pageSource = readFileSync(
  'src/pages/PosAdmin/DeviceRegistration/index.tsx',
  'utf8',
)

function assertIncludes(fragment: string, label: string) {
  if (!source.includes(fragment)) {
    throw new Error(`${label}: missing ${fragment}`)
  }
}

assertIncludes("setCreatedCode(null)", 'Closing the result must clear the one-time code')
assertIncludes('<QRCode', 'Created code result must render a QR code')
assertIncludes('getDeviceActivationManageableStores', 'Store choices must use scoped backend options')
assertIncludes(
  "record.status === 'Available' || record.status === 'Expired'",
  'Every unconsumed code may expose revoke action',
)
assertIncludes('validForMinutes: 1440', 'Create form must default to the approved 24 hour TTL')
assertIncludes(
  'if (page === 1)',
  'Create success must refresh page one without issuing a stale-page request',
)
if (source.includes('setPage(1)\n      await load()')) {
  throw new Error('Create success must not immediately reload with the stale page closure')
}

if (!pageSource.includes("canViewLegacyDeviceRegistration ? 'registered' : 'activationCodes'")) {
  throw new Error('Activation-only users must enter the activation tab without loading legacy devices')
}
if (!pageSource.includes('if (canViewLegacyDeviceRegistration)')) {
  throw new Error('Activation-only users must not load legacy store/device filters')
}

console.log('DeviceActivationCodePanel.uiContract.test: ok')
