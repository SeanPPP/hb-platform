// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/backgroundJobGuards.ts
function canApplyInvoiceJobResult(currentInvoiceGuid, submittedInvoiceGuid) {
  return Boolean(currentInvoiceGuid) && currentInvoiceGuid === submittedInvoiceGuid;
}
function canApplyCheckProductsJobResult({
  currentInvoiceGuid,
  submittedInvoiceGuid,
  status,
  hasResult
}) {
  return canApplyInvoiceJobResult(currentInvoiceGuid, submittedInvoiceGuid) && status === "Succeeded" && hasResult;
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/backgroundJobGuards.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
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
async function main() {
  const failures = [];
  const invoiceGuardFailure = await runTest("\u540E\u53F0 job \u53EA\u5E94\u5199\u56DE\u63D0\u4EA4\u65F6\u7684\u540C\u4E00\u5F20\u8FDB\u8D27\u5355", () => {
    assertEqual(canApplyInvoiceJobResult("invoice-1", "invoice-1"), true, "\u540C\u4E00\u5F20\u8FDB\u8D27\u5355\u5E94\u5141\u8BB8\u5199\u56DE");
    assertEqual(canApplyInvoiceJobResult("invoice-2", "invoice-1"), false, "\u5207\u6362\u5230\u5176\u4ED6\u8FDB\u8D27\u5355\u540E\u4E0D\u5E94\u5199\u56DE");
    assertEqual(canApplyInvoiceJobResult(void 0, "invoice-1"), false, "\u5F53\u524D\u6CA1\u6709\u8FDB\u8D27\u5355\u65F6\u4E0D\u5E94\u5199\u56DE");
  });
  if (invoiceGuardFailure) failures.push(invoiceGuardFailure);
  const checkGuardFailure = await runTest("\u5546\u54C1\u68C0\u6D4B\u53EA\u6709\u540C\u4E00\u5F20\u8FDB\u8D27\u5355\u4E14\u6210\u529F\u65F6\u624D\u5408\u5E76\u7ED3\u679C", () => {
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: "invoice-1",
        submittedInvoiceGuid: "invoice-1",
        status: "Succeeded",
        hasResult: true
      }),
      true,
      "\u6210\u529F\u68C0\u6D4B\u7ED3\u679C\u5E94\u5199\u56DE\u540C\u4E00\u5F20\u8FDB\u8D27\u5355"
    );
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: "invoice-1",
        submittedInvoiceGuid: "invoice-1",
        status: "Failed",
        hasResult: true
      }),
      false,
      "\u5931\u8D25\u68C0\u6D4B\u5373\u4F7F\u5E26 result \u4E5F\u4E0D\u5E94\u6C61\u67D3\u8868\u683C\u72B6\u6001"
    );
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: "invoice-2",
        submittedInvoiceGuid: "invoice-1",
        status: "Succeeded",
        hasResult: true
      }),
      false,
      "\u65E7\u8FDB\u8D27\u5355\u68C0\u6D4B\u5B8C\u6210\u65F6\u4E0D\u5E94\u5199\u56DE\u5F53\u524D\u8FDB\u8D27\u5355"
    );
    assertEqual(
      canApplyCheckProductsJobResult({
        currentInvoiceGuid: "invoice-1",
        submittedInvoiceGuid: "invoice-1",
        status: "Succeeded",
        hasResult: false
      }),
      false,
      "\u6210\u529F\u4F46\u6CA1\u6709 result \u65F6\u4E0D\u5E94\u5199\u56DE"
    );
  });
  if (checkGuardFailure) failures.push(checkGuardFailure);
  if (failures.length) {
    throw new Error(failures.join("\n"));
  }
  console.log("backgroundJobGuards.test: ok");
}
main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
