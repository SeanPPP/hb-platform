// src/pages/Warehouse/StoreOrders/productPickerModal.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/Warehouse/StoreOrders/productPickerSupplierFilter.ts
function matchesProductPickerSupplierOption(input, option) {
  const keyword = input.trim().toLowerCase();
  if (!keyword) {
    return true;
  }
  return [option?.supplierCode, option?.supplierName, option?.shopNumber].filter((value) => Boolean(value)).some((value) => value.toLowerCase().includes(keyword));
}

// src/pages/Warehouse/StoreOrders/productPickerModal.logic.test.ts
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
var productPickerSource = detailSource.slice(
  detailSource.indexOf("function ProductPickerModal"),
  detailSource.indexOf("function BatchEditModal")
);
async function main() {
  const failures = [];
  const supplierFilterFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5E94\u63D0\u4F9B\u56FD\u5185\u4F9B\u5E94\u5546\u7B5B\u9009\u4E0B\u62C9", () => {
    assert(
      detailSource.includes("placeholder={t('storeOrders.detail.filterDomesticSupplier', '\u7B5B\u9009\u56FD\u5185\u4F9B\u5E94\u5546')}") && detailSource.includes("showSearch") && detailSource.includes('optionFilterProp="label"') && detailSource.includes("allowClear"),
      "\u5546\u54C1\u5F39\u7A97\u7F3A\u5C11\u5E26\u4E2D\u6587\u515C\u5E95\u7684\u56FD\u5185\u4F9B\u5E94\u5546\u7B5B\u9009\u4E0B\u62C9"
    );
  });
  if (supplierFilterFailure) failures.push(supplierFilterFailure);
  const supplierShopNumberFilterFailure = await runTest("\u56FD\u5185\u4F9B\u5E94\u5546\u4E0B\u62C9\u5E94\u6309\u7F16\u7801\u3001\u540D\u79F0\u548C\u5E97\u94FA\u53F7\u672C\u5730\u641C\u7D22", () => {
    const supplierOption = {
      label: "\u590D\u6D3B\u8282\u5934\u6263-6022 (022)",
      value: "CN-FILTER",
      supplierCode: "CN-FILTER",
      supplierName: "\u590D\u6D3B\u8282\u5934\u6263-6022",
      shopNumber: "022"
    };
    assert(matchesProductPickerSupplierOption(" cn-filter ", supplierOption), "\u4F9B\u5E94\u5546\u7F16\u7801\u641C\u7D22\u5E94\u5FFD\u7565\u524D\u540E\u7A7A\u767D\u548C\u5927\u5C0F\u5199");
    assert(matchesProductPickerSupplierOption("\u590D\u6D3B\u8282", supplierOption), "\u4F9B\u5E94\u5546\u540D\u79F0\u641C\u7D22\u5E94\u547D\u4E2D");
    assert(matchesProductPickerSupplierOption("022", supplierOption), "\u5E97\u94FA\u53F7\u641C\u7D22\u5E94\u547D\u4E2D");
    assert(!matchesProductPickerSupplierOption("\u4E0D\u5B58\u5728", supplierOption), "\u65E0\u5173\u5173\u952E\u5B57\u4E0D\u5E94\u547D\u4E2D");
    assert(
      productPickerSource.includes("supplierCode: item.supplierCode") && productPickerSource.includes("supplierName: item.supplierName") && productPickerSource.includes("shopNumber: item.shopNumber") && productPickerSource.includes("filterOption={(input, option) =>") && productPickerSource.includes("matchesProductPickerSupplierOption(input"),
      "\u56FD\u5185\u4F9B\u5E94\u5546\u4E0B\u62C9\u672A\u628A\u4F9B\u5E94\u5546\u7F16\u7801\u3001\u540D\u79F0\u548C\u5E97\u94FA\u53F7\u90FD\u7EB3\u5165\u672C\u5730\u641C\u7D22"
    );
  });
  if (supplierShopNumberFilterFailure) failures.push(supplierShopNumberFilterFailure);
  const queryFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u641C\u7D22\u5E94\u540C\u65F6\u652F\u6301\u8D27\u53F7\u4E0E\u5546\u54C1\u540D\u79F0", () => {
    assert(
      detailSource.includes("const trimmedKeyword = nextKeyword.trim()") && detailSource.includes("itemNumber: trimmedKeyword || undefined") && detailSource.includes("productName: trimmedKeyword || undefined") && detailSource.includes("supplierCode: nextSupplierCode || undefined") && detailSource.includes("excludeOrderGUID: orderGUID") && !detailSource.includes("excludeExistingWarehouseProducts: true"),
      "\u5546\u54C1\u5F39\u7A97\u641C\u7D22\u672A\u540C\u65F6\u4F20\u9012\u8D27\u53F7/\u6761\u7801\u4E0E\u5546\u54C1\u540D\u79F0\u67E5\u8BE2\uFF0C\u6216\u7F3A\u5C11\u56FD\u5185\u4F9B\u5E94\u5546/\u6392\u9664\u6761\u4EF6"
    );
  });
  if (queryFailure) failures.push(queryFailure);
  const supplierStateFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5173\u95ED\u65F6\u5E94\u91CD\u7F6E\u4F9B\u5E94\u5546\u7B5B\u9009\u72B6\u6001", () => {
    assert(
      detailSource.includes("const [supplierCode, setSupplierCode] = useState<string>()") && detailSource.includes("setSupplierCode(undefined)") && detailSource.includes("setSupplierOptions([])"),
      "\u5546\u54C1\u5F39\u7A97\u5173\u95ED\u540E\u672A\u91CD\u7F6E\u4F9B\u5E94\u5546\u7B5B\u9009\u72B6\u6001"
    );
  });
  if (supplierStateFailure) failures.push(supplierStateFailure);
  const supplierColumnFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5E94\u663E\u793A\u4F9B\u5E94\u5546\u540D\u79F0\u5217", () => {
    assert(
      detailSource.includes("title: t('column.supplierName', '\u4F9B\u5E94\u5546\u540D\u79F0')") && detailSource.includes("dataIndex: 'domesticSupplierName'") && detailSource.includes("record.domesticSupplierCode || '--'"),
      "\u5546\u54C1\u5F39\u7A97\u7F3A\u5C11\u56FD\u5185\u4F9B\u5E94\u5546\u540D\u79F0\u5217\u6216\u7F16\u7801\u515C\u5E95"
    );
  });
  if (supplierColumnFailure) failures.push(supplierColumnFailure);
  const paginationFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u9ED8\u8BA4\u5206\u9875\u5E94\u4E3A 100 \u4E14\u53EA\u5141\u8BB8 50/100/500", () => {
    assert(
      detailSource.includes("const PRODUCT_PICKER_DEFAULT_PAGE_SIZE = 100") && detailSource.includes("const PRODUCT_PICKER_PAGE_SIZE_OPTIONS = ['50', '100', '500']") && productPickerSource.includes("useState(PRODUCT_PICKER_DEFAULT_PAGE_SIZE)") && productPickerSource.includes("setPageSize(PRODUCT_PICKER_DEFAULT_PAGE_SIZE)") && productPickerSource.includes("pageSizeOptions: PRODUCT_PICKER_PAGE_SIZE_OPTIONS"),
      "\u5546\u54C1\u5F39\u7A97\u5206\u9875\u9ED8\u8BA4\u503C\u6216\u9875\u5BB9\u91CF\u9009\u9879\u4E0D\u7B26\u5408 100 / 50-100-500 \u8981\u6C42"
    );
  });
  if (paginationFailure) failures.push(paginationFailure);
  const compactTableFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u8868\u683C\u5E94\u56FA\u5B9A\u5E03\u5C40\u4E14\u4E0D\u914D\u7F6E\u6A2A\u5411\u6EDA\u52A8", () => {
    assert(
      productPickerSource.includes('className="store-order-product-picker-table"') && productPickerSource.includes('tableLayout="fixed"') && productPickerSource.includes("scroll={{ y: 440 }}") && !productPickerSource.includes("scroll={{ x:") && productPickerSource.includes('className="store-order-product-picker-modal"'),
      "\u5546\u54C1\u5F39\u7A97\u8868\u683C\u7F3A\u5C11\u56FA\u5B9A\u5E03\u5C40/\u4E13\u7528 class\uFF0C\u6216\u4ECD\u914D\u7F6E\u4E86\u6A2A\u5411 scroll.x"
    );
  });
  if (compactTableFailure) failures.push(compactTableFailure);
  const compactRendererFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5173\u952E\u5217\u5E94\u4F7F\u7528\u7D27\u51D1\u56FE\u7247\u3001\u590D\u5236\u56FE\u6807\u3001\u7A84\u6570\u5B57\u8F93\u5165\u548C\u4EF7\u683C $ \u524D\u7F00", () => {
    assert(
      productPickerSource.includes("width={32}") && productPickerSource.includes("height={32}") && productPickerSource.includes("icon={<CopyOutlined />}") && productPickerSource.includes('className="store-order-picker-copy-button"') && productPickerSource.includes('className="store-order-picker-two-line"') && productPickerSource.includes('className="store-order-picker-number-input"') && productPickerSource.includes('className="store-order-picker-number-input store-order-picker-price-input"') && productPickerSource.includes('prefix="$"') && productPickerSource.includes("formatCurrencyAmount(value)") && productPickerSource.includes("style={{ width: 58 }}") && productPickerSource.includes("style={{ width: 70 }}"),
      "\u5546\u54C1\u5F39\u7A97\u672A\u538B\u7F29\u56FE\u7247\u3001\u590D\u5236\u6309\u94AE\u3001\u6587\u672C\u5217\u3001\u6570\u5B57\u8F93\u5165\u6846\u6216\u4EF7\u683C $ \u524D\u7F00"
    );
  });
  if (compactRendererFailure) failures.push(compactRendererFailure);
  const abortFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5546\u54C1\u8BF7\u6C42\u5E94\u652F\u6301\u53D6\u6D88\u65E7\u8BF7\u6C42", () => {
    assert(
      productPickerSource.includes("const productRequestControllerRef = useRef<AbortController | null>(null)") && productPickerSource.includes("productRequestControllerRef.current?.abort()") && productPickerSource.includes("const currentController = new AbortController()") && productPickerSource.includes("currentController.signal") && productPickerSource.includes("if (isAbortError(error))") && productPickerSource.includes("productRequestControllerRef.current !== currentController"),
      "\u5546\u54C1\u5F39\u7A97\u5546\u54C1\u8BF7\u6C42\u7F3A\u5C11 AbortController \u53D6\u6D88\u6216\u65E7\u8BF7\u6C42\u9632\u56DE\u5199"
    );
  });
  if (abortFailure) failures.push(abortFailure);
  const supplierLazyFailure = await runTest("\u56FD\u5185\u4F9B\u5E94\u5546\u4E0B\u62C9\u5E94\u9996\u6B21\u5C55\u5F00\u624D\u5F02\u6B65\u52A0\u8F7D\u5E76\u53EF\u5173\u95ED\u53D6\u6D88", () => {
    assert(
      productPickerSource.includes("const supplierRequestControllerRef = useRef<AbortController | null>(null)") && productPickerSource.includes("const supplierOptionsLoadedRef = useRef(false)") && productPickerSource.includes("getActiveChinaSuppliers(currentController.signal)") && productPickerSource.includes("onOpenChange={(visible) =>") && productPickerSource.includes("void loadSupplierOptions()") && productPickerSource.includes("return") && productPickerSource.includes("supplierRequestControllerRef.current?.abort()") && productPickerSource.includes("supplierRequestControllerRef.current = null") && productPickerSource.includes("setSupplierLoading(false)") && !productPickerSource.includes("void loadSupplierOptions()\n    void loadProducts"),
      "\u56FD\u5185\u4F9B\u5E94\u5546\u4E0B\u62C9\u672A\u6309\u9996\u6B21\u5C55\u5F00\u61D2\u52A0\u8F7D\uFF0C\u6216\u5173\u95ED\u65F6\u4E0D\u80FD\u53D6\u6D88\u672A\u5B8C\u6210\u8BF7\u6C42"
    );
  });
  if (supplierLazyFailure) failures.push(supplierLazyFailure);
  const columnFilterFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u8868\u5934\u5E94\u63D0\u4F9B\u670D\u52A1\u7AEF\u5217\u8FC7\u6EE4\u548C\u6392\u5E8F\u72B6\u6001", () => {
    assert(
      productPickerSource.includes("const [productSortBy, setProductSortBy] = useState('Default')") && productPickerSource.includes("const [productSortDescending, setProductSortDescending] = useState(false)") && productPickerSource.includes("const [columnFilters, setColumnFilters] = useState<StoreOrderProductColumnFilters>({})") && productPickerSource.includes("cleanProductPickerColumnFilters(nextColumnFilters)") && productPickerSource.includes("columnFilters: cleanedColumnFilters") && productPickerSource.includes("productTextFilterProps('itemNumber'") && productPickerSource.includes("productTextFilterProps('productName'") && productPickerSource.includes("productTextFilterProps('supplierKeyword'") && productPickerSource.includes("productTextFilterProps('barcode'") && productPickerSource.includes("productNumberFilterProps({ min: 'stockQuantityMin', max: 'stockQuantityMax' })") && productPickerSource.includes("productNumberFilterProps({ min: 'minOrderQuantityMin', max: 'minOrderQuantityMax' })") && productPickerSource.includes("productNumberFilterProps({ min: 'importPriceMin', max: 'importPriceMax' })") && productPickerSource.includes("sortOrder: productSortOrder('importPrice')"),
      "\u5546\u54C1\u5F39\u7A97\u7F3A\u5C11\u5217\u8FC7\u6EE4/\u6392\u5E8F\u72B6\u6001\u3001\u8BF7\u6C42\u53C2\u6570\u6216\u6838\u5FC3\u5217\u8868\u5934\u8FC7\u6EE4\u914D\u7F6E"
    );
  });
  if (columnFilterFailure) failures.push(columnFilterFailure);
  const columnFilterDraftIsolationFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u8868\u5934\u672A\u5E94\u7528\u8349\u7A3F\u4E0D\u5E94\u8DE8\u5217\u63D0\u4EA4", () => {
    assert(
      detailSource.includes("function ProductPickerTextFilterDropdown") && detailSource.includes("const [draft, setDraft] = useState(value ??") && productPickerSource.includes("applyProductColumnFilterPatch({ [key]: value }, nextConfirm)") && productPickerSource.includes("\u6BCF\u4E2A\u8868\u5934\u5F39\u5C42\u53EA\u63D0\u4EA4\u81EA\u5DF1\u7684 patch") && !detailSource.includes("columnFilterDrafts"),
      "\u5546\u54C1\u5F39\u7A97\u4ECD\u53EF\u80FD\u628A\u5176\u4ED6\u5217\u672A\u5E94\u7528\u7684\u7B5B\u9009\u8349\u7A3F\u4E00\u8D77\u63D0\u4EA4"
    );
  });
  if (columnFilterDraftIsolationFailure) failures.push(columnFilterDraftIsolationFailure);
  const preservedSelectionFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u8DE8\u9875\u9009\u62E9\u5E94\u7F13\u5B58\u5DF2\u9009\u5546\u54C1\u5B9E\u4F53", () => {
    assert(
      productPickerSource.includes("const [selectedProductsByCode, setSelectedProductsByCode] = useState<Record<string, StoreOrderProductItem>>({})") && productPickerSource.includes("preserveSelectedRowKeys: true") && productPickerSource.includes("nextSelectedRows.forEach((product) =>") && productPickerSource.includes("selectedProductsByCode[String(key)] ?? products.find") && productPickerSource.includes("setSelectedProductsByCode({})"),
      "\u5546\u54C1\u5F39\u7A97\u8DE8\u9875/\u8FC7\u6EE4\u540E\u9009\u4E2D\u5546\u54C1\u6CA1\u6709\u5B9E\u4F53\u7F13\u5B58\uFF0C\u786E\u8BA4\u6DFB\u52A0\u53EF\u80FD\u4E22\u5931\u975E\u5F53\u524D\u9875\u5546\u54C1"
    );
  });
  if (preservedSelectionFailure) failures.push(preservedSelectionFailure);
  const tableChangeFailure = await runTest("\u5546\u54C1\u5F39\u7A97\u5206\u9875\u6392\u5E8F\u5E94\u7EDF\u4E00\u8D70\u8868\u683C onChange \u5E76\u5FFD\u7565\u672C\u5730 filter action", () => {
    assert(
      productPickerSource.includes("if (extra.action === 'filter')") && productPickerSource.includes("extra.action === 'sort' ? 1 : pagination.current || 1") && productPickerSource.includes("nextSortBy = 'Default'") && productPickerSource.includes("sortDescending: nextSortDescending") && !productPickerSource.includes("onChange: (nextPage, nextPageSize) =>\n              void loadProducts"),
      "\u5546\u54C1\u5F39\u7A97\u5206\u9875/\u6392\u5E8F\u6CA1\u6709\u7EDF\u4E00\u4F7F\u7528 Table onChange\uFF0C\u6216\u672A\u907F\u514D filter action \u91CD\u590D\u89E6\u53D1"
    );
  });
  if (tableChangeFailure) failures.push(tableChangeFailure);
  const quickAddFailure = await runTest("\u5FEB\u901F\u6DFB\u52A0\u8BF7\u6C42\u4ECD\u4FDD\u6301\u539F\u59CB\u67E5\u8BE2\u7ED3\u6784", () => {
    assert(
      detailSource.includes("const result = await getStoreOrderProducts({") && detailSource.includes("itemNumber: normalizedItemNumber") && detailSource.includes("includeInactiveWarehouseProducts: true") && detailSource.includes("pageNumber: 1") && detailSource.includes("pageSize: 50") && detailSource.includes("sortBy: 'Default'"),
      "\u5FEB\u901F\u6DFB\u52A0\u5546\u54C1\u67E5\u8BE2\u7ED3\u6784\u88AB\u610F\u5916\u6539\u52A8"
    );
  });
  if (quickAddFailure) failures.push(quickAddFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25\\n- ${failures.join("\\n- ")}`);
  }
  console.log("productPickerModal.logic.test: ok");
}
await main();
