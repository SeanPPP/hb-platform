import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../utils/latestRequestGuard'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual(actual: unknown, expected: unknown, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
}

function extractSourceBlock(source: string, startMarker: string, endMarker: string, label: string) {
  const start = source.indexOf(startMarker)
  assert(start >= 0, `${label}缺少开始标记`)
  const contentStart = start + startMarker.length
  const end = source.indexOf(endMarker, contentStart)
  assert(end > contentStart, `${label}缺少有效结束标记`)
  const block = source.slice(start, end)
  assert(block.trim().length > startMarker.length, `${label}不得为空`)
  return block
}

function countOccurrences(source: string, value: string) {
  return source.split(value).length - 1
}

function createDeferred<T>() {
  let resolvePromise!: (value: T) => void
  let rejectPromise!: (error: unknown) => void
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve
    rejectPromise = reject
  })
  return { promise, resolve: resolvePromise, reject: rejectPromise }
}

const guard = createLatestRequestGuard()
const state: { data: string; page: number; error: string; loading: boolean } = {
  data: '',
  page: 0,
  error: '',
  loading: false,
}

function runRequest(request: Promise<string>, page: number) {
  return runLatestGuardedRequest(guard, () => request, {
    onStart: () => { state.loading = true },
    onSuccess: (value) => {
      state.data = value
      state.page = page
    },
    onError: (error) => { state.error = String(error) },
    onSettled: () => { state.loading = false },
  })
}

const first = createDeferred<string>()
const second = createDeferred<string>()
const firstRun = runRequest(first.promise, 1)
const secondRun = runRequest(second.promise, 2)

first.resolve('stale')
await firstRun
assertEqual(state.data, '', '旧成功响应不得写入数据')
assertEqual(state.page, 0, '旧成功响应不得写入页码')
assert(state.loading, '旧 finally 不得关闭最新请求的 loading')

second.resolve('latest')
await secondRun
assertEqual(state.data, 'latest', '最新成功响应应写入数据')
assertEqual(state.page, 2, '最新成功响应应写入页码')
assert(!state.loading, '最新请求完成后应关闭 loading')

const lateOldSuccess = createDeferred<string>()
const fastLatestSuccess = createDeferred<string>()
const lateOldSuccessRun = runRequest(lateOldSuccess.promise, 3)
const fastLatestSuccessRun = runRequest(fastLatestSuccess.promise, 4)
fastLatestSuccess.resolve('fast latest')
await fastLatestSuccessRun
const afterFastLatestSuccess = { ...state }
lateOldSuccess.resolve('late stale')
await lateOldSuccessRun
assertEqual(state.data, afterFastLatestSuccess.data, 'B 完成后到达的 A 旧成功不得覆盖数据')
assertEqual(state.page, afterFastLatestSuccess.page, 'B 完成后到达的 A 旧成功不得覆盖页码')
assertEqual(state.error, afterFastLatestSuccess.error, 'B 完成后到达的 A 旧成功不得改变错误')
assertEqual(state.loading, afterFastLatestSuccess.loading, 'B 完成后到达的 A 旧 finally 不得改变 loading')

const staleFailure = createDeferred<string>()
const latestAfterFailure = createDeferred<string>()
const staleFailureRun = runRequest(staleFailure.promise, 5)
const latestAfterFailureRun = runRequest(latestAfterFailure.promise, 6)
latestAfterFailure.resolve('latest after failure')
await latestAfterFailureRun
const afterLatestBeforeOldFailure = { ...state }
staleFailure.reject(new Error('stale failure'))
await staleFailureRun
assertEqual(state.data, afterLatestBeforeOldFailure.data, 'B 完成后到达的 A 旧失败不得覆盖数据')
assertEqual(state.page, afterLatestBeforeOldFailure.page, 'B 完成后到达的 A 旧失败不得覆盖页码')
assertEqual(state.error, afterLatestBeforeOldFailure.error, 'B 完成后到达的 A 旧失败不得写入错误')
assertEqual(state.loading, afterLatestBeforeOldFailure.loading, 'B 完成后到达的 A 旧失败 finally 不得改变 loading')

