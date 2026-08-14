import assert from "node:assert/strict";
import test from "node:test";

import type { ReceiptPrinterSettings } from "../db/pos-settings-repository";

import { resolveTrustedReceiptPrinterSettings } from "./trusted-receipt-settings";

const settings: ReceiptPrinterSettings = {
  printEnabled: true,
  drawerEnabled: false,
  peripheralId: "printer-1",
  paper: "80mm",
  locale: "en",
  brandName: "Hot Bargain",
  storeName: "Saved Store",
  address: "1 Queen St",
  phone: "0712345678",
  abn: "12 345 678 901",
};

test("可信设备分店名称仅在门店编码完全匹配时覆盖保存设置", async () => {
  const matched = await resolveTrustedReceiptPrinterSettings(
    settings,
    "1042",
    async () => ({
      deviceCode: "POS_1042_1155",
      storeCode: "1042",
      storeName: "Redbank Plaza",
      terminalName: "",
    }),
  );
  const mismatched = await resolveTrustedReceiptPrinterSettings(
    settings,
    "1042",
    async () => ({
      deviceCode: "POS_9999_0001",
      storeCode: "9999",
      storeName: "Another Store",
      terminalName: "",
    }),
  );

  assert.equal(matched.storeName, "Redbank Plaza");
  assert.equal(mismatched.storeName, "Saved Store");
  assert.equal(matched.address, settings.address);
  assert.equal(matched.peripheralId, settings.peripheralId);
});

test("可信名称缺失或读取失败时依次回退保存名称和门店编码", async () => {
  const saved = await resolveTrustedReceiptPrinterSettings(
    settings,
    "1042",
    async () => ({
      deviceCode: "POS_1042_1155",
      storeCode: "1042",
      storeName: "   ",
      terminalName: "",
    }),
  );
  const code = await resolveTrustedReceiptPrinterSettings(
    { ...settings, storeName: "" },
    "1042",
    async () => {
      throw new Error("keychain unavailable");
    },
  );

  assert.equal(saved.storeName, "Saved Store");
  assert.equal(code.storeName, "1042");
});

test("包含控制字符的设备展示名称不能进入小票设置", async () => {
  const resolved = await resolveTrustedReceiptPrinterSettings(
    settings,
    "1042",
    async () => ({
      deviceCode: "POS_1042_1155",
      storeCode: "1042",
      storeName: "Unsafe\u001b@Store",
      terminalName: "",
    }),
  );

  assert.equal(resolved.storeName, "Saved Store");
});
