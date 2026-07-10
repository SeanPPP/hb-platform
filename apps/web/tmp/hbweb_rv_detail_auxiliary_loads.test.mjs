// src/pages/Warehouse/StoreOrders/detailAuxiliaryLoads.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/utils/detailLoadState.ts
function shouldShowDetailInitialLoading({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId
}) {
  if (!requestedDetailId) {
    return false;
  }
  return loadedDetailId !== requestedDetailId || visibleDetailId !== requestedDetailId;
}
function shouldSkipDetailAutoReload({
  requestedDetailId,
  loadedDetailId,
  visibleDetailId,
  requestedDetailQueryKey,
  loadedDetailQueryKey
}) {
  if (!requestedDetailId) {
    return false;
  }
  const isSameDetail = loadedDetailId === requestedDetailId && visibleDetailId === requestedDetailId;
  if (!isSameDetail) {
    return false;
  }
  if (requestedDetailQueryKey !== void 0 || loadedDetailQueryKey !== void 0) {
    return requestedDetailQueryKey === loadedDetailQueryKey;
  }
  return true;
}

// src/pages/Warehouse/StoreOrders/detailLoadState.ts
function shouldShowStoreOrderDetailInitialLoading({
  requestedOrderId,
  loadedOrderId,
  visibleDetailId
}) {
  return shouldShowDetailInitialLoading({
    requestedDetailId: requestedOrderId,
    loadedDetailId: loadedOrderId,
    visibleDetailId
  });
}