const invalidated = createDeferred<string>()
const invalidatedRun = runRequest(invalidated.promise, 7)
guard.invalidate()
invalidated.resolve('after unmount')
await invalidatedRun
assertEqual(state.data, 'latest after failure', '组件卸载后旧响应不得写入数据')
assertEqual(state.page, 6, '组件卸载后旧响应不得写入页码')
assert(state.loading, '组件卸载后的旧 finally 不得写入状态')

interface WpfQuerySnapshot {
  page: number
  pageSize: number
  channel: string
  includeDisabled: boolean
}

const wpfMutationGate = createDeferred<void>()
const wpfMutationGuard = createLatestRequestGuard()
let wpfMounted = true
let latestWpfQuery: WpfQuerySnapshot = {
  page: 1,
  pageSize: 10,
  channel: 'production',
  includeDisabled: false,
}
let wpfVisibleQuery = ''
const mutationRefreshQueries: string[] = []

function runWpfList(query: WpfQuerySnapshot, request: Promise<string>) {
  return runLatestGuardedRequest(wpfMutationGuard, () => request, {
    onSuccess: () => {
      wpfVisibleQuery = `${query.channel}:${query.page}:${query.pageSize}:${String(query.includeDisabled)}`
    },
  })
}

const wpfMutationRun = (async () => {
  await wpfMutationGate.promise
  if (!wpfMounted) return
  const query = latestWpfQuery
  mutationRefreshQueries.push(`${query.channel}:${query.page}:${query.pageSize}:${String(query.includeDisabled)}`)
  await runWpfList(query, Promise.resolve('mutation refresh'))
})()

// mutation 挂起时用户切换 channel/page，并完成新的列表请求。
latestWpfQuery = { page: 3, pageSize: 50, channel: 'beta', includeDisabled: true }
const currentWpfList = createDeferred<string>()
const currentWpfListRun = runWpfList(latestWpfQuery, currentWpfList.promise)
currentWpfList.resolve('current beta page 3')
await currentWpfListRun
wpfMutationGate.resolve()
await wpfMutationRun
assertEqual(mutationRefreshQueries.join(','), 'beta:3:50:true', 'WPF mutation 完成后只能使用完成时最新 UI query')
assertEqual(wpfVisibleQuery, 'beta:3:50:true', 'WPF mutation 完成后不得用旧 query 覆盖新列表')

const wpfUnmountGate = createDeferred<void>()
let wpfReloadsAfterUnmount = 0
const wpfUnmountMutationRun = (async () => {
  await wpfUnmountGate.promise
  if (wpfMounted) wpfReloadsAfterUnmount += 1
})()
wpfMounted = false
wpfMutationGuard.invalidate()
wpfUnmountGate.resolve()
await wpfUnmountMutationRun
assertEqual(wpfReloadsAfterUnmount, 0, 'WPF mutation 挂起期间卸载后不得重新 begin 列表请求')

const wpfDirectRefreshGate = createDeferred<void>()
let wpfDirectRefreshBeginsAfterUnmount = 0
const wpfDirectRefreshRun = (async () => {
  await wpfDirectRefreshGate.promise
  if (!wpfMounted) return
  wpfMutationGuard.begin()
  wpfDirectRefreshBeginsAfterUnmount += 1
})()
wpfDirectRefreshGate.resolve()
await wpfDirectRefreshRun
assertEqual(wpfDirectRefreshBeginsAfterUnmount, 0, 'WPF layout cleanup 后直接刷新不得 begin 列表请求')

interface ListQuerySnapshot {
  page: number
  keyword: string
  sort: string
}

const lateMutationGate = createDeferred<void>()
const lateMutationListGuard = createLatestRequestGuard()
let lateMutationMounted = true
let lateMutationVisibleQuery = ''
let lateMutationLoading = false
let desiredListQuery: ListQuerySnapshot = { page: 1, keyword: 'old', sort: 'createdAt:desc' }
const lateMutationStartedQueries: string[] = []
const lateMutationRequests: Array<ReturnType<typeof createDeferred<string>>> = []

