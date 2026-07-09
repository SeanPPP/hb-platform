// src/pages/Warehouse/StoreOrders/storeOrderInvoice.logic.test.ts
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
var invoiceFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Invoice.tsx");
var printCssFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css");
var zhFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var enFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
function readSource(file) {
  return readFileSync(file, "utf8").replace(/\r\n/g, "\n");
}
var invoiceSource = readSource(invoiceFile);
var printCssSource = readSource(printCssFile);
var zhSource = readSource(zhFile);
var enSource = readSource(enFile);
var zhMessages = JSON.parse(zhSource);
var enMessages = JSON.parse(enSource);
function readCssRule(source, selector) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? "";
}
async function main() {
  const failures = [];
  const emailEntryFailure = await runTest("\u53D1\u7968\u9875\u5E94\u63D0\u4F9B\u53D1\u9001\u90AE\u4EF6\u5165\u53E3\u5E76\u9501\u5B9A\u63A5\u53E3\u8C03\u7528\u5B57\u7B26\u4E32", () => {
    assert(invoiceSource.includes("sendStoreOrderInvoiceEmail"), "\u53D1\u7968\u9875\u5E94\u63A5\u5165\u53D1\u9001\u90AE\u4EF6 service");
    assert(invoiceSource.includes("getStoreOrderInvoiceEmailJob"), "\u53D1\u7968\u9875\u5E94\u63A5\u5165\u53D1\u7968\u90AE\u4EF6 job \u67E5\u8BE2 service");
    assert(invoiceSource.includes("translateStoreOrderInvoiceEmailText"), "\u53D1\u7968\u9875\u5E94\u63A5\u5165\u53D1\u7968\u90AE\u4EF6\u6587\u672C\u7FFB\u8BD1 service");
    assert(invoiceSource.includes("createStoreOrderInvoiceEmailJobPoller"), "\u53D1\u7968\u9875\u5E94\u4F7F\u7528\u53D1\u7968\u90AE\u4EF6 job \u8F6E\u8BE2\u5668");
    assert(invoiceSource.includes("InvoiceEmailSentStatusText"), "\u53D1\u7968\u9875\u5E94\u590D\u7528\u53D1\u7968\u90AE\u4EF6\u53D1\u9001\u72B6\u6001\u63D0\u793A\u7EC4\u4EF6");
    assert(invoiceSource.includes("stopInvoiceEmailPollingRef.current?.()"), "\u53D1\u7968\u9875\u5378\u8F7D\u65F6\u5E94\u6E05\u7406\u90AE\u4EF6 job \u8F6E\u8BE2");
    assert(invoiceSource.includes("result.status === 'Succeeded'"), "\u53D1\u7968\u9875\u5E94\u5904\u7406\u90AE\u4EF6\u53D1\u9001\u6210\u529F\u7EC8\u6001");
    assert(invoiceSource.includes("result.status === 'Failed'"), "\u53D1\u7968\u9875\u5E94\u5904\u7406\u90AE\u4EF6\u53D1\u9001\u5931\u8D25\u7EC8\u6001");
    assert(invoiceSource.includes("t('warehouse.invoice.emailJobSubmitted')"), "\u53D1\u7968\u9875\u63D0\u4EA4 job \u540E\u5E94\u7ACB\u5373\u63D0\u793A\u4EFB\u52A1\u5DF2\u63D0\u4EA4");
    assert(invoiceSource.includes("updateStoreOrderStoreContact"), "\u53D1\u7968\u9875\u5E94\u53EF\u628A\u7F16\u8F91\u540E\u7684\u90AE\u7BB1\u4FDD\u5B58\u4E3A\u5206\u5E97\u9ED8\u8BA4\u90AE\u7BB1");
    assert(invoiceSource.includes("downloadElementAsPdf"), "\u53D1\u7968\u9875\u4E0B\u8F7D PDF \u6309\u94AE\u5E94\u4FDD\u7559\u524D\u7AEF\u5BFC\u51FA\u903B\u8F91");
    assert(invoiceSource.includes("downloadInvoiceExcel"), "\u53D1\u7968\u9875\u5BFC\u51FA Excel \u6309\u94AE\u5E94\u4FDD\u7559\u524D\u7AEF\u5BFC\u51FA\u903B\u8F91");
    assert(!invoiceSource.includes("createStoreOrderInvoicePdfBase64"), "\u53D1\u7968\u90AE\u4EF6\u4E0D\u5E94\u4FDD\u7559\u524D\u7AEF\u90AE\u4EF6 PDF \u751F\u6210 helper");
    assert(
      !invoiceSource.includes("const pdfBase64 = await createStoreOrderInvoicePdfBase64()"),
      "\u53D1\u9001\u90AE\u4EF6\u65F6\u4E0D\u5E94\u518D\u7531\u524D\u7AEF\u751F\u6210 PDF base64"
    );
    assert(!invoiceSource.includes("pdfBase64,"), "\u53D1\u9001\u90AE\u4EF6 payload \u4E0D\u5E94\u5305\u542B pdfBase64");
    assert(!invoiceSource.includes("pdfFileName,"), "\u53D1\u9001\u90AE\u4EF6 payload \u4E0D\u5E94\u5305\u542B pdfFileName");
    assert(invoiceSource.includes("emailModalLanguage"), "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u5E94\u7EF4\u62A4\u5C40\u90E8\u8BED\u8A00\u72B6\u6001");
    assert(invoiceSource.includes("emailSubjectTouched"), "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u5E94\u8BB0\u5F55\u4E3B\u9898\u662F\u5426\u88AB\u624B\u52A8\u7F16\u8F91");
    assert(invoiceSource.includes("emailBodyTouched"), "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u5E94\u8BB0\u5F55\u6B63\u6587\u662F\u5426\u88AB\u624B\u52A8\u7F16\u8F91");
    assert(invoiceSource.includes("translatingEmailText"), "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u7FFB\u8BD1\u671F\u95F4\u5E94\u6709 loading \u72B6\u6001");
    assert(invoiceSource.includes("lng: emailModalLanguage"), "\u5F39\u7A97\u6587\u6848\u5E94\u4F7F\u7528\u5C40\u90E8\u8BED\u8A00\u6E32\u67D3");
    assert(!invoiceSource.includes("i18n.changeLanguage"), "\u5F39\u7A97\u8BED\u8A00\u5207\u6362\u4E0D\u5E94\u6539\u53D8\u5168\u7AD9\u8BED\u8A00");
    assert(invoiceSource.includes("translateStoreOrderInvoiceEmailText({"), "\u624B\u52A8\u7F16\u8F91\u540E\u7684\u4E3B\u9898/\u6B63\u6587\u5207\u6362\u8BED\u8A00\u5E94\u8C03\u7528\u7FFB\u8BD1\u63A5\u53E3");
    assert(invoiceSource.includes("setEmailSubjectTouched(true)"), "\u7528\u6237\u7F16\u8F91\u4E3B\u9898\u65F6\u5E94\u6807\u8BB0 touched");
    assert(invoiceSource.includes("setEmailBodyTouched(true)"), "\u7528\u6237\u7F16\u8F91\u6B63\u6587\u65F6\u5E94\u6807\u8BB0 touched");
    assert(invoiceSource.includes("t('warehouse.invoice.sendEmail')"), "\u53D1\u7968\u9875\u5E94\u63D0\u4F9B\u53D1\u9001\u90AE\u4EF6\u6309\u94AE\u6587\u6848");
    assert(
      invoiceSource.includes("t('warehouse.invoice.emailModalTitle', { lng: emailModalLanguage })"),
      "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u6807\u9898\u5E94\u4F7F\u7528\u5C40\u90E8\u8BED\u8A00"
    );
    assert(
      invoiceSource.includes("t('warehouse.invoice.saveAsStoreDefault', { lng: emailModalLanguage })"),
      "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u4FDD\u5B58\u9ED8\u8BA4\u90AE\u7BB1\u5F00\u5173\u5E94\u4F7F\u7528\u5C40\u90E8\u8BED\u8A00"
    );
    assert(
      invoiceSource.includes("<InvoiceEmailSentStatusText info={order?.invoiceEmailSentInfo} t={t} lng={emailModalLanguage} />"),
      "\u53D1\u7968\u90AE\u4EF6\u5F39\u7A97\u6536\u4EF6\u4EBA\u8F93\u5165\u6846\u4E0A\u65B9\u5E94\u663E\u793A\u4E0A\u6B21\u53D1\u9001\u63D0\u793A"
    );
  });
  if (emailEntryFailure) failures.push(emailEntryFailure);
  const excelFileNameFailure = await runTest("\u53D1\u7968 Excel \u5BFC\u51FA\u6587\u4EF6\u540D\u5E94\u56FA\u5B9A\u4F7F\u7528\u5927\u5199 INVOICE \u524D\u7F00", () => {
    assert(
      invoiceSource.includes("link.download = buildDocumentFileName(\n    'INVOICE',"),
      "\u53D1\u7968 Excel \u5BFC\u51FA\u6587\u4EF6\u540D\u5E94\u4F7F\u7528 INVOICE \u524D\u7F00"
    );
    assert(invoiceSource.includes("const invoiceDateSource = order?.outboundDate || order?.orderDate"), "\u53D1\u7968\u65E5\u671F\u5E94\u4F18\u5148\u4F7F\u7528\u51FA\u5E93\u65E5\u671F\uFF0C\u8BA2\u5355\u65E5\u671F\u53EA\u505A\u515C\u5E95");
    assert(invoiceSource.includes("const invoiceFileDate = formatDocumentFileDate(invoiceDateSource)"), "\u53D1\u7968\u6587\u4EF6\u540D\u65E5\u671F\u5E94\u7531\u7EDF\u4E00\u5DE5\u5177\u683C\u5F0F\u5316");
    assert(invoiceSource.includes("headerInfo.invoiceFileDate"), "\u53D1\u7968 Excel \u6587\u4EF6\u540D\u5E94\u4F20\u5165 invoice \u65E5\u671F");
  });
  if (excelFileNameFailure) failures.push(excelFileNameFailure);
  const excelHeaderFailure = await runTest("\u53D1\u7968 Excel \u5BFC\u51FA\u5E94\u5728\u660E\u7EC6\u524D\u6DFB\u52A0\u8BA2\u5355\u57FA\u7840\u4FE1\u606F\u9875\u5934", () => {
    assert(invoiceSource.includes("const titleRow = worksheet.addRow(['INVOICE'])"), "Excel \u5E94\u6DFB\u52A0 INVOICE \u6807\u9898\u884C");
    assert(invoiceSource.includes("worksheet.mergeCells(titleRow.number, 1, titleRow.number, 9)"), "Excel \u6807\u9898\u884C\u5E94\u8986\u76D6\u65B0\u589E RRP \u540E\u7684 9 \u5217");
    assert(invoiceSource.includes("worksheet.mergeCells(2, 5, 2, 9)"), "Excel \u65E5\u671F\u9875\u5934\u5E94\u8986\u76D6\u65B0\u589E RRP \u540E\u7684\u53F3\u4FA7 5 \u5217");
    assert(invoiceSource.includes("t('warehouse.invoice.invoiceNo'"), "Excel \u9875\u5934\u5E94\u5305\u542B\u53D1\u7968\u53F7");
    assert(invoiceSource.includes("t('warehouse.invoice.invoiceDate'"), "Excel \u9875\u5934\u5E94\u5305\u542B\u53D1\u7968\u65E5\u671F");
    assert(invoiceSource.includes("t('warehouse.invoice.customer')"), "Excel \u9875\u5934\u5E94\u5305\u542B\u5BA2\u6237");
    assert(invoiceSource.includes("t('warehouse.invoice.customerContact')"), "Excel \u9875\u5934\u5E94\u5305\u542B\u5BA2\u6237\u8054\u7CFB\u65B9\u5F0F");
    assert(invoiceSource.includes("t('warehouse.invoice.address')"), "Excel \u9875\u5934\u5E94\u5305\u542B\u5730\u5740");
    assert(
      invoiceSource.includes("storeContact: order.storeContactEmail || store?.contactEmail || '-'"),
      "\u524D\u7AEF Excel \u5BA2\u6237\u8054\u7CFB\u65B9\u5F0F\u5E94\u4E0E\u90AE\u4EF6\u9644\u4EF6\u7EDF\u4E00\u4F7F\u7528\u8054\u7CFB\u90AE\u7BB1\u53E3\u5F84"
    );
  });
  if (excelHeaderFailure) failures.push(excelHeaderFailure);
  const invoiceRrpFailure = await runTest("\u53D1\u7968\u9875\u9762\u548C Excel \u5E94\u5728\u6210\u672C\u540E\u5C55\u793A RRP \u5217", () => {
    assert(invoiceSource.includes("t('warehouse.invoice.excel.rrp')"), "Excel \u660E\u7EC6\u8868\u5934\u5E94\u5305\u542B RRP \u7FFB\u8BD1\u952E");
    assert(invoiceSource.includes("t('column.rrp')"), "\u53D1\u7968\u9875\u9762\u8868\u5934\u5E94\u590D\u7528\u901A\u7528 RRP \u7FFB\u8BD1");
    assert(invoiceSource.includes("{ key: 'rrp', width: 12 }"), "Excel columns \u5E94\u5728\u6210\u672C\u540E\u58F0\u660E RRP \u5217");
    assert(invoiceSource.includes("rrp: item.rrp ?? null"), "Excel \u660E\u7EC6\u5E94\u8BFB\u53D6 item.rrp\uFF0C\u7F3A\u5931\u65F6\u7559\u7A7A");
    assert(invoiceSource.includes(`<th className="col-rrp">{t('column.rrp')}</th>`), "\u53D1\u7968\u8868\u683C\u5E94\u65B0\u589E RRP \u8868\u5934");
    assert(invoiceSource.includes('<td className="col-rrp">{formatOptionalCurrency(item.rrp)}</td>'), "\u53D1\u7968\u8868\u683C RRP \u7F3A\u5931\u65F6\u5E94\u663E\u793A --");
    assert(invoiceSource.includes("worksheet.getColumn('rrp').numFmt = '$#,##0.00'"), "Excel RRP \u5217\u5E94\u4F7F\u7528\u8D27\u5E01\u683C\u5F0F");
    assert(invoiceSource.includes("return value === undefined || value === null ? '--' : formatCurrency(value)"), "RRP \u7A7A\u503C\u4E0D\u5E94\u88AB\u683C\u5F0F\u5316\u4E3A $0.00");
  });
  if (invoiceRrpFailure) failures.push(invoiceRrpFailure);
  const invoicePdfBreakFailure = await runTest("\u53D1\u7968 PDF \u5BFC\u51FA\u5E94\u6309\u660E\u7EC6\u884C\u548C\u9875\u811A\u8FB9\u754C\u5207\u9875", () => {
    assert(invoiceSource.includes("collectElementBreakOffsets"), "\u53D1\u7968\u9875\u5E94\u5F15\u5165 PDF \u884C\u8FB9\u754C\u6536\u96C6\u5DE5\u5177");
    assert(
      invoiceSource.includes("'.store-order-invoice-table tbody tr'"),
      "\u53D1\u7968 PDF \u5E94\u6536\u96C6\u660E\u7EC6\u8868\u683C\u884C\u8FB9\u754C"
    );
    assert(
      invoiceSource.includes("'.store-order-invoice-footer'"),
      "\u53D1\u7968 PDF \u5E94\u6536\u96C6\u53D1\u7968\u9875\u811A\u8FB9\u754C"
    );
    assert(invoiceSource.includes("avoidBreakOffsets: getInvoicePdfBreakOffsets()"), "\u4E0B\u8F7D PDF \u5E94\u4F20\u5165\u53D1\u7968\u5207\u9875\u8FB9\u754C");
    assert(!invoiceSource.includes("createElementPdfBase64(printRootRef.current"), "\u90AE\u4EF6\u94FE\u8DEF\u4E0D\u5E94\u518D\u751F\u6210 PDF base64");
  });
  if (invoicePdfBreakFailure) failures.push(invoicePdfBreakFailure);
  const emailDefaultFailure = await runTest("\u53D1\u7968\u9875\u9ED8\u8BA4\u90AE\u7BB1\u4E0E\u5730\u5740\u8BFB\u53D6\u987A\u5E8F\u5E94\u4FDD\u6301\u4E1A\u52A1\u7EA6\u675F", () => {
    assert(
      invoiceSource.includes("const storeAddress = order.storeAddress || store?.address || '--'"),
      "\u53D1\u7968\u5730\u5740\u5E94\u4F18\u5148\u8BFB\u53D6\u8BA2\u5355\u5730\u5740\uFF0C\u518D\u56DE\u9000\u5230\u5206\u5E97\u5730\u5740"
    );
    assert(
      invoiceSource.includes("order.storeContactEmail || store?.contactEmail || ''"),
      "\u53D1\u9001\u90AE\u4EF6\u9ED8\u8BA4\u6536\u4EF6\u4EBA\u5E94\u4F18\u5148\u8BFB\u53D6\u8BA2\u5355\u90AE\u7BB1\uFF0C\u518D\u56DE\u9000\u5230\u5206\u5E97\u9ED8\u8BA4\u90AE\u7BB1"
    );
  });
  if (emailDefaultFailure) failures.push(emailDefaultFailure);
  const invoiceAmountFailure = await runTest("\u53D1\u7968\u91D1\u989D\u5E94\u8BFB\u53D6\u53D1\u8D27\u91D1\u989D\u5B57\u6BB5\u800C\u4E0D\u662F\u8BA2\u8D27\u91D1\u989D\u5B57\u6BB5", () => {
    assert(
      invoiceSource.includes("order.totalAllocatedImportAmount ?? order.totalImportAmount"),
      "\u53D1\u7968\u6574\u5355\u5C0F\u8BA1\u5E94\u4F18\u5148\u8BFB\u53D6 totalAllocatedImportAmount\uFF0C\u5E76\u53EA\u7528 totalImportAmount \u517C\u5BB9\u65E7\u54CD\u5E94"
    );
    assert(
      invoiceSource.includes("item.allocatedImportAmount ?? allocQuantity * Number(item.importPrice || 0)"),
      "\u53D1\u7968\u660E\u7EC6\u5C0F\u8BA1\u5E94\u4F18\u5148\u8BFB\u53D6 allocatedImportAmount\uFF0C\u5E76\u6309\u53D1\u8D27\u6570\u91CF\u515C\u5E95"
    );
  });
  if (invoiceAmountFailure) failures.push(invoiceAmountFailure);
  const invoiceCssFailure = await runTest("\u53D1\u7968 print.css \u5E94\u53EA\u8C03\u6574\u53D1\u7968\u89C4\u5219\u4E14\u907F\u514D\u6A2A\u5411\u6EA2\u51FA", () => {
    const paperRule = readCssRule(printCssSource, ".store-order-invoice-paper");
    const tableRule = readCssRule(printCssSource, ".store-order-invoice-table");
    const thRule = readCssRule(printCssSource, ".store-order-invoice-table th");
    const tdRule = readCssRule(printCssSource, ".store-order-invoice-table td");
    const barcodeRule = readCssRule(printCssSource, ".store-order-invoice-table .col-barcode");
    const rrpRule = readCssRule(printCssSource, ".store-order-invoice-table .col-rrp");
    assert(/padding:\s*\d+mm\s+\d+mm/.test(paperRule), "\u53D1\u7968\u7EB8\u5F20 padding \u5E94\u6536\u7A84\u5230\u66F4\u7D27\u51D1\u7684\u6BEB\u7C73\u7EA7\u8BBE\u7F6E");
    assert(/table-layout:\s*fixed/.test(tableRule), "\u53D1\u7968\u8868\u683C\u5E94\u4F7F\u7528\u56FA\u5B9A\u5217\u5E03\u5C40\u9632\u6B62\u6491\u5BBD");
    assert(/font-size:\s*1[01]px/.test(tableRule), "\u53D1\u7968\u8868\u683C\u5B57\u4F53\u5E94\u7F29\u5C0F\u5230 10-11px");
    assert(/padding:\s*[45]px/.test(thRule), "\u53D1\u7968\u8868\u5934\u5185\u8FB9\u8DDD\u5E94\u7F29\u5C0F");
    assert(/padding:\s*[45]px/.test(tdRule), "\u53D1\u7968\u5355\u5143\u683C\u5185\u8FB9\u8DDD\u5E94\u7F29\u5C0F");
    assert(/overflow:\s*hidden/.test(barcodeRule) || /word-break:\s*break-all/.test(barcodeRule), "\u6761\u7801\u5217\u5E94\u9650\u5236\u6EA2\u51FA");
    assert(/width:\s*58px/.test(rrpRule), "RRP \u5217\u5E94\u4F7F\u7528\u7D27\u51D1\u4EF7\u683C\u5217\u5BBD\u5EA6");
    assert(/text-align:\s*right/.test(rrpRule), "RRP \u5217\u5E94\u53F3\u5BF9\u9F50\u65B9\u4FBF\u4EF7\u683C\u626B\u63CF");
    assert(!printCssSource.includes(".store-order-detail-table"), "print.css \u4E0D\u5E94\u6C61\u67D3\u8BE6\u60C5\u9875\u7D27\u51D1\u6837\u5F0F");
    assert(!printCssSource.includes(".store-order-list-table"), "print.css \u4E0D\u5E94\u6C61\u67D3\u5217\u8868\u9875\u7D27\u51D1\u6837\u5F0F");
  });
  if (invoiceCssFailure) failures.push(invoiceCssFailure);
  const translationFailure = await runTest("\u53D1\u7968\u90AE\u4EF6\u6587\u6848\u5E94\u63D0\u4F9B\u4E2D\u82F1\u6587\u7FFB\u8BD1", () => {
    for (const key of [
      "invoiceEmailLabel",
      "invoiceEmailNotSent",
      "invoiceEmailSent",
      "invoiceEmailLastSentAt",
      "invoiceEmailRecipient",
      "sendEmail",
      "emailModalTitle",
      "recipientEmail",
      "emailSubject",
      "emailBody",
      "saveAsStoreDefault",
      "emailJobSubmitted",
      "emailSendSuccess",
      "emailSendFailed",
      "emailJobPollingFailed",
      "emailJobPollingTimeout",
      "emailLanguage",
      "emailLanguageChinese",
      "emailLanguageEnglish",
      "emailTranslateFailed"
    ]) {
      assert(zhSource.includes(`"${key}"`), `\u4E2D\u6587\u7FFB\u8BD1\u7F3A\u5C11 ${key}`);
      assert(enSource.includes(`"${key}"`), `\u82F1\u6587\u7FFB\u8BD1\u7F3A\u5C11 ${key}`);
    }
    assert(zhMessages?.warehouse?.invoice?.excel?.rrp === "RRP", "\u4E2D\u6587\u53D1\u7968 Excel \u7FFB\u8BD1\u7F3A\u5C11 warehouse.invoice.excel.rrp");
    assert(enMessages?.warehouse?.invoice?.excel?.rrp === "RRP", "\u82F1\u6587\u53D1\u7968 Excel \u7FFB\u8BD1\u7F3A\u5C11 warehouse.invoice.excel.rrp");
  });
  if (translationFailure) failures.push(translationFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("storeOrderInvoice.logic.test: ok");
}
await main();
