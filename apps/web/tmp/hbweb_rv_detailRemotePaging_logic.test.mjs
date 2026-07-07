// src/pages/Warehouse/StoreOrders/detailRemotePaging.logic.test.ts
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
var detailFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Detail.tsx");
var detailSource = readFileSync(detailFile, "utf8");
async function main() {
  const failures = [];
  const loadStateFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u663E\u5F0F\u533A\u5206 idle/loading/loaded/notFound/error \u72B6\u6001", () => {
    assert(
      detailSource.includes("type DetailLoadStatus = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error'"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u58F0\u660E\u8FDC\u7A0B\u52A0\u8F7D\u72B6\u6001\u673A"
    );
  });
  if (loadStateFailure) failures.push(loadStateFailure);
  const remoteQueryFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u5C06\u5206\u9875\u7B5B\u9009\u6392\u5E8F\u4F5C\u4E3A\u8FDC\u7A0B query \u53D1\u9001", () => {
    assert(
      detailSource.includes("pageNumber: detailPage") && detailSource.includes("pageSize: detailPageSize") && detailSource.includes("keyword: detailItemFilter.trim() || undefined") && detailSource.includes("statFilter: detailStatFilter === 'all' ? undefined : detailStatFilter") && detailSource.includes("columnFilters: cleanedDetailColumnFilters") && detailSource.includes("sortBy: detailSortField || undefined") && detailSource.includes("sortDescending: detailSortField ? detailSortOrder === 'descend' : undefined"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u628A\u5206\u9875\u7B5B\u9009\u6392\u5E8F\u62FC\u5230\u8FDC\u7A0B\u660E\u7EC6\u67E5\u8BE2\u91CC"
    );
  });
  if (remoteQueryFailure) failures.push(remoteQueryFailure);
  const defaultLocationSortFailure = await runTest("\u8BE6\u60C5\u9875\u9ED8\u8BA4\u6392\u5E8F\u5E94\u6309\u8D27\u4F4D\u5347\u5E8F\u5E76\u63D0\u4F9B\u9ED8\u8BA4\u6392\u5E8F\u6309\u94AE", () => {
    assert(
      detailSource.includes("useState<DetailSortField>('locationCode')") && detailSource.includes("useState<SortOrder>('ascend')") && detailSource.includes("const handleResetDetailDefaultSort = () =>") && detailSource.includes("setDetailSortField('locationCode')") && detailSource.includes("setDetailSortOrder('ascend')") && detailSource.includes("t('storeOrders.detail.defaultSort')") && detailSource.includes("icon={<SortAscendingOutlined />}"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u9ED8\u8BA4\u6309\u8D27\u4F4D\u5347\u5E8F\uFF0C\u6216\u7F3A\u5C11\u6062\u590D\u9ED8\u8BA4\u6392\u5E8F\u6309\u94AE"
    );
  });
  if (defaultLocationSortFailure) failures.push(defaultLocationSortFailure);
  const detailColumnFilterFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u5173\u952E\u5217\u5E94\u652F\u6301\u5217\u5934\u8FC7\u6EE4\u548C\u670D\u52A1\u7AEF\u6392\u5E8F\u5B57\u6BB5", () => {
    const requiredSortFields = [
      "'itemNumber'",
      "'productName'",
      "'barcode'",
      "'locationCode'",
      "'quantity'",
      "'allocQuantity'",
      "'importPrice'",
      "'isActive'"
    ];
    assert(
      detailSource.includes("useState<StoreOrderDetailColumnFilters>({})") && detailSource.includes("cleanStoreOrderDetailColumnFilters(detailColumnFilters)") && requiredSortFields.every((field) => detailSource.includes(field)) && detailSource.includes("detailTextFilterProps('itemNumber'") && detailSource.includes("detailTextFilterProps('productName'") && detailSource.includes("detailTextFilterProps('barcode'") && detailSource.includes("detailTextFilterProps('locationCode'") && detailSource.includes("detailNumberFilterProps({ min: 'quantityMin', max: 'quantityMax' })") && detailSource.includes("detailNumberFilterProps({ min: 'allocQuantityMin', max: 'allocQuantityMax' })") && detailSource.includes("detailNumberFilterProps({ min: 'importPriceMin', max: 'importPriceMax' })") && detailSource.includes("detailStatusFilterProps()") && detailSource.includes("isStoreOrderDetailSortField(field)") && detailSource.includes("applyDetailColumnFilters") && detailSource.includes("setSelectedLineKeys([])") && detailSource.includes("setDetailPage(1)"),
      "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u5173\u952E\u5217\u5C1A\u672A\u5B8C\u6574\u63A5\u5165\u5217\u5934\u8FC7\u6EE4\u3001\u6392\u5E8F\u767D\u540D\u5355\u548C\u7ED3\u679C\u96C6\u5207\u6362\u4FDD\u62A4"
    );
  });
  if (detailColumnFilterFailure) failures.push(detailColumnFilterFailure);
  const defaultPageSizeFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u9ED8\u8BA4\u6BCF\u9875 200 \u5E76\u53EA\u63D0\u4F9B\u6307\u5B9A\u5206\u9875\u9009\u9879", () => {
    assert(
      detailSource.includes("const STORE_ORDER_DETAIL_DEFAULT_PAGE_SIZE = 200") && detailSource.includes("const STORE_ORDER_DETAIL_PAGE_SIZE_OPTIONS = ['50', '100', '200', '500', '1000']") && detailSource.includes("useState(STORE_ORDER_DETAIL_DEFAULT_PAGE_SIZE)") && detailSource.includes("pageSizeOptions: STORE_ORDER_DETAIL_PAGE_SIZE_OPTIONS") && !detailSource.includes("pageSizeOptions: ['20', '50', '100', '500']"),
      "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u9ED8\u8BA4\u5206\u9875\u6216\u5206\u9875\u9009\u9879\u4E0D\u7B26\u5408 200 / 50-1000 \u8981\u6C42"
    );
  });
  if (defaultPageSizeFailure) failures.push(defaultPageSizeFailure);
  const lazyImageFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u56FE\u7247\u5E94\u4F7F\u7528\u6D4F\u89C8\u5668\u539F\u751F\u61D2\u52A0\u8F7D", () => {
    assert(
      detailSource.includes('loading="lazy"') && detailSource.includes('fallback="data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs="'),
      '\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u56FE\u7247\u5217\u5C1A\u672A\u8BBE\u7F6E loading="lazy"'
    );
  });
  if (lazyImageFailure) failures.push(lazyImageFailure);
  const currentPageDataFailure = await runTest("\u8BE6\u60C5\u8868\u683C\u5E94\u76F4\u63A5\u4F7F\u7528\u670D\u52A1\u7AEF\u5F53\u524D\u9875 items \u4E0E itemsTotal", () => {
    assert(
      detailSource.includes("dataSource={detail.items}") && detailSource.includes("total: detail.itemsTotal ?? detail.items.length") && !detailSource.includes("dataSource={pagedItems}"),
      "\u8BE6\u60C5\u8868\u683C\u4ECD\u5728\u4F7F\u7528\u672C\u5730\u5207\u7247\u5206\u9875\uFF0C\u800C\u4E0D\u662F\u670D\u52A1\u7AEF\u5F53\u524D\u9875\u6570\u636E"
    );
  });
  if (currentPageDataFailure) failures.push(currentPageDataFailure);
  const clearSelectionFailure = await runTest("\u7FFB\u9875\u7B5B\u9009\u6392\u5E8F\u65F6\u5E94\u6E05\u7A7A\u52FE\u9009\u884C", () => {
    assert(
      detailSource.includes("setSelectedLineKeys([])") && detailSource.includes("setDetailPage(nextPage)") && detailSource.includes("extra.action === 'paginate'") && detailSource.includes("extra.action === 'filter'") && detailSource.includes("setDetailItemFilter(event.target.value)") && detailSource.includes("setDetailSortField(field)"),
      "\u7FFB\u9875\u7B5B\u9009\u6392\u5E8F\u65F6\u5C1A\u672A\u7EDF\u4E00\u6E05\u7A7A selectedLineKeys"
    );
  });
  if (clearSelectionFailure) failures.push(clearSelectionFailure);
  const cancelFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u53D6\u6D88\u4E0A\u4E00\u7B14\u8FDB\u884C\u4E2D\u7684\u660E\u7EC6\u8BF7\u6C42", () => {
    assert(
      detailSource.includes("detailRequestControllerRef.current?.abort()") && detailSource.includes("new AbortController()") && detailSource.includes("detailRequestControllerRef.current.signal"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u63A5\u5165\u660E\u7EC6\u8BF7\u6C42\u53D6\u6D88\u903B\u8F91"
    );
  });
  if (cancelFailure) failures.push(cancelFailure);
  const containerCodesFailure = await runTest("\u8D27\u67DC\u9009\u54C1\u5E94\u4F7F\u7528\u8DE8\u9875\u5546\u54C1\u7F16\u7801\u53BB\u91CD", () => {
    assert(
      detailSource.includes("getStoreOrderDetailProductCodes") && detailSource.includes("alreadySelectedCodes={containerExistingProductCodes}") && detailSource.includes("handleOpenContainerPicker") && detailSource.includes("setContainerPickerOpen(false)") && !detailSource.includes("alreadySelectedCodes={detail.items.map((item) => item.productCode)}") && !detailSource.includes("detail.items.map((item) => item.productCode)"),
      "\u8D27\u67DC\u9009\u54C1\u4ECD\u5728\u4F7F\u7528\u5F53\u524D\u9875 items \u505A\u5DF2\u9009\u5546\u54C1\u53BB\u91CD"
    );
  });
  if (containerCodesFailure) failures.push(containerCodesFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("detailRemotePaging.logic.test: ok");
}
await main();
