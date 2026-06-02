import { readFileSync } from 'node:fs'
import path from 'node:path'
import { syncWarehouseProductsFromHq } from '../../../services/warehouseProductService'
import type { CurrentUser } from '../../../types/auth'
import { buildAccess } from '../../../utils/access'

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'test-user-guid',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, label)
    return
  }

  throw new Error(`${label}。Expected promise to reject`)
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

function extractSection(source: string, startText: string, endText: string) {
  const startIndex = source.indexOf(startText)
  assert(startIndex >= 0, `未找到代码片段：${startText}`)

  const endIndex = source.indexOf(endText, startIndex)
  assert(endIndex >= 0, `未找到结束片段：${endText}`)

  return source.slice(startIndex, endIndex)
}

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const adminAccessFailure = await runTest('Admin 权限判断成立', () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ['Admin'],
      }),
    )

    assertEqual(access.isAdmin, true, 'Admin 应被识别为管理员')
  })
  if (adminAccessFailure) failures.push(adminAccessFailure)

  const nonAdminAccessFailure = await runTest('非 Admin 权限不会显示同步按钮', () => {
    const access = buildAccess(
      createCurrentUser({
        roleNames: ['WarehouseStaff'],
      }),
    )

    assertEqual(access.isAdmin, false, 'WarehouseStaff 不应被识别为管理员')
  })
  if (nonAdminAccessFailure) failures.push(nonAdminAccessFailure)

  const adminOnlyButtonFailure = await runTest('页面应仅对 Admin 渲染从 HQ 同步按钮', () => {
    assert(
      pageSource.includes('CloudSyncOutlined'),
      '页面应引入 CloudSyncOutlined 图标',
    )

    assert(
      pageSource.includes('access.isAdmin') &&
      pageSource.includes("t('warehouse.hqSync', '从HQ同步库存')"),
      '页面应基于 access.isAdmin 控制“从HQ同步库存”按钮可见性',
    )
  })
  if (adminOnlyButtonFailure) failures.push(adminOnlyButtonFailure)

  const modalConfirmFailure = await runTest('点击同步按钮前应弹出明确提示全量覆盖 WarehouseProduct 的确认框', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )

    assert(
      syncSection.includes('Modal.confirm({') &&
      syncSection.includes("t('warehouse.hqSyncTitle', '从HQ同步库存')") &&
      syncSection.includes('全量覆盖 WarehouseProduct'),
      '同步前应弹出明确提示“全量覆盖 WarehouseProduct”的确认框',
    )
  })
  if (modalConfirmFailure) failures.push(modalConfirmFailure)

  const loadingFailure = await runTest('同步按钮应在同步中保持 loading 和 disabled', () => {
    assert(
      pageSource.includes('loading={syncingFromHq}') &&
      pageSource.includes('disabled={syncingFromHq}'),
      '同步按钮应绑定 syncingFromHq 的 loading 与 disabled 状态',
    )
  })
  if (loadingFailure) failures.push(loadingFailure)

  const successRefreshFailure = await runTest('同步成功后提示成功并刷新第一页', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )

    assert(
      syncSection.includes('if (success) {') &&
      syncSection.includes('message.success(successMessage)') &&
      syncSection.includes('await loadData({ page: 1 })'),
      '同步成功分支应提示成功并刷新第一页',
    )
  })
  if (successRefreshFailure) failures.push(successRefreshFailure)

  const failureNoRefreshFailure = await runTest('同步失败时只提示失败且不刷新第一页', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )
    const catchSection = extractSection(syncSection, 'catch (error) {', 'finally {')

    assert(
      catchSection.includes("message.error(error instanceof Error ? error.message : t('warehouse.hqSyncFailed', '从HQ同步库存失败'))"),
      '同步失败时应优先展示 error.message',
    )

    assert(
      !catchSection.includes("loadData({ page: 1 })"),
      '同步失败分支不应刷新第一页',
    )
  })
  if (failureNoRefreshFailure) failures.push(failureNoRefreshFailure)

  const serviceUrlFailure = await runTest('同步服务应使用正确的 URL、POST 方法，并在后端返回失败时抛出 message', async () => {
    const originalFetch = globalThis.fetch
    let capturedUrl = ''
    let capturedInit: RequestInit | undefined

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = String(input)
        capturedInit = init

        return new Response(JSON.stringify({
          success: true,
          data: {
            isSuccess: true,
            message: '同步完成',
          },
        }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }) as typeof fetch

      await syncWarehouseProductsFromHq()

      assertEqual(capturedUrl, '/api/react/v1/product-warehouse/sync-from-hq', '同步服务应命中既定接口地址')
      assertEqual(capturedInit?.method, 'POST', '同步服务应使用 POST 方法')

      globalThis.fetch = (async () => new Response(JSON.stringify({
        success: false,
        message: '后端返回同步失败',
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch

      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        '后端返回同步失败',
        '后端 success=false 时应抛出后端 message',
      )

      globalThis.fetch = (async () => new Response(JSON.stringify({
        success: true,
        message: '外层成功但同步失败',
        data: {
          isSuccess: false,
          message: '内层同步事务失败',
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })) as typeof fetch

      await assertRejects(
        () => syncWarehouseProductsFromHq(),
        '内层同步事务失败',
        '外层 success=true 但 data.isSuccess=false 时应抛出内层 message',
      )
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (serviceUrlFailure) failures.push(serviceUrlFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('WarehouseProducts.hqSync.logic.test: ok')
}

await main()
