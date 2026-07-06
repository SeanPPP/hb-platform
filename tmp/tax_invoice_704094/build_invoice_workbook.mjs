import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const parsedPath = path.join(__dirname, "parsed_invoice.json");
const outputDir = "/Users/sean/DEV/hb-platform/outputs/tax_invoice_704094";
const outputPath = path.join(outputDir, "TAX INVOICE - 704094.xlsx");
const previewPath = path.join(outputDir, "invoice-preview.png");

const payload = JSON.parse(await fs.readFile(parsedPath, "utf8"));
const { invoice, rows } = payload;

function parseInvoiceDate(value) {
  const match = /^(\d{2})-([A-Z]{3})-(\d{2})$/.exec(value);
  if (!match) return value;
  const months = {
    JAN: 0,
    FEB: 1,
    MAR: 2,
    APR: 3,
    MAY: 4,
    JUN: 5,
    JUL: 6,
    AUG: 7,
    SEP: 8,
    OCT: 9,
    NOV: 10,
    DEC: 11,
  };
  const [, day, month, year] = match;
  return new Date(2000 + Number(year), months[month], Number(day));
}

function colName(index) {
  let n = index + 1;
  let name = "";
  while (n > 0) {
    const r = (n - 1) % 26;
    name = String.fromCharCode(65 + r) + name;
    n = Math.floor((n - 1) / 26);
  }
  return name;
}

function idFormula(value) {
  // 中文注释：条码和货号是标识符，不是数字；用文本公式避免 Excel 改成科学计数法。
  return `=TEXT(${String(value).replaceAll('"', '""')},"0")`;
}

const workbook = Workbook.create();
const summary = workbook.worksheets.add("Invoice");
const items = workbook.worksheets.add("Items");

summary.showGridLines = false;
items.showGridLines = false;

const titleFill = "#1F73B7";
const labelFill = "#EAF2FB";
const border = { preset: "all", style: "thin", color: "#D9E2EC" };

summary.getRange("A1:F1").merge();
summary.getRange("A1").values = [["TAX INVOICE 704094"]];
summary.getRange("A1").format = {
  fill: titleFill,
  font: { bold: true, color: "#FFFFFF", size: 16 },
};
summary.getRange("A1:F1").format.rowHeight = 28;

const infoRows = [
  ["Supplier", invoice.supplier, "Invoice No", invoice.invoice_no],
  ["ABN", invoice.supplier_abn, "Invoice Date", parseInvoiceDate(invoice.invoice_date)],
  ["Account", invoice.account, "Order Ref", invoice.order_ref],
  ["Warehouse", invoice.warehouse, "Sales Rep", invoice.sales_rep],
  ["Our Order No", invoice.our_order_no, "Terms", invoice.terms],
  ["Bill To", invoice.bill_to, "Delivery Address", invoice.delivery_address],
  ["Phone", invoice.phone, "FSC COC Code", invoice.fsc_coc_code],
  ["Source PDF", invoice.source_file, "Parsed Lines", invoice.parsed_line_count],
];
summary.getRange(`A3:D${2 + infoRows.length}`).values = infoRows;
summary.getRange(`A3:A${2 + infoRows.length}`).format = {
  fill: labelFill,
  font: { bold: true },
};
summary.getRange(`C3:C${2 + infoRows.length}`).format = {
  fill: labelFill,
  font: { bold: true },
};
summary.getRange(`A3:D${2 + infoRows.length}`).format.borders = border;
summary.getRange("D4").format.numberFormat = "dd-mmm-yy";
summary.getRange("B8").format.wrapText = true;
summary.getRange("D8").format.wrapText = true;

summary.getRange("F3:G3").values = [["Summary", "Amount"]];
summary.getRange("F3:G3").format = {
  fill: titleFill,
  font: { bold: true, color: "#FFFFFF" },
};
summary.getRange("F4:G10").values = [
  ["Total Ex", invoice.total_ex],
  ["GST", invoice.gst],
  ["Total", invoice.total],
  ["Total Cartons", invoice.total_cartons],
  ["Total Weight", invoice.total_weight],
  ["Calculated Total Ex", null],
  ["Difference", null],
];
summary.getRange("G9").formulas = [[`=SUM(Items!K2:K${rows.length + 1})`]];
summary.getRange("G10").formulas = [["=G9-G4"]];
summary.getRange("F4:F10").format = {
  fill: labelFill,
  font: { bold: true },
};
summary.getRange("F3:G10").format.borders = border;
summary.getRange("G4:G6").format.numberFormat = "#,##0.00";
summary.getRange("G7:G8").format.numberFormat = "#,##0.00";
summary.getRange("G9:G10").format.numberFormat = "#,##0.00";

