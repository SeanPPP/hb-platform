import type { StoreOption } from '../services/storeService'
import type { CurrentUser } from '../types/auth'
import { buildAccess } from './access'
import {
  buildStoreOptionsFromUserStores,
  buildScopedStoreCodeFilter,
  filterStoreOptionsByManagedCodes,
  isStoreCodeInManagedScope,
  shouldSkipScopedStoreQuery,
  shouldSkipStoreQueryForScope,
} from './managedStoreScope'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertArrayEqual<T>(actual: T[], expected: T[], message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

function assertDeepEqual<T>(actual: T, expected: T, message: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${message}. Expected: ${expectedText}, received: ${actualText}`)
  }
}

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'current-user-guid',
    username: 'storemanager',
    email: 'storemanager@example.com',
    permissions: [],
    roleNames: ['StoreManager'],
    storeNames: [],
    stores: [],
    ...overrides,
  }
}

const allStoreOptions: StoreOption[] = [
  { label: 'Managed One', value: 'M1' },
  { label: 'Linked One', value: 'L1' },
  { label: 'Managed Two', value: 'M2' },
]

const scopedAccess = buildAccess(createCurrentUser({
  stores: [
    {
      storeGUID: 'managed-one-guid',
      storeName: 'Managed One',
      storeCode: 'M1',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'linked-one-guid',
      storeName: 'Linked One',
      storeCode: 'L1',
      isManageable: false,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'managed-two-guid',
      storeName: 'Managed Two',
      storeCode: 'M2',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
  ],
}))

assertArrayEqual(
  scopedAccess.managedStoreCodes() ?? [],
  ['M1', 'M2'],
  'Managed store codes should include only manageable stores',
)

assertArrayEqual(
  scopedAccess.visibleStoreCodes() ?? [],
  ['M1', 'L1', 'M2'],
  'Visible store codes should include all linked stores',
)

assertArrayEqual(
  buildStoreOptionsFromUserStores(scopedAccess.visibleStoreCodes() === null ? undefined : [
    {
      storeGUID: 'managed-one-guid',
      storeName: 'Managed One',
      storeCode: 'M1',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'linked-one-guid',
      storeName: 'Linked One',
      storeCode: 'L1',
      isManageable: false,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'managed-two-guid',
      storeName: 'Managed Two',
      storeCode: 'M2',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
  ]).map((store) => store.value),
  ['L1', 'M1', 'M2'],
  'Visible store options should include linked and manageable stores sorted by name',
)

assertArrayEqual(
  buildStoreOptionsFromUserStores([
    {
      storeGUID: 'managed-one-guid',
      storeName: 'Managed One',
      storeCode: 'M1',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'linked-one-guid',
      storeName: 'Linked One',
      storeCode: 'L1',
      isManageable: false,
      assignedAt: '2026-01-01T00:00:00Z',
    },
    {
      storeGUID: 'managed-two-guid',
      storeName: 'Managed Two',
      storeCode: 'M2',
      isManageable: true,
      assignedAt: '2026-01-01T00:00:00Z',
    },
  ], { manageableOnly: true }).map((store) => store.value),
  ['M1', 'M2'],
  'Editable store options should include only manageable stores sorted by name',
)

assertArrayEqual(
  filterStoreOptionsByManagedCodes(allStoreOptions, ['M1', 'M2']).map((store) => store.value),
  ['M1', 'M2'],
  'Scoped store options should include only managed store codes',
)

assertArrayEqual(
  filterStoreOptionsByManagedCodes(allStoreOptions, null).map((store) => store.value),
  ['M1', 'L1', 'M2'],
  'Unscoped store options should include all stores',
)

assertDeepEqual(
  buildScopedStoreCodeFilter('M1', ['M1', 'M2']),
  { filterType: 'text', type: 'equals', filter: 'M1' },
  'Selected managed store should generate equals filter',
)

assertDeepEqual(
  buildScopedStoreCodeFilter(undefined, ['M1', 'M2']),
  { filterType: 'set', values: ['M1', 'M2'] },
  'Multiple managed stores should generate set filter',
)

assertDeepEqual(
  buildScopedStoreCodeFilter('L1', ['M1', 'M2']),
  { filterType: 'set', values: [] },
  'Out-of-scope selected store should generate no-match filter',
)

assertEqual(
  buildScopedStoreCodeFilter(undefined, null),
  undefined,
  'Unscoped users should not get a store filter',
)

assertEqual(
  shouldSkipScopedStoreQuery([]),
  true,
  'Empty managed store scope should skip scoped queries',
)

assertEqual(
  shouldSkipStoreQueryForScope(undefined, ['M1', 'L1']),
  true,
  'Scoped visible queries should be skipped until a visible store is selected',
)

assertEqual(
  shouldSkipStoreQueryForScope('M1', ['M1', 'L1']),
  false,
  'Scoped visible queries should run for selected visible stores',
)

assertEqual(
  shouldSkipStoreQueryForScope('X1', ['M1', 'L1']),
  true,
  'Scoped visible queries should skip out-of-scope selected stores',
)

assertEqual(
  shouldSkipStoreQueryForScope(undefined, null),
  false,
  'Unscoped users may run all-store queries without selecting a store',
)

assertEqual(
  isStoreCodeInManagedScope('M2', ['M1', 'M2']),
  true,
  'Managed store record should be inside scope',
)

assertEqual(
  isStoreCodeInManagedScope('L1', ['M1', 'M2']),
  false,
  'Linked-only store record should be outside managed scope',
)
