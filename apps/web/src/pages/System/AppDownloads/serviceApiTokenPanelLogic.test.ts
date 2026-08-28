import { readFileSync } from 'node:fs'
import { join } from 'node:path'
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
  'mobile-ota-publisher',
)

assertEqual(
  envSnippet,
  'HBWEB_API_BASE_URL=https://hotbargain.vip/api\nHBWEB_API_TOKEN=hbsvc_test_token',
  '环境变量片段应保留既有 OTA 脚本协议并清理空白',
)
assertEqual(
  buildServiceApiTokenEnvSnippet(
    'https://hotbargain.vip/api/',
    ' hbsvc_reader_token ',
    'pos-ipad-update-decision-reader',
  ),
  'HBPOS_APP_UPDATE_DECISION_READ_TOKEN=hbsvc_reader_token',
  'iPad 决策读取 Token 必须输出 POS API 专用环境变量且不得混入发布凭据',
)
assertEqual(
  buildServiceApiTokenEnvSnippet(
    'https://hotbargain.vip/api/',
    ' hbsvc_quality_token ',
    'quality-ci-reporter',
  ),
  'QUALITY_BASELINE_SERVICE_URL=https://hotbargain.vip\nQUALITY_BASELINE_SERVICE_TOKEN=hbsvc_quality_token',
  '质量 CI Reporter 必须复制脚本约定的 origin 与专用 Token 环境变量',
)
assertEqual(
  buildServiceApiTokenEnvSnippet(
    'https://hotbargain.vip/api/',
    ' hbsvc_deployment_token ',
    'deployment-acceptance-reporter',
  ),
  'PERFORMANCE_SERVICE_URL=https://hotbargain.vip\nPERFORMANCE_SERVICE_TOKEN=hbsvc_deployment_token',
  '部署验收 Reporter 必须复制发布事件脚本约定的 origin 与专用 Token 环境变量',
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

const panelSource = readFileSync(join(process.cwd(), 'src/pages/System/AppDownloads/ServiceApiTokensPanel.tsx'), 'utf8')
const typeSource = readFileSync(join(process.cwd(), 'src/types/serviceApiToken.ts'), 'utf8')
const zh = JSON.parse(readFileSync(join(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))
const en = JSON.parse(readFileSync(join(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))

for (const purpose of ['quality-ci-reporter', 'deployment-acceptance-reporter']) {
  assertEqual(typeSource.includes(`| '${purpose}'`), true, `${purpose} 必须加入前端 purpose 类型白名单`)
  assertEqual(panelSource.includes(`value: '${purpose}'`), true, `${purpose} 必须出现在管理员签发下拉框`)
  assertEqual(
    Boolean(zh.system?.appDownloads?.serviceTokens?.purposes?.[purpose]?.label) &&
      Boolean(zh.system?.appDownloads?.serviceTokens?.purposes?.[purpose]?.description),
    true,
    `${purpose} 必须提供中文标签和用途说明`,
  )
  assertEqual(
    Boolean(en.system?.appDownloads?.serviceTokens?.purposes?.[purpose]?.label) &&
      Boolean(en.system?.appDownloads?.serviceTokens?.purposes?.[purpose]?.description),
    true,
    `${purpose} 必须提供英文标签和用途说明`,
  )
}

console.log('serviceApiTokenPanelLogic.test.ts: ok')
