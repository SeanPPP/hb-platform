import assert from "node:assert/strict";

import { createExpoCameraResultAdapter } from "./expo-camera-adapter";
import { HidScannerRouter } from "./hid-scanner";

let now = 1_000;
const events: { value: string; context: string; source: string; category: string }[] = [];
const router = new HidScannerRouter({
  idleMs: 80,
  maxLength: 32,
  now: () => now,
});
router.subscribeRouted((event) => events.push(event));
router.setCaptureActive(true);

router.setContext("product");
for (const character of "1234567890123") {
  router.acceptHidText(character);
}
router.acceptHidText("\r\n");
assert.deepEqual(scanSummary(events.at(-1)), {
  value: "1234567890123",
  context: "product",
  source: "hid",
  category: "product-code",
});

for (let index = 0; index < 500; index += 1) {
  router.acceptHidText(`SKU${index.toString().padStart(4, "0")}\n`);
}
assert.equal(events.length, 501, "500 次连续 HID 扫码不应串码或丢失");
assert.equal(events.at(-1)?.value, "SKU0499");

router.setContext("cashier-login");
router.acceptHidText("CASHIER-01\r\n");
assert.equal(events.at(-1)?.category, "cashier-code");

router.setContext("supervisor-authorization");
router.acceptHidText("SUPERVISOR-01\n");
assert.equal(events.at(-1)?.category, "supervisor-code");

router.setContext("product-search");
router.acceptHidText("PART");
now += 81;
router.resetPartialIfIdle();
router.acceptHidText("NEW-CODE\n");
assert.equal(events.at(-1)?.value, "NEW-CODE", "超时后的半段扫码不得与后续扫码拼接");

router.setContext("product");
router.acceptHidText("OLD");
router.setCaptureActive(false);
assert.equal(router.getCaptureStatus(), "inactive");
router.setCaptureActive(true);
router.acceptHidText("NEW\n");
assert.equal(events.at(-1)?.value, "NEW", "焦点恢复后不得携带旧缓冲");

router.setContext("product");
router.acceptHidText("HALF");
router.pushContext("dialog");
router.acceptHidText("DIALOG-1\n");
assert.deepEqual(scanSummary(events.at(-1)), {
  value: "DIALOG-1",
  context: "dialog",
  source: "hid",
  category: "dialog-code",
});
router.popContext();
router.acceptHidText("PRODUCT-2\n");
assert.equal(events.at(-1)?.context, "product", "弹窗 pop 后应恢复原上下文");
assert.equal(events.at(-1)?.value, "PRODUCT-2");

const releaseProductRoute = router.acquireContext("product");
const releaseSupervisorDialog = router.acquireContext("supervisor-authorization");
releaseProductRoute();
router.acceptHidText("SUPERVISOR-LEASE\n");
assert.equal(
  events.at(-1)?.context,
  "supervisor-authorization",
  "路由卸载不得错误弹出仍显示中的主管授权弹窗上下文",
);
releaseSupervisorDialog();
router.acceptHidText("PRODUCT-AFTER-LEASE\n");
assert.equal(
  events.at(-1)?.context,
  "product",
  "主管授权弹窗关闭后必须恢复可扫码路由的上下文",
);

router.setCaptureContext("emergency-qr");
router.acceptHidText("EMERGENCY.SIGNED.PAYLOAD\n");
assert.equal(events.at(-1)?.category, "emergency-qr", "紧急 QR 必须路由为独立类别");

const camera = createExpoCameraResultAdapter(router);
void router.startCamera();
camera.onBarcodeScanned({ data: "CAMERA-ITEM" });
assert.deepEqual(scanSummary(events.at(-1)), {
  value: "CAMERA-ITEM",
  context: "emergency-qr",
  source: "camera",
  category: "emergency-qr",
});
void router.stopCamera();

router.setContext("product");
router.acceptHidText("A".repeat(33));
router.acceptHidText("GOOD\n");
assert.equal(events.at(-1)?.value, "GOOD", "超长缓冲必须丢弃，不能污染下一码");

// 扫码器未配置回车后缀：停止输入超过 idleMs 后应自动提交（等效自动追加回车）。
router.setContext("product");
router.acceptHidText("AUTO");
now += 81;
assert.equal(router.flushPartialIfIdle(), true, "停顿超过 idleMs 应自动提交半段");
assert.equal(events.at(-1)?.value, "AUTO", "无回车扫码应在空闲时自动提交完整条码");
assert.equal(events.at(-1)?.source, "hid", "自动提交的来源仍为 hid");

// 失焦（setCaptureActive(false)）必须清空缓冲：残留半段在恢复前不得被自动提交。
router.setContext("product");
router.acceptHidText("RESIDUE");
router.setCaptureActive(false);
now += 81;
assert.equal(router.flushPartialIfIdle(), false, "失焦后缓冲已清空，不得自动提交残留");
router.setCaptureActive(true);

// 停顿未超过 idleMs 不得自动提交；超过后提交并清空缓冲，空缓冲不再提交。
router.acceptHidText("B");
now += 40;
assert.equal(router.flushPartialIfIdle(), false, "未超过 idleMs 不得自动提交");
now += 41;
assert.equal(router.flushPartialIfIdle(), true, "超过 idleMs 后应自动提交半段 B");
assert.equal(events.at(-1)?.value, "B", "自动提交的值为超时的半段");
assert.equal(router.flushPartialIfIdle(), false, "空缓冲不得自动提交");

console.log("hid-scanner.test.ts: ok");

function scanSummary(event: (typeof events)[number] | undefined) {
  if (!event) {
    return undefined;
  }
  return {
    value: event.value,
    context: event.context,
    source: event.source,
    category: event.category,
  };
}
