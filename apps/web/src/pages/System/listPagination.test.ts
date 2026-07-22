import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  DEFAULT_SYSTEM_LIST_PAGE_SIZE,
  createLatestRequestGuard,
  resolveSystemListPagination,
  runLatestGuardedRequest,
} from './listPagination'

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

assertEqual(DEFAULT_SYSTEM_LIST_PAGE_SIZE, 50, '系统主列表默认每页应显示 50 条')

const paginate = resolveSystemListPagination('paginate', { current: 3, pageSize: 100 }, 50)
assertEqual(paginate.page, 3, '翻页应使用组件提供的目标页码')
assertEqual(paginate.pageSize, 100, '翻页应使用组件提供的目标页长')

const sort = resolveSystemListPagination('sort', { current: 3, pageSize: 100 }, 50)
assertEqual(sort.page, 1, '排序后必须回到第一页')
assertEqual(sort.pageSize, 100, '排序后应保留用户选择的页长')

const filter = resolveSystemListPagination('filter', { current: 3 }, 50)
assertEqual(filter.page, 1, '筛选后必须回到第一页')
assertEqual(filter.pageSize, 50, '筛选未提供页长时应保持当前页长')

function createDeferred<T>() {
  let resolvePromise!: (value: T) => void
  let rejectPromise!: (error: unknown) => void
  const promise = new Promise<T>((resolve, reject) => {
    resolvePromise = resolve
    rejectPromise = reject
  })

  return { promise, resolve: resolvePromise, reject: rejectPromise }
}

const requestGuard = createLatestRequestGuard()
const state = { data: '', page: 0, error: '', loading: false }

function createHandlers(page: number) {
  return {
    onStart: () => { state.loading = true },
    onSuccess: (data: string) => {
      state.data = data
      state.page = page
    },
    onError: (error: unknown) => { state.error = String(error) },
    onSettled: () => { state.loading = false },
  }
}

const firstPage = createDeferred<string>()
const firstRun = runLatestGuardedRequest(requestGuard, () => firstPage.promise, createHandlers(1))

const secondPage = createDeferred<string>()
const secondRun = runLatestGuardedRequest(requestGuard, () => secondPage.promise, createHandlers(2))

// 旧第一页在新页发起后结束：它既不能回写数据/页码，也不能结束新请求的 loading。
firstPage.resolve('stale page 1')
await firstRun
assertEqual(state.data, '', '旧第一页响应不能覆盖数据')
assertEqual(state.page, 0, '旧第一页响应不能覆盖页码')
assertEqual(state.error, '', '旧第一页响应不能展示错误')
assertEqual(state.loading, true, '旧第一页 finally 不能关闭新页请求的 loading')

secondPage.resolve('latest page 2')
await secondRun
assertEqual(state.data, 'latest page 2', '最新页响应应更新数据')
assertEqual(state.page, 2, '最新页响应应更新页码')
assertEqual(state.loading, false, '最新页响应应结束 loading')

const lateFirstPage = createDeferred<string>()
const lateFirstRun = runLatestGuardedRequest(requestGuard, () => lateFirstPage.promise, createHandlers(1))
const completedSecondPage = createDeferred<string>()
const completedSecondRun = runLatestGuardedRequest(requestGuard, () => completedSecondPage.promise, createHandlers(2))

// 新第 2 页先完成后，较晚返回的旧第 1 页仍不得覆盖已显示的数据。
completedSecondPage.resolve('completed page 2')
await completedSecondRun
lateFirstPage.resolve('late page 1')
await lateFirstRun
assertEqual(state.data, 'completed page 2', '较晚返回的旧第一页不能覆盖新页数据')
assertEqual(state.page, 2, '较晚返回的旧第一页不能覆盖新页页码')
assertEqual(state.loading, false, '较晚返回的旧第一页不能改变已完成的 loading 状态')

const staleFailure = createDeferred<string>()
const staleFailureRun = runLatestGuardedRequest(requestGuard, () => staleFailure.promise, createHandlers(1))
const retry = createDeferred<string>()
const retryRun = runLatestGuardedRequest(requestGuard, () => retry.promise, createHandlers(2))

staleFailure.reject(new Error('stale failure'))
await staleFailureRun
assertEqual(state.error, '', '旧请求错误不能覆盖最新请求的错误状态')
assertEqual(state.loading, true, '旧请求错误 finally 不能关闭重试 loading')
retry.resolve('retry page 2')
await retryRun

const invalidatedRequest = createDeferred<string>()
const invalidatedRun = runLatestGuardedRequest(requestGuard, () => invalidatedRequest.promise, createHandlers(3))
requestGuard.invalidate()
invalidatedRequest.resolve('ignored after unmount')
await invalidatedRun
assertEqual(state.data, 'retry page 2', '卸载后在途请求不能更新数据')
assertEqual(state.page, 2, '卸载后在途请求不能更新页码')
assertEqual(state.loading, true, '卸载后在途请求不能关闭页面外的 loading 状态')

const usersSource = readFileSync(resolve('src/pages/System/Users/index.tsx'), 'utf8')
const storesSource = readFileSync(resolve('src/pages/System/Stores/index.tsx'), 'utf8')

for (const [name, source] of [['用户', usersSource], ['分店', storesSource]] as const) {
  assertIncludes(source, 'useState(DEFAULT_SYSTEM_LIST_PAGE_SIZE)', `${name}主列表应默认每页 50 条`)
  assertIncludes(source, 'createLatestRequestGuard()', `${name}主列表应创建最新请求保护`)
  assertIncludes(source, 'mainListRequestGuardRef.current.invalidate()', `${name}卸载时应让在途请求失效`)
  assertIncludes(source, 'runLatestGuardedRequest(mainListRequestGuardRef.current', `${name}主列表应通过生产请求执行器更新状态`)
}

assertIncludes(usersSource, 'const [loginRecordsPageSize, setLoginRecordsPageSize] = useState(10)', '登录记录分页仍应保持每页 10 条')
const usersTableStart = usersSource.indexOf('<Table\n          rowKey="userGUID"')
const usersTableEnd = usersSource.indexOf('\n        />', usersTableStart)
const usersTableSource = usersSource.slice(usersTableStart, usersTableEnd)
assertEqual((usersTableSource.match(/onChange=/g) ?? []).length, 1, '用户主表只能通过单一 onChange 发起请求')
assertEqual(usersTableSource.includes('pagination={{\n            current: page,'), true, '用户主表应保留受控分页')

console.log('listPagination.test.ts: ok')
