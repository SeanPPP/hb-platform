import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import ts from "typescript";
import type { WarehousePriceSyncState } from "./warehouse-price-sync";
import {
  buildWarehousePriceSyncRequest,
  calculateDiscountedRetailPrice,
  createWarehousePriceSyncState,
  extractWarehousePriceSyncConflict,
  formatWarehouseDiscountRate,
  getWarehousePriceSyncApplicability,
  isProductQueryInteractionBlocked,
  isWarehousePriceConflictSnapshotComplete,
  isWarehousePriceInteractionLocked,
  isWarehousePriceSyncSupplier,
  normalizeWarehouseDiscountRate,
  normalizeWarehouseMoney,
  normalizeWarehousePriceSyncResponse,
  reduceWarehousePriceSyncState,
  resolveWarehousePriceConfirmationFeedback,
  shouldAutoPrintWarehousePrice,
} from "./warehouse-price-sync";

// 执行页面真实详情后处理及扫码打印回调，只替换网络与打印机边界。
const pageSource = readFileSync(resolve(__dirname, "../../../app/(shell)/product-query.tsx"), "utf8");
const pageAst = ts.createSourceFile("product-query.tsx", pageSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const productQuery = pageAst.statements.find((node): node is ts.FunctionDeclaration =>
  ts.isFunctionDeclaration(node) && node.name?.text === "ProductQueryContent");
function callbackStatement(name: string): ts.VariableStatement {
  const statement = productQuery?.body?.statements.find((node) =>
    ts.isVariableStatement(node) && node.declarationList.declarations.some((declaration) =>
      ts.isIdentifier(declaration.name) && declaration.name.text === name));
  assert.ok(statement && ts.isVariableStatement(statement), `页面必须保留可执行的 ${name} 回调`);
  return statement;
}
const processLoadedDetailStatement = callbackStatement("processLoadedDetail");
const smartAutoPrintStatement = callbackStatement("smartAutoPrint");

const warehouseRetailPriceDeclaration = productQuery?.body?.statements
  .filter(ts.isVariableStatement)
  .flatMap((statement) => [...statement.declarationList.declarations])
  .find((declaration) => ts.isIdentifier(declaration.name) && declaration.name.text === "warehouseRetailPrice");
const warehouseRetailPriceExpression = warehouseRetailPriceDeclaration?.initializer;
assert.ok(warehouseRetailPriceExpression, "页面必须计算当前门店的仓库零售价差异提示");
const warehouseRetailPriceSource = warehouseRetailPriceExpression.getText(pageAst);
function evaluateWarehouseRetailPrice(input: {
  offlineMode?: boolean;
  storePrice: { uuid: string; retailPrice: number | null } | null;
  warehousePriceSnapshot: typeof camelSnapshot | null;
}): string | null {
  const deps = {
    offlineMode: input.offlineMode === true,
    storePrice: input.storePrice,
    warehousePriceSnapshot: input.warehousePriceSnapshot,
    normalizeWarehouseMoney,
    formatCurrency: (value: number) => `$${value.toFixed(2)}`,
  };
  return new Function("deps", `const { ${Object.keys(deps).join(", ")} } = deps; return (${warehouseRetailPriceSource});`)(deps) as string | null;
}

function compileCallback<T>(statement: ts.VariableStatement, name: string, deps: Record<string, unknown>): T {
  const source = `const { ${Object.keys(deps).join(", ")} } = deps;\n${statement.getText(pageAst)}\nreturn ${name};`;
  return new Function("deps", ts.transpile(source, { target: ts.ScriptTarget.ES2022 }))(deps) as T;
}

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const camelSnapshot = normalizeWarehousePriceSyncResponse({
  status: "confirmation_required",
  purchaseUpdated: true,
  retailUpdated: false,
  retailConfirmationRequired: true,
  storePrice: {
    uuid: "price-1",
    storeCode: "S01",
    purchasePrice: "3.25",
    retailPrice: 5,
    discountRate: 0.2,
    isAutoPricing: true,
    isSpecialProduct: false,
    isActive: true,
  },
  warehousePurchasePrice: "3.25",
  warehouseRetailPrice: 6,
  previousStorePurchasePrice: 3,
  previousStoreRetailPrice: 5,
  discountRate: 0.2,
  previousDiscountedRetailPrice: 4,
  newDiscountedRetailPrice: 4.8,
});

assertEqual(camelSnapshot.status, "confirmation_required", "camel response normalizes status");
assertEqual(camelSnapshot.storePrice?.purchasePrice, 3.25, "camel response normalizes latest purchase");
assertEqual(camelSnapshot.storePrice?.isAutoPricing, true, "camel response preserves auto pricing flag");
assertEqual(
  evaluateWarehouseRetailPrice({
    storePrice: { uuid: "price-1", retailPrice: 5 },
    warehousePriceSnapshot: camelSnapshot,
  }),
  "$6.00",
  "仓库与当前门店零售价不同则在页面展示仓库价"
);
for (const [label, input] of [
  ["同价", { storePrice: { uuid: "price-1", retailPrice: 6 }, warehousePriceSnapshot: camelSnapshot }],
  ["不同门店价 UUID", { storePrice: { uuid: "price-other", retailPrice: 5 }, warehousePriceSnapshot: camelSnapshot }],
  ["离线", { offlineMode: true, storePrice: { uuid: "price-1", retailPrice: 5 }, warehousePriceSnapshot: camelSnapshot }],
  ["缺失门店金额", { storePrice: { uuid: "price-1", retailPrice: null }, warehousePriceSnapshot: camelSnapshot }],
  ["缺失仓库金额", { storePrice: { uuid: "price-1", retailPrice: 5 }, warehousePriceSnapshot: { ...camelSnapshot, warehouseRetailPrice: null } }],
] as const) {
  assertEqual(evaluateWarehouseRetailPrice(input), null, `${label}不显示过期或无意义的仓库价差异`);
}

const pascalSnapshot = normalizeWarehousePriceSyncResponse({
  Success: true,
  Data: {
    Status: "synced",
    PurchaseUpdated: false,
    RetailUpdated: true,
    RetailConfirmationRequired: false,
    StorePrice: {
      Uuid: "price-2",
      StoreCode: "S02",
      PurchasePrice: 4,
      RetailPrice: 8,
      DiscountRate: 15,
      IsAutoPricing: false,
      IsSpecialProduct: true,
      IsActive: true,
    },
    WarehousePurchasePrice: 4,
    WarehouseRetailPrice: 8,
    PreviousStorePurchasePrice: 4,
    PreviousStoreRetailPrice: 7,
    DiscountRate: 15,
    PreviousDiscountedRetailPrice: 5.95,
    NewDiscountedRetailPrice: 6.8,
    HqSync: {
      OperationId: "operation-warehouse-1",
      Status: "pending",
      ProductCode: "P-WAREHOUSE",
      StoreCode: "S02",
      AttemptCount: 0,
      Retryable: true,
      Message: "pending",
    },
  },
});

assertEqual(pascalSnapshot.storePrice?.discountRate, 0.15, "Pascal response normalizes percent discount");
assertEqual(pascalSnapshot.newDiscountedRetailPrice, 6.8, "Pascal response normalizes discounted price");
assertEqual(
  pascalSnapshot.hqSync?.operationId,
  "operation-warehouse-1",
  "warehouse mutation exposes its HQ sync operation"
);

const nullSnapshot = normalizeWarehousePriceSyncResponse(null);
assertDeepEqual(
  {
    status: nullSnapshot.status,
    purchaseUpdated: nullSnapshot.purchaseUpdated,
    retailUpdated: nullSnapshot.retailUpdated,
    retailConfirmationRequired: nullSnapshot.retailConfirmationRequired,
    storePrice: nullSnapshot.storePrice,
    warehousePurchasePrice: nullSnapshot.warehousePurchasePrice,
  },
  {
    status: "missing_source",
    purchaseUpdated: false,
    retailUpdated: false,
    retailConfirmationRequired: false,
    storePrice: null,
    warehousePurchasePrice: null,
  },
  "null response normalizes to a safe non-writing snapshot"
);

assertEqual(isWarehousePriceSyncSupplier(" 200 "), true, "supplier 200 is trim-aware");
assertEqual(isWarehousePriceSyncSupplier("0200"), false, "other supplier is not applicable");
assertEqual(isWarehousePriceSyncSupplier(null), false, "missing supplier is not applicable");

assertEqual(normalizeWarehouseMoney(1.005), 1.01, "money rounds to two decimals");
assertEqual(normalizeWarehouseMoney("bad"), null, "invalid money becomes null");
assertEqual(normalizeWarehouseDiscountRate(20), 0.2, "percentage discount normalizes to ratio");
assertEqual(normalizeWarehouseDiscountRate(0.2), 0.2, "ratio discount stays unchanged");
assertEqual(calculateDiscountedRetailPrice(6, 0.2), 4.8, "discounted price preserves discount ratio");
assertEqual(calculateDiscountedRetailPrice(7.99, 15), 6.79, "discounted price rounds to cents");

const nullDiscountSnapshot = normalizeWarehousePriceSyncResponse({
  status: "confirmation_required",
  retailConfirmationRequired: true,
  storePrice: {
    uuid: "price-null-discount",
    purchasePrice: 4,
    retailPrice: 8,
    discountRate: null,
    isAutoPricing: false,
    isSpecialProduct: false,
    isActive: true,
  },
  warehousePurchasePrice: 4,
  warehouseRetailPrice: 10,
  previousStorePurchasePrice: 4,
  previousStoreRetailPrice: 8,
  discountRate: null,
  previousDiscountedRetailPrice: 8,
  newDiscountedRetailPrice: 10,
});
assertEqual(nullDiscountSnapshot.discountRate, null, "null discount remains null for concurrency snapshot");
assertEqual(calculateDiscountedRetailPrice(8, null), 8, "null discount calculates as zero discount");
assertEqual(formatWarehouseDiscountRate(null), "0%", "null discount displays as zero percent");
assertEqual(
  buildWarehousePriceSyncRequest(
    { ...nullDiscountSnapshot, discountRate: 0.2 },
    true
  ).expectedDiscountRate,
  null,
  "confirm request preserves latest store-price null discount for concurrency validation"
);
assertEqual(
  isWarehousePriceConflictSnapshotComplete(nullDiscountSnapshot),
  true,
  "conflict snapshot completeness allows a legitimate null discount"
);
assertEqual(
  isWarehousePriceConflictSnapshotComplete({
    ...nullDiscountSnapshot,
    previousStoreRetailPrice: null,
    storePrice: { ...nullDiscountSnapshot.storePrice!, retailPrice: null },
  }),
  true,
  "conflict snapshot completeness allows a legitimate null current retail price"
);

assertEqual(
  getWarehousePriceSyncApplicability("200", null),
  "missing_store_price",
  "supplier 200 without an existing store price cannot sync or auto print"
);
assertEqual(
  getWarehousePriceSyncApplicability(" 200 ", "price-1"),
  "sync",
  "supplier 200 with a store price enters warehouse sync"
);

assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: false,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: false,
    storeSelectionInFlight: false,
  }),
  false,
  "idle product query accepts a new scan"
);
assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: false,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: true,
    storeSelectionInFlight: false,
  }),
  true,
  "in-flight lookup serializes camera, HID, and store selection"
);
assertEqual(
  isProductQueryInteractionBlocked({
    loading: false,
    lookupVisible: false,
    lookupSelectionOpen: true,
    autoPricingVisible: false,
    autoPricingSaving: false,
    warehouseLocked: false,
    requestInFlight: false,
    storeSelectionInFlight: false,
  }),
  true,
  "synchronous selection ref closes the state-update window before lookup sheet renders"
);

