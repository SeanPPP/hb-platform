import { readFileSync } from 'node:fs'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

const routeSource = readFileSync('src/router/routes.tsx', 'utf8')
const menuPreviewSource = readFileSync('src/utils/webMenuPreview.ts', 'utf8')
assert(
  routeSource.includes("import EmergencyLoginKeysPage from '../pages/System/EmergencyLoginKeys'"),
  'Routes should import the emergency login keys page',
)
assert(routeSource.includes("path: '/system/emergency-login-keys'"), 'Route should use the fixed system path')
assert(routeSource.includes("title: 'menu.emergencyLoginKeys'"), 'Route should use the localized menu key')
assert(routeSource.includes("accessKey: 'canManageSystemSettings'"), 'Route should use system settings access')
assert(routeSource.includes('element: <EmergencyLoginKeysPage />'), 'Route should render the new page')
assert(
  menuPreviewSource.includes("{ path: '/system/emergency-login-keys', title: 'menu.emergencyLoginKeys', accessKey: 'canManageSystemSettings' }"),
  'Role Web menu preview should expose the new route with system settings access',
)

const packageSource = readFileSync('package.json', 'utf8')
assert(packageSource.includes('"test:emergency-login-keys"'), 'Package should expose the focused test script')
assert(
  packageSource.match(/"test"\s*:\s*"[^"]*test:emergency-login-keys/),
  'Default npm test should include emergency login key tests',
)

console.log('emergencyLoginKeysRoute.test: ok')
