import type { AccessControl } from '../types/auth'

type WebPortalAccess = Pick<AccessControl, 'canAccessDashboard' | 'canAccessOrderFront'>

export const WEB_NO_ACCESS_PATH = '/web-access-denied'

export function getDefaultWebPath(access: WebPortalAccess) {
  if (access.canAccessDashboard) {
    return '/dashboard'
  }
  if (access.canAccessOrderFront) {
    return '/shop'
  }
  return WEB_NO_ACCESS_PATH
}

export function resolveAuthorizedWebTarget(target: string | null | undefined, access: WebPortalAccess) {
  if (!target || !target.startsWith('/') || target.startsWith('//') || target === '/login') {
    return undefined
  }
  if (target === '/') {
    return target
  }
  if (target === WEB_NO_ACCESS_PATH) {
    return !access.canAccessDashboard && !access.canAccessOrderFront ? target : undefined
  }
  if (/^\/shop(?:\/|[?#]|$)/.test(target)) {
    return access.canAccessOrderFront ? target : undefined
  }
  return access.canAccessDashboard ? target : undefined
}
