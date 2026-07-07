// src/pages/Warehouse/StoreOrders/storeOrderCompactUi.logic.test.ts
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
var storeOrdersFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/index.tsx");
var detailFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Detail.tsx");
var compactCssFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/compact.css");
var pickingListFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx");
var invoiceFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Invoice.tsx");
var containerProductPickerFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/components/ContainerProductPicker.tsx");
var printCssFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css");
var packageFile = path.resolve(process.cwd(), "package.json");
function readSource(file) {
  return readFileSync(file, "utf8").replace(/\r\n/g, "\n");
}
var storeOrdersSource = readSource(storeOrdersFile);
var detailSource = readSource(detailFile);
var compactCssSource = readSource(compactCssFile);
var pickingListSource = readSource(pickingListFile);
var invoiceSource = readSource(invoiceFile);
var containerProductPickerSource = readSource(containerProductPickerFile);
var printCssSource = readSource(printCssFile);
var packageSource = readSource(packageFile);
var detailMainTableSource = detailSource.slice(detailSource.indexOf("const columns: ColumnsType<StoreOrderDetailLine>"));
var detailKeyboardHandlerSource = detailSource.slice(
  detailSource.indexOf("const handleDetailInputKeyDown"),
  detailSource.indexOf("const handleCompleteOrder")
);
function readCssRule(source, selector) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? "";
}
function readColumnBlock(source, dataIndex) {
  const dataIndexPosition = source.indexOf(`dataIndex: '${dataIndex}'`);
  if (dataIndexPosition < 0) {
    return "";
  }
  const blockStart = source.lastIndexOf("    {", dataIndexPosition);
  const nextBlockStart = source.indexOf("    {", dataIndexPosition + dataIndex.length);
  return source.slice(blockStart, nextBlockStart > 0 ? nextBlockStart : source.length);
}
function readNumericValue(source, pattern) {
  const match = source.match(pattern);
  return match ? Number(match[1]) : Number.NaN;
}
async function main() {
  const failures = [];
  const detailClassFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8868\u5E94\u6302\u8F7D\u7D27\u51D1\u6837\u5F0F class", () => {
    assert(detailSource.includes("import './compact.css'"), "\u8BE6\u60C5\u9875\u5E94\u5F15\u5165 StoreOrders \u5C40\u90E8\u7D27\u51D1\u6837\u5F0F");
    assert(detailSource.includes('className="store-order-detail-table"'), "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8868\u7F3A\u5C11 store-order-detail-table class");
    assert(detailSource.includes('className="store-order-detail-filter-bar"'), "\u8BE6\u60C5\u9875\u7B5B\u9009\u7EDF\u8BA1\u6761\u7F3A\u5C11\u7D27\u51D1\u6837\u5F0F class");
    assert(detailSource.includes("renderStoreOrderDetailNumericCell("), "\u8BE6\u60C5\u9875\u6570\u5B57\u5217\u5E94\u8D70\u5355\u884C\u7B49\u5BBD\u6570\u5B57 helper");
  });
  if (detailClassFailure) failures.push(detailClassFailure);
  const listOrderNoFailure = await runTest("\u5217\u8868\u9875\u8BA2\u5355\u53F7\u590D\u5236\u6309\u94AE\u5E94\u9650\u5236\u5728\u8BA2\u5355\u53F7\u5217\u5185", () => {
    const orderCellRule = readCssRule(compactCssSource, ".store-order-list-table .store-order-list-order-cell");
    const orderButtonRule = readCssRule(compactCssSource, ".store-order-list-table .store-order-list-order-no");
    const copyButtonRule = readCssRule(compactCssSource, ".store-order-list-table .store-order-copy-button");
    assert(storeOrdersSource.includes('className="store-order-list-order-cell"'), "\u8BA2\u5355\u53F7\u5217\u5E94\u6302\u8F7D\u4E13\u5C5E\u5E03\u5C40 class");
    assert(/width:\s*100%/.test(orderCellRule), "\u8BA2\u5355\u53F7\u5E03\u5C40\u5BB9\u5668\u5E94\u5360\u6EE1\u5355\u5143\u683C\u5BBD\u5EA6");
    assert(/min-width:\s*0/.test(orderCellRule), "\u8BA2\u5355\u53F7\u5E03\u5C40\u5BB9\u5668\u5E94\u5141\u8BB8\u5185\u5BB9\u6536\u7F29");
    assert(/flex:\s*0\s+0\s+auto/.test(orderButtonRule), "\u8BA2\u5355\u53F7\u6587\u672C\u5E94\u5B8C\u6574\u663E\u793A\uFF0C\u4E0D\u5E94\u88AB\u538B\u7F29\u7701\u7565");
    assert(!/text-overflow:\s*ellipsis/.test(orderButtonRule), "\u8BA2\u5355\u53F7\u6587\u672C\u4E0D\u5E94\u7701\u7565\u663E\u793A");
    assert(!/overflow:\s*hidden/.test(orderButtonRule), "\u8BA2\u5355\u53F7\u6587\u672C\u4E0D\u5E94\u88AB\u9690\u85CF\u622A\u65AD");
    assert(/flex:\s*0\s+0\s+20px/.test(copyButtonRule), "\u590D\u5236\u6309\u94AE\u5E94\u56FA\u5B9A\u5BBD\u5EA6\uFF0C\u907F\u514D\u88AB\u6324\u51FA\u5217");
  });
  if (listOrderNoFailure) failures.push(listOrderNoFailure);
  const listTwoLineFailure = await runTest("\u5217\u8868\u9875\u5206\u5E97\u548C\u5907\u6CE8\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C", () => {
    const storeTagRule = readCssRule(compactCssSource, ".store-order-list-table .store-order-store-tag");
    const twoLineRule = readCssRule(compactCssSource, ".store-order-list-table .store-order-two-line-text");
    assert(storeOrdersSource.includes('className="store-order-store-tag"'), "\u5206\u5E97\u5217\u5E94\u6302\u8F7D\u4E13\u5C5E\u4E24\u884C\u6837\u5F0F class");
    assert(storeOrdersSource.includes("renderStoreOrderTwoLineText(value)"), "\u5907\u6CE8\u5217\u5E94\u4F7F\u7528\u4E24\u884C\u6587\u672C helper");
    assert(/-webkit-line-clamp:\s*2/.test(storeTagRule), "\u5206\u5E97\u540D\u79F0\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C");
    assert(/overflow:\s*hidden/.test(storeTagRule), "\u5206\u5E97\u540D\u79F0\u8D85\u8FC7\u4E24\u884C\u5E94\u9690\u85CF");
    assert(/white-space:\s*normal/.test(storeTagRule), "\u5206\u5E97\u540D\u79F0\u5E94\u5141\u8BB8\u6362\u884C");
    assert(/-webkit-line-clamp:\s*2/.test(twoLineRule), "\u5907\u6CE8\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C");
    assert(/overflow:\s*hidden/.test(twoLineRule), "\u5907\u6CE8\u8D85\u8FC7\u4E24\u884C\u5E94\u9690\u85CF");
    assert(/white-space:\s*normal/.test(twoLineRule), "\u5907\u6CE8\u5E94\u5141\u8BB8\u6362\u884C");
  });
  if (listTwoLineFailure) failures.push(listTwoLineFailure);
  const listColumnDragFailure = await runTest("\u5217\u8868\u9875\u4E3B\u8868\u5E94\u652F\u6301\u548C\u8D27\u67DC\u660E\u7EC6\u4E00\u81F4\u7684\u8868\u5934\u5217\u62D6\u62FD", () => {
    assert(
      storeOrdersSource.includes("DndContext") && storeOrdersSource.includes("SortableContext") && storeOrdersSource.includes("useSortable") && storeOrdersSource.includes("horizontalListSortingStrategy"),
      "\u5217\u8868\u9875\u4E3B\u8868\u5E94\u590D\u7528 @dnd-kit \u6A2A\u5411\u6392\u5E8F\u80FD\u529B"
    );
    assert(
      storeOrdersSource.includes("const STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.storeOrders.list.columnOrder.v1'") && storeOrdersSource.includes("localStorage.setItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY") && storeOrdersSource.includes("mergeStoreOrderListColumnOrder("),
      "\u5217\u8868\u9875\u5217\u987A\u5E8F\u5E94\u4FDD\u5B58\u5230\u4E13\u7528 localStorage key\uFF0C\u5E76\u517C\u5BB9\u5217\u589E\u5220"
    );
    assert(
      storeOrdersSource.includes("components={{ header: { cell: DraggableHeaderCell } }}") && storeOrdersSource.includes("<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>") && storeOrdersSource.includes("<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>"),
      "\u5217\u8868\u9875\u8868\u683C\u5E94\u63A5\u5165\u53EF\u62D6\u62FD\u8868\u5934 cell \u4E0E\u6A2A\u5411 SortableContext"
    );
    assert(
      storeOrdersSource.includes("isStoreOrderListColumnOrderCustomized(columnOrder, draggableColumnKeys)") && storeOrdersSource.includes("setColumnOrder(draggableColumnKeys)") && storeOrdersSource.includes("localStorage.removeItem(STORE_ORDER_LIST_COLUMN_ORDER_STORAGE_KEY)"),
      "\u5217\u8868\u9875\u62D6\u62FD\u5217\u540E\u5E94\u63D0\u4F9B\u91CD\u7F6E\u5217\u6309\u94AE\u5E76\u6E05\u9664\u672C\u5730\u5217\u987A\u5E8F"
    );
    assert(
      storeOrdersSource.includes("const draggableColumnKeys = baseColumns.map((column) => String(column.key) as StoreOrderListTableColumnKey)") && storeOrdersSource.includes("rowSelection={") && !storeOrdersSource.includes("columnOrder.includes('selection')"),
      "\u5217\u8868\u9875\u9009\u62E9\u5217\u4ECD\u5E94\u7531 rowSelection \u7BA1\u7406\uFF0C\u4E0D\u80FD\u8FDB\u5165\u4E1A\u52A1\u5217\u62D6\u62FD\u987A\u5E8F"
    );
    assert(
      compactCssSource.includes(".store-order-list-draggable-header") && compactCssSource.includes("cursor: move") && compactCssSource.includes("user-select: none"),
      "\u5217\u8868\u9875\u62D6\u62FD\u8868\u5934\u5E94\u6709\u5C40\u90E8\u6837\u5F0F\uFF0C\u907F\u514D\u5F71\u54CD\u5176\u4ED6\u8868\u683C"
    );
  });
  if (listColumnDragFailure) failures.push(listColumnDragFailure);
  const listStatusFilterFailure = await runTest("\u5217\u8868\u9875\u72B6\u6001\u7B5B\u9009\u5E94\u4F7F\u7528\u591A\u9009\u6846\u5E76\u9ED8\u8BA4\u52FE\u9009\u5DF2\u63D0\u4EA4\u548C\u914D\u8D27\u4E2D", () => {
    assert(storeOrdersSource.includes("Checkbox.Group"), "\u72B6\u6001\u7B5B\u9009\u5E94\u4F7F\u7528 Checkbox.Group");
    assert(storeOrdersSource.includes("const DEFAULT_STATUS_LIST = [FlowStatus.Submitted, FlowStatus.Picking]"), "\u9ED8\u8BA4\u72B6\u6001\u7B5B\u9009\u5E94\u4E3A\u5DF2\u63D0\u4EA4\u548C\u914D\u8D27\u4E2D");
    assert(storeOrdersSource.includes("useState<StoreOrderFlowStatus[]>(DEFAULT_STATUS_LIST)"), "\u72B6\u6001\u7B5B\u9009\u521D\u59CB\u503C\u5E94\u590D\u7528\u9ED8\u8BA4\u72B6\u6001\u5217\u8868");
    assert(storeOrdersSource.includes("setStatusList(DEFAULT_STATUS_LIST)"), "\u91CD\u7F6E\u65F6\u5E94\u6062\u590D\u9ED8\u8BA4\u72B6\u6001\u7B5B\u9009");
    assert(storeOrdersSource.includes("statusList: DEFAULT_STATUS_LIST"), "\u91CD\u7F6E\u540E\u67E5\u8BE2\u5E94\u6309\u9ED8\u8BA4\u72B6\u6001\u7B5B\u9009\u53D1\u8D77");
    assert(storeOrdersSource.includes("const STATUS_FILTER_ORDER = [FlowStatus.Submitted, FlowStatus.Picking, FlowStatus.Completed]"), "\u72B6\u6001\u7B5B\u9009\u5C55\u793A\u987A\u5E8F\u5E94\u628A\u5DF2\u5B8C\u6210\u653E\u5728\u6700\u540E");
    assert(!storeOrdersSource.includes('<Select\n            mode="multiple"\n            value={statusList}'), "\u72B6\u6001\u7B5B\u9009\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528\u591A\u9009 Select");
  });
  if (listStatusFilterFailure) failures.push(listStatusFilterFailure);
  const listColumnFilterFailure = await runTest("\u5217\u8868\u9875\u4E3B\u8868\u5217\u5934\u7B5B\u9009\u5E94\u8D70\u670D\u52A1\u7AEF\u67E5\u8BE2\u53C2\u6570\u5E76\u652F\u6301\u91CD\u7F6E", () => {
    assert(storeOrdersSource.includes("StoreOrderListColumnFilters"), "\u5217\u8868\u9875\u5E94\u5F15\u5165\u5217\u5934\u7B5B\u9009\u7C7B\u578B");
    assert(storeOrdersSource.includes("const [columnFilters, setColumnFilters] = useState<StoreOrderListColumnFilters>({})"), "\u5217\u8868\u9875\u5E94\u7EF4\u62A4\u5217\u5934\u7B5B\u9009\u72B6\u6001");
    assert(storeOrdersSource.includes("columnFilters: cleanStoreOrderListColumnFilters("), "\u5217\u8868\u67E5\u8BE2\u5E94\u643A\u5E26\u6E05\u7406\u540E\u7684 columnFilters");
    assert(storeOrdersSource.includes("setColumnFilters({})"), "\u91CD\u7F6E\u6309\u94AE\u5E94\u6E05\u7A7A\u5217\u5934\u7B5B\u9009\u72B6\u6001");
    assert(storeOrdersSource.includes("columnFilters: undefined"), "\u91CD\u7F6E\u67E5\u8BE2\u5E94\u663E\u5F0F\u6E05\u7A7A\u670D\u52A1\u7AEF\u5217\u7B5B\u9009\u53C2\u6570");
    assert(storeOrdersSource.includes("makeTextFilterDropdown") && storeOrdersSource.includes("makeNumberRangeFilterDropdown") && storeOrdersSource.includes("makeDateRangeFilterDropdown"), "\u5217\u8868\u9875\u5E94\u63D0\u4F9B\u6587\u672C\u3001\u6570\u503C\u8303\u56F4\u548C\u65E5\u671F\u8303\u56F4\u7B5B\u9009\u5F39\u5C42");
    assert(storeOrdersSource.includes("makeStoreFilterDropdown") && storeOrdersSource.includes("makeStatusFilterDropdown") && storeOrdersSource.includes("makeOrderDateFilterDropdown"), "\u5206\u5E97\u3001\u72B6\u6001\u548C\u8BA2\u5355\u65E5\u671F\u5217\u5934\u7B5B\u9009\u5E94\u590D\u7528\u9876\u90E8\u7B5B\u9009\u72B6\u6001");
    assert(storeOrdersSource.includes("onMouseDown={(event) => event.stopPropagation()}"), "\u5217\u5934\u7B5B\u9009\u5F39\u5C42\u5E94\u963B\u6B62\u9F20\u6807\u4E8B\u4EF6\u5192\u6CE1\uFF0C\u907F\u514D\u89E6\u53D1\u8868\u5934\u62D6\u62FD");
    assert(compactCssSource.includes(".store-order-list-column-filter"), "\u5217\u5934\u7B5B\u9009\u5F39\u5C42\u5E94\u6709\u5C40\u90E8\u7D27\u51D1\u6837\u5F0F");
  });
  if (listColumnFilterFailure) failures.push(listColumnFilterFailure);
  const detailContentFailure = await runTest("\u8BE6\u60C5\u9875\u8D27\u53F7\u6761\u7801\u540D\u79F0\u5E94\u4FDD\u7559\u4E1A\u52A1\u53EF\u8BFB\u6027", () => {
    assert(detailMainTableSource.includes("width={30}") && detailMainTableSource.includes("height={30}"), "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u56FE\u7247\u5E94\u7F29\u5230 30x30");
    assert(detailMainTableSource.includes('className="store-order-detail-copy-button"'), "\u8BE6\u60C5\u9875\u8D27\u53F7\u590D\u5236\u6309\u94AE\u5E94\u4E3A\u65E0\u6587\u5B57\u56FE\u6807\u6309\u94AE");
    assert(!detailMainTableSource.includes('<Button size="small" type="link" onClick={() => void copyTextToClipboard(value)}>'), "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8D27\u53F7\u590D\u5236\u6309\u94AE\u4E0D\u5E94\u663E\u793A\u590D\u5236\u6587\u5B57");
    assert(detailMainTableSource.includes('className="store-order-barcode-cell"'), "\u8BE6\u60C5\u9875\u6761\u7801\u6587\u672C\u5E94\u6302\u8F7D\u4E0D\u9690\u85CF\u4E0D\u6298\u53E0\u6837\u5F0F");
    assert(detailMainTableSource.includes("textNoWrap"), "\u8BE6\u60C5\u9875\u6761\u7801\u6587\u672C\u5E94\u4FDD\u6301\u5355\u884C\u663E\u793A");
    assert(detailMainTableSource.includes("showCopy={false}"), "\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u6761\u7801\u5217\u5E94\u5173\u95ED\u590D\u5236\u6309\u94AE\u4EE5\u4FDD\u7559\u6761\u7801\u53EF\u8BFB\u5BBD\u5EA6");
    assert(!detailMainTableSource.includes("textMaxWidth"), "\u8BE6\u60C5\u9875\u6761\u7801\u6587\u672C\u4E0D\u5E94\u8BBE\u7F6E textMaxWidth \u7701\u7565\u6298\u53E0");
    assert(detailMainTableSource.includes("renderStoreOrderTwoLineText(value)"), "\u8BE6\u60C5\u9875\u5546\u54C1\u540D\u79F0\u5E94\u6700\u591A\u663E\u793A\u4E24\u884C");
    assert(detailMainTableSource.includes("Tooltip title={t('common.save')}"), "\u8BE6\u60C5\u9875\u64CD\u4F5C\u5217\u4FDD\u5B58\u6309\u94AE\u5E94\u4F7F\u7528 Tooltip \u56FE\u6807\u6309\u94AE");
    assert(detailMainTableSource.includes('className="store-order-detail-action-button"'), "\u8BE6\u60C5\u9875\u64CD\u4F5C\u5217\u5E94\u4F7F\u7528\u7D27\u51D1\u56FE\u6807\u6309\u94AE\u6837\u5F0F");
  });
  if (detailContentFailure) failures.push(detailContentFailure);
  const detailProductStatusCopyFailure = await runTest("\u8BE6\u60C5\u9875\u5546\u54C1\u72B6\u6001\u5E94\u4F7F\u7528\u4E0A\u4E0B\u67B6\u6587\u6848", () => {
    const statusColumn = readColumnBlock(detailMainTableSource, "isActive");
    assert(statusColumn.includes("t('common.activeUpper')") && statusColumn.includes("t('common.inactiveUpper')"), "\u8BE6\u60C5\u9875\u5546\u54C1\u72B6\u6001\u5217\u5E94\u663E\u793A\u4E0A\u67B6/\u4E0B\u67B6");
    assert(detailMainTableSource.includes("record.isActive ? t('common.inactiveUpper') : t('common.activeUpper')"), "\u8BE6\u60C5\u9875\u5546\u54C1\u72B6\u6001\u5207\u6362\u6309\u94AE\u5E94\u63D0\u793A\u4E0A\u67B6/\u4E0B\u67B6");
    assert(detailSource.includes("status: line.isActive ? t('common.inactiveUpper') : t('common.activeUpper')"), "\u8BE6\u60C5\u9875\u5546\u54C1\u72B6\u6001\u5207\u6362\u6210\u529F\u63D0\u793A\u5E94\u4F7F\u7528\u4E0A\u67B6/\u4E0B\u67B6");
    assert(detailSource.includes("{ value: 'active', label: t('common.activeUpper') }") && detailSource.includes("{ value: 'inactive', label: t('common.inactiveUpper') }"), "\u6279\u91CF\u4FEE\u6539\u72B6\u6001\u4E0B\u62C9\u5E94\u4F7F\u7528\u4E0A\u67B6/\u4E0B\u67B6");
  });
  if (detailProductStatusCopyFailure) failures.push(detailProductStatusCopyFailure);
  const containerPickerRetailPriceFailure = await runTest("\u8D27\u67DC\u9009\u54C1\u5F39\u7A97\u5546\u54C1\u8868\u683C\u5E94\u5C55\u793A\u96F6\u552E\u4EF7\u5217", () => {
    const retailPriceColumn = readColumnBlock(containerProductPickerSource, "\u96F6\u552E\u4EF7\u683C");
    const importPricePosition = containerProductPickerSource.indexOf("title: t('column.importPrice')");
    const retailPricePosition = containerProductPickerSource.indexOf("title: t('column.retailPrice')");
    const containerQtyPosition = containerProductPickerSource.indexOf("title: t('column.containerQty')");
    assert(retailPricePosition > importPricePosition, "\u96F6\u552E\u4EF7\u5217\u5E94\u4F4D\u4E8E\u8FDB\u53E3\u4EF7\u5217\u4E4B\u540E");
    assert(retailPricePosition < containerQtyPosition, "\u96F6\u552E\u4EF7\u5217\u5E94\u4F4D\u4E8E\u8D27\u67DC\u6570\u91CF\u5217\u4E4B\u524D");
    assert(retailPriceColumn.includes("title: t('column.retailPrice')"), "\u96F6\u552E\u4EF7\u5217\u5E94\u4F7F\u7528 column.retailPrice \u7FFB\u8BD1");
    assert(retailPriceColumn.includes("record.\u5546\u54C1\u4FE1\u606F?.\u96F6\u552E\u4EF7\u683C"), "\u96F6\u552E\u4EF7\u5217\u5E94\u8BFB\u53D6\u5546\u54C1\u4FE1\u606F\u4E2D\u7684\u96F6\u552E\u4EF7\u683C");
    assert(retailPriceColumn.includes("value === undefined || value === null ? '--' : Number(value).toFixed(2)"), "\u96F6\u552E\u4EF7\u5217\u7F3A\u5931\u663E\u793A --\uFF0C\u6709\u6548\u503C\u5E94\u4FDD\u7559\u4E24\u4F4D");
    assert(!containerProductPickerSource.includes("retailPrice:"), "\u8D27\u67DC\u9009\u54C1\u52A0\u5165\u8BA2\u5355 payload \u4E0D\u5E94\u5199\u5165\u96F6\u552E\u4EF7");
  });
  if (containerPickerRetailPriceFailure) failures.push(containerPickerRetailPriceFailure);
  const densityFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8868\u5173\u952E\u5B57\u6BB5\u5E94\u538B\u7F29\u5230\u9996\u5C4F\u4F18\u5148\u663E\u793A", () => {
    const imageColumn = readColumnBlock(detailMainTableSource, "productImage");
    const itemNumberColumn = readColumnBlock(detailMainTableSource, "itemNumber");
    const productNameColumn = readColumnBlock(detailMainTableSource, "productName");
    const barcodeColumn = readColumnBlock(detailMainTableSource, "barcode");
    const locationColumn = readColumnBlock(detailMainTableSource, "locationCode");
    const allocQuantityColumn = readColumnBlock(detailMainTableSource, "allocQuantity");
    const importPriceColumn = readColumnBlock(detailMainTableSource, "importPrice");
    const scrollX = readNumericValue(detailMainTableSource, /scroll=\{\{\s*x:\s*(\d+)/);
    assert(readNumericValue(imageColumn, /width:\s*(\d+)/) <= 44, "\u56FE\u7247\u5217\u5BBD\u5E94\u538B\u5230 44 \u4EE5\u5185");
    assert(readNumericValue(imageColumn, /width=\{(\d+)\}/) <= 32, "\u56FE\u7247\u5BBD\u5EA6\u5E94\u538B\u5230 32 \u4EE5\u5185");
    assert(readNumericValue(imageColumn, /height=\{(\d+)\}/) <= 32, "\u56FE\u7247\u9AD8\u5EA6\u5E94\u538B\u5230 32 \u4EE5\u5185");
    assert(readNumericValue(itemNumberColumn, /width:\s*(\d+)/) <= 80, "\u8D27\u53F7\u5217\u5BBD\u5E94\u538B\u5230 80 \u4EE5\u5185");
    assert(readNumericValue(productNameColumn, /width:\s*(\d+)/) >= 128, "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u4FDD\u7559\u81F3\u5C11 128 \u5BBD\u5EA6");
    assert(readNumericValue(barcodeColumn, /width:\s*(\d+)/) <= 106, "\u6761\u7801\u5217\u5BBD\u5E94\u63A7\u5236\u5728 106 \u4EE5\u5185");
    assert(readNumericValue(locationColumn, /width:\s*(\d+)/) <= 85, "\u8D27\u4F4D\u5217\u5BBD\u5E94\u538B\u5230 85 \u4EE5\u5185");
    assert(readNumericValue(allocQuantityColumn, /style=\{\{\s*width:\s*(\d+)/) <= 62, "\u53D1\u8D27\u6570\u8F93\u5165\u6846\u5BBD\u5EA6\u5E94\u538B\u5230 62 \u4EE5\u5185");
    assert(readNumericValue(importPriceColumn, /style=\{\{\s*width:\s*(\d+)/) <= 62, "\u8FDB\u53E3\u4EF7\u8F93\u5165\u6846\u5BBD\u5EA6\u5E94\u538B\u5230 62 \u4EE5\u5185");
    assert(importPriceColumn.includes("controls={false}"), "\u8FDB\u53E3\u4EF7\u8F93\u5165\u6846\u5E94\u9690\u85CF\u52A0\u51CF\u6309\u94AE\uFF0C\u907F\u514D\u8BEF\u89E6\u6539\u4EF7");
    assert(scrollX >= 1280 && scrollX <= 1320, "\u4E3B\u8868 scroll.x \u5E94\u6536\u655B\u5230 1280-1320");
  });
  if (densityFailure) failures.push(densityFailure);
  const cssFailure = await runTest("\u5C40\u90E8 CSS \u5E94\u63D0\u4F9B\u7D27\u51D1\u8868\u683C\u3001\u4E24\u884C\u6587\u672C\u3001nowrap \u548C\u7B49\u5BBD\u6570\u5B57\u89C4\u5219", () => {
    const barcodeCellRule = readCssRule(compactCssSource, ".store-order-detail-table .store-order-barcode-cell");
    const barcodeTextRule = readCssRule(compactCssSource, ".store-order-detail-table .store-order-barcode-cell .ant-typography");
    const inputNumberRule = readCssRule(compactCssSource, ".store-order-detail-table .ant-input-number");
    const detailCellRule = readCssRule(compactCssSource, ".store-order-detail-table .ant-table-cell");
    assert(compactCssSource.includes(".store-order-detail-table .ant-table-cell"), "\u8BE6\u60C5\u8868\u683C\u7F3A\u5C11\u5C40\u90E8 cell padding \u89C4\u5219");
    assert(compactCssSource.includes(".store-order-list-table .store-order-list-order-cell"), "\u5217\u8868\u8BA2\u5355\u53F7\u5217\u7F3A\u5C11\u5C40\u90E8\u9632\u6EA2\u51FA\u6837\u5F0F");
    assert(compactCssSource.includes(".store-order-list-table .store-order-store-tag"), "\u5217\u8868\u5206\u5E97\u5217\u7F3A\u5C11\u4E24\u884C\u622A\u65AD\u6837\u5F0F");
    assert(compactCssSource.includes(".store-order-list-table .store-order-two-line-text"), "\u5217\u8868\u5907\u6CE8\u5217\u7F3A\u5C11\u4E24\u884C\u622A\u65AD\u6837\u5F0F");
    assert(!/^\\.store-order-nowrap/m.test(compactCssSource), "nowrap \u5DE5\u5177\u7C7B\u5FC5\u987B\u9650\u5B9A\u5230\u8BE6\u60C5\u4E3B\u8868\u4E0B");
    assert(!/^\\.store-order-numeric-cell/m.test(compactCssSource), "\u6570\u5B57\u5DE5\u5177\u7C7B\u5FC5\u987B\u9650\u5B9A\u5230\u8BE6\u60C5\u4E3B\u8868\u4E0B");
    assert(!/^\\.store-order-two-line-text/m.test(compactCssSource), "\u4E24\u884C\u6587\u672C\u5DE5\u5177\u7C7B\u5FC5\u987B\u9650\u5B9A\u5230\u8BE6\u60C5\u4E3B\u8868\u4E0B");
    assert(compactCssSource.includes("-webkit-line-clamp: 2"), "\u7D27\u51D1\u6837\u5F0F\u7F3A\u5C11\u6700\u591A\u4E24\u884C\u89C4\u5219");
    assert(compactCssSource.includes("white-space: nowrap"), "\u7D27\u51D1\u6837\u5F0F\u7F3A\u5C11 nowrap \u89C4\u5219");
    assert(compactCssSource.includes("font-variant-numeric: tabular-nums"), "\u7D27\u51D1\u6837\u5F0F\u7F3A\u5C11\u7B49\u5BBD\u6570\u5B57\u89C4\u5219");
    assert(compactCssSource.includes(".store-order-detail-filter-bar"), "\u8BE6\u60C5\u7B5B\u9009\u7EDF\u8BA1\u6761\u7F3A\u5C11\u7D27\u51D1\u6837\u5F0F");
    assert(compactCssSource.includes(".store-order-detail-table .store-order-barcode-cell .ant-typography"), "\u6761\u7801\u6587\u672C\u7F3A\u5C11\u4E0D\u9690\u85CF\u4E0D\u6298\u53E0\u6837\u5F0F");
    assert(/vertical-align:\s*middle/.test(detailCellRule), "\u8BE6\u60C5\u4E3B\u8868\u5355\u5143\u683C\u5E94\u5782\u76F4\u5C45\u4E2D");
    assert(/white-space:\s*nowrap/.test(barcodeCellRule), "\u6761\u7801\u5BB9\u5668\u5E94\u5F3A\u5236\u5355\u884C\uFF0C\u907F\u514D\u6761\u7801\u56FE\u7247\u548C\u6587\u672C\u6362\u884C");
    assert(/overflow:\s*visible/.test(barcodeCellRule), "\u6761\u7801\u5BB9\u5668\u4E0D\u5E94\u9690\u85CF\u8D85\u51FA\u5185\u5BB9");
    assert(/text-overflow:\s*clip/.test(barcodeTextRule), "\u6761\u7801\u6587\u672C\u4E0D\u5E94\u7701\u7565\u9690\u85CF");
    assert(/white-space:\s*nowrap/.test(inputNumberRule), "\u8BE6\u60C5\u4E3B\u8868\u8F93\u5165\u578B\u6570\u5B57\u5217\u5E94\u4FDD\u6301\u5355\u884C");
  });
  if (cssFailure) failures.push(cssFailure);
  const detailTableStripeFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8868\u5E94\u6709\u9694\u884C\u8272\u5E76\u4FDD\u6301\u56FA\u5B9A\u5217\u548C hover \u4E00\u81F4", () => {
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr:nth-child(even) > td"),
      "\u8BE6\u60C5\u4E3B\u8868\u7F3A\u5C11\u5076\u6570\u884C\u9694\u884C\u8272\u89C4\u5219"
    );
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr:nth-child(odd) > td"),
      "\u8BE6\u60C5\u4E3B\u8868\u7F3A\u5C11\u5947\u6570\u884C\u9694\u884C\u8272\u89C4\u5219"
    );
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr:hover > td"),
      "\u8BE6\u60C5\u4E3B\u8868\u7F3A\u5C11 hover \u884C\u80CC\u666F\u89C4\u5219"
    );
    assert(compactCssSource.includes(".ant-table-cell-fix-left"), "\u8BE6\u60C5\u4E3B\u8868\u56FA\u5B9A\u5DE6\u5217\u80CC\u666F\u5E94\u8DDF\u968F\u884C\u80CC\u666F");
    assert(compactCssSource.includes(".ant-table-cell-fix-right"), "\u8BE6\u60C5\u4E3B\u8868\u56FA\u5B9A\u53F3\u5217\u80CC\u666F\u5E94\u8DDF\u968F\u884C\u80CC\u666F");
    assert(!/^\\.ant-table-tbody\s*>/m.test(compactCssSource), "\u9694\u884C\u8272\u89C4\u5219\u5FC5\u987B\u9650\u5B9A\u5728\u8BE6\u60C5\u4E3B\u8868\u4E0B");
  });
  if (detailTableStripeFailure) failures.push(detailTableStripeFailure);
  const detailTableVerticalAlignFailure = await runTest("\u8BE6\u60C5\u9875\u4E3B\u660E\u7EC6\u8868\u5185\u90E8\u5143\u7D20\u5E94\u5782\u76F4\u5C45\u4E2D", () => {
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr > td .ant-space"),
      "\u8BE6\u60C5\u4E3B\u8868 Space \u5185\u5BB9\u5E94\u5782\u76F4\u5C45\u4E2D"
    );
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr > td .ant-image"),
      "\u8BE6\u60C5\u4E3B\u8868\u56FE\u7247\u5185\u5BB9\u5E94\u5782\u76F4\u5C45\u4E2D"
    );
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr > td .ant-tag"),
      "\u8BE6\u60C5\u4E3B\u8868\u72B6\u6001\u6807\u7B7E\u5E94\u5782\u76F4\u5C45\u4E2D"
    );
    assert(
      compactCssSource.includes(".store-order-detail-table .ant-table-tbody > tr > td .ant-input-number"),
      "\u8BE6\u60C5\u4E3B\u8868\u6570\u5B57\u8F93\u5165\u6846\u5E94\u5782\u76F4\u5C45\u4E2D"
    );
    assert(compactCssSource.includes(".store-order-detail-table .store-order-two-line-text"), "\u8BE6\u60C5\u4E3B\u8868\u4E24\u884C\u6587\u672C\u5E94\u4FDD\u7559\u5C40\u90E8\u6837\u5F0F");
    assert(compactCssSource.includes("align-items: center"), "\u8BE6\u60C5\u4E3B\u8868\u5185\u90E8 flex \u5143\u7D20\u7F3A\u5C11\u5C45\u4E2D\u5BF9\u9F50");
  });
  if (detailTableVerticalAlignFailure) failures.push(detailTableVerticalAlignFailure);
  const detailBulkSaveFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u6574\u5355\u4FDD\u5B58\u4E14\u53EA\u63D0\u4EA4\u5DF2\u4FEE\u6539\u660E\u7EC6\u884C", () => {
    assert(detailSource.includes("handleSaveEditedLines"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58\u5904\u7406\u51FD\u6570");
    assert(detailSource.includes("getEditedLinePayloads()"), "\u6574\u5355\u4FDD\u5B58\u5E94\u4ECE\u5DF2\u4FEE\u6539\u884C\u751F\u6210 payload");
    assert(detailSource.includes("batchUpdateStoreOrderLines({"), "\u6574\u5355\u4FDD\u5B58\u5E94\u590D\u7528\u660E\u7EC6\u6279\u91CF\u4FDD\u5B58\u63A5\u53E3");
    assert(detailSource.includes("detailGUID: item.detailGUID"), "\u6574\u5355\u4FDD\u5B58 payload \u5E94\u643A\u5E26\u660E\u7EC6 GUID \u4EE5\u547D\u4E2D\u540E\u7AEF\u5FEB\u8DEF\u5F84");
    assert(detailSource.includes("t('storeOrders.detail.saveEditedLines'"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58\u6309\u94AE\u6587\u6848");
    assert(
      detailSource.includes("disabled={isReadonlyOrder || isPasteOptimisticPreviewActive || editedLineCount === 0}"),
      "\u6574\u5355\u4FDD\u5B58\u5E94\u5728\u53EA\u8BFB\u3001\u4E34\u65F6\u9884\u89C8\u6216\u65E0\u4FEE\u6539\u65F6\u7981\u7528"
    );
    assert(detailSource.includes("setEditingRows((current) => {") && detailSource.includes("savedDetailGUIDs"), "\u6574\u5355\u4FDD\u5B58\u6210\u529F\u540E\u5E94\u6E05\u7406\u5DF2\u4FDD\u5B58\u884C\u7F16\u8F91\u72B6\u6001");
  });
  if (detailBulkSaveFailure) failures.push(detailBulkSaveFailure);
  const detailRefreshImportPriceFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u5141\u8BB8\u4ED3\u5E93\u7BA1\u7406\u5458\u4E8C\u6B21\u786E\u8BA4\u540E\u4ECE\u4ED3\u5E93\u8868\u66F4\u65B0\u8FDB\u8D27\u4EF7", () => {
    assert(detailSource.includes("refreshStoreOrderImportPrices"), "\u8BE6\u60C5\u9875\u5E94\u8C03\u7528\u66F4\u65B0\u8FDB\u8D27\u4EF7\u4E13\u7528\u670D\u52A1");
    assert(detailSource.includes("handleRefreshImportPricesFromWarehouse"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u66F4\u65B0\u8FDB\u8D27\u4EF7\u5904\u7406\u51FD\u6570");
    assert(detailSource.includes("t('storeOrders.detail.refreshImportPrices'"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u66F4\u65B0\u8FDB\u8D27\u4EF7\u6309\u94AE\u6587\u6848");
    assert(
      detailSource.includes("detailGUIDs: isSelectedScope ? targetDetailGUIDs : undefined"),
      "\u6709\u9009\u4E2D\u884C\u65F6\u5E94\u4F20\u660E\u7EC6 GUID\uFF0C\u672A\u9009\u4E2D\u65F6\u5E94\u4EA4\u7ED9\u540E\u7AEF\u6574\u5355\u5237\u65B0"
    );
    assert(
      detailSource.includes("t('storeOrders.detail.refreshImportPricesSelectedContent'") && detailSource.includes("t('storeOrders.detail.refreshImportPricesWholeOrderContent'"),
      "\u66F4\u65B0\u8FDB\u8D27\u4EF7\u4E8C\u6B21\u786E\u8BA4\u5E94\u533A\u5206\u9009\u4E2D\u884C\u548C\u6574\u5355\u8303\u56F4"
    );
    assert(
      detailSource.includes("disabled={!detail || isPasteOptimisticPreviewActive || refreshImportPriceLoading}"),
      "\u66F4\u65B0\u8FDB\u8D27\u4EF7\u6309\u94AE\u4E0D\u5E94\u56E0\u4E3A isReadonlyOrder \u7981\u7528\uFF0C\u4F46\u4E34\u65F6\u9884\u89C8\u671F\u95F4\u5E94\u7B49\u5F85\u540E\u53F0\u5237\u65B0\u540E\u518D\u64CD\u4F5C"
    );
  });
  if (detailRefreshImportPriceFailure) failures.push(detailRefreshImportPriceFailure);
  const warehouseManagerActionFailure = await runTest("\u4ED3\u5E93\u5458\u5DE5\u4EC5\u53EF\u770B\u5230\u8BE6\u60C5\u9875\u53EA\u8BFB\u6587\u6863\u5165\u53E3\uFF0C\u4E0D\u5E94\u770B\u5230\u8BA2\u8D27\u7BA1\u7406\u529F\u80FD\u6309\u94AE", () => {
    const orderDetailSectionSource = detailSource.slice(
      detailSource.indexOf("title={t('storeOrders.orderDetailSection')}"),
      detailSource.indexOf('className="store-order-detail-filter-bar"')
    );
    const pickingButtonSource = orderDetailSectionSource.slice(
      orderDetailSectionSource.indexOf("icon={<PrinterOutlined />}"),
      orderDetailSectionSource.indexOf("t('storeOrders.pickingList')")
    );
    const managerGuardText = "{canUseWarehouseManagerActions ? (";
    const detailExtraGuardText = "canUseStoreOrderDetailExtraActions ? (";
    const isInsideGuard = (guardText, targetPosition) => {
      const guardPosition = orderDetailSectionSource.lastIndexOf(guardText, targetPosition);
      const guardClosePosition = orderDetailSectionSource.lastIndexOf(") : null}", targetPosition);
      return guardPosition >= 0 && guardPosition > guardClosePosition;
    };
    const invoiceButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.invoice')");
    const pickingButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.pickingList')");
    const managerOnlyDetailActions = [
      "t('storeOrders.quickAdd')",
      "t('storeOrders.selectProduct')",
      "t('storeOrders.containerPicker')",
      "t('storeOrders.excelPaste')",
      "t('storeOrders.detail.saveEditedLines')",
      "t('storeOrders.detail.refreshImportPrices')",
      "t('storeOrders.batchModify')",
      "t('storeOrders.detail.selectedRows'"
    ];
    assert(
      storeOrdersSource.includes("const isWarehouseStaffOnly =") && storeOrdersSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly") && storeOrdersSource.includes("const canCreateStoreOrder = access.canWriteOrder || canUseWarehouseManagerActions") && storeOrdersSource.includes("const canDeleteStoreOrder = access.canDeleteOrder || canUseWarehouseManagerActions"),
      "\u5217\u8868\u9875\u5E94\u4F7F\u7528\u4ED3\u5E93\u8BA2\u8D27\u7BA1\u7406\u6743\u9650\u5F00\u5173\uFF0C\u5E76\u6392\u9664\u7EAF WarehouseStaff \u5199\u6743\u9650"
    );
    assert(
      storeOrdersSource.includes("{canUseWarehouseManagerActions ? (") && storeOrdersSource.includes("t('storeOrders.syncIncrementalOrders')") && storeOrdersSource.includes("t('storeOrders.fixStoreGuid', '\u4FEE\u590D\u5206\u5E97 GUID')") && storeOrdersSource.includes("t('storeOrders.newOrder')") && storeOrdersSource.includes("disabled={!canCreateStoreOrder}") && storeOrdersSource.includes("t('storeOrders.copyOrder'") && storeOrdersSource.includes("t('storeOrders.batchSubmitted')") && storeOrdersSource.includes("t('storeOrders.batchCompleted')") && storeOrdersSource.includes("{canDeleteStoreOrder ? ("),
      "\u5217\u8868\u9875\u540C\u6B65\u3001\u4FEE\u590D\u3001\u65B0\u5EFA\u3001\u590D\u5236\u3001\u5220\u9664\u548C\u6279\u91CF\u72B6\u6001\u6309\u94AE\u5E94\u4EC5\u4ED3\u5E93\u8BA2\u8D27\u7BA1\u7406\u6743\u9650\u53EF\u89C1"
    );
    assert(
      storeOrdersSource.includes("canUseWarehouseManagerActions && (record.flowStatus === FlowStatus.Submitted || record.flowStatus === FlowStatus.Picking)"),
      "\u5217\u8868\u9875\u914D\u8D27\u5165\u53E3\u5E94\u4EC5\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u89C1"
    );
    assert(
      storeOrdersSource.includes("rowSelection={\n                canUseWarehouseManagerActions"),
      "\u5217\u8868\u9875\u52FE\u9009\u5217\u5E94\u4EC5\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u89C1"
    );
    assert(
      detailSource.includes("const isWarehouseStaffOnly =") && detailSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly"),
      "\u8BE6\u60C5\u9875\u5E94\u4F7F\u7528\u4ED3\u5E93\u8BA2\u8D27\u7BA1\u7406\u6743\u9650\u5F00\u5173\uFF0C\u5E76\u6392\u9664\u7EAF WarehouseStaff \u5199\u6743\u9650"
    );
    assert(
      detailSource.includes("const canUseStoreOrderDocumentActions = access.isWarehouseStaff"),
      "\u8BE6\u60C5\u9875\u5E94\u4E3A WarehouseStaff \u63D0\u4F9B\u53EA\u8BFB\u6587\u6863\u5165\u53E3\u6743\u9650\u5F00\u5173"
    );
    assert(
      detailSource.includes("const canUseStoreOrderDetailExtraActions = canUseWarehouseManagerActions || canUseStoreOrderDocumentActions"),
      "\u8BE6\u60C5\u9875\u660E\u7EC6\u5361\u7247 extra \u5E94\u540C\u65F6\u5141\u8BB8\u4ED3\u5E93\u7BA1\u7406\u5458\u548C WarehouseStaff \u6587\u6863\u5165\u53E3\uFF0C\u907F\u514D\u4E2D\u6587\u4ED3\u5E93\u7ECF\u7406\u88AB\u8BEF\u9690\u85CF"
    );
    assert(
      detailSource.includes("if (canUseWarehouseManagerActions && canEditOrder)"),
      "\u8BE6\u60C5\u9875\u7F16\u8F91\u4FDD\u62A4\u5E94\u540C\u65F6\u68C0\u67E5\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650"
    );
    assert(
      detailSource.includes("extra={\n                  canUseWarehouseManagerActions ? ("),
      "\u8BE6\u60C5\u9875\u8BA2\u5355\u5934\u529F\u80FD\u6309\u94AE\u5E94\u4EC5\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u89C1"
    );
    assert(
      orderDetailSectionSource.includes("canUseStoreOrderDetailExtraActions ? (\n                  <Space wrap>") && orderDetailSectionSource.indexOf(detailExtraGuardText) >= 0 && orderDetailSectionSource.indexOf(detailExtraGuardText) < pickingButtonPosition && !isInsideGuard(managerGuardText, pickingButtonPosition) && pickingButtonSource.includes("navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)") && pickingButtonSource.includes("icon={<PrinterOutlined />}"),
      "\u8BE6\u60C5\u9875\u914D\u8D27\u5355\u6309\u94AE\u5E94\u53D7\u53EA\u8BFB\u6587\u6863\u5165\u53E3\u6743\u9650\u63A7\u5236\uFF0C\u4E0D\u80FD\u53EA\u7531\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u5305\u4F4F"
    );
    assert(
      invoiceButtonPosition > 0 && isInsideGuard(managerGuardText, invoiceButtonPosition),
      "\u8BE6\u60C5\u9875\u53D1\u7968\u6309\u94AE\u4ECD\u5E94\u4EC5\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u89C1"
    );
    assert(
      managerOnlyDetailActions.every((actionText) => {
        const actionPosition = orderDetailSectionSource.indexOf(actionText);
        return actionPosition > 0 && isInsideGuard(managerGuardText, actionPosition);
      }),
      "\u8BE6\u60C5\u9875\u660E\u7EC6\u7BA1\u7406\u529F\u80FD\u6309\u94AE\u5E94\u7EE7\u7EED\u53D7\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u4FDD\u62A4"
    );
    assert(
      detailSource.includes("column.key !== 'actions'") && detailSource.includes("rowSelection={\n                  canUseWarehouseManagerActions"),
      "\u8BE6\u60C5\u9875\u884C\u64CD\u4F5C\u5217\u548C\u52FE\u9009\u5217\u5E94\u4EC5\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u89C1"
    );
    assert(
      detailSource.includes("disabled={!canUseWarehouseManagerActions || isReadonlyOrder}") && detailSource.includes("disabled={!canUseWarehouseManagerActions || !canEditOutboundDate}"),
      "\u8BE6\u60C5\u9875\u975E\u4ED3\u5E93\u7BA1\u7406\u5458\u5E94\u4E0D\u80FD\u7F16\u8F91\u8BA2\u5355\u5934\u548C\u660E\u7EC6\u8F93\u5165"
    );
  });
  if (warehouseManagerActionFailure) failures.push(warehouseManagerActionFailure);
  const importPriceConfirmFailure = await runTest("\u8BE6\u60C5\u9875\u4FDD\u5B58\u8FDB\u53E3\u4EF7\u53D8\u66F4\u524D\u5E94\u63D0\u793A\u540C\u6B65\u4ED3\u5E93\u5546\u54C1\u8868\u548C\u5206\u5E97\u8868", () => {
    assert(detailSource.includes("confirmImportPriceSync"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4 helper");
    assert(detailSource.includes("t('storeOrders.detail.importPriceSyncConfirmTitle'"), "\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u7F3A\u5C11\u6807\u9898\u6587\u6848");
    assert(detailSource.includes("t('storeOrders.detail.importPriceSyncConfirmContent'"), "\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u7F3A\u5C11\u5185\u5BB9\u6587\u6848");
    assert(detailSource.includes("Checkbox") && detailSource.includes("defaultChecked"), "\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u5E94\u63D0\u4F9B\u9ED8\u8BA4\u52FE\u9009\u7684 Checkbox");
    assert(detailSource.includes("t('storeOrders.detail.syncImportPriceCheckbox'"), "\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u7F3A\u5C11\u52FE\u9009\u6587\u6848");
    assert(detailSource.includes("getEditedLinePayloads(syncImportPrice)"), "\u6574\u5355\u4FDD\u5B58\u5E94\u6309\u52FE\u9009\u72B6\u6001\u51B3\u5B9A\u662F\u5426\u63D0\u4EA4\u8FDB\u53E3\u4EF7");
    assert(detailSource.includes("importPrice: importPriceChanged ? importPrice : undefined"), "\u5355\u884C\u4FDD\u5B58\u5E94\u59CB\u7EC8\u63D0\u4EA4\u5DF2\u53D8\u66F4\u7684\u8BA2\u5355\u660E\u7EC6\u8FDB\u53E3\u4EF7");
    assert(detailSource.includes("syncImportPrice: importPriceChanged ? syncImportPrice : undefined"), "\u5355\u884C\u4FDD\u5B58\u5E94\u5355\u72EC\u63D0\u4EA4\u5546\u54C1/\u5206\u5E97\u540C\u6B65\u5F00\u5173");
    assert(detailSource.includes("importPrice: importPriceChanged ? edited.importPrice : undefined"), "\u6574\u5355\u4FDD\u5B58\u5E94\u59CB\u7EC8\u63D0\u4EA4\u5DF2\u53D8\u66F4\u7684\u8BA2\u5355\u660E\u7EC6\u8FDB\u53E3\u4EF7");
    assert(detailSource.includes("syncImportPrice: importPriceChanged ? syncImportPrice : undefined"), "\u6574\u5355\u4FDD\u5B58\u5E94\u5355\u72EC\u63D0\u4EA4\u5546\u54C1/\u5206\u5E97\u540C\u6B65\u5F00\u5173");
    assert(detailSource.includes("hasImportPriceChanged(line)"), "\u5355\u884C\u4FDD\u5B58\u5E94\u5224\u65AD\u8FDB\u53E3\u4EF7\u662F\u5426\u53D8\u66F4");
    assert(detailSource.includes("payloads.some((item) => item.importPriceChanged)"), "\u6574\u5355\u4FDD\u5B58\u5E94\u5224\u65AD\u672C\u6B21\u662F\u5426\u5305\u542B\u8FDB\u53E3\u4EF7\u53D8\u66F4");
  });
  if (importPriceConfirmFailure) failures.push(importPriceConfirmFailure);
  const batchCopyOrderQuantityFailure = await runTest("\u8BE6\u60C5\u9875\u6279\u91CF\u4FEE\u6539\u5E94\u652F\u6301\u628A\u8BA2\u8D27\u6570\u91CF\u590D\u5236\u7ED9\u53D1\u8D27\u6570\u91CF", () => {
    const copyBranchStart = detailSource.indexOf("} else if (payload.type === 'copyOrderQuantityToAllocQuantity' && copyOrderQuantityPayload)");
    const copyBranchEnd = detailSource.indexOf("} else {", copyBranchStart + 1);
    const copyBranchSource = detailSource.slice(copyBranchStart, copyBranchEnd);
    assert(
      detailSource.includes("buildBatchCopyOrderQuantityPayload") && detailSource.includes("shouldSubmitBatchCopyOrderQuantity") && detailSource.includes("from './batchCopyOrderQuantity'"),
      "\u8BE6\u60C5\u9875\u5E94\u590D\u7528\u6279\u91CF\u590D\u5236\u8BA2\u8D27\u6570 helper"
    );
    assert(detailSource.includes("'copyOrderQuantityToAllocQuantity'"), "\u6279\u91CF\u4FEE\u6539\u7C7B\u578B\u5E94\u5305\u542B\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5230\u53D1\u8D27\u6570\u91CF");
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityToAllocQuantity')"), "\u6279\u91CF\u5F39\u7A97\u5E94\u5C55\u793A\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5230\u53D1\u8D27\u6570\u91CF\u9009\u9879");
    assert(detailSource.includes("payload.type === 'copyOrderQuantityToAllocQuantity'"), "\u6279\u91CF\u786E\u8BA4\u5E94\u5904\u7406\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F");
    assert(copyBranchSource.includes("const changedCopyLines = selectedLines.filter"), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u5E94\u8BA1\u7B97\u5B9E\u9645\u53D8\u5316\u884C\u6570");
    assert(copyBranchSource.includes("setEditingRows((current) => {"), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u5E94\u53EA\u5199\u9875\u9762\u8349\u7A3F");
    assert(copyBranchSource.includes("changedCopyLines.forEach"), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u5E94\u53EA\u628A\u5B9E\u9645\u53D8\u5316\u884C\u5199\u5165\u53D1\u8D27\u6570\u8349\u7A3F");
    assert(copyBranchSource.includes("allocQuantity: Number(line.quantity ?? 0)"), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u5E94\u628A\u8BA2\u8D27\u6570\u91CF\u5199\u5165\u53D1\u8D27\u6570\u8349\u7A3F");
    assert(!copyBranchSource.includes("batchUpdateStoreOrderLines("), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u4E0D\u5E94\u7ACB\u5373\u63D0\u4EA4\u540E\u7AEF");
    assert(!copyBranchSource.includes("loadDetail("), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF\u5206\u652F\u4E0D\u5E94\u7ACB\u5373\u5237\u65B0\u540E\u7AEF\u6570\u636E");
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityDraftSuccess'"), "\u590D\u5236\u8349\u7A3F\u6210\u529F\u540E\u5E94\u63D0\u793A\u7528\u6237\u70B9\u51FB\u6574\u5355\u4FDD\u5B58");
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityNoChange')"), "\u590D\u5236\u540E\u65E0\u5B9E\u9645\u53D8\u5316\u65F6\u5E94\u63D0\u793A\u672A\u4EA7\u751F\u65B0\u7684\u53D1\u8D27\u6570\u53D8\u66F4");
    assert(detailSource.includes("handleBatchConfirm({ type: 'copyOrderQuantityToAllocQuantity' })"), "\u9875\u9762\u6279\u91CF\u590D\u5236\u6309\u94AE\u5E94\u590D\u7528\u540C\u4E00\u4E2A\u6279\u91CF\u786E\u8BA4\u5206\u652F");
    assert(detailSource.includes("detailGUID: line.detailGUID"), "\u590D\u5236\u8BA2\u8D27\u6570\u91CF payload \u5E94\u643A\u5E26\u660E\u7EC6 GUID \u4EE5\u547D\u4E2D\u540E\u7AEF\u5FEB\u8DEF\u5F84");
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityConfirmTitle')"), "\u98CE\u9669\u884C\u5E94\u5F39\u51FA\u4E8C\u6B21\u786E\u8BA4\u6807\u9898");
    assert(detailSource.includes("t('storeOrders.batchCopyOrderQuantityButton')"), "\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u6279\u91CF\u590D\u5236\u6309\u94AE\u77ED\u6587\u6848");
    assert(
      detailSource.indexOf("t('storeOrders.batchCopyOrderQuantityButton')") < detailSource.indexOf("t('storeOrders.pickingList')"),
      "\u6279\u91CF\u590D\u5236\u6309\u94AE\u5E94\u653E\u5728\u914D\u8D27\u5355\u6309\u94AE\u524D\u9762"
    );
  });
  if (batchCopyOrderQuantityFailure) failures.push(batchCopyOrderQuantityFailure);
  const detailActionButtonColorFailure = await runTest("\u8BE6\u60C5\u9875\u6574\u5355\u4FDD\u5B58\u548C Excel \u7C98\u8D34\u6309\u94AE\u5E94\u4F7F\u7528\u4E0D\u540C\u989C\u8272", () => {
    assert(detailSource.includes("store-order-excel-paste-button"), "Excel \u7C98\u8D34\u6309\u94AE\u5E94\u6709\u4E13\u7528\u989C\u8272 class");
    assert(detailSource.includes("store-order-save-edited-lines-button"), "\u6574\u5355\u4FDD\u5B58\u6309\u94AE\u5E94\u6709\u4E13\u7528\u989C\u8272 class");
    assert(compactCssSource.includes(".store-order-excel-paste-button"), "\u7D27\u51D1\u6837\u5F0F\u7F3A\u5C11 Excel \u7C98\u8D34\u6309\u94AE\u989C\u8272");
    assert(compactCssSource.includes(".store-order-save-edited-lines-button"), "\u7D27\u51D1\u6837\u5F0F\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58\u6309\u94AE\u989C\u8272");
  });
  if (detailActionButtonColorFailure) failures.push(detailActionButtonColorFailure);
  const keyboardNavigationFailure = await runTest("\u8BE6\u60C5\u9875\u660E\u7EC6\u8F93\u5165\u6846\u5E94\u53EA\u652F\u6301\u4E0A\u4E0B\u65B9\u5411\u952E\u548C Enter \u79FB\u52A8\u7126\u70B9", () => {
    assert(detailSource.includes("detailInputRefs"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u660E\u7EC6\u8F93\u5165\u6846 ref map");
    assert(detailSource.includes("registerDetailInput"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u660E\u7EC6\u8F93\u5165\u6846\u6CE8\u518C\u51FD\u6570");
    assert(detailSource.includes("focusDetailInput"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u660E\u7EC6\u8F93\u5165\u6846\u805A\u7126\u51FD\u6570");
    assert(detailSource.includes("handleDetailInputKeyDown"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u952E\u76D8\u5BFC\u822A\u5904\u7406\u51FD\u6570");
    assert(!detailKeyboardHandlerSource.includes("'ArrowRight'"), "\u952E\u76D8\u5BFC\u822A\u4E0D\u5E94\u518D\u5904\u7406 ArrowRight");
    assert(!detailKeyboardHandlerSource.includes("'ArrowLeft'"), "\u952E\u76D8\u5BFC\u822A\u4E0D\u5E94\u518D\u5904\u7406 ArrowLeft");
    assert(detailKeyboardHandlerSource.includes("'ArrowDown'") && detailKeyboardHandlerSource.includes("'Enter'"), "\u952E\u76D8\u5BFC\u822A\u5E94\u5904\u7406 ArrowDown \u548C Enter");
    assert(detailKeyboardHandlerSource.includes("'ArrowUp'"), "\u952E\u76D8\u5BFC\u822A\u5E94\u5904\u7406 ArrowUp");
    assert(!detailKeyboardHandlerSource.includes("field === 'allocQuantity' ? 'importPrice' : 'allocQuantity'"), "\u5DE6\u53F3\u952E\u4E0D\u5E94\u518D\u5728\u53D1\u8D27\u6570\u548C\u8FDB\u53E3\u4EF7\u4E4B\u95F4\u79FB\u52A8");
    assert(!detailKeyboardHandlerSource.includes("nextField"), "\u4E0A\u4E0B\u65B9\u5411\u952E\u4E0D\u5E94\u518D\u5F15\u5165\u6A2A\u5411\u76EE\u6807\u5B57\u6BB5");
    assert(detailKeyboardHandlerSource.includes("event.preventDefault()"), "\u4E0A\u4E0B\u65B9\u5411\u952E\u5E94\u963B\u6B62 InputNumber \u9ED8\u8BA4\u52A0\u51CF");
    assert(detailKeyboardHandlerSource.includes("if (!nextRow)"), "\u4E0A\u4E0B\u65B9\u5411\u952E\u8D8A\u8FC7\u9996\u5C3E\u884C\u65F6\u5E94\u5B89\u5168\u8FD4\u56DE");
    assert(detailKeyboardHandlerSource.includes("focusDetailInput(nextRow.detailGUID, field)"), "\u4E0A\u4E0B\u65B9\u5411\u952E\u548C Enter \u5E94\u4FDD\u6301\u5F53\u524D\u5217\u79FB\u52A8\u7126\u70B9");
    assert(detailSource.includes("focus?.({ cursor: 'all' })"), "\u65B9\u5411\u952E\u5207\u5165\u8F93\u5165\u6846\u540E\u5E94\u9ED8\u8BA4\u5168\u9009\u6587\u672C\uFF0C\u65B9\u4FBF\u76F4\u63A5\u8986\u76D6\u7F16\u8F91");
    assert(detailMainTableSource.includes("onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, 'allocQuantity')}"), "\u53D1\u8D27\u6570\u8F93\u5165\u6846\u5E94\u7ED1\u5B9A\u952E\u76D8\u5BFC\u822A");
    assert(detailMainTableSource.includes("onKeyDown={(event) => handleDetailInputKeyDown(event, record.detailGUID, 'importPrice')}"), "\u8FDB\u53E3\u4EF7\u8F93\u5165\u6846\u5E94\u7ED1\u5B9A\u952E\u76D8\u5BFC\u822A");
    assert(!detailKeyboardHandlerSource.includes("updateStoreOrderLine") && !detailKeyboardHandlerSource.includes("batchUpdateStoreOrderLines"), "\u952E\u76D8\u79FB\u52A8\u4E0D\u5E94\u81EA\u52A8\u8C03\u7528\u4FDD\u5B58\u63A5\u53E3");
  });
  if (keyboardNavigationFailure) failures.push(keyboardNavigationFailure);
  const amountLabelsFailure = await runTest("\u8BE6\u60C5\u9875\u9876\u90E8\u91D1\u989D\u5E94\u663E\u793A\u9884\u8BA1\u9500\u552E\u989D\u3001\u53D1\u8D27\u91D1\u989D ex GST \u548C GST 10%", () => {
    assert(detailSource.includes("estimatedSalesAmount"), "\u8BE6\u60C5\u9875\u7F3A\u5C11\u9884\u8BA1\u9500\u552E\u989D\u8BA1\u7B97");
    assert(detailSource.includes("gstAmount"), "\u8BE6\u60C5\u9875\u7F3A\u5C11 GST 10% \u8BA1\u7B97");
    assert(detailSource.includes("const totalAllocQuantity = useMemo") && detailSource.includes("draftDelta"), "\u9876\u90E8\u53D1\u8D27\u6570\u91CF\u5E94\u6309\u540E\u7AEF\u603B\u6570\u53E0\u52A0\u9875\u9762\u8349\u7A3F\u5DEE\u503C");
    assert(detailSource.includes("const totalAllocVolume = useMemo") && detailSource.includes("Number(item.volume) * (Number(editedAllocQuantity)"), "\u9876\u90E8\u53D1\u8D27\u4F53\u79EF\u5E94\u6309\u9875\u9762\u8349\u7A3F\u5DEE\u503C\u66F4\u65B0");
    assert(detailSource.includes("draftTotalImportAmount") && detailSource.includes("Number(allocQuantity) * Number(importPrice) - Number(savedAmount)"), "\u53D1\u8D27\u91D1\u989D ex GST \u5E94\u6309\u9875\u9762\u8349\u7A3F\u91D1\u989D\u5DEE\u503C\u66F4\u65B0");
    assert(detailSource.includes("detail?.totalAllocatedImportAmount") && detailSource.includes("line.allocatedImportAmount"), "\u53D1\u8D27\u91D1\u989D ex GST \u5E94\u4F18\u5148\u4F7F\u7528\u53D1\u8D27/\u53D1\u7968\u91D1\u989D\u5B57\u6BB5");
    assert(detailSource.includes("line.price") && detailSource.includes("line.allocQuantity"), "\u9884\u8BA1\u9500\u552E\u989D\u5E94\u6309\u96F6\u552E\u4EF7\u548C\u5F53\u524D\u53D1\u8D27\u6570\u8BA1\u7B97");
    assert(detailSource.includes("label={t('storeOrders.orderAmountLabel')}") && detailSource.includes("formatAmount(estimatedSalesAmount)"), "\u8BA2\u5355\u91D1\u989D\u4F4D\u7F6E\u5E94\u6539\u4E3A\u663E\u793A\u9884\u8BA1\u9500\u552E\u989D");
    assert(detailSource.includes("label={t('storeOrders.importAmountLabel')}") && detailSource.includes("formatAmount(draftTotalImportAmount)"), "\u53D1\u8D27\u91D1\u989D ex GST \u5E94\u663E\u793A\u8349\u7A3F\u603B\u91D1\u989D");
    assert(detailSource.includes("label={t('storeOrders.gstAmountLabel')}") && detailSource.includes("formatAmount(gstAmount)"), "\u8BE6\u60C5\u9875\u5E94\u65B0\u589E GST 10% \u663E\u793A");
    assert(detailMainTableSource.includes("Number(edited.allocQuantity ?? record.allocQuantity ?? 0) * Number(edited.importPrice ?? record.importPrice ?? 0)"), "\u660E\u7EC6\u8FDB\u53E3\u91D1\u989D\u5E94\u6309\u5F53\u524D\u8349\u7A3F\u53D1\u8D27\u6570\u548C\u8FDB\u53E3\u4EF7\u663E\u793A");
    assert(detailMainTableSource.includes("sortOrder: detailColumnSortOrder('allocatedImportAmount')"), "\u660E\u7EC6\u53D1\u8D27\u91D1\u989D\u5217\u5E94\u6309 allocatedImportAmount \u53D1\u8D77\u670D\u52A1\u7AEF\u6392\u5E8F");
    assert(detailMainTableSource.includes("editedAllocQuantity !== undefined") && detailMainTableSource.includes("Number(record.volume) * Number(editedAllocQuantity)"), "\u660E\u7EC6\u53D1\u8D27\u4F53\u79EF\u5E94\u6309\u5F53\u524D\u8349\u7A3F\u53D1\u8D27\u6570\u663E\u793A");
  });
  if (amountLabelsFailure) failures.push(amountLabelsFailure);
  const packageScriptFailure = await runTest("\u8BA2\u8D27\u660E\u7EC6\u6807\u51C6\u6D4B\u8BD5\u811A\u672C\u5E94\u5305\u542B\u7D27\u51D1 UI \u7EA6\u675F", () => {
    assert(packageSource.includes("storeOrderCompactUi.logic.test.ts"), "test:store-order-detail \u5E94\u63A5\u5165 storeOrderCompactUi.logic.test.ts");
  });
  if (packageScriptFailure) failures.push(packageScriptFailure);
  const printIsolationFailure = await runTest("\u672C\u6B21\u7D27\u51D1\u6837\u5F0F\u4E0D\u5E94\u63A5\u5165\u6253\u5370\u9875\u9762", () => {
    assert(!pickingListSource.includes("./compact.css"), "\u914D\u8D27\u5355\u6253\u5370\u9875\u4E0D\u5E94\u5F15\u5165\u9875\u9762\u7D27\u51D1\u6837\u5F0F");
    assert(!invoiceSource.includes("./compact.css"), "\u53D1\u7968\u9875\u4E0D\u5E94\u5F15\u5165\u9875\u9762\u7D27\u51D1\u6837\u5F0F");
    assert(!printCssSource.includes("store-order-list-table"), "\u6253\u5370 CSS \u4E0D\u5E94\u5305\u542B\u5217\u8868\u9875\u7D27\u51D1\u6837\u5F0F");
    assert(!printCssSource.includes("store-order-detail-table"), "\u6253\u5370 CSS \u4E0D\u5E94\u5305\u542B\u8BE6\u60C5\u9875\u7D27\u51D1\u6837\u5F0F");
  });
  if (printIsolationFailure) failures.push(printIsolationFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("storeOrderCompactUi.logic.test: ok");
}
await main();
