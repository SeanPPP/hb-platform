import type {
  ProductLabelPrintPayload,
  WarehouseLocationLabelPrintPayload,
  WarehouseProductLabelPrintPayload,
} from "@/modules/printer/types";

const CRLF = "\r\n";
const STANDARD_WIDTH = 570;
const STANDARD_HEIGHT = 400;
const SMALL_WIDTH = 472;
const SMALL_HEIGHT = 320;
const WAREHOUSE_HEIGHT = 208;

function command(lines: string[]) {
  return lines.join(CRLF) + CRLF;
}

function cpclText(value: unknown, maxLength = 80) {
  const text = typeof value === "string" ? value : value == null ? "" : String(value);
  return text.replace(/[\r\n]+/g, " ").replace(/\s+/g, " ").trim().slice(0, maxLength);
}

function text(font: number, x: number, y: number, value: unknown, maxLength?: number) {
  return `TEXT ${font} 0 ${x} ${y} ${cpclText(value, maxLength) || " "}`;
}

function line(x1: number, y1: number, x2: number, y2: number, width = 2) {
  return `LINE ${x1} ${y1} ${x2} ${y2} ${width}`;
}

function barcode(kind: "EAN13" | "128", x: number, y: number, value: unknown, height = 44, ratio = 2) {
  const safeValue = cpclText(value, 64);
  return safeValue ? `BARCODE ${kind} 1 ${ratio} ${height} ${x} ${y} ${safeValue}` : null;
}

function formatMoney(value: number | null | undefined) {
  const cents = Math.round(((Number.isFinite(value) ? Number(value) : 0) + 1e-8) * 100);
  return (cents / 100).toFixed(2);
}

function formatPriceParts(value: number | null | undefined) {
  const [integer, decimal] = formatMoney(value).split(".");
  return { integer, decimal };
}

function formatOptionalMoney(value: number | null | undefined) {
  return Number.isFinite(value) ? formatMoney(value) : "--";
}

function formatOptionalQuantity(value: number | null | undefined) {
  if (!Number.isFinite(value)) {
    return "--";
  }
  const rounded = Math.round(Number(value));
  return Math.abs(Number(value) - rounded) < 0.01 ? String(rounded) : Number(value).toFixed(2);
}