const confirmRequest = buildWarehousePriceSyncRequest(camelSnapshot, true);
assertDeepEqual(
  confirmRequest,
  {
    confirmRetailPrice: true,
    expectedWarehousePurchasePrice: 3.25,
    expectedWarehouseRetailPrice: 6,
    expectedStorePurchasePrice: 3.25,
    expectedStoreRetailPrice: 5,
    expectedDiscountRate: 0.2,
  },
  "confirm request uses the latest post-purchase-sync store snapshot"
);

let flowState = createWarehousePriceSyncState();
flowState = reduceWarehousePriceSyncState(flowState, { type: "preview_started" });
assertEqual(flowState.phase, "previewing", "preview transition enters busy state");
assertEqual(isWarehousePriceInteractionLocked(flowState), true, "preview disables scanner and editing");

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "preview_succeeded",
  snapshot: camelSnapshot,
});
assertEqual(flowState.phase, "idle", "retail difference does not open a confirmation state");
assertEqual(isWarehousePriceInteractionLocked(flowState), false, "completed preview releases scanner state");

// 旧确认事件暂保留 API 契约；页面不再进入此流程。
flowState = { phase: "confirmation", snapshot: camelSnapshot, errorMessage: null };
flowState = reduceWarehousePriceSyncState(flowState, { type: "confirm_started" });
assertEqual(flowState.phase, "confirming", "confirm transition enters submit state");

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "confirm_failed",
  message: "network failed",
});
assertEqual(flowState.phase, "confirmation", "confirm failure keeps modal open");
assertEqual(flowState.errorMessage, "network failed", "confirm failure remains retryable with error");

