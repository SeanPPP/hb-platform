import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import type { CurrentUser } from '../../../types/auth'
import type { WebMenuPreviewNode } from '../../../utils/webMenuPreview'
import { P } from '../../../types/permissions'
import { buildAccess } from '../../../utils/access'
import {
  getDefaultWebPath,
  resolveAuthorizedWebTarget,
  WEB_NO_ACCESS_PATH,
} from '../../../utils/webPortalAccess'
import {
  buildWebRoleMenuPreview,
  getAccessKeyPermissionCodes,
} from '../../../utils/webMenuPreview'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function createCurrentUser(permissions: string[]): CurrentUser {
  return {
    userGUID: 'performance-baseline-user',
    username: 'performance-baseline-user',
    email: 'performance-baseline@example.invalid',
    permissions,
    exactPermissions: permissions,
    roleNames: ['User'],
    storeNames: [],
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

const performancePermission = P.System.ViewPerformanceBaseline
const managePerformancePermission = P.System.ManagePerformanceBaseline
assertEqual(
  performancePermission,
  'System.ViewPerformanceBaseline',
  '前端权限常量应与后端 System.ViewPerformanceBaseline 保持一致',
)
assertEqual(
  managePerformancePermission,
  'System.ManagePerformanceBaseline',
  '冻结基线权限常量应与后端 System.ManagePerformanceBaseline 保持一致',
)
const performanceOnlyAccess = buildAccess(createCurrentUser([performancePermission]))

assertEqual(
  performanceOnlyAccess.canViewPerformanceBaseline,
  true,
  '仅有 System.ViewPerformanceBaseline 的用户应获得性能基线页面权限',
)
assertEqual(
  performanceOnlyAccess.canAccessAdminShell,
  true,
  '仅有 System.ViewPerformanceBaseline 的用户应能进入后台壳',
)
assertEqual(
  getDefaultWebPath(performanceOnlyAccess),
  '/system/performance-baseline',
  '仅有性能基线权限的用户登录后应进入性能基线页',
)
assertEqual(
  resolveAuthorizedWebTarget('/system/performance-baseline', performanceOnlyAccess),
  '/system/performance-baseline',
  '仅有性能基线权限的用户应能保留性能基线目标地址',
)
assertEqual(
  getAccessKeyPermissionCodes('canViewPerformanceBaseline').join(','),
  performancePermission,
  '性能基线菜单应只映射到专用查看权限',
)

const performancePreview = buildWebRoleMenuPreview(performanceOnlyAccess, (key) => key)
assertEqual(
  Boolean(findNode(performancePreview, '/system/performance-baseline')),
  true,
  '仅有性能基线权限时菜单应展示性能基线入口',
)

const deniedAccess = buildAccess(createCurrentUser([]))
assertEqual(
  deniedAccess.canViewPerformanceBaseline,
  false,
  '无性能基线权限的用户不得获得页面权限',
)
assertEqual(deniedAccess.canAccessAdminShell, false, '无任何后台权限的用户不得进入后台壳')
assertEqual(getDefaultWebPath(deniedAccess), WEB_NO_ACCESS_PATH, '无权限用户应进入拒绝页')
assertEqual(
  Boolean(findNode(buildWebRoleMenuPreview(deniedAccess, (key) => key), '/system/performance-baseline')),
  false,
  '无性能基线权限时菜单不得展示性能基线入口',
)

const manageOnlyAccess = buildAccess(createCurrentUser([managePerformancePermission]))
assertEqual(
  manageOnlyAccess.canViewPerformanceBaseline,
  false,
  '仅有管理权限不得隐式获得性能基线查看权限',
)
assertEqual(manageOnlyAccess.canAccessAdminShell, false, '仅有管理权限不得进入后台壳')
assertEqual(getDefaultWebPath(manageOnlyAccess), WEB_NO_ACCESS_PATH, '仅有管理权限仍应进入拒绝页')
assertEqual(
  resolveAuthorizedWebTarget('/system/performance-baseline', manageOnlyAccess),
  undefined,
  '仅有管理权限不得进入性能基线页面',
)

const viewAndManageAccess = buildAccess(
  createCurrentUser([performancePermission, managePerformancePermission]),
)
assertEqual(viewAndManageAccess.canViewPerformanceBaseline, true, '查看加管理权限应能进入页面')
assertEqual(
  viewAndManageAccess.hasPermission(managePerformancePermission),
  true,
  '查看加管理权限应能显示冻结操作',
)

const existingEntryCases = [
  [P.Dashboard.View, '/dashboard'],
  [P.System.ManageSettings, '/system/invoice-email-settings'],
  [P.System.ViewAppDownloads, '/system/app-downloads'],
  [P.PosTerminal.AuditView, '/pos-admin/operation-logs'],
] as const

for (const [permission, expectedPath] of existingEntryCases) {
  const access = buildAccess(createCurrentUser([permission]))
  assertEqual(
    getDefaultWebPath(access),
    expectedPath,
    `${permission} 的既有后台默认入口不得被性能基线权限改动`,
  )
  assertEqual(
    resolveAuthorizedWebTarget(expectedPath, access),
    expectedPath,
    `${permission} 的既有授权地址行为不得改变`,
  )

  assertEqual(
    resolveAuthorizedWebTarget(expectedPath, performanceOnlyAccess),
    undefined,
    `仅有性能基线权限不得扩权访问 ${expectedPath}`,
  )

  const combinedAccess = buildAccess(createCurrentUser([permission, performancePermission]))
  assertEqual(
    getDefaultWebPath(combinedAccess),
    expectedPath,
    `${permission} 与性能基线权限组合时应继续保留既有默认入口`,
  )
  assertEqual(
    resolveAuthorizedWebTarget(expectedPath, combinedAccess),
    expectedPath,
    `${permission} 与性能基线权限组合时既有授权地址不得改变`,
  )
}

const routeSource = readFileSync(join(process.cwd(), 'src/router/routes.tsx'), 'utf8')
const pageSource = readFileSync(
  join(process.cwd(), 'src/pages/System/PerformanceBaseline/index.tsx'),
  'utf8',
)
assertEqual(
  routeSource.includes("import SystemPerformanceBaselinePage from '../pages/System/PerformanceBaseline'") &&
    routeSource.includes("path: '/system/performance-baseline'") &&
    routeSource.includes("title: 'menu.performanceBaseline'") &&
    routeSource.includes("accessKey: 'canViewPerformanceBaseline'") &&
    routeSource.includes('element: <SystemPerformanceBaselinePage />'),
  true,
  '性能基线页面应以专用权限接入既有系统路由',
)
assertEqual(
  pageSource.includes('P.System.ManagePerformanceBaseline') &&
    pageSource.includes('canManagePerformanceBaseline') &&
    pageSource.includes('freezePerformanceBaseline'),
  true,
  '冻结按钮必须在页面内部独立使用 ManagePerformanceBaseline 权限，不得改变查看路由权限',
)

console.log('performanceBaseline access tests: ok')