function formatDate(now = new Date()) {
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${year}/${month}/${day}`;
}

function asNumber(value: number | null | undefined, fallback = 0) {
  return Number.isFinite(value) ? Number(value) : fallback;
}

function discountPercent(value: number | null | undefined) {
  return Math.round(asNumber(value) * 100);
}

function discountLabel(value: number | null | undefined) {
  return `${String(discountPercent(value)).padStart(2, "0")}%OFF`;
}

function discountedPrice(payload: ProductLabelPrintPayload) {
  return asNumber(payload.retailPrice) * (1 - asNumber(payload.discountRate));
}

function isSmallLabel(printType?: string | null) {
  return printType?.trim().toLowerCase() === "small";
}

function isValidEan13(value: string) {
  const barcodeValue = cpclText(value);
  if (!/^\d{13}$/.test(barcodeValue)) {
    return false;
  }
  const checkDigit = barcodeValue
    .slice(0, 12)
    .split("")
    .reduce((sum, char, index) => sum + Number(char) * (index % 2 === 0 ? 1 : 3), 0);
  return (10 - (checkDigit % 10)) % 10 === Number(barcodeValue[12]);
}

function barcodeKind(value: string) {
  return isValidEan13(value) ? "EAN13" : "128";
}

function estimateTextWidth(value: unknown, font: number) {
  const unit = font === 7 ? 28 : 12;
  const text = cpclText(value) || " ";
  const width = Array.from(text).reduce((total, char) => {
    const code = char.codePointAt(0) ?? 0;
    return total + (code > 127 ? unit * 2 : unit);
  }, 0);
  return Math.max(1, width);
}

function truncateTextByWidth(value: unknown, maxWidth: number, font: number) {
  let result = "";
  Array.from(cpclText(value)).some((char) => {
    const next = result + char;
    if (estimateTextWidth(next, font) > maxWidth) {
      return true;
    }
    result = next;
    return false;
  });
  return result || " ";
}

function wrapTextByWidth(value: unknown, maxWidth: number, font: number, maxLines: number) {
  const chars = Array.from(cpclText(value));
  if (!chars.length) {
    return [" "];
  }

  const lines: string[] = [];
  let current = "";
  chars.forEach((char) => {
    const next = current + char;
    if (current && estimateTextWidth(next, font) > maxWidth && lines.length < maxLines - 1) {
      lines.push(current);
      current = char;
      return;
    }
    current = next;
  });

  lines.push(current || " ");
  // CPCL TEXT 不会裁剪宽度，最后一行也要按 Android 位图画布宽度截断。
  return lines.slice(0, maxLines).map((lineText) => truncateTextByWidth(lineText, maxWidth, font));
}

function formatSupplierAbbreviation(value: unknown) {
  const words = cpclText(value)
    .toLocaleLowerCase("en-US")
    .split(/\s+/)
    .filter(Boolean)
    .map((word) => word.charAt(0).toLocaleUpperCase("en-US") + word.slice(1));

  if (!words.length) {
    return "";
  }
  if (words.length === 1) {
    return words[0].slice(0, 3).toLocaleUpperCase("en-US");
  }
  return words
    .slice(0, 4)
    .map((word) => word.charAt(0).toLocaleUpperCase("en-US"))
    .join(".");
}

function formatGrade(value: unknown) {
  return cpclText(value).toLocaleUpperCase("en-US").charAt(0);
}

function addBarcode(
  lines: string[],
  kind: "EAN13" | "128",
  x: number,
  y: number,
  value: unknown,
  height?: number,
  ratio?: number
) {
  const nextBarcode = barcode(kind, x, y, value, height, ratio);
  if (nextBarcode) {
    lines.push(nextBarcode);
  }
}

export function buildProductLabelCommand(payload: ProductLabelPrintPayload, printType?: string | null) {
  const small = isSmallLabel(printType);
  const width = small ? SMALL_WIDTH : STANDARD_WIDTH;
  const height = small ? SMALL_HEIGHT : STANDARD_HEIGHT;
  const barcodeValue = cpclText(payload.barcode);
  const price = formatPriceParts(payload.retailPrice);
  const priceTopY = 30;
  const priceDecimalWidth = estimateTextWidth(price.decimal, 4);
  const priceDotWidth = estimateTextWidth(".", 4);
  const priceIntegerWidth = estimateTextWidth(price.integer, 7);
  const priceCurrencyWidth = estimateTextWidth("$", 4);
  const priceDecimalX = width - priceDecimalWidth;
  const priceDotX = priceDecimalX - priceDotWidth;
  const priceIntegerX = priceDotX - priceIntegerWidth;
  const priceCurrencyX = priceIntegerX - priceCurrencyWidth;
  const itemNumber = cpclText(payload.itemNumber);
  const itemNumberX = 5;
  const itemNumberY = 120;
  const supplierName = formatSupplierAbbreviation(payload.supplierName);
  const dateValue = formatDate();
  const dateWidth = estimateTextWidth(dateValue, 4);
  const dateX = width - dateWidth;
  const grade = formatGrade(payload.grade);
  const nameMaxWidth = Math.max(1, priceCurrencyX - 10);
  const productNameLines = wrapTextByWidth(payload.productName, nameMaxWidth, 4, 2);
  const lines = [
    `! 0 200 200 ${height} 1`,
    `PAGE-WIDTH ${width}`,
  ];

  // iOS 使用 CPCL 文字近似 Android 位图模板，坐标保持和 Android 普通标签一致。
  productNameLines.forEach((lineText, index) => {
    lines.push(text(4, 5, 5 + index * 32, lineText));
  });
  lines.push(text(4, itemNumberX, itemNumberY, itemNumber));
  lines.push(text(4, itemNumberX + estimateTextWidth(itemNumber, 4) + 10, 118, supplierName));

  if (barcodeValue) {
    lines.push("BARCODE-TEXT 7 0 5");
    addBarcode(lines, barcodeKind(barcodeValue), 5, 145, barcodeValue, 30);
  }

  if (asNumber(payload.discountRate) > 0) {
    const nextDiscountLabel = discountLabel(payload.discountRate);
    lines.push(text(4, width - estimateTextWidth(nextDiscountLabel, 4) - dateWidth - 20, 175, nextDiscountLabel));
  }
  if (grade) {
    lines.push(text(4, 300, 175, grade));
  }

  lines.push(text(4, priceCurrencyX, priceTopY, "$"));
  lines.push(text(7, priceIntegerX, priceTopY, price.integer));
  lines.push(text(4, priceDotX, priceTopY + 38, "."));
  lines.push(text(4, priceDecimalX, priceTopY, price.decimal));
  lines.push(text(4, dateX, 175, dateValue));
  lines.push("PRINT");
  return command(lines);
}

export function buildDiscountLabelCommand(payload: ProductLabelPrintPayload, printType?: string | null) {
  const small = isSmallLabel(printType);
  const width = small ? SMALL_WIDTH : STANDARD_WIDTH;
  const height = small ? SMALL_HEIGHT : STANDARD_HEIGHT;
  const rightX = small ? 330 : 420;
  const barcodeValue = cpclText(payload.barcode) || cpclText(payload.itemNumber);
  const lines = [
    `! 0 200 200 ${height} 1`,
    `PAGE-WIDTH ${width}`,
    text(4, 20, 20, payload.productName),
    text(7, rightX, 35, `${discountPercent(payload.discountRate)}% OFF`),
    text(7, rightX, 92, `NOW $${formatMoney(discountedPrice(payload))}`),
    text(4, 20, 190, payload.itemNumber || "--"),
    text(4, 20, 230, formatDate()),
  ];
  addBarcode(lines, "128", 20, 132, barcodeValue, 56);
  lines.push("PRINT");
  return command(lines);
}

export function buildClearanceLabelCommand(payload: ProductLabelPrintPayload) {
  const clearancePrice = Number.isFinite(payload.clearancePrice)
    ? Number(payload.clearancePrice)
    : discountedPrice(payload);
  const barcodeValue = cpclText(payload.clearanceBarcode) || cpclText(payload.barcode);
  const lines = [
    "! 0 200 200 205 1",
    "PAGE-WIDTH 614",
    text(4, 20, 20, payload.productName),
    text(7, 360, 48, `$${formatMoney(clearancePrice)}`),
    text(4, 20, 80, payload.itemNumber || "--"),
    text(7, 260, 145, "CLEARANCE"),
    text(4, 360, 145, formatDate()),
  ];
  addBarcode(lines, "128", 20, 110, barcodeValue, 44);
  lines.push("PRINT");
  return command(lines);
}

export function buildBigDiscountLabelCommand(payload: ProductLabelPrintPayload, printType?: string | null) {
  const discount = discountPercent(payload.discountRate);
  const afterDiscount = discountedPrice(payload);
  const saveAmount = asNumber(payload.retailPrice) - afterDiscount;
  const title = cpclText(printType) || (discount > 0 ? `${discount}% OFF` : "SPECIAL");
  const lines = [
    "! 0 200 200 1200 1",
    "PAGE-WIDTH 480",
    text(7, 120, 70, title),
    text(7, 120, 230, `$${formatMoney(afterDiscount)}`),
    text(4, 20, 360, `WAS $${formatMoney(payload.retailPrice)}`),
    line(20, 388, 160, 388),
    text(4, 20, 410, `SAVE $${formatMoney(saveAmount)}`),
    text(4, 20, 510, payload.productName),
  ];
  addBarcode(lines, "128", 20, 560, payload.barcode, 44);
  lines.push(text(4, 340, 650, formatDate()));
  lines.push("PRINT");
  return command(lines);
}

export function buildWarehouseProductLabelCommand(payload: WarehouseProductLabelPrintPayload) {
  const barcodeValue = cpclText(payload.barcode) || cpclText(payload.itemNumber);
  const displayPrice = payload.retailPrice ?? payload.domesticPrice ?? payload.oemPrice ?? payload.importPrice;
  const costPrice = payload.purchasePrice ?? payload.importPrice ?? payload.domesticPrice ?? payload.oemPrice;
  const lines = [
    `! 0 200 200 ${WAREHOUSE_HEIGHT} 1`,
    `PAGE-WIDTH ${STANDARD_WIDTH}`,
    text(7, 20, 14, "WAREHOUSE PRODUCT"),
    text(4, 20, 46, payload.productName),
    text(4, 20, 66, `ITEM ${payload.itemNumber || "--"}`),
    text(4, 20, 86, `LOC ${payload.locationCode || "UNASSIGNED"}`),
    text(4, 20, 106, payload.locationBarcode || ""),
  ];
  addBarcode(lines, "128", 20, 132, barcodeValue, 38, 1);
  lines.push(text(4, 360, 124, `PK ${formatOptionalQuantity(payload.middlePackageQuantity)}`));
  lines.push(text(4, 360, 152, `COST ${formatOptionalMoney(costPrice)}`));
  lines.push(text(4, 360, 180, `RRP ${formatOptionalMoney(displayPrice)}`));
  lines.push("PRINT");
  return command(lines);
}

export function buildWarehouseLocationLabelCommand(payload: WarehouseLocationLabelPrintPayload) {
  const displayCode = cpclText(payload.locationCode) || cpclText(payload.locationBarcode) || cpclText(payload.locationGuid);
  const barcodeValue = cpclText(payload.locationBarcode) || displayCode;
  const middlePackageQuantity = Math.max(1, Math.round(asNumber(payload.middlePackageQuantity, 1)));
  const lines = [
    `! 0 200 200 ${WAREHOUSE_HEIGHT} 1`,
    `PAGE-WIDTH ${STANDARD_WIDTH}`,
    text(7, 20, 16, "LOCATION"),
    text(7, 20, 46, displayCode || "--"),
    text(4, 20, 82, `ITEM ${payload.itemNumber || "--"}`),
    text(4, 20, 114, `DESC ${payload.productName || "--"}`),
    text(4, 392, 90, `INNER ${middlePackageQuantity}`),
    text(4, 392, 118, `COUNT ${payload.productCount}`),
  ];
  addBarcode(lines, "128", 24, 150, barcodeValue, 44, 1);
  lines.push("PRINT");
  return command(lines);
}