const conflictSnapshot = extractWarehousePriceSyncConflict({
  response: {
    status: 409,
    data: {
      success: false,
      errorCode: "PRICE_VERSION_CONFLICT",
      data: {
        status: "confirmation_required",
        retailConfirmationRequired: true,
        storePrice: {
          uuid: "price-1",
          purchasePrice: 3.5,
          retailPrice: 5.5,
          discountRate: 0.2,
          isAutoPricing: true,
          isSpecialProduct: false,
          isActive: true,
        },
        warehousePurchasePrice: 3.5,
        warehouseRetailPrice: 6.5,
        previousStorePurchasePrice: 3.5,
        previousStoreRetailPrice: 5.5,
        discountRate: 0.2,
        previousDiscountedRetailPrice: 4.4,
        newDiscountedRetailPrice: 5.2,
      },
    },
  },
});

assertEqual(conflictSnapshot?.storePrice?.purchasePrice, 3.5, "409 exposes latest store snapshot");
assertEqual(conflictSnapshot?.warehouseRetailPrice, 6.5, "409 exposes latest warehouse snapshot");
assertEqual(
  extractWarehousePriceSyncConflict({ response: { status: 500, data: {} } }),
  null,
  "non-conflict errors do not masquerade as a version conflict"
);

flowState = reduceWarehousePriceSyncState(flowState, {
  type: "conflict_received",
  snapshot: conflictSnapshot!,
  message: "price changed",
});
assertEqual(flowState.phase, "confirmation", "conflict refresh keeps confirmation open");
assertEqual(flowState.snapshot?.warehouseRetailPrice, 6.5, "conflict transition replaces stale snapshot");

