import {
  APP_DEVICE_SYSTEM_OPTIONS,
  canEditRegisteredDeviceSystem,
  EDITABLE_DEVICE_SYSTEM_OPTIONS,
  getRegisteredDeviceSystemEditOptions,
  isEnabledLegacyIosPos,
  REGISTERED_DEVICE_SYSTEM_OPTIONS,
} from './deviceSystemOptions'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  REGISTERED_DEVICE_SYSTEM_OPTIONS,
  ['Windows', 'iPadOS', 'Other'],
  'Registered device filter should expose the platform categories',
)
assertDeepEqual(
  APP_DEVICE_SYSTEM_OPTIONS,
  ['Android', 'iOS', 'iPadOS', 'Windows', 'Mac'],
  'App usage filter should retain mobile platforms and include iPadOS',
)
assertDeepEqual(
  EDITABLE_DEVICE_SYSTEM_OPTIONS,
  ['Android', 'iOS', 'iPadOS', 'Windows', 'Mac'],
  'Existing device edit workflow should allow iPadOS',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(-1, 'Windows', 'POS'),
  true,
  'Pending device platform should remain editable',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(1, 'iOS', 'POS'),
  true,
  'Enabled legacy iOS should allow one-way canonicalization',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(1, 'iPadOS', 'POS'),
  false,
  'Enabled canonical iPadOS should not be editable',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(0, 'iOS', 'POS'),
  false,
  'Disabled legacy iOS should remain locked',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(2, 'iOS', 'POS'),
  false,
  'Locked legacy iOS should remain locked',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(3, 'iOS', 'POS'),
  false,
  'Unregistered legacy iOS should remain locked',
)
assertDeepEqual(
  canEditRegisteredDeviceSystem(1, 'iOS', 'Mobile'),
  false,
  'Enabled Mobile iOS should not be reclassified as iPadOS',
)
assertDeepEqual(
  getRegisteredDeviceSystemEditOptions(1, 'iOS', 'POS'),
  ['iOS', 'iPadOS'],
  'Enabled legacy iOS should only expose the equivalent canonical platform',
)
assertDeepEqual(
  isEnabledLegacyIosPos(1, 'iOS', 'POS'),
  true,
  'Enabled legacy iOS POS should lock device type during canonicalization',
)
assertDeepEqual(
  isEnabledLegacyIosPos(1, 'iOS', 'Mobile'),
  false,
  'Enabled Mobile iOS should not enter canonicalization mode',
)

console.log('deviceSystemOptions.test: ok')
