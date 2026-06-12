import type { ReactNode } from 'react'
import { useAuthStore } from '../store/auth'

interface HasPermissionProps {
  /** Permission code string, e.g. 'Users.Create' or P.Users.Create */
  code: string
  children: ReactNode
  /** Optional fallback rendered when permission is denied */
  fallback?: ReactNode
}

/**
 * Conditionally renders children based on the current user's permission.
 *
 * @example
 * <HasPermission code={P.Users.Create}>
 *   <Button>Create User</Button>
 * </HasPermission>
 *
 * <HasPermission code="Users.Delete" fallback={<span>No access</span>}>
 *   <Button danger>Delete</Button>
 * </HasPermission>
 */
export function HasPermission({ code, children, fallback = null }: HasPermissionProps) {
  const access = useAuthStore((s) => s.access)

  if (access.hasPermission(code)) {
    return <>{children}</>
  }

  return <>{fallback}</>
}

/**
 * Render-time permission check — no component wrapper needed.
 *
 * @example
 * const canCreate = usePermission('Users.Create')
 */
export function usePermission(code: string): boolean {
  return useAuthStore((s) => s.access.hasPermission(code))
}
