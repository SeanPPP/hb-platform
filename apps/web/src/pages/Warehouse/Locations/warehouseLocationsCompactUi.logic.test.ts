import { existsSync, readFileSync } from 'node:fs'
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

function readCssRule(source: string, selector: string) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`))
  return match?.[1] ?? ''
}

function readColumnBlock(source: string, marker: string) {
  const markerPosition = source.indexOf(marker)
  if (markerPosition < 0) {
    return ''
  }

  const blockStart = source.lastIndexOf('    {', markerPosition)
  const nextBlockStart = source.indexOf('    {', markerPosition + marker.length)
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length)
}

function readNumericValue(source: string, pattern: RegExp) {
  const match = source.match(pattern)
  return match ? Number(match[1]) : Number.NaN
}

const locationsPageFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Locations/index.tsx')
const compactCssFile = path.resolve(process.cwd(), 'src/pages/Warehouse/Locations/compact.css')
const locationTypesFile = path.resolve(process.cwd(), 'src/types/location.ts')
const locationServiceFile = path.resolve(process.cwd(), 'src/services/locationService.ts')
const zhLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const enLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')
const packageFile = path.resolve(process.cwd(), 'package.json')

const locationsPageSource = readFileSync(locationsPageFile, 'utf8')
const locationTypesSource = readFileSync(locationTypesFile, 'utf8')
const locationServiceSource = readFileSync(locationServiceFile, 'utf8')
const zhLocaleSource = readFileSync(zhLocaleFile, 'utf8')
const enLocaleSource = readFileSync(enLocaleFile, 'utf8')
const packageSource = readFileSync(packageFile, 'utf8')
const compactCssSource = existsSync(compactCssFile) ? readFileSync(compactCssFile, 'utf8') : ''

async function main() {
  const failures: string[] = []

  const typeFailure = await runTest('仓库标签商品类型应包含商品条码字段', () => {
    assert(locationTypesSource.includes('productBarcode?: string'), 'LocationProduct 缺少 productBarcode 字段')
  })
  if (typeFailure) failures.push(typeFailure)

  const normalizeFailure = await runTest('仓库标签列表应兼容多种商品条码返回字段', () => {
    assert(locationServiceSource.includes('normalizeLocationProduct'), 'locationService 缺少商品 normalize helper')
    assert(locationServiceSource.includes('normalizeLocationItem'), 'locationService 缺少标签 normalize helper')
    assert(locationServiceSource.includes('productBarcode ??'), 'normalize 应优先读取 productBarcode')
    assert(locationServiceSource.includes('ProductBarcode'), 'normalize 应兼容 ProductBarcode')
    assert(locationServiceSource.includes('raw.barcode'), 'normalize 应兼容 barcode')
    assert(locationServiceSource.includes('raw.Barcode'), 'normalize 应兼容 Barcode')
    assert(locationServiceSource.includes('raw.Products'), 'normalize 应兼容 Products 商品数组')
    assert(locationServiceSource.includes('items: (data?.items ?? []).map(normalizeLocationItem)'), '列表返回应统一 normalize')
  })
  if (normalizeFailure) failures.push(normalizeFailure)

  const hqSyncServiceFailure = await runTest('仓库标签服务应顺序增量同步货位和商品货位', () => {
    const serviceFunctionStart = locationServiceSource.indexOf('export async function syncLocationsFromHq')
    assert(serviceFunctionStart >= 0, 'locationService 缺少 syncLocationsFromHq')

    const serviceFunction = locationServiceSource.slice(serviceFunctionStart)
    const locationsEndpointPosition = serviceFunction.indexOf('/api/react/v1/sync/locations-incremental')
    const productLocationsEndpointPosition = serviceFunction.indexOf('/api/react/v1/sync/product-locations-incremental')

    assert(locationsEndpointPosition >= 0, '货位同步应调用 locations-incremental')
    assert(productLocationsEndpointPosition >= 0, '商品货位同步应调用 product-locations-incremental')
    assert(
      locationsEndpointPosition < productLocationsEndpointPosition,
      '商品货位同步必须在货位同步之后执行',
    )
    assert(serviceFunction.includes('locationResult') && serviceFunction.includes('productLocationResult'), '应返回两段同步结果')
  })
  if (hqSyncServiceFailure) failures.push(hqSyncServiceFailure)

  const pageWiringFailure = await runTest('仓库标签页应挂载局部紧凑表格样式和商品条码列', () => {
    assert(locationsPageSource.includes("import './compact.css'"), '页面应引入局部 compact.css')
    assert(locationsPageSource.includes('CloudSyncOutlined'), '页面应引入 HQ 同步图标')
    assert(locationsPageSource.includes('className="warehouse-locations-compact-table"'), 'Table 缺少局部紧凑 class')
    assert(locationsPageSource.includes('size="small"'), 'Table 应使用 small 尺寸')
    assert(locationsPageSource.includes("title: t('column.productBarcode')"), '表格缺少商品条码列')
    assert(locationsPageSource.includes('renderCopyableProductBarcodes'), '商品条码列应走专属可复制渲染 helper')
    assert(locationsPageSource.includes('renderLocationProductName'), '商品名称应走两行展示 helper')
  })
  if (pageWiringFailure) failures.push(pageWiringFailure)

  const hqSyncPageFailure = await runTest('仓库标签页应提供一键从HQ更新货位入口', () => {
    assert(locationsPageSource.includes('syncLocationsFromHq'), '页面应调用 syncLocationsFromHq')
    assert(locationsPageSource.includes('const [syncingFromHq, setSyncingFromHq] = useState(false)'), '页面应维护 HQ 同步 loading 状态')
    assert(locationsPageSource.includes('const canSyncLocationsFromHq = access.isAdmin || access.isWarehouseManager'), '页面应按后端 Admin/WarehouseManager 角色显示同步按钮')
    assert(locationsPageSource.includes('const handleSyncFromHq = () => {'), '页面缺少 HQ 同步处理函数')
    assert(locationsPageSource.includes('Modal.confirm'), 'HQ 同步应二次确认')
    assert(locationsPageSource.includes('setSyncingFromHq(true)'), '确认同步后应进入 loading 状态')
    assert(locationsPageSource.includes('await syncLocationsFromHq()'), '页面应等待一键同步完成')
    assert(locationsPageSource.includes('await loadData(1, pageSize)'), '同步成功后应刷新第一页')
    assert(locationsPageSource.includes('loading={syncingFromHq}'), '同步按钮应绑定 loading 状态')
    assert(locationsPageSource.includes('disabled={syncingFromHq || loading}'), '同步按钮应在同步或列表加载中禁用')
    assert(locationsPageSource.includes("t('warehouseLocations.syncFromHq'"), '同步按钮应使用仓库标签文案')
    assert(locationsPageSource.includes("t('warehouseLocations.syncFromHqSuccessTitle'"), '同步成功应展示专属结果标题')
  })
  if (hqSyncPageFailure) failures.push(hqSyncPageFailure)

  const barcodeColumnFailure = await runTest('商品条码列应显示文本和复制图标且不回退货号', () => {
    const barcodeColumn = readColumnBlock(locationsPageSource, "title: t('column.productBarcode')")

    assert(barcodeColumn.includes('renderCopyableProductBarcodes(record.products, t)'), '商品条码列未绑定商品条码渲染')
    assert(locationsPageSource.includes('product.productBarcode'), '商品条码 helper 应读取 productBarcode')
    assert(locationsPageSource.includes('<BarcodePreview'), '商品条码列应显示条码图')
    assert(locationsPageSource.includes('className="warehouse-locations-product-barcode-preview"'), '商品条码列应使用专属条码图样式')
    assert(!locationsPageSource.includes('product.productImage'), '商品条码列不能显示现实商品图')
    assert(!locationsPageSource.includes("field: 'itemNumber' | 'productName' | 'productBarcode'"), '商品条码不应复用会混淆货号的通用字段参数')
    assert(locationsPageSource.includes('className="warehouse-locations-barcode-cell warehouse-locations-nowrap"'), '商品条码文本应使用 nowrap class')
    assert(locationsPageSource.includes('className="warehouse-locations-copyable-cell warehouse-locations-nowrap"'), '货号复制内容应使用 nowrap class')
    assert(locationsPageSource.includes('className="warehouse-locations-copyable-content"'), '货号内容区应限制在单元格内')
    assert(locationsPageSource.includes('className="warehouse-locations-barcode-content"'), '商品条码内容区应限制在单元格内')
    assert(locationsPageSource.includes('className="warehouse-locations-copy-button"'), '商品条码复制按钮应使用紧凑图标按钮')
    assert(!barcodeColumn.includes('itemNumber'), '商品条码列缺失时不能回退显示货号')
  })
  if (barcodeColumnFailure) failures.push(barcodeColumnFailure)

  const locationBarcodeFailure = await runTest('货位代码和货位条码应保持文本完整显示', () => {
    const locationCodeColumn = readColumnBlock(locationsPageSource, "dataIndex: 'locationCode'")
    const locationBarcodeColumn = readColumnBlock(locationsPageSource, "dataIndex: 'locationBarcode'")
    const locationCodePosition = locationsPageSource.indexOf("dataIndex: 'locationCode'")
    const locationTypePosition = locationsPageSource.indexOf("dataIndex: 'locationType'")
    const locationBarcodePosition = locationsPageSource.indexOf("dataIndex: 'locationBarcode'")

    assert(locationCodeColumn.includes('textNoWrap'), '货位代码条码文本应保持单行')
    assert(locationBarcodeColumn.includes('textNoWrap'), '货位条码文本应保持单行')
    assert(!locationCodeColumn.includes('textMaxWidth'), '货位代码不应通过 textMaxWidth 省略隐藏')
    assert(!locationBarcodeColumn.includes('textMaxWidth'), '货位条码不应通过 textMaxWidth 省略隐藏')
    assert(locationCodePosition < locationTypePosition, '货位类型列应放在货位代码之后')
    assert(locationTypePosition < locationBarcodePosition, '货位类型列应放在货位条码之前')
  })
  if (locationBarcodeFailure) failures.push(locationBarcodeFailure)

  const cssFailure = await runTest('仓库标签紧凑 CSS 应局部限制并保留关键字段', () => {
    const headerRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .ant-table-column-title')
    const nowrapRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-nowrap')
    const twoLineRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-two-line-text')
    const barcodeRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-barcode-cell')
    const copyableContentRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-copyable-content')
    const barcodeContentRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-barcode-content')
    const copyButtonRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-copy-button')
    const barcodePreviewRule = readCssRule(compactCssSource, '.warehouse-locations-compact-table .warehouse-locations-product-barcode-preview canvas')

    assert(/-webkit-line-clamp:\s*2/.test(headerRule), '列头应最多显示两行')
    assert(/white-space:\s*nowrap/.test(nowrapRule), '关键字段应保持单行')
    assert(!/overflow:\s*hidden/.test(nowrapRule), '关键字段不应被隐藏截断')
    assert(/-webkit-line-clamp:\s*2/.test(twoLineRule), '商品名称应最多显示两行')
    assert(/overflow:\s*hidden/.test(twoLineRule), '商品名称超过两行才可隐藏')
    assert(/white-space:\s*nowrap/.test(barcodeRule), '商品条码容器应保持单行')
    assert(/overflow:\s*hidden/.test(copyableContentRule), '货号内容区应在单元格内裁切，避免复制按钮溢出')
    assert(/overflow:\s*hidden/.test(barcodeContentRule), '商品条码内容区应在单元格内裁切，避免复制按钮溢出')
    assert(/min-width:\s*0/.test(copyableContentRule), '货号内容区应允许 flex 收缩')
    assert(/min-width:\s*0/.test(barcodeContentRule), '商品条码内容区应允许 flex 收缩')
    assert(/width:\s*20px/.test(copyButtonRule), '复制图标按钮应收窄')
    assert(/max-width:\s*74px/.test(barcodePreviewRule), '商品条码图应控制最大宽度')
    assert(/height:\s*18px/.test(barcodePreviewRule), '商品条码图高度应控制到 18px')
    assert(!/^\.warehouse-locations-nowrap/m.test(compactCssSource), 'nowrap 样式必须限定到仓库标签表格下')
    assert(!/^\.warehouse-locations-two-line-text/m.test(compactCssSource), '两行样式必须限定到仓库标签表格下')
  })
  if (cssFailure) failures.push(cssFailure)

  const layoutFailure = await runTest('仓库标签表格列宽应按紧凑预算设置', () => {
    const indexColumn = readColumnBlock(locationsPageSource, "key: 'index'")
    const itemNumberColumn = readColumnBlock(locationsPageSource, "key: 'itemNumbers'")
    const productBarcodeColumn = readColumnBlock(locationsPageSource, "title: t('column.productBarcode')")
    const productNameColumn = readColumnBlock(locationsPageSource, "key: 'productNames'")
    const imageColumn = readColumnBlock(locationsPageSource, "key: 'productImages'")
    const actionColumn = readColumnBlock(locationsPageSource, "key: 'action'")
    const scrollX = readNumericValue(locationsPageSource, /scroll=\{\{\s*x:\s*(\d+)/)

    assert(readNumericValue(indexColumn, /width:\s*(\d+)/) <= 56, '序号列应压到 56 以内')
    assert(readNumericValue(itemNumberColumn, /width:\s*(\d+)/) <= 118, '货号列应压到 118 以内')
    assert(readNumericValue(productBarcodeColumn, /width:\s*(\d+)/) <= 150, '商品条码列应压到 150 以内')
    assert(readNumericValue(productNameColumn, /width:\s*(\d+)/) >= 190, '商品名称列应保留至少 190 宽度')
    assert(readNumericValue(imageColumn, /width:\s*(\d+)/) <= 112, '图片列应压到 112 以内')
    assert(readNumericValue(actionColumn, /width:\s*(\d+)/) <= 132, '操作列应压到 132 以内')
    assert(scrollX >= 1460 && scrollX <= 1500, 'scroll.x 应设置到 1460-1500')
  })
  if (layoutFailure) failures.push(layoutFailure)

  const localeAndScriptFailure = await runTest('商品条码文案和测试脚本应接入项目', () => {
    assert(zhLocaleSource.includes('"productBarcode": "商品条码"'), '中文列名缺少商品条码')
    assert(enLocaleSource.includes('"productBarcode": "Product Barcode"'), '英文列名缺少 Product Barcode')
    assert(zhLocaleSource.includes('"syncFromHq": "从HQ更新货位"'), '中文文案缺少从HQ更新货位')
    assert(enLocaleSource.includes('"syncFromHq": "Update Locations from HQ"'), '英文文案缺少 Update Locations from HQ')
    assert(zhLocaleSource.includes('"syncResultStats": "新增 {{added}}，更新 {{updated}}，错误 {{errors}}"'), '中文文案缺少同步结果统计')
    assert(enLocaleSource.includes('"syncResultStats": "Added {{added}}, updated {{updated}}, errors {{errors}}"'), '英文文案缺少同步结果统计')
    assert(packageSource.includes('"test:warehouse-locations"'), 'package.json 缺少 test:warehouse-locations 脚本')
    assert(packageSource.includes('warehouseLocationsCompactUi.logic.test.ts'), '测试脚本未运行仓库标签紧凑 UI 约束')
    assert(packageSource.includes('locationService.hqSync.test.ts'), '测试脚本未运行仓库标签 HQ 同步服务行为测试')
  })
  if (localeAndScriptFailure) failures.push(localeAndScriptFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('warehouseLocationsCompactUi.logic.test: ok')
}

await main()
