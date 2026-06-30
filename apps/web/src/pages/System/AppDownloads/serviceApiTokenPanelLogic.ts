import type { ServiceApiTokenStatus } from '../../../types/serviceApiToken'

export function resolveServiceApiTokenApiBaseUrl(baseUrl: string, origin?: string) {
  const trimmed = baseUrl.trim()
  if (!trimmed) {
    return (origin ?? '').trim().replace(/\/+$/, '')
  }

  try {
    // 关键位置：Vite API base 常见配置是 /api，复制给移动端脚本前必须转成绝对 URL。
    const resolved = new URL(trimmed, origin || undefined)
    resolved.search = ''
    resolved.hash = ''
    return resolved.toString().replace(/\/+$/, '')
  } catch {
    return trimmed.replace(/\/+$/, '')
  }
}

export function buildServiceApiTokenEnvSnippet(baseUrl: string, token: string) {
  const normalizedBaseUrl = baseUrl.trim().replace(/\/+$/, '')
  return [`HBWEB_API_BASE_URL=${normalizedBaseUrl}`, `HBWEB_API_TOKEN=${token.trim()}`].join('\n')
}

export function resolveServiceApiTokenStatusColor(status?: ServiceApiTokenStatus | null) {
  switch ((status ?? '').toLowerCase()) {
    case 'active':
      return 'green'
    case 'revoked':
      return 'red'
    case 'expired':
      return 'orange'
    default:
      return 'default'
  }
}

export function canRevokeServiceApiToken(status?: ServiceApiTokenStatus | null) {
  return (status ?? '').toLowerCase() === 'active'
}
