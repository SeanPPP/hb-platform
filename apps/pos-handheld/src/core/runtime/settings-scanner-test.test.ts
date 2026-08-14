import assert from "node:assert/strict";
import test from "node:test";

import { HidScannerRouter } from "../peripherals/scanner/hid-scanner";

import { SettingsScannerTestCoordinator } from "./settings-scanner-test";

test("只接收 dialog 上下文的下一次完整 HID 扫码并立即释放监听", async () => {
  const scanner = new HidScannerRouter();
  scanner.setCaptureActive(true);
  const subject = new SettingsScannerTestCoordinator(scanner, {
    timeoutMs: 1_000,
  });
  const result = subject.test(new AbortController().signal);

  scanner.setCaptureContext("product");
  scanner.acceptHidText("IGNORED\n");
  scanner.setCaptureContext("dialog");
  scanner.acceptHidText("SETTINGS-001\n");

  assert.deepEqual(await result, {
    source: "hid",
    value: "SETTINGS-001",
  });
  assert.equal(subject.hasPendingTest(), false);
});

test("Abort 和并发测试都会确定性释放，且相机结果走同一入口", async () => {
  const scanner = new HidScannerRouter();
  scanner.setCaptureActive(true);
  const subject = new SettingsScannerTestCoordinator(scanner, {
    timeoutMs: 1_000,
  });
  const abort = new AbortController();
  const pending = subject.test(abort.signal);

  await assert.rejects(
    () => subject.test(new AbortController().signal),
    /progress/i,
  );
  abort.abort();
  await assert.rejects(() => pending, /abort/i);
  assert.equal(subject.hasPendingTest(), false);

  const camera = subject.test(new AbortController().signal);
  scanner.setCaptureContext("dialog");
  await scanner.startCamera();
  scanner.acceptCameraText("CAM-1");
  assert.deepEqual(await camera, {
    source: "camera",
    value: "CAM-1",
  });
});