flowState = reduceWarehousePriceSyncState(flowState, { type: "print_started" });
assertEqual(flowState.phase, "printing", "explicit keep-current-price print locks the flow");
assertEqual(isWarehousePriceInteractionLocked(flowState), true, "printing keeps scanner and editing locked");
flowState = reduceWarehousePriceSyncState(flowState, { type: "cancelled" });
assertEqual(flowState.phase, "idle", "print completion cleanup returns the flow to idle");
assertEqual(flowState.snapshot, null, "cancel cleanup clears the transient snapshot");

assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "preview_succeeded",
    snapshot: { ...camelSnapshot, status: "synced", retailConfirmationRequired: false },
    alreadyPrinted: false,
  }),
  true,
  "same-price warehouse sync still auto prints"
);
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "preview_succeeded",
    snapshot: camelSnapshot,
    alreadyPrinted: false,
  }),
  true,
  "retail difference auto prints the verified current store price"
);
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "confirmation_succeeded",
    snapshot: { ...pascalSnapshot, retailUpdated: true },
    alreadyPrinted: false,
  }),
  true,
  "scan prints after successful retail confirmation"
);
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "confirmation_succeeded",
    snapshot: { ...pascalSnapshot, retailUpdated: false },
    alreadyPrinted: false,
  }),
  false,
  "confirmation response without a retail update does not print"
);
const keepCurrentPriceSnapshot = {
  ...camelSnapshot,
  status: "confirmation_required" as const,
  retailConfirmationRequired: true,
  retailUpdated: false,
  storePrice: {
    ...camelSnapshot.storePrice!,
    uuid: "price-current",
    retailPrice: 5,
  },
};
assertEqual(
  shouldAutoPrintWarehousePrice({
    lookupOrigin: "scan",
    stage: "retail_update_skipped",
    snapshot: keepCurrentPriceSnapshot,
    alreadyPrinted: false,
  }),
  true,
  "explicitly keeping the current store price allows the scanned label to print"
);
for (const [label, snapshot] of [
  ["skip print requires confirmation status", { ...keepCurrentPriceSnapshot, status: "synced" as const }],
  ["skip print requires a confirmation marker", { ...keepCurrentPriceSnapshot, retailConfirmationRequired: false }],
  ["skip print requires a store-price UUID", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, uuid: "" } }],
  ["skip print rejects a missing current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: null } }],
  ["skip print rejects a negative current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: -1 } }],
  ["skip print rejects a non-finite current retail price", { ...keepCurrentPriceSnapshot, storePrice: { ...keepCurrentPriceSnapshot.storePrice!, retailPrice: Number.NaN } }],
] as const) {
  assertEqual(
    shouldAutoPrintWarehousePrice({
      lookupOrigin: "scan",
      stage: "retail_update_skipped",
      snapshot,
      alreadyPrinted: false,
    }),
    false,
    label
  );
}
for (const status of ["missing_source", "not_applicable"] as const) {
  assertEqual(
    shouldAutoPrintWarehousePrice({
      lookupOrigin: "scan",
      stage: "preview_succeeded",
      snapshot: { ...pascalSnapshot, status, retailUpdated: false },
      alreadyPrinted: false,
    }),
    false,
    `${status} does not print an unverified warehouse label`
  );
}
for (const [label, storePrice] of [
  ["missing UUID", { ...camelSnapshot.storePrice!, uuid: "" }],
  ["missing price", { ...camelSnapshot.storePrice!, retailPrice: null }],
  ["negative price", { ...camelSnapshot.storePrice!, retailPrice: -1 }],
  ["non-finite price", { ...camelSnapshot.storePrice!, retailPrice: Number.NaN }],
] as const) {
  assertEqual(
    shouldAutoPrintWarehousePrice({
      lookupOrigin: "scan",
      stage: "preview_succeeded",
      snapshot: { ...camelSnapshot, storePrice },
      alreadyPrinted: false,
    }),
    false,
    `retail difference preview rejects ${label}`
  );
}
for (const [label, input] of [
  [
    "manual lookup never auto prints",
    { lookupOrigin: "manual", stage: "preview_succeeded", snapshot: pascalSnapshot, alreadyPrinted: false },
  ],
  [
    "cancel never prints stale label",
    { lookupOrigin: "scan", stage: "cancelled", snapshot: camelSnapshot, alreadyPrinted: false },
  ],
  [
    "failure never prints stale label",
    { lookupOrigin: "scan", stage: "failed", snapshot: camelSnapshot, alreadyPrinted: false },
  ],
  [
    "already printed scan cannot print twice",
    { lookupOrigin: "scan", stage: "confirmation_succeeded", snapshot: pascalSnapshot, alreadyPrinted: true },
  ],
  [
    "dismissal/cancel does not print the current label",
    { lookupOrigin: "scan", stage: "cancelled", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: false },
  ],
  [
    "manual keep-current-price action never auto prints",
    { lookupOrigin: "manual", stage: "retail_update_skipped", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: false },
  ],
  [
    "repeat keep-current-price action cannot print twice",
    { lookupOrigin: "scan", stage: "retail_update_skipped", snapshot: keepCurrentPriceSnapshot, alreadyPrinted: true },
  ],
] as const) {
  assertEqual(shouldAutoPrintWarehousePrice(input), false, label);
}

assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: true,
    printAttempted: true,
    labelPrinted: false,
  }),
  "retail_updated_print_failed",
  "price success plus print failure uses combined feedback"
);
assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: true,
    printAttempted: false,
    labelPrinted: false,
  }),
  "retail_updated",
  "price success without auto print reports only the price update"
);
assertEqual(
  resolveWarehousePriceConfirmationFeedback({
    retailUpdated: false,
    printAttempted: false,
    labelPrinted: false,
  }),
  "no_update",
  "unchanged confirmation never claims that retail was updated"
);

const currentDetail = {
  productCode: "PRODUCT-1",
  barcode: "MAIN-1",
  itemNumber: "ITEM-1",
  localSupplierCode: "200",
  storePrice: { ...camelSnapshot.storePrice!, storeProductCode: "STORE-1" },
  setCodes: [{ setCodeId: "set-id", setBarcode: "SET-1", setRetailPrice: 10 }],
  multiCodes: [{ multiCodeId: "multi-id", barcode: "MULTI-1", retailPrice: 5 }],
};
type Detail = typeof currentDetail;
type FlowResult = { foregroundPending?: boolean; labelPrinted: boolean; autoPricingStatus: string };
type PrintCall = { detail: Detail; options?: { barcode: string; retailPrice: number; action: string; printType: string | null } };

