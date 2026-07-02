import assert from "node:assert/strict";
import {
  buildReceiptPrinterTestCommand,
  formatReceiptPrinterTestTime,
} from "./receipt";

const fixedTime = new Date(2026, 5, 22, 9, 8, 7);
const command = buildReceiptPrinterTestCommand(fixedTime);

assert.equal(formatReceiptPrinterTestTime(fixedTime), "2026-06-22 09:08:07", "小票测试时间按本地时间补零");
assert.equal(command.includes("\x00"), false, "测试小票命令不能包含 NUL，避免蓝牙/打印链路截断");
assert.equal(command.startsWith("\x1B@"), false, "测试小票不再用 ESC @ 开头，避免打印机把 @ 打成正文");
assert.ok(command.includes("\x1Ba1HotBargain"), "品牌标题必须用无 NUL 的居中命令打印");
assert.ok(command.includes("\x1Ba0Order: 11111111-2222-3333-4444-555555555555"), "订单信息必须用无 NUL 的左对齐命令打印");
assert.ok(command.includes("===== TAX INVOICE ====="), "测试小票必须参考 WPF tax invoice 标题");
assert.ok(command.includes("*** Paid ***"), "测试小票必须包含 WPF paid 状态");
const paidIndex = command.indexOf("*** Paid ***");
assert.ok(paidIndex >= 0, "测试小票必须包含 Paid 标记");
assert.ok(command.indexOf("Order:", paidIndex) > paidIndex, "Paid 后必须继续包含订单信息，避免在对齐命令处截断");
assert.ok(command.indexOf("商品列表", paidIndex) > paidIndex, "Paid 后必须继续包含商品列表，避免只打印抬头");
assert.ok(command.indexOf("支付方式", paidIndex) > paidIndex, "Paid 后必须继续包含支付方式，避免只打印抬头");
assert.ok(command.indexOf("Print Time: 2026-06-22 09:08:07", paidIndex) > paidIndex, "Paid 后必须继续包含打印时间");
assert.ok(command.includes("ITEM"), "商品表头必须包含 ITEM");
assert.ok(command.includes("QTY"), "商品表头必须包含 QTY");
assert.ok(command.includes("PRICE"), "商品表头必须包含 PRICE");
assert.ok(command.includes("商品列表"), "测试小票必须明确打印商品列表标题");
assert.ok(command.includes("Organic Gala Apples"), "测试小票必须包含 WPF 样例商品");
assert.ok(command.includes("690101"), "测试小票必须包含商品 lookup code");
assert.ok(command.includes("Whole Grain Bread"), "测试小票必须包含第二个 WPF 样例商品");
assert.ok(command.includes("Subtotal"), "测试小票必须包含小计");
assert.ok(command.includes("GST"), "测试小票必须包含 GST");
assert.ok(command.includes("Total(inc GST)"), "测试小票必须包含含税总计");
assert.ok(command.includes("Payment:"), "测试小票必须包含付款区");
assert.ok(command.includes("支付方式"), "测试小票必须明确打印支付方式标题");
assert.ok(command.includes("Card"), "测试小票必须包含刷卡付款行");
assert.ok(command.includes("Cash"), "测试小票必须包含现金付款 demo 行");
assert.ok(command.includes("Voucher"), "测试小票必须包含购物券付款 demo 行");
assert.ok(command.includes("Refunds and returns"), "测试小票必须包含退换货 demo 段落");
assert.ok(command.includes("APPROVED 00"), "测试小票必须包含刷卡返回 demo 状态");
assert.ok(command.includes("TXN REF 260601120038"), "测试小票必须包含交易参考 demo 行");
assert.ok(command.includes("中文测试"), "测试小票必须包含中文编码 demo 行");
assert.ok(command.includes("Print Time: 2026-06-22 09:08:07"), "测试小票必须包含打印时间");
assert.equal(command.includes("Tendered"), false, "移动端测试小票不打印成功页 Tendered 行");
assert.equal(command.includes("Change"), false, "移动端测试小票不打印成功页 Change 行");
assert.ok(command.endsWith("\x1DVB\x03"), "测试小票末尾必须发送无 NUL 的切纸命令");

console.log("receipt.test.ts: ok");
