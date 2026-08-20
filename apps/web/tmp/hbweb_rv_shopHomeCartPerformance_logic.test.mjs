// src/pages/ShopHome/shopHomeCartPerformance.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/ShopHome/shopHomeCartDynamicData.ts
function buildShopHomeDynamicDataRequestIdentity({
  active,
  storeCode,
  productCodes
}) {
  if (!active || !storeCode || productCodes.length === 0) {
    return null;
  }
  return JSON.stringify([storeCode, ...productCodes]);
}
function readShopHomeDynamicDataRequestProductCodes(identity) {
  if (!identity) {
    return [];
  }
  const parsedIdentity = JSON.parse(identity);
  if (!Array.isArray(parsedIdentity)) {
    return [];
  }
  return parsedIdentity.slice(1).filter((code) => typeof code === "string");
}
function createShopHomeDynamicDataStoreScopeCoordinator() {
  let activeStoreCode = null;
  let generation = 0;
  return {
    activate(storeCode) {
      if (activeStoreCode === storeCode) {
        return;
      }
      activeStoreCode = storeCode;
      generation += 1;
    },
    deactivate(storeCode) {
      if (activeStoreCode !== storeCode) {
        return;
      }
      activeStoreCode = null;
      generation += 1;
    },
    capture(storeCode) {
      if (!storeCode || activeStoreCode !== storeCode) {
        return null;
      }
      return { storeCode, generation };
    },
    isCurrent(token) {
      return activeStoreCode === token.storeCode && generation === token.generation;
    }
  };
}
function createShopHomeDynamicDataRequestCoordinator() {
  let activeIdentity = null;
  let activeToken = null;
  let version = 0;
  const isCurrent = (token) => activeIdentity === token.identity && activeToken?.identity === token.identity && activeToken.version === token.version;
  return {
    activate(identity) {
      if (activeIdentity === identity) {
        return;
      }
      activeIdentity = identity;
      activeToken = null;
      version += 1;
    },
    deactivate(identity) {
      if (activeIdentity !== identity) {
        return;
      }
      activeIdentity = null;
      activeToken = null;
      version += 1;
    },
    begin(identity) {
      if (!identity || activeIdentity !== identity) {
        return null;
      }
      version += 1;
      const token = { identity, version };
      activeToken = token;
      return token;
    },
    invalidate(token) {
      if (!token || !isCurrent(token)) {
        return;
      }
      activeToken = null;
      version += 1;
    },
    isCurrent
  };
}
function createShopHomeSalesSummaryRequestCoordinator() {
  let activeIdentity = null;
  let activeToken = null;
  let version = 0;
  const isCurrent = (token) => activeIdentity === token.identity && activeToken?.identity === token.identity && activeToken.version === token.version;
  return {
    activate(identity) {
      if (activeIdentity === identity) {
        return;
      }
      activeIdentity = identity;
      activeToken = null;
      version += 1;
    },
    deactivate(identity) {
      if (activeIdentity !== identity) {
        return;
      }
      activeIdentity = null;
      activeToken = null;
      version += 1;
    },
    begin(identity) {
      if (!identity || activeIdentity !== identity) {
        return null;
      }
      version += 1;
      const token = { identity, version };
      activeToken = token;
      return token;
    },
    isCurrent
  };
}
async function runShopHomeDynamicDataRequest({
  coordinator,
  token,
  productCodes,
  request,
  onSuccess,
  onError
}) {
  if (!token || productCodes.length === 0) {
    return;
  }
  try {
    const result = await request(productCodes);
    if (coordinator.isCurrent(token)) {
      onSuccess(result);
    }
  } catch (error) {
    if (coordinator.isCurrent(token)) {
      onError(error);
    }
  }
}
async function runShopHomeSalesSummaryRequest({
  coordinator,
  token,
  productCodes,
  request,
  onSuccess,
  onError
}) {
  if (!token || productCodes.length === 0) {
    return;
  }
  try {
    const result = await request(productCodes);
    if (coordinator.isCurrent(token)) {
      onSuccess(result);
    }
  } catch (error) {
    if (coordinator.isCurrent(token)) {
      onError(error);
    }
  }
}
async function runShopHomeStoreScopedDynamicDataRequest({
  coordinator,
  token,
  productCodes,
  request,
  onSuccess
}) {
  if (!token || productCodes.length === 0) {
    return;
  }
  const result = await request(productCodes);
  if (coordinator.isCurrent(token)) {
    onSuccess(result);
  }
}
function mergeShopHomeBaseDynamicDataMap(previousMap, nextBaseMap) {
  const nextMap = { ...previousMap };
  Object.entries(nextBaseMap).forEach(([productCode, nextBaseData]) => {
    const previousData = previousMap[productCode];
    nextMap[productCode] = previousData?.salesQuantitySinceLastArrival === void 0 ? nextBaseData : {
      ...nextBaseData,
      salesQuantitySinceLastArrival: previousData.salesQuantitySinceLastArrival
    };
  });
  return nextMap;
}
function mergeShopHomeCartDynamicData({
  dynamicData,
  productCode,
  cartQuantity
}) {
  const nextCartQuantity = cartQuantity ?? dynamicData?.cartQuantity ?? 0;
  if (dynamicData && dynamicData.productCode === productCode && dynamicData.cartQuantity === nextCartQuantity) {
    return dynamicData;
  }
  return {
    ...dynamicData ?? { productCode, cartQuantity: 0 },
    productCode: dynamicData?.productCode ?? productCode,
    // full cart 的明细数量是当前真值；summary-only 则继续沿用动态接口数量。
    cartQuantity: nextCartQuantity
  };
}

