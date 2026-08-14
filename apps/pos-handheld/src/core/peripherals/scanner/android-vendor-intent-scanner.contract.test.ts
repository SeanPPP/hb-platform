import assert from "node:assert/strict";
import test from "node:test";

import {
  createAndroidVendorIntentScanner,
  type AndroidVendorIntentScannerAdapterPort,
} from "./android-vendor-intent-scanner";

test("未配置厂商 profile 时保持 disabled/no-op，零广播、零焦点、零额外权限", () => {
  let registrations = 0;
  let focusRequests = 0;
  let permissionRequests = 0;
  const adapter = {
    requiredPermissionsFor() {
      permissionRequests += 1;
      return ["com.vendor.permission.SCANNER"];
    },
    registerBroadcastReceiver() {
      registrations += 1;
      return () => {};
    },
    requestFocus() {
      focusRequests += 1;
    },
  } as AndroidVendorIntentScannerAdapterPort & {
    requestFocus(): void;
  };

  const scanner = createAndroidVendorIntentScanner({
    profile: null,
    adapter,
    onBarcode() {
      throw new Error("disabled scanner must not emit");
    },
  });

  assert.equal(scanner.status, "disabled");
  assert.deepEqual(scanner.requiredPermissions, []);
  const stop = scanner.start();
  stop();
  assert.equal(registrations, 0);
  assert.equal(focusRequests, 0);
  assert.equal(permissionRequests, 0);
});

test("仅配置 profile 但没有原生 adapter 时仍失败关闭，不伪装为可用 scanner", () => {
  const scanner = createAndroidVendorIntentScanner({
    profile: {
      id: "future-vendor",
      broadcastAction: "com.vendor.SCAN",
      barcodeExtraKey: "barcode",
    },
    onBarcode() {},
  });

  assert.equal(scanner.status, "disabled");
  assert.deepEqual(scanner.requiredPermissions, []);
  scanner.start()();
});
