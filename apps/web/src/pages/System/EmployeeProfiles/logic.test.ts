import { RequestError } from '../../../utils/request'
import {
  getChangedSensitiveFields,
  handleSensitiveReviewFailure,
  isRejectReasonValid,
  maskSensitiveSummary,
  shouldConfirmAdminSensitiveSupersede,
  shouldConfirmPendingSupersede,
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

const current = {
  bankBsb: '123-456',
  bankAccountNumber: '12345678',
  superannuationCompanyName: 'Current Super',
  superannuationCompanyCode: 'CUR',
  superannuationAccountNumber: 'SUPER-1111',
  identityType: 'Passport',
  identityId: 'P1234567',
  identityPhotoUrl: 'https://example.com/current.jpg',
}

const proposed = {
  bankBsb: '123-456',
  bankAccountNumber: '87654321',
  superannuationCompanyName: 'Current Super',
  superannuationCompanyCode: 'CUR',
  superannuationAccountNumber: 'SUPER-2222',
  identityType: 'Passport',
  identityId: 'P1234567',
  identityPhotoUrl: 'https://example.com/pending.jpg',
}

const changed = getChangedSensitiveFields(current, proposed)
assertEqual(changed.join(','), 'bankAccountNumber,superannuationAccountNumber,identityPhotoUrl', '应只计算真正变化的敏感字段')
assertEqual(getChangedSensitiveFields(current, { ...current, bankBsb: ' 123-456 ' }).length, 0, '比较前应规范化首尾空白')

const masked = maskSensitiveSummary('full-sensitive-account-6789')
assertEqual(masked, '****6789', '列表摘要只保留末四位')
assert(!masked.includes('full-sensitive-account'), '列表摘要不得泄露完整账号')

assertEqual(isRejectReasonValid('  '), false, '拒绝原因不得为空')
assertEqual(isRejectReasonValid('资料无法核验'), true, '非空拒绝原因应通过校验')

assertEqual(
  shouldConfirmPendingSupersede({ status: 'Pending' }, current, { ...current, address: 'new address' }),
  false,
  '仅修改非敏感资料时不应误导用户待审申请会作废',
)
assertEqual(
  shouldConfirmPendingSupersede({ status: 'Pending' }, current, { ...current, bankBsb: '999-999' }),
  true,
  '存在待审申请且管理员修改敏感资料时必须确认作废提示',
)
assertEqual(
  shouldConfirmPendingSupersede({ status: 'Approved' }, current, { ...current, bankBsb: '999-999' }),
  false,
  '非 Pending 申请不应触发作废提示',
)

assertEqual(
  shouldConfirmAdminSensitiveSupersede(
    { status: 'Pending' },
    { ...current, identityPhotoUrlExpiresAt: '2026-07-16T01:00:00Z' },
    { ...current, identityPhotoUrl: 'https://example.com/ignored-managed-change.jpg' },
  ),
  false,
  'managed signed URL 不会被管理员 PUT 修改，因此不应误提示待审作废',
)
assertEqual(
  shouldConfirmAdminSensitiveSupersede(
    { status: 'Pending' },
    current,
    { ...current, identityPhotoUrl: 'https://example.com/legacy-change.jpg' },
  ),
  true,
  'legacy identity URL 可被管理员 PUT 修改，应触发待审作废确认',
)

let detailRefreshes = 0
let listRefreshes = 0
const conflict = new RequestError('版本冲突', 409, {
  success: false,
  errorCode: 'EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT',
})
const handled = await handleSensitiveReviewFailure(
  conflict,
  async () => { detailRefreshes += 1 },
  async () => { listRefreshes += 1 },
)
assertEqual(handled, true, '敏感资料版本冲突应被识别')
assertEqual(detailRefreshes, 1, '版本冲突后应刷新审核详情')
assertEqual(listRefreshes, 1, '版本冲突后应刷新审核列表')

const genericHandled = await handleSensitiveReviewFailure(
  new RequestError('普通错误', 500, { errorCode: 'INTERNAL_SERVER_ERROR' }),
  async () => { detailRefreshes += 1 },
  async () => { listRefreshes += 1 },
)
assertEqual(genericHandled, false, '普通错误不得误判为版本冲突')

console.log('employeeProfiles.logic.test: ok')
