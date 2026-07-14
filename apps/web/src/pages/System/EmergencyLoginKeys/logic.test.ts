import { readFileSync } from 'node:fs'
import enLocale from '../../../i18n/locales/en.json'
import zhLocale from '../../../i18n/locales/zh.json'
import { RequestError } from '../../../utils/request'
import {
  getEmergencyLoginKeyActionState,
  getEmergencyLoginKeyDataProtectionStatusKey,
  getLatestEmergencyLoginKeyOperator,
  getShortEmergencyLoginKeyFingerprint,
  resolveEmergencyLoginKeyErrorMessage,
} from './logic'

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

assertEqual(
  getShortEmergencyLoginKeyFingerprint('AABBCCDDEEFF00112233445566778899'),
  'AABBCCDD...778899',
  'Fingerprint should be shortened without exposing any private material',
)

const completeStaged = getEmergencyLoginKeyActionState('Staged', true)
assertEqual(completeStaged.canActivate, true, 'Staged key should activate after all devices acknowledge')
assertEqual(completeStaged.canForceActivate, false, 'Complete coverage should not show force activation')
assertEqual(completeStaged.canDiscard, true, 'Staged key should be discardable')

const incompleteStaged = getEmergencyLoginKeyActionState('Staged', false)
assertEqual(incompleteStaged.canActivate, false, 'Incomplete coverage must block normal activation')
assertEqual(incompleteStaged.canForceActivate, true, 'Incomplete coverage should offer force activation')

const active = getEmergencyLoginKeyActionState('Active', true)
assertEqual(active.canRetire, false, 'Active key must never be retired directly')
assertEqual(active.canDiscard, false, 'Active key must not be discarded')

const retiring = getEmergencyLoginKeyActionState('Retiring', true)
assertEqual(retiring.canRetire, true, 'Retiring key should allow backend-validated retirement')

const conflict = new RequestError(
  'conflict',
  409,
  { code: 'EMERGENCY_KEY_VERSION_CONFLICT' },
)
assertEqual(
  resolveEmergencyLoginKeyErrorMessage(conflict, '版本冲突，请刷新后重试', '操作失败'),
  '版本冲突，请刷新后重试',
  'HTTP 409 should map to the localized version conflict message',
)
assertEqual(
  resolveEmergencyLoginKeyErrorMessage(
    new RequestError(
      'EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE: 后端原始错误',
      200,
      { errorCode: 'EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE' },
    ),
    'conflict',
    'fallback',
    { EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE: 'Some devices have not synced the staged key.' },
  ),
  'Some devices have not synced the staged key.',
  'Known business errors should use localized messages without exposing backend codes',
)
assertEqual(
  resolveEmergencyLoginKeyErrorMessage(new Error('后端原始错误'), 'conflict', 'fallback'),
  'fallback',
  'Unknown errors should use the localized fallback instead of backend text',
)

assertEqual(
  getEmergencyLoginKeyDataProtectionStatusKey('StoredKeyDecryptFailed'),
  'StoredKeyDecryptFailed',
  'Known Data Protection status should map to its localization key',
)
assertEqual(
  getEmergencyLoginKeyDataProtectionStatusKey('future-status'),
  'Unknown',
  'Unknown Data Protection status should use a safe localized fallback',
)

assertEqual(
  getLatestEmergencyLoginKeyOperator({
    keyId: 'retired',
    status: 'Retired',
    publicKeyFingerprint: 'AA',
    createdAtUtc: '2026-07-15T00:00:00Z',
    createdBy: 'creator',
    createdReason: 'reason',
    activatedBy: 'activator',
    retiredBy: 'retirer',
  }),
  'retirer',
  'Latest operator should prefer retirement actor',
)

assertEqual(zhLocale.menu.emergencyLoginKeys, '紧急登录密钥', 'Chinese menu text should exist')
assertEqual(enLocale.menu.emergencyLoginKeys, 'Emergency Login Keys', 'English menu text should exist')
assert(typeof zhLocale.emergencyLoginKeys.versionConflict === 'string', 'Chinese conflict text should exist')
assert(typeof enLocale.emergencyLoginKeys.versionConflict === 'string', 'English conflict text should exist')
assert(
  typeof enLocale.emergencyLoginKeys.errors.EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE === 'string',
  'English business error localization should exist',
)
assert(
  typeof zhLocale.emergencyLoginKeys.dataProtectionStatuses.StoredKeyDecryptFailed === 'string',
  'Chinese Data Protection status localization should exist',
)

const pageSource = readFileSync('src/pages/System/EmergencyLoginKeys/index.tsx', 'utf8')
const conflictBranchStart = pageSource.indexOf('if (conflict)')
const conflictBranch = pageSource.slice(conflictBranchStart, conflictBranchStart + 500)
assert(conflictBranch.includes('setMutationOperation(null)'), '409 recovery should close the stale operation modal')
assert(conflictBranch.includes('mutationForm.resetFields()'), '409 recovery should clear stale reason and KID confirmation')
assert(
  conflictBranch.indexOf('setMutationOperation(null)') < conflictBranch.indexOf('await loadKeyset()'),
  '409 recovery should clear stale operation context before refreshing the new version',
)
assert(
  pageSource.includes("getEmergencyLoginKeyDataProtectionStatusKey(keyset.dataProtectionStatus)"),
  'Page should localize Data Protection status instead of rendering the raw code',
)

console.log('emergencyLoginKeys.logic.test: ok')
