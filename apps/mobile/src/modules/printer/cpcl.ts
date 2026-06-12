import type { PreparedBarcode, PrinterBarcodeKind, ProductLabelPrintPayload } from "@/modules/printer/types";

function cleanText(value?: string | null) {
  return (value ?? "").replace(/\r?\n/g, " ").trim();
}

function limitText(value: string, maxLength: number) {
  return value.length > maxLength ? value.slice(0, maxLength) : value;
}

function splitLines(value: string, maxLength: number, maxLines: number) {
  const clean = cleanText(value);
  if (!clean) {
    return Array.from({ length: maxLines }, () => "");
  }

  const lines: string[] = [];
  let remaining = clean;
  while (remaining && lines.length < maxLines) {
    lines.push(remaining.slice(0, maxLength));
    remaining = remaining.slice(maxLength).trimStart();
  }

  while (lines.length < maxLines) {
    lines.push("");
  }

  return lines;
}

function formatMoney(value?: number | null) {
  if (value == null || Number.isNaN(value)) {
    return "";
  }
  return value.toFixed(2);
}

function onlyDigits(value: string) {
  return /^\d+$/.test(value);
}

function computeEan13CheckDigit(value12: string) {
  const digits = value12.split("").map(Number);
  const total = digits.reduce((sum, digit, index) => {
    return sum + digit * (index % 2 === 0 ? 1 : 3);
  }, 0);

  return (10 - (total % 10)) % 10;
}

export function prepareBarcode(rawValue?: string | null): PreparedBarcode | null {
  const value = cleanText(rawValue);
  if (!value) {
    return null;
  }

  if (onlyDigits(value) && value.length === 12) {
    const checkDigit = computeEan13CheckDigit(value);
    return {
      kind: "EAN13",
      value: `${value}${checkDigit}`,
    };
  }

  if (onlyDigits(value) && value.length === 13) {
    const expected = computeEan13CheckDigit(value.slice(0, 12));
    if (expected === Number(value[12])) {
      return {
        kind: "EAN13",
        value,
      };
    }
  }

  return {
    kind: "CODE128",
    value,
  };
}

function barcodeCommand(x: number, y: number, barcode: PreparedBarcode, height = 60) {
  const barcodeType = barcode.kind === "EAN13" ? "EAN13" : "128";
  return `BARCODE ${barcodeType} 1 1 ${height} ${x} ${y} ${barcode.value}`;
}

function barcodeCaptionY(kind: PrinterBarcodeKind) {
  return kind === "EAN13" ? 280 : 290;
}

function todayString() {
  return new Date().toISOString().slice(0, 10);
}

function finalizeCommands(commands: string[]) {
  return `${commands.filter(Boolean).join("\r\n")}\r\nPRINT\r\n`;
}

export function buildDiscountLabelCpcl(payload: ProductLabelPrintPayload) {
  const [nameLine1, nameLine2] = splitLines(payload.productName, 24, 2);
  const barcode = prepareBarcode(payload.barcode);
  const retailPrice = payload.retailPrice ?? 0;
  const discountRate = payload.discountRate ?? 0;
  const saveAmount = retailPrice * discountRate;
  const nowPrice = retailPrice - saveAmount;
  const commands = [
    "! 0 200 200 400 1",
    "PAGE-WIDTH 570",
    `TEXT 7 0 18 16 ${limitText(nameLine1, 24)}`,
    nameLine2 ? `TEXT 7 0 18 50 ${limitText(nameLine2, 24)}` : "",
    `TEXT 7 0 360 20 ${(discountRate * 100).toFixed(0)}%`,
    "TEXT 7 0 470 54 OFF",
    `TEXT 4 0 300 122 SAVE:$${formatMoney(saveAmount)}`,
    `TEXT 4 0 300 156 NOW:$${formatMoney(nowPrice)}`,
    payload.itemNumber ? `TEXT 4 0 18 118 ITEM:${limitText(cleanText(payload.itemNumber), 20)}` : "",
    barcode ? barcodeCommand(18, 186, barcode, 62) : "",
    barcode ? `TEXT 4 0 18 ${barcodeCaptionY(barcode.kind) + 8} ${barcode.value}` : "",
    `TEXT 4 0 390 340 ${todayString()}`,
  ];

  return finalizeCommands(commands);
}

export function buildClearanceLabelCpcl(payload: ProductLabelPrintPayload) {
  const [nameLine1, nameLine2] = splitLines(payload.productName, 22, 2);
  const barcode = prepareBarcode(payload.clearanceBarcode || payload.barcode);
  const retailPrice = payload.retailPrice ?? 0;
  const discountRate = payload.discountRate ?? 0;
  const fallbackClearance = retailPrice > 0 && discountRate > 0 ? retailPrice * (1 - discountRate) : null;
  const clearancePrice = payload.clearancePrice ?? fallbackClearance;
  const commands = [
    "! 0 200 200 400 1",
    "PAGE-WIDTH 570",
    `TEXT 7 0 18 16 ${limitText(nameLine1, 22)}`,
    nameLine2 ? `TEXT 7 0 18 50 ${limitText(nameLine2, 22)}` : "",
    "TEXT 7 0 360 18 CLEARANCE",
    payload.itemNumber ? `TEXT 4 0 18 96 ITEM:${limitText(cleanText(payload.itemNumber), 20)}` : "",
    payload.supplierName ? `TEXT 4 0 18 124 SUP:${limitText(cleanText(payload.supplierName), 18)}` : "",
    barcode ? barcodeCommand(18, 170, barcode, 58) : "",
    barcode ? `TEXT 4 0 18 ${barcodeCaptionY(barcode.kind)} ${barcode.value}` : "",
    `TEXT 4 0 320 122 WAS:$${formatMoney(retailPrice)}`,
    clearancePrice != null ? `TEXT 7 0 320 164 NOW:$${formatMoney(clearancePrice)}` : "",
    `TEXT 4 0 390 340 ${todayString()}`,
  ];

  return finalizeCommands(commands);
}