function runLateMutationList(query: ListQuerySnapshot) {
  if (!lateMutationMounted) return Promise.resolve()
  // 与生产 loader 一致：在 guard begin / await 前同步 desired query。
  desiredListQuery = query
  const queryLabel = `${query.keyword}:${query.page}:${query.sort}`
  lateMutationStartedQueries.push(queryLabel)
  const request = lateMutationRequests.shift()
  if (!request) throw new Error(`缺少模拟请求: ${queryLabel}`)
  return runLatestGuardedRequest(lateMutationListGuard, () => request.promise, {
    onStart: () => { lateMutationLoading = true },
    onSuccess: () => { lateMutationVisibleQuery = queryLabel },
    onSettled: () => { lateMutationLoading = false },
  })
}

function refreshDesiredListQuery() {
  return runLateMutationList({ ...desiredListQuery })
}

// A mutation 挂起时，B 已发布 page3/sort 查询但尚未完成；A 完成触发的 C 必须沿用 B desired query。
const lateMutationRun = (async () => {
  await lateMutationGate.promise
  await refreshDesiredListQuery()
})()
const latestListLabel = 'latest:3:name:asc'
const bListRequest = createDeferred<string>()
const cListRequest = createDeferred<string>()
lateMutationRequests.push(bListRequest, cListRequest)
const bListRun = runLateMutationList({ page: 3, keyword: 'latest', sort: 'name:asc' })
lateMutationGate.resolve()
await Promise.resolve()
assertEqual(lateMutationStartedQueries.join(','), `${latestListLabel},${latestListLabel}`, 'mutation 刷新必须保留 B 已开始的分页与排序')
bListRequest.resolve('B')
await bListRun
assertEqual(lateMutationVisibleQuery, '', 'B 被 C 淘汰后不得写入列表')
assert(lateMutationLoading, 'B 的旧 finally 不得关闭 C 的 loading')
cListRequest.resolve('C')
await lateMutationRun
assertEqual(lateMutationVisibleQuery, latestListLabel, 'B/C 乱序时只有沿用 desired query 的 C 可以更新列表')
assert(!lateMutationLoading, '最新 C 完成后应关闭 loading')

const unmountedMutationGate = createDeferred<void>()
let beginsAfterLayoutCleanup = 0
const unmountedMutationRun = (async () => {
  await unmountedMutationGate.promise
  if (!lateMutationMounted) return
  beginsAfterLayoutCleanup += 1
  await refreshDesiredListQuery()
})()
lateMutationMounted = false
lateMutationListGuard.invalidate()
unmountedMutationGate.resolve()
await unmountedMutationRun
assertEqual(beginsAfterLayoutCleanup, 0, 'layout cleanup 后 mutation 不得重新 begin 列表请求')

const sources = {
  employee: readFileSync(resolve('src/pages/System/EmployeeProfiles/index.tsx'), 'utf8'),
  sensitive: readFileSync(resolve('src/pages/System/EmployeeProfiles/SensitiveChangeReviewPanel.tsx'), 'utf8'),
  roles: readFileSync(resolve('src/pages/System/Roles/index.tsx'), 'utf8'),
  scheduled: readFileSync(resolve('src/pages/System/ScheduledStatistics/index.tsx'), 'utf8'),
  wpf: readFileSync(resolve('src/pages/System/WpfVersions/index.tsx'), 'utf8'),
  users: readFileSync(resolve('src/pages/System/Users/index.tsx'), 'utf8'),
}

for (const [name, source, guardName] of [
  ['员工列表', sources.employee, 'listRequestGuardRef'],
  ['敏感资料审核列表', sources.sensitive, 'listRequestGuardRef'],
  ['角色列表', sources.roles, 'listRequestGuardRef'],
  ['任务日志列表', sources.scheduled, 'taskLogsRequestGuardRef'],
  ['WPF 版本列表', sources.wpf, 'releasesRequestGuardRef'],
  ['用户登录记录', sources.users, 'loginRecordsRequestGuardRef'],
] as const) {
  assert(source.includes(`${guardName} = useRef(createLatestRequestGuard())`), `${name}应创建独立 latest-request guard`)
  assert(source.includes(`runLatestGuardedRequest(${guardName}.current`), `${name}应通过共用执行器加载`)
  assert(source.includes(`${guardName}.current.invalidate()`), `${name}卸载或关闭时应使在途请求失效`)
}

