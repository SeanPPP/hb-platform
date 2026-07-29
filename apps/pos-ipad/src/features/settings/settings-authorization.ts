export const SETTINGS_VIEW_PERMISSION =
  "Permissions.PosTerminal.Settings.View";
export const SETTINGS_PAYMENT_TERMINAL_PERMISSION =
  "Permissions.PosTerminal.Settings.PaymentTerminal";
export const SETTINGS_RECEIPT_PRINTER_PERMISSION =
  "Permissions.PosTerminal.Settings.ReceiptPrinter";
export const SETTINGS_CATALOG_DOWNLOAD_PERMISSION =
  "Permissions.PosTerminal.Settings.CatalogDownload";
export const SETTINGS_CATALOG_RESET_PERMISSION =
  "Permissions.PosTerminal.Settings.CatalogReset";
export const SETTINGS_DEVICE_REGISTRATION_PERMISSION =
  "Permissions.PosTerminal.Settings.DeviceRegistration";
export const SETTINGS_APP_UPDATE_PERMISSION =
  "Permissions.PosTerminal.Settings.AppUpdate";
export const SETTINGS_CUSTOMER_DISPLAY_PERMISSION =
  "Permissions.PosTerminal.CustomerDisplay.Manage";

export type SettingsAccess = Readonly<{
  canView: boolean;
  canConfigurePayments: boolean;
  canConfigurePrinter: boolean;
  canDownloadCatalog: boolean;
  canResetCatalog: boolean;
  canReregisterDevice: boolean;
  canManageAppUpdate: boolean;
  canManageCustomerDisplay: boolean;
  canTestScanner: boolean;
}>;

/**
 * Settings 沿用 WPF/后端已发布的细分权限，不把“能打开设置页”扩大成设备写权限。
 * 扫描器测试仅监听一次输入且不保存绑定，因此 View 权限即可执行。
 */
export function resolveSettingsAccess(
  permissions: readonly string[],
): SettingsAccess {
  const granted = new Set(
    permissions.map((permission) => permission.trim()),
  );
  const canView = granted.has(SETTINGS_VIEW_PERMISSION);
  return Object.freeze({
    canView,
    canConfigurePayments: granted.has(
      SETTINGS_PAYMENT_TERMINAL_PERMISSION,
    ),
    canConfigurePrinter: granted.has(
      SETTINGS_RECEIPT_PRINTER_PERMISSION,
    ),
    canDownloadCatalog: granted.has(
      SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
    ),
    canResetCatalog: granted.has(SETTINGS_CATALOG_RESET_PERMISSION),
    canReregisterDevice: granted.has(
      SETTINGS_DEVICE_REGISTRATION_PERMISSION,
    ),
    canManageAppUpdate: granted.has(SETTINGS_APP_UPDATE_PERMISSION),
    canManageCustomerDisplay: granted.has(
      SETTINGS_CUSTOMER_DISPLAY_PERMISSION,
    ),
    canTestScanner: canView,
  });
}
