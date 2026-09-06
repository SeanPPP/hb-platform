import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

const mobileRoot = path.resolve(__dirname, "../../..");

function read(relativePath: string) {
  return readFileSync(path.join(mobileRoot, relativePath), "utf8");
}

test("设置首页采用设备、偏好、支持三组高密度信息架构", () => {
  const source = read("app/(shell)/settings.tsx");

  assert.match(source, /HB_COLORS/);
  assert.match(source, /HB_SPACING/);
  assert.match(source, /HB_RADIUS/);
  assert.match(source, /t\("groups\.devices"\)/);
  assert.match(source, /t\("groups\.preferences"\)/);
  assert.match(source, /t\("groups\.support"\)/);
  assert.match(source, /<StatusPill/);
  assert.match(source, /appPackageVersion/);
  assert.match(source, /deviceStoreDisplayName/);
  assert.match(source, /savedPrinter/);
  assert.match(source, /savedReceiptPrinter/);
  assert.doesNotMatch(source, /TC26-FV-01|Zebra ZQ320/);
});

test("诊断、设备与双打印机详情入口可操作且共享原生打印忙碌锁", () => {
  const source = read("app/(shell)/settings.tsx");
  const settingsStart = source.indexOf("export default function Settings");
  const renderStart = source.indexOf("  return (", settingsStart);
  const renderSource = source.slice(renderStart);

  assert.match(source, /testID="settings-diagnostics"/);
  assert.match(source, /setDiagnosticsVisible\(true\)/);
  assert.match(source, /visible=\{diagnosticsVisible\}/);
  assert.match(source, /testID="settings-device-details"/);
  assert.match(source, /visible=\{deviceSettingsVisible\}/);
  assert.match(source, /testID="settings-printer-details"/);
  assert.match(source, /visible=\{printerSettingsVisible\}/);
  assert.match(source, /printerNativeBusy\s*=\s*printerBusy\s*\|\|\s*receiptPrinterBusy/);
  assert.match(source, /onPress=\{handleScanPrinters\}[\s\S]{0,220}disabled=\{printerNativeBusy\}/);
  assert.match(source, /onPress=\{handleConnectSavedPrinter\}[\s\S]{0,260}disabled=\{printerNativeBusy\}/);
  assert.match(source, /onPress=\{handleScanReceiptPrinters\}[\s\S]{0,220}disabled=\{printerNativeBusy\}/);
  assert.match(source, /onPress=\{handleTestReceiptPrinter\}[\s\S]{0,260}disabled=\{printerNativeBusy \|\| !savedReceiptPrinter\}/);

  for (const handler of [
    "handleCheckUpdates",
    "openApiHostSettings",
    "openDeviceActivation",
    "handleRefreshDevice",
    "handleDeviceUnbind",
    "handleScanPrinters",
    "handleConnectPrinter",
    "handleTestPrinter",
    "handleClearPrinter",
    "handleScanReceiptPrinters",
    "handleSaveReceiptPrinter",
    "handleTestReceiptPrinter",
    "handleClearReceiptPrinter",
    "handleLogout",
  ]) {
    assert.match(renderSource, new RegExp(handler), `${handler} 必须仍有可点击入口`);
  }
});

test("详情弹窗使用原生可访问模态并管理进入与返回焦点", () => {
  const source = read("app/(shell)/settings.tsx");

  assert.match(source, /Modal as NativeModal/);
  assert.match(source, /<NativeModal/);
  assert.match(source, /accessibilityViewIsModal/);
  assert.match(source, /onRequestClose=\{onDismiss\}/);
  assert.match(source, /AccessibilityInfo\.setAccessibilityFocus\(headingHandle\)/);
  assert.match(source, /AccessibilityInfo\.setAccessibilityFocus\(triggerHandle\)/);
  assert.match(source, /accessibilityLabel=\{dismissLabel\}/);
  assert.doesNotMatch(source, /\n\s+Modal,\n|\n\s+Portal,\n/);
  assert.match(source, /<Pressable\s+ref=\{actionRef\}[\s\S]{0,260}accessibilityRole="button"/);
  assert.match(source, /styles\.compactRowPressed/);
});

test("中英文设置文案同时提供新分组和诊断入口", () => {
  const zh = JSON.parse(read("src/locales/zh/screens/settings.json"));
  const en = JSON.parse(read("src/locales/en/screens/settings.json"));

  for (const locale of [zh, en]) {
    assert.equal(typeof locale.groups.devices, "string");
    assert.equal(typeof locale.groups.preferences, "string");
    assert.equal(typeof locale.groups.support, "string");
    assert.equal(typeof locale.overview.diagnostics, "string");
    assert.equal(typeof locale.overview.aboutDiagnostics, "string");
    assert.equal(typeof locale.diagnostics.title, "string");
  }
});

test("设置页手动检查复用受控 OTA 唯一发布通道", () => {
  const settings = read("app/(shell)/settings.tsx");
  const layout = read("app/_layout.tsx");
  const boundary = read("src/modules/updates/MobileOtaUpdateBoundary.tsx");
  const hook = read("src/modules/updates/use-mobile-ota-update.ts");

  assert.match(settings, /useMobileOtaManualCheck/);
  assert.match(settings, /await checkMobileOtaUpdate\(\)/);
  assert.doesNotMatch(settings, /checkAndDownloadAppUpdate/);
  assert.match(layout, /onManualCheck=\{mobileOtaUpdate\.checkManually\}/);
  assert.match(boundary, /MobileOtaManualCheckContext\.Provider/);
  assert.match(hook, /optionalPromptTargetRef\.current = null;[\s\S]{0,500}await runCheckRef\.current\(\)/);
});
