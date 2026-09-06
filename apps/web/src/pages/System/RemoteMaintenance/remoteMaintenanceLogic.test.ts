import {
  buildRemoteMaintenanceQuery,
  createRemoteMaintenancePollScheduler,
  createRemoteMaintenanceRequestGate,
  getRemoteOnlineStatusColor,
  getRemoteServiceStatusColor,
} from './remoteMaintenanceLogic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) throw new Error(`${message}: expected ${expectedJson}, got ${actualJson}`)
}

assertDeepEqual(
  buildRemoteMaintenanceQuery(2, 20, {
    keyword: '  POS-01 ',
    storeCode: ' S001 ',
    onlineStatus: 'offline',
    serviceStatus: 'stopped',
  }),
  {
    page: 2,
    pageSize: 20,
    keyword: 'POS-01',
    storeCode: 'S001',
    onlineStatus: 'offline',
    serviceStatus: 'stopped',
  },
  'query should trim filters and preserve all server filter names',
)

assertEqual(getRemoteOnlineStatusColor('online'), 'green', 'online status color')
assertEqual(getRemoteOnlineStatusColor('offline'), 'orange', 'offline status color')
assertEqual(getRemoteOnlineStatusColor('never'), 'default', 'never status color')
assertEqual(getRemoteServiceStatusColor('running'), 'green', 'running service color')
assertEqual(getRemoteServiceStatusColor('starting'), 'processing', 'starting service color')
assertEqual(getRemoteServiceStatusColor('stopped'), 'orange', 'stopped service color')
assertEqual(getRemoteServiceStatusColor('checkFailed'), 'red', 'failed service color')

const gate = createRemoteMaintenanceRequestGate()
const first = gate.begin()
const second = gate.begin()
assertEqual(gate.isCurrent(first), false, 'a late request must not remain current')
assertEqual(gate.isCurrent(second), true, 'the latest request must remain current')
gate.invalidate()
assertEqual(gate.isCurrent(second), false, 'visibility changes must invalidate in-flight requests')

console.log('remoteMaintenanceLogic.test: ok')

const timers = new Map<number, () => void>()
let nextTimerId = 1
let visible = true
let refreshCount = 0
let invalidationCount = 0
let resolveRefresh: (() => void) | undefined
const refreshPromise = () => new Promise<void>((resolve) => {
  refreshCount += 1
  resolveRefresh = resolve
})
const scheduler = createRemoteMaintenancePollScheduler({
  isVisible: () => visible,
  refresh: refreshPromise,
  onInvalidate: () => { invalidationCount += 1 },
  setTimer: (callback) => {
    const id = nextTimerId++
    timers.set(id, callback)
    return id
  },
  clearTimer: (id) => { timers.delete(id) },
})

scheduler.start()
assertEqual(refreshCount, 1, 'visible scheduler should refresh immediately')
scheduler.dispose()
resolveRefresh?.()
await Promise.resolve()
assertEqual(timers.size, 0, 'disposed scheduler must not recreate a timer from refresh finally')
assertEqual(invalidationCount, 1, 'dispose must invalidate the in-flight request')

const hiddenTimers = new Map<number, () => void>()
visible = false
const hiddenScheduler = createRemoteMaintenancePollScheduler({
  isVisible: () => visible,
  refresh: async () => { refreshCount += 1 },
  onInvalidate: () => { invalidationCount += 1 },
  setTimer: (callback) => { hiddenTimers.set(1, callback); return 1 },
  clearTimer: (id) => { hiddenTimers.delete(id) },
})
hiddenScheduler.start()
assertEqual(hiddenTimers.size, 0, 'hidden scheduler must pause without starting a timer')
visible = true
hiddenScheduler.handleVisibilityChange()
await Promise.resolve()
assertEqual(hiddenTimers.size, 1, 'becoming visible should refresh then schedule a timer')
hiddenScheduler.dispose()
