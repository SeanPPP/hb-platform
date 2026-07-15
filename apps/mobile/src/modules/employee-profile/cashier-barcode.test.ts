import assert from "node:assert/strict";
import {
  buildCashierBarcodePrintConfirmationRequest,
  isCashierBarcodeChangedError,
  classifyCashierBarcodePrintError,
  canRefreshCashierBarcode,
  executeCashierBarcodePrint,
  loadCashierBarcodePrintPending,
  prepareUncertainCashierBarcodeReprint,
  prepareNewCashierBarcodePrint,
  resolveUncertainCashierBarcodePrint,
  saveCashierBarcodePrintPending,
  type PendingCashierBarcodePrintConfirmation,
} from "./cashier-barcode";
import {
  activateCashierPrintSecureSession,
  resetCashierPrintSecureSessionForTests,
} from "../../shared/storage/cashier-print-secure-session";

assert.deepEqual(
  buildCashierBarcodePrintConfirmationRequest(" 2912345678906 ", "attempt-1"),
  { barcode: "2912345678906", printAttemptId: "attempt-1" },
  "打印确认必须携带实际打印的条码"
);

assert.throws(
  () => buildCashierBarcodePrintConfirmationRequest(" ", "attempt-1"),
  /barcode/i,
  "空条码不得发送打印确认"
);

assert.equal(
  isCashierBarcodeChangedError(new Error("CASHIER_BARCODE_CHANGED")),
  true,
  "后台错误码应识别为条码已刷新"
);

assert.equal(classifyCashierBarcodePrintError(new Error("No label printer has been selected yet.")), "noPrinter");
assert.equal(classifyCashierBarcodePrintError(new Error("Bluetooth is disabled.")), "bluetoothDisabled");
assert.equal(classifyCashierBarcodePrintError(new Error("Bluetooth permission was not granted.")), "permissionDenied");
assert.equal(classifyCashierBarcodePrintError(new Error("Unable to connect to the saved label printer.")), "connectionFailed");
assert.equal(classifyCashierBarcodePrintError(new Error("unknown")), "unknown");
assert.equal(
  isCashierBarcodeChangedError(new Error("当前条码已刷新，请重新打印")),
  true,
  "业务错误消息应识别为条码已刷新"
);
assert.equal(
  isCashierBarcodeChangedError(new Error("Bluetooth permission was not granted.")),
  false,
  "打印机错误不能误判为条码刷新"
);

async function testIdempotentConfirmationRetry() {
  let pending: {
    attemptId: string;
    barcode: string;
    phase: "printing" | "printed";
    createdAt: string;
  } | null = null;
  let printCount = 0;
  let confirmationCount = 0;
  const phases: string[] = [];
  const dependencies = {
    createAttemptId: () => "attempt-stable",
    printLabel: async () => {
      assert.equal(pending?.phase, "printing", "调用打印机前必须已持久化 printing");
      printCount += 1;
    },
    confirmPrint: async () => {
      assert.equal(pending?.phase, "printed", "调用确认接口前必须已持久化 printed");
      confirmationCount += 1;
      if (confirmationCount === 1) {
        throw new Error("network failed");
      }
      return { confirmed: true };
    },
    now: () => "2026-07-15T00:00:00.000Z",
    onPendingChange: async (value: typeof pending) => {
      pending = value;
      phases.push(value?.phase ?? "cleared");
    },
  };

  await assert.rejects(
    () => executeCashierBarcodePrint({ pending, barcode: "2912345678906", ...dependencies }),
    /network failed/,
    "确认网络失败应保留待确认状态"
  );
  assert.deepEqual(pending, {
    attemptId: "attempt-stable",
    barcode: "2912345678906",
    phase: "printed",
    createdAt: "2026-07-15T00:00:00.000Z",
  });
  assert.deepEqual(phases.slice(0, 2), ["printing", "printed"], "必须在出纸前后持久化阶段");

  await executeCashierBarcodePrint({ pending, barcode: "2912345678906", ...dependencies });
  assert.equal(printCount, 1, "重试确认时不得再次实体打印");
  assert.equal(confirmationCount, 2, "第二次点击应重试同一 attempt 确认");
  assert.equal(pending, null, "确认成功后应清除待确认状态");
}

async function testChangedBarcodeClearsPendingConfirmation() {
  const pending = {
    attemptId: "attempt-old",
    barcode: "2911111111116",
    phase: "printed" as const,
    createdAt: "2026-07-15T00:00:00.000Z",
  };
  const pendingChanges: Array<PendingCashierBarcodePrintConfirmation | null> = [];
  let printedBarcode = "";
  let confirmedAttempt = "";

  await assert.rejects(() => executeCashierBarcodePrint({
      pending,
      barcode: "2922222222221",
      createAttemptId: () => "attempt-new",
      printLabel: async (barcode) => {
        printedBarcode = barcode;
      },
      confirmPrint: async (confirmation) => {
        confirmedAttempt = confirmation.attemptId;
        return { confirmed: true };
      },
      onPendingChange: async (value) => { pendingChanges.push(value); },
    }), /changed/i);

  assert.equal(printedBarcode, "", "条码变化后不得自动重复打印");
  assert.equal(confirmedAttempt, "", "条码变化后不得确认旧 attempt");
  assert.deepEqual(pendingChanges[0], null, "条码变化时应先清除旧待确认状态");
}

