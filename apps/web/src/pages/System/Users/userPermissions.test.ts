import {
  buildGrantedPosPermissionCodes,
  buildDirectPermissionPayload,
  buildFallbackUserPermissionState,
  buildPosPermissionSections,
  deriveDirectPermissionKeysFromChecked,
  buildPermissionSourceMap,
  getCheckedPermissionKeys,
  getEditablePosPermissionCodes,
  getPosPermissionGroupSelectionState,
  isCurrentPosPermissionRequest,
  isInheritedPosPermissionMode,
  setPosPermissionGroupSelection,
  shouldEnablePosPermissionSave,
  toggleDirectPermission,
} from './userPermissions'
import type { PosTerminalPermissionOptionDto } from '../../../types/user'
import type { UserPermissionStateDto } from '../../../types/user'

function assertArrayEqual<T>(actual: T[], expected: T[], message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function createPosPermission(
  code: string,
  name: string,
  group = 'POS 销售',
): PosTerminalPermissionOptionDto {
  return { code, name, group, description: '' }
}

const posSalesPrefix = 'Permissions.PosTerminal.Sales.'
const assignableDiscountPermissions = [
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount50Percent`, '整单 50% 折扣'),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount20Percent`, '单行 20% 折扣'),
  createPosPermission(`${posSalesPrefix}LineManualDiscount`, '单行手动折扣'),
  createPosPermission(`${posSalesPrefix}OrderManualDiscount`, '整单手动折扣'),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount10Percent`, '单行 10% 折扣'),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount30Percent`, '单行 30% 折扣'),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount40Percent`, '单行 40% 折扣'),
  createPosPermission(`${posSalesPrefix}LineQuickDiscount50Percent`, '单行 50% 折扣'),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount10Percent`, '整单 10% 折扣'),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount20Percent`, '整单 20% 折扣'),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount30Percent`, '整单 30% 折扣'),
  createPosPermission(`${posSalesPrefix}OrderQuickDiscount40Percent`, '整单 40% 折扣'),
]

const posSections = buildPosPermissionSections(assignableDiscountPermissions)

assertArrayEqual(
  posSections.flatMap((section) => section.groups.map((group) => group.displayName)),
  ['单行折扣', '整单折扣'],
  'POS discount permissions should be presented as clear line and order groups',
)

assertArrayEqual(
  posSections[0]?.groups[0]?.permissions.map((permission) => permission.code) ?? [],
  [
    `${posSalesPrefix}LineManualDiscount`,
    `${posSalesPrefix}LineQuickDiscount10Percent`,
    `${posSalesPrefix}LineQuickDiscount20Percent`,
    `${posSalesPrefix}LineQuickDiscount30Percent`,
    `${posSalesPrefix}LineQuickDiscount40Percent`,
    `${posSalesPrefix}LineQuickDiscount50Percent`,
  ],
  'Line discount permissions should keep manual then 10 to 50 percent order',
)

assertArrayEqual(
  posSections[0]?.groups[1]?.permissions.map((permission) => permission.code) ?? [],
  [
    `${posSalesPrefix}OrderManualDiscount`,
    `${posSalesPrefix}OrderQuickDiscount10Percent`,
    `${posSalesPrefix}OrderQuickDiscount20Percent`,
    `${posSalesPrefix}OrderQuickDiscount30Percent`,
    `${posSalesPrefix}OrderQuickDiscount40Percent`,
    `${posSalesPrefix}OrderQuickDiscount50Percent`,
  ],
  'Order discount permissions should keep manual then 10 to 50 percent order',
)

assertArrayEqual(
  buildGrantedPosPermissionCodes(
    [
      `${posSalesPrefix}LineManualDiscount`,
      'Permissions.PosTerminal.Admin.Manage',
      `${posSalesPrefix}LineManualDiscount`,
    ],
    assignableDiscountPermissions,
  ),
  [`${posSalesPrefix}LineManualDiscount`],
  'POS save payload should only contain unique API-assignable permission codes',
)

