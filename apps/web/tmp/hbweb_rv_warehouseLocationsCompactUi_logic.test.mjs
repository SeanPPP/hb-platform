// src/pages/Warehouse/Locations/warehouseLocationsCompactUi.logic.test.ts
import { existsSync, readFileSync } from "node:fs";
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
function readCssRule(source, selector) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? "";
}
function readColumnBlock(source, marker) {
  const markerPosition = source.indexOf(marker);
  if (markerPosition < 0) {
    return "";
  }
  const blockStart = source.lastIndexOf("    {", markerPosition);
  const nextBlockStart = source.indexOf("    {", markerPosition + marker.length);
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length);
}
function readNumericValue(source, pattern) {
  const match = source.match(pattern);
  return match ? Number(match[1]) : Number.NaN;
}
var locationsPageFile = path.resolve(process.cwd(), "src/pages/Warehouse/Locations/index.tsx");
var compactCssFile = path.resolve(process.cwd(), "src/pages/Warehouse/Locations/compact.css");
var locationTypesFile = path.resolve(process.cwd(), "src/types/location.ts");
var locationServiceFile = path.resolve(process.cwd(), "src/services/locationService.ts");
var zhLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var enLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
var packageFile = path.resolve(process.cwd(), "package.json");
var locationsPageSource = readFileSync(locationsPageFile, "utf8");
var locationTypesSource = readFileSync(locationTypesFile, "utf8");
var locationServiceSource = readFileSync(locationServiceFile, "utf8");
var zhLocaleSource = readFileSync(zhLocaleFile, "utf8");
var enLocaleSource = readFileSync(enLocaleFile, "utf8");
var packageSource = readFileSync(packageFile, "utf8");
var compactCssSource = existsSync(compactCssFile) ? readFileSync(compactCssFile, "utf8") : "";
async function main() {
  const failures = [];
  const typeFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u5546\u54C1\u7C7B\u578B\u5E94\u5305\u542B\u5546\u54C1\u6761\u7801\u5B57\u6BB5", () => {
    assert(locationTypesSource.includes("productBarcode?: string"), "LocationProduct \u7F3A\u5C11 productBarcode \u5B57\u6BB5");
  });
  if (typeFailure) failures.push(typeFailure);
  const normalizeFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u5217\u8868\u5E94\u517C\u5BB9\u591A\u79CD\u5546\u54C1\u6761\u7801\u8FD4\u56DE\u5B57\u6BB5", () => {
    assert(locationServiceSource.includes("normalizeLocationProduct"), "locationService \u7F3A\u5C11\u5546\u54C1 normalize helper");
    assert(locationServiceSource.includes("normalizeLocationItem"), "locationService \u7F3A\u5C11\u6807\u7B7E normalize helper");
    assert(locationServiceSource.includes("productBarcode ??"), "normalize \u5E94\u4F18\u5148\u8BFB\u53D6 productBarcode");
    assert(locationServiceSource.includes("ProductBarcode"), "normalize \u5E94\u517C\u5BB9 ProductBarcode");
    assert(locationServiceSource.includes("raw.barcode"), "normalize \u5E94\u517C\u5BB9 barcode");
    assert(locationServiceSource.includes("raw.Barcode"), "normalize \u5E94\u517C\u5BB9 Barcode");
    assert(locationServiceSource.includes("raw.Products"), "normalize \u5E94\u517C\u5BB9 Products \u5546\u54C1\u6570\u7EC4");
    assert(locationServiceSource.includes("items: (data?.items ?? []).map(normalizeLocationItem)"), "\u5217\u8868\u8FD4\u56DE\u5E94\u7EDF\u4E00 normalize");
  });
  if (normalizeFailure) failures.push(normalizeFailure);
  const hqSyncServiceFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u670D\u52A1\u5E94\u987A\u5E8F\u589E\u91CF\u540C\u6B65\u8D27\u4F4D\u548C\u5546\u54C1\u8D27\u4F4D", () => {
    const serviceFunctionStart = locationServiceSource.indexOf("export async function syncLocationsFromHq");
    assert(serviceFunctionStart >= 0, "locationService \u7F3A\u5C11 syncLocationsFromHq");
    const serviceFunction = locationServiceSource.slice(serviceFunctionStart);
    const locationsEndpointPosition = serviceFunction.indexOf("/api/react/v1/sync/locations-incremental");
    const productLocationsEndpointPosition = serviceFunction.indexOf("/api/react/v1/sync/product-locations-incremental");
    assert(locationsEndpointPosition >= 0, "\u8D27\u4F4D\u540C\u6B65\u5E94\u8C03\u7528 locations-incremental");
    assert(productLocationsEndpointPosition >= 0, "\u5546\u54C1\u8D27\u4F4D\u540C\u6B65\u5E94\u8C03\u7528 product-locations-incremental");
    assert(
      locationsEndpointPosition < productLocationsEndpointPosition,
      "\u5546\u54C1\u8D27\u4F4D\u540C\u6B65\u5FC5\u987B\u5728\u8D27\u4F4D\u540C\u6B65\u4E4B\u540E\u6267\u884C"
    );
    assert(serviceFunction.includes("locationResult") && serviceFunction.includes("productLocationResult"), "\u5E94\u8FD4\u56DE\u4E24\u6BB5\u540C\u6B65\u7ED3\u679C");
  });
  if (hqSyncServiceFailure) failures.push(hqSyncServiceFailure);
  const pageWiringFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u9875\u5E94\u6302\u8F7D\u5C40\u90E8\u7D27\u51D1\u8868\u683C\u6837\u5F0F\u548C\u5546\u54C1\u6761\u7801\u5217", () => {
    assert(locationsPageSource.includes("import './compact.css'"), "\u9875\u9762\u5E94\u5F15\u5165\u5C40\u90E8 compact.css");
    assert(locationsPageSource.includes("CloudSyncOutlined"), "\u9875\u9762\u5E94\u5F15\u5165 HQ \u540C\u6B65\u56FE\u6807");
    assert(locationsPageSource.includes('className="warehouse-locations-compact-table"'), "Table \u7F3A\u5C11\u5C40\u90E8\u7D27\u51D1 class");
    assert(locationsPageSource.includes('size="small"'), "Table \u5E94\u4F7F\u7528 small \u5C3A\u5BF8");
    assert(locationsPageSource.includes("title: t('column.productBarcode')"), "\u8868\u683C\u7F3A\u5C11\u5546\u54C1\u6761\u7801\u5217");
    assert(locationsPageSource.includes("renderCopyableProductBarcodes"), "\u5546\u54C1\u6761\u7801\u5217\u5E94\u8D70\u4E13\u5C5E\u53EF\u590D\u5236\u6E32\u67D3 helper");
    assert(locationsPageSource.includes("renderLocationProductName"), "\u5546\u54C1\u540D\u79F0\u5E94\u8D70\u4E24\u884C\u5C55\u793A helper");
  });
  if (pageWiringFailure) failures.push(pageWiringFailure);
  const hqSyncPageFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u9875\u5E94\u63D0\u4F9B\u4E00\u952E\u4ECEHQ\u66F4\u65B0\u8D27\u4F4D\u5165\u53E3", () => {
    assert(locationsPageSource.includes("syncLocationsFromHq"), "\u9875\u9762\u5E94\u8C03\u7528 syncLocationsFromHq");
    assert(locationsPageSource.includes("const [syncingFromHq, setSyncingFromHq] = useState(false)"), "\u9875\u9762\u5E94\u7EF4\u62A4 HQ \u540C\u6B65 loading \u72B6\u6001");
    assert(locationsPageSource.includes("const canSyncLocationsFromHq = access.isAdmin || access.isWarehouseManager"), "\u9875\u9762\u5E94\u6309\u540E\u7AEF Admin/WarehouseManager \u89D2\u8272\u663E\u793A\u540C\u6B65\u6309\u94AE");
    assert(locationsPageSource.includes("const handleSyncFromHq = () => {"), "\u9875\u9762\u7F3A\u5C11 HQ \u540C\u6B65\u5904\u7406\u51FD\u6570");
    assert(locationsPageSource.includes("Modal.confirm"), "HQ \u540C\u6B65\u5E94\u4E8C\u6B21\u786E\u8BA4");
    assert(locationsPageSource.includes("setSyncingFromHq(true)"), "\u786E\u8BA4\u540C\u6B65\u540E\u5E94\u8FDB\u5165 loading \u72B6\u6001");
    assert(locationsPageSource.includes("await syncLocationsFromHq()"), "\u9875\u9762\u5E94\u7B49\u5F85\u4E00\u952E\u540C\u6B65\u5B8C\u6210");
    assert(locationsPageSource.includes("await loadDataWithColumnFilters(1, pageSize)"), "\u540C\u6B65\u6210\u529F\u540E\u5E94\u5237\u65B0\u7B2C\u4E00\u9875");
    assert(locationsPageSource.includes("loading={syncingFromHq}"), "\u540C\u6B65\u6309\u94AE\u5E94\u7ED1\u5B9A loading \u72B6\u6001");
    assert(locationsPageSource.includes("disabled={syncingFromHq || loading}"), "\u540C\u6B65\u6309\u94AE\u5E94\u5728\u540C\u6B65\u6216\u5217\u8868\u52A0\u8F7D\u4E2D\u7981\u7528");
    assert(locationsPageSource.includes("t('warehouseLocations.syncFromHq'"), "\u540C\u6B65\u6309\u94AE\u5E94\u4F7F\u7528\u4ED3\u5E93\u6807\u7B7E\u6587\u6848");
    assert(locationsPageSource.includes("t('warehouseLocations.syncFromHqSuccessTitle'"), "\u540C\u6B65\u6210\u529F\u5E94\u5C55\u793A\u4E13\u5C5E\u7ED3\u679C\u6807\u9898");
  });
  if (hqSyncPageFailure) failures.push(hqSyncPageFailure);
  const barcodeColumnFailure = await runTest("\u5546\u54C1\u6761\u7801\u5217\u5E94\u663E\u793A\u6587\u672C\u548C\u590D\u5236\u56FE\u6807\u4E14\u4E0D\u56DE\u9000\u8D27\u53F7", () => {
    const barcodeColumn = readColumnBlock(locationsPageSource, "title: t('column.productBarcode')");
    assert(barcodeColumn.includes("renderCopyableProductBarcodes(record.products, t)"), "\u5546\u54C1\u6761\u7801\u5217\u672A\u7ED1\u5B9A\u5546\u54C1\u6761\u7801\u6E32\u67D3");
    assert(locationsPageSource.includes("product.productBarcode"), "\u5546\u54C1\u6761\u7801 helper \u5E94\u8BFB\u53D6 productBarcode");
    assert(locationsPageSource.includes("<BarcodePreview"), "\u5546\u54C1\u6761\u7801\u5217\u5E94\u663E\u793A\u6761\u7801\u56FE");
    assert(locationsPageSource.includes('className="warehouse-locations-product-barcode-preview"'), "\u5546\u54C1\u6761\u7801\u5217\u5E94\u4F7F\u7528\u4E13\u5C5E\u6761\u7801\u56FE\u6837\u5F0F");
    assert(!locationsPageSource.includes("product.productImage"), "\u5546\u54C1\u6761\u7801\u5217\u4E0D\u80FD\u663E\u793A\u73B0\u5B9E\u5546\u54C1\u56FE");
    assert(!locationsPageSource.includes("field: 'itemNumber' | 'productName' | 'productBarcode'"), "\u5546\u54C1\u6761\u7801\u4E0D\u5E94\u590D\u7528\u4F1A\u6DF7\u6DC6\u8D27\u53F7\u7684\u901A\u7528\u5B57\u6BB5\u53C2\u6570");
    assert(locationsPageSource.includes('className="warehouse-locations-barcode-cell warehouse-locations-nowrap"'), "\u5546\u54C1\u6761\u7801\u6587\u672C\u5E94\u4F7F\u7528 nowrap class");
    assert(locationsPageSource.includes('className="warehouse-locations-copyable-cell warehouse-locations-nowrap"'), "\u8D27\u53F7\u590D\u5236\u5185\u5BB9\u5E94\u4F7F\u7528 nowrap class");
    assert(locationsPageSource.includes('className="warehouse-locations-copyable-content"'), "\u8D27\u53F7\u5185\u5BB9\u533A\u5E94\u9650\u5236\u5728\u5355\u5143\u683C\u5185");
    assert(locationsPageSource.includes('className="warehouse-locations-barcode-content"'), "\u5546\u54C1\u6761\u7801\u5185\u5BB9\u533A\u5E94\u9650\u5236\u5728\u5355\u5143\u683C\u5185");
    assert(locationsPageSource.includes('className="warehouse-locations-copy-button"'), "\u5546\u54C1\u6761\u7801\u590D\u5236\u6309\u94AE\u5E94\u4F7F\u7528\u7D27\u51D1\u56FE\u6807\u6309\u94AE");
    assert(!barcodeColumn.includes("itemNumber"), "\u5546\u54C1\u6761\u7801\u5217\u7F3A\u5931\u65F6\u4E0D\u80FD\u56DE\u9000\u663E\u793A\u8D27\u53F7");
  });
  if (barcodeColumnFailure) failures.push(barcodeColumnFailure);
  const locationBarcodeFailure = await runTest("\u8D27\u4F4D\u4EE3\u7801\u548C\u8D27\u4F4D\u6761\u7801\u5E94\u4FDD\u6301\u6587\u672C\u5B8C\u6574\u663E\u793A", () => {
    const locationCodeColumn = readColumnBlock(locationsPageSource, "dataIndex: 'locationCode'");
    const locationBarcodeColumn = readColumnBlock(locationsPageSource, "dataIndex: 'locationBarcode'");
    const locationCodePosition = locationsPageSource.indexOf("dataIndex: 'locationCode'");
    const locationTypePosition = locationsPageSource.indexOf("dataIndex: 'locationType'");
    const locationBarcodePosition = locationsPageSource.indexOf("dataIndex: 'locationBarcode'");
    assert(locationCodeColumn.includes("textNoWrap"), "\u8D27\u4F4D\u4EE3\u7801\u6761\u7801\u6587\u672C\u5E94\u4FDD\u6301\u5355\u884C");
    assert(locationBarcodeColumn.includes("textNoWrap"), "\u8D27\u4F4D\u6761\u7801\u6587\u672C\u5E94\u4FDD\u6301\u5355\u884C");
    assert(!locationCodeColumn.includes("textMaxWidth"), "\u8D27\u4F4D\u4EE3\u7801\u4E0D\u5E94\u901A\u8FC7 textMaxWidth \u7701\u7565\u9690\u85CF");
    assert(!locationBarcodeColumn.includes("textMaxWidth"), "\u8D27\u4F4D\u6761\u7801\u4E0D\u5E94\u901A\u8FC7 textMaxWidth \u7701\u7565\u9690\u85CF");
    assert(locationCodePosition < locationTypePosition, "\u8D27\u4F4D\u7C7B\u578B\u5217\u5E94\u653E\u5728\u8D27\u4F4D\u4EE3\u7801\u4E4B\u540E");
    assert(locationTypePosition < locationBarcodePosition, "\u8D27\u4F4D\u7C7B\u578B\u5217\u5E94\u653E\u5728\u8D27\u4F4D\u6761\u7801\u4E4B\u524D");
  });
  if (locationBarcodeFailure) failures.push(locationBarcodeFailure);
  const sortingFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u57FA\u7840\u5217\u5E94\u4F7F\u7528\u670D\u52A1\u7AEF\u8FDC\u7A0B\u6392\u5E8F", () => {
    const sortableMarkers = [
      "dataIndex: 'locationCode'",
      "dataIndex: 'locationType'",
      "dataIndex: 'locationBarcode'",
      "dataIndex: 'status'",
      "key: 'usage'",
      "dataIndex: 'updatedAt'",
      "dataIndex: 'updatedBy'"
    ];
    for (const marker of sortableMarkers) {
      const column = readColumnBlock(locationsPageSource, marker);
      assert(column.includes("sorter: true"), `${marker} \u7F3A\u5C11 sorter: true`);
      assert(column.includes("sortOrder:"), `${marker} \u7F3A\u5C11\u53D7\u63A7 sortOrder`);
    }
    const unsortableMarkers = [
      "key: 'index'",
      "key: 'itemNumbers'",
      "key: 'productBarcodes'",
      "key: 'productNames'",
      "key: 'productImages'",
      "key: 'action'"
    ];
    for (const marker of unsortableMarkers) {
      const column = readColumnBlock(locationsPageSource, marker);
      assert(!column.includes("sorter: true"), `${marker} \u4E0D\u5E94\u5F00\u542F\u6392\u5E8F`);
      assert(!column.includes("sortOrder:"), `${marker} \u4E0D\u5E94\u7ED1\u5B9A\u6392\u5E8F\u72B6\u6001`);
    }
    assert(locationsPageSource.includes("LOCATION_SORT_FIELD_MAP"), "\u9875\u9762\u7F3A\u5C11\u8FDC\u7A0B\u6392\u5E8F\u5B57\u6BB5\u767D\u540D\u5355");
    assert(locationsPageSource.includes("Usage: 'Usage'") || locationsPageSource.includes("usage: 'Usage'"), "\u4F7F\u7528\u72B6\u6001\u6392\u5E8F\u5E94\u6620\u5C04\u5230 Usage");
    assert(locationsPageSource.includes("const [sortBy, setSortBy] = useState<LocationSortBy>(DEFAULT_LOCATION_SORT_BY)"), "\u9875\u9762\u7F3A\u5C11 sortBy \u72B6\u6001");
    assert(locationsPageSource.includes("const [sortOrder, setSortOrder] = useState<SortOrder>(DEFAULT_LOCATION_SORT_ORDER)"), "\u9875\u9762\u7F3A\u5C11 sortOrder \u72B6\u6001");
    assert(locationsPageSource.includes("sortDirection: toApiSortDirection(effectiveSortOrder)"), "\u5217\u8868\u8BF7\u6C42\u5E94\u53D1\u9001\u6392\u5E8F\u65B9\u5411");
    assert(locationsPageSource.includes("onChange={handleTableChange}"), "\u8868\u683C\u5E94\u4F7F\u7528\u7EDF\u4E00\u6392\u5E8F\u5206\u9875\u56DE\u8C03");
    assert(locationsPageSource.includes("extra.action === 'sort'"), "\u6392\u5E8F\u56DE\u8C03\u5E94\u8BC6\u522B sort action");
    assert(locationsPageSource.includes("pagination.pageSize || pageSize"), "\u6392\u5E8F\u53D8\u66F4\u5E94\u6CBF\u7528\u5F53\u524D\u9875\u5BB9\u91CF");
    assert(locationsPageSource.includes("nextSortBy") && locationsPageSource.includes("nextSortOrder"), "\u6392\u5E8F\u56DE\u8C03\u5E94\u4F20\u9012\u670D\u52A1\u7AEF\u6392\u5E8F\u5B57\u6BB5\u548C\u65B9\u5411");
    assert(locationServiceSource.includes("SortDirection: params.sortDirection"), "locationService \u5E94\u53D1\u9001\u540E\u7AEF DTO \u7684 SortDirection \u5B57\u6BB5");
    assert(!locationServiceSource.includes("sortDirection: params.sortDirection"), "locationService \u4E0D\u5E94\u53D1\u9001\u5C0F\u5199 sortDirection \u5B57\u6BB5");
  });
  if (sortingFailure) failures.push(sortingFailure);
  const cssFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u7D27\u51D1 CSS \u5E94\u5C40\u90E8\u9650\u5236\u5E76\u4FDD\u7559\u5173\u952E\u5B57\u6BB5", () => {
    const headerRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .ant-table-column-title");
    const nowrapRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-nowrap");
    const twoLineRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-two-line-text");
    const barcodeRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-barcode-cell");
    const copyableContentRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-copyable-content");
    const barcodeContentRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-barcode-content");
    const copyButtonRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-copy-button");
    const barcodePreviewRule = readCssRule(compactCssSource, ".warehouse-locations-compact-table .warehouse-locations-product-barcode-preview canvas");
    assert(/-webkit-line-clamp:\s*2/.test(headerRule), "\u5217\u5934\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C");
    assert(/white-space:\s*nowrap/.test(nowrapRule), "\u5173\u952E\u5B57\u6BB5\u5E94\u4FDD\u6301\u5355\u884C");
    assert(!/overflow:\s*hidden/.test(nowrapRule), "\u5173\u952E\u5B57\u6BB5\u4E0D\u5E94\u88AB\u9690\u85CF\u622A\u65AD");
    assert(/-webkit-line-clamp:\s*2/.test(twoLineRule), "\u5546\u54C1\u540D\u79F0\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C");
    assert(/overflow:\s*hidden/.test(twoLineRule), "\u5546\u54C1\u540D\u79F0\u8D85\u8FC7\u4E24\u884C\u624D\u53EF\u9690\u85CF");
    assert(/white-space:\s*nowrap/.test(barcodeRule), "\u5546\u54C1\u6761\u7801\u5BB9\u5668\u5E94\u4FDD\u6301\u5355\u884C");
    assert(/overflow:\s*hidden/.test(copyableContentRule), "\u8D27\u53F7\u5185\u5BB9\u533A\u5E94\u5728\u5355\u5143\u683C\u5185\u88C1\u5207\uFF0C\u907F\u514D\u590D\u5236\u6309\u94AE\u6EA2\u51FA");
    assert(/overflow:\s*hidden/.test(barcodeContentRule), "\u5546\u54C1\u6761\u7801\u5185\u5BB9\u533A\u5E94\u5728\u5355\u5143\u683C\u5185\u88C1\u5207\uFF0C\u907F\u514D\u590D\u5236\u6309\u94AE\u6EA2\u51FA");
    assert(/min-width:\s*0/.test(copyableContentRule), "\u8D27\u53F7\u5185\u5BB9\u533A\u5E94\u5141\u8BB8 flex \u6536\u7F29");
    assert(/min-width:\s*0/.test(barcodeContentRule), "\u5546\u54C1\u6761\u7801\u5185\u5BB9\u533A\u5E94\u5141\u8BB8 flex \u6536\u7F29");
    assert(/width:\s*20px/.test(copyButtonRule), "\u590D\u5236\u56FE\u6807\u6309\u94AE\u5E94\u6536\u7A84");
    assert(/max-width:\s*74px/.test(barcodePreviewRule), "\u5546\u54C1\u6761\u7801\u56FE\u5E94\u63A7\u5236\u6700\u5927\u5BBD\u5EA6");
    assert(/height:\s*18px/.test(barcodePreviewRule), "\u5546\u54C1\u6761\u7801\u56FE\u9AD8\u5EA6\u5E94\u63A7\u5236\u5230 18px");
    assert(!/^\.warehouse-locations-nowrap/m.test(compactCssSource), "nowrap \u6837\u5F0F\u5FC5\u987B\u9650\u5B9A\u5230\u4ED3\u5E93\u6807\u7B7E\u8868\u683C\u4E0B");
    assert(!/^\.warehouse-locations-two-line-text/m.test(compactCssSource), "\u4E24\u884C\u6837\u5F0F\u5FC5\u987B\u9650\u5B9A\u5230\u4ED3\u5E93\u6807\u7B7E\u8868\u683C\u4E0B");
  });
  if (cssFailure) failures.push(cssFailure);
  const layoutFailure = await runTest("\u4ED3\u5E93\u6807\u7B7E\u8868\u683C\u5217\u5BBD\u5E94\u6309\u7D27\u51D1\u9884\u7B97\u8BBE\u7F6E", () => {
    const indexColumn = readColumnBlock(locationsPageSource, "key: 'index'");
    const itemNumberColumn = readColumnBlock(locationsPageSource, "key: 'itemNumbers'");
    const productBarcodeColumn = readColumnBlock(locationsPageSource, "title: t('column.productBarcode')");
    const productNameColumn = readColumnBlock(locationsPageSource, "key: 'productNames'");
    const imageColumn = readColumnBlock(locationsPageSource, "key: 'productImages'");
    const actionColumn = readColumnBlock(locationsPageSource, "key: 'action'");
    const scrollX = readNumericValue(locationsPageSource, /scroll=\{\{\s*x:\s*(\d+)/);
    assert(readNumericValue(indexColumn, /width:\s*(\d+)/) <= 56, "\u5E8F\u53F7\u5217\u5E94\u538B\u5230 56 \u4EE5\u5185");
    assert(readNumericValue(itemNumberColumn, /width:\s*(\d+)/) <= 118, "\u8D27\u53F7\u5217\u5E94\u538B\u5230 118 \u4EE5\u5185");
    assert(readNumericValue(productBarcodeColumn, /width:\s*(\d+)/) <= 150, "\u5546\u54C1\u6761\u7801\u5217\u5E94\u538B\u5230 150 \u4EE5\u5185");
    assert(readNumericValue(productNameColumn, /width:\s*(\d+)/) >= 190, "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u4FDD\u7559\u81F3\u5C11 190 \u5BBD\u5EA6");
    assert(readNumericValue(imageColumn, /width:\s*(\d+)/) <= 112, "\u56FE\u7247\u5217\u5E94\u538B\u5230 112 \u4EE5\u5185");
    assert(readNumericValue(actionColumn, /width:\s*(\d+)/) <= 132, "\u64CD\u4F5C\u5217\u5E94\u538B\u5230 132 \u4EE5\u5185");
    assert(scrollX >= 1460 && scrollX <= 1500, "scroll.x \u5E94\u8BBE\u7F6E\u5230 1460-1500");
  });
  if (layoutFailure) failures.push(layoutFailure);
  const localeAndScriptFailure = await runTest("\u5546\u54C1\u6761\u7801\u6587\u6848\u548C\u6D4B\u8BD5\u811A\u672C\u5E94\u63A5\u5165\u9879\u76EE", () => {
    assert(zhLocaleSource.includes('"productBarcode": "\u5546\u54C1\u6761\u7801"'), "\u4E2D\u6587\u5217\u540D\u7F3A\u5C11\u5546\u54C1\u6761\u7801");
    assert(enLocaleSource.includes('"productBarcode": "Product Barcode"'), "\u82F1\u6587\u5217\u540D\u7F3A\u5C11 Product Barcode");
    assert(zhLocaleSource.includes('"syncFromHq": "\u4ECEHQ\u66F4\u65B0\u8D27\u4F4D"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u4ECEHQ\u66F4\u65B0\u8D27\u4F4D");
    assert(enLocaleSource.includes('"syncFromHq": "Update Locations from HQ"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11 Update Locations from HQ");
    assert(zhLocaleSource.includes('"syncResultStats": "\u65B0\u589E {{added}}\uFF0C\u66F4\u65B0 {{updated}}\uFF0C\u9519\u8BEF {{errors}}"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u7ED3\u679C\u7EDF\u8BA1");
    assert(enLocaleSource.includes('"syncResultStats": "Added {{added}}, updated {{updated}}, errors {{errors}}"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u7ED3\u679C\u7EDF\u8BA1");
    assert(packageSource.includes('"test:warehouse-locations"'), "package.json \u7F3A\u5C11 test:warehouse-locations \u811A\u672C");
    assert(packageSource.includes("warehouseLocationsCompactUi.logic.test.ts"), "\u6D4B\u8BD5\u811A\u672C\u672A\u8FD0\u884C\u4ED3\u5E93\u6807\u7B7E\u7D27\u51D1 UI \u7EA6\u675F");
    assert(packageSource.includes("locationService.hqSync.test.ts"), "\u6D4B\u8BD5\u811A\u672C\u672A\u8FD0\u884C\u4ED3\u5E93\u6807\u7B7E HQ \u540C\u6B65\u670D\u52A1\u884C\u4E3A\u6D4B\u8BD5");
  });
  if (localeAndScriptFailure) failures.push(localeAndScriptFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("warehouseLocationsCompactUi.logic.test: ok");
}
await main();