function createPageFlow(options: {
  snapshot?: typeof camelSnapshot;
  scanKeyword?: string;
  autoPrintEnabled?: boolean;
  lookupOrigin?: "scan" | "manual";
  localSupplierCode?: string;
  scopeCurrent?: boolean;
  offline?: boolean;
  syncError?: Error;
  print?: () => Promise<boolean>;
  refreshedStorePrice?: { uuid?: string; retailPrice?: number | null };
} = {}) {
  let state = createWarehousePriceSyncState();
  const requestRef = { current: false };
  const syncCalls: { uuid: string; request: Record<string, unknown> }[] = [];
  const prints: PrintCall[] = [];
  const messages: string[] = [];
  const feedback: string[] = [];
  const contexts: unknown[] = [];
  const phases: WarehousePriceSyncState["phase"][] = [];
  const detail: Detail = structuredClone({
    ...currentDetail,
    localSupplierCode: options.localSupplierCode ?? "200",
  });
  const snapshot = options.snapshot ?? camelSnapshot;
  let failedMutation = false;
  const printDeps = {
    useCallback: (callback: unknown) => callback,
    getErrorMessage: (_error: unknown, fallback: string) => fallback,
    printQuantity: 1,
    quantitySingleUse: false,
    sendProductLabel: async (targetDetail: Detail, printOptions?: PrintCall["options"]) => {
      prints.push({ detail: targetDetail, options: printOptions });
      return options.print ? options.print() : true;
    },
    getMultiCodeItemId: (code: Detail["multiCodes"][number]) => code.multiCodeId,
    smallLabel: false,
    t: (key: string) => key,
  };
  const smartAutoPrint = compileCallback<(keyword: string, targetDetail: Detail) => Promise<boolean>>(
    smartAutoPrintStatement, "smartAutoPrint", printDeps,
  );
  const deps = {
    useCallback: (callback: unknown) => callback,
    ensureCurrentDetailStoreScope: () => options.scopeCurrent !== false,
    offlineModeRef: { current: options.offline === true },
    playQueryFeedback: (value: string) => { feedback.push(value); },
    smartAutoPrint,
    DEFAULT_LOOKUP_FLOW_RESULT: { keepCameraOpen: false, labelPrinted: false, autoPricingStatus: "no_action" },
    getWarehousePriceSyncApplicability,
    maybeHandleAutoPricing: async () => ({ keepCameraOpen: false, labelPrinted: false, autoPricingStatus: "no_action" }),
    warehousePriceRequestInFlightRef: requestRef,
    beginHqSyncMutation: () => "mutation-1",
    selectedStoreCode: "S01",
    setWarehousePriceSyncState: (reduce: (value: WarehousePriceSyncState) => WarehousePriceSyncState) => {
      state = reduce(state);
      phases.push(state.phase);
    },
    reduceWarehousePriceSyncState,
    syncWarehousePrice: async (uuid: string, request: Record<string, unknown>) => {
      syncCalls.push({ uuid, request });
      if (options.syncError) throw options.syncError;
      return snapshot;
    },
    normalizeDiscountRateValue: normalizeWarehouseDiscountRate,
    presentHqSyncOperation: () => {},
    replaceStorePriceDetail: (value: Detail, storePrice: Detail["storePrice"]) => ({ ...value, storePrice }),
    loadDetail: async () => ({
      ...detail,
      storePrice: {
        ...detail.storePrice,
        purchasePrice: snapshot.storePrice?.purchasePrice ?? 3.25,
        ...options.refreshedStorePrice,
      },
    }),
    setDetail: () => {},
    setInitialDetail: () => {},
    cloneDetail: (value: Detail) => structuredClone(value),
    setSnackbarMessage: (value: string) => { messages.push(value); },
    t: (key: string) => key,
    setWarehousePriceSyncContext: (value: unknown) => { contexts.push(value); },
    shouldAutoPrintWarehousePrice,
    getErrorMessage: (_error: unknown, fallback: string) => fallback,
    hqSyncMutationCoordinatorRef: { current: { fail: () => { failedMutation = true; } } },
    isAxiosError: () => false,
  };
  const processLoadedDetail = compileCallback<(value: Detail, input: Record<string, unknown>) => Promise<FlowResult>>(
    processLoadedDetailStatement, "processLoadedDetail", deps,
  );
  const run = () => processLoadedDetail(detail, {
    lookupOrigin: options.lookupOrigin ?? "scan",
    storeCodeOverride: "S01",
    scanSource: "camera",
    scanKeyword: options.scanKeyword ?? "MAIN-1",
    autoPrintEnabled: options.autoPrintEnabled !== false,
  });
  return {
    run, requestRef, syncCalls, prints, messages, feedback, contexts, phases, detail,
    get state() { return state; },
    get failedMutation() { return failedMutation; },
    assertReleased() {
      assert.equal(requestRef.current, false, "请求锁必须释放");
      assert.equal(state.phase, "idle", "结束后仓库价流程回到 idle");
      assert.ok(phases.every((phase) => phase !== "confirmation"), "页面流程不能进入零售价确认态");
      assert.ok(contexts.every((value) => value === null), "零售价差异不能保存弹窗上下文");
      assert.equal(detail.storePrice.retailPrice, 5, "原始门店售价保持不变");
      assert.equal(detail.storePrice.discountRate, 0.2, "原始折扣保持不变");
    },
  };
}