assertArrayEqual(
  getEditablePosPermissionCodes(
    [
      `${posSalesPrefix}LineManualDiscount`,
      'Permissions.PosTerminal.Admin.Hidden',
    ],
    assignableDiscountPermissions,
  ),
  [`${posSalesPrefix}LineManualDiscount`],
  'POS effective selection should not expose permissions outside the API whitelist',
)

assertArrayEqual(
  setPosPermissionGroupSelection(
    ['Permissions.PosTerminal.Payment.View', `${posSalesPrefix}LineManualDiscount`],
    [
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`,
      `${posSalesPrefix}LineQuickDiscount10Percent`,
    ],
    true,
  ),
  [
    'Permissions.PosTerminal.Payment.View',
    `${posSalesPrefix}LineManualDiscount`,
    `${posSalesPrefix}LineQuickDiscount10Percent`,
  ],
  'Selecting a POS permission group should add unique group codes and preserve other groups',
)

assertArrayEqual(
  setPosPermissionGroupSelection(
    [
      'Permissions.PosTerminal.Payment.View',
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`,
    ],
    [
      `${posSalesPrefix}LineManualDiscount`,
      `${posSalesPrefix}LineQuickDiscount10Percent`,
    ],
    false,
  ),
  ['Permissions.PosTerminal.Payment.View'],
  'Clearing a POS permission group should remove only that group and preserve other groups',
)

assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState([], ['pos.view', 'pos.edit'])),
  JSON.stringify({ checked: false, indeterminate: false }),
  'A POS permission group without selected permissions should be unchecked',
)

assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState(['pos.view'], ['pos.view', 'pos.edit'])),
  JSON.stringify({ checked: false, indeterminate: true }),
  'A partially selected POS permission group should be indeterminate',
)

assertEqual(
  JSON.stringify(getPosPermissionGroupSelectionState(['pos.view', 'pos.edit'], ['pos.view', 'pos.edit'])),
  JSON.stringify({ checked: true, indeterminate: false }),
  'A fully selected POS permission group should be checked',
)

assertEqual(
  isInheritedPosPermissionMode('Inherited'),
  true,
  'Inherited POS mode should be recognized case-insensitively',
)

assertEqual(
  isInheritedPosPermissionMode('Override'),
  false,
  'Override POS mode should not be treated as inherited',
)

assertEqual(
  shouldEnablePosPermissionSave('Inherited', false),
  true,
  'Inherited POS mode should allow saving an unchanged effective snapshot',
)

assertEqual(
  shouldEnablePosPermissionSave('Override', false),
  false,
  'Unchanged override mode should keep save disabled',
)

assertEqual(
  shouldEnablePosPermissionSave('Override', true),
  true,
  'Changed override mode should allow saving',
)

assertEqual(
  isCurrentPosPermissionRequest(
    { sequence: 3, userGuid: 'user-a', storeGuid: 'store-b' },
    { sequence: 3, userGuid: 'user-a', storeGuid: 'store-b' },
  ),
  true,
  'Matching POS permission request target should be current',
)

assertEqual(
  isCurrentPosPermissionRequest(
    { sequence: 2, userGuid: 'user-a', storeGuid: 'store-a' },
    { sequence: 3, userGuid: 'user-a', storeGuid: 'store-b' },
  ),
  false,
  'Older POS permission response must not overwrite the latest store request',
)

const permissionState: UserPermissionStateDto = {
  userGuid: 'user-a-guid',
  inheritedPermissionCodes: ['Orders.View', 'StoreProducts.View'],
  directPermissionCodes: ['Reports.Export'],
  effectivePermissionCodes: ['Orders.View', 'StoreProducts.View', 'Reports.Export'],
  inheritedSources: [
    { roleName: 'StoreManager', permissionCodes: ['Orders.View', 'StoreProducts.View'] },
  ],
}

