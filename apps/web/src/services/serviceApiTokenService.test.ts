import {
  buildServiceApiTokenCreateRequest,
  normalizeServiceApiToken,
  normalizeServiceApiTokenCreateResponse,
} from './serviceApiTokenService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}: expected ${expectedJson}, got ${actualJson}`)
  }
}

const token = normalizeServiceApiToken({
  id: 'token-id',
  name: 'OTA 自动发布',
  tokenPrefix: 'hbsvc_abcdef123',
  scopes: ['System.ManageAppDownloads'],
  status: 'active',
  createdAt: '2026-06-30T01:02:03Z',
  lastUsedAt: '2026-06-30T02:02:03Z',
  lastUsedIp: '203.0.113.7',
})

assertEqual(token.id, 'token-id', 'normalizer 应保留 id')
assertEqual(token.tokenPrefix, 'hbsvc_abcdef123', 'normalizer 应保留 token 前缀')
assertDeepEqual(token.scopes, ['System.ManageAppDownloads'], 'normalizer 应保留固定 scope')
assertEqual(token.status, 'active', 'normalizer 应保留状态')
assertEqual(token.lastUsedIp, '203.0.113.7', 'normalizer 应保留最后使用 IP')

const legacyScopes = normalizeServiceApiToken({
  id: 'legacy-token-id',
  scopes: 'System.ManageAppDownloads;Other.Scope',
})
assertDeepEqual(
  legacyScopes.scopes,
  ['System.ManageAppDownloads', 'Other.Scope'],
  'normalizer 应兼容分隔符格式 scope',
)

const created = normalizeServiceApiTokenCreateResponse({
  id: 'token-id',
  name: 'OTA 自动发布',
  tokenPrefix: 'hbsvc_abcdef123',
  scopes: ['System.ManageAppDownloads'],
  status: 'active',
  token: 'hbsvc_full_plaintext',
})

assertEqual(created.token, 'hbsvc_full_plaintext', '创建响应应保留一次性明文 token')

const createRequest = buildServiceApiTokenCreateRequest(
  ' iPad 策略读取 ',
  'pos-ipad-update-decision-reader',
)
assertDeepEqual(
  createRequest,
  {
    name: 'iPad 策略读取',
    purpose: 'pos-ipad-update-decision-reader',
  },
  '创建请求只能提交显式 allowlist purpose，不得由浏览器提交 scopes',
)
assertEqual(
  Object.prototype.hasOwnProperty.call(createRequest, 'scopes'),
  false,
  'Service Token 创建请求严禁提交 scopes',
)

for (const purpose of ['quality-ci-reporter', 'deployment-acceptance-reporter'] as const) {
  const reporterRequest = buildServiceApiTokenCreateRequest(` ${purpose} `, purpose)
  assertDeepEqual(
    reporterRequest,
    { name: purpose, purpose },
    `${purpose} 必须通过现有创建请求按 purpose 签发，且不由浏览器提交 scope`,
  )
  assertEqual(
    Object.prototype.hasOwnProperty.call(reporterRequest, 'scopes'),
    false,
    `${purpose} 创建请求不得扩展为前端可控 scopes`,
  )
}

console.log('serviceApiTokenService.test.ts: ok')
