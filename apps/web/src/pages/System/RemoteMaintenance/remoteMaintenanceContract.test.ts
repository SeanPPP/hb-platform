import { readFileSync } from 'node:fs'
import { join } from 'node:path'

function assert(condition: unknown, message: string) {
  if (!condition) throw new Error(message)
}

const pageSource = readFileSync(join(process.cwd(), 'src/pages/System/RemoteMaintenance/index.tsx'), 'utf8')
const serviceSource = readFileSync(join(process.cwd(), 'src/services/remoteMaintenanceService.ts'), 'utf8')
const requestSource = readFileSync(join(process.cwd(), 'src/utils/request.ts'), 'utf8')
const routeSource = readFileSync(join(process.cwd(), 'src/router/routes.tsx'), 'utf8')

assert(routeSource.includes("path: '/system/remote-maintenance'"), 'route should be registered')
assert(routeSource.includes("accessKey: 'isAdmin'"), 'route must use the existing admin role gate')
assert(serviceSource.includes("const BASE_PATH = '/api/remote-maintenance/admin'"), 'remote maintenance API base must match contract')
assert(serviceSource.includes('`${BASE_PATH}/devices`'), 'device endpoint must match contract')
assert(serviceSource.includes('`${BASE_PATH}/manifest`'), 'manifest endpoint must match contract')
assert(serviceSource.includes("cache: 'no-store'"), 'credential request must disable browser caching')
assert(requestSource.includes('cache?: RequestCache') && requestSource.includes('cache,') , 'request layer must pass through optional cache mode')
assert(serviceSource.includes("'status-agent'"), 'status agent artifact kind must match contract')
assert(!pageSource.includes('password='), 'password must never be placed in a URL')
assert(pageSource.includes('requestGateRef.current.isCurrent(requestId)'), 'late device requests must be ignored')
assert(pageSource.includes('new AbortController()'), 'device and credential requests must be cancellable')
assert(pageSource.includes('createRemoteMaintenancePollScheduler'), 'polling must use a lifecycle-aware scheduler')
assert(pageSource.includes('document.addEventListener(\'visibilitychange\''), 'visibility listener must be installed')
assert(pageSource.includes('if (!isAdmin) return'), 'non-admin renders must not start a device poll')
assert(pageSource.includes('requestGateRef.current.invalidate()'), 'hidden/cleanup must invalidate in-flight requests')
assert(pageSource.includes('setLoadError'), 'failed refresh must expose a stale data warning')
assert(pageSource.includes('record.onlineStatus === \'offline\''), 'offline rows must retain and label last service status')
assert(pageSource.includes('setCredentialPassword(null)'), 'credential state must clear before and after use')

console.log('remoteMaintenanceContract.test: ok')
