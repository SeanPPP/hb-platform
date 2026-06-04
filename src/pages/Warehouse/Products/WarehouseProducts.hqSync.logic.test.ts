import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  createWarehouseProductHqSyncJob,
  getWarehouseProductHqSyncJob,
  syncWarehouseProductsFromHq,
} from '../../../services/warehouseProductService'
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

  const modalConfirmFailure = await runTest('点击同步按钮前应弹出明确提示按商品编码新增更新的确认框', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )

    assert(
      syncSection.includes('Modal.confirm({') &&
      syncSection.includes("t('warehouse.hqSyncTitle', '从HQ同步库存')") &&
      syncSection.includes('按商品编码匹配') &&
      syncSection.includes('不会删除本地缺失商品'),
      '同步前应弹出明确提示“按商品编码匹配新增/更新且不删除本地缺失商品”的确认框',
    )
  })
  if (modalConfirmFailure) failures.push(modalConfirmFailure)

  const loadingFailure = await runTest('同步按钮应在后台任务提交中或运行中展示 loading，提交请求中 disabled', () => {
    assert(
      pageSource.includes('loading={syncingFromHq || Boolean(activeHqSyncJob)}') &&
      pageSource.includes('disabled={syncingFromHq}'),
      '同步按钮应绑定提交中和后台运行中状态，并允许运行中点击查看状态',
    )
  })
  if (loadingFailure) failures.push(loadingFailure)

  const jobApiFailure = await runTest('页面应提交后台 job 并轮询查询 job 状态', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )

    assert(
      pageSource.includes('createWarehouseProductHqSyncJob') &&
      pageSource.includes('getWarehouseProductHqSyncJob') &&
      pageSource.includes('createWarehouseProductHqSyncJobPoller'),
      '页面应使用后台 job 创建接口、查询接口和轮询器',
    )

    assert(
      syncSection.includes('createWarehouseProductHqSyncJob') &&
      !syncSection.includes('syncWarehouseProductsFromHq()'),
      '按钮确认后不应再直接等待旧同步接口完成',
    )
  })
  if (jobApiFailure) failures.push(jobApiFailure)

  const notificationFailure = await runTest('同步提交和完成结果应通过右上角 notification 返回', () => {
    const syncSection = extractSection(
      pageSource,
      'const handleSyncWarehouseProductsFromHq = () => {',
      'const columns = useMemo',
    )

    assert(
      pageSource.includes('notification') &&
      pageSource.includes('notification.info') &&
      pageSource.includes('notification.success') &&
      pageSource.includes('notification.error') &&
      pageSource.includes('notification.warning'),
      '页面应使用 notification 展示提交、成功、失败和超时信息',
    )

    assert(
      syncSection.includes("t('warehouse.hqSyncJobSubmitted") &&
      syncSection.includes('startHqSyncJobPolling'),
      '提交成功后应提示后台执行并启动轮询',
    )
  })
  if (notificationFailure) failures.push(notificationFailure)

  const successRefreshFailure = await runTest('后台同步成功后右上角提示结果并刷新第一页', () => {
    const descriptionSection = extractSection(
      pageSource,
      'const buildHqSyncResultDescription',
      'const showHqSyncJobResult',
    )
    const resultSection = extractSection(
      pageSource,
      'const showHqSyncJobResult',
      'const startHqSyncJobPolling',
    )

    assert(
      resultSection.includes('notification.success') &&
      descriptionSection.includes('addedCount') &&
      descriptionSection.includes('updatedCount') &&
      descriptionSection.includes('errorCount') &&
      resultSection.includes('void loadDataRef.current?.({ page: 1 })'),
      '后台同步成功应通过 notification 展示新增/更新/错误统计并刷新第一页',
    )
  })
  if (successRefreshFailure) failures.push(successRefreshFailure)

  const failureNoRefreshFailure = await runTest('后台同步失败时只提示失败且不刷新第一页', () => {
    const resultSection = extractSection(
      pageSource,
      'const showHqSyncJobResult',
      'const startHqSyncJobPolling',
    )

    assert(
      resultSection.includes('notification.error'),
      '后台同步失败时应使用 notification.error',
    )

    assert(
      !extractSection(resultSection, 'if (!success) {', 'const errorCount').includes("loadDataRef.current?.({ page: 1 })"),
      '后台同步失败分支不应刷新第一页',
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

  const jobServiceFailure = await runTest('后台 job 服务应使用创建和查询 URL', async () => {
    const originalFetch = globalThis.fetch
    const capturedUrls: string[] = []
    const capturedMethods: Array<string | undefined> = []

    try {
      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrls.push(String(input))
        capturedMethods.push(init?.method)

        return new Response(JSON.stringify({
          success: true,
          data: {
            jobId: 'warehouse-job-1',
            status: 'Running',
            createdAt: '2026-06-04T00:00:00Z',
          },
        }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }) as typeof fetch

      await createWarehouseProductHqSyncJob({ operationId: 'warehouse-products-hq-sync' })
      await getWarehouseProductHqSyncJob('warehouse-job-1')

      assertEqual(
        capturedUrls[0],
        '/api/react/v1/product-warehouse/sync-from-hq/jobs',
        '创建后台 job 应命中新接口地址',
      )
      assertEqual(capturedMethods[0], 'POST', '创建后台 job 应使用 POST 方法')
      assertEqual(
        capturedUrls[1],
        '/api/react/v1/product-warehouse/sync-from-hq/jobs/warehouse-job-1',
        '查询后台 job 应命中 job 查询接口地址',
      )
      assertEqual(capturedMethods[1], 'GET', '查询后台 job 应使用 GET 方法')
    } finally {
      globalThis.fetch = originalFetch
    }
  })
  if (jobServiceFailure) failures.push(jobServiceFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('WarehouseProducts.hqSync.logic.test: ok')
}

await main()
