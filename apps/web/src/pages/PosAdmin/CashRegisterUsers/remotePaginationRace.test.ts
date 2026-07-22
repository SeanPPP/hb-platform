import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../../utils/latestRequestGuard'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

function assertIncludes(source: string, expected: string, label: string) {
  if (!source.includes(expected)) {
    throw new Error(`${label}. Missing: ${expected}`)
  }
}

function createDeferred<T>() {
  let resolvePromise!: (value: T) => void
  let rejectPromise!: (error: unknown) => void
  const promise = new Promise<T>((resolvePromiseValue, rejectPromiseValue) => {
    resolvePromise = resolvePromiseValue
    rejectPromise = rejectPromiseValue
  })
  return { promise, resolve: resolvePromise, reject: rejectPromise }
}

async function assertLayoutCleanupBlocksReload() {
  const lifecycleGuard = createLatestRequestGuard()
  let mounted = false
  let apiCalls = 0
  let loading = false
  let currentQuery = 'all stores'

  const currentLoader = async () => {
    if (!mounted) return
    await runLatestGuardedRequest(lifecycleGuard, async () => {
      apiCalls += 1
      return currentQuery
    }, {
      onStart: () => { loading = true },
      onSuccess: () => {},
      onSettled: () => { loading = false },
    })
  }
  const commitLayout = () => { mounted = true }
  const cleanupLayout = () => {
    mounted = false
    lifecycleGuard.invalidate()
  }

  const mutation = createDeferred<void>()
  commitLayout()
  const mutationRun = (async () => {
    await mutation.promise
    await currentLoader()
  })()

  // mutation 等待期间 scope 已切换，新 render 提交后先执行 layout cleanup。
  currentQuery = 'managed stores'
  cleanupLayout()
  mutation.resolve()
  await mutationRun

  assertEqual(apiCalls, 0, 'layout cleanup 后 mutation 不得重新 begin 列表请求')
  assertEqual(loading, false, 'layout cleanup 后不得留下列表 loading')
}

const sequenceGuard = createLatestRequestGuard()
const sequenceState = { data: '', loading: false }
const sequenceHandlers = {
  onStart: () => { sequenceState.loading = true },
  onSuccess: (data: string) => { sequenceState.data = data },
  onSettled: () => { sequenceState.loading = false },
}

const aFirst = createDeferred<string>()
const aFirstRun = runLatestGuardedRequest(sequenceGuard, () => aFirst.promise, sequenceHandlers)
const bSecond = createDeferred<string>()
const bSecondRun = runLatestGuardedRequest(sequenceGuard, () => bSecond.promise, sequenceHandlers)
aFirst.resolve('stale A')
await aFirstRun
assertEqual(sequenceState.data, '', 'A 先完成时不能覆盖 B')
assertEqual(sequenceState.loading, true, 'A 的 finally 不能关闭 B 的 loading')
bSecond.resolve('latest B')
await bSecondRun
assertEqual(sequenceState.data, 'latest B', 'B 应更新列表')

const aLate = createDeferred<string>()
const aLateRun = runLatestGuardedRequest(sequenceGuard, () => aLate.promise, sequenceHandlers)
const bEarly = createDeferred<string>()
const bEarlyRun = runLatestGuardedRequest(sequenceGuard, () => bEarly.promise, sequenceHandlers)
bEarly.resolve('early latest B')
await bEarlyRun
aLate.resolve('late stale A')
await aLateRun
assertEqual(sequenceState.data, 'early latest B', 'B 先完成后晚到的 A 仍不能覆盖列表')

const guard = createLatestRequestGuard()
const state = {
  data: ['existing'],
  total: 1,
  selectedRows: ['selected'],
  loading: false,
}

const request = createDeferred<string[]>()
const run = runLatestGuardedRequest(guard, () => request.promise, {
  onStart: () => { state.loading = true },
  onSuccess: (data) => {
    state.data = data
    state.total = data.length
  },
  onSettled: () => { state.loading = false },
})

// 权限范围变为空时必须先使在途请求失效，并同步清理可见状态。
guard.invalidate()
state.data = []
state.total = 0
state.selectedRows = []
state.loading = false

request.resolve(['stale scoped result'])
await run
assertEqual(state.data.length, 0, 'scope skip 后旧响应不能恢复列表')
assertEqual(state.total, 0, 'scope skip 后旧响应不能恢复总数')
assertEqual(state.selectedRows.length, 0, 'scope skip 应清空选中行')
assertEqual(state.loading, false, 'scope skip 后旧 finally 不能改变 loading')

const mutation = createDeferred<void>()
let refreshedScope = ''
const mountedRef = { current: true }
const latestLoadDataRef = {
  current: async () => { refreshedScope = 'all stores' },
}
const mutationRun = (async () => {
  await mutation.promise
  if (mountedRef.current) {
    await latestLoadDataRef.current()
  }
})()

// mutation 等待期间权限范围变化；完成后必须使用能执行最新 scope skip 的 loader。
latestLoadDataRef.current = async () => { refreshedScope = 'skip empty scope' }
mutation.resolve()
await mutationRun
assertEqual(refreshedScope, 'skip empty scope', 'mutation 完成后应按最新管理范围刷新')

const pendingUnmountMutation = createDeferred<void>()
const unmountedCalls = { api: 0, start: 0, error: 0, settled: 0 }
const unmountedLoaderRef = {
  current: async () => {
    unmountedCalls.api += 1
    unmountedCalls.start += 1
    unmountedCalls.error += 1
    unmountedCalls.settled += 1
  },
}
const pendingUnmountRun = (async () => {
  await pendingUnmountMutation.promise
  if (mountedRef.current) {
    await unmountedLoaderRef.current()
  }
})()

