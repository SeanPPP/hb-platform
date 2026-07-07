// src/pages/ShopHome/shopComingSoonPerformance.logic.test.ts
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
var comingSoonFile = path.resolve(process.cwd(), "src/pages/ShopHome/components/ComingSoonSection.tsx");
var comingSoonSource = readFileSync(comingSoonFile, "utf8");
var styleFile = path.resolve(process.cwd(), "src/styles/global.css");
var styleSource = readFileSync(styleFile, "utf8");
var zhLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var zhLocaleSource = readFileSync(zhLocaleFile, "utf8");
var enLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
var enLocaleSource = readFileSync(enLocaleFile, "utf8");
function extractCssBlock(selector) {
  const start = styleSource.indexOf(selector);
  assert(start >= 0, `\u627E\u4E0D\u5230\u6837\u5F0F ${selector}`);
  const open = styleSource.indexOf("{", start);
  const close = styleSource.indexOf("}", open);
  assert(open > start && close > open, `\u627E\u4E0D\u5230\u6837\u5F0F ${selector} \u7684\u4EE3\u7801\u5757`);
  return styleSource.slice(open + 1, close);
}
async function main() {
  const failures = [];
  const summaryOnlyFailure = await runTest("Coming Soon \u9996\u5C4F\u5E94\u53EA\u52A0\u8F7D\u8D27\u67DC\u6458\u8981", () => {
    assert(
      comingSoonSource.includes("getComingSoonContainerSummaries") && !comingSoonSource.includes("getComingSoonContainers()"),
      "Coming Soon \u9996\u5C4F\u4ECD\u5728\u8C03\u7528\u5168\u91CF getComingSoonContainers()\uFF0C\u4F1A\u63D0\u524D\u62C9\u6240\u6709\u8D27\u67DC\u5546\u54C1"
    );
  });
  if (summaryOnlyFailure) failures.push(summaryOnlyFailure);
  const lazyContainerFailure = await runTest("\u8D27\u67DC\u5546\u54C1\u5E94\u901A\u8FC7 IntersectionObserver \u61D2\u52A0\u8F7D", () => {
    assert(
      comingSoonSource.includes("IntersectionObserver") && comingSoonSource.includes("getComingSoonContainerProducts") && comingSoonSource.includes("loadContainerProducts"),
      "Coming Soon \u7F3A\u5C11 IntersectionObserver \u8D27\u67DC\u7EA7\u61D2\u52A0\u8F7D\u94FE\u8DEF"
    );
  });
  if (lazyContainerFailure) failures.push(lazyContainerFailure);
  const virtualListFailure = await runTest("\u8D27\u67DC\u5185\u5546\u54C1\u5E94\u4F7F\u7528\u539F\u751F\u865A\u62DF\u5217\u8868\u51CF\u5C11 DOM", () => {
    assert(
      comingSoonSource.includes("PRODUCT_ROW_HEIGHT") && comingSoonSource.includes("VIRTUAL_OVERSCAN") && comingSoonSource.includes("visibleProducts") && comingSoonSource.includes("shop-coming-soon-virtual-spacer") && comingSoonSource.includes("productListElementsRef"),
      "Coming Soon \u7F3A\u5C11\u5546\u54C1\u865A\u62DF\u5217\u8868\u7684\u7A97\u53E3\u8BA1\u7B97\u548C\u5360\u4F4D\u5143\u7D20"
    );
  });
  if (virtualListFailure) failures.push(virtualListFailure);
  const filterScrollResetFailure = await runTest("\u5207\u6362\u7B5B\u9009\u65F6\u5E94\u540C\u6B65\u91CD\u7F6E\u865A\u62DF\u5217\u8868 DOM \u6EDA\u52A8\u4F4D\u7F6E", () => {
    assert(
      comingSoonSource.includes("resetProductScrollPositions") && comingSoonSource.includes("element.scrollTop = 0") && comingSoonSource.includes("resetProductScrollPositions()"),
      "Coming Soon \u5207\u6362 All/Reorder/New \u65F6\u672A\u540C\u6B65\u91CD\u7F6E\u5546\u54C1\u5217\u8868\u771F\u5B9E scrollTop"
    );
  });
  if (filterScrollResetFailure) failures.push(filterScrollResetFailure);
  const containerFilterFailure = await runTest("\u6BCF\u4E2A\u8D27\u67DC\u5E94\u652F\u6301\u72EC\u7ACB\u7EDF\u8BA1\u8FC7\u6EE4\u5E76\u4FDD\u7559\u9876\u90E8\u5168\u5C40\u8FC7\u6EE4", () => {
    assert(
      comingSoonSource.includes("const [filterMode, setFilterMode] = useState<FilterMode>('all')") && comingSoonSource.includes("containerFilterModes") && comingSoonSource.includes("containerFilterModes[container.hguid] ?? filterMode") && comingSoonSource.includes("getContainerFilterStats") && comingSoonSource.includes("shop-coming-soon-container-filters"),
      "Coming Soon \u7F3A\u5C11\u9876\u90E8\u5168\u5C40\u8FC7\u6EE4\u4FDD\u7559\u548C\u6BCF\u8D27\u67DC\u72EC\u7ACB\u8FC7\u6EE4\u8986\u76D6\u903B\u8F91"
    );
  });
  if (containerFilterFailure) failures.push(containerFilterFailure);
  const visibleContainerFailure = await runTest("\u9876\u90E8\u5168\u5C40\u8FC7\u6EE4\u5E94\u9690\u85CF\u5DF2\u52A0\u8F7D\u540E\u65E0\u5339\u914D\u5546\u54C1\u7684\u8D27\u67DC", () => {
    assert(
      comingSoonSource.includes("visibleContainers") && comingSoonSource.includes("filterMode === 'all'") && comingSoonSource.includes("state.status !== 'loaded'") && comingSoonSource.includes("state.products.some((product) => matchesFilter(product, filterMode))") && comingSoonSource.includes("visibleContainers.map((container) => {"),
      "Coming Soon \u9876\u90E8\u5168\u5C40\u8FC7\u6EE4\u672A\u6D3E\u751F\u53EF\u89C1\u8D27\u67DC\uFF0C\u5DF2\u52A0\u8F7D\u540E\u65E0\u5339\u914D\u5546\u54C1\u7684\u8D27\u67DC\u4ECD\u4F1A\u5360\u4F4D"
    );
  });
  if (visibleContainerFailure) failures.push(visibleContainerFailure);
  const containerFilterI18nFailure = await runTest("\u6BCF\u8D27\u67DC\u72EC\u7ACB\u8FC7\u6EE4\u6309\u94AE\u5E94\u652F\u6301\u56FD\u9645\u5316", () => {
    assert(
      comingSoonSource.includes("import { useTranslation } from 'react-i18next'") && comingSoonSource.includes("t('shop.comingSoonFilterAll'") && comingSoonSource.includes("t('shop.comingSoonFilterReorder'") && comingSoonSource.includes("t('shop.comingSoonFilterNew'") && zhLocaleSource.includes('"comingSoonFilterAll"') && zhLocaleSource.includes('"comingSoonFilterReorder"') && zhLocaleSource.includes('"comingSoonFilterNew"') && enLocaleSource.includes('"comingSoonFilterAll"') && enLocaleSource.includes('"comingSoonFilterReorder"') && enLocaleSource.includes('"comingSoonFilterNew"'),
      "Coming Soon \u6BCF\u8D27\u67DC\u8FC7\u6EE4\u6309\u94AE\u4ECD\u6709\u786C\u7F16\u7801\u6587\u6848\u6216\u7F3A\u5C11\u4E2D\u82F1\u6587 locale key"
    );
  });
  if (containerFilterI18nFailure) failures.push(containerFilterI18nFailure);
  const globalFilterClearsLocalFailure = await runTest("\u5207\u6362\u9876\u90E8\u5168\u5C40\u8FC7\u6EE4\u65F6\u5E94\u6E05\u7A7A\u5355\u67DC\u8FC7\u6EE4\u8986\u76D6", () => {
    assert(
      comingSoonSource.includes("setContainerFilterModes({})") && comingSoonSource.includes("resetProductScrollPositions()"),
      "Coming Soon \u9876\u90E8\u5168\u5C40\u8FC7\u6EE4\u5207\u6362\u672A\u6E05\u7A7A\u5355\u67DC\u8986\u76D6\u6216\u672A\u91CD\u7F6E\u865A\u62DF\u5217\u8868\u6EDA\u52A8"
    );
  });
  if (globalFilterClearsLocalFailure) failures.push(globalFilterClearsLocalFailure);
  const containerFilterScrollResetFailure = await runTest("\u5207\u6362\u5355\u67DC\u8FC7\u6EE4\u65F6\u53EA\u5E94\u91CD\u7F6E\u5F53\u524D\u8D27\u67DC\u6EDA\u52A8\u4F4D\u7F6E", () => {
    assert(
      comingSoonSource.includes("resetContainerProductScrollPosition") && comingSoonSource.includes("productListElementsRef.current.get(containerGuid)") && comingSoonSource.includes("handleContainerFilterChange"),
      "Coming Soon \u5355\u67DC\u8FC7\u6EE4\u5207\u6362\u672A\u53EA\u91CD\u7F6E\u5F53\u524D\u8D27\u67DC\u865A\u62DF\u5217\u8868\u6EDA\u52A8"
    );
  });
  if (containerFilterScrollResetFailure) failures.push(containerFilterScrollResetFailure);
  const dateToneFailure = await runTest("\u8D27\u67DC\u65E5\u671F\u5E94\u6309\u72B6\u6001\u663E\u793A\u4E0D\u540C\u989C\u8272", () => {
    assert(
      comingSoonSource.includes("getComingSoonDateTone") && comingSoonSource.includes("return 'arrived'") && comingSoonSource.includes("return 'soon'") && comingSoonSource.includes("return 'future'") && comingSoonSource.includes("return 'unknown'"),
      "Coming Soon \u7F3A\u5C11 arrived/soon/future/unknown \u65E5\u671F\u989C\u8272\u72B6\u6001 helper"
    );
    assert(
      styleSource.includes(".shop-coming-soon-date-badge") && styleSource.includes(".shop-coming-soon-date-badge--arrived") && styleSource.includes(".shop-coming-soon-date-badge--soon") && styleSource.includes(".shop-coming-soon-date-badge--future") && styleSource.includes(".shop-coming-soon-date-badge--unknown"),
      "Coming Soon \u7F3A\u5C11\u7A33\u5B9A\u65E5\u671F\u989C\u8272 class"
    );
  });
  if (dateToneFailure) failures.push(dateToneFailure);
  const observerFallbackFailure = await runTest("\u65E0 IntersectionObserver \u65F6\u5E94\u52A0\u8F7D\u5168\u90E8\u8D27\u67DC\u907F\u514D\u6C38\u4E45\u9AA8\u67B6\u5C4F", () => {
    assert(
      comingSoonSource.includes("typeof IntersectionObserver === 'undefined'") && comingSoonSource.includes("containers.forEach((container) => {") && !comingSoonSource.includes("containers.slice(0, 2)"),
      "Coming Soon \u5728\u65E0 IntersectionObserver \u73AF\u5883\u4E0B\u4ECD\u53EF\u80FD\u53EA\u52A0\u8F7D\u90E8\u5206\u8D27\u67DC"
    );
  });
  if (observerFallbackFailure) failures.push(observerFallbackFailure);
  const lazyImageFailure = await runTest("Coming Soon \u5546\u54C1\u56FE\u7247\u5E94\u61D2\u52A0\u8F7D", () => {
    assert(
      comingSoonSource.includes('loading="lazy"'),
      'Coming Soon \u5546\u54C1\u56FE\u7247\u672A\u8BBE\u7F6E loading="lazy"\uFF0C\u9996\u5C4F\u5916\u56FE\u7247\u4F1A\u62A2\u5360\u8D44\u6E90'
    );
  });
  if (lazyImageFailure) failures.push(lazyImageFailure);
  const horizontalLayoutFailure = await runTest("Coming Soon \u8D27\u67DC\u5217\u8868\u5E94\u6A2A\u5411\u6EDA\u52A8\u6392\u5217", () => {
    const listBlock = extractCssBlock(".shop-coming-soon-list");
    assert(
      listBlock.includes("overflow-x: auto") && listBlock.includes("grid-auto-flow: column") && listBlock.includes("grid-auto-columns"),
      "Coming Soon \u8D27\u67DC\u5217\u8868\u672A\u914D\u7F6E\u6A2A\u5411\u6ED1\u52A8\u5217\u5E03\u5C40"
    );
  });
  if (horizontalLayoutFailure) failures.push(horizontalLayoutFailure);
  const threeContainerColumnsFailure = await runTest("Coming Soon \u684C\u9762\u7AEF\u4E00\u5C4F\u5E94\u6309 3 \u4E2A\u8D27\u67DC\u5217\u8BA1\u7B97\u5BBD\u5EA6", () => {
    const listBlock = extractCssBlock(".shop-coming-soon-list");
    assert(
      listBlock.includes("calc((100% - 36px) / 3)"),
      "Coming Soon \u8D27\u67DC\u5217\u5BBD\u672A\u6309\u5185\u5BB9\u533A 3 \u5217\u5E03\u5C40\u8BA1\u7B97"
    );
  });
  if (threeContainerColumnsFailure) failures.push(threeContainerColumnsFailure);
  const tallerContainerFailure = await runTest("Coming Soon \u8D27\u67DC\u548C\u865A\u62DF\u5546\u54C1\u5217\u8868\u5E94\u52A0\u9AD8", () => {
    const cardBlock = extractCssBlock(".shop-coming-soon-card");
    assert(
      comingSoonSource.includes("const PRODUCT_ROW_HEIGHT = 176") && comingSoonSource.includes("const PRODUCT_LIST_HEIGHT = 560") && cardBlock.includes("height: 780px"),
      "Coming Soon \u8D27\u67DC\u9AD8\u5EA6\u6216\u865A\u62DF\u5217\u8868\u9AD8\u5EA6\u672A\u6309\u52A0\u9AD8\u540E\u7684\u5546\u54C1\u884C\u540C\u6B65\u8C03\u6574"
    );
  });
  if (tallerContainerFailure) failures.push(tallerContainerFailure);
  const cardFlexLayoutFailure = await runTest("Coming Soon \u56FA\u5B9A\u9AD8\u5EA6\u8D27\u67DC\u5185\u90E8\u5E94\u4F7F\u7528\u5F39\u6027\u5E03\u5C40\u907F\u514D\u88C1\u526A", () => {
    const cardBlock = extractCssBlock(".shop-coming-soon-card");
    const bodyBlock = extractCssBlock(".shop-coming-soon-card .ant-card-body");
    const productGridBlock = extractCssBlock(".shop-coming-soon-product-grid");
    assert(
      cardBlock.includes("display: flex") && cardBlock.includes("flex-direction: column") && bodyBlock.includes("flex: 1") && bodyBlock.includes("min-height: 0") && bodyBlock.includes("display: flex") && productGridBlock.includes("flex: 1") && productGridBlock.includes("min-height: 0"),
      "Coming Soon \u8D27\u67DC\u56FA\u5B9A\u9AD8\u5EA6\u5185\u90E8\u672A\u4F7F\u7528 flex \u627F\u63A5\u5269\u4F59\u9AD8\u5EA6\uFF0C\u4ECD\u6709\u5E95\u90E8\u88C1\u526A\u98CE\u9669"
    );
  });
  if (cardFlexLayoutFailure) failures.push(cardFlexLayoutFailure);
  const barcodePreviewFailure = await runTest("Coming Soon \u5546\u54C1\u884C\u5E94\u751F\u6210\u6761\u7801\u56FE", () => {
    assert(
      comingSoonSource.includes("BarcodePreview") && comingSoonSource.includes("product.barcode") && comingSoonSource.includes("textNoWrap"),
      "Coming Soon \u5546\u54C1\u884C\u672A\u590D\u7528 BarcodePreview \u751F\u6210\u6761\u7801\u56FE\u5E76\u4FDD\u6301\u6761\u7801\u6587\u672C\u5355\u884C"
    );
  });
  if (barcodePreviewFailure) failures.push(barcodePreviewFailure);
  const retailPriceFailure = await runTest("Coming Soon \u5546\u54C1\u884C\u5E94\u663E\u793A\u5EFA\u8BAE\u96F6\u552E\u4EF7\u4E14\u7F3A\u5931\u65F6\u4E3A\u7A7A", () => {
    assert(
      comingSoonSource.includes("formatComingSoonRetailPrice") && comingSoonSource.includes("product.retailPrice") && comingSoonSource.includes("t('shop.rrp'") && comingSoonSource.includes("typeof price !== 'number'") && comingSoonSource.includes("return ''"),
      "Coming Soon \u5546\u54C1\u884C\u672A\u663E\u793A\u5EFA\u8BAE\u96F6\u552E\u4EF7\uFF0C\u6216\u7F3A\u5931\u96F6\u552E\u4EF7\u65F6\u4E0D\u662F\u7A7A\u5B57\u7B26\u4E32"
    );
  });
  if (retailPriceFailure) failures.push(retailPriceFailure);
  const fullItemNumberFailure = await runTest("Coming Soon \u8D27\u53F7\u5E94\u5B8C\u6574\u663E\u793A\u4E0D\u7701\u7565\u4E0D\u6362\u884C", () => {
    const itemNumberBlock = extractCssBlock(".shop-coming-soon-item-number-value");
    assert(
      itemNumberBlock.includes("white-space: nowrap") && !itemNumberBlock.includes("text-overflow") && !itemNumberBlock.includes("overflow: hidden"),
      "Coming Soon \u8D27\u53F7\u503C\u6837\u5F0F\u4ECD\u5305\u542B\u7701\u7565\u6216\u9690\u85CF\u7B56\u7565"
    );
  });
  if (fullItemNumberFailure) failures.push(fullItemNumberFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("shopComingSoonPerformance.logic.test: ok");
}
await main();
