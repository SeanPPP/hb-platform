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
  let currentQuery = 'page 1'

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

  // 模拟查询已提交新 render，随后 React layout cleanup 先于被挂起 mutation 的 continuation 执行。
  currentQuery = 'page 2'
  cleanupLayout()
  mutation.resolve()
  await mutationRun

  assertEqual(apiCalls, 0, 'layout cleanup 后 mutation 不得重新 begin 列表请求')
  assertEqual(loading, false, 'layout cleanup 后不得留下列表 loading')
}

const guard = createLatestRequestGuard()
const state = { data: '', total: 0, error: '', loading: false }

function handlers(total: number) {
  return {
    onStart: () => { state.loading = true },
    onSuccess: (data: string) => {
      state.data = data
      state.total = total
    },
    onError: (error: unknown) => { state.error = String(error) },
    onSettled: () => { state.loading = false },
  }
}

const first = createDeferred<string>()
const firstRun = runLatestGuardedRequest(guard, () => first.promise, handlers(1))
const second = createDeferred<string>()
const secondRun = runLatestGuardedRequest(guard, () => second.promise, handlers(2))

first.resolve('stale first')
await firstRun
assertEqual(state.data, '', '旧请求先完成时不能写列表')
assertEqual(state.total, 0, '旧请求先完成时不能写总数')
assertEqual(state.loading, true, '旧 finally 不能关闭最新请求 loading')

second.resolve('latest second')
await secondRun
assertEqual(state.data, 'latest second', '最新请求应更新列表')
assertEqual(state.total, 2, '最新请求应更新总数')
assertEqual(state.loading, false, '最新请求应结束 loading')

const lateOld = createDeferred<string>()
const lateOldRun = runLatestGuardedRequest(guard, () => lateOld.promise, handlers(3))
const earlyLatest = createDeferred<string>()
const earlyLatestRun = runLatestGuardedRequest(guard, () => earlyLatest.promise, handlers(4))

earlyLatest.resolve('early latest')
await earlyLatestRun
lateOld.resolve('late stale')
await lateOldRun
assertEqual(state.data, 'early latest', '新请求先完成后旧响应仍不能覆盖列表')
assertEqual(state.total, 4, '新请求先完成后旧响应仍不能覆盖总数')

const staleFailure = createDeferred<string>()
const staleFailureRun = runLatestGuardedRequest(guard, () => staleFailure.promise, handlers(5))
const retry = createDeferred<string>()
const retryRun = runLatestGuardedRequest(guard, () => retry.promise, handlers(6))

staleFailure.reject(new Error('stale failure'))
await staleFailureRun
assertEqual(state.error, '', '旧失败不能显示错误')
assertEqual(state.loading, true, '旧失败 finally 不能关闭重试 loading')
retry.resolve('retry latest')
await retryRun

const mutation = createDeferred<void>()
let refreshedQuery = ''
const mountedRef = { current: true }
const latestLoadDataRef = {
  current: async () => { refreshedQuery = 'page 1' },
}
const mutationRun = (async () => {
  await mutation.promise
  if (mountedRef.current) {
    await latestLoadDataRef.current()
  }
})()

// mutation 等待期间翻页；完成后必须调用新 render 写入 ref 的 loader。
latestLoadDataRef.current = async () => { refreshedQuery = 'page 2' }
mutation.resolve()
await mutationRun
assertEqual(refreshedQuery, 'page 2', 'mutation 完成后应刷新当前页而不是启动时页面')

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

const source = readFileSync(resolve('src/pages/PosAdmin/Advertisements/index.tsx'), 'utf8')
const loaderStart = source.indexOf('  const loadData = async () => {')
const loaderEnd = source.indexOf('\n  }\n\n  // mutation', loaderStart)
const loaderSource = source.slice(loaderStart, loaderEnd)
const cleanupStart = source.indexOf('  useLayoutEffect(() => {\n    mountedRef.current = true', loaderEnd)
const cleanupEnd = source.indexOf('\n  }, [])', cleanupStart)
const cleanupSource = source.slice(cleanupStart, cleanupEnd)
const saveStart = source.indexOf('  const handleSave = async () => {', cleanupEnd)
const deleteStart = source.indexOf('  const handleDelete = async', saveStart)
const toggleStart = source.indexOf('  const handleToggleEnable = async', deleteStart)
const columnsStart = source.indexOf('  const columns:', toggleStart)
const saveSource = source.slice(saveStart, deleteStart)
const deleteSource = source.slice(deleteStart, toggleStart)
const toggleSource = source.slice(toggleStart, columnsStart)

assertEqual(loaderStart >= 0 && loaderEnd > loaderStart, true, '必须精确定位广告主列表 loadData')
assertEqual(cleanupStart >= 0 && cleanupEnd > cleanupStart, true, '必须精确定位广告页面卸载清理 effect')
assertIncludes(loaderSource, 'runLatestGuardedRequest(mainListRequestGuardRef.current', '广告主列表应使用最新请求守卫')
assertIncludes(loaderSource, 'if (!mountedRef.current) {\n      return\n    }', '广告主列表在 begin 前应确认页面仍挂载')
assertIncludes(loaderSource, 'onSuccess: (result)', '广告主列表数据只能在最新成功回调中更新')
assertIncludes(loaderSource, 'onError: (error)', '广告主列表错误只能由最新请求显示')
assertIncludes(loaderSource, 'onSettled: () => setLoading(false)', '广告主列表 loading 只能由最新请求结束')
assertIncludes(cleanupSource, 'mountedRef.current = true', '广告页面挂载后应标记当前 session 有效')
assertIncludes(cleanupSource, 'useLayoutEffect', '广告页面应在 layout cleanup 阶段关闭 session')
assertEqual(cleanupSource.indexOf('mountedRef.current = false') < cleanupSource.indexOf('mainListRequestGuardRef.current.invalidate()'), true, '广告页面卸载时应先关闭 session 再使请求失效')
assertIncludes(cleanupSource, 'mainListRequestGuardRef.current.invalidate()', '广告页面卸载时应使主列表请求失效')
assertIncludes(source, 'const latestLoadDataRef = useRef(loadData)', '广告页应保存当前 render 的 loadData')
assertIncludes(source, 'useLayoutEffect(() => {\n    latestLoadDataRef.current = loadData\n  })', '广告页应在 commit 后发布当前 loadData')
assertEqual(source.includes('\n  latestLoadDataRef.current = loadData\n'), false, '广告页不得在 render 阶段直接写 loader ref')
for (const [name, mutationSource] of [
  ['保存', saveSource],
  ['删除', deleteSource],
  ['启停', toggleSource],
] as const) {
  assertEqual(mutationSource.length > 0, true, `必须精确定位广告${name} mutation`)
  assertIncludes(mutationSource, 'if (mountedRef.current)', `广告${name}刷新前应检查页面仍挂载`)
  assertIncludes(mutationSource, 'await latestLoadDataRef.current()', `广告${name}完成后应使用当前 loader 刷新`)
  assertEqual(mutationSource.includes('await loadData()'), false, `广告${name}不得使用启动 mutation 时的旧 loader`)
}

console.log('Advertisements remotePaginationRace.test.ts: ok')
