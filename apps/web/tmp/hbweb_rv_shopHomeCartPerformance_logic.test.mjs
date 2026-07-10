// src/pages/ShopHome/shopHomeCartPerformance.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
function extractFunctionBody(source, marker, endMarker) {
  const start = source.indexOf(marker);
  const end = source.indexOf(endMarker, start);
  assert(start >= 0 && end > start, `\u627E\u4E0D\u5230 ${marker} \u5BF9\u5E94\u7684\u51FD\u6570\u4EE3\u7801\u5757`);
  return source.slice(start, end);
}
var shopHomeFile = path.resolve(process.cwd(), "src/pages/ShopHome/index.tsx");
var shopHomeSource = readFileSync(shopHomeFile, "utf8");
var productCardFile = path.resolve(process.cwd(), "src/pages/ShopHome/components/ProductCard.tsx");
var productCardSource = readFileSync(productCardFile, "utf8");
var bestSellersFile = path.resolve(process.cwd(), "src/pages/ShopHome/components/BestSellersSection.tsx");
var bestSellersSource = readFileSync(bestSellersFile, "utf8");
var globalCssFile = path.resolve(process.cwd(), "src/styles/global.css");
var globalCssSource = readFileSync(globalCssFile, "utf8");
var zhLocaleSource = readFileSync(path.resolve(process.cwd(), "src/i18n/locales/zh.json"), "utf8");
var enLocaleSource = readFileSync(path.resolve(process.cwd(), "src/i18n/locales/en.json"), "utf8");
async function main() {
  const failures = [];
  const productQuantityUpdateFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u6309\u94AE\u5E94\u76F4\u63A5\u8BBE\u7F6E\u8D2D\u7269\u8F66\u6570\u91CF\u5E76\u590D\u7528\u8FD4\u56DE\u8D2D\u7269\u8F66", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleProductQuantityChange = useCallback",
      "const handleRemoveFromCart = async"
    );
    assert(
      shopHomeSource.includes("updateStoreOrderCartItem,") && shopHomeSource.includes("const SHOP_PRODUCT_QUANTITY_UPDATE_DEBOUNCE_MS = 300") && shopHomeSource.includes("const quantityUpdateTimersRef = useRef<Record<string, number>>({})") && shopHomeSource.includes("const quantityUpdateVersionRef = useRef<Record<string, number>>({})") && shopHomeSource.includes("Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))") && body.includes("const nextCart = await updateStoreOrderCartItem({") && body.includes("quantity: normalizedQuantity") && body.includes("setCart(nextCart)") && body.includes("refreshDynamicDataForProducts([productCode])") && body.includes("setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: normalizedQuantity }))") && body.includes("delete next[productCode]") && body.includes("quantityUpdateTimersRef.current[productCode] = window.setTimeout") && body.includes("window.clearTimeout(quantityUpdateTimersRef.current[productCode])") && body.includes("quantityUpdateVersionRef.current[productCode] = updateVersion") && body.includes("quantityUpdateVersionRef.current[productCode] !== updateVersion") && body.includes("setQuantityLoadingMap") && body.includes("normalizedQuantity <= 0 && currentCartQuantity <= 0") && !body.includes("refreshCart()"),
      "\u5546\u54C1\u5361\u6570\u91CF\u66F4\u65B0\u672A\u76F4\u63A5\u8C03\u7528 cart/update\u3001\u672A\u590D\u7528\u8FD4\u56DE\u8D2D\u7269\u8F66\uFF0C\u6216\u7F3A\u5C11\u4E50\u89C2\u66F4\u65B0/\u56DE\u6EDA/\u5355\u5546\u54C1\u5237\u65B0"
    );
  });
  if (productQuantityUpdateFailure) failures.push(productQuantityUpdateFailure);
  const productQuantityUpdateSuccessMessageFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u4FDD\u5B58\u6210\u529F\u540E\u5E94\u53EA\u63D0\u793A\u6700\u65B0\u6570\u91CF", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleProductQuantityChange = useCallback",
      "const handleRemoveFromCart = async"
    );
    const refreshIndex = body.indexOf("await refreshDynamicDataForProducts([productCode])");
    const latestVersionCheckIndex = body.indexOf("if (quantityUpdateVersionRef.current[productCode] !== updateVersion) return", refreshIndex);
    const successMessageIndex = body.indexOf("message.success({", latestVersionCheckIndex);
    assert(
      refreshIndex >= 0 && latestVersionCheckIndex > refreshIndex && successMessageIndex > latestVersionCheckIndex && body.includes("content: t('shop.cartQuantityUpdated', { quantity: normalizedQuantity })") && body.includes("key: `shop-product-quantity-${productCode}`") && zhLocaleSource.includes('"cartQuantityUpdated": "\u6570\u91CF\u5DF2\u4FDD\u5B58\uFF1A{{quantity}}"') && enLocaleSource.includes('"cartQuantityUpdated": "Quantity saved: {{quantity}}"'),
      "\u5546\u54C1\u5361\u6570\u91CF\u4FDD\u5B58\u6210\u529F\u540E\u7F3A\u5C11\u6700\u65B0\u7248\u672C\u6821\u9A8C\u540E\u7684\u6210\u529F\u63D0\u793A\u3001message key\uFF0C\u6216 zh/en \u6587\u6848\u4E0D\u540C\u6B65"
    );
  });
  if (productQuantityUpdateSuccessMessageFailure) failures.push(productQuantityUpdateSuccessMessageFailure);
  const productCardAddFailure = await runTest("\u5546\u54C1\u5361\u672A\u5165\u8F66\u5546\u54C1\u5E94\u4FDD\u7559 Add \u9996\u6B21\u52A0\u8D2D\u5E76\u4E50\u89C2\u66F4\u65B0", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleAddToCart = useCallback",
      "const handleProductQuantityChange = useCallback"
    );
    assert(
      body.includes("const addQuantity = Math.max(1, Math.floor(Number.isFinite(quantity) ? quantity : 0))") && body.includes("setOptimisticCartQuantityMap((prev) => ({ ...prev, [product.productCode]: addQuantity }))") && body.includes("const nextCart = await addStoreOrderCartItem({") && body.includes("quantity: addQuantity") && body.includes("setCart(nextCart)") && body.includes("refreshDynamicDataForProducts([product.productCode])") && body.includes("delete next[product.productCode]") && shopHomeSource.includes("onAddToCart={handleAddToCart}"),
      "\u5546\u54C1\u5361 Add \u6CA1\u6709\u8D70 cart/add\uFF0C\u6216\u7F3A\u5C11\u4E50\u89C2\u6570\u91CF\u3001\u6210\u529F\u5237\u65B0\u3001\u5931\u8D25\u56DE\u6EDA"
    );
  });
  if (productCardAddFailure) failures.push(productCardAddFailure);
  const scanAddFailure = await runTest("\u626B\u7801 Add \u5E94\u590D\u7528 cart/add \u8FD4\u56DE\u7684\u8D2D\u7269\u8F66\u5E76\u907F\u514D\u4E8C\u6B21\u62C9\u6574\u8F66", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const addScannedProductToCart = useCallback",
      "const handleBarcodeSubmit = useCallback"
    );
    assert(
      body.includes("const nextCart = await addStoreOrderCartItem({") && body.includes("setCart(nextCart)") && !body.includes("getActiveStoreOrderCart("),
      "\u626B\u7801 Add \u4ECD\u7136\u5728 addStoreOrderCartItem \u540E\u989D\u5916\u8C03\u7528 getActiveStoreOrderCart"
    );
  });
  if (scanAddFailure) failures.push(scanAddFailure);
  const dynamicDataFailure = await runTest("\u5546\u54C1\u5361 Add\u3001\u6570\u91CF\u66F4\u65B0\u548C\u626B\u7801\u52A0\u8D2D\u540E\u5E94\u53EA\u5237\u65B0\u5F53\u524D\u5546\u54C1\u52A8\u6001\u6570\u636E", () => {
    const productAddBody = extractFunctionBody(
      shopHomeSource,
      "const handleAddToCart = useCallback",
      "const handleProductQuantityChange = useCallback"
    );
    const productQuantityBody = extractFunctionBody(
      shopHomeSource,
      "const handleProductQuantityChange = useCallback",
      "const handleRemoveFromCart = async"
    );
    const scanAddBody = extractFunctionBody(
      shopHomeSource,
      "const addScannedProductToCart = useCallback",
      "const handleBarcodeSubmit = useCallback"
    );
    assert(
      shopHomeSource.includes("const refreshDynamicDataForProducts = useCallback") && productAddBody.includes("refreshDynamicDataForProducts([product.productCode])") && productQuantityBody.includes("refreshDynamicDataForProducts([productCode])") && scanAddBody.includes("refreshDynamicDataForProducts([product.productCode])") && !productAddBody.includes("refreshDynamicData()") && !productQuantityBody.includes("refreshDynamicData()") && !scanAddBody.includes("refreshDynamicData()"),
      "\u52A0\u8D2D\u540E\u4ECD\u5728\u5237\u65B0\u6574\u9875 dynamic-data \u6216\u8D2D\u7269\u8F66\uFF0C\u800C\u4E0D\u662F\u53EA\u5237\u65B0\u5F53\u524D\u5546\u54C1"
    );
  });
  if (dynamicDataFailure) failures.push(dynamicDataFailure);
  const defaultSortFailure = await runTest("\u5546\u57CE\u9996\u9875\u9ED8\u8BA4\u5546\u54C1\u67E5\u8BE2\u5E94\u547D\u4E2D\u540E\u7AEF\u9996\u9875\u7F13\u5B58\u6392\u5E8F", () => {
    const fetchProductsBody = extractFunctionBody(
      shopHomeSource,
      "const fetchProducts = async",
      "void fetchProducts()"
    );
    assert(
      fetchProductsBody.includes("sortBy: 'Default'") && !fetchProductsBody.includes("sortBy: 'productName'"),
      "\u5546\u57CE\u9996\u9875\u9ED8\u8BA4\u5546\u54C1\u67E5\u8BE2\u672A\u4F7F\u7528\u540E\u7AEF\u9996\u9875\u7F13\u5B58\u5BF9\u5E94\u7684 Default \u6392\u5E8F"
    );
  });
  if (defaultSortFailure) failures.push(defaultSortFailure);
  const storeScopeFailure = await runTest("\u5546\u57CE\u9996\u9875\u5546\u54C1\u67E5\u8BE2\u5FC5\u987B\u5E26\u5F53\u524D\u5206\u5E97\u5E76\u7B49\u5F85\u5206\u5E97\u9009\u4E2D", () => {
    const fetchProductsBody = extractFunctionBody(
      shopHomeSource,
      "const fetchProducts = async",
      "void fetchProducts()"
    );
    assert(
      fetchProductsBody.includes("if (!selectedStore?.storeCode)") && fetchProductsBody.includes("setProducts([])") && fetchProductsBody.includes("setTotal(0)") && fetchProductsBody.includes("storeCode: selectedStore.storeCode"),
      "\u5546\u57CE\u9996\u9875\u5546\u54C1\u67E5\u8BE2\u672A\u7B49\u5F85\u5F53\u524D\u5206\u5E97\uFF0C\u6216\u672A\u628A selectedStore.storeCode \u4F20\u7ED9\u540E\u7AEF"
    );
  });
  if (storeScopeFailure) failures.push(storeScopeFailure);
  const storeScopeDependencyFailure = await runTest("\u5546\u57CE\u9996\u9875\u5207\u6362\u5206\u5E97\u540E\u5E94\u91CD\u65B0\u52A0\u8F7D\u5546\u54C1\u5217\u8868", () => {
    assert(
      shopHomeSource.includes("selectedStore?.storeCode, gradeFilter, t") || shopHomeSource.includes("gradeFilter, selectedStore?.storeCode, t"),
      "\u5546\u54C1\u52A0\u8F7D effect \u4F9D\u8D56\u7F3A\u5C11 selectedStore.storeCode\uFF0C\u5207\u6362\u5206\u5E97\u540E\u4E0D\u4F1A\u91CD\u65B0\u6309\u95E8\u5E97\u52A0\u8F7D"
    );
  });
  if (storeScopeDependencyFailure) failures.push(storeScopeDependencyFailure);
  const pageSizeOptionsFailure = await runTest("\u5546\u57CE\u9996\u9875\u9ED8\u8BA4\u6BCF\u9875 200 \u4E14\u652F\u6301 50/100/200/500", () => {
    assert(
      shopHomeSource.includes("const SHOP_HOME_PAGE_SIZE_OPTIONS = [50, 100, 200, 500]") && shopHomeSource.includes("const [pageSize, setPageSize] = useState(200)") && shopHomeSource.includes("options={SHOP_HOME_PAGE_SIZE_OPTIONS.map((value) => ({ value, label: String(value) }))}") && shopHomeSource.includes("pageSizeOptions={SHOP_HOME_PAGE_SIZE_OPTIONS.map(String)}"),
      "\u5546\u57CE\u9996\u9875\u9ED8\u8BA4\u6BCF\u9875\u6570\u91CF\u6216\u9876\u90E8/\u5E95\u90E8\u5206\u9875\u9009\u9879\u672A\u7EDF\u4E00\u4E3A 50/100/200/500"
    );
  });
  if (pageSizeOptionsFailure) failures.push(pageSizeOptionsFailure);
  const cartOnlyFilterFailure = await runTest("\u5546\u57CE\u9996\u9875\u8D2D\u7269\u8F66\u5546\u54C1\u6309\u94AE\u5E94\u663E\u793A\u5F53\u524D\u5206\u5E97\u5168\u90E8\u8D2D\u7269\u8F66\u5546\u54C1", () => {
    assert(
      shopHomeSource.includes("import { ShoppingCartOutlined } from '@ant-design/icons'") && shopHomeSource.includes("const [cartOnlyFilter, setCartOnlyFilter] = useState(false)") && shopHomeSource.includes("const cart = useShopStore((state) => state.cart)") && shopHomeSource.includes("const cartProductItems = useMemo<StoreOrderProductItem[]>(() => {") && shopHomeSource.includes("return (cart?.items ?? []).map((item) => ({") && shopHomeSource.includes("const cartProductPageItems = useMemo(() => {") && shopHomeSource.includes("return cartProductItems.slice(startIndex, startIndex + pageSize)") && shopHomeSource.includes("const displayProducts = cartOnlyFilter ? cartProductPageItems : products") && shopHomeSource.includes("const displayTotal = cartOnlyFilter ? cartProductItems.length : total") && shopHomeSource.includes("const cartProductCount = (cart?.items.length || cart?.totalSKU) ?? 0") && shopHomeSource.includes("type={cartOnlyFilter ?") && shopHomeSource.includes("icon={<ShoppingCartOutlined />}") && shopHomeSource.includes("const handleCartOnlyFilterToggle = useCallback") && shopHomeSource.includes("await ensureFullCart()") && shopHomeSource.includes("t('shop.cartProductsFilter', { count: cartProductCount })") && shopHomeSource.includes("cartOnlyFilter ? t(") && shopHomeSource.includes("t('shop.noCartProductsFound')"),
      "\u5546\u57CE\u9996\u9875\u8D2D\u7269\u8F66\u5546\u54C1\u8FC7\u6EE4\u6CA1\u6709\u8BFB\u53D6 cart.items\u3001\u6CA1\u6709\u672C\u5730\u5206\u9875\uFF0C\u6216\u6CA1\u6709\u72EC\u7ACB\u6309\u94AE/\u7A7A\u72B6\u6001\u6587\u6848"
    );
  });
  if (cartOnlyFilterFailure) failures.push(cartOnlyFilterFailure);
  const cartOnlyDynamicDataFailure = await runTest("\u8D2D\u7269\u8F66\u5546\u54C1\u8FC7\u6EE4\u5E94\u4F7F\u7528\u8D2D\u7269\u8F66\u6570\u91CF\u8986\u76D6\u5546\u54C1\u5361\u52A8\u6001\u6570\u636E", () => {
    assert(
      shopHomeSource.includes("const cartProductDynamicDataMap = useMemo<Record<string, StoreOrderDynamicData>>(() => {") && shopHomeSource.includes("cartQuantity: item.quantity") && shopHomeSource.includes("const cartQuantityByProductCode = useMemo<Record<string, number>>(() => {") && shopHomeSource.includes("acc[item.productCode] = item.quantity") && shopHomeSource.includes("cartOnlyFilter") && shopHomeSource.includes("const currentCartQuantity = cart?.isSummaryOnly") && shopHomeSource.includes("const dynamicData = cartOnlyFilter") && shopHomeSource.includes("? cartProductDynamicDataMap[product.productCode]") && shopHomeSource.includes(": dynamicDataMap[product.productCode]") && shopHomeSource.includes("const syncedDynamicData: StoreOrderDynamicData = {") && shopHomeSource.includes("cart?.isSummaryOnly") && shopHomeSource.includes("dynamicData?.cartQuantity ?? 0") && shopHomeSource.includes("cartQuantityByProductCode[product.productCode] ?? dynamicData?.cartQuantity ?? 0") && shopHomeSource.includes("dynamicData={cardDynamicData}"),
      "\u5546\u54C1\u5361\u6CA1\u6709\u5728 summary-only \u65F6\u4FDD\u7559 dynamic-data.cartQuantity\uFF0C\u6216 full cart \u65F6\u6CA1\u6709\u7528\u5168\u5C40 cart.items \u8986\u76D6\u6570\u91CF"
    );
  });
  if (cartOnlyDynamicDataFailure) failures.push(cartOnlyDynamicDataFailure);
  const summaryCartFailure = await runTest("\u5546\u57CE\u9996\u9875 summary-only \u4E0D\u5E94\u963B\u65AD\u5F53\u524D\u9875\u5546\u54C1\u5361\u8D2D\u7269\u8F66\u72B6\u6001", () => {
    assert(
      shopHomeSource.includes("const ensureFullCart = useCallback") && shopHomeSource.includes("selectedStoreCodeRef.current !== storeCode") && shopHomeSource.includes("cart && !cart.isSummaryOnly && cart.storeCode === storeCode") && shopHomeSource.includes("cart?.isSummaryOnly || cart?.items.length") && shopHomeSource.includes("summary \u53EA\u670D\u52A1\u9996\u5C4F\uFF1B\u9700\u8981\u660E\u7EC6\u4EA4\u4E92\u65F6\u518D\u8865 full cart") && shopHomeSource.includes("const fullCart = await ensureFullCart()") && shopHomeSource.includes("fullCart?.items.find((item) => item.productCode === productCode)"),
      "summary-only \u4E0B\u7F3A\u5C11\u6309\u9700 full cart\u3001\u9632\u9648\u65E7\u95E8\u5E97\u4FDD\u62A4\uFF0C\u6216\u5220\u9664\u5546\u54C1\u6CA1\u6709\u5148\u8865 detailGUID \u660E\u7EC6"
    );
  });
  if (summaryCartFailure) failures.push(summaryCartFailure);
  const cartClearSyncFailure = await runTest("\u6E05\u7A7A\u8D2D\u7269\u8F66\u540E\u5E94\u6E05\u7406\u5361\u7247\u4E50\u89C2\u72B6\u6001\u548C\u672A\u63D0\u4EA4\u6570\u91CF\u66F4\u65B0", () => {
    assert(
      shopHomeSource.includes("if (cart?.isSummaryOnly || cart?.items.length) {\n      return\n    }") && shopHomeSource.includes("Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))") && shopHomeSource.includes("quantityUpdateTimersRef.current = {}") && shopHomeSource.includes("quantityUpdateVersionRef.current = {}") && shopHomeSource.includes("setOptimisticCartQuantityMap({})") && shopHomeSource.includes("setRemovingCartProductMap({})") && shopHomeSource.includes("setQuantityLoadingMap({})") && shopHomeSource.includes("[cart?.isSummaryOnly, cart?.items.length]"),
      "\u8D2D\u7269\u8F66\u6E05\u7A7A\u540E\u6CA1\u6709\u53D6\u6D88\u672A\u63D0\u4EA4\u6570\u91CF\u66F4\u65B0\uFF0C\u6216\u6CA1\u6709\u6E05\u7406\u5546\u54C1\u5361\u4E50\u89C2/\u5220\u9664\u4E2D\u72B6\u6001"
    );
  });
  if (cartClearSyncFailure) failures.push(cartClearSyncFailure);
  const cartOnlyI18nFailure = await runTest("\u8D2D\u7269\u8F66\u5546\u54C1\u8FC7\u6EE4\u6587\u6848\u5E94\u4FDD\u6301\u4E2D\u82F1\u6587\u540C\u6B65", () => {
    assert(
      zhLocaleSource.includes('"cartProductsFilter": "\u8D2D\u7269\u8F66\u5546\u54C1 ({{count}})"') && zhLocaleSource.includes('"cartProductsTitle": "\u8D2D\u7269\u8F66\u5546\u54C1"') && zhLocaleSource.includes('"noCartProductsFound": "\u8D2D\u7269\u8F66\u6682\u65E0\u5546\u54C1"') && enLocaleSource.includes('"cartProductsFilter": "Cart Products ({{count}})"') && enLocaleSource.includes('"cartProductsTitle": "Cart Products"') && enLocaleSource.includes('"noCartProductsFound": "No products in cart"'),
      "\u8D2D\u7269\u8F66\u5546\u54C1\u8FC7\u6EE4\u7F3A\u5C11 zh/en \u540C\u6B65\u6587\u6848"
    );
  });
  if (cartOnlyI18nFailure) failures.push(cartOnlyI18nFailure);
  const searchCategoryPathFailure = await runTest("\u641C\u7D22\u5546\u54C1\u5361\u7247\u5E94\u663E\u793A\u5206\u7C7B\u5B8C\u6574\u8DEF\u5F84", () => {
    assert(
      shopHomeSource.includes("buildWarehouseCategoryLookup") && shopHomeSource.includes("getWarehouseProductCategoryTooltip") && shopHomeSource.includes("const shouldShowCategoryPath = Boolean(keyword)") && shopHomeSource.includes("categoryPath={categoryPathMap[product.productCode]}"),
      "\u641C\u7D22\u7ED3\u679C\u5546\u54C1\u5361\u7247\u672A\u590D\u7528\u5206\u7C7B\u6811\u8DEF\u5F84\u5DE5\u5177\uFF0C\u6216\u672A\u628A\u5206\u7C7B\u5B8C\u6574\u8DEF\u5F84\u4F20\u7ED9 ProductCard"
    );
  });
  if (searchCategoryPathFailure) failures.push(searchCategoryPathFailure);
  const searchOnlyCategoryPathFailure = await runTest("\u5206\u7C7B\u8DEF\u5F84\u53EA\u5E94\u663E\u793A\u5728\u641C\u7D22\u7ED3\u679C\u5361\u7247", () => {
    assert(
      shopHomeSource.includes("if (!shouldShowCategoryPath || !categoryLookup)") && shopHomeSource.includes("return {}"),
      "\u5206\u7C7B\u8DEF\u5F84\u7F3A\u5C11 keyword \u5F00\u5173\uFF0C\u5206\u7C7B\u9875\u6216\u5168\u90E8\u5546\u54C1\u9875\u53EF\u80FD\u4E5F\u4F1A\u663E\u793A\u8DEF\u5F84"
    );
  });
  if (searchOnlyCategoryPathFailure) failures.push(searchOnlyCategoryPathFailure);
  const productCardCategoryPathFailure = await runTest("\u5546\u54C1\u5361\u7247\u5E94\u5728\u8D27\u53F7\u4E0B\u65B9\u4EE5\u4E24\u884C\u7701\u7565\u663E\u793A\u5206\u7C7B\u8DEF\u5F84", () => {
    assert(
      productCardSource.includes("categoryPath?: string") && productCardSource.includes("Tooltip") && productCardSource.includes("shop-product-category-path") && productCardSource.includes("ellipsis={{ rows: 2 }}") && globalCssSource.includes(".shop-product-category-path"),
      "ProductCard \u672A\u58F0\u660E categoryPath\uFF0C\u6216\u5206\u7C7B\u8DEF\u5F84\u6CA1\u6709 Tooltip/\u4E24\u884C\u7701\u7565/\u7A33\u5B9A\u6837\u5F0F"
    );
  });
  if (productCardCategoryPathFailure) failures.push(productCardCategoryPathFailure);
  const productCardQuantityStepperFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u5E94\u9ED8\u8BA4 0 \u5E76\u4F7F\u7528 INNER \u6B65\u8FDB\u76F4\u63A5\u66F4\u65B0\u8D2D\u7269\u8F66", () => {
    assert(
      productCardSource.includes("onQuantityChange: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void") && productCardSource.includes("onAddToCart: (product: StoreOrderProductItem, quantity: number) => Promise<void> | void") && productCardSource.includes("ShoppingCartOutlined") && productCardSource.includes("const stepQuantity = product.minOrderQuantity > 0 ? product.minOrderQuantity : 1") && productCardSource.includes("const cartQuantity = dynamicData?.cartQuantity ?? 0") && productCardSource.includes("const [quantity, setQuantity] = useState<number>(0)") && productCardSource.includes("setQuantity(cartQuantity)") && productCardSource.includes("applyQuantityChange(quantity - stepQuantity)") && productCardSource.includes("applyQuantityChange(quantity + stepQuantity)") && productCardSource.includes("min={0}") && productCardSource.includes("controls={false}") && productCardSource.includes("disabled={removing || quantity <= 0}") && productCardSource.includes("disabled={removing}") && !productCardSource.includes("disabled={loading || quantity <= 0}") && !productCardSource.includes('disabled={loading}\n                className="shop-product-quantity-input"') && productCardSource.includes("onBlur={() => applyQuantityChange(quantity)}") && productCardSource.includes("onPressEnter={() => applyQuantityChange(quantity)}"),
      "\u5546\u54C1\u5361\u6570\u91CF\u63A7\u4EF6\u6CA1\u6709\u9ED8\u8BA4 0\u3001\u6CA1\u6709\u6309 INNER \u6B65\u8FDB\u3001\u6CA1\u6709\u4FDD\u7559 Add\uFF0C\u6216\u6CA1\u6709\u76F4\u63A5\u63D0\u4EA4\u6570\u91CF\u53D8\u5316"
    );
  });
  if (productCardQuantityStepperFailure) failures.push(productCardQuantityStepperFailure);
  const productCardAddVisibilityFailure = await runTest("\u5546\u54C1\u5361\u5E94\u672A\u5165\u8F66\u663E\u793A Add\u3001\u5DF2\u5165\u8F66\u663E\u793A\u5220\u9664\u4E14\u4E0D\u6324\u538B\u6B65\u8FDB\u5668", () => {
    assert(
      productCardSource.includes("cartQuantity > 0 ? (") && productCardSource.includes("cartQuantity <= 0 ? (") && productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--left"') && productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--right"') && productCardSource.includes("cartQuantity > 0 ? 'shop-product-card-actions--in-cart' : ''") && productCardSource.includes("const addQuantity = quantity > 0 ? quantity : stepQuantity") && productCardSource.includes("void onAddToCart(product, addQuantity)") && productCardSource.includes('className="shop-product-add-button"'),
      "\u5546\u54C1\u5361\u6CA1\u6709\u6309\u8D2D\u7269\u8F66\u72B6\u6001\u5207\u6362\u5220\u9664/Add\uFF0C\u6216 Add \u6CA1\u6709\u6309\u5F53\u524D\u6570\u91CF/INNER \u9996\u6B21\u52A0\u8D2D"
    );
  });
  if (productCardAddVisibilityFailure) failures.push(productCardAddVisibilityFailure);
  const productCardRemoveOptimisticFailure = await runTest("\u5546\u54C1\u5361\u5220\u9664\u5E94\u5148\u4E50\u89C2\u9000\u51FA\u5DF2\u5165\u8F66\u72B6\u6001\u5E76\u9632\u91CD\u590D\u70B9\u51FB", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleRemoveFromCart = async",
      '  return (\n    <div className="shop-home-page">'
    );
    assert(
      productCardSource.includes("removing?: boolean") && productCardSource.includes("removing = false") && productCardSource.includes("if (removing)") && shopHomeSource.includes("const [removingCartProductMap, setRemovingCartProductMap] = useState<Record<string, boolean>>({})") && body.includes("if (removingCartProductMap[productCode])") && body.includes("setRemovingCartProductMap((prev) => ({ ...prev, [productCode]: true }))") && body.includes("setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: 0 }))") && body.includes("delete next[productCode]") && shopHomeSource.includes("const isRemovingFromCart = Boolean(removingCartProductMap[product.productCode])") && shopHomeSource.includes("const optimisticCartQuantity = isRemovingFromCart") && shopHomeSource.includes("? 0") && shopHomeSource.includes("removing={isRemovingFromCart}"),
      "\u5546\u54C1\u5361\u5220\u9664\u6CA1\u6709\u5148\u4E50\u89C2\u7F6E 0\u3001\u7F3A\u5C11\u5220\u9664\u4E2D\u53BB\u91CD guard\uFF0C\u6216 ProductCard \u672A\u6536\u5230 removing \u72B6\u6001"
    );
  });
  if (productCardRemoveOptimisticFailure) failures.push(productCardRemoveOptimisticFailure);
  const optimisticDynamicDataFailure = await runTest("\u5546\u57CE\u9996\u9875\u5E94\u628A\u4E50\u89C2\u8D2D\u7269\u8F66\u6570\u91CF\u8986\u76D6\u5230\u5546\u54C1\u5361 dynamicData", () => {
    assert(
      shopHomeSource.includes("const [optimisticCartQuantityMap, setOptimisticCartQuantityMap] = useState<Record<string, number>>({})") && shopHomeSource.includes("const optimisticCartQuantity = isRemovingFromCart") && shopHomeSource.includes(": optimisticCartQuantityMap[product.productCode]") && shopHomeSource.includes("const syncedDynamicData: StoreOrderDynamicData =") && shopHomeSource.includes("const cardDynamicData =") && shopHomeSource.includes("? syncedDynamicData") && shopHomeSource.includes("...syncedDynamicData") && shopHomeSource.includes("cartQuantity: optimisticCartQuantity") && shopHomeSource.includes("dynamicData={cardDynamicData}"),
      "\u5546\u57CE\u9996\u9875\u6CA1\u6709\u628A optimisticCartQuantityMap \u8986\u76D6\u5230 ProductCard dynamicData"
    );
  });
  if (optimisticDynamicDataFailure) failures.push(optimisticDynamicDataFailure);
  const productCardQuantityStyleFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u6B65\u8FDB\u5668\u5E94\u56FA\u5B9A\u5C45\u4E2D\u4E14 Add \u56FA\u5B9A\u5BBD\u5EA6", () => {
    assert(
      productCardSource.includes("shop-product-quantity-stepper") && productCardSource.includes("shop-product-quantity-button") && productCardSource.includes("shop-product-quantity-input") && globalCssSource.includes("grid-template-columns: 28px minmax(108px, 1fr) 58px") && globalCssSource.includes(".shop-product-card-actions--in-cart") && globalCssSource.includes("grid-template-columns: 28px minmax(108px, 1fr) 28px") && globalCssSource.includes("box-sizing: border-box") && globalCssSource.includes("justify-self: center") && globalCssSource.includes("max-width: 120px") && globalCssSource.includes("min-width: 108px") && globalCssSource.includes(".shop-product-quantity-stepper") && globalCssSource.includes(".shop-product-quantity-button") && globalCssSource.includes(".shop-product-quantity-input") && globalCssSource.includes(".shop-product-add-button"),
      "\u5546\u54C1\u5361\u6570\u91CF\u6B65\u8FDB\u5668\u7F3A\u5C11\u56FA\u5B9A\u5C45\u4E2D/Add \u56FA\u5B9A\u5BBD\u5EA6\u6837\u5F0F\uFF0C\u5361\u7247\u52A8\u4F5C\u533A\u53EF\u80FD\u6296\u52A8"
    );
  });
  if (productCardQuantityStyleFailure) failures.push(productCardQuantityStyleFailure);
  const categoryPathClickFailure = await runTest("\u641C\u7D22\u5546\u54C1\u5206\u7C7B\u8DEF\u5F84\u70B9\u51FB\u540E\u5E94\u8FDB\u5165\u5BF9\u5E94\u5206\u7C7B\u5E76\u6E05\u9664\u641C\u7D22\u8BCD", () => {
    assert(
      shopHomeSource.includes("useNavigate") && shopHomeSource.includes("const navigate = useNavigate()") && shopHomeSource.includes("const handleCategoryPathClick = useCallback") && shopHomeSource.includes("product.warehouseCategoryGUID") && shopHomeSource.includes("navigate(`/shop?category=${encodeURIComponent(product.warehouseCategoryGUID)}`)") && shopHomeSource.includes("shouldShowCategoryPath && product.warehouseCategoryGUID") && shopHomeSource.includes("? handleCategoryPathClick") && shopHomeSource.includes(": undefined"),
      "\u641C\u7D22\u5546\u54C1\u5206\u7C7B\u8DEF\u5F84\u6CA1\u6709\u70B9\u51FB\u8FDB\u5165\u5206\u7C7B\u3001\u8DF3\u8F6C\u6CA1\u6709\u6E05\u9664 keyword\uFF0C\u6216\u7F3A\u5C11\u5206\u7C7B GUID \u65F6\u4ECD\u4F1A\u663E\u793A\u53EF\u70B9\u51FB\u72B6\u6001"
    );
  });
  if (categoryPathClickFailure) failures.push(categoryPathClickFailure);
  const productCardCategoryPathA11yFailure = await runTest("\u53EF\u70B9\u51FB\u5206\u7C7B\u8DEF\u5F84\u5E94\u652F\u6301\u9F20\u6807\u548C\u952E\u76D8\u89E6\u53D1", () => {
    assert(
      productCardSource.includes("onCategoryPathClick?: (product: StoreOrderProductItem) => void") && productCardSource.includes("const canClickCategoryPath = Boolean(categoryPath && onCategoryPathClick)") && productCardSource.includes("role={canClickCategoryPath ? 'button' : undefined}") && productCardSource.includes("tabIndex={canClickCategoryPath ? 0 : undefined}") && productCardSource.includes("event.key === 'Enter' || event.key === ' '") && productCardSource.includes("onCategoryPathClick(product)") && globalCssSource.includes(".shop-product-category-path--clickable"),
      "ProductCard \u5206\u7C7B\u8DEF\u5F84\u7F3A\u5C11\u53EF\u70B9\u51FB prop\u3001\u952E\u76D8\u89E6\u53D1\u6216\u53EF\u70B9\u51FB\u6837\u5F0F"
    );
  });
  if (productCardCategoryPathA11yFailure) failures.push(productCardCategoryPathA11yFailure);
  const lazyImageFailure = await runTest("\u5546\u54C1\u5361\u56FE\u7247\u5E94\u61D2\u52A0\u8F7D\u907F\u514D\u62D6\u6162\u9996\u5C4F", () => {
    assert(
      productCardSource.includes('loading="lazy"'),
      '\u5546\u54C1\u5361\u56FE\u7247\u672A\u8BBE\u7F6E loading="lazy"\uFF0C\u9996\u5C4F\u5916\u56FE\u7247\u4F1A\u62A2\u5360\u9996\u9875\u52A0\u8F7D\u8D44\u6E90'
    );
  });
  if (lazyImageFailure) failures.push(lazyImageFailure);
  const bestSellerLazyImageFailure = await runTest("\u70ED\u9500\u5546\u54C1\u8868\u683C\u56FE\u7247\u5E94\u61D2\u52A0\u8F7D\u907F\u514D\u62D6\u6162\u9996\u5C4F", () => {
    assert(
      bestSellersSource.includes('loading="lazy"') && bestSellersSource.includes('decoding="async"') && bestSellersSource.includes('className="shop-best-sellers-image"') && bestSellersSource.includes("shop-best-sellers-image-placeholder"),
      "\u70ED\u9500\u5546\u54C1\u8868\u683C\u56FE\u7247\u7F3A\u5C11 lazy loading\u3001\u5F02\u6B65\u89E3\u7801\u6216\u7A33\u5B9A\u5360\u4F4D\uFF0C\u9996\u5C4F\u5916\u56FE\u7247\u4ECD\u53EF\u80FD\u62A2\u5360\u52A0\u8F7D\u8D44\u6E90"
    );
  });
  if (bestSellerLazyImageFailure) failures.push(bestSellerLazyImageFailure);
  const bestSellerVirtualFailure = await runTest("\u70ED\u9500\u5546\u54C1\u5217\u8868\u5E94\u4F7F\u7528 AntD Table \u865A\u62DF\u6EDA\u52A8", () => {
    assert(
      bestSellersSource.includes("import type { ColumnsType }") && bestSellersSource.includes("Table") && bestSellersSource.includes("virtual") && bestSellersSource.includes("scroll={{ x: 1080, y: 560 }}") && bestSellersSource.includes('className="shop-best-sellers-table"') && bestSellersSource.includes("rowKey={(record) => record.productCode || record.itemNumber || String(record.rank)}") && !bestSellersSource.includes("virtualWindow.visibleProducts.map") && !bestSellersSource.includes("ResizeObserver") && !bestSellersSource.includes("Badge.Ribbon"),
      "\u70ED\u9500\u5546\u54C1\u5217\u8868\u672A\u5207\u6362\u5230 AntD Table virtual\uFF0C\u6216\u4ECD\u4FDD\u7559\u624B\u5199 Card \u865A\u62DF\u5217\u8868"
    );
  });
  if (bestSellerVirtualFailure) failures.push(bestSellerVirtualFailure);
  const bestSellerAbortFailure = await runTest("\u70ED\u9500\u5546\u54C1\u5207\u6362\u7B5B\u9009\u5206\u9875\u65F6\u5E94\u53D6\u6D88\u65E7\u8BF7\u6C42", () => {
    assert(
      bestSellersSource.includes("const controller = new AbortController()") && bestSellersSource.includes("controller.signal") && bestSellersSource.includes("controller.abort()") && bestSellersSource.includes("fetchError instanceof DOMException && fetchError.name === 'AbortError'"),
      "\u70ED\u9500\u5546\u54C1\u8BF7\u6C42\u7F3A\u5C11 AbortController\uFF0C\u6162\u8BF7\u6C42\u4ECD\u53EF\u80FD\u8986\u76D6\u6700\u65B0\u7B5B\u9009\u7ED3\u679C"
    );
  });
  if (bestSellerAbortFailure) failures.push(bestSellerAbortFailure);
  const bestSellerDateRangeFailure = await runTest("\u70ED\u9500\u5546\u54C1\u9ED8\u8BA4\u65E5\u671F\u8303\u56F4\u5E94\u4ECE\u6628\u5929\u5F00\u59CB\u4E14\u4F7F\u7528\u672C\u5730\u4E1A\u52A1\u65E5\u671F", () => {
    assert(
      bestSellersSource.includes("import { buildBestSellerDateRange } from '../../../utils/bestSellerDateRange'") && bestSellersSource.includes("buildBestSellerDateRange(timeRange)") && !bestSellersSource.includes("toISOString().slice(0, 10)") && !bestSellersSource.includes("start.setDate(start.getDate() - timeRange)"),
      "\u70ED\u9500\u5546\u54C1\u65E5\u671F\u8303\u56F4\u672A\u590D\u7528\u672C\u5730\u4E1A\u52A1\u65E5\u671F\u5DE5\u5177\uFF0C\u6216\u4ECD\u7528 toISOString/\u4ECA\u5929\u4F5C\u4E3A\u7ED3\u675F\u65E5\u671F"
    );
  });
  if (bestSellerDateRangeFailure) failures.push(bestSellerDateRangeFailure);
  const bestSellerStatusNoticeFailure = await runTest("\u70ED\u9500\u5546\u54C1\u975E Fresh \u72B6\u6001\u5E94\u63D0\u793A\u7EDF\u8BA1\u672A\u5C31\u7EEA", () => {
    assert(
      bestSellersSource.includes("const isBestSellerStatisticFresh = statisticStatus ===") && bestSellersSource.includes("bestSellerStatusNotice") && bestSellersSource.includes("\u5546\u54C1\u7EDF\u8BA1\u672A\u5C31\u7EEA\uFF0C\u8BF7\u5148\u751F\u6210\u5546\u54C1\u7EDF\u8BA1\u3002") && bestSellersSource.includes("getBestSellerEmptyText()") && bestSellersSource.includes("\u7EDF\u8BA1\u672A\u5C31\u7EEA\uFF0C\u6682\u672A\u8FD4\u56DE\u70ED\u9500\u5546\u54C1\u3002"),
      "\u70ED\u9500\u5546\u54C1\u7F3A\u5C11\u975E Fresh \u72B6\u6001\u63D0\u793A\uFF0C\u6216\u7A7A\u6570\u636E\u6587\u6848\u6CA1\u6709\u533A\u5206\u7EDF\u8BA1\u672A\u5C31\u7EEA\u548C\u771F\u65E0\u6570\u636E"
    );
  });
  if (bestSellerStatusNoticeFailure) failures.push(bestSellerStatusNoticeFailure);
  const bestSellerTableColumnsFailure = await runTest("\u70ED\u9500\u5546\u54C1\u8868\u683C\u5E94\u663E\u793A\u6761\u7801\u3001\u590D\u5236\u3001\u72B6\u6001\u3001\u5206\u5E97\u9500\u91CF\u548C\u52A0\u8D2D\u64CD\u4F5C", () => {
    assert(
      bestSellersSource.includes("BarcodePreview") && bestSellersSource.includes("CopyOutlined") && bestSellersSource.includes("ShoppingCartOutlined") && bestSellersSource.includes("Popover") && bestSellersSource.includes("title: 'Gross Profit'") && bestSellersSource.includes("title: 'Gross Margin'") && bestSellersSource.includes("title: 'Stats'") && bestSellersSource.includes("addStoreOrderCartItem") && bestSellersSource.includes("setCart(nextCart)") && !bestSellersSource.includes("title: 'Product Code'"),
      "\u70ED\u9500\u5546\u54C1\u8868\u683C\u5217\u672A\u5305\u542B\u6761\u7801\u3001\u8D27\u53F7\u590D\u5236\u3001\u5206\u5E97\u9500\u91CF\u5F39\u5C42\u6216\u52A0\u8D2D\u64CD\u4F5C\uFF0C\u6216\u4ECD\u663E\u793A Product Code \u5217"
    );
  });
  if (bestSellerTableColumnsFailure) failures.push(bestSellerTableColumnsFailure);
  const bestSellerStatsAlwaysVisibleFailure = await runTest("\u70ED\u9500\u5546\u54C1 Stats \u5217\u5E94\u4FDD\u7559\u7ED9\u6240\u6709\u8BA2\u8D27\u524D\u53F0\u7528\u6237", () => {
    assert(
      bestSellersSource.includes("title: 'Stats'") && !bestSellersSource.includes("import { useAuthStore } from '../../../store/auth'") && !bestSellersSource.includes("const isAdmin = useAuthStore") && !bestSellersSource.includes("...(isAdmin ? ["),
      "\u70ED\u9500\u5546\u54C1 Stats \u5217\u4E0D\u5E94\u6309\u7BA1\u7406\u5458\u6761\u4EF6\u9690\u85CF\uFF0C\u666E\u901A\u8BA2\u8D27\u524D\u53F0\u7528\u6237\u4E5F\u8981\u4FDD\u7559\u5B8C\u6574\u5217"
    );
  });
  if (bestSellerStatsAlwaysVisibleFailure) failures.push(bestSellerStatsAlwaysVisibleFailure);
  const bestSellerBranchSalesFailure = await runTest("\u70ED\u9500\u5546\u54C1\u5206\u5E97\u9500\u91CF\u660E\u7EC6\u5E94\u6309\u9500\u91CF\u5012\u5E8F\u5C55\u793A", () => {
    assert(
      bestSellersSource.includes("function getBranchSalesRows(product: BestSellerProduct)") && bestSellersSource.includes("function getBranchSalesCount(product: BestSellerProduct)") && bestSellersSource.includes("return product.branchSalesCount ?? product.branchSales?.length ?? 0") && bestSellersSource.includes("].sort((a, b) => (b.quantity ?? 0) - (a.quantity ?? 0))") && bestSellersSource.includes("defaultSortOrder: 'descend'") && bestSellersSource.includes("dataIndex: 'salesAmount'") && bestSellersSource.includes("dataIndex: 'grossProfit'") && bestSellersSource.includes("dataIndex: 'grossMarginRate'") && bestSellersSource.includes("const count = getBranchSalesCount(record)") && bestSellersSource.includes("shop-best-sellers-branch-sales-popover"),
      "\u5206\u5E97\u9500\u91CF\u660E\u7EC6\u7F3A\u5C11\u9ED8\u8BA4\u9500\u91CF\u5012\u5E8F\u6392\u5E8F\u3001branchSalesCount \u4F18\u5148\u7EA7\u6216\u7D27\u51D1\u5F39\u5C42"
    );
  });
  if (bestSellerBranchSalesFailure) failures.push(bestSellerBranchSalesFailure);
  const bestSellerAddGuardFailure = await runTest("\u70ED\u9500\u5546\u54C1\u52A0\u8D2D\u53EA\u5141\u8BB8\u4E0A\u67B6\u4E14\u5DF2\u9009\u5206\u5E97\u7684\u5546\u54C1", () => {
    assert(
      bestSellersSource.includes("record.isActive !== true || !selectedStore?.storeCode") && bestSellersSource.includes("disabled={disabled}") && bestSellersSource.includes("getAddQuantity(product)") && bestSellersSource.includes("message.warning(t(") && bestSellersSource.includes("setCart(nextCart)"),
      "\u70ED\u9500\u5546\u54C1\u52A0\u8D2D\u672A\u9650\u5236\u4E0A\u4E0B\u67B6\u72B6\u6001\u548C\u5206\u5E97\uFF0C\u6216\u672A\u590D\u7528 cart/add \u8FD4\u56DE\u7ED3\u679C"
    );
  });
  if (bestSellerAddGuardFailure) failures.push(bestSellerAddGuardFailure);
  const bestSellerTableLayoutFailure = await runTest("\u70ED\u9500\u5546\u54C1\u8868\u683C\u5E94\u4FDD\u7559\u5168\u5217\u5E76\u4F7F\u7528\u7D27\u51D1\u5217\u5BBD", () => {
    assert(
      bestSellersSource.includes("title: 'Rank'") && bestSellersSource.includes("title: 'Image'") && bestSellersSource.includes("title: 'Barcode'") && bestSellersSource.includes("title: 'Item No.'") && bestSellersSource.includes("title: 'Product Name'") && bestSellersSource.includes("title: 'Units Sold'") && bestSellersSource.includes("title: 'Sales Amount'") && bestSellersSource.includes("title: 'Gross Profit'") && bestSellersSource.includes("title: 'Gross Margin'") && bestSellersSource.includes("title: 'Stats'") && bestSellersSource.includes("title: 'Status'") && bestSellersSource.includes("title: 'Stores Sold'") && bestSellersSource.includes("title: 'Action'") && bestSellersSource.includes("scroll={{ x: 1080, y: 560 }}") && bestSellersSource.includes("width: 44") && bestSellersSource.includes("width: 50") && bestSellersSource.includes("width: 104") && bestSellersSource.includes("width: 155") && globalCssSource.includes(".shop-best-sellers-table") && globalCssSource.includes(".shop-best-sellers-image-cell") && globalCssSource.includes("width: 42px") && globalCssSource.includes("height: 42px") && globalCssSource.includes(".shop-best-sellers-barcode-cell") && globalCssSource.includes("max-width: 98px") && globalCssSource.includes(".shop-best-sellers-item-number") && globalCssSource.includes(".shop-best-sellers-store-count") && globalCssSource.includes("width: 560px") && globalCssSource.includes(".shop-best-sellers-product-name") && globalCssSource.includes("max-height: calc(1.3em * 2)") && globalCssSource.includes("-webkit-line-clamp: 2") && globalCssSource.includes(".shop-best-sellers-rank") && globalCssSource.includes(".shop-best-sellers-table .ant-table-cell") && globalCssSource.includes("padding: 7px 6px !important") && !globalCssSource.includes(".shop-best-sellers-virtual-list") && !globalCssSource.includes(".shop-best-seller-card"),
      "\u70ED\u9500\u5546\u54C1\u8868\u683C\u672A\u4FDD\u7559\u5168\u5217\u3001\u7D27\u51D1\u5217\u5BBD\u548C\u56FA\u5B9A\u56FE\u7247\u6761\u7801\u5C3A\u5BF8\uFF0C\u6216\u4ECD\u4FDD\u7559\u65E7\u5361\u7247\u6837\u5F0F"
    );
  });
  if (bestSellerTableLayoutFailure) failures.push(bestSellerTableLayoutFailure);
  const perfLogGateFailure = await runTest("\u9996\u9875\u6027\u80FD console \u65E5\u5FD7\u751F\u4EA7\u73AF\u5883\u9ED8\u8BA4\u5173\u95ED", () => {
    const helperBody = extractFunctionBody(
      shopHomeSource,
      "function logShopHomePerf",
      "export default function ShopHomePage"
    );
    assert(
      helperBody.includes("import.meta.env.DEV") && helperBody.includes("window.localStorage.getItem('shopHomePerf') === '1'") && helperBody.includes("if (!isDebugEnabled)"),
      "\u9996\u9875\u6027\u80FD console \u65E5\u5FD7\u7F3A\u5C11\u5F00\u53D1\u73AF\u5883\u6216\u663E\u5F0F\u5F00\u5173\u4FDD\u62A4"
    );
  });
  if (perfLogGateFailure) failures.push(perfLogGateFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("shopHomeCartPerformance.logic.test: ok");
}
await main();