assert(
  sources.sensitive.includes('detailRequestGuardRef = useRef(createDetailRequestGuard())')
    && sources.sensitive.includes('detailRequestGuardRef.current.isCurrent'),
  '敏感资料详情应继续使用独立的既有 guard',
)
assert(sources.users.includes('const closeLoginRecords = () =>'), '登录记录弹窗应统一关闭入口')
assert(sources.users.match(/closeLoginRecords/g)?.length === 3, '登录记录关闭按钮和 onCancel 应复用统一关闭入口')
const closeLoginRecordsBlock = extractSourceBlock(
  sources.users,
  'const closeLoginRecords = () => {',
  '\n\n  useEffect(',
  '登录记录关闭函数块',
)
assert(closeLoginRecordsBlock.includes('loginRecordsRequestGuardRef.current.invalidate()'), '关闭登录记录弹窗应立即使请求失效')
assert(closeLoginRecordsBlock.includes('setLoginRecordsLoading(false)'), '关闭登录记录弹窗应立即结束 loading')
assert(closeLoginRecordsBlock.includes('setLoginRecords([])'), '关闭登录记录弹窗应清空记录')
assert(closeLoginRecordsBlock.includes('setLoginRecordsTotal(0)'), '关闭登录记录弹窗应清空总数')
assert(closeLoginRecordsBlock.includes('setLoginRecordsPage(1)'), '关闭登录记录弹窗应重置第一页')
assert(closeLoginRecordsBlock.includes('setLoginRecordsPageSize(10)'), '关闭登录记录弹窗应保持每页 10 条')

const wpfLoader = extractSourceBlock(
  sources.wpf,
  'const loadReleases = useCallback',
  '\n\n  const latestLoadReleasesRef',
  'WPF loadReleases 函数块',
)
assert(!wpfLoader.includes('nextPage = page'), 'WPF loader 的页码参数不得依赖当前分页闭包')
assert(!wpfLoader.includes('nextChannel = channel'), 'WPF loader 的 channel 参数不得依赖当前筛选闭包')
assert(wpfLoader.includes('  ) => {\n    if (!mountedRef.current) {'), 'WPF loader 第一行应检查页面仍挂载')
assert(wpfLoader.indexOf('if (!mountedRef.current)') < wpfLoader.indexOf('runLatestGuardedRequest('), 'WPF loader 应在请求 begin 前检查页面仍挂载')
const wpfOnSuccess = extractSourceBlock(
  wpfLoader,
  'onSuccess: (result) => {',
  '\n      onError:',
  'WPF guarded onSuccess 块',
)
assert(wpfOnSuccess.includes('policyForm.setFieldsValue({'), 'WPF 策略表单只能由 guarded onSuccess 回填')
assertEqual(countOccurrences(wpfLoader, 'policyForm.setFieldsValue({'), countOccurrences(wpfOnSuccess, 'policyForm.setFieldsValue({'), 'WPF loader 不得在 guarded onSuccess 外回填策略表单')

assert(sources.wpf.includes('const mountedRef = useRef(false)'), 'WPF 页面应记录组件挂载状态')
assert(sources.wpf.includes('const latestReleaseQueryRef = useRef<WpfReleaseQuery>({'), 'WPF 页面应保存最新 UI query 快照')
const wpfSnapshotLayoutEffect = extractSourceBlock(
  sources.wpf,
  'useLayoutEffect(() => {\n    latestReleaseQueryRef.current = {',
  '\n\n  useLayoutEffect(() => {\n    mountedRef.current = true',
  'WPF 查询快照 layout effect',
)
for (const field of ['page', 'pageSize', 'channel', 'includeDisabled']) {
  assert(wpfSnapshotLayoutEffect.includes(field), `WPF 查询快照应包含 ${field}`)
}
assert(wpfSnapshotLayoutEffect.includes('latestLoadReleasesRef.current = loadReleases'), 'WPF 应在 commit 后发布当前 loader')
const wpfMountLifecycle = extractSourceBlock(
  sources.wpf,
  'useLayoutEffect(() => {\n    mountedRef.current = true',
  '\n\n  useEffect(() => {\n    const currentQuery = latestReleaseQueryRef.current',
  'WPF 挂载生命周期',
)
assert(wpfMountLifecycle.includes('}, [])'), 'WPF mounted 生命周期应是独立 layout effect')
assert(wpfMountLifecycle.indexOf('mountedRef.current = false') < wpfMountLifecycle.indexOf('releasesRequestGuardRef.current.invalidate()'), 'WPF 卸载时应先标记 unmounted 再 invalidate')