// src/pages/Warehouse/StoreOrders/detailAuxiliaryLoads.logic.test.ts
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
var pickingListFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx");
var invoiceFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Invoice.tsx");
var containerDetailFile = path.resolve(process.cwd(), "src/pages/Warehouse/ContainerDetail/index.tsx");
var localSupplierInvoiceDetailFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoiceDetailPage/index.tsx");
var localSupplierInvoiceEditFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx");
var detailLoadStateFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/detailLoadState.ts");
var sharedDetailLoadStateFile = path.resolve(process.cwd(), "src/utils/detailLoadState.ts");
var zhFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var enFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
function readSource(file) {
  return readFileSync(file, "utf8").replace(/\r\n/g, "\n");
}
var detailSource = readSource(detailFile);
var pickingListSource = readSource(pickingListFile);
var invoiceSource = readSource(invoiceFile);
var containerDetailSource = readSource(containerDetailFile);
var localSupplierInvoiceDetailSource = readSource(localSupplierInvoiceDetailFile);
var localSupplierInvoiceEditSource = readSource(localSupplierInvoiceEditFile);
var detailLoadStateSource = readSource(detailLoadStateFile);
var sharedDetailLoadStateSource = readSource(sharedDetailLoadStateFile);
var zhSource = readSource(zhFile);
var enSource = readSource(enFile);
async function main() {
  const failures = [];
  const auxiliaryWarningFailure = await runTest("\u5206\u5E97\u4E0B\u62C9\u52A0\u8F7D\u5931\u8D25\u5E94\u964D\u7EA7\u4E3A\u975E\u963B\u65AD\u63D0\u793A", () => {
    assert(
      detailSource.includes("message.warning(t('storeOrders.detail.loadStoreOptionsFailed'"),
      "loadStores \u5931\u8D25\u65F6\u5E94\u4F7F\u7528\u975E\u963B\u65AD warning \u6587\u6848\uFF0C\u907F\u514D\u8BEF\u63D0\u793A\u6574\u5F20\u8BA2\u8D27\u660E\u7EC6\u5931\u8D25"
    );
    assert(
      !detailSource.includes("message.error(error instanceof Error ? error.message : t('storeOrders.loadStoresFailed'))"),
      "loadStores \u5931\u8D25\u65F6\u4E0D\u5E94\u76F4\u63A5\u900F\u4F20\u540E\u7AEF\u9519\u8BEF message"
    );
  });
  if (auxiliaryWarningFailure) failures.push(auxiliaryWarningFailure);
  const warehouseStaffStoreSelectorFailure = await runTest("\u4ED3\u5E93\u5458\u5DE5\u660E\u7EC6\u9875\u4E0D\u5E94\u8BF7\u6C42\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9", () => {
    assert(
      detailSource.includes("if (!canUseWarehouseManagerActions)") && detailSource.includes("setStores([])") && detailSource.includes("lastLoadedStoresQueryKeyRef.current = storesQueryKey") && detailSource.includes("return\n    }\n\n    setStoresLoading(true)"),
      "\u975E\u4ED3\u5E93\u7BA1\u7406\u5458\u5E94\u8DF3\u8FC7\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9\u63A5\u53E3\uFF0C\u907F\u514D WarehouseStaff \u56E0 /api/stores 403 \u770B\u5230\u5206\u5E97\u663E\u793A\u5931\u8D25"
    );
    assert(
      detailSource.includes("if (headerForm.storeCode && !options.some((item) => item.value === headerForm.storeCode))") && detailSource.includes("const currentStoreLabel = detail?.storeName") && detailSource.includes("`${detail.storeName} (${headerForm.storeCode})`") && detailSource.includes("`${headerForm.storeCode} (${t('column.currentStore')})`"),
      "\u5206\u5E97\u4E0B\u62C9\u8DF3\u8FC7\u540E\u5E94\u4F18\u5148\u4F7F\u7528\u660E\u7EC6\u63A5\u53E3\u8FD4\u56DE\u7684 storeName \u663E\u793A\u5F53\u524D\u8BA2\u5355\u5206\u5E97"
    );
    assert(
      !detailSource.includes("userGUID: canViewAllStores ? undefined : currentUser?.userGUID"),
      "\u8BE6\u60C5\u9875\u4E0D\u5E94\u7EE7\u7EED\u4E3A\u4ED3\u5E93\u5458\u5DE5\u8BF7\u6C42\u6309\u7528\u6237\u8FC7\u6EE4\u7684\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9"
    );
  });
  if (warehouseStaffStoreSelectorFailure) failures.push(warehouseStaffStoreSelectorFailure);
  const translationFailure = await runTest("\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A\u5E94\u6709\u4E2D\u82F1\u6587\u6587\u6848", () => {
    assert(
      zhSource.includes('"loadStoreOptionsFailed": "\u5206\u5E97\u4E0B\u62C9\u52A0\u8F7D\u5931\u8D25\uFF0C\u8BA2\u5355\u660E\u7EC6\u53EF\u7EE7\u7EED\u67E5\u770B"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A"
    );
    assert(
      enSource.includes('"loadStoreOptionsFailed": "Store selector failed to load. Order details remain available."'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u5206\u5E97\u4E0B\u62C9\u975E\u963B\u65AD\u63D0\u793A"
    );
  });
  if (translationFailure) failures.push(translationFailure);
  const detailSaveTranslationFailure = await runTest("\u8BA2\u8D27\u660E\u7EC6\u4FDD\u5B58\u548C\u91D1\u989D\u663E\u793A\u5E94\u6709\u4E2D\u82F1\u6587\u6587\u6848", () => {
    assert(zhSource.includes('"saveEditedLines": "\u6574\u5355\u4FDD\u5B58"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58");
    assert(enSource.includes('"saveEditedLines": "Save All Lines"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u6574\u5355\u4FDD\u5B58");
    assert(zhSource.includes('"importPriceSyncConfirmTitle": "\u786E\u8BA4\u540C\u6B65\u8FDB\u53E3\u4EF7"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u6807\u9898");
    assert(enSource.includes('"importPriceSyncConfirmTitle": "Confirm Import Price Sync"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u6807\u9898");
    assert(
      zhSource.includes('"importPriceSyncConfirmContent": "\u8FDB\u53E3\u4EF7\u4FDD\u5B58\u540E\u4F1A\u540C\u6B65\u5199\u5165\u4ED3\u5E93\u5546\u54C1\u8868\u548C\u5206\u5E97\u8868\uFF0C\u8BF7\u786E\u8BA4\u662F\u5426\u7EE7\u7EED\u3002"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u5185\u5BB9"
    );
    assert(
      enSource.includes('"importPriceSyncConfirmContent": "After saving, import prices will sync to warehouse products and store products. Continue?"'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8FDB\u53E3\u4EF7\u540C\u6B65\u786E\u8BA4\u5185\u5BB9"
    );
    assert(zhSource.includes('"syncImportPriceCheckbox": "\u540C\u6B65\u8FDB\u53E3\u4EF7\u5230\u4ED3\u5E93\u5546\u54C1\u8868\u548C\u5206\u5E97\u8868"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u8FDB\u53E3\u4EF7\u52FE\u9009\u9879");
    assert(enSource.includes('"syncImportPriceCheckbox": "Sync import price to warehouse products and store products"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u540C\u6B65\u8FDB\u53E3\u4EF7\u52FE\u9009\u9879");
    assert(zhSource.includes('"orderAmountLabel": "\u9884\u8BA1\u9500\u552E\u989D"'), "\u4E2D\u6587\u8BA2\u5355\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A\u9884\u8BA1\u9500\u552E\u989D");
    assert(enSource.includes('"orderAmountLabel": "Estimated Sales"'), "\u82F1\u6587\u8BA2\u5355\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A Estimated Sales");
    assert(zhSource.includes('"importAmountLabel": "\u53D1\u8D27\u91D1\u989D ex GST"'), "\u4E2D\u6587\u8FDB\u53E3\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A\u53D1\u8D27\u91D1\u989D ex GST");
    assert(enSource.includes('"importAmountLabel": "Allocated Amount ex GST"'), "\u82F1\u6587\u8FDB\u53E3\u91D1\u989D\u6807\u7B7E\u5E94\u6539\u4E3A Allocated Amount ex GST");
    assert(zhSource.includes('"gstAmountLabel": "GST 10%"'), "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11 GST 10%");
    assert(enSource.includes('"gstAmountLabel": "GST 10%"'), "\u82F1\u6587\u6587\u6848\u7F3A\u5C11 GST 10%");
  });
  if (detailSaveTranslationFailure) failures.push(detailSaveTranslationFailure);
  const editabilityStateFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u590D\u7528\u8BA2\u5355\u72B6\u6001\u6743\u9650\u6D3E\u751F\u51FD\u6570", () => {
    assert(
      detailSource.includes("import { deriveStoreOrderDetailPermissions } from './storeOrderDetailPermissions'") && detailSource.includes("} = deriveStoreOrderDetailPermissions(detail?.flowStatus)"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u590D\u7528 deriveStoreOrderDetailPermissions \u6D3E\u751F\u72B6\u6001\u6743\u9650"
    );
  });
  if (editabilityStateFailure) failures.push(editabilityStateFailure);
  const editGuardFailure = await runTest("\u4E0D\u53EF\u7F16\u8F91\u8BA2\u5355\u7684\u5199\u5165\u53E3\u5E94\u5148\u8D70\u7EDF\u4E00 guard", () => {
    assert(
      detailSource.includes("function ensureOrderEditable") && detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))") && detailSource.includes("if (!ensureOrderEditable())") && detailSource.includes("handleSaveLine") && detailSource.includes("handleConfirmPaste"),
      "\u8BE6\u60C5\u9875\u5199\u64CD\u4F5C\u5C1A\u672A\u7EDF\u4E00\u62E6\u622A\u4E0D\u53EF\u7F16\u8F91\u8BA2\u5355"
    );
  });
  if (editGuardFailure) failures.push(editGuardFailure);
  const flowGuardFailure = await runTest("\u72B6\u6001\u6D41\u8F6C\u5199\u5165\u53E3\u5E94\u6709\u51FD\u6570\u5185\u4E8C\u6B21\u95E8\u7981", () => {
    assert(
      detailSource.includes("if (!canUseWarehouseManagerActions || !canStartPicking)") && detailSource.includes("if (!canUseWarehouseManagerActions || !canCompleteOrder)") && detailSource.includes("message.warning(t('storeOrders.detail.orderReadonlyRefresh'))"),
      "\u5F00\u59CB\u914D\u8D27/\u5B8C\u6210\u8BA2\u5355\u51FD\u6570\u5165\u53E3\u5C1A\u672A\u6309\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u548C\u72B6\u6001\u4E8C\u6B21\u62E6\u622A"
    );
  });
  if (flowGuardFailure) failures.push(flowGuardFailure);
  const completeOrderOutboundDateFailure = await runTest("\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u5E94\u53EA\u5728\u51FA\u5E93\u65E5\u671F\u4E3A\u7A7A\u65F6\u8865\u5F53\u5929", () => {
    const completeOrderSource = detailSource.slice(
      detailSource.indexOf("const handleCompleteOrder"),
      detailSource.indexOf("const handleChangeOrderStatus")
    );
    assert(detailSource.includes("function formatLocalDateForInput"), "\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u672C\u5730\u65E5\u671F\u683C\u5F0F\u5316 helper\uFF0C\u907F\u514D UTC \u65E5\u671F\u504F\u79FB");
    assert(!detailSource.includes("completeStoreOrder,"), "\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u4E0D\u5E94\u518D\u5BFC\u5165\u76F4\u63A5\u5B8C\u6210\u63A5\u53E3");
    assert(!completeOrderSource.includes("completeStoreOrder(detail.orderGUID)"), "\u8BE6\u60C5\u9875\u5B8C\u6210\u8BA2\u5355\u4E0D\u5E94\u76F4\u63A5\u8C03\u7528\u5B8C\u6210\u63A5\u53E3");
    assert(
      completeOrderSource.includes("const currentOutboundDate = headerForm.outboundDate?.slice(0, 10)") && completeOrderSource.includes("const nextOutboundDate = currentOutboundDate || formatLocalDateForInput()") && completeOrderSource.includes("updateStoreOrderOutboundDate({") && completeOrderSource.includes("outboundDate: nextOutboundDate") && completeOrderSource.includes("completeOrder: true"),
      "\u5B8C\u6210\u8BA2\u5355\u5E94\u590D\u7528\u51FA\u5E93\u65E5\u671F\u63A5\u53E3\uFF1A\u5DF2\u6709\u51FA\u5E93\u65E5\u671F\u5219\u4FDD\u7559\uFF0C\u7A7A\u51FA\u5E93\u65E5\u671F\u624D\u8865\u5F53\u5929\u5E76\u540C\u6B65\u5B8C\u6210\u8BA2\u5355"
    );
  });
  if (completeOrderOutboundDateFailure) failures.push(completeOrderOutboundDateFailure);
  const disabledUiFailure = await runTest("\u975E\u4ED3\u5E93\u7BA1\u7406\u5458\u5E94\u7981\u7528\u8868\u5934\u548C\u660E\u7EC6\u5199\u63A7\u4EF6\uFF0C\u4EC5\u4FDD\u7559 WarehouseStaff \u53EA\u8BFB\u914D\u8D27\u5355\u5165\u53E3", () => {
    const orderDetailSectionSource = detailSource.slice(
      detailSource.indexOf("title={t('storeOrders.orderDetailSection')}"),
      detailSource.indexOf('className="store-order-detail-filter-bar"')
    );
    const pickingButtonSource = orderDetailSectionSource.slice(
      orderDetailSectionSource.indexOf("icon={<PrinterOutlined />}"),
      orderDetailSectionSource.indexOf("t('storeOrders.pickingList')")
    );
    const pickingButtonPosition = orderDetailSectionSource.indexOf("t('storeOrders.pickingList')");
    const managerGuardPosition = orderDetailSectionSource.lastIndexOf("{canUseWarehouseManagerActions ? (", pickingButtonPosition);
    const managerGuardClosePosition = orderDetailSectionSource.lastIndexOf(") : null}", pickingButtonPosition);
    assert(
      detailSource.includes("disabled={!canUseWarehouseManagerActions || isReadonlyOrder}") && detailSource.includes("disabled={!canUseWarehouseManagerActions || isReadonlyOrder || validPastePreviewCount === 0}") && detailSource.includes("disabled={isReadonlyOrder || !canStartPicking}") && detailSource.includes("disabled={!canCompleteOrder}") && detailSource.includes("extra={\n                  canUseWarehouseManagerActions ? (") && detailSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly") && detailSource.includes("const canUseStoreOrderDocumentActions = access.isWarehouseStaff") && detailSource.includes("const canUseStoreOrderDetailExtraActions = canUseWarehouseManagerActions || canUseStoreOrderDocumentActions") && orderDetailSectionSource.includes("canUseStoreOrderDetailExtraActions ? (\n                  <Space wrap>") && pickingButtonSource.includes("navigate(`/warehouse/store-order/picking/${detail.orderGUID}`)") && managerGuardPosition <= managerGuardClosePosition && detailSource.includes("rowSelection={\n                  canUseWarehouseManagerActions"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u6309\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650\u7981\u7528\u5199\u63A7\u4EF6\uFF0C\u6216\u672A\u4E3A WarehouseStaff \u4FDD\u7559\u53EA\u8BFB\u914D\u8D27\u5355\u5165\u53E3"
    );
  });
  if (disabledUiFailure) failures.push(disabledUiFailure);
  const statusChangeFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u4E09\u72B6\u6001\u8BA2\u5355\u72B6\u6001\u66F4\u6539\u5165\u53E3", () => {
    assert(
      detailSource.includes("updateStoreOrderStatus") && detailSource.includes("handleChangeOrderStatus") && detailSource.includes("orderStatusChangeOptions") && detailSource.includes("StoreOrderFlowStatus.Submitted") && detailSource.includes("StoreOrderFlowStatus.Picking") && detailSource.includes("StoreOrderFlowStatus.Completed") && detailSource.includes("t('storeOrders.detail.changeOrderStatus'") && detailSource.includes("t('storeOrders.detail.statusChangeSuccess'"),
      "\u8BE6\u60C5\u9875\u5C1A\u672A\u63D0\u4F9B\u4E09\u72B6\u6001\u8BA2\u5355\u72B6\u6001\u66F4\u6539\u5165\u53E3"
    );
  });
  if (statusChangeFailure) failures.push(statusChangeFailure);
  const keepAliveSkipAutoReloadFailure = await runTest("\u8BE6\u60C5\u9875 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    assert(
      detailSource.includes("loadedDetailIdRef") && detailSource.includes("useKeepAliveContext") && detailSource.includes("const { active } = useKeepAliveContext()") && detailSource.includes("if (!active) return") && detailSource.includes("visibleDetailIdRef") && detailSource.includes("lastLoadedDetailQueryKeyRef") && detailSource.includes("shouldSkipDetailAutoReload({") && detailSource.includes("shouldShowStoreOrderDetailInitialLoading({") && detailSource.includes("active,") && detailSource.includes("return () => {") && detailSource.includes("detailRequestControllerRef.current?.abort()"),
      "\u8BE6\u60C5\u9875\u7F3A\u5C11 KeepAlive active \u5B88\u536B\uFF0C\u9690\u85CF Tab \u4F1A\u8DDF\u968F\u5168\u5C40\u8DEF\u7531\u53D8\u5316\u91CD\u65B0\u8BF7\u6C42"
    );
    assert(
      detailSource.includes("loadedDetailIdRef.current = result.orderGUID || id") && detailSource.includes("visibleDetailIdRef.current = result.orderGUID || id") && detailSource.includes("lastLoadedDetailQueryKeyRef.current = detailQueryKey"),
      "\u8BE6\u60C5\u9875\u52A0\u8F7D\u6210\u529F\u540E\u5E94\u8BB0\u5F55\u5DF2\u52A0\u8F7D\u8BA2\u5355 id \u548C\u67E5\u8BE2\u53C2\u6570\uFF0C\u540E\u7EED\u540C\u8BA2\u5355\u540C\u67E5\u8BE2\u624D\u80FD\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
  });
  if (keepAliveSkipAutoReloadFailure) failures.push(keepAliveSkipAutoReloadFailure);
  const initialLoadingDecisionFailure = await runTest("\u8BE6\u60C5\u9875\u521D\u59CB\u52A0\u8F7D\u548C\u81EA\u52A8\u5237\u65B0\u8DF3\u8FC7\u5224\u65AD\u5E94\u8986\u76D6\u5207\u56DE\u548C\u6362\u5355\u8FB9\u754C", () => {
    assert(
      sharedDetailLoadStateSource.includes("loadedDetailId !== requestedDetailId || visibleDetailId !== requestedDetailId") && sharedDetailLoadStateSource.includes("export function shouldSkipDetailAutoReload") && detailLoadStateSource.includes("shouldShowDetailInitialLoading"),
      "\u521D\u59CB\u52A0\u8F7D\u548C\u81EA\u52A8\u5237\u65B0\u8DF3\u8FC7\u5224\u65AD\u5E94\u6C89\u5230\u901A\u7528 helper\uFF0C\u5E76\u540C\u65F6\u68C0\u67E5\u5DF2\u52A0\u8F7D\u8BB0\u5F55\u548C\u5F53\u524D\u53EF\u5C55\u793A\u8BB0\u5F55"
    );
    assert(
      !shouldShowDetailInitialLoading({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: "order-a"
      }),
      "\u540C\u8BA2\u5355\u4E14\u5F53\u524D\u4ECD\u6709\u53EF\u5C55\u793A\u660E\u7EC6\u65F6\u5E94\u9759\u9ED8\u5237\u65B0"
    );
    assert(
      shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }),
      "\u540C\u8BE6\u60C5\u4E14\u5F53\u524D\u4ECD\u6709\u53EF\u5C55\u793A\u5185\u5BB9\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-b",
        loadedOrderId: "order-a",
        visibleDetailId: "order-a"
      }),
      "\u5207\u5230\u65B0\u8BA2\u5355\u65F6\u5E94\u663E\u793A\u9996\u6B21\u4E3B\u52A0\u8F7D"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: null
      }),
      "\u5F53\u524D\u6CA1\u6709\u53EF\u5C55\u793A\u660E\u7EC6\u65F6\u5373\u4F7F\u5DF2\u52A0\u8F7D\u6807\u8BB0\u547D\u4E2D\u4E5F\u5E94\u663E\u793A\u4E3B\u52A0\u8F7D"
    );
    assert(
      shouldShowStoreOrderDetailInitialLoading({
        requestedOrderId: "order-a",
        loadedOrderId: "order-a",
        visibleDetailId: "order-b"
      }),
      "\u5F53\u524D\u53EF\u5C55\u793A\u660E\u7EC6\u5C5E\u4E8E\u5176\u4ED6\u8BA2\u5355\u65F6\u5E94\u663E\u793A\u4E3B\u52A0\u8F7D\uFF0C\u907F\u514D\u77ED\u6682\u5C55\u793A\u9519\u8BEF\u8BA2\u5355\u72B6\u6001"
    );
    assert(
      !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-b",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a"
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: null
      }),
      "\u6362\u8BE6\u60C5\u3001\u7A7A id \u6216\u6CA1\u6709\u53EF\u5C55\u793A\u5185\u5BB9\u65F6\u4E0D\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0"
    );
    assert(
      shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a",
        requestedDetailQueryKey: '{"pageNumber":1}',
        loadedDetailQueryKey: '{"pageNumber":1}'
      }) && !shouldSkipDetailAutoReload({
        requestedDetailId: "detail-a",
        loadedDetailId: "detail-a",
        visibleDetailId: "detail-a",
        requestedDetailQueryKey: '{"pageNumber":2}',
        loadedDetailQueryKey: '{"pageNumber":1}'
      }),
      "\u95E8\u5E97\u8BA2\u5355\u8BE6\u60C5\u67E5\u8BE2\u53C2\u6570\u4E00\u81F4\u624D\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\uFF0C\u5206\u9875\u641C\u7D22\u6392\u5E8F\u53D8\u5316\u5FC5\u987B\u91CD\u65B0\u8BF7\u6C42"
    );
  });
  if (initialLoadingDecisionFailure) failures.push(initialLoadingDecisionFailure);
  const silentFailurePreserveFailure = await runTest("\u8BE6\u60C5\u9875\u9759\u9ED8\u5237\u65B0\u5931\u8D25\u4E0D\u5E94\u6E05\u7A7A\u5F53\u524D\u660E\u7EC6", () => {
    assert(
      detailSource.includes("const errorMessage = error instanceof Error ? error.message : t('storeOrders.detail.loadDetailFailed')") && detailSource.includes("if (showLoading)") && detailSource.includes("setDetail(null)") && detailSource.includes("setDetailLoadStatus('error')") && detailSource.includes("setDetailErrorMessage(errorMessage)") && detailSource.includes("message.error(errorMessage)"),
      "\u9759\u9ED8\u5237\u65B0\u5931\u8D25\u65F6\u5E94\u4FDD\u7559\u65E7 detail\uFF0C\u53EA\u63D0\u793A\u9519\u8BEF\uFF1B\u9996\u6B21\u52A0\u8F7D\u5931\u8D25\u624D\u8FDB\u5165 error \u7A7A\u6001"
    );
  });
  if (silentFailurePreserveFailure) failures.push(silentFailurePreserveFailure);
  const storeOrderPrintPagesKeepAliveFailure = await runTest("\u914D\u8D27\u5355\u548C\u53D1\u7968 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    for (const [pageName, source, loadFailureKey] of [
      ["\u914D\u8D27\u5355", pickingListSource, "warehouse.pickingList.loadFailed"],
      ["\u53D1\u7968", invoiceSource, "warehouse.invoice.loadFailed"]
    ]) {
      assert(
        source.includes("import { shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && source.includes("loadedOrderIdRef") && source.includes("visibleOrderIdRef") && source.includes("const load = async (showLoading = true)") && source.includes("if (showLoading) {") && source.includes("setLoading(true)") && source.includes("shouldSkipDetailAutoReload({") && source.includes("return"),
        `${pageName}\u7F3A\u5C11\u540C\u8BA2\u5355 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4`
      );
      assert(
        source.includes("loadedOrderIdRef.current = detail.orderGUID || id") && source.includes("visibleOrderIdRef.current = detail.orderGUID || id") && source.includes(`const errorMessage = error instanceof Error ? error.message : t('${loadFailureKey}')`) && source.includes("if (showLoading) {") && source.includes("setOrder(null)") && source.includes("setStore(null)"),
        `${pageName}\u5E94\u5728\u6210\u529F\u540E\u8BB0\u5F55\u53EF\u5C55\u793A\u8BA2\u5355\uFF0C\u4E14\u9996\u6B21\u52A0\u8F7D\u5931\u8D25\u624D\u6E05\u7A7A\u5F53\u524D\u5185\u5BB9`
      );
    }
  });
  if (storeOrderPrintPagesKeepAliveFailure) failures.push(storeOrderPrintPagesKeepAliveFailure);
  const warehouseStaffPickingStoreLoadFailure = await runTest("\u914D\u8D27\u5355\u9875 WarehouseStaff \u4E0D\u5E94\u8BF7\u6C42\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9", () => {
    assert(
      pickingListSource.includes("import { useAuthStore } from '../../../store/auth'") && pickingListSource.includes("const { access } = useAuthStore()") && pickingListSource.includes("const canUseWarehouseManagerActions = access.canManageWarehouseOrders && !isWarehouseStaffOnly") && pickingListSource.includes("if (detail.storeCode && canUseWarehouseManagerActions)") && pickingListSource.includes("WarehouseStaff \u65E0\u9700\u52A0\u8F7D\u5B8C\u6574\u5206\u5E97\u4E0B\u62C9") && pickingListSource.includes("store?.storeName || order.storeName || order.storeCode"),
      "\u914D\u8D27\u5355\u9875\u5E94\u8DF3\u8FC7 WarehouseStaff \u7684\u5B8C\u6574\u5206\u5E97\u63A5\u53E3\u8BF7\u6C42\uFF0C\u5E76\u4F7F\u7528\u8BA2\u5355\u8BE6\u60C5\u4E2D\u7684 storeName/storeCode \u5C55\u793A"
    );
  });
  if (warehouseStaffPickingStoreLoadFailure) failures.push(warehouseStaffPickingStoreLoadFailure);
  const warehouseStaffPickingPrintFailure = await runTest("\u914D\u8D27\u5355\u9875 WarehouseStaff \u6253\u5370\u4E0B\u8F7D\u4E0D\u5E94\u89E6\u53D1\u72B6\u6001\u6D41\u8F6C\u5199\u63A5\u53E3", () => {
    const beforePrintSource = pickingListSource.slice(
      pickingListSource.indexOf("const handleBeforePrint = async () => {"),
      pickingListSource.indexOf("const handlePrint = async () => {")
    );
    assert(
      beforePrintSource.includes("WarehouseStaff \u53EF\u6253\u5370/\u4E0B\u8F7D\u914D\u8D27\u5355") && beforePrintSource.includes("if (canUseWarehouseManagerActions && order.flowStatus === StoreOrderFlowStatus.Submitted)") && beforePrintSource.includes("await startPickingStoreOrder(order.orderGUID)") && pickingListSource.includes("await handleBeforePrint()") && pickingListSource.includes("await printElementPagesAsPdf") && pickingListSource.includes("await downloadElementPagesAsPdf"),
      "\u914D\u8D27\u5355\u6253\u5370/\u4E0B\u8F7D\u524D\u53EA\u6709\u4ED3\u5E93\u7BA1\u7406\u5458\u53EF\u81EA\u52A8\u5F00\u59CB\u914D\u8D27\uFF0CWarehouseStaff \u4E0D\u80FD\u89E6\u53D1 start-picking \u5199\u63A5\u53E3"
    );
  });
  if (warehouseStaffPickingPrintFailure) failures.push(warehouseStaffPickingPrintFailure);
  const lowRiskDetailPagesKeepAliveFailure = await runTest("\u4F4E\u98CE\u9669\u8BE6\u60C5\u9875 Tab \u5207\u56DE\u5E94\u4FDD\u7559\u5DF2\u6709\u5185\u5BB9\u5E76\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0", () => {
    assert(
      containerDetailSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && containerDetailSource.includes("useKeepAliveContext") && containerDetailSource.includes("const { active } = useKeepAliveContext()") && containerDetailSource.includes("if (!active) return") && containerDetailSource.includes("loadedContainerGuidRef") && containerDetailSource.includes("visibleContainerGuidRef") && containerDetailSource.includes("lastLoadedContainerDetailSuccessRef") && containerDetailSource.includes("const loadData = async (showLoading = true)") && containerDetailSource.includes("shouldSkipDetailAutoReload({") && containerDetailSource.includes("const activeLoadQueryKey = detailQueryKey") && containerDetailSource.includes("requestedDetailQueryKey: activeLoadQueryKey") && containerDetailSource.includes("loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid") && containerDetailSource.includes("void loadHeader(shouldShowInitialLoading)") && containerDetailSource.includes("loadDetailChunk(1, 'reset')") && containerDetailSource.includes("loadedContainerGuidRef.current = containerGuid") && containerDetailSource.includes("visibleContainerGuidRef.current = containerGuid") && containerDetailSource.includes("lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: detailQueryKey }"),
      "\u8D27\u67DC\u8BE6\u60C5\u7F3A\u5C11 KeepAlive active \u5B88\u536B\u6216\u660E\u7EC6\u67E5\u8BE2\u6761\u4EF6\u7F13\u5B58\u4FDD\u62A4"
    );
    assert(
      containerDetailSource.includes("setDetailTableRenderKey((value) => value + 1)") && containerDetailSource.includes("detailTableRef.current?.scrollTo?.({ top: scrollTop })") && containerDetailSource.indexOf("setDetailTableRenderKey((value) => value + 1)") > containerDetailSource.indexOf("if (!active || wasActive || rows.length === 0)") && containerDetailSource.indexOf("loadDetailChunk(1, 'reset')") < containerDetailSource.indexOf("setDetailTableRenderKey((value) => value + 1)"),
      "\u8D27\u67DC\u660E\u7EC6 Tab \u5207\u56DE\u5DF2\u6709\u6570\u636E\u65F6\u5E94\u53EA\u6062\u590D\u865A\u62DF\u8868\u683C\u6D4B\u91CF\uFF0C\u4E0D\u80FD\u901A\u8FC7\u91CD\u65B0\u52A0\u8F7D\u660E\u7EC6\u4FEE\u590D\u7A7A\u767D"
    );
    assert(
      localSupplierInvoiceDetailSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../utils/detailLoadState'") && localSupplierInvoiceDetailSource.includes("loadedInvoiceGuidRef") && localSupplierInvoiceDetailSource.includes("visibleInvoiceGuidRef") && localSupplierInvoiceDetailSource.includes("const loadInvoice = async (showLoading = true)") && localSupplierInvoiceDetailSource.includes("shouldSkipDetailAutoReload({") && localSupplierInvoiceDetailSource.includes("loadedInvoiceGuidRef.current = invoiceGuid") && localSupplierInvoiceDetailSource.includes("visibleInvoiceGuidRef.current = invoiceGuid"),
      "\u672C\u5730\u4F9B\u5E94\u5546\u53D1\u7968\u53EA\u8BFB\u8BE6\u60C5\u7F3A\u5C11\u540C\u53D1\u7968 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4"
    );
    assert(
      localSupplierInvoiceEditSource.includes("import { shouldShowDetailInitialLoading, shouldSkipDetailAutoReload } from '../../../../utils/detailLoadState'") && localSupplierInvoiceEditSource.includes("loadedInvoiceGuidRef") && localSupplierInvoiceEditSource.includes("visibleInvoiceGuidRef") && localSupplierInvoiceEditSource.includes("const loadInvoice = useCallback(async (showLoading = true)") && localSupplierInvoiceEditSource.includes("shouldSkipDetailAutoReload({") && localSupplierInvoiceEditSource.includes("loadedInvoiceGuidRef.current = invoiceGuid") && localSupplierInvoiceEditSource.includes("visibleInvoiceGuidRef.current = invoiceGuid"),
      "\u672C\u5730\u4F9B\u5E94\u5546\u53D1\u7968\u7F16\u8F91\u9875\u7F3A\u5C11\u540C\u53D1\u7968 Tab \u6062\u590D\u8DF3\u8FC7\u81EA\u52A8\u5237\u65B0\u4FDD\u62A4"
    );
  });
  if (lowRiskDetailPagesKeepAliveFailure) failures.push(lowRiskDetailPagesKeepAliveFailure);
  const readonlyCopyFailure = await runTest("\u53EA\u8BFB\u72B6\u6001\u5E94\u63D0\u4F9B\u4E2D\u82F1\u6587\u63D0\u793A\u6587\u6848", () => {
    assert(
      zhSource.includes('"orderReadonlyTitle": "\u5F53\u524D\u8BA2\u5355\u4E3A\u53EA\u8BFB\u72B6\u6001"') && zhSource.includes('"orderReadonlyDescription": "\u5DF2\u5B8C\u6210\u8BA2\u5355\u4E0D\u53EF\u7F16\u8F91\uFF0C\u8BF7\u66F4\u6539\u72B6\u6001\u540E\u518D\u64CD\u4F5C\u3002\u4F46\u4ECD\u53EF\u8865\u5F55\u6216\u4FEE\u6B63\u51FA\u5E93\u65E5\u671F\u3002"') && zhSource.includes('"orderReadonlyRefresh": "\u5F53\u524D\u8BA2\u5355\u72B6\u6001\u4E0D\u53EF\u7F16\u8F91\uFF0C\u8BF7\u5237\u65B0\u786E\u8BA4\u72B6\u6001\u3002"'),
      "\u4E2D\u6587\u6587\u6848\u7F3A\u5C11\u8BA2\u5355\u53EA\u8BFB\u63D0\u793A"
    );
    assert(
      enSource.includes('"orderReadonlyTitle": "Order is read-only"') && enSource.includes('"orderReadonlyDescription": "Completed orders cannot be edited. Change the status before editing. The outbound date can still be corrected."') && enSource.includes('"orderReadonlyRefresh": "The current order status is not editable. Please refresh and confirm the status."'),
      "\u82F1\u6587\u6587\u6848\u7F3A\u5C11\u8BA2\u5355\u53EA\u8BFB\u63D0\u793A"
    );
  });
  if (readonlyCopyFailure) failures.push(readonlyCopyFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("detailAuxiliaryLoads.logic.test: ok");
}
await main();
