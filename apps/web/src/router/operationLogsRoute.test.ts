import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import type { WebMenuPreviewNode } from '../utils/webMenuPreview'

const storage = new Map<string, string>()

Object.defineProperty(globalThis, 'localStorage', {
  value: {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
    removeItem: (key: string) => storage.delete(key),
  },
  configurable: true,
})

const { buildRolePreviewAccess } = await import('../utils/roleMenuPreview')
const { buildWebRoleMenuPreview, getAccessKeyPermissionCodes } = await import('../utils/webMenuPreview')
const { P } = await import('../types/permissions')

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function findNode(nodes: WebMenuPreviewNode[], path: string): WebMenuPreviewNode | undefined {
  for (const node of nodes) {
    if (node.path === path) {
      return node
    }
    const child = node.children ? findNode(node.children, path) : undefined
    if (child) {
      return child
    }
  }
  return undefined
}

const access = buildRolePreviewAccess({
  roleGuid: 'operation-audit-role',
  roleName: 'StoreManager',
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [P.PosTerminal.AuditView],
  effectivePermissionCodes: [P.PosTerminal.AuditView],
})

const routeSource = readFileSync(join(process.cwd(), 'src/router/routes.tsx'), 'utf8')

assertEqual(
  routeSource.includes("import PosAdminOperationLogsPage from '../pages/PosAdmin/OperationLogs'") &&
    routeSource.includes("path: '/pos-admin/operation-logs'") &&
    routeSource.includes("title: 'menu.operationLogs'") &&
    routeSource.includes("accessKey: 'canViewOperationAudits'") &&
    routeSource.includes('element: <PosAdminOperationLogsPage />'),
  true,
  '操作日志路由应注册页面和独立权限',
)

assertEqual(
  getAccessKeyPermissionCodes('canViewOperationAudits').join(','),
  P.PosTerminal.AuditView,
  '操作日志菜单应映射到查看审计权限',
)

const preview = buildWebRoleMenuPreview(access, (key) => key, { includeHidden: true })
assertEqual(
  Boolean(findNode(preview, '/pos-admin/operation-logs')),
  true,
  '角色菜单预览应包含操作日志入口',
)

console.log('operationLogsRoute.test: ok')