const wpfInitialEffect = extractSourceBlock(
  sources.wpf,
  'useEffect(() => {\n    const currentQuery = latestReleaseQueryRef.current',
  '\n\n  const resetReleaseScope',
  'WPF 初始加载 effect 块',
)
assertEqual(countOccurrences(wpfInitialEffect, 'loadReleases('), 1, 'WPF channel effect 每次只能发出一个列表请求')
assert(wpfInitialEffect.includes('}, [channel, includeDisabled, loadReleases])'), 'WPF channel effect 只能依赖 channel、includeDisabled 和稳定 loader')
assert(!wpfInitialEffect.includes('[page,') && !wpfInitialEffect.includes('pageSize, loadReleases'), 'WPF effect 不得依赖 page 或 pageSize')

const wpfRefreshLatestQuery = extractSourceBlock(
  sources.wpf,
  'const refreshLatestReleaseQuery = useCallback',
  '\n\n  const activeVersionOptions',
  'WPF mutation 最新查询刷新函数块',
)
assert(wpfRefreshLatestQuery.includes('if (!mountedRef.current)'), 'WPF mutation 刷新前应确认页面仍挂载')
assert(wpfRefreshLatestQuery.includes('const currentQuery = latestReleaseQueryRef.current'), 'WPF mutation 刷新应读取完成时 query 快照')
assert(wpfRefreshLatestQuery.includes('resetReleaseScope(targetChannel, currentQuery.includeDisabled)'), 'WPF 上传或策略目标 channel 不同时应保持切换语义')
assertEqual(countOccurrences(wpfRefreshLatestQuery, 'latestLoadReleasesRef.current('), 1, 'WPF mutation 当前 channel 只能刷新一次最新 query')

const wpfControls = extractSourceBlock(
  sources.wpf,
  '<Select\n              style={{ width: 140 }}\n              value={channel}',
  '<Button\n              type="primary"',
  'WPF channel 与刷新控件块',
)
assert(wpfControls.includes('onChange={handleChannelChange}'), 'WPF channel 控件应通过原子 scope 重置切换 channel')
assert(wpfControls.includes('<Switch checked={includeDisabled} onChange={handleIncludeDisabledChange} />'), 'WPF显示禁用控件应通过原子 scope 重置切换筛选状态')
assertEqual(countOccurrences(wpfControls, 'loadReleases('), 0, 'WPF 控件块不得直接加载，避免使用旧 scope')
assertEqual(countOccurrences(wpfControls, 'refreshLatestReleaseQuery('), 1, 'WPF 控件块只能由刷新按钮刷新一次最新 scope')

const wpfSubmitPolicy = extractSourceBlock(
  sources.wpf,
  'const submitPolicy = async',
  '\n\n  const handlePolicyFinish',
  'WPF 保存策略函数块',
)
assert(wpfSubmitPolicy.includes('await refreshLatestReleaseQuery(payload.channel, refreshQuery)'), 'WPF 保存策略应按完成时 query 或目标 channel 刷新')
assertEqual(countOccurrences(wpfSubmitPolicy, 'loadReleases('), 0, 'WPF 保存策略不得使用发起时旧 query 直接加载')

