// src/pages/PosAdmin/LocalSupplierInvoices/importPreview.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/PosAdmin/LocalSupplierInvoices/importPreview.ts
var REQUIRED_IMPORT_FIELDS = [
  "itemNumber",
  "barcode",
  "productName",
  "quantity",
  "price"
];
var FIELD_TO_MAPPING_KEY = {
  itemNumber: "itemNumberColumnKey",
  barcode: "barcodeColumnKey",
  productName: "productNameColumnKey",
  quantity: "quantityColumnKey",
  price: "priceColumnKey"
};
function normalizeColumnKey(value) {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}
function sanitizeCellValue(value) {
  return typeof value === "string" ? value.trim() : "";
}
function parseNumericCell(value) {
  const sanitized = sanitizeCellValue(value);
  if (!sanitized) {
    return void 0;
  }
  const normalized = sanitized.replace(/\s+/g, "").replace(/[$￥€,]/g, "").replace(/\((.*)\)/, "-$1");
  if (!/^[-+]?\d+(\.\d+)?$/.test(normalized)) {
    return void 0;
  }
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : void 0;
}
function readMappedCell(rawValues, mapping, field) {
  const columnKey = normalizeColumnKey(mapping[FIELD_TO_MAPPING_KEY[field]]);
  return columnKey === null ? "" : sanitizeCellValue(rawValues[columnKey]);
}
function normalizeImportColumnMapping(mapping) {
  return {
    itemNumberColumnKey: normalizeColumnKey(mapping?.itemNumberColumnKey),
    barcodeColumnKey: normalizeColumnKey(mapping?.barcodeColumnKey),
    productNameColumnKey: normalizeColumnKey(mapping?.productNameColumnKey),
    quantityColumnKey: normalizeColumnKey(mapping?.quantityColumnKey),
    priceColumnKey: normalizeColumnKey(mapping?.priceColumnKey)
  };
}
function hasRequiredImportMappings(mapping) {
  const normalized = normalizeImportColumnMapping(mapping);
  return REQUIRED_IMPORT_FIELDS.every((field) => normalized[FIELD_TO_MAPPING_KEY[field]] !== null);
}
function hasDuplicateImportMappings(mapping) {
  const normalized = normalizeImportColumnMapping(mapping);
  const selectedColumns = REQUIRED_IMPORT_FIELDS.map((field) => normalized[FIELD_TO_MAPPING_KEY[field]]).filter((value) => value !== null);
  return new Set(selectedColumns).size !== selectedColumns.length;
}
function isLegacyExcelFileName(fileName) {
  return /\.xls$/i.test(fileName) && !/\.(xlsx|xlsm)$/i.test(fileName);
}
function buildImportPreviewLines(lines, mapping) {
  const normalized = normalizeImportColumnMapping(mapping);
  return lines.map((line, index) => {
    const itemNumber = readMappedCell(line.rawValues, normalized, "itemNumber");
    const barcode = readMappedCell(line.rawValues, normalized, "barcode");
    const productName = readMappedCell(line.rawValues, normalized, "productName");
    const quantity = parseNumericCell(readMappedCell(line.rawValues, normalized, "quantity"));
    const price = parseNumericCell(readMappedCell(line.rawValues, normalized, "price"));
    const amount = quantity !== void 0 && price !== void 0 ? Number((quantity * price).toFixed(2)) : void 0;
    return {
      key: `${line.rowNumber ?? index}-${Object.values(line.rawValues).join("|")}`,
      rowNumber: line.rowNumber,
      itemNumber,
      barcode,
      productName,
      quantity,
      price,
      amount,
      rawValues: line.rawValues
    };
  });
}

