import { readFileSync } from 'node:fs'
import path from 'node:path'

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

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Containers/index.tsx')
const pageSource = readFileSync(pageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const successRefreshFailure = await runTest('同步成功后才提示成功并刷新第一页', () => {
    assert(
      pageSource.includes('if (success) {') &&
      pageSource.includes('message.success(msg)') &&
      pageSource.includes('await loadData(1, pageSize)'),
      '页面应显式区分成功分支，并在成功后提示成功且刷新第一页',
    )

    assert(
      pageSource.includes("const success = result.isSuccess ?? result.IsSuccess ?? true"),
      '页面应基于同步结果中的 success 字段判断是否成功',
    )
  })
  if (successRefreshFailure) failures.push(successRefreshFailure)

  const errorHandlingFailure = await runTest('同步失败时只展示 error.message 且不刷新', () => {
    assert(
      pageSource.includes("const errorMessage = error instanceof Error ? error.message : t('containers.messages.syncFailed')") &&
      pageSource.includes('message.error(errorMessage)'),
      '页面失败分支应优先展示 error.message，并为非 Error 异常保留兜底文案',
    )

    const loadDataCount = pageSource.split('await loadData(1, pageSize)').length - 1
    assertEqual(loadDataCount, 2, '页面源码中刷新第一页的调用次数应保持为创建成功一次、同步成功一次')
  })
  if (errorHandlingFailure) failures.push(errorHandlingFailure)

  const loadingGuardFailure = await runTest('同步按钮应保留 loading 与 disabled 行为', () => {
    assert(
      pageSource.includes('loading={syncing}') &&
      pageSource.includes('disabled={pushing}'),
      '同步按钮应继续保留 loading 和 disabled 控制',
    )
  })
  if (loadingGuardFailure) failures.push(loadingGuardFailure)

  const inlineStatusFailure = await runTest('状态列应支持行内四态下拉更新', () => {
    assert(
      pageSource.includes('handleContainerStatusChange') &&
      pageSource.includes('updateContainer(record.hguid') &&
      pageSource.includes('{ 状态: nextStatus }'),
      '页面应通过行内 handler 调用 updateContainer 更新当前货柜状态',
    )

    assert(
      pageSource.includes('containerStatusOptions') &&
      pageSource.includes('statusUpdatingKeys') &&
      pageSource.includes('onChange={(nextStatus) => void handleContainerStatusChange(record, nextStatus)}'),
      '状态列应使用四态下拉，并在行级更新中禁用当前状态控件',
    )

    assert(
      pageSource.includes('CONTAINER_STATUS_SELECT_WIDTH') &&
      pageSource.includes('style={{ width: CONTAINER_STATUS_SELECT_WIDTH }}') &&
      pageSource.includes('popupMatchSelectWidth={CONTAINER_STATUS_SELECT_WIDTH}'),
      '状态列下拉选择框和弹出层应使用同一宽度，避免控件大小不匹配',
    )

    assert(
      pageSource.includes('if (record.状态 === nextStatus || statusUpdatingKeys.includes(recordKey))') &&
      pageSource.includes('setContainers((items) => items.map((item) => (itemKeyOf(item) === recordKey ? { ...item, 状态: previousStatus } : item)))'),
      '状态更新应跳过相同状态和忙碌行，并在失败时回滚原状态',
    )
  })
  if (inlineStatusFailure) failures.push(inlineStatusFailure)

  const weekDateColorFailure = await runTest('三列日期应按同年同 ISO 周使用一致颜色', () => {
    assert(
      pageSource.includes("import isoWeek from 'dayjs/plugin/isoWeek'") &&
        pageSource.includes('dayjs.extend(isoWeek)'),
      '页面应启用 dayjs isoWeek 插件，按 ISO 周计算同年同周',
    )

    assert(
      pageSource.includes('containerDateWeekColors') &&
        pageSource.includes('getContainerDateWeekKey') &&
        pageSource.includes('renderContainerWeekDate'),
      '页面应提供日期周 key、稳定调色板和周日期渲染 helper',
    )

    assert(
      pageSource.includes("return `${date.isoWeekYear()}-W${String(date.isoWeek()).padStart(2, '0')}`"),
      '日期周 key 应同时包含 ISO week-year 和两位 week，避免跨年同周混色',
    )

    const weekDateRenderCount = pageSource.split('render: renderContainerWeekDate').length - 1
    assertEqual(weekDateRenderCount, 3, '装柜日期、预计到岸日期、实际到货日期三列都应使用同周配色渲染')

    assert(
      pageSource.includes("if (!value) return '--'") &&
        pageSource.includes('if (!weekKey) return formatDate(value)'),
      '空日期应继续显示 --，无效日期应保留普通日期格式兜底',
    )
  })
  if (weekDateColorFailure) failures.push(weekDateColorFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('Containers.hqSync.logic.test: ok')
}

await main()