const wpfToggleRelease = extractSourceBlock(
  sources.wpf,
  'const handleToggleReleaseActive = async',
  '\n\n  const resetUploadFile',
  'WPF 启停版本函数块',
)
assert(wpfToggleRelease.includes('await refreshLatestReleaseQuery(undefined, refreshQuery)'), 'WPF 启停版本后应刷新完成时最新 query')
assertEqual(countOccurrences(wpfToggleRelease, 'loadReleases('), 0, 'WPF 启停版本不得使用发起时旧 query 直接加载')

const wpfUpload = extractSourceBlock(
  sources.wpf,
  'const handleUploadFinish = async',
  '\n\n  const columns:',
  'WPF 上传完成函数块',
)
assert(wpfUpload.includes('await refreshLatestReleaseQuery(uploadedChannel, refreshQuery)'), 'WPF 上传完成后应按完成时 query 比较目标 channel')
assertEqual(countOccurrences(wpfUpload, 'loadReleases('), 0, 'WPF 上传完成后不得使用发起时旧 query 直接加载')

for (const [name, source, loaderName, latestLoaderName, desiredRefName, refreshName] of [
  ['员工列表', sources.employee, 'loadData', 'latestLoadDataRef', 'desiredListQueryRef', 'refreshDesiredList'],
  ['敏感资料审核列表', sources.sensitive, 'loadList', 'latestLoadListRef', 'desiredListQueryRef', 'refreshDesiredList'],
  ['角色列表', sources.roles, 'loadData', 'latestLoadDataRef', 'desiredListQueryRef', 'refreshDesiredList'],
  ['任务日志列表', sources.scheduled, 'loadTaskLogs', 'latestLoadTaskLogsRef', 'desiredTaskLogQueryRef', 'refreshDesiredTaskLogs'],
] as const) {
  assert(source.includes('const mountedRef = useRef(false)'), `${name}应记录 mounted 状态`)
  assert(source.includes(`const ${latestLoaderName} = useRef(${loaderName})`), `${name}应保存 commit 后最新 loader`)
  assert(source.includes(`${latestLoaderName}.current = ${loaderName}`), `${name}应在 layout effect 发布最新 loader`)
  const loaderBlock = extractSourceBlock(
    source,
    `const ${loaderName} = async`,
    `\n\n  const ${latestLoaderName}`,
    `${name} loader`,
  )
  assert(loaderBlock.indexOf('if (!mountedRef.current)') < loaderBlock.indexOf('runLatestGuardedRequest('), `${name}应在 begin 前拦截卸载后的刷新`)
  assert(loaderBlock.includes(`${desiredRefName}.current = query`), `${name} loader 应同步发布 desired query`)
  assert(loaderBlock.indexOf(`${desiredRefName}.current = query`) < loaderBlock.indexOf('runLatestGuardedRequest('), `${name}应在 guard begin 前发布 desired query`)
  assert(source.includes(`${latestLoaderName}.current({ ...${desiredRefName}.current, ...overrides })`), `${name} mutation 刷新应读取 desired query`)
  assert(source.includes(`const ${refreshName} =`), `${name}应提供统一 desired-query 刷新入口`)
  assert(source.includes('mountedRef.current = false'), `${name}应在 layout cleanup 标记 unmounted`)
}

assert(sources.employee.includes('void refreshDesiredList()'), '员工保存后应刷新已开始的最新查询')
assert(sources.sensitive.includes('await Promise.all([refreshDesiredList(), refreshPendingCount()])'), '敏感资料审核后应刷新 desired query')
assert(sources.sensitive.includes('() => refreshDesiredList()'), '敏感资料审核冲突恢复应刷新 desired query')
assert(sources.roles.includes('await refreshDesiredList({ page: 1 })'), '角色创建后应保留回第一页的业务语义')
assert(sources.roles.includes('void refreshDesiredList()'), '角色编辑及关联更新后应刷新 desired query')
assert(sources.scheduled.includes('await refreshDesiredTaskLogs({ pageNumber: 1 })'), '任务触发后应保留回第一页的业务语义')
assert(sources.scheduled.includes('await refreshDesiredTaskLogs()'), '任务重试后应刷新 desired query')

console.log('System/requestRace.test.ts: ok')