// src/pages/PosAdmin/LocalSupplierInvoices/importPreview.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
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
var modalFile = path.resolve(process.cwd(), "src/pages/PosAdmin/LocalSupplierInvoices/ImportInvoiceModal.tsx");
var modalSource = readFileSync(modalFile, "utf8");
async function main() {
  const failures = [];
  const uploadAcceptFailure = await runTest("\u5BFC\u5165\u5F39\u7A97\u5E94\u9650\u5236\u6587\u4EF6\u9009\u62E9\u4E3A xlsx\u3001xlsm \u548C pdf", () => {
    assert(
      modalSource.includes('accept=".xlsx,.xlsm,.pdf"'),
      "\u6587\u4EF6\u9009\u62E9\u63A7\u4EF6\u5E94\u663E\u5F0F\u9650\u5236\u4E3A .xlsx,.xlsm,.pdf"
    );
  });
  if (uploadAcceptFailure) failures.push(uploadAcceptFailure);
  const legacyExcelFailure = await runTest(".xls \u5E94\u88AB\u8BC6\u522B\u4E3A\u4E0D\u652F\u6301\u7684\u65E7\u7248 Excel", () => {
    assertEqual(isLegacyExcelFileName("invoice.xls"), true, ".xls \u6587\u4EF6\u5E94\u88AB\u62D2\u7EDD");
    assertEqual(isLegacyExcelFileName("invoice.xlsx"), false, ".xlsx \u6587\u4EF6\u4E0D\u5E94\u88AB\u62D2\u7EDD");
    assertEqual(isLegacyExcelFileName("invoice.xlsm"), false, ".xlsm \u6587\u4EF6\u4E0D\u5E94\u88AB\u62D2\u7EDD");
    assertEqual(isLegacyExcelFileName("invoice.pdf"), false, ".pdf \u6587\u4EF6\u4E0D\u5E94\u88AB\u62D2\u7EDD");
  });
  if (legacyExcelFailure) failures.push(legacyExcelFailure);
  const mappingRequiredFailure = await runTest("\u4E94\u9879\u5B57\u6BB5\u6620\u5C04\u5FC5\u987B\u5168\u90E8\u63D0\u4F9B\u4E14\u4E0D\u80FD\u91CD\u590D", () => {
    assertEqual(
      hasRequiredImportMappings({
        itemNumberColumnKey: "col_1",
        barcodeColumnKey: "col_2",
        productNameColumnKey: "col_3",
        quantityColumnKey: "col_4"
      }),
      false,
      "\u7F3A\u5C11\u4EF7\u683C\u5217\u65F6\u4E0D\u5E94\u5141\u8BB8\u7EE7\u7EED"
    );
    assertEqual(
      hasRequiredImportMappings({
        itemNumberColumnKey: "col_1",
        barcodeColumnKey: "col_2",
        productNameColumnKey: "col_3",
        quantityColumnKey: "col_4",
        priceColumnKey: "col_5"
      }),
      true,
      "\u4E94\u9879\u5B57\u6BB5\u6620\u5C04\u9F50\u5168\u65F6\u5E94\u5141\u8BB8\u7EE7\u7EED"
    );
    assertEqual(
      hasDuplicateImportMappings({
        itemNumberColumnKey: "col_1",
        barcodeColumnKey: "col_2",
        productNameColumnKey: "col_3",
        quantityColumnKey: "col_4",
        priceColumnKey: "col_4"
      }),
      true,
      "\u540C\u4E00\u5217\u4E0D\u80FD\u540C\u65F6\u6620\u5C04\u5230\u6570\u91CF\u548C\u4EF7\u683C"
    );
  });
  if (mappingRequiredFailure) failures.push(mappingRequiredFailure);
  const previewRemapFailure = await runTest("\u4FEE\u6539\u5217\u6620\u5C04\u540E\u5E94\u6839\u636E rawValues \u91CD\u65B0\u751F\u6210\u9884\u89C8\u660E\u7EC6", () => {
    const lines = [
      {
        rowNumber: 7,
        rawValues: {
          col_1: "HB001",
          col_2: "935001",
          col_3: "\u82F9\u679C",
          col_4: "2",
          col_5: "3.50"
        }
      }
    ];
    const defaultPreview = buildImportPreviewLines(lines, {
      itemNumberColumnKey: "col_1",
      barcodeColumnKey: "col_2",
      productNameColumnKey: "col_3",
      quantityColumnKey: "col_4",
      priceColumnKey: "col_5"
    });
    const remappedPreview = buildImportPreviewLines(lines, {
      itemNumberColumnKey: "col_3",
      barcodeColumnKey: "col_2",
      productNameColumnKey: "col_1",
      quantityColumnKey: "col_5",
      priceColumnKey: "col_4"
    });
    assertDeepEqual(
      defaultPreview[0],
      {
        key: "7-HB001|935001|\u82F9\u679C|2|3.50",
        rowNumber: 7,
        itemNumber: "HB001",
        barcode: "935001",
        productName: "\u82F9\u679C",
        quantity: 2,
        price: 3.5,
        amount: 7,
        rawValues: {
          col_1: "HB001",
          col_2: "935001",
          col_3: "\u82F9\u679C",
          col_4: "2",
          col_5: "3.50"
        }
      },
      "\u9ED8\u8BA4\u6620\u5C04\u5E94\u6309\u539F\u59CB\u5217\u987A\u5E8F\u751F\u6210\u9884\u89C8"
    );
    assertDeepEqual(
      remappedPreview[0],
      {
        key: "7-HB001|935001|\u82F9\u679C|2|3.50",
        rowNumber: 7,
        itemNumber: "\u82F9\u679C",
        barcode: "935001",
        productName: "HB001",
        quantity: 3.5,
        price: 2,
        amount: 7,
        rawValues: {
          col_1: "HB001",
          col_2: "935001",
          col_3: "\u82F9\u679C",
          col_4: "2",
          col_5: "3.50"
        }
      },
      "\u4FEE\u6539\u6620\u5C04\u540E\u9884\u89C8\u884C\u5E94\u57FA\u4E8E rawValues \u91CD\u65B0\u8BA1\u7B97\u5B57\u6BB5\u548C\u503C"
    );
  });
  if (previewRemapFailure) failures.push(previewRemapFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25\\n- ${failures.join("\\n- ")}`);
  }
  console.log("importPreview.test: ok");
}
await main();
