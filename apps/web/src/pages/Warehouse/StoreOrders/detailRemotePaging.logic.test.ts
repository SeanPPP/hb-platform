import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
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

const detailFile = path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/Detail.tsx')
const detailSource = readFileSync(detailFile, 'utf8')

async function main() {
  const failures: string[] = []

  const loadStateFailure = await runTest('详情页应显式区分 idle/loading/loaded/notFound/error 状态', () => {
    assert(
      detailSource.includes("type DetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error'"),
      '详情页尚未声明远程加载状态机',
    )
  })
  if (loadStateFailure) failures.push(loadStateFailure)

  const remoteQueryFailure = await runTest('详情页应将分页筛选排序作为远程 query 发送', () => {
	    assert(
	      detailSource.includes('pageNumber: detailPage') &&
	        detailSource.includes('pageSize: detailPageSize') &&
	        detailSource.includes('keyword: detailItemFilter.trim() || undefined') &&
	        detailSource.includes("statFilter: detailStatFilter === 'all' ? undefined : detailStatFilter") &&
	        detailSource.includes('sortBy: detailSortField || undefined') &&
	        detailSource.includes("sortDescending: detailSortField ? detailSortOrder === 'descend' : undefined"),
	      '详情页尚未把分页筛选排序拼到远程明细查询里',
    )
  })
  if (remoteQueryFailure) failures.push(remoteQueryFailure)

  const defaultLocationSortFailure = await runTest('详情页默认排序应按货位升序并提供默认排序按钮', () => {
    assert(
      detailSource.includes("useState<DetailSortField>('locationCode')") &&
        detailSource.includes("useState<SortOrder>('ascend')") &&
        detailSource.includes('const handleResetDetailDefaultSort = () =>') &&
        detailSource.includes("setDetailSortField('locationCode')") &&
        detailSource.includes("setDetailSortOrder('ascend')") &&
        detailSource.includes("t('storeOrders.detail.defaultSort')") &&
        detailSource.includes('icon={<SortAscendingOutlined />}'),
      '详情页尚未默认按货位升序，或缺少恢复默认排序按钮',
    )
  })
  if (defaultLocationSortFailure) failures.push(defaultLocationSortFailure)

  const currentPageDataFailure = await runTest('详情表格应直接使用服务端当前页 items 与 itemsTotal', () => {
    assert(
      detailSource.includes('dataSource={detail.items}') &&
        detailSource.includes('total: detail.itemsTotal ?? detail.items.length') &&
        !detailSource.includes('dataSource={pagedItems}'),
      '详情表格仍在使用本地切片分页，而不是服务端当前页数据',
    )
  })
  if (currentPageDataFailure) failures.push(currentPageDataFailure)

	  const clearSelectionFailure = await runTest('翻页筛选排序时应清空勾选行', () => {
	    assert(
	      detailSource.includes('setSelectedLineKeys([])') &&
	        detailSource.includes('setDetailPage(nextPage)') &&
	        detailSource.includes("extra.action === 'paginate'") &&
	        detailSource.includes('setDetailItemFilter(event.target.value)') &&
	        detailSource.includes('setDetailSortField(field)'),
	      '翻页筛选排序时尚未统一清空 selectedLineKeys',
    )
  })
  if (clearSelectionFailure) failures.push(clearSelectionFailure)

  const cancelFailure = await runTest('详情页应取消上一笔进行中的明细请求', () => {
    assert(
      detailSource.includes('detailRequestControllerRef.current?.abort()') &&
        detailSource.includes('new AbortController()') &&
        detailSource.includes('detailRequestControllerRef.current.signal'),
      '详情页尚未接入明细请求取消逻辑',
    )
  })
	  if (cancelFailure) failures.push(cancelFailure)

  const containerCodesFailure = await runTest('货柜选品应使用跨页商品编码去重', () => {
    assert(
      detailSource.includes('getStoreOrderDetailProductCodes') &&
        detailSource.includes('alreadySelectedCodes={containerExistingProductCodes}') &&
        detailSource.includes('handleOpenContainerPicker') &&
        detailSource.includes('setContainerPickerOpen(false)') &&
        !detailSource.includes('alreadySelectedCodes={detail.items.map((item) => item.productCode)}') &&
        !detailSource.includes('detail.items.map((item) => item.productCode)'),
      '货柜选品仍在使用当前页 items 做已选商品去重',
    )
  })
	  if (containerCodesFailure) failures.push(containerCodesFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('detailRemotePaging.logic.test: ok')
}

await main()
