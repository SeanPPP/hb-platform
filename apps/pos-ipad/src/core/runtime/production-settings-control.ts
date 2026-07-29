import {
  hasPendingLocalData,
  type SettingsAppUpdateSnapshot,
  type SettingsCatalogSnapshot,
  type SettingsControlPort,
  type SettingsDangerousActionResult,
  type SettingsDangerousConfirmation,
  type SettingsPaymentSettingsInput,
  type SettingsPendingDataSnapshot,
  type SettingsPrinterDevice,
  type SettingsScannerTestResult,
  type SettingsSnapshot,
} from "../../features/settings/settings-presenter";
import type { ReceiptPrinterSettings } from "../db/pos-settings-repository";

export type ProductionSettingsControlDependencies = Readonly<{
  readSnapshot(signal: AbortSignal): Promise<SettingsSnapshot>;
  catalog: Readonly<{
    download(signal: AbortSignal): Promise<SettingsCatalogSnapshot>;
    reset(signal: AbortSignal): Promise<SettingsCatalogSnapshot>;
  }>;
  payments: Readonly<{
    test(
      provider: "square" | "linkly",
      input: SettingsPaymentSettingsInput,
      signal: AbortSignal,
    ): Promise<void>;
  }>;
  paymentConfiguration: Readonly<{
    save(input: SettingsPaymentSettingsInput): Promise<void>;
  }>;
  runtimeReload: Readonly<{
    reload(signal: AbortSignal): Promise<void>;
  }>;
  printer: Readonly<{
    saveSettings(
      settings: ReceiptPrinterSettings,
      signal: AbortSignal,
    ): Promise<void>;
    scan(signal: AbortSignal): Promise<readonly SettingsPrinterDevice[]>;
    connect(peripheralId: string, signal: AbortSignal): Promise<void>;
    test(signal: AbortSignal): Promise<void>;
  }>;
  scanner: Readonly<{
    test(signal: AbortSignal): Promise<SettingsScannerTestResult>;
  }>;
  display: Readonly<{
    setEnabled(enabled: boolean, signal: AbortSignal): Promise<void>;
    test(signal: AbortSignal): Promise<void>;
  }>;
  appUpdate: Readonly<{
    check(signal: AbortSignal): Promise<SettingsAppUpdateSnapshot>;
    restart(signal: AbortSignal): Promise<boolean>;
  }>;
  /**
   * 调用者已持有与所有交易写路径共享的独占门闩；read 必须在该门闩内读取
   * 活动购物车、销售/退款队列、未决支付和耐久写入的一致快照。
   */
  pendingData: Readonly<{
    read(signal: AbortSignal): Promise<SettingsPendingDataSnapshot>;
  }>;
  apiConfiguration: Readonly<{
    probe(healthUrl: string, signal: AbortSignal): Promise<boolean>;
    save(apiBaseUrl: string): Promise<void>;
  }>;
  device: Readonly<{
    reregister(
      input: Readonly<{
        targetStoreCode: string;
        terminalName?: string;
      }>,
      signal: AbortSignal,
    ): Promise<void>;
  }>;
}>;

/**
 * Settings 的真实控制面。危险动作的安全顺序固定为：
 * 中止检查 → 一致待处理快照 → 候选探测（如有）→ 状态变更 → 中止检查。
 * 组合根必须在外层独占锁释放前完成整个调用。
 */
export class ProductionSettingsControl implements SettingsControlPort {
  public constructor(
    private readonly input: ProductionSettingsControlDependencies,
  ) {}

  public loadSnapshot(signal: AbortSignal): Promise<SettingsSnapshot> {
    return abortChecked(signal, () => this.input.readSnapshot(signal));
  }

  public downloadCatalog(
    signal: AbortSignal,
  ): Promise<SettingsCatalogSnapshot> {
    return abortChecked(signal, () => this.input.catalog.download(signal));
  }

  public testPaymentProvider(
    provider: "square" | "linkly",
    input: SettingsPaymentSettingsInput,
    signal: AbortSignal,
  ): Promise<void> {
    return abortChecked(signal, () =>
      this.input.payments.test(provider, input, signal),
    );
  }

