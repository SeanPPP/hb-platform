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

function readColumnBlock(source: string, marker: string) {
  const markerPosition = source.indexOf(marker)
  if (markerPosition < 0) return ''
  const blockStart = source.lastIndexOf('      {', markerPosition)
  const nextBlockStart = source.indexOf('      {', markerPosition + marker.length)
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length)
}

const pageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/ProductGradeManagement/index.tsx')
const serviceFile = path.resolve(process.cwd(), 'src/services/productGradeService.ts')
const typeFile = path.resolve(process.cwd(), 'src/types/productGrade.ts')
const packageFile = path.resolve(process.cwd(), 'package.json')

const pageSource = readFileSync(pageFile, 'utf8')
const serviceSource = readFileSync(serviceFile, 'utf8')
const typeSource = readFileSync(typeFile, 'utf8')
const packageSource = readFileSync(packageFile, 'utf8')

async function main() {
  const failures: string[] = []

  const requestSignalFailure = await runTest('商品等级列表请求应支持取消且 signal 不进入 query string', () => {
    assert(typeSource.includes('signal?: AbortSignal'), 'ProductGradeListParams 缺少 signal')
    assert(serviceSource.includes('signal: params.signal'), 'getProductGradeList 未把 signal 传给 request.get')
    const paramsBlock = serviceSource.slice(
      serviceSource.indexOf('params: {'),
      serviceSource.indexOf('signal: params.signal'),
    )
    assert(!paramsBlock.includes('signal'), 'signal 不能放进 params 查询字符串')
  })
  if (requestSignalFailure) failures.push(requestSignalFailure)

  const listAbortFailure = await runTest('页面列表请求应取消旧请求并忽略旧响应', () => {
    assert(pageSource.includes('const listAbortRef = useRef<AbortController | null>(null)'), '页面缺少列表 AbortController ref')
    assert(pageSource.includes('const listRequestSeqRef = useRef(0)'), '页面缺少请求序号 ref')
    assert(pageSource.includes('listAbortRef.current?.abort()'), '新请求前应取消旧列表请求')
    assert(pageSource.includes('requestSeq !== listRequestSeqRef.current'), '旧响应不能覆盖新数据')
    assert(pageSource.includes('controller.signal.aborted'), 'Abort 请求不应弹失败提示')
  })
  if (listAbortFailure) failures.push(listAbortFailure)

  const supplierLazyFailure = await runTest('供应商选项应首次展开时异步加载并可取消', () => {
    assert(!pageSource.includes('void loadSuppliers()\n    void loadList(1, pageSize)'), '首屏不应同步加载供应商选项')
    assert(pageSource.includes('const [suppliersLoaded, setSuppliersLoaded] = useState(false)'), '缺少供应商已加载状态')
    assert(pageSource.includes('supplierAbortRef.current?.abort()'), '供应商请求缺少取消逻辑')
    assert(pageSource.includes('onDropdownVisibleChange={(open) =>'), '供应商下拉缺少展开触发加载')
    assert(pageSource.includes('if (open) void loadSuppliers()'), '供应商下拉展开时应加载选项')
  })
  if (supplierLazyFailure) failures.push(supplierLazyFailure)

  const tableChangeFailure = await runTest('表格排序过滤应统一走 Table.onChange 服务端查询', () => {
    assert(pageSource.includes('const handleTableChange = ('), '页面缺少统一表格 onChange handler')
    assert(pageSource.includes("extra: { action: 'paginate' | 'sort' | 'filter' }"), 'handler 应识别分页/排序/过滤动作')
    assert(pageSource.includes("const nextPage = extra.action === 'paginate' ? pagination.current ?? 1 : 1"), '排序/过滤变化应回到第一页')
    assert(pageSource.includes('onChange={handleTableChange}'), 'Table 未绑定统一 onChange')
    assert(!pageSource.includes('pagination={{\n            current: page') || !pageSource.includes('onChange: (nextPage, nextPageSize)'), '分页不应保留独立 onChange')
  })
  if (tableChangeFailure) failures.push(tableChangeFailure)

  const sortableColumnsFailure = await runTest('商品等级列头应启用受控服务端排序', () => {
    const markers = [
      "dataIndex: 'supplierName'",
      "dataIndex: 'supplierCode'",
      "dataIndex: 'hbProductNo'",
      "dataIndex: 'grade'",
      "dataIndex: 'domesticPrice'",
      "dataIndex: 'importPrice'",
      "dataIndex: 'oemPrice'",
    ]

    markers.forEach((marker) => {
      const block = readColumnBlock(pageSource, marker)
      assert(block.includes('sorter: true'), `${marker} 缺少 sorter: true`)
      assert(block.includes('sortOrder:'), `${marker} 缺少受控 sortOrder`)
    })
  })
  if (sortableColumnsFailure) failures.push(sortableColumnsFailure)

  const filterColumnsFailure = await runTest('商品等级列头应启用服务端过滤入口', () => {
    assert(readColumnBlock(pageSource, "dataIndex: 'supplierCode'").includes('filterDropdown: renderSupplierFilterDropdown'), '供应商代码列缺少供应商过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'hbProductNo'").includes('filterDropdown: renderTextFilterDropdown'), '货号列缺少文本过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'grade'").includes('filters: Object.keys(PRODUCT_GRADE_CONFIG)'), '等级列缺少 A/B/C/D 过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'domesticPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '国内价列缺少区间过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'importPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '进口价列缺少区间过滤')
    assert(readColumnBlock(pageSource, "dataIndex: 'oemPrice'").includes('filterDropdown: renderPriceFilterDropdown'), '零售价列缺少区间过滤')
    assert(pageSource.includes('getFiltersFromTable(filters)'), '表格过滤参数未统一转换为接口参数')
  })
  if (filterColumnsFailure) failures.push(filterColumnsFailure)

  const priceRangeClearFailure = await runTest('价格区间过滤应允许只清空单侧边界', () => {
    assert(pageSource.includes('const selectedRangeValue = selectedKeys[0]'), '价格筛选应显式读取当前 selectedKey')
    assert(pageSource.includes("const hasSelectedRange = typeof selectedRangeValue === 'string'"), '价格筛选应区分无 selectedKey 和空边界 selectedKey')
    assert(pageSource.includes('const min = hasSelectedRange ? range.min : minValue'), '清空最小值后不能回填旧最小值')
    assert(pageSource.includes('const max = hasSelectedRange ? range.max : maxValue'), '清空最大值后不能回填旧最大值')
    assert(pageSource.includes("return `${min ?? ''}|${max ?? ''}`"), '价格区间编码应保留单侧空边界')
  })
  if (priceRangeClearFailure) failures.push(priceRangeClearFailure)

  const lazyImageFailure = await runTest('商品图片应懒加载并异步解码', () => {
    const imageColumn = readColumnBlock(pageSource, "dataIndex: 'productImage'")
    assert(imageColumn.includes('loading="lazy"'), '图片列缺少 loading="lazy"')
    assert(imageColumn.includes('decoding="async"'), '图片列缺少 decoding="async"')
    assert(imageColumn.includes('width={48}') && imageColumn.includes('height={48}'), '图片列应保持稳定尺寸')
    assert(imageColumn.includes('alt={record.productName || record.hbProductNo || record.productCode}'), '图片列缺少可读 alt')
  })
  if (lazyImageFailure) failures.push(lazyImageFailure)

  const serviceParamsFailure = await runTest('商品等级 service 应传递列头过滤和排序参数', () => {
    ;[
      'hbProductNo',
      'domesticPriceMin',
      'domesticPriceMax',
      'importPriceMin',
      'importPriceMax',
      'oemPriceMin',
      'oemPriceMax',
      'sortField',
      'sortDirection',
    ].forEach((field) => {
      assert(serviceSource.includes(`${field}: params.${field}`), `service 缺少 ${field} 参数透传`)
    })
  })
  if (serviceParamsFailure) failures.push(serviceParamsFailure)

  const exportRegressionFailure = await runTest('商品等级 Excel 导出链路应保留完整字段回查和选中顺序', () => {
    assert(pageSource.includes('getGradesByProductCodes(selectedProductCodes)'), '导出前应按商品编码回查完整字段')
    assert(pageSource.includes('const rowOrder = new Map(selectedProductCodes.map((code, index) => [code, index]))'), '导出应记录选中顺序')
    assert(pageSource.includes('.sort((a, b) => (rowOrder.get(a.productCode) ?? 0) - (rowOrder.get(b.productCode) ?? 0))'), '导出应按选中顺序排序')
    assert(pageSource.includes('includeProductImage: exportIncludeImage'), '导出应传递是否包含图片')
  })
  if (exportRegressionFailure) failures.push(exportRegressionFailure)

  const scriptFailure = await runTest('package 应提供商品等级性能专项测试脚本', () => {
    assert(packageSource.includes('"test:product-grade-performance"'), 'package.json 缺少 test:product-grade-performance')
  })
  if (scriptFailure) failures.push(scriptFailure)

  if (failures.length > 0) {
    throw new Error(`商品等级性能测试失败:\n${failures.join('\n')}`)
  }

  console.log('productGradePerformance.logic.test.ts: ok')
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})
