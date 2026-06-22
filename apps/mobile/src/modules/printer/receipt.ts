const ESC = "\x1B";

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

export function buildReceiptPrinterTestCommand(now = new Date()) {
  // 只发送初始化、对齐和走纸指令，不切纸，避免不同型号小票机切刀能力不一致。
  return [
    `${ESC}@`,
    `${ESC}a\x01`,
    "HBWEB RECEIPT PRINTER\r\n",
    `${ESC}a\x00`,
    "Connection OK\r\n",
    `Print Time: ${formatReceiptPrinterTestTime(now)}\r\n`,
    "------------------------------\r\n",
    "This is a test receipt.\r\n",
    "\r\n\r\n\r\n",
  ].join("");
}
