import assert from "node:assert/strict";
import test from "node:test";

import {
  SETTINGS_APP_UPDATE_PERMISSION,
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_CATALOG_RESET_PERMISSION,
  SETTINGS_DEVICE_REGISTRATION_PERMISSION,
  SETTINGS_PAYMENT_TERMINAL_PERMISSION,
  SETTINGS_RECEIPT_PRINTER_PERMISSION,
  SETTINGS_VIEW_PERMISSION,
  resolveSettingsAccess,
} from "./settings-authorization";

test("Settings 使用 WPF 已有的精确权限码并忽略空白", () => {
  const access = resolveSettingsAccess([
    ` ${SETTINGS_VIEW_PERMISSION} `,
    SETTINGS_PAYMENT_TERMINAL_PERMISSION,
    SETTINGS_RECEIPT_PRINTER_PERMISSION,
    SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
    SETTINGS_CATALOG_RESET_PERMISSION,
    SETTINGS_DEVICE_REGISTRATION_PERMISSION,
    SETTINGS_APP_UPDATE_PERMISSION,
  ]);

  assert.deepEqual(access, {
    canView: true,
    canConfigurePayments: true,
    canConfigurePrinter: true,
    canDownloadCatalog: true,
    canResetCatalog: true,
    canReregisterDevice: true,
    canManageAppUpdate: true,
    canTestScanner: true,
  });
});
test("View 不会隐式扩大任何写权限，仅允许无写入的扫描器测试", () => {
  assert.deepEqual(resolveSettingsAccess([SETTINGS_VIEW_PERMISSION]), {
    canView: true,
    canConfigurePayments: false,
    canConfigurePrinter: false,
    canDownloadCatalog: false,
    canResetCatalog: false,
    canReregisterDevice: false,
    canManageAppUpdate: false,
    canTestScanner: true,
  });
});
