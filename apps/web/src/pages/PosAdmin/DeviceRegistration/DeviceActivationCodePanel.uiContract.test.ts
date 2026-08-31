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
assertIncludes('getMobileDeviceActivationCodes', 'Mobile list must use its independent backend endpoint')
assertIncludes('getMobileDeviceActivationManageableAccounts', 'Mobile account choices must be scoped by store')
assertIncludes('createMobileDeviceActivationCode', 'Mobile create must use the mobile endpoint')
assertIncludes('revokeMobileDeviceActivationCode', 'Mobile revoke must use the mobile endpoint')
assertIncludes("type ActivationType = 'POS' | 'Mobile'", 'Panel must distinguish POS and Mobile grants')
assertIncludes('targetUserGuid', 'Mobile create must require a target account')
assertIncludes("const MOBILE_DEVICE_SYSTEMS: DeviceActivationSystem[] = ['Android', 'iOS']", 'Mobile must only allow Android and iOS')
assertIncludes("posAdmin.devices.activation.targetAccount", 'Table and form must render the target-account label')
assertIncludes('canManageMobile', 'Mobile actions must have an independent permission gate')
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
if (!pageSource.includes('access.canManageMobileDeviceActivationCodes')) {
  throw new Error('Device registration page must gate Mobile activation with its dedicated permission')
}
if (!pageSource.includes('canManageMobile={canManageMobileActivationCodes}')) {
  throw new Error('Device registration page must pass the Mobile permission to the activation panel')
}

for (const localePath of ['src/i18n/locales/zh.json', 'src/i18n/locales/en.json']) {
  const locale = JSON.parse(readFileSync(localePath, 'utf8'))
  const activation = locale.posAdmin.devices.activation
  for (const key of [
    'deviceType',
    'deviceTypes',
    'targetAccount',
    'targetAccountRequired',
    'loadAccountsFailed',
    'mobileAccountScopeHint',
  ]) {
    if (!activation[key]) {
      throw new Error(`${localePath} missing activation locale key: ${key}`)
    }
  }
}

console.log('DeviceActivationCodePanel.uiContract.test: ok')
