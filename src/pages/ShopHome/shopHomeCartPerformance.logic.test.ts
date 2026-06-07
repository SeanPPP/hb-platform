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

function extractFunctionBody(source: string, marker: string, endMarker: string) {
  const start = source.indexOf(marker)
  const end = source.indexOf(endMarker, start)
  assert(start >= 0 && end > start, `找不到 ${marker} 对应的函数代码块`)
  return source.slice(start, end)
}

const shopHomeFile = path.resolve(process.cwd(), 'src/pages/ShopHome/index.tsx')
const shopHomeSource = readFileSync(shopHomeFile, 'utf8')

async function main() {
  const failures: string[] = []

  const normalAddFailure = await runTest('普通 Add 应复用 cart/add 返回的购物车并避免二次拉整车', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const handleAddToCart = async',
      'const handleRemoveFromCart = async',
    )

    assert(
      body.includes('const nextCart = await addStoreOrderCartItem({') &&
        body.includes('setCart(nextCart)') &&
        !body.includes('refreshCart()'),
      '普通 Add 仍然没有直接使用 addStoreOrderCartItem 返回的 cart，或仍在二次刷新购物车',
    )
  })
  if (normalAddFailure) failures.push(normalAddFailure)

  const scanAddFailure = await runTest('扫码 Add 应复用 cart/add 返回的购物车并避免二次拉整车', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const addScannedProductToCart = useCallback',
      'const handleBarcodeSubmit = useCallback',
    )

    assert(
      body.includes('const nextCart = await addStoreOrderCartItem({') &&
        body.includes('setCart(nextCart)') &&
        !body.includes('getActiveStoreOrderCart('),
      '扫码 Add 仍然在 addStoreOrderCartItem 后额外调用 getActiveStoreOrderCart',
    )
  })
  if (scanAddFailure) failures.push(scanAddFailure)

  const dynamicDataFailure = await runTest('加购后应只刷新当前商品动态数据', () => {
    const normalAddBody = extractFunctionBody(
      shopHomeSource,
      'const handleAddToCart = async',
      'const handleRemoveFromCart = async',
    )
    const scanAddBody = extractFunctionBody(
      shopHomeSource,
      'const addScannedProductToCart = useCallback',
      'const handleBarcodeSubmit = useCallback',
    )

    assert(
      shopHomeSource.includes('const refreshDynamicDataForProducts = useCallback') &&
        normalAddBody.includes('refreshDynamicDataForProducts([product.productCode])') &&
        scanAddBody.includes('refreshDynamicDataForProducts([product.productCode])') &&
        !normalAddBody.includes('refreshDynamicData()') &&
        !scanAddBody.includes('refreshDynamicData()'),
      '加购后仍在刷新整页 dynamic-data 或购物车，而不是只刷新当前商品',
    )
  })
  if (dynamicDataFailure) failures.push(dynamicDataFailure)

  const defaultSortFailure = await runTest('商城首页默认商品查询应命中后端首页缓存排序', () => {
    const fetchProductsBody = extractFunctionBody(
      shopHomeSource,
      'const fetchProducts = async',
      'void fetchProducts()',
    )

    assert(
      fetchProductsBody.includes("sortBy: 'Default'") &&
        !fetchProductsBody.includes("sortBy: 'productName'"),
      '商城首页默认商品查询未使用后端首页缓存对应的 Default 排序',
    )
  })
  if (defaultSortFailure) failures.push(defaultSortFailure)

  const storeScopeFailure = await runTest('商城首页商品查询必须带当前分店并等待分店选中', () => {
    const fetchProductsBody = extractFunctionBody(
      shopHomeSource,
      'const fetchProducts = async',
      'void fetchProducts()',
    )

    assert(
      fetchProductsBody.includes('if (!selectedStore?.storeCode)') &&
        fetchProductsBody.includes('setProducts([])') &&
        fetchProductsBody.includes('setTotal(0)') &&
        fetchProductsBody.includes('storeCode: selectedStore.storeCode'),
      '商城首页商品查询未等待当前分店，或未把 selectedStore.storeCode 传给后端',
    )
  })
  if (storeScopeFailure) failures.push(storeScopeFailure)

  const storeScopeDependencyFailure = await runTest('商城首页切换分店后应重新加载商品列表', () => {
    assert(
      shopHomeSource.includes('selectedStore?.storeCode, gradeFilter, t') ||
        shopHomeSource.includes('gradeFilter, selectedStore?.storeCode, t'),
      '商品加载 effect 依赖缺少 selectedStore.storeCode，切换分店后不会重新按门店加载',
    )
  })
  if (storeScopeDependencyFailure) failures.push(storeScopeDependencyFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('shopHomeCartPerformance.logic.test: ok')
}

await main()
