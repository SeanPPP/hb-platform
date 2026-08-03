import {
  SettingsPresenter,
  type SettingsClearSavedPrinterResult,
  type SettingsControlPort,
  type SettingsDangerousActionResult,
  type SettingsDangerousConfirmation,
  type SettingsSquareSetupControlPort,
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

type LeaseAwareSettingsControlPort = SettingsControlPort &
  Readonly<{
    executeDangerousAction(
      action: SettingsDangerousConfirmation,
      signal: AbortSignal,
      assertActive?: () => void,
    ): Promise<SettingsDangerousActionResult>;
    clearSavedPrinter?:
      | ((
          signal: AbortSignal,
          assertActive?: () => void,
        ) => Promise<SettingsClearSavedPrinterResult>)
      | undefined;
  }>;

/**
 * route 只能取得零参数 presenter 工厂。可信门店、设备和权限由 cashier lease
 * 冻结。普通动作在异步边界前后复核；不可撤销动作只在提交点前复核，避免把
 * 已完成的硬件脉冲或持久化写入伪装成失败。
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

  const runIrreversibleMutation = <T>(
    operation: () => Promise<T>,
  ): Promise<T> => {
    assertSameSession(lease.get(), identity);
    // 外部 POST 成功后不可因换班改写为失败，否则新幂等键重试会重复创建。
    return operation();
  };

  const leaseAwareControl = input.control as LeaseAwareSettingsControlPort;

  const runDangerous = async (
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
  ): Promise<SettingsDangerousActionResult> => {
    assertSameSession(lease.get(), identity);
    const execute = async () => {
      const result =
        await leaseAwareControl.executeDangerousAction(
          action,
          signal,
          () => assertSameSession(lease.get(), identity),
        );
      assertSameSession(lease.get(), identity);
      return result;
    };
    // restart/payment 的最终互斥由 transition 按目录→购物车顺序取得；
    // 若这里先持有普通购物车 lease，transition 会等待当前动作自身而永久自锁。
    return action.kind === "restart-app" ||
      action.kind === "change-payment-settings"
      ? execute()
      : input.runDangerousExclusive(execute);
  };

  const squareSetup = input.control.squareSetup;

  const secured: SettingsControlPort = {
    ...(squareSetup
      ? {
          squareSetup: Object.freeze({
            getSquareTokenStatus: (environment, signal) =>
              run(() =>
                squareSetup.getSquareTokenStatus(environment, signal),
              ),
            listSquareLocations: (environment, signal) =>
              run(() =>
                squareSetup.listSquareLocations(environment, signal),
              ),
            listSquareDevices: (environment, locationId, signal) =>
              run(() =>
                squareSetup.listSquareDevices(
                  environment,
                  locationId,
                  signal,
                ),
              ),
            listSquareDeviceCodes: (environment, locationId, signal) =>
              run(() =>
                squareSetup.listSquareDeviceCodes(
                  environment,
                  locationId,
                  signal,
                ),
              ),
            createSquareDeviceCode: (
              environment,
              locationId,
              name,
              signal,
            ) =>
              runIrreversibleMutation(() =>
                squareSetup.createSquareDeviceCode(
                  environment,
                  locationId,
                  name,
                  signal,
                ),
              ),
            getSquareDeviceCode: (environment, deviceCodeId, signal) =>
              run(() =>
                squareSetup.getSquareDeviceCode(
                  environment,
                  deviceCodeId,
                  signal,
                ),
              ),
          } satisfies SettingsSquareSetupControlPort),
        }
      : {}),
    getCatalogRefreshState: () => {
      assertSameSession(lease.get(), identity);
      return input.control.getCatalogRefreshState();
    },
    subscribeCatalogRefresh: (listener) =>
      input.control.subscribeCatalogRefresh(() => {
        assertSameSession(lease.get(), identity);
        listener();
      }),
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
    ...(input.control.testCashDrawer
      ? {
          testCashDrawer: async (signal: AbortSignal) => {
            assertSameSession(lease.get(), identity);
            // 正式开箱动作在发出脉冲前自行复核 lease；脉冲后不得改写终态。
            return input.control.testCashDrawer!(signal);
          },
        }
      : {}),
    ...(leaseAwareControl.clearSavedPrinter
      ? {
          clearSavedPrinter: async (signal: AbortSignal) => {
            assertSameSession(lease.get(), identity);
            return leaseAwareControl.clearSavedPrinter!(
              signal,
              () => assertSameSession(lease.get(), identity),
            );
          },
        }
      : {}),
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
