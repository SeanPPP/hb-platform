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

const comingSoonFile = path.resolve(process.cwd(), 'src/pages/ShopHome/components/ComingSoonSection.tsx')
const comingSoonSource = readFileSync(comingSoonFile, 'utf8')
const styleFile = path.resolve(process.cwd(), 'src/styles/global.css')
const styleSource = readFileSync(styleFile, 'utf8')
const zhLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/zh.json')
const zhLocaleSource = readFileSync(zhLocaleFile, 'utf8')
const enLocaleFile = path.resolve(process.cwd(), 'src/i18n/locales/en.json')
const enLocaleSource = readFileSync(enLocaleFile, 'utf8')

function extractCssBlock(selector: string) {
  const start = styleSource.indexOf(selector)
  assert(start >= 0, `找不到样式 ${selector}`)
  const open = styleSource.indexOf('{', start)
  const close = styleSource.indexOf('}', open)
  assert(open > start && close > open, `找不到样式 ${selector} 的代码块`)
  return styleSource.slice(open + 1, close)
}

async function main() {
  const failures: string[] = []

  const summaryOnlyFailure = await runTest('Coming Soon 首屏应只加载货柜摘要', () => {
    assert(
      comingSoonSource.includes('getComingSoonContainerSummaries') &&
        !comingSoonSource.includes('getComingSoonContainers()'),
      'Coming Soon 首屏仍在调用全量 getComingSoonContainers()，会提前拉所有货柜商品',
    )
  })
  if (summaryOnlyFailure) failures.push(summaryOnlyFailure)

  const lazyContainerFailure = await runTest('货柜商品应通过 IntersectionObserver 懒加载', () => {
    assert(
      comingSoonSource.includes('IntersectionObserver') &&
        comingSoonSource.includes('getComingSoonContainerProducts') &&
        comingSoonSource.includes('loadContainerProducts'),
      'Coming Soon 缺少 IntersectionObserver 货柜级懒加载链路',
    )
  })
  if (lazyContainerFailure) failures.push(lazyContainerFailure)

  const virtualListFailure = await runTest('货柜内商品应使用原生虚拟列表减少 DOM', () => {
    assert(
      comingSoonSource.includes('PRODUCT_ROW_HEIGHT') &&
        comingSoonSource.includes('VIRTUAL_OVERSCAN') &&
        comingSoonSource.includes('visibleProducts') &&
        comingSoonSource.includes('shop-coming-soon-virtual-spacer') &&
        comingSoonSource.includes('productListElementsRef'),
      'Coming Soon 缺少商品虚拟列表的窗口计算和占位元素',
    )
  })
  if (virtualListFailure) failures.push(virtualListFailure)

  const filterScrollResetFailure = await runTest('切换筛选时应同步重置虚拟列表 DOM 滚动位置', () => {
    assert(
      comingSoonSource.includes('resetProductScrollPositions') &&
        comingSoonSource.includes('element.scrollTop = 0') &&
        comingSoonSource.includes('resetProductScrollPositions()'),
      'Coming Soon 切换 All/Reorder/New 时未同步重置商品列表真实 scrollTop',
    )
  })
  if (filterScrollResetFailure) failures.push(filterScrollResetFailure)

  const containerFilterFailure = await runTest('每个货柜应支持独立统计过滤并保留顶部全局过滤', () => {
    assert(
      comingSoonSource.includes("const [filterMode, setFilterMode] = useState<FilterMode>('all')") &&
        comingSoonSource.includes('containerFilterModes') &&
        comingSoonSource.includes('containerFilterModes[container.hguid] ?? filterMode') &&
        comingSoonSource.includes('getContainerFilterStats') &&
        comingSoonSource.includes('shop-coming-soon-container-filters'),
      'Coming Soon 缺少顶部全局过滤保留和每货柜独立过滤覆盖逻辑',
    )
  })
  if (containerFilterFailure) failures.push(containerFilterFailure)

  const visibleContainerFailure = await runTest('顶部全局过滤应隐藏已加载后无匹配商品的货柜', () => {
    assert(
      comingSoonSource.includes('visibleContainers') &&
        comingSoonSource.includes('filterMode === \'all\'') &&
        comingSoonSource.includes('state.status !== \'loaded\'') &&
        comingSoonSource.includes('state.products.some((product) => matchesFilter(product, filterMode))') &&
        comingSoonSource.includes('visibleContainers.map((container) => {'),
      'Coming Soon 顶部全局过滤未派生可见货柜，已加载后无匹配商品的货柜仍会占位',
    )
  })
  if (visibleContainerFailure) failures.push(visibleContainerFailure)

  const containerFilterI18nFailure = await runTest('每货柜独立过滤按钮应支持国际化', () => {
    assert(
      comingSoonSource.includes("import { useTranslation } from 'react-i18next'") &&
        comingSoonSource.includes("t('shop.comingSoonFilterAll'") &&
        comingSoonSource.includes("t('shop.comingSoonFilterReorder'") &&
        comingSoonSource.includes("t('shop.comingSoonFilterNew'") &&
        zhLocaleSource.includes('"comingSoonFilterAll"') &&
        zhLocaleSource.includes('"comingSoonFilterReorder"') &&
        zhLocaleSource.includes('"comingSoonFilterNew"') &&
        enLocaleSource.includes('"comingSoonFilterAll"') &&
        enLocaleSource.includes('"comingSoonFilterReorder"') &&
        enLocaleSource.includes('"comingSoonFilterNew"'),
      'Coming Soon 每货柜过滤按钮仍有硬编码文案或缺少中英文 locale key',
    )
  })
  if (containerFilterI18nFailure) failures.push(containerFilterI18nFailure)

  const globalFilterClearsLocalFailure = await runTest('切换顶部全局过滤时应清空单柜过滤覆盖', () => {
    assert(
      comingSoonSource.includes('setContainerFilterModes({})') &&
        comingSoonSource.includes('resetProductScrollPositions()'),
      'Coming Soon 顶部全局过滤切换未清空单柜覆盖或未重置虚拟列表滚动',
    )
  })
  if (globalFilterClearsLocalFailure) failures.push(globalFilterClearsLocalFailure)

  const containerFilterScrollResetFailure = await runTest('切换单柜过滤时只应重置当前货柜滚动位置', () => {
    assert(
      comingSoonSource.includes('resetContainerProductScrollPosition') &&
        comingSoonSource.includes('productListElementsRef.current.get(containerGuid)') &&
        comingSoonSource.includes('handleContainerFilterChange'),
      'Coming Soon 单柜过滤切换未只重置当前货柜虚拟列表滚动',
    )
  })
  if (containerFilterScrollResetFailure) failures.push(containerFilterScrollResetFailure)

  const dateToneFailure = await runTest('货柜日期应按状态显示不同颜色', () => {
    assert(
      comingSoonSource.includes('getComingSoonDateTone') &&
        comingSoonSource.includes("return 'arrived'") &&
        comingSoonSource.includes("return 'soon'") &&
        comingSoonSource.includes("return 'future'") &&
        comingSoonSource.includes("return 'unknown'"),
      'Coming Soon 缺少 arrived/soon/future/unknown 日期颜色状态 helper',
    )

    assert(
      styleSource.includes('.shop-coming-soon-date-badge') &&
        styleSource.includes('.shop-coming-soon-date-badge--arrived') &&
        styleSource.includes('.shop-coming-soon-date-badge--soon') &&
        styleSource.includes('.shop-coming-soon-date-badge--future') &&
        styleSource.includes('.shop-coming-soon-date-badge--unknown'),
      'Coming Soon 缺少稳定日期颜色 class',
    )
  })
  if (dateToneFailure) failures.push(dateToneFailure)

  const observerFallbackFailure = await runTest('无 IntersectionObserver 时应加载全部货柜避免永久骨架屏', () => {
    assert(
      comingSoonSource.includes("typeof IntersectionObserver === 'undefined'") &&
        comingSoonSource.includes('containers.forEach((container) => {') &&
        !comingSoonSource.includes('containers.slice(0, 2)'),
      'Coming Soon 在无 IntersectionObserver 环境下仍可能只加载部分货柜',
    )
  })
  if (observerFallbackFailure) failures.push(observerFallbackFailure)

  const lazyImageFailure = await runTest('Coming Soon 商品图片应懒加载', () => {
    assert(
      comingSoonSource.includes('loading="lazy"'),
      'Coming Soon 商品图片未设置 loading="lazy"，首屏外图片会抢占资源',
    )
  })
  if (lazyImageFailure) failures.push(lazyImageFailure)

  const horizontalLayoutFailure = await runTest('Coming Soon 货柜列表应横向滚动排列', () => {
    const listBlock = extractCssBlock('.shop-coming-soon-list')

    assert(
      listBlock.includes('overflow-x: auto') &&
        listBlock.includes('grid-auto-flow: column') &&
        listBlock.includes('grid-auto-columns'),
      'Coming Soon 货柜列表未配置横向滑动列布局',
    )
  })
  if (horizontalLayoutFailure) failures.push(horizontalLayoutFailure)

  const threeContainerColumnsFailure = await runTest('Coming Soon 桌面端一屏应按 3 个货柜列计算宽度', () => {
    const listBlock = extractCssBlock('.shop-coming-soon-list')

    assert(
      listBlock.includes('calc((100% - 36px) / 3)'),
      'Coming Soon 货柜列宽未按内容区 3 列布局计算',
    )
  })
  if (threeContainerColumnsFailure) failures.push(threeContainerColumnsFailure)

  const tallerContainerFailure = await runTest('Coming Soon 货柜和虚拟商品列表应加高', () => {
    const cardBlock = extractCssBlock('.shop-coming-soon-card')

    assert(
      comingSoonSource.includes('const PRODUCT_ROW_HEIGHT = 176') &&
        comingSoonSource.includes('const PRODUCT_LIST_HEIGHT = 560') &&
        cardBlock.includes('height: 780px'),
      'Coming Soon 货柜高度或虚拟列表高度未按加高后的商品行同步调整',
    )
  })
  if (tallerContainerFailure) failures.push(tallerContainerFailure)

  const cardFlexLayoutFailure = await runTest('Coming Soon 固定高度货柜内部应使用弹性布局避免裁剪', () => {
    const cardBlock = extractCssBlock('.shop-coming-soon-card')
    const bodyBlock = extractCssBlock('.shop-coming-soon-card .ant-card-body')
    const productGridBlock = extractCssBlock('.shop-coming-soon-product-grid')

    assert(
      cardBlock.includes('display: flex') &&
        cardBlock.includes('flex-direction: column') &&
        bodyBlock.includes('flex: 1') &&
        bodyBlock.includes('min-height: 0') &&
        bodyBlock.includes('display: flex') &&
        productGridBlock.includes('flex: 1') &&
        productGridBlock.includes('min-height: 0'),
      'Coming Soon 货柜固定高度内部未使用 flex 承接剩余高度，仍有底部裁剪风险',
    )
  })
  if (cardFlexLayoutFailure) failures.push(cardFlexLayoutFailure)

  const barcodePreviewFailure = await runTest('Coming Soon 商品行应生成条码图', () => {
    assert(
      comingSoonSource.includes('BarcodePreview') &&
        comingSoonSource.includes('product.barcode') &&
        comingSoonSource.includes('textNoWrap'),
      'Coming Soon 商品行未复用 BarcodePreview 生成条码图并保持条码文本单行',
    )
  })
  if (barcodePreviewFailure) failures.push(barcodePreviewFailure)

  const retailPriceFailure = await runTest('Coming Soon 商品行应显示建议零售价且缺失时为空', () => {
    assert(
      comingSoonSource.includes('formatComingSoonRetailPrice') &&
        comingSoonSource.includes('product.retailPrice') &&
        comingSoonSource.includes("t('shop.rrp'") &&
        comingSoonSource.includes("typeof price !== 'number'") &&
        comingSoonSource.includes("return ''"),
      'Coming Soon 商品行未显示建议零售价，或缺失零售价时不是空字符串',
    )
  })
  if (retailPriceFailure) failures.push(retailPriceFailure)

  const fullItemNumberFailure = await runTest('Coming Soon 货号应完整显示不省略不换行', () => {
    const itemNumberBlock = extractCssBlock('.shop-coming-soon-item-number-value')

    assert(
      itemNumberBlock.includes('white-space: nowrap') &&
        !itemNumberBlock.includes('text-overflow') &&
        !itemNumberBlock.includes('overflow: hidden'),
      'Coming Soon 货号值样式仍包含省略或隐藏策略',
    )
  })
  if (fullItemNumberFailure) failures.push(fullItemNumberFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('shopComingSoonPerformance.logic.test: ok')
}

await main()