// src/pages/ShopHome/shopHomeCartPerformance.logic.test.ts
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
function createDeferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
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
  const localBaseRefreshPreservesSalesFailure = await runTest(
    "\u5355\u5546\u54C1\u57FA\u7840\u52A8\u6001\u5237\u65B0\u5E94\u4FDD\u7559\u5DF2\u7ECF\u52A0\u8F7D\u7684 Sales",
    () => {
      const previousMap = {
        P1: {
          productCode: "P1",
          cartQuantity: 1,
          salesQuantitySinceLastArrival: -2
        },
        P2: {
          productCode: "P2",
          cartQuantity: 2,
          salesQuantitySinceLastArrival: null
        }
      };
      const nextMap = mergeShopHomeBaseDynamicDataMap(previousMap, {
        P1: { productCode: "P1", cartQuantity: 3 },
        P2: { productCode: "P2", cartQuantity: 4 }
      });
      assert(nextMap.P1.cartQuantity === 3, "\u57FA\u7840\u52A8\u6001\u5B57\u6BB5\u5FC5\u987B\u4F7F\u7528\u6700\u65B0\u503C");
      assert(nextMap.P1.salesQuantitySinceLastArrival === -2, "\u8D1F Sales \u5FC5\u987B\u5728\u5C40\u90E8\u5237\u65B0\u540E\u4FDD\u7559");
      assert(nextMap.P2.salesQuantitySinceLastArrival === null, "\u4E0D\u53EF\u7528 Sales \u7684 null \u8BED\u4E49\u5FC5\u987B\u4FDD\u7559");
      assert(
        shopHomeSource.includes("mergeShopHomeBaseDynamicDataMap(prev, nextMap)"),
        "\u4E3B\u5546\u54C1\u52A8\u6001 map \u7684\u5C40\u90E8\u5237\u65B0\u5FC5\u987B\u590D\u7528\u4FDD\u7559 Sales \u7684\u5B57\u6BB5\u7EA7\u5408\u5E76"
      );
    }
  );
  if (localBaseRefreshPreservesSalesFailure) failures.push(localBaseRefreshPreservesSalesFailure);
  const productQuantityUpdateFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u6309\u94AE\u5E94\u76F4\u63A5\u8BBE\u7F6E\u8D2D\u7269\u8F66\u6570\u91CF\u5E76\u590D\u7528\u8FD4\u56DE\u8D2D\u7269\u8F66", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleProductQuantityChange = useCallback",
      "const handleRemoveFromCart = useCallback"
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
      "const handleRemoveFromCart = useCallback"
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
      "const handleRemoveFromCart = useCallback"
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
  const cartOnlyDynamicDataFailure = await runTest("\u8D2D\u7269\u8F66\u5546\u54C1\u8FC7\u6EE4\u5E94\u4FDD\u7559\u5B8C\u6574\u52A8\u6001\u6570\u636E\u5E76\u4EE5\u771F\u5B9E\u8D2D\u7269\u8F66\u6570\u91CF\u8986\u76D6", async () => {
    const salesValues = [12, 0, -3, null, void 0];
    for (const salesQuantitySinceLastArrival of salesValues) {
      const merged = mergeShopHomeCartDynamicData({
        dynamicData: {
          productCode: "P-CART",
          cartQuantity: 999,
          lastOrderDate: "2026-08-01",
          salesQuantitySinceLastArrival
        },
        productCode: "P-CART",
        cartQuantity: 7
      });
      assert(merged.cartQuantity === 7, "\u8D2D\u7269\u8F66\u771F\u5B9E\u6570\u91CF\u5FC5\u987B\u8986\u76D6 dynamic-data \u4E2D\u7684\u65E7\u6570\u91CF");
      assert(merged.lastOrderDate === "2026-08-01", "\u8D2D\u7269\u8F66\u6A21\u5F0F\u5FC5\u987B\u4FDD\u7559\u6700\u8FD1\u6765\u8D27\u7B49\u5B8C\u6574\u52A8\u6001\u6570\u636E");
      assert(
        Object.is(merged.salesQuantitySinceLastArrival, salesQuantitySinceLastArrival),
        `Sales \u503C ${String(salesQuantitySinceLastArrival)} \u5728\u5408\u5E76\u540E\u53D1\u751F\u53D8\u5316`
      );
      assert(
        typeof merged.salesQuantitySinceLastArrival === "number" === (salesQuantitySinceLastArrival !== null && salesQuantitySinceLastArrival !== void 0),
        "Sales \u6B63\u6570\u30010\u3001\u8D1F\u6570\u5E94\u663E\u793A\uFF0Cnull/undefined \u5E94\u9690\u85CF"
      );
    }
    const stableDynamicData = {
      productCode: "P-STABLE",
      cartQuantity: 7,
      lastOrderDate: "2026-08-01",
      salesQuantitySinceLastArrival: 0
    };
    assert(
      Object.is(
        mergeShopHomeCartDynamicData({
          dynamicData: stableDynamicData,
          productCode: "P-STABLE",
          cartQuantity: 7
        }),
        stableDynamicData
      ),
      "\u52A8\u6001\u6570\u636E\u5185\u5BB9\u672A\u53D8\u65F6\u5FC5\u987B\u8FD4\u56DE\u539F\u5F15\u7528\uFF0C\u907F\u514D Sales \u5206\u6279\u89E6\u53D1\u672A\u53D8\u5316\u5361\u7247\u91CD\u6E32\u67D3"
    );
    assert(productCardSource.includes("export default memo(ProductCard)"), "ProductCard \u5FC5\u987B\u4F7F\u7528 React.memo");
    const quantityHandler = extractFunctionBody(
      shopHomeSource,
      "const handleProductQuantityChange = useCallback",
      "const handleRemoveFromCart = useCallback"
    );
    assert(
      shopHomeSource.includes("const dynamicDataMapRef = useRef<Record<string, StoreOrderDynamicData>>({})") && quantityHandler.includes("dynamicDataMapRef.current[productCode]?.cartQuantity") && !quantityHandler.includes("\n      dynamicDataMap,\n"),
      "\u6570\u91CF\u56DE\u8C03\u4E0D\u5F97\u4F9D\u8D56\u6574\u4E2A dynamicDataMap\uFF0C\u5426\u5219\u6BCF\u6279 Sales \u90FD\u4F1A\u8BA9\u6240\u6709 ProductCard \u91CD\u65B0\u6E32\u67D3"
    );
    const hiddenIdentity = buildShopHomeDynamicDataRequestIdentity({
      active: false,
      storeCode: "S1",
      productCodes: ["P1", "P2"]
    });
    const hiddenCoordinator = createShopHomeDynamicDataRequestCoordinator();
    hiddenCoordinator.activate(hiddenIdentity);
    let requestCount = 0;
    await runShopHomeDynamicDataRequest({
      coordinator: hiddenCoordinator,
      token: hiddenCoordinator.begin(hiddenIdentity),
      productCodes: ["P1", "P2"],
      request: async () => {
        requestCount += 1;
        return {};
      },
      onSuccess: () => void 0,
      onError: () => void 0
    });
    assert(requestCount === 0, "\u65E0\u6709\u6548\u5C55\u793A\u4E0A\u4E0B\u6587\u65F6\u4E0D\u5F97\u53D1\u8D77\u52A8\u6001\u6570\u636E\u8BF7\u6C42");
    const pageOneIdentity = buildShopHomeDynamicDataRequestIdentity({
      active: true,
      storeCode: "S1",
      productCodes: ["P1", "P2"]
    });
    const pageTwoIdentity = buildShopHomeDynamicDataRequestIdentity({
      active: true,
      storeCode: "S1",
      productCodes: ["P3", "P4"]
    });
    const storeTwoIdentity = buildShopHomeDynamicDataRequestIdentity({
      active: true,
      storeCode: "S2",
      productCodes: ["P3", "P4"]
    });
    assert(pageOneIdentity !== pageTwoIdentity, "\u5206\u9875\u540E\u8BF7\u6C42\u8EAB\u4EFD\u5FC5\u987B\u53D8\u5316");
    assert(pageTwoIdentity !== storeTwoIdentity, "\u5207\u5E97\u540E\u8BF7\u6C42\u8EAB\u4EFD\u5FC5\u987B\u53D8\u5316");
    const renderSafeCoordinator = createShopHomeDynamicDataRequestCoordinator();
    renderSafeCoordinator.activate(pageOneIdentity);
    const committedPageOneToken = renderSafeCoordinator.begin(pageOneIdentity);
    assert(committedPageOneToken, "\u5DF2\u63D0\u4EA4\u7684\u9875 1 \u5E94\u80FD\u5F00\u59CB\u8BF7\u6C42");
    const uncommittedPageTwoIdentity = pageTwoIdentity;
    assert(uncommittedPageTwoIdentity !== pageOneIdentity, "\u672A\u63D0\u4EA4\u5019\u9009\u8EAB\u4EFD\u5E94\u4E0E\u5F53\u524D\u8EAB\u4EFD\u4E0D\u540C");
    assert(
      renderSafeCoordinator.isCurrent(committedPageOneToken),
      "\u672A\u63D0\u4EA4\u7684\u5019\u9009\u8EAB\u4EFD\u4E0D\u5E94\u6539\u53D8\u5DF2\u63D0\u4EA4\u9875\u9762\u7684\u5F53\u524D token"
    );
    renderSafeCoordinator.deactivate(pageOneIdentity);
    renderSafeCoordinator.activate(pageTwoIdentity);
    renderSafeCoordinator.deactivate(pageTwoIdentity);
    renderSafeCoordinator.activate(pageOneIdentity);
    const returnedPageOneToken = renderSafeCoordinator.begin(pageOneIdentity);
    assert(returnedPageOneToken, "B \u672A begin \u5C31\u56DE A \u65F6\uFF0CA \u5E94\u80FD\u5F00\u59CB\u65B0\u8BF7\u6C42");
    assert(
      returnedPageOneToken.version > committedPageOneToken.version,
      "\u56DE\u5230 A \u7684\u8BF7\u6C42\u5FC5\u987B\u4F7F\u7528\u66F4\u65B0\u7684\u5355\u8C03 version"
    );
    assert(
      !renderSafeCoordinator.isCurrent(committedPageOneToken) && renderSafeCoordinator.isCurrent(returnedPageOneToken),
      "B \u672A begin \u5C31\u56DE A \u65F6\uFF0C\u65E7 A token \u5E94\u5931\u6548\u4E14\u65B0 A token \u5E94\u751F\u6548"
    );
    const requestedBatches = [];
    const runAbaScenario = async (label, firstIdentity, middleIdentity, firstProductCodes, middleProductCodes, firstOutcome) => {
      const coordinator = createShopHomeDynamicDataRequestCoordinator();
      const committed = [];
      const errors = [];
      const firstDeferred = createDeferred();
      const middleDeferred = createDeferred();
      const currentDeferred = createDeferred();
      coordinator.activate(firstIdentity);
      const firstRequest = runShopHomeDynamicDataRequest({
        coordinator,
        token: coordinator.begin(firstIdentity),
        productCodes: firstProductCodes,
        request: (productCodes) => {
          requestedBatches.push(productCodes);
          return firstDeferred.promise;
        },
        onSuccess: () => committed.push(`${label}-first`),
        onError: () => errors.push(`${label}-first`)
      });
      coordinator.activate(middleIdentity);
      const middleRequest = runShopHomeDynamicDataRequest({
        coordinator,
        token: coordinator.begin(middleIdentity),
        productCodes: middleProductCodes,
        request: (productCodes) => {
          requestedBatches.push(productCodes);
          return middleDeferred.promise;
        },
        onSuccess: () => committed.push(`${label}-middle`),
        onError: () => errors.push(`${label}-middle`)
      });
      coordinator.activate(firstIdentity);
      const currentRequest = runShopHomeDynamicDataRequest({
        coordinator,
        token: coordinator.begin(firstIdentity),
        productCodes: firstProductCodes,
        request: (productCodes) => {
          requestedBatches.push(productCodes);
          return currentDeferred.promise;
        },
        onSuccess: () => committed.push(`${label}-current`),
        onError: () => errors.push(`${label}-current`)
      });
      currentDeferred.resolve({ CURRENT: { productCode: "CURRENT", cartQuantity: 3 } });
      await currentRequest;
      if (firstOutcome === "success") {
        firstDeferred.resolve({ STALE: { productCode: "STALE", cartQuantity: 1 } });
        middleDeferred.reject(new Error(`${label} stale middle error`));
      } else {
        firstDeferred.reject(new Error(`${label} stale first error`));
        middleDeferred.resolve({ STALE: { productCode: "STALE", cartQuantity: 2 } });
      }
      await Promise.all([firstRequest, middleRequest]);
      assert(
        JSON.stringify(committed) === JSON.stringify([`${label}-current`]),
        `${label} ABA \u540E\u65E7\u6210\u529F\u54CD\u5E94\u4E0D\u5F97\u63D0\u4EA4`
      );
      assert(errors.length === 0, `${label} ABA \u540E\u65E7\u9519\u8BEF\u4E0D\u5F97\u6C61\u67D3\u65B0\u72B6\u6001`);
    };
    await runAbaScenario(
      "cart-delete-page",
      pageOneIdentity,
      pageTwoIdentity,
      ["P1", "P2"],
      ["P3", "P4"],
      "success"
    );
    await runAbaScenario(
      "store",
      pageTwoIdentity,
      storeTwoIdentity,
      ["P3", "P4"],
      ["P3", "P4"],
      "error"
    );
    assert(
      requestedBatches.every((batch) => batch.length === 2) && requestedBatches.length === 6,
      "\u6BCF\u9875\u52A8\u6001\u6570\u636E\u5FC5\u987B\u4E00\u6B21\u6279\u91CF\u8BF7\u6C42\uFF0C\u4E0D\u5F97\u6309\u5546\u54C1\u4EA7\u751F N+1"
    );
    assert(
      shopHomeSource.includes("const cartQuantityByProductCode = useMemo<Record<string, number>>(() => {") && shopHomeSource.includes("acc[item.productCode] = item.quantity") && shopHomeSource.includes("const displayProductCodes = useMemo(") && shopHomeSource.includes("buildShopHomeDynamicDataRequestIdentity({") && shopHomeSource.includes("createShopHomeDynamicDataRequestCoordinator()") && shopHomeSource.includes("runShopHomeDynamicDataRequest({") && !shopHomeSource.includes("const cartProductDynamicDataMap = useMemo") && shopHomeSource.includes("const currentCartQuantity = cart?.isSummaryOnly") && shopHomeSource.includes("mergeShopHomeCartDynamicData({") && productCardSource.includes("const hasSalesQuantity = typeof salesQuantity === 'number'") && shopHomeSource.includes("dynamicData={cardDynamicData}"),
      "ShopHome \u672A\u6279\u91CF\u52A0\u8F7D\u5F53\u524D\u8D2D\u7269\u8F66\u9875\u52A8\u6001\u6570\u636E\u3001\u672A\u6309\u771F\u5B9E\u8D2D\u7269\u8F66\u6570\u91CF\u5408\u5E76\uFF0C\u6216 ProductCard \u7684 Sales \u663E\u793A\u5951\u7EA6\u56DE\u5F52"
    );
  });
  if (cartOnlyDynamicDataFailure) failures.push(cartOnlyDynamicDataFailure);
  const dynamicDataCommitPhaseFailure = await runTest("\u52A8\u6001\u6570\u636E\u8EAB\u4EFD\u53EA\u5E94\u5728 layout \u63D0\u4EA4\u9636\u6BB5\u6FC0\u6D3B", () => {
    const identityStart = shopHomeSource.indexOf("const dynamicDataRequestIdentity =");
    const pageTitleStart = shopHomeSource.indexOf("const pageTitle = useMemo", identityStart);
    const layoutEffectStart = shopHomeSource.indexOf("useLayoutEffect(() => {", identityStart);
    const requestEffectStart = shopHomeSource.indexOf(
      "useEffect(() => {\n    const identity = dynamicDataRequestIdentity",
      identityStart
    );
    assert(identityStart >= 0 && pageTitleStart > identityStart, "\u627E\u4E0D\u5230\u52A8\u6001\u6570\u636E identity render \u533A\u6BB5");
    assert(
      !shopHomeSource.slice(identityStart, pageTitleStart).includes(".activate("),
      "render \u9636\u6BB5\u4E0D\u5F97\u76F4\u63A5 activate coordinator"
    );
    assert(
      shopHomeSource.includes("import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'") && layoutEffectStart > pageTitleStart && requestEffectStart > layoutEffectStart,
      "coordinator activate \u5FC5\u987B\u4F7F\u7528 useLayoutEffect\uFF0C\u4E14\u5148\u4E8E passive request effect"
    );
    const layoutEffectBody = shopHomeSource.slice(layoutEffectStart, requestEffectStart);
    assert(
      layoutEffectBody.includes("coordinator.activate(dynamicDataRequestIdentity)") && layoutEffectBody.includes("coordinator.deactivate(dynamicDataRequestIdentity)"),
      "layout effect \u5FC5\u987B\u5728\u63D0\u4EA4\u65F6\u6FC0\u6D3B identity\uFF0C\u5E76\u5728 cleanup \u5B9A\u5411\u5931\u6548\u8BE5 identity"
    );
  });
  if (dynamicDataCommitPhaseFailure) failures.push(dynamicDataCommitPhaseFailure);
  const progressiveSalesSummaryFailure = await runTest(
    "Sales summary \u5E94\u5148\u63D0\u4EA4\u57FA\u7840\u52A8\u6001\u6570\u636E\u3001\u4F18\u5148 50 \u4E2A\u4E14\u62D2\u7EDD\u65E7\u54CD\u5E94\u4E0E\u4F59\u91CF\u5931\u8D25",
    async () => {
      const coordinator = createShopHomeSalesSummaryRequestCoordinator();
      const identity = buildShopHomeDynamicDataRequestIdentity({
        active: true,
        storeCode: "S1",
        productCodes: ["P1", "P2", "P3"]
      });
      assert(
        JSON.stringify(readShopHomeDynamicDataRequestProductCodes(identity)) === JSON.stringify(["P1", "P2", "P3"]),
        "Sales \u5206\u6279\u5546\u54C1\u7801\u5FC5\u987B\u4ECE\u7A33\u5B9A identity \u8FD8\u539F\uFF0C\u4E0D\u80FD\u4F9D\u8D56\u8D2D\u7269\u8F66\u91CD\u7B97\u4EA7\u751F\u7684\u65B0\u6570\u7EC4\u5F15\u7528"
      );
      const priorityDeferred = createDeferred();
      const remainderDeferred = createDeferred();
      const requestedBatches = [];
      const committed = {};
      let remainderRequest = null;
      coordinator.activate(identity);
      const priorityRequest = runShopHomeSalesSummaryRequest({
        coordinator,
        token: coordinator.begin(identity),
        productCodes: ["P1", "P2"],
        request: (productCodes) => {
          requestedBatches.push(productCodes);
          return priorityDeferred.promise;
        },
        onSuccess: (result) => {
          Object.assign(committed, result);
          remainderRequest = runShopHomeSalesSummaryRequest({
            coordinator,
            token: coordinator.begin(identity),
            productCodes: ["P3"],
            request: (productCodes) => {
              requestedBatches.push(productCodes);
              return remainderDeferred.promise;
            },
            onSuccess: (nextResult) => Object.assign(committed, nextResult),
            // 余量失败不得清掉已经可见的优先批 Sales。
            onError: () => void 0
          });
        },
        onError: () => void 0
      });
      assert(requestedBatches.length === 1, "\u57FA\u7840\u52A8\u6001\u6570\u636E\u63D0\u4EA4\u524D\u4E0D\u5E94\u9884\u53D6\u4F59\u91CF Sales");
      priorityDeferred.resolve({ P1: 12, P2: 0 });
      await priorityRequest;
      assert(
        JSON.stringify(requestedBatches) === JSON.stringify([["P1", "P2"], ["P3"]]),
        "Sales \u5FC5\u987B\u5148\u8BF7\u6C42\u4F18\u5148\u6279\uFF0C\u6210\u529F\u540E\u624D\u8BF7\u6C42\u4F59\u91CF"
      );
      assert(committed.P1 === 12 && committed.P2 === 0, "\u4F18\u5148\u6279\u6210\u529F\u540E\u5FC5\u987B\u5148\u63D0\u4EA4\u5176 Sales");
      remainderDeferred.reject(new Error("remainder failed"));
      await remainderRequest;
      assert(committed.P1 === 12 && committed.P2 === 0 && !("P3" in committed), "\u4F59\u91CF\u5931\u8D25\u4E0D\u5F97\u6E05\u7A7A\u4F18\u5148\u6279");
      const abaCoordinator = createShopHomeSalesSummaryRequestCoordinator();
      const staleDeferred = createDeferred();
      const currentDeferred = createDeferred();
      const abaCommitted = [];
      const abaErrors = [];
      const filterIdentity = buildShopHomeDynamicDataRequestIdentity({
        active: true,
        storeCode: "S1",
        productCodes: ["FILTERED"]
      });
      abaCoordinator.activate(identity);
      const staleRequest = runShopHomeSalesSummaryRequest({
        coordinator: abaCoordinator,
        token: abaCoordinator.begin(identity),
        productCodes: ["P1"],
        request: () => staleDeferred.promise,
        onSuccess: () => abaCommitted.push("stale"),
        onError: () => abaErrors.push("stale")
      });
      abaCoordinator.activate(filterIdentity);
      abaCoordinator.activate(identity);
      const currentRequest = runShopHomeSalesSummaryRequest({
        coordinator: abaCoordinator,
        token: abaCoordinator.begin(identity),
        productCodes: ["P1"],
        request: () => currentDeferred.promise,
        onSuccess: () => abaCommitted.push("current"),
        onError: () => abaErrors.push("current")
      });
      staleDeferred.reject(new Error("stale error"));
      currentDeferred.resolve({ P1: -3 });
      await Promise.all([staleRequest, currentRequest]);
      assert(
        JSON.stringify(abaCommitted) === JSON.stringify(["current"]) && abaErrors.length === 0,
        "\u7B5B\u9009/\u5207\u5E97 ABA \u540E\u65E7 Sales \u6210\u529F\u6216\u9519\u8BEF\u4E0D\u5F97\u6C61\u67D3\u5F53\u524D\u9875"
      );
      const dynamicDataEffect = extractFunctionBody(
        shopHomeSource,
        "useEffect(() => {\n    const identity = dynamicDataRequestIdentity",
        "useEffect(() => {\n    let cancelled = false"
      );
      const removeBody = extractFunctionBody(
        shopHomeSource,
        "const handleRemoveFromCart = useCallback",
        '  return (\n    <div className="shop-home-page">'
      );
      assert(
        shopHomeSource.includes("includeSales: false") && dynamicDataEffect.includes("const requestProductCodes = readShopHomeDynamicDataRequestProductCodes(identity)") && dynamicDataEffect.includes("const prioritySalesProductCodes = requestProductCodes.slice(0, 50)") && dynamicDataEffect.includes("const remainderSalesProductCodes = requestProductCodes.slice(50)") && !dynamicDataEffect.includes("\n    prioritySalesProductCodes,\n") && !dynamicDataEffect.includes("\n    remainderSalesProductCodes,\n") && shopHomeSource.includes("logShopHomePerf('dynamic-base.done'") && shopHomeSource.includes("stage: 'sales-priority.done' | 'sales-remainder.done'") && dynamicDataEffect.includes("const requestRemainderSales = () => {") && dynamicDataEffect.includes("onError: requestRemainderSales") && dynamicDataEffect.includes("runShopHomeSalesSummaryRequest({") && !removeBody.includes("refreshDynamicData()") && removeBody.includes("const [cartRefreshResult] = await Promise.allSettled([") && removeBody.includes("refreshCart(),") && removeBody.includes("refreshDynamicDataForProducts([productCode]),"),
        "ShopHome \u672A\u4F7F\u7528\u7A33\u5B9A identity \u5206\u6279\u3001\u4F18\u5148\u5931\u8D25\u540E\u7EE7\u7EED\u4F59\u91CF\u3001\u6027\u80FD\u65E5\u5FD7\u6216\u5220\u9664\u5C40\u90E8\u5237\u65B0"
      );
    }
  );
  if (progressiveSalesSummaryFailure) failures.push(progressiveSalesSummaryFailure);
  const storeScopedRefreshFailure = await runTest("\u5355\u5546\u54C1\u52A8\u6001\u6570\u636E\u5237\u65B0\u5E94\u62D2\u7EDD\u5207\u5E97 ABA \u7684\u65E7\u54CD\u5E94\uFF0C\u5E76\u4FDD\u7559\u5E76\u53D1\u5546\u54C1\u5404\u81EA\u7ED3\u679C", async () => {
    const coordinator = createShopHomeDynamicDataStoreScopeCoordinator();
    const staleResponse = createDeferred();
    const productAResponse = createDeferred();
    const productBResponse = createDeferred();
    const committed = {};
    coordinator.activate("S1");
    const staleToken = coordinator.capture("S1");
    const staleRequest = runShopHomeStoreScopedDynamicDataRequest({
      coordinator,
      token: staleToken,
      productCodes: ["STALE"],
      request: () => staleResponse.promise,
      onSuccess: (result) => Object.assign(committed, result)
    });
    coordinator.activate("S2");
    coordinator.activate("S1");
    const currentToken = coordinator.capture("S1");
    const productARequest = runShopHomeStoreScopedDynamicDataRequest({
      coordinator,
      token: currentToken,
      productCodes: ["A"],
      request: () => productAResponse.promise,
      onSuccess: (result) => Object.assign(committed, result)
    });
    const productBRequest = runShopHomeStoreScopedDynamicDataRequest({
      coordinator,
      token: currentToken,
      productCodes: ["B"],
      request: () => productBResponse.promise,
      onSuccess: (result) => Object.assign(committed, result)
    });
    productBResponse.resolve({ B: { productCode: "B", cartQuantity: 2 } });
    staleResponse.resolve({ STALE: { productCode: "STALE", cartQuantity: 99 } });
    productAResponse.resolve({ A: { productCode: "A", cartQuantity: 1 } });
    await Promise.all([staleRequest, productARequest, productBRequest]);
    assert(!("STALE" in committed), "\u5207\u5E97 ABA \u540E\u65E7\u5355\u5546\u54C1\u54CD\u5E94\u4E0D\u5F97\u5408\u5E76");
    assert(committed.A?.cartQuantity === 1 && committed.B?.cartQuantity === 2, "\u4E0D\u540C\u5546\u54C1\u7684\u5C40\u90E8\u5237\u65B0\u4E0D\u5F97\u4E92\u76F8\u8986\u76D6");
    const refreshDynamicDataForProductsBody = extractFunctionBody(
      shopHomeSource,
      "const refreshDynamicDataForProducts = useCallback",
      "const updateScanFeedback = useCallback"
    );
    assert(
      refreshDynamicDataForProductsBody.includes("const coordinator = dynamicDataStoreScopeCoordinatorRef.current") && refreshDynamicDataForProductsBody.includes("const token = coordinator.capture(storeCode)") && refreshDynamicDataForProductsBody.includes("runShopHomeStoreScopedDynamicDataRequest({"),
      "\u5355\u5546\u54C1\u5237\u65B0\u6CA1\u6709\u63A5\u5165\u53EF\u6D4B\u8BD5\u7684\u95E8\u5E97\u8303\u56F4\u8BF7\u6C42\u534F\u8C03\u903B\u8F91"
    );
  });
  if (storeScopedRefreshFailure) failures.push(storeScopedRefreshFailure);
  const cartRefreshScopeFailure = await runTest("\u5220\u9664\u540E\u7684\u8D2D\u7269\u8F66\u5237\u65B0\u5E94\u62D2\u7EDD\u5207\u5E97\u4E0E ABA \u7684\u65E7\u54CD\u5E94", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const refreshCart = useCallback",
      "const ensureFullCart = useCallback"
    );
    assert(
      body.includes("const coordinator = dynamicDataStoreScopeCoordinatorRef.current") && body.includes("const token = coordinator.capture(storeCode)") && body.includes("if (!coordinator.isCurrent(token))") && body.indexOf("if (!coordinator.isCurrent(token))") < body.indexOf("setCart(nextCart)"),
      "\u8D2D\u7269\u8F66\u5237\u65B0\u5FC5\u987B\u5728 setCart \u524D\u6821\u9A8C\u95E8\u5E97 generation\uFF0C\u963B\u6B62 S1\u2192S2\u2192S1 \u7684\u65E7\u54CD\u5E94\u8986\u76D6"
    );
  });
  if (cartRefreshScopeFailure) failures.push(cartRefreshScopeFailure);
  const storeSwitchMutationStateFailure = await runTest("\u5207\u5E97\u5E94\u6E05\u7406\u65E7\u95E8\u5E97\u5546\u54C1\u64CD\u4F5C\u72B6\u6001\u548C\u5F85\u63D0\u4EA4\u5B9A\u65F6\u5668", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "useEffect(() => {\n    const nextStoreCode = selectedStore?.storeCode ?? null",
      "useEffect(() => {\n    return () => {"
    );
    assert(
      body.includes("Object.values(quantityUpdateTimersRef.current).forEach((timer) => window.clearTimeout(timer))") && body.includes("quantityUpdateTimersRef.current = {}") && body.includes("quantityUpdateVersionRef.current = {}") && body.includes("setOptimisticCartQuantityMap({})") && body.includes("setRemovingCartProductMap({})") && body.includes("setQuantityLoadingMap({})"),
      "\u5207\u5E97\u5FC5\u987B\u6E05\u7A7A\u65E7\u95E8\u5E97\u4E50\u89C2\u6570\u91CF\u3001\u5220\u9664/\u52A0\u8F7D\u72B6\u6001\u5E76\u5931\u6548\u5F85\u63D0\u4EA4\u6570\u91CF\u66F4\u65B0"
    );
  });
  if (storeSwitchMutationStateFailure) failures.push(storeSwitchMutationStateFailure);
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
  const productCardAddVisibilityFailure = await runTest("\u5546\u54C1\u5361\u5E94\u4EE5\u56FA\u5B9A\u69FD\u4F4D\u5207\u6362\u9996\u6B21\u52A0\u8D2D\u548C\u5220\u9664", () => {
    assert(
      productCardSource.includes("cartQuantity > 0 ? (") && productCardSource.includes("cartQuantity <= 0 ? (") && productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--left"') && productCardSource.includes('className="shop-product-card-action-slot shop-product-card-action-slot--right"') && productCardSource.includes("const addQuantity = quantity > 0 ? quantity : stepQuantity") && productCardSource.includes("void onAddToCart(product, addQuantity)") && productCardSource.includes('className="shop-product-cart-button"') && productCardSource.includes('aria-label="Add product to cart"') && productCardSource.includes('title="Add product to cart"') && !productCardSource.includes("shop-product-card-actions--in-cart") && !productCardSource.includes(">\n                  Add\n                </Button>"),
      "\u5546\u54C1\u5361\u6CA1\u6709\u4FDD\u6301\u5DE6\u53F3\u56FA\u5B9A\u69FD\u4F4D\uFF0C\u9996\u6B21\u52A0\u8D2D\u4ECD\u663E\u793A Add \u6587\u5B57\uFF0C\u6216\u672A\u8865\u9F50\u53EF\u8BBF\u95EE\u6027\u8BF4\u660E"
    );
  });
  if (productCardAddVisibilityFailure) failures.push(productCardAddVisibilityFailure);
  const productCardQuickPackFailure = await runTest("\u5546\u54C1\u5361 2/3/4 \u4EFD\u5FEB\u6377\u6309\u94AE\u5E94\u8BBE\u7F6E\u603B\u6570\u91CF", () => {
    const quickPackCases = [
      { packCount: 2, stepQuantity: 1, currentQuantity: 9, expected: 2 },
      { packCount: 3, stepQuantity: 12, currentQuantity: 60, expected: 36 },
      { packCount: 4, stepQuantity: 12, currentQuantity: 0, expected: 48 }
    ];
    assert(
      quickPackCases.every(({ packCount, stepQuantity, expected }) => packCount * stepQuantity === expected) && quickPackCases.some(({ currentQuantity, expected }) => currentQuantity > expected) && productCardSource.includes("const handleQuickPackQuantity = (packCount: number) => {") && productCardSource.includes("const quickQuantity = packCount * stepQuantity") && productCardSource.includes("applyQuantityChange(quickQuantity)") && productCardSource.includes("[2, 3, 4].map((packCount) =>") && productCardSource.includes("onClick={() => handleQuickPackQuantity(packCount)}") && productCardSource.includes('className="shop-product-quick-pack-button"') && productCardSource.includes("aria-label={`Set total quantity to ${packCount} packs (${quickQuantity})`}") && productCardSource.includes("title={`Set total quantity to ${packCount} packs (${quickQuantity})`}"),
      "2/3/4 \u4EFD\u6309\u94AE\u672A\u6309 packCount * stepQuantity \u8BBE\u7F6E\u603B\u91CF\u3001\u672A\u8FDE\u63A5\u70B9\u51FB\u5904\u7406\uFF0C\u6216\u7F3A\u5C11\u53EF\u8BBF\u95EE\u6027\u8BF4\u660E"
    );
  });
  if (productCardQuickPackFailure) failures.push(productCardQuickPackFailure);
  const productCardRemoveOptimisticFailure = await runTest("\u5546\u54C1\u5361\u5220\u9664\u5E94\u5148\u4E50\u89C2\u9000\u51FA\u5DF2\u5165\u8F66\u72B6\u6001\u5E76\u9632\u91CD\u590D\u70B9\u51FB", () => {
    const body = extractFunctionBody(
      shopHomeSource,
      "const handleRemoveFromCart = useCallback",
      '  return (\n    <div className="shop-home-page">'
    );
    assert(
      productCardSource.includes("removing?: boolean") && productCardSource.includes("removing = false") && productCardSource.includes("if (removing)") && shopHomeSource.includes("const [removingCartProductMap, setRemovingCartProductMap] = useState<Record<string, boolean>>({})") && body.includes("if (removingCartProductMap[productCode])") && body.includes("setRemovingCartProductMap((prev) => ({ ...prev, [productCode]: true }))") && body.includes("setOptimisticCartQuantityMap((prev) => ({ ...prev, [productCode]: 0 }))") && body.includes("const [cartRefreshResult] = await Promise.allSettled") && body.includes("cartRefreshResult.status === 'fulfilled' && cartRefreshResult.value") && body.includes("delete next[productCode]") && shopHomeSource.includes("const isRemovingFromCart = Boolean(removingCartProductMap[product.productCode])") && shopHomeSource.includes("const optimisticCartQuantity = isRemovingFromCart") && shopHomeSource.includes("? 0") && shopHomeSource.includes("removing={isRemovingFromCart}"),
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
  const productCardQuantityStyleFailure = await runTest("\u5546\u54C1\u5361\u6570\u91CF\u64CD\u4F5C\u533A\u5E94\u4F7F\u7528\u5355\u4E00\u56FA\u5B9A\u7F51\u683C\u5E03\u5C40", () => {
    assert(
      productCardSource.includes("shop-product-quantity-stepper") && productCardSource.includes("shop-product-quantity-button") && productCardSource.includes("shop-product-quantity-input") && globalCssSource.includes("grid-template-columns: 20px 20px minmax(24px, 1fr) 20px repeat(3, 20px) 20px") && globalCssSource.includes("box-sizing: border-box") && globalCssSource.includes("white-space: nowrap") && globalCssSource.includes(".shop-product-quantity-stepper") && globalCssSource.includes("display: contents") && globalCssSource.includes(".shop-product-quantity-button") && globalCssSource.includes(".shop-product-quantity-input") && globalCssSource.includes(".shop-product-quick-pack-button") && globalCssSource.includes(".shop-product-cart-button") && !globalCssSource.includes(".shop-product-card-actions--in-cart") && !globalCssSource.includes(".shop-product-add-button"),
      "\u5546\u54C1\u5361\u6570\u91CF\u64CD\u4F5C\u533A\u4ECD\u4F7F\u7528\u5165\u8F66\u524D\u540E\u52A8\u6001\u5217\u5BBD\uFF0C\u6216\u7F3A\u5C11\u5FEB\u6377\u6309\u94AE/\u56FE\u6807\u69FD\u6837\u5F0F"
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
  const bestSellerFailedRowsNoticeFailure = await runTest("\u70ED\u9500\u5546\u54C1 Failed \u72B6\u6001\u5E94\u540C\u65F6\u4FDD\u7559\u5546\u54C1\u884C\u548C\u683C\u5F0F\u5316\u8B66\u544A", () => {
    assert(
      bestSellersSource.includes("import { formatStatisticMessageAmounts } from '../../../utils/statisticMessage'") && bestSellersSource.includes("const formattedStatisticMessage = useMemo(") && bestSellersSource.includes("formatStatisticMessageAmounts(statisticMessage)") && bestSellersSource.includes("formattedStatisticMessage ||") && bestSellersSource.includes("<Tooltip title={formattedStatisticMessage}>") && bestSellersSource.includes("type={statisticStatus === 'Failed' ? 'error' : 'warning'}") && bestSellersSource.includes("message={bestSellerStatusNotice}") && bestSellersSource.includes("dataSource={products}"),
      "Failed \u72B6\u6001\u6CA1\u6709\u540C\u65F6\u4FDD\u7559\u5546\u54C1\u884C\u3001\u7EA2\u8272\u8B66\u544A\uFF0C\u6216 Tooltip/Alert \u672A\u4F7F\u7528\u7CBE\u786E\u683C\u5F0F\u5316\u6D88\u606F"
    );
  });
  if (bestSellerFailedRowsNoticeFailure) failures.push(bestSellerFailedRowsNoticeFailure);
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
