import {
  buildServiceApiTokenEnvSnippet,
  canRevokeServiceApiToken,
  resolveServiceApiTokenApiBaseUrl,
  resolveServiceApiTokenStatusColor,
} from './serviceApiTokenPanelLogic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const envSnippet = buildServiceApiTokenEnvSnippet(
  'https://hotbargain.vip/api/',
  ' hbsvc_test_token ',
)

assertEqual(
  envSnippet,
  'HBWEB_API_BASE_URL=https://hotbargain.vip/api\nHBWEB_API_TOKEN=hbsvc_test_token',
  '环境变量片段应保留既有 OTA 脚本协议并清理空白',
)
assertEqual(
  resolveServiceApiTokenApiBaseUrl('/api', 'https://hotbargain.vip/system/app-downloads'),
  'https://hotbargain.vip/api',
  '相对 API base 应按当前站点 origin 转成移动端脚本可用的绝对 URL',
)
assertEqual(
  resolveServiceApiTokenApiBaseUrl('', 'https://hotbargain.vip/'),
  'https://hotbargain.vip',
  '空 API base 应回退到当前站点 origin',
)
assertEqual(
  resolveServiceApiTokenApiBaseUrl('https://api.hotbargain.vip/api/', 'https://hotbargain.vip'),
  'https://api.hotbargain.vip/api',
  '绝对 API base 应保留并清理末尾斜杠',
)
assertEqual(resolveServiceApiTokenStatusColor('active'), 'green', 'active 状态应显示绿色')
assertEqual(resolveServiceApiTokenStatusColor('revoked'), 'red', 'revoked 状态应显示红色')
assertEqual(resolveServiceApiTokenStatusColor('expired'), 'orange', 'expired 状态应显示橙色')
assertEqual(canRevokeServiceApiToken('active'), true, '仅 active token 可撤销')
assertEqual(canRevokeServiceApiToken('revoked'), false, '已撤销 token 不再显示撤销动作')

console.log('serviceApiTokenPanelLogic.test.ts: ok')
