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
const productCardFile = path.resolve(process.cwd(), 'src/pages/ShopHome/components/ProductCard.tsx')
const productCardSource = readFileSync(productCardFile, 'utf8')
const bestSellersFile = path.resolve(process.cwd(), 'src/pages/ShopHome/components/BestSellersSection.tsx')
const bestSellersSource = readFileSync(bestSellersFile, 'utf8')
const globalCssFile = path.resolve(process.cwd(), 'src/styles/global.css')
const globalCssSource = readFileSync(globalCssFile, 'utf8')
const zhLocaleSource = readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8')
const enLocaleSource = readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8')

async function main() {
  const failures: string[] = []

  const productQuantityUpdateFailure = await runTest('商品卡数量按钮应直接设置购物车数量并复用返回购物车', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const handleProductQuantityChange = useCallback',
      'const handleRemoveFromCart = async',
    )

    assert(
      shopHomeSource.includes('updateStoreOrderCartItem,') &&
        shopHomeSource.includes('const SHOP_PRODUCT_QUANTITY_UPDATE_DEBOUNCE_MS = 300') &&
        shopHomeSource.includes('const quantityUpdateTimersRef = useRef<Record<string, number>>({})') &&
        shopHomeSource.includes('const quantityUpdateVersionRef = useRef<Record<string, number>>({})') &&
        shopHomeSource.includes('Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))') &&
        body.includes('const nextCart = await updateStoreOrderCartItem({') &&
        body.includes('quantity: normalizedQuantity') &&
        body.includes('setCart(nextCart)') &&
        body.includes('refreshDynamicDataForProducts([productCode])') &&
        body.includes('setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: normalizedQuantity }))') &&
        body.includes('delete next[productCode]') &&
        body.includes('quantityUpdateTimersRef.current[productCode] = window.setTimeout') &&
        body.includes('window.clearTimeout(quantityUpdateTimersRef.current[productCode])') &&
        body.includes('quantityUpdateVersionRef.current[productCode] = updateVersion') &&
        body.includes('quantityUpdateVersionRef.current[productCode] !== updateVersion') &&
        body.includes('setQuantityLoadingMap') &&
        body.includes('normalizedQuantity <= 0 && currentCartQuantity <= 0') &&
        !body.includes('refreshCart()'),
      '商品卡数量更新未直接调用 cart/update、未复用返回购物车，或缺少乐观更新/回滚/单商品刷新',
    )
  })
  if (productQuantityUpdateFailure) failures.push(productQuantityUpdateFailure)

  const productQuantityUpdateSuccessMessageFailure = await runTest('商品卡数量保存成功后应只提示最新数量', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const handleProductQuantityChange = useCallback',
      'const handleRemoveFromCart = async',
    )
    const refreshIndex = body.indexOf('await refreshDynamicDataForProducts([productCode])')
    const latestVersionCheckIndex = body.indexOf('if (quantityUpdateVersionRef.current[productCode] !== updateVersion) return', refreshIndex)
    const successMessageIndex = body.indexOf("message.success({", latestVersionCheckIndex)

    assert(
      refreshIndex >= 0 &&
        latestVersionCheckIndex > refreshIndex &&
        successMessageIndex > latestVersionCheckIndex &&
        body.includes("content: t('shop.cartQuantityUpdated', { quantity: normalizedQuantity })") &&
        body.includes('key: `shop-product-quantity-${productCode}`') &&
        zhLocaleSource.includes('"cartQuantityUpdated": "数量已保存：{{quantity}}"') &&
        enLocaleSource.includes('"cartQuantityUpdated": "Quantity saved: {{quantity}}"'),
      '商品卡数量保存成功后缺少最新版本校验后的成功提示、message key，或 zh/en 文案不同步',
    )
  })
  if (productQuantityUpdateSuccessMessageFailure) failures.push(productQuantityUpdateSuccessMessageFailure)

  const productCardAddFailure = await runTest('商品卡未入车商品应保留 Add 首次加购并乐观更新', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const handleAddToCart = useCallback',
      'const handleProductQuantityChange = useCallback',
    )

    assert(
      body.includes('const addQuantity = Math.max(1, Math.floor(Number.isFinite(quantity) ? quantity : 0))') &&
        body.includes('setOptimisticCartQuantityMap((prev) => ({ ...prev, [product.productCode]: addQuantity }))') &&
        body.includes('const nextCart = await addStoreOrderCartItem({') &&
        body.includes('quantity: addQuantity') &&
        body.includes('setCart(nextCart)') &&
        body.includes('refreshDynamicDataForProducts([product.productCode])') &&
        body.includes('delete next[product.productCode]') &&
        shopHomeSource.includes('onAddToCart={handleAddToCart}'),
      '商品卡 Add 没有走 cart/add，或缺少乐观数量、成功刷新、失败回滚',
    )
  })
  if (productCardAddFailure) failures.push(productCardAddFailure)

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

  const dynamicDataFailure = await runTest('商品卡 Add、数量更新和扫码加购后应只刷新当前商品动态数据', () => {
    const productAddBody = extractFunctionBody(
      shopHomeSource,
      'const handleAddToCart = useCallback',
      'const handleProductQuantityChange = useCallback',
    )
    const productQuantityBody = extractFunctionBody(
      shopHomeSource,
      'const handleProductQuantityChange = useCallback',
      'const handleRemoveFromCart = async',
    )
    const scanAddBody = extractFunctionBody(
      shopHomeSource,
      'const addScannedProductToCart = useCallback',
      'const handleBarcodeSubmit = useCallback',
    )

    assert(
      shopHomeSource.includes('const refreshDynamicDataForProducts = useCallback') &&
        productAddBody.includes('refreshDynamicDataForProducts([product.productCode])') &&
        productQuantityBody.includes('refreshDynamicDataForProducts([productCode])') &&
        scanAddBody.includes('refreshDynamicDataForProducts([product.productCode])') &&
        !productAddBody.includes('refreshDynamicData()') &&
        !productQuantityBody.includes('refreshDynamicData()') &&
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

  const pageSizeOptionsFailure = await runTest('商城首页默认每页 200 且支持 50/100/200/500', () => {
    assert(
      shopHomeSource.includes('const SHOP_HOME_PAGE_SIZE_OPTIONS = [50, 100, 200, 500]') &&
        shopHomeSource.includes('const [pageSize, setPageSize] = useState(200)') &&
        shopHomeSource.includes('options={SHOP_HOME_PAGE_SIZE_OPTIONS.map((value) => ({ value, label: String(value) }))}') &&
        shopHomeSource.includes('pageSizeOptions={SHOP_HOME_PAGE_SIZE_OPTIONS.map(String)}'),
      '商城首页默认每页数量或顶部/底部分页选项未统一为 50/100/200/500',
    )
  })
  if (pageSizeOptionsFailure) failures.push(pageSizeOptionsFailure)

  const cartOnlyFilterFailure = await runTest('商城首页购物车商品按钮应显示当前分店全部购物车商品', () => {
    assert(
      shopHomeSource.includes("import { ShoppingCartOutlined } from '@ant-design/icons'") &&
        shopHomeSource.includes('const [cartOnlyFilter, setCartOnlyFilter] = useState(false)') &&
        shopHomeSource.includes('const cart = useShopStore((state) => state.cart)') &&
        shopHomeSource.includes('const cartProductItems = useMemo<StoreOrderProductItem[]>(() => {') &&
        shopHomeSource.includes('return (cart?.items ?? []).map((item) => ({') &&
        shopHomeSource.includes('const cartProductPageItems = useMemo(() => {') &&
        shopHomeSource.includes('return cartProductItems.slice(startIndex, startIndex + pageSize)') &&
        shopHomeSource.includes('const displayProducts = cartOnlyFilter ? cartProductPageItems : products') &&
        shopHomeSource.includes('const displayTotal = cartOnlyFilter ? cartProductItems.length : total') &&
        shopHomeSource.includes('const cartProductCount = cart?.items.length ?? 0') &&
        shopHomeSource.includes('type={cartOnlyFilter ?') &&
        shopHomeSource.includes('icon={<ShoppingCartOutlined />}') &&
        shopHomeSource.includes('setCartOnlyFilter((current) => !current)') &&
        shopHomeSource.includes("t('shop.cartProductsFilter', { count: cartProductCount })") &&
        shopHomeSource.includes('cartOnlyFilter ? t(') &&
        shopHomeSource.includes("t('shop.noCartProductsFound')"),
      '商城首页购物车商品过滤没有读取 cart.items、没有本地分页，或没有独立按钮/空状态文案',
    )
  })
  if (cartOnlyFilterFailure) failures.push(cartOnlyFilterFailure)

  const cartOnlyDynamicDataFailure = await runTest('购物车商品过滤应使用购物车数量覆盖商品卡动态数据', () => {
    assert(
      shopHomeSource.includes('const cartProductDynamicDataMap = useMemo<Record<string, StoreOrderDynamicData>>(() => {') &&
        shopHomeSource.includes('cartQuantity: item.quantity') &&
        shopHomeSource.includes('const cartQuantityByProductCode = useMemo<Record<string, number>>(() => {') &&
        shopHomeSource.includes('acc[item.productCode] = item.quantity') &&
        shopHomeSource.includes('cartOnlyFilter') &&
        shopHomeSource.includes('const currentCartQuantity = cartQuantityByProductCode[productCode] ?? 0') &&
        shopHomeSource.includes('const dynamicData = cartOnlyFilter') &&
        shopHomeSource.includes('? cartProductDynamicDataMap[product.productCode]') &&
        shopHomeSource.includes(': dynamicDataMap[product.productCode]') &&
        shopHomeSource.includes('const syncedDynamicData: StoreOrderDynamicData = {') &&
        shopHomeSource.includes('cartQuantity: cartQuantityByProductCode[product.productCode] ?? 0') &&
        shopHomeSource.includes('dynamicData={cardDynamicData}'),
      '商品卡没有用全局 cart.items 覆盖 ProductCard dynamicData.cartQuantity',
    )
  })
  if (cartOnlyDynamicDataFailure) failures.push(cartOnlyDynamicDataFailure)

  const cartClearSyncFailure = await runTest('清空购物车后应清理卡片乐观状态和未提交数量更新', () => {
    assert(
      shopHomeSource.includes('if (cart?.items.length) {\n      return\n    }') &&
        shopHomeSource.includes('Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))') &&
        shopHomeSource.includes('quantityUpdateTimersRef.current = {}') &&
        shopHomeSource.includes('quantityUpdateVersionRef.current = {}') &&
        shopHomeSource.includes('setOptimisticCartQuantityMap({})') &&
        shopHomeSource.includes('setRemovingCartProductMap({})') &&
        shopHomeSource.includes('setQuantityLoadingMap({})') &&
        shopHomeSource.includes('[cart?.items.length]'),
      '购物车清空后没有取消未提交数量更新，或没有清理商品卡乐观/删除中状态',
    )
  })
  if (cartClearSyncFailure) failures.push(cartClearSyncFailure)

  const cartOnlyI18nFailure = await runTest('购物车商品过滤文案应保持中英文同步', () => {
    assert(
      zhLocaleSource.includes('"cartProductsFilter": "购物车商品 ({{count}})"') &&
        zhLocaleSource.includes('"cartProductsTitle": "购物车商品"') &&
        zhLocaleSource.includes('"noCartProductsFound": "购物车暂无商品"') &&
        enLocaleSource.includes('"cartProductsFilter": "Cart Products ({{count}})"') &&
        enLocaleSource.includes('"cartProductsTitle": "Cart Products"') &&
        enLocaleSource.includes('"noCartProductsFound": "No products in cart"'),
      '购物车商品过滤缺少 zh/en 同步文案',
    )
  })
  if (cartOnlyI18nFailure) failures.push(cartOnlyI18nFailure)

  const searchCategoryPathFailure = await runTest('搜索商品卡片应显示分类完整路径', () => {
    assert(
      shopHomeSource.includes('buildWarehouseCategoryLookup') &&
        shopHomeSource.includes('getWarehouseProductCategoryTooltip') &&
        shopHomeSource.includes('const shouldShowCategoryPath = Boolean(keyword)') &&
        shopHomeSource.includes('categoryPath={categoryPathMap[product.productCode]}'),
      '搜索结果商品卡片未复用分类树路径工具，或未把分类完整路径传给 ProductCard',
    )
  })
  if (searchCategoryPathFailure) failures.push(searchCategoryPathFailure)

  const searchOnlyCategoryPathFailure = await runTest('分类路径只应显示在搜索结果卡片', () => {
    assert(
      shopHomeSource.includes('if (!shouldShowCategoryPath || !categoryLookup)') &&
        shopHomeSource.includes('return {}'),
      '分类路径缺少 keyword 开关，分类页或全部商品页可能也会显示路径',
    )
  })
  if (searchOnlyCategoryPathFailure) failures.push(searchOnlyCategoryPathFailure)

  const productCardCategoryPathFailure = await runTest('商品卡片应在货号下方以两行省略显示分类路径', () => {
    assert(
      productCardSource.includes('categoryPath?: string') &&
        productCardSource.includes('Tooltip') &&
        productCardSource.includes('shop-product-category-path') &&
        productCardSource.includes('ellipsis={{ rows: 2 }}') &&
        globalCssSource.includes('.shop-product-category-path'),
      'ProductCard 未声明 categoryPath，或分类路径没有 Tooltip/两行省略/稳定样式',
    )
  })
  if (productCardCategoryPathFailure) failures.push(productCardCategoryPathFailure)

  const productCardQuantityStepperFailure = await runTest('商品卡数量应默认 0 并使用 INNER 步进直接更新购物车', () => {
    assert(
      productCardSource.includes('onQuantityChange: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void') &&
        productCardSource.includes('onAddToCart: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void') &&
        productCardSource.includes('ShoppingCartOutlined') &&
        productCardSource.includes('const stepQuantity = product.minOrderQuantity > 0 ? product.minOrderQuantity : 1') &&
        productCardSource.includes('const cartQuantity = dynamicData?.cartQuantity ?? 0') &&
        productCardSource.includes('const [quantity, setQuantity] = useState<number>(0)') &&
        productCardSource.includes('setQuantity(cartQuantity)') &&
        productCardSource.includes('applyQuantityChange(quantity - stepQuantity)') &&
        productCardSource.includes('applyQuantityChange(quantity + stepQuantity)') &&
        productCardSource.includes('min={0}') &&
        productCardSource.includes('controls={false}') &&
        productCardSource.includes('disabled={removing || quantity <= 0}') &&
        productCardSource.includes('disabled={removing}') &&
        !productCardSource.includes('disabled={loading || quantity <= 0}') &&
        !productCardSource.includes('disabled={loading}\n                className="shop-product-quantity-input"') &&
        productCardSource.includes('onBlur={() => applyQuantityChange(quantity)}') &&
        productCardSource.includes('onPressEnter={() => applyQuantityChange(quantity)}'),
      '商品卡数量控件没有默认 0、没有按 INNER 步进、没有保留 Add，或没有直接提交数量变化',
    )
  })
  if (productCardQuantityStepperFailure) failures.push(productCardQuantityStepperFailure)

  const productCardAddVisibilityFailure = await runTest('商品卡应未入车显示 Add、已入车显示删除且不挤压步进器', () => {
    assert(
      productCardSource.includes('cartQuantity > 0 ? (') &&
        productCardSource.includes('cartQuantity <= 0 ? (') &&
        productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--left"') &&
        productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--right"') &&
        productCardSource.includes("cartQuantity > 0 ? 'shop-product-card-actions--in-cart' : ''") &&
        productCardSource.includes('const addQuantity = quantity > 0 ? quantity : stepQuantity') &&
        productCardSource.includes('void onAddToCart(product, addQuantity)') &&
        productCardSource.includes('className="shop-product-add-button"'),
      '商品卡没有按购物车状态切换删除/Add，或 Add 没有按当前数量/INNER 首次加购',
    )
  })
  if (productCardAddVisibilityFailure) failures.push(productCardAddVisibilityFailure)

  const productCardRemoveOptimisticFailure = await runTest('商品卡删除应先乐观退出已入车状态并防重复点击', () => {
    const body = extractFunctionBody(
      shopHomeSource,
      'const handleRemoveFromCart = async',
      '  return (\n    <div className="shop-home-page">',
    )

    assert(
      productCardSource.includes('removing?: boolean') &&
        productCardSource.includes('removing = false') &&
        productCardSource.includes('if (removing)') &&
        shopHomeSource.includes('const [removingCartProductMap, setRemovingCartProductMap] = useState<Record<string, boolean>>({})') &&
        body.includes('if (removingCartProductMap[productCode])') &&
        body.includes('setRemovingCartProductMap((prev) => ({ ...prev, [productCode]: true }))') &&
        body.includes('setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: 0 }))') &&
        body.includes('delete next[productCode]') &&
        shopHomeSource.includes('const isRemovingFromCart = Boolean(removingCartProductMap[product.productCode])') &&
        shopHomeSource.includes('const optimisticCartQuantity = isRemovingFromCart') &&
        shopHomeSource.includes('? 0') &&
        shopHomeSource.includes('removing={isRemovingFromCart}'),
      '商品卡删除没有先乐观置 0、缺少删除中去重 guard，或 ProductCard 未收到 removing 状态',
    )
  })
  if (productCardRemoveOptimisticFailure) failures.push(productCardRemoveOptimisticFailure)

  const optimisticDynamicDataFailure = await runTest('商城首页应把乐观购物车数量覆盖到商品卡 dynamicData', () => {
    assert(
        shopHomeSource.includes('const [optimisticCartQuantityMap, setOptimisticCartQuantityMap] = useState<Record<string, number>>({})') &&
        shopHomeSource.includes('const optimisticCartQuantity = isRemovingFromCart') &&
        shopHomeSource.includes(': optimisticCartQuantityMap[product.productCode]') &&
        shopHomeSource.includes('const syncedDynamicData: StoreOrderDynamicData =') &&
        shopHomeSource.includes('const cardDynamicData =') &&
        shopHomeSource.includes('? syncedDynamicData') &&
        shopHomeSource.includes('...syncedDynamicData') &&
        shopHomeSource.includes('cartQuantity: optimisticCartQuantity') &&
        shopHomeSource.includes('dynamicData={cardDynamicData}'),
      '商城首页没有把 optimisticCartQuantityMap 覆盖到 ProductCard dynamicData',
    )
  })
  if (optimisticDynamicDataFailure) failures.push(optimisticDynamicDataFailure)

  const productCardQuantityStyleFailure = await runTest('商品卡数量步进器应固定居中且 Add 固定宽度', () => {
    assert(
      productCardSource.includes('shop-product-quantity-stepper') &&
        productCardSource.includes('shop-product-quantity-button') &&
        productCardSource.includes('shop-product-quantity-input') &&
        globalCssSource.includes('grid-template-columns: 28px minmax(108px, 1fr) 58px') &&
        globalCssSource.includes('.shop-product-card-actions--in-cart') &&
        globalCssSource.includes('grid-template-columns: 28px minmax(108px, 1fr) 28px') &&
        globalCssSource.includes('box-sizing: border-box') &&
        globalCssSource.includes('justify-self: center') &&
        globalCssSource.includes('max-width: 120px') &&
        globalCssSource.includes('min-width: 108px') &&
        globalCssSource.includes('.shop-product-quantity-stepper') &&
        globalCssSource.includes('.shop-product-quantity-button') &&
        globalCssSource.includes('.shop-product-quantity-input') &&
        globalCssSource.includes('.shop-product-add-button'),
      '商品卡数量步进器缺少固定居中/Add 固定宽度样式，卡片动作区可能抖动',
    )
  })
  if (productCardQuantityStyleFailure) failures.push(productCardQuantityStyleFailure)

  const categoryPathClickFailure = await runTest('搜索商品分类路径点击后应进入对应分类并清除搜索词', () => {
    assert(
      shopHomeSource.includes('useNavigate') &&
        shopHomeSource.includes('const navigate = useNavigate()') &&
        shopHomeSource.includes('const handleCategoryPathClick = useCallback') &&
        shopHomeSource.includes('product.warehouseCategoryGUID') &&
        shopHomeSource.includes('navigate(`/shop?category=${encodeURIComponent(product.warehouseCategoryGUID)}`)') &&
        shopHomeSource.includes('shouldShowCategoryPath && product.warehouseCategoryGUID') &&
        shopHomeSource.includes('? handleCategoryPathClick') &&
        shopHomeSource.includes(': undefined'),
      '搜索商品分类路径没有点击进入分类、跳转没有清除 keyword，或缺少分类 GUID 时仍会显示可点击状态',
    )
  })
  if (categoryPathClickFailure) failures.push(categoryPathClickFailure)

  const productCardCategoryPathA11yFailure = await runTest('可点击分类路径应支持鼠标和键盘触发', () => {
    assert(
      productCardSource.includes('onCategoryPathClick?: (product: StoreOrderProductItem) => void') &&
        productCardSource.includes('const canClickCategoryPath = Boolean(categoryPath && onCategoryPathClick)') &&
        productCardSource.includes("role={canClickCategoryPath ? 'button' : undefined}") &&
        productCardSource.includes('tabIndex={canClickCategoryPath ? 0 : undefined}') &&
        productCardSource.includes("event.key === 'Enter' || event.key === ' '") &&
        productCardSource.includes('onCategoryPathClick(product)') &&
        globalCssSource.includes('.shop-product-category-path--clickable'),
      'ProductCard 分类路径缺少可点击 prop、键盘触发或可点击样式',
    )
  })
  if (productCardCategoryPathA11yFailure) failures.push(productCardCategoryPathA11yFailure)

  const lazyImageFailure = await runTest('商品卡图片应懒加载避免拖慢首屏', () => {
    assert(
      productCardSource.includes('loading="lazy"'),
      '商品卡图片未设置 loading="lazy"，首屏外图片会抢占首页加载资源',
    )
  })
  if (lazyImageFailure) failures.push(lazyImageFailure)

  const bestSellerLazyImageFailure = await runTest('热销商品表格图片应懒加载避免拖慢首屏', () => {
    assert(
      bestSellersSource.includes('loading="lazy"') &&
        bestSellersSource.includes('decoding="async"') &&
        bestSellersSource.includes('className="shop-best-sellers-image"') &&
        bestSellersSource.includes('shop-best-sellers-image-placeholder'),
      '热销商品表格图片缺少 lazy loading、异步解码或稳定占位，首屏外图片仍可能抢占加载资源',
    )
  })
  if (bestSellerLazyImageFailure) failures.push(bestSellerLazyImageFailure)

  const bestSellerVirtualFailure = await runTest('热销商品列表应使用 AntD Table 虚拟滚动', () => {
    assert(
        bestSellersSource.includes('import type { ColumnsType }') &&
        bestSellersSource.includes('Table') &&
        bestSellersSource.includes('virtual') &&
        bestSellersSource.includes('scroll={{ x: 1080, y: 560 }}') &&
        bestSellersSource.includes('className="shop-best-sellers-table"') &&
        bestSellersSource.includes('rowKey={(record) => record.productCode || record.itemNumber || String(record.rank)}') &&
        !bestSellersSource.includes('virtualWindow.visibleProducts.map') &&
        !bestSellersSource.includes('ResizeObserver') &&
        !bestSellersSource.includes('Badge.Ribbon'),
      '热销商品列表未切换到 AntD Table virtual，或仍保留手写 Card 虚拟列表',
    )
  })
  if (bestSellerVirtualFailure) failures.push(bestSellerVirtualFailure)

  const bestSellerAbortFailure = await runTest('热销商品切换筛选分页时应取消旧请求', () => {
    assert(
      bestSellersSource.includes('const controller = new AbortController()') &&
        bestSellersSource.includes('controller.signal') &&
        bestSellersSource.includes('controller.abort()') &&
        bestSellersSource.includes("fetchError instanceof DOMException && fetchError.name === 'AbortError'"),
      '热销商品请求缺少 AbortController，慢请求仍可能覆盖最新筛选结果',
    )
  })
  if (bestSellerAbortFailure) failures.push(bestSellerAbortFailure)

  const bestSellerDateRangeFailure = await runTest('热销商品默认日期范围应从昨天开始且使用本地业务日期', () => {
    assert(
      bestSellersSource.includes("import { buildBestSellerDateRange } from '../../../utils/bestSellerDateRange'") &&
        bestSellersSource.includes('buildBestSellerDateRange(timeRange)') &&
        !bestSellersSource.includes('toISOString().slice(0, 10)') &&
        !bestSellersSource.includes('start.setDate(start.getDate() - timeRange)'),
      '热销商品日期范围未复用本地业务日期工具，或仍用 toISOString/今天作为结束日期',
    )
  })
  if (bestSellerDateRangeFailure) failures.push(bestSellerDateRangeFailure)

  const bestSellerStatusNoticeFailure = await runTest('热销商品非 Fresh 状态应提示统计未就绪或回退中', () => {
    assert(
      bestSellersSource.includes('const isBestSellerStatisticFresh = statisticStatus ===') &&
        bestSellersSource.includes('bestSellerStatusNotice') &&
        bestSellersSource.includes('商品统计未就绪或正在回退 POSM 实时数据，加载可能较慢。') &&
        bestSellersSource.includes('getBestSellerEmptyText()') &&
        bestSellersSource.includes('统计未就绪或正在回退 POSM，暂未返回热销商品。'),
      '热销商品缺少非 Fresh 状态提示，或空数据文案没有区分统计未就绪和真无数据',
    )
  })
  if (bestSellerStatusNoticeFailure) failures.push(bestSellerStatusNoticeFailure)

  const bestSellerTableColumnsFailure = await runTest('热销商品表格应显示条码、复制、状态、分店销量和加购操作', () => {
    assert(
      bestSellersSource.includes('BarcodePreview') &&
        bestSellersSource.includes('CopyOutlined') &&
        bestSellersSource.includes('ShoppingCartOutlined') &&
        bestSellersSource.includes('Popover') &&
        bestSellersSource.includes("title: 'Gross Profit'") &&
        bestSellersSource.includes("title: 'Gross Margin'") &&
        bestSellersSource.includes("title: 'Stats'") &&
        bestSellersSource.includes('addStoreOrderCartItem') &&
        bestSellersSource.includes('setCart(nextCart)') &&
        !bestSellersSource.includes("title: 'Product Code'"),
      '热销商品表格列未包含条码、货号复制、分店销量弹层或加购操作，或仍显示 Product Code 列',
    )
  })
  if (bestSellerTableColumnsFailure) failures.push(bestSellerTableColumnsFailure)

  const bestSellerStatsAlwaysVisibleFailure = await runTest('热销商品 Stats 列应保留给所有订货前台用户', () => {
    assert(
      bestSellersSource.includes("title: 'Stats'") &&
        !bestSellersSource.includes("import { useAuthStore } from '../../../store/auth'") &&
        !bestSellersSource.includes('const isAdmin = useAuthStore') &&
        !bestSellersSource.includes('...(isAdmin ? ['),
      '热销商品 Stats 列不应按管理员条件隐藏，普通订货前台用户也要保留完整列',
    )
  })
  if (bestSellerStatsAlwaysVisibleFailure) failures.push(bestSellerStatsAlwaysVisibleFailure)

  const bestSellerBranchSalesFailure = await runTest('热销商品分店销量明细应按销量倒序展示', () => {
    assert(
        bestSellersSource.includes('function getBranchSalesRows(product: BestSellerProduct)') &&
        bestSellersSource.includes('function getBranchSalesCount(product: BestSellerProduct)') &&
        bestSellersSource.includes('return product.branchSalesCount ?? product.branchSales?.length ?? 0') &&
        bestSellersSource.includes('].sort((a, b) => (b.quantity ?? 0) - (a.quantity ?? 0))') &&
        bestSellersSource.includes("defaultSortOrder: 'descend'") &&
        bestSellersSource.includes("dataIndex: 'salesAmount'") &&
        bestSellersSource.includes("dataIndex: 'grossProfit'") &&
        bestSellersSource.includes("dataIndex: 'grossMarginRate'") &&
        bestSellersSource.includes('const count = getBranchSalesCount(record)') &&
        bestSellersSource.includes('shop-best-sellers-branch-sales-popover'),
      '分店销量明细缺少默认销量倒序排序、branchSalesCount 优先级或紧凑弹层',
    )
  })
  if (bestSellerBranchSalesFailure) failures.push(bestSellerBranchSalesFailure)

  const bestSellerAddGuardFailure = await runTest('热销商品加购只允许上架且已选分店的商品', () => {
    assert(
      bestSellersSource.includes('record.isActive !== true || !selectedStore?.storeCode') &&
        bestSellersSource.includes('disabled={disabled}') &&
        bestSellersSource.includes('getAddQuantity(product)') &&
        bestSellersSource.includes('message.warning(t(') &&
        bestSellersSource.includes('setCart(nextCart)'),
      '热销商品加购未限制上下架状态和分店，或未复用 cart/add 返回结果',
    )
  })
  if (bestSellerAddGuardFailure) failures.push(bestSellerAddGuardFailure)

  const bestSellerTableLayoutFailure = await runTest('热销商品表格应保留全列并使用紧凑列宽', () => {
    assert(
      bestSellersSource.includes("title: 'Rank'") &&
        bestSellersSource.includes("title: 'Image'") &&
        bestSellersSource.includes("title: 'Barcode'") &&
        bestSellersSource.includes("title: 'Item No.'") &&
        bestSellersSource.includes("title: 'Product Name'") &&
        bestSellersSource.includes("title: 'Units Sold'") &&
        bestSellersSource.includes("title: 'Sales Amount'") &&
        bestSellersSource.includes("title: 'Gross Profit'") &&
        bestSellersSource.includes("title: 'Gross Margin'") &&
        bestSellersSource.includes("title: 'Stats'") &&
        bestSellersSource.includes("title: 'Status'") &&
        bestSellersSource.includes("title: 'Stores Sold'") &&
        bestSellersSource.includes("title: 'Action'") &&
        bestSellersSource.includes('scroll={{ x: 1080, y: 560 }}') &&
        bestSellersSource.includes('width: 44') &&
        bestSellersSource.includes('width: 50') &&
        bestSellersSource.includes('width: 104') &&
        bestSellersSource.includes('width: 155') &&
        globalCssSource.includes('.shop-best-sellers-table') &&
        globalCssSource.includes('.shop-best-sellers-image-cell') &&
        globalCssSource.includes('width: 42px') &&
        globalCssSource.includes('height: 42px') &&
        globalCssSource.includes('.shop-best-sellers-barcode-cell') &&
        globalCssSource.includes('max-width: 98px') &&
        globalCssSource.includes('.shop-best-sellers-item-number') &&
        globalCssSource.includes('.shop-best-sellers-store-count') &&
        globalCssSource.includes('width: 560px') &&
        globalCssSource.includes('.shop-best-sellers-product-name') &&
        globalCssSource.includes('max-height: calc(1.3em * 2)') &&
        globalCssSource.includes('-webkit-line-clamp: 2') &&
        globalCssSource.includes('.shop-best-sellers-rank') &&
        globalCssSource.includes('.shop-best-sellers-table .ant-table-cell') &&
        globalCssSource.includes('padding: 7px 6px !important') &&
        !globalCssSource.includes('.shop-best-sellers-virtual-list') &&
        !globalCssSource.includes('.shop-best-seller-card'),
      '热销商品表格未保留全列、紧凑列宽和固定图片条码尺寸，或仍保留旧卡片样式',
    )
  })
  if (bestSellerTableLayoutFailure) failures.push(bestSellerTableLayoutFailure)

  const perfLogGateFailure = await runTest('首页性能 console 日志生产环境默认关闭', () => {
    const helperBody = extractFunctionBody(
      shopHomeSource,
      'function logShopHomePerf',
      'export default function ShopHomePage',
    )

    assert(
      helperBody.includes('import.meta.env.DEV') &&
        helperBody.includes("window.localStorage.getItem('shopHomePerf') === '1'") &&
        helperBody.includes('if (!isDebugEnabled)'),
      '首页性能 console 日志缺少开发环境或显式开关保护',
    )
  })
  if (perfLogGateFailure) failures.push(perfLogGateFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('shopHomeCartPerformance.logic.test: ok')
}

await main()
