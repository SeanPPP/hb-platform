import type { WebMenuPreviewNode } from '../utils/webMenuPreview'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

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

const translate = (key: string, fallback?: string) => fallback ?? key

const viewAccess = buildRolePreviewAccess({
  roleGuid: 'wpf-view-role',
  roleName: 'WpfViewRole',
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [P.System.ViewAppDownloads],
  effectivePermissionCodes: [P.System.ViewAppDownloads],
})

const noWpfAccess = buildRolePreviewAccess({
  roleGuid: 'wpf-hidden-role',
  roleName: 'WpfHiddenRole',
  isSuperAdmin: false,
  implicitAllPermissions: false,
  explicitPermissionCodes: [],
  effectivePermissionCodes: [],
})

const routeSource = readFileSync(join(process.cwd(), 'src/router/routes.tsx'), 'utf8')

assertEqual(
  routeSource.includes("import SystemWpfVersionsPage from '../pages/System/WpfVersions'"),
  true,
  'Routes should import the WPF versions page',
)

assertEqual(
  routeSource.includes("path: '/system/wpf-versions'") &&
    routeSource.includes("title: 'menu.wpfVersions'") &&
    routeSource.includes("accessKey: 'canViewAppDownloads'") &&
    routeSource.includes('element: <SystemWpfVersionsPage />'),
  true,
  'WPF versions route should be registered with reused App Downloads view permission',
)

assertEqual(
  getAccessKeyPermissionCodes('canViewAppDownloads').join(','),
  `${P.System.ViewAppDownloads},${P.System.ManageAppDownloads}`,
  'WPF versions menu should document view and manage permissions accepted by the route',
)

const preview = buildWebRoleMenuPreview(viewAccess, translate, { includeHidden: true })
const wpfVersionsMenu = findNode(preview, '/system/wpf-versions')

assertEqual(Boolean(wpfVersionsMenu), true, 'Web role preview should include the WPF versions menu')
assertEqual(
  wpfVersionsMenu?.permissionCodes.join(','),
  `${P.System.ViewAppDownloads},${P.System.ManageAppDownloads}`,
  'WPF versions menu preview should display both accepted WPF version permissions',
)

const hiddenPreview = buildWebRoleMenuPreview(noWpfAccess, translate, {
  includeHidden: true,
  explicitPermissionCodes: [],
})
const hiddenWpfVersionsMenu = findNode(hiddenPreview, '/system/wpf-versions')

assertEqual(
  hiddenWpfVersionsMenu?.edit.addPermissionCodes.includes(P.System.ViewAppDownloads),
  true,
  'Adding the hidden WPF versions menu should grant only the view permission',
)
assertEqual(
  hiddenWpfVersionsMenu?.edit.addPermissionCodes.includes(P.System.ManageAppDownloads),
  false,
  'Adding the hidden WPF versions menu should not promote the role to manage permission',
)
assertEqual(
  wpfVersionsMenu?.edit.removePermissionCodes.join(','),
  P.System.ViewAppDownloads,
  'Removing the WPF versions menu should not delete manage permission together with view permission',
)

console.log('wpfVersionsRoute.test: ok')
