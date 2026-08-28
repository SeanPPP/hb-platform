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

const privateActivationEventCount = events.length;
router.setCaptureContext("device-activation");
assert.equal(
  router.acceptHidText("HBDEV1-PRIVATE-CODE\n").emitted,
  true,
  "设备开通专用上下文仍须接收 HID 原文",
);
assert.equal(
  events.length,
  privateActivationEventCount,
  "设备开通码不得进入共享 routed 扫码总线",
);

const camera = createExpoCameraResultAdapter(router);
void router.startCamera();
assert.equal(
  camera.onBarcodeScanned({ data: "HBDEV1-CAMERA-PRIVATE" }),
  true,
  "设备开通专用上下文仍须接收相机原文",
);
assert.equal(
  events.length,
  privateActivationEventCount,
  "设备开通码相机结果也不得进入共享 routed 扫码总线",
);
void router.stopCamera();

router.setContext("product");
const quarantinedActivationEventCount = events.length;
assert.equal(
  router.acceptHidText(" h b d e v 1 -DO-NOT-SELL\n").emitted,
  false,
  "商品上下文的 HBDEV1 前缀必须被 HID 隔离",
);
assert.equal(
  router.acceptHidText("HBDEV1-ſECRET\n").emitted,
  false,
  "前缀已经确认后，后续 Unicode 也不得使开通码逃逸到商品总线",
);
void router.startCamera();
assert.equal(
  camera.onBarcodeScanned({ data: "\thb dev1 -do-not-sell\r\n" }),
  false,
  "商品上下文的 HBDEV1 前缀必须被相机隔离",
);
void router.stopCamera();
assert.equal(
  events.length,
  quarantinedActivationEventCount,
  "隔离的开通码不得进入任何商品扫码订阅",
);

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