summary.getRange("F12:G15").values = [
  ["Bank Account", invoice.bank_account_name],
  ["Currency", invoice.bank_currency],
  ["BSB", invoice.bank_bsb],
  ["Account No", invoice.bank_account_no],
];
summary.getRange("F12:F15").format = {
  fill: labelFill,
  font: { bold: true },
};
summary.getRange("F12:G15").format.borders = border;

summary.getRange("A1:G15").format.font = { name: "Aptos" };
summary.getRange("A:A").format.columnWidth = 16;
summary.getRange("B:B").format.columnWidth = 48;
summary.getRange("C:C").format.columnWidth = 16;
summary.getRange("D:D").format.columnWidth = 42;
summary.getRange("F:F").format.columnWidth = 20;
summary.getRange("G:G").format.columnWidth = 18;

const headers = [
  "Page",
  "Item Code",
  "Customer Item Code",
  "Item Description",
  "INNR/OUTR",
  "Ordered Qty",
  "Invoiced Qty",
  "UOM",
  "Unit Price",
  "Disc %",
  "Line Total",
];
const itemValues = [
  headers,
  ...rows.map((row) => [
    row.page,
    "",
    "",
    row.item_description,
    row.innr_outr,
    row.ordered_qty,
    row.invoiced_qty,
    row.uom,
    row.unit_price,
    row.disc_percent,
    row.line_total,
  ]),
];
const lastRow = itemValues.length;
const lastCol = colName(headers.length - 1);
items.getRange(`A1:${lastCol}${lastRow}`).values = itemValues;
items.getRange(`B2:C${lastRow}`).formulas = rows.map((row) => [
  idFormula(row.item_code),
  idFormula(row.customer_item_code),
]);
items.getRange(`A1:${lastCol}1`).format = {
  fill: titleFill,
  font: { bold: true, color: "#FFFFFF" },
  horizontalAlignment: "center",
};
items.getRange(`A1:${lastCol}${lastRow}`).format.borders = {
  insideHorizontal: { style: "thin", color: "#E5E7EB" },
  insideVertical: { style: "thin", color: "#F0F3F7" },
  top: { style: "thin", color: "#CBD5E1" },
  bottom: { style: "thin", color: "#CBD5E1" },
  left: { style: "thin", color: "#CBD5E1" },
  right: { style: "thin", color: "#CBD5E1" },
};
items.getRange(`A2:A${lastRow}`).format.numberFormat = "0";
items.getRange(`B2:C${lastRow}`).format.numberFormat = "0";
items.getRange(`D2:D${lastRow}`).format.wrapText = true;
items.getRange(`F2:G${lastRow}`).format.numberFormat = "#,##0";
items.getRange(`I2:K${lastRow}`).format.numberFormat = "#,##0.00";
items.getRange(`A1:${lastCol}${lastRow}`).format.font = { name: "Aptos", size: 10 };
items.getRange(`I2:K${lastRow}`).format.horizontalAlignment = "right";
items.freezePanes.freezeRows(1);
const table = items.tables.add(`A1:${lastCol}${lastRow}`, true, "InvoiceItems");
table.style = "TableStyleMedium2";

const widths = [8, 13, 18, 62, 11, 12, 12, 9, 12, 10, 13];
for (const [index, width] of widths.entries()) {
  const col = colName(index);
  items.getRange(`${col}:${col}`).format.columnWidth = width;
}

await fs.mkdir(outputDir, { recursive: true });
const preview = await workbook.render({
  sheetName: "Invoice",
  autoCrop: "all",
  scale: 1,
  format: "png",
});
await fs.writeFile(previewPath, new Uint8Array(await preview.arrayBuffer()));

const check = await workbook.inspect({
  kind: "table",
  sheetId: "Items",
  range: `A1:${lastCol}${Math.min(lastRow, 8)}`,
  include: "values,formulas",
  tableMaxRows: 8,
  tableMaxCols: 11,
});
console.log(check.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 300 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

const xlsx = await SpreadsheetFile.exportXlsx(workbook);
await xlsx.save(outputPath);
console.log(JSON.stringify({ outputPath, previewPath, rows: rows.length }));