async function testPersistentRecoveryAndUncertainChoice() {
  resetCashierPrintSecureSessionForTests();
  let secureValue: string | null = null;
  const storage = {
    getCashierBarcodePrintPending: async () => secureValue,
    setCashierBarcodePrintPending: async (value: string) => { secureValue = value; },
    removeCashierBarcodePrintPending: async () => { secureValue = null; },
  };
  const printed = {
    attemptId: "attempt-restored",
    barcode: "2912345678906",
    phase: "printed" as const,
    createdAt: "2026-07-15T00:00:00.000Z",
  };
  const userSession = activateCashierPrintSecureSession("user-guid");
  await saveCashierBarcodePrintPending(storage, "user-guid", printed, userSession);
  assert.deepEqual(await loadCashierBarcodePrintPending(storage, "user-guid", userSession), printed);

  let printerCalls = 0;
  await executeCashierBarcodePrint({
    pending: await loadCashierBarcodePrintPending(storage, "user-guid", userSession),
    barcode: printed.barcode,
    createAttemptId: () => "must-not-be-used",
    printLabel: async () => { printerCalls += 1; },
    confirmPrint: async () => ({ confirmed: true }),
    onPendingChange: async (value) => {
      await saveCashierBarcodePrintPending(storage, "user-guid", value, userSession);
    },
  });
  assert.equal(printerCalls, 0, "恢复 printed 状态只可重试确认，不能再次出纸");

  const uncertain = { ...printed, phase: "printing" as const };
  let uncertainPrinterCalls = 0;
  let uncertainConfirmationCalls = 0;
  await assert.rejects(() => executeCashierBarcodePrint({
    pending: uncertain,
    barcode: uncertain.barcode,
    createAttemptId: () => "must-not-be-used",
    printLabel: async () => { uncertainPrinterCalls += 1; },
    confirmPrint: async () => {
      uncertainConfirmationCalls += 1;
      return { confirmed: true };
    },
    onPendingChange: async () => undefined,
  }), /uncertain/i, "printing 恢复态必须等待用户选择");
  assert.equal(uncertainPrinterCalls, 0, "不确定态不得自动重复打印");
  assert.equal(uncertainConfirmationCalls, 0, "不确定态不得自动确认计数");
  assert.equal((await resolveUncertainCashierBarcodePrint(uncertain, "printed"))?.phase, "printed");
  assert.equal(await resolveUncertainCashierBarcodePrint(uncertain, "notPrinted"), null);
  assert.equal(canRefreshCashierBarcode(uncertain, true), false, "不确定状态存在时必须禁用刷新");
  assert.equal(canRefreshCashierBarcode(null, true), true);

  const firstSession = activateCashierPrintSecureSession("first-user");
  await saveCashierBarcodePrintPending(storage, "first-user", printed, firstSession);
  const secondSession = activateCashierPrintSecureSession("second-user");
  assert.equal(await loadCashierBarcodePrintPending(storage, "second-user", secondSession), null,
    "安全记录的账号不匹配时不得暴露或恢复");
  assert.equal(secureValue, null, "账号不匹配的安全记录应立即删除");
}

async function testReprintMustRefetchBarcode() {
  const pending = {
    attemptId: "attempt-old",
    barcode: "2912345678906",
    phase: "printing" as const,
    createdAt: "2026-07-15T00:00:00.000Z",
  };
  const changes: Array<PendingCashierBarcodePrintConfirmation | null> = [];
  await assert.rejects(() => prepareUncertainCashierBarcodeReprint({
    pending,
    refetchBarcode: async () => "2999999999994",
    onPendingChange: async (value) => { changes.push(value); },
  }), /changed/i);
  assert.deepEqual(changes, [null], "服务器条码变化时必须清理旧 attempt");

  const sameBarcodeChanges: Array<PendingCashierBarcodePrintConfirmation | null> = [];
  const sameBarcode = await prepareUncertainCashierBarcodeReprint({
    pending,
    refetchBarcode: async () => pending.barcode,
    onPendingChange: async (value) => { sameBarcodeChanges.push(value); },
  });
  assert.equal(sameBarcode, pending.barcode);
  assert.equal(sameBarcodeChanges.at(-1), null, "确认未打印且条码未变化后才可清除旧 attempt");
}

async function testNewPrintMustRefetchBarcode() {
  let refetchCount = 0;
  let printerCalls = 0;
  await assert.rejects(async () => {
    const barcode = await prepareNewCashierBarcodePrint({
      cachedBarcode: "2912345678906",
      refetchBarcode: async () => {
        refetchCount += 1;
        return "2999999999994";
      },
    });
    await executeCashierBarcodePrint({
      pending: null,
      barcode,
      createAttemptId: () => "must-not-run",
      printLabel: async () => { printerCalls += 1; },
      confirmPrint: async () => ({ confirmed: true }),
      onPendingChange: async () => undefined,
    });
  }, /changed/i);
  assert.equal(refetchCount, 1, "普通首次打印也必须先refetch服务端条码");
  assert.equal(printerCalls, 0, "服务端条码变化时不得调用打印机");

  await assert.rejects(async () => {
    const barcode = await prepareNewCashierBarcodePrint({
      cachedBarcode: "2912345678906",
      refetchBarcode: async () => null,
    });
    await executeCashierBarcodePrint({
      pending: null,
      barcode,
      createAttemptId: () => "must-not-run",
      printLabel: async () => { printerCalls += 1; },
      confirmPrint: async () => ({ confirmed: true }),
      onPendingChange: async () => undefined,
    });
  }, /revalidate/i);
  assert.equal(printerCalls, 0, "条码不存在或refetch失败时不得调用打印机");

  assert.equal(await prepareNewCashierBarcodePrint({
    cachedBarcode: "2912345678906",
    refetchBarcode: async () => "2912345678906",
  }), "2912345678906");
}

async function main() {
  await testIdempotentConfirmationRetry();
  await testChangedBarcodeClearsPendingConfirmation();
  await testPersistentRecoveryAndUncertainChoice();
  await testReprintMustRefetchBarcode();
  await testNewPrintMustRefetchBarcode();
  console.log("cashier-barcode.test.ts: ok");
}

void main();
