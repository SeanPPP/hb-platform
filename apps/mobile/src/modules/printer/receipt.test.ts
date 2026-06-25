import assert from "node:assert/strict";
import {
  buildReceiptPrinterTestCommand,
  formatReceiptPrinterTestTime,
} from "./receipt";

const fixedTime = new Date(2026, 5, 22, 9, 8, 7);
const command = buildReceiptPrinterTestCommand(fixedTime);

assert.equal(formatReceiptPrinterTestTime(fixedTime), "2026-06-22 09:08:07", "小票测试时间按本地时间补零");
assert.ok(command.startsWith("\x1B@"), "测试小票必须先初始化打印机");
assert.ok(command.includes("\x1Ba\x01HBWEB RECEIPT PRINTER"), "标题必须居中打印");
assert.ok(command.includes("\x1Ba\x00Connection OK"), "正文必须恢复左对齐");
assert.ok(command.includes("Print Time: 2026-06-22 09:08:07"), "测试小票必须包含打印时间");
assert.equal(command.includes("\x1DV"), false, "测试小票不发送切纸命令");

console.log("receipt.test.ts: ok");
