import {
  SettingsPresenter,
  type SettingsControlPort,
  type SettingsDangerousActionResult,
  type SettingsDangerousConfirmation,
} from "../../features/settings/settings-presenter";
import type { SettingsRuntimeFactory } from "../../features/settings/settings-runtime";

export type SettingsTrustedSession = Readonly<{
  storeCode: string;
  deviceCode: string;
  permissionCodes: readonly string[];
}>;

export interface SettingsTrustedSessionLease {
  get(): SettingsTrustedSession;
}

export type ProductionSettingsRuntimeDependencies = Readonly<{
  createSessionLease(): SettingsTrustedSessionLease;
  control: SettingsControlPort;
  /**
   * 必须与销售、支付、退款和分期的写路径共享同一独占门闩。
   * 组合根负责在 operation 内复核所有耐久队列并执行动作。
   */
  runDangerousExclusive<T>(operation: () => Promise<T>): Promise<T>;
}>;

/**
 * route 只能取得零参数 presenter 工厂。可信门店、设备和权限由 cashier lease
 * 冻结，并在每次异步边界前后复核，旧页面无法在换班后继续操作设置或硬件。
 */
export function createProductionSettingsRuntime(
  input: ProductionSettingsRuntimeDependencies,
): SettingsRuntimeFactory {
  return Object.freeze({
    createPresenter: () => {
      const lease = input.createSessionLease();
      const identity = normalizedIdentity(lease.get());
      const port = securedSettingsPort(input, lease, identity);
      return new SettingsPresenter({
        permissions: identity.permissionCodes,
        port,
      });
    },
  });
}

function securedSettingsPort(
  input: ProductionSettingsRuntimeDependencies,
  lease: SettingsTrustedSessionLease,
  identity: SettingsTrustedSession,
): SettingsControlPort {
  const run = async <T>(
    operation: () => Promise<T>,
  ): Promise<T> => {
    assertSameSession(lease.get(), identity);
    const result = await operation();
    assertSameSession(lease.get(), identity);
    return result;
  };

  const runDangerous = async (
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
  ): Promise<SettingsDangerousActionResult> => {
    assertSameSession(lease.get(), identity);
    return input.runDangerousExclusive(async () => {
      const result =
        await input.control.executeDangerousAction(action, signal);
      assertSameSession(lease.get(), identity);
      return result;
    });
  };

  const secured: SettingsControlPort = {
    loadSnapshot: (signal) =>
      run(async () => {
        const snapshot = await input.control.loadSnapshot(signal);
        if (
          snapshot.device.storeCode !== identity.storeCode ||
          snapshot.device.deviceCode !== identity.deviceCode
        ) {
          throw new Error("SETTINGS_DEVICE_SCOPE_MISMATCH");
        }
        return snapshot;
      }),
    downloadCatalog: (signal) =>
      run(() => input.control.downloadCatalog(signal)),
    testApiAddress: (apiBaseUrl, signal) =>
      run(() => input.control.testApiAddress(apiBaseUrl, signal)),
    testPaymentProvider: (provider, settings, signal) =>
      run(() =>
        input.control.testPaymentProvider(
          provider,
          settings,
          signal,
        ),
      ),
    savePrinterSettings: (settings, signal) =>
      run(() => input.control.savePrinterSettings(settings, signal)),
    scanPrinters: (signal) =>
      run(() => input.control.scanPrinters(signal)),
    connectPrinter: (peripheralId, signal) =>
      run(() => input.control.connectPrinter(peripheralId, signal)),
    testPrinter: (signal) =>
      run(() => input.control.testPrinter(signal)),
    testScanner: (signal) =>
      run(() => input.control.testScanner(signal)),
    setExternalDisplayEnabled: (enabled, signal) =>
      run(() =>
        input.control.setExternalDisplayEnabled(enabled, signal),
      ),
    testExternalDisplay: (signal) =>
      run(() => input.control.testExternalDisplay(signal)),
    checkForAppUpdate: (signal) =>
      run(() => input.control.checkForAppUpdate(signal)),
    executeDangerousAction: runDangerous,
  };
  return Object.freeze(secured);
}

function normalizedIdentity(
  session: SettingsTrustedSession,
): SettingsTrustedSession {
  const storeCode = requiredText(session.storeCode);
  const deviceCode = requiredText(session.deviceCode);
  const permissionCodes = Object.freeze(
    session.permissionCodes.map(requiredText),
  );
  return Object.freeze({ storeCode, deviceCode, permissionCodes });
}

function assertSameSession(
  observed: SettingsTrustedSession,
  expected: SettingsTrustedSession,
): void {
  if (
    requiredText(observed.storeCode) !== expected.storeCode ||
    requiredText(observed.deviceCode) !== expected.deviceCode
  ) {
    throw new Error("SETTINGS_CASHIER_SESSION_REPLACED");
  }
}

function requiredText(value: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error("SETTINGS_TRUSTED_IDENTITY_INVALID");
  return normalized;
}