async function runInlineWarehousePriceRegression() {
  for (const [scanKeyword, expectedOptions] of [
    ["MAIN-1", undefined],
    ["PRODUCT-1", undefined],
    ["SET-1", { barcode: "SET-1", retailPrice: 10, action: "set:set-id", printType: null }],
    ["MULTI-1", { barcode: "MULTI-1", retailPrice: 5, action: "multi:multi-id", printType: null }],
  ] as const) {
    const flow = createPageFlow({ scanKeyword });
    const result = await flow.run();
    assert.equal(result.foregroundPending, undefined, "零售价差异不能挂起前台等待弹窗");
    assert.equal(result.labelPrinted, true, "扫码应自动打印当前门店价");
    assert.equal(flow.syncCalls.length, 1, "每次查询只做一次仓库价对账");
    assert.equal(flow.syncCalls[0].uuid, "price-1");
    assert.equal(flow.syncCalls[0].request.confirmRetailPrice, false, "不得确认写入仓库零售价");
    assert.equal(flow.prints.length, 1);
    assert.deepEqual(flow.prints[0].options, expectedOptions);
    assert.equal(flow.prints[0].detail.storePrice.retailPrice, 5, "标签使用门店原售价 5 而非仓库价 6");
    assert.equal(flow.prints[0].detail.storePrice.discountRate, 0.2, "标签保留门店折扣");
    flow.assertReleased();
  }

  const unknown = createPageFlow({ scanKeyword: "MULTI-ON-PAGE-2" });
  const unknownResult = await unknown.run();
  assert.equal(unknownResult.labelPrinted, false);
  assert.equal(unknown.prints.length, 0, "未加载的多码不能回退打印主价");
  assert.deepEqual(unknown.messages, ["warehousePriceSync.purchaseUpdated", "messages.codesLoadFailed"]);
  unknown.assertReleased();

  for (const [label, refreshedStorePrice] of [
    ["缺失售价", { retailPrice: null }],
    ["负售价", { retailPrice: -1 }],
    ["非有限售价", { retailPrice: Number.NaN }],
    ["不同价记录", { uuid: "price-other", retailPrice: 5 }],
  ] as const) {
    const invalidRefresh = createPageFlow({ refreshedStorePrice });
    const result = await invalidRefresh.run();
    assert.equal(result.labelPrinted, false, `${label}的二次详情不得打印`);
    assert.equal(invalidRefresh.prints.length, 0, `${label}不能沿用预览价或回退主价`);
    assert.deepEqual(invalidRefresh.messages, [
      "warehousePriceSync.purchaseUpdated",
      "warehousePriceSync.currentPriceUnavailable",
    ]);
    invalidRefresh.assertReleased();
  }

  for (const retailPrice of [5.25, 0]) {
    const concurrentPrice = createPageFlow({ refreshedStorePrice: { retailPrice } });
    assert.equal((await concurrentPrice.run()).labelPrinted, true, "同一价记录的合法门店售价仍可打印");
    assert.equal(concurrentPrice.prints[0].detail.storePrice.retailPrice, retailPrice, "标签必须采用刷新后的门店价");
    concurrentPrice.assertReleased();
  }

  for (const flow of [
    createPageFlow({ autoPrintEnabled: false }),
    createPageFlow({ lookupOrigin: "manual" }),
    createPageFlow({ scopeCurrent: false }),
  ]) {
    const result = await flow.run();
    assert.equal(result.labelPrinted, false);
    assert.equal(flow.prints.length, 0, "关闭自动打印、手动查询或门店范围失效不打印");
    flow.assertReleased();
  }

  let finishPrint!: (value: boolean) => void;
  const pendingPrint = new Promise<boolean>((resolve) => { finishPrint = resolve; });
  const duplicate = createPageFlow({ print: () => pendingPrint });
  const first = duplicate.run();
  await Promise.resolve();
  await Promise.resolve();
  assert.equal(duplicate.state.phase, "previewing", "打印未完成前继续锁定扫码");
  assert.equal(isWarehousePriceInteractionLocked(duplicate.state), true);
  await duplicate.run();
  assert.equal(duplicate.syncCalls.length, 1, "重复详情回调不能再发对账请求");
  assert.equal(duplicate.prints.length, 1, "重复详情回调不能重复打印");
  finishPrint(true);
  assert.equal((await first).labelPrinted, true);
  duplicate.assertReleased();

  for (const print of [async () => false, async () => { throw new Error("printer disconnected"); }]) {
    const failedPrint = createPageFlow({ print });
    const result = await failedPrint.run();
    assert.equal(result.labelPrinted, false, "打印返回失败或抛错不能报告成功");
    assert.equal(failedPrint.prints.length, 1);
    failedPrint.assertReleased();
  }

  const networkFailure = createPageFlow({ syncError: new Error("offline") });
  const failureResult = await networkFailure.run();
  assert.equal(failureResult.autoPricingStatus, "failed");
  assert.equal(networkFailure.prints.length, 0);
  assert.equal(networkFailure.failedMutation, true, "网络失败须标记 HQ mutation 失败");
  networkFailure.assertReleased();

  const samePrice = createPageFlow({ snapshot: {
    ...camelSnapshot, status: "synced", retailConfirmationRequired: false, warehouseRetailPrice: 5,
  } });
  assert.equal((await samePrice.run()).labelPrinted, true, "同价流程仍可打印");
  samePrice.assertReleased();

  const otherSupplier = createPageFlow({ localSupplierCode: "100" });
  assert.equal((await otherSupplier.run()).labelPrinted, true, "非 200 供应商保留原自动打印路径");
  assert.equal(otherSupplier.syncCalls.length, 0);
  otherSupplier.assertReleased();

  const offline = createPageFlow({ offline: true });
  assert.equal((await offline.run()).labelPrinted, true, "离线路径保留已有标签打印行为");
  assert.equal(offline.syncCalls.length, 0, "离线时不得调用仓库价 API");
  offline.assertReleased();
  console.log("仓库价页面内差异提示与扫码打印回归通过");
}

void runInlineWarehousePriceRegression().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
