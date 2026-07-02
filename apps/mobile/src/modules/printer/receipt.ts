const ESC = "\x1B";
const GS = "\x1D";
const RECEIPT_LINE_WIDTH = 42;
const ITEM_WIDTH = 25;
const QTY_WIDTH = 5;
const PRICE_WIDTH = 12;
const FULL_CUT_COMMAND = `${GS}VB\x03`;

function pad(value: number) {
  return value.toString().padStart(2, "0");
}

export function formatReceiptPrinterTestTime(now: Date) {
  const year = now.getFullYear();
  const month = pad(now.getMonth() + 1);
  const day = pad(now.getDate());
  const hour = pad(now.getHours());
  const minute = pad(now.getMinutes());
  const second = pad(now.getSeconds());

  return `${year}-${month}-${day} ${hour}:${minute}:${second}`;
}

function trimTo(value: string, maxChars: number) {
  if (!value || value.length <= maxChars) {
    return value;
  }

  return maxChars <= 3 ? value.slice(0, maxChars) : `${value.slice(0, maxChars - 3)}...`;
}

function fitColumns(left: string, middle: string, right: string) {
  return (
    trimTo(left, ITEM_WIDTH).padEnd(ITEM_WIDTH) +
    trimTo(middle, QTY_WIDTH).padStart(QTY_WIDTH) +
    trimTo(right, PRICE_WIDTH).padStart(PRICE_WIDTH)
  );
}

function fitTwoColumns(left: string, right: string) {
  const safeLeft = trimTo(left, 24);
  const safeRight = trimTo(right, 16);
  return safeLeft + " ".repeat(Math.max(1, RECEIPT_LINE_WIDTH - safeLeft.length - safeRight.length)) + safeRight;
}

export function buildReceiptPrinterTestCommand(now = new Date()) {
  const separator = "-".repeat(RECEIPT_LINE_WIDTH);

  // 按 WPF ReceiptTextFormatter 的 42 列格式生成固定样张，便于测试列宽、金额和刷卡摘要。
  return [
    `${ESC}a1`,
    "HotBargain\r\n",
    "Main Store\r\n",
    "Tel: 07 3000 0000\r\n",
    "ABN: 12 345 678 901\r\n",
    "\r\n",
    "===== TAX INVOICE =====\r\n",
    "\r\n",
    "*** Paid ***\r\n",
    "\r\n",
    `${ESC}a0`,
    "Order: 11111111-2222-3333-4444-555555555555\r\n",
    "Date: 2026-05-27 09:00:00\r\n",
    "Cashier: Alice\r\n",
    "Store: S001\r\n",
    "Device: POS-01\r\n",
    `${separator}\r\n`,
    "商品列表 / Items\r\n",
    `${fitColumns("ITEM", "QTY", "PRICE")}\r\n`,
    `${separator}\r\n`,
    "Organic Gala Apples\r\n",
    `${fitColumns("690101", "2", "$5.00")}\r\n`,
    "Whole Grain Bread\r\n",
    `${fitColumns("690102", "1", "$4.00")}\r\n`,
    `${fitTwoColumns("Dis", "-$0.20")}\r\n`,
    "Imported Chocolate Gift Box\r\n",
    `${fitColumns("690103", "1", "$12.80")}\r\n`,
    "中文商品测试 芒果干\r\n",
    `${fitColumns("690104", "3", "$9.90")}\r\n`,
    `${separator}\r\n`,
    `${fitTwoColumns("Subtotal", "$31.90")}\r\n`,
    `${fitTwoColumns("Discount", "-$1.20")}\r\n`,
    `${fitTwoColumns("GST", "$2.79")}\r\n`,
    `${fitTwoColumns("Total(inc GST)", "$30.70")}\r\n`,
    `${separator}\r\n`,
    "支付方式 / Payment\r\n",
    "Payment:\r\n",
    `${fitTwoColumns("Card", "$20.00")}\r\n`,
    `${fitTwoColumns("Cash", "$5.00")}\r\n`,
    `${fitTwoColumns("Voucher", "$5.70")}\r\n`,
    "  ANZ:123\r\n",
    "  VISA ****1111\r\n",
    "  APPROVED 00\r\n",
    "  TXN REF 260601120038\r\n",
    "  APPROVED CARD RECEIPT\r\n",
    `${separator}\r\n`,
    "Refunds and returns\r\n",
    "Keep receipt for refunds within 7 days.\r\n",
    "中文测试: 打印编码 / 对齐 / 切纸\r\n",
    `${separator}\r\n`,
    `Print Time: ${formatReceiptPrinterTestTime(now)}\r\n`,
    "Device: POS-01\r\n",
    "\r\n",
    `${ESC}a1`,
    "Thank you for your purchase!\r\n",
    `${ESC}a0`,
    "\r\n\r\n\r\n",
    // 用户测试场景需要自动切纸；避开 NUL 字节，防止蓝牙链路截断后续内容。
    FULL_CUT_COMMAND,
  ].join("");
}
