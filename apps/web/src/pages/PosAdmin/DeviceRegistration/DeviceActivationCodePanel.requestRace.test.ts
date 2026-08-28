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
  const promise = new Promise<T>((resolvePromiseValue) => {
    resolvePromise = resolvePromiseValue
  })
  return { promise, resolve: resolvePromise }
}

const guard = createLatestRequestGuard()
let items = ''
let total = 0
let loading = false

function load(request: Promise<{ items: string; total: number }>) {
  return runLatestGuardedRequest(guard, () => request, {
    onStart: () => { loading = true },
    onSuccess: (result) => {
      items = result.items
      total = result.total
    },
    onSettled: () => { loading = false },
  })
}

const firstPage = createDeferred<{ items: string; total: number }>()
const firstRun = load(firstPage.promise)
const secondPage = createDeferred<{ items: string; total: number }>()
const secondRun = load(secondPage.promise)

firstPage.resolve({ items: 'stale page', total: 31 })
await firstRun
assertEqual(items, '', '迟到旧页不得覆盖最新请求')
assertEqual(total, 0, '迟到旧页不得覆盖最新总数')
assertEqual(loading, true, '旧请求 finally 不得关闭最新请求的 loading')

secondPage.resolve({ items: 'current page', total: 62 })
await secondRun
assertEqual(items, 'current page', '最新页必须提交到表格')
assertEqual(total, 62, '最新页必须提交总数')
assertEqual(loading, false, '最新请求完成后必须关闭 loading')

const source = readFileSync(
  resolve('src/pages/PosAdmin/DeviceRegistration/DeviceActivationCodePanel.tsx'),
  'utf8',
)
assertIncludes(source, 'createLatestRequestGuard()', '设备开通码列表应创建最新请求守卫')
assertIncludes(source, 'runLatestGuardedRequest(listRequestGuardRef.current', '列表加载应统一经过最新请求守卫')
assertIncludes(source, 'listRequestGuardRef.current.invalidate()', '页面卸载时应淘汰未完成请求')

console.log('DeviceActivationCodePanel.requestRace.test.ts: ok')