mountedRef.current = false
pendingUnmountMutation.resolve()
await pendingUnmountRun
assertEqual(unmountedCalls.api, 0, '卸载后 mutation 不得重新调用列表 API')
assertEqual(unmountedCalls.start, 0, '卸载后 mutation 不得重新触发 onStart')
assertEqual(unmountedCalls.error, 0, '卸载后 mutation 不得重新触发错误处理')
assertEqual(unmountedCalls.settled, 0, '卸载后 mutation 不得重新触发 finally')

await assertLayoutCleanupBlocksReload()

const source = readFileSync(resolve('src/pages/PosAdmin/CashRegisterUsers/index.tsx'), 'utf8')
const loaderStart = source.indexOf('  const loadData = async () => {')
const loaderEnd = source.indexOf('\n  }\n\n  // mutation', loaderStart)
const loaderSource = source.slice(loaderStart, loaderEnd)
const skipStart = loaderSource.indexOf('if (shouldSkipScopedStoreQuery(managedStoreCodes))')
const invalidateIndex = loaderSource.indexOf('mainListRequestGuardRef.current.invalidate()', skipStart)
const clearDataIndex = loaderSource.indexOf('setData([])', skipStart)
const cleanupStart = source.indexOf('  useLayoutEffect(() => {\n    mountedRef.current = true', loaderEnd)
const cleanupEnd = source.indexOf('\n  }, [])', cleanupStart)
const cleanupSource = source.slice(cleanupStart, cleanupEnd)
const createStart = source.indexOf('  const handleCreate = async () => {', cleanupEnd)
const editStart = source.indexOf('  const handleEdit =', createStart)
const updateStart = source.indexOf('  const handleUpdate = async () => {', editStart)
const deleteStart = source.indexOf('  const handleDelete = async', updateStart)
const batchStart = source.indexOf('  const handleBatchDelete = async', deleteStart)
const columnsStart = source.indexOf('  const columns:', batchStart)
const createSource = source.slice(createStart, editStart)
const updateSource = source.slice(updateStart, deleteStart)
const deleteSource = source.slice(deleteStart, batchStart)
const batchSource = source.slice(batchStart, columnsStart)

assertEqual(loaderStart >= 0 && loaderEnd > loaderStart, true, '必须精确定位收银用户主列表 loadData')
assertEqual(cleanupStart >= 0 && cleanupEnd > cleanupStart, true, '必须精确定位收银用户页面卸载清理 effect')
assertEqual(source.includes('inFlightRef'), false, '不得继续用 inFlightRef 丢弃后续查询')
assertIncludes(loaderSource, 'runLatestGuardedRequest(mainListRequestGuardRef.current', '收银用户主列表应使用最新请求守卫')
assertIncludes(loaderSource, 'if (!mountedRef.current) {\n      return\n    }', '收银用户主列表在 begin 前应确认页面仍挂载')
assertIncludes(loaderSource, 'onSuccess: (result)', '收银用户列表只能在最新成功回调中更新')
assertIncludes(loaderSource, 'onError: ()', '收银用户错误只能由最新请求显示')
assertIncludes(loaderSource, 'onSettled: () => setLoading(false)', '收银用户 loading 只能由最新请求结束')
assertEqual(skipStart >= 0 && invalidateIndex > skipStart && clearDataIndex > invalidateIndex, true, 'scope skip 必须先 invalidate 再清空列表')
assertIncludes(loaderSource, 'setSelectedRowKeys([])', 'scope skip 应清空选中键')
assertIncludes(loaderSource, 'setSelectedRows([])', 'scope skip 应清空选中行')
assertIncludes(loaderSource, 'setLoading(false)', 'scope skip 应立即结束 loading')
assertIncludes(cleanupSource, 'mountedRef.current = true', '收银用户页挂载后应标记当前 session 有效')
assertIncludes(cleanupSource, 'useLayoutEffect', '收银用户页应在 layout cleanup 阶段关闭 session')
assertEqual(cleanupSource.indexOf('mountedRef.current = false') < cleanupSource.indexOf('mainListRequestGuardRef.current.invalidate()'), true, '收银用户页卸载时应先关闭 session 再使请求失效')
assertIncludes(cleanupSource, 'mainListRequestGuardRef.current.invalidate()', '收银用户页面卸载时应使主列表请求失效')
assertIncludes(source, 'const latestLoadDataRef = useRef(loadData)', '收银用户页应保存当前 render 的 loadData')
assertIncludes(source, 'useLayoutEffect(() => {\n    latestLoadDataRef.current = loadData\n  })', '收银用户页应在 commit 后发布当前 loadData')
assertEqual(source.includes('\n  latestLoadDataRef.current = loadData\n'), false, '收银用户页不得在 render 阶段直接写 loader ref')
for (const [name, mutationSource] of [
  ['创建', createSource],
  ['更新', updateSource],
  ['删除', deleteSource],
  ['批量删除', batchSource],
] as const) {
  assertEqual(mutationSource.length > 0, true, `必须精确定位收银用户${name} mutation`)
  assertIncludes(mutationSource, 'if (mountedRef.current)', `收银用户${name}刷新前应检查页面仍挂载`)
  assertIncludes(mutationSource, 'await latestLoadDataRef.current()', `收银用户${name}完成后应使用当前 loader 刷新`)
  assertEqual(mutationSource.includes('await loadData()'), false, `收银用户${name}不得使用启动 mutation 时的旧 loader`)
}

console.log('CashRegisterUsers remotePaginationRace.test.ts: ok')