assertArrayEqual(
  getCheckedPermissionKeys(permissionState),
  ['Orders.View', 'StoreProducts.View', 'Reports.Export'],
  'Checked permission keys should include inherited and direct permissions',
)

assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: permissionState.directPermissionCodes,
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: 'Orders.View',
    checked: false,
  }),
  ['Reports.Export'],
  'Inherited permissions should not be removed from direct permission payload',
)

assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: permissionState.directPermissionCodes,
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: 'Orders.Create',
    checked: true,
  }),
  ['Reports.Export', 'Orders.Create'],
  'Unchecked permissions should be addable as direct permissions',
)

assertArrayEqual(
  toggleDirectPermission({
    currentDirectPermissions: ['Reports.Export', 'Orders.View'],
    inheritedPermissionCodes: permissionState.inheritedPermissionCodes,
    permissionCode: 'Orders.View',
    checked: false,
  }),
  ['Reports.Export'],
  'Permission with both inherited and direct sources should keep only the direct payload removable',
)

assertArrayEqual(
  buildDirectPermissionPayload(['Reports.Export', 'Orders.Create', 'Reports.Export']),
  ['Reports.Export', 'Orders.Create'],
  'Saved payload should include unique direct permissions only',
)

assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ['Orders.View'],
    allPermissionCodes: ['Orders.View', 'Orders.Create', 'Reports.Export'],
    inheritedPermissionCodes: ['Orders.View'],
    currentDirectPermissionCodes: [],
  }),
  [],
  'Unchecking an inherited-only permission should not add it to the direct payload',
)

assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ['Orders.View'],
    allPermissionCodes: ['Orders.View', 'Orders.Create', 'Reports.Export'],
    inheritedPermissionCodes: ['Orders.View'],
    currentDirectPermissionCodes: ['Orders.View', 'Reports.Export'],
  }),
  ['Orders.View'],
  'Unrelated Tree changes should preserve direct permission payloads that are also inherited',
)

assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: ['Orders.Create', 'Reports.Export'],
    allPermissionCodes: ['Orders.View', 'Orders.Create', 'Reports.Export'],
    inheritedPermissionCodes: ['Orders.View'],
    currentDirectPermissionCodes: [],
  }),
  ['Orders.Create', 'Reports.Export'],
  'Checking a category should add only non-inherited permissions as direct permissions',
)

assertArrayEqual(
  deriveDirectPermissionKeysFromChecked({
    checkedPermissionKeys: [],
    allPermissionCodes: ['Orders.View', 'Orders.Create', 'Reports.Export'],
    inheritedPermissionCodes: ['Orders.View'],
    currentDirectPermissionCodes: ['Orders.View', 'Orders.Create', 'Reports.Export'],
  }),
  [],
  'Clearing a category should remove direct permissions while inherited permissions stay effective elsewhere',
)

const sourceMap = buildPermissionSourceMap(permissionState.inheritedSources)

assertArrayEqual(
  sourceMap['Orders.View'],
  ['StoreManager'],
  'Permission source map should expose role sources by permission code',
)

assertEqual(
  sourceMap['Reports.Export'],
  undefined,
  'Direct-only permissions should not have inherited role sources',
)

const fallbackPermissionState = buildFallbackUserPermissionState({
  userGuid: 'user-b-guid',
  permissions: ['Orders.View', 'Reports.Export', 'Orders.View'],
})

assertArrayEqual(
  fallbackPermissionState.inheritedPermissionCodes,
  ['Orders.View', 'Reports.Export'],
  'Fallback permission state should expose user detail permissions as effective inherited permissions',
)

assertArrayEqual(
  fallbackPermissionState.directPermissionCodes,
  [],
  'Fallback permission state should not create direct permissions without a user permission API',
)

assertArrayEqual(
  fallbackPermissionState.effectivePermissionCodes,
  ['Orders.View', 'Reports.Export'],
  'Fallback permission state should deduplicate effective permission codes',
)