  public savePrinterSettings(
    settings: ReceiptPrinterSettings,
    signal: AbortSignal,
  ): Promise<void> {
    return abortChecked(signal, () =>
      this.input.printer.saveSettings(settings, signal),
    );
  }

  public scanPrinters(
    signal: AbortSignal,
  ): Promise<readonly SettingsPrinterDevice[]> {
    return abortChecked(signal, () => this.input.printer.scan(signal));
  }

  public connectPrinter(
    peripheralId: string,
    signal: AbortSignal,
  ): Promise<void> {
    return abortChecked(signal, () =>
      this.input.printer.connect(peripheralId, signal),
    );
  }

  public testPrinter(signal: AbortSignal): Promise<void> {
    return abortChecked(signal, () => this.input.printer.test(signal));
  }

  public testScanner(
    signal: AbortSignal,
  ): Promise<SettingsScannerTestResult> {
    return abortChecked(signal, () => this.input.scanner.test(signal));
  }

  public setExternalDisplayEnabled(
    enabled: boolean,
    signal: AbortSignal,
  ): Promise<void> {
    return abortChecked(signal, () =>
      this.input.display.setEnabled(enabled, signal),
    );
  }

  public testExternalDisplay(signal: AbortSignal): Promise<void> {
    return abortChecked(signal, () => this.input.display.test(signal));
  }

  public checkForAppUpdate(
    signal: AbortSignal,
  ): Promise<SettingsAppUpdateSnapshot> {
    return abortChecked(signal, () => this.input.appUpdate.check(signal));
  }

  public async executeDangerousAction(
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
  ): Promise<SettingsDangerousActionResult> {
    throwIfAborted(signal);
    const pending = await abortChecked(signal, () =>
      this.input.pendingData.read(signal),
    );
    if (hasPendingLocalData(pending)) {
      return Object.freeze({
        status: "blocked",
        reason: "pending-local-data",
      });
    }

    switch (action.kind) {
      case "change-api-address": {
        const healthUrl = `${action.apiBaseUrl}/api/v1/health`;
        const reachable = await abortChecked(signal, () =>
          this.input.apiConfiguration.probe(healthUrl, signal),
        );
        if (!reachable) {
          return Object.freeze({
            status: "blocked",
            reason: "candidate-unreachable",
          });
        }
        await abortChecked(signal, () =>
          this.input.apiConfiguration.save(action.apiBaseUrl),
        );
        await abortChecked(signal, () =>
          this.input.runtimeReload.reload(signal),
        );
        return completed(action.kind);
      }
      case "change-payment-settings":
        await abortChecked(signal, () =>
          this.input.paymentConfiguration.save(action.input),
        );
        await abortChecked(signal, () =>
          this.input.runtimeReload.reload(signal),
        );
        return completed(action.kind);
      case "reset-catalog": {
        const catalog = await abortChecked(signal, () =>
          this.input.catalog.reset(signal),
        );
        return Object.freeze({
          status: "completed",
          kind: action.kind,
          catalog,
        });
      }
      case "reregister-device":
        await abortChecked(signal, () =>
          this.input.device.reregister(
            {
              targetStoreCode: action.targetStoreCode,
              ...(action.terminalName
                ? { terminalName: action.terminalName }
                : {}),
            },
            signal,
          ),
        );
        await abortChecked(signal, () =>
          this.input.runtimeReload.reload(signal),
        );
        return completed(action.kind);
      case "restart-app": {
        const restarted = await abortChecked(signal, () =>
          this.input.appUpdate.restart(signal),
        );
        return restarted
          ? completed(action.kind)
          : Object.freeze({
              status: "blocked" as const,
              reason: "safety-check-failed" as const,
            });
      }
    }
  }
}

function completed(
  kind: Exclude<
    SettingsDangerousConfirmation["kind"],
    "reset-catalog"
  >,
): SettingsDangerousActionResult {
  return Object.freeze({ status: "completed", kind });
}

async function abortChecked<T>(
  signal: AbortSignal,
  operation: () => Promise<T>,
): Promise<T> {
  throwIfAborted(signal);
  const result = await operation();
  throwIfAborted(signal);
  return result;
}

function throwIfAborted(signal: AbortSignal): void {
  if (signal.aborted) {
    throw new Error("Settings operation aborted.");
  }
}
