import {
  CatalogRefreshCoordinatorError,
  type CatalogRefreshState,
} from "../../features/catalog/catalog-refresh-coordinator";
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
    getRefreshState(): CatalogRefreshState;
    subscribeRefresh(listener: () => void): () => void;
    runExclusive<T>(operation: () => Promise<T>): Promise<T>;
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
    /**
     * 仅供本机开发构建切换调试后端；生产组合根必须保持 false/undefined。
     */
    allowSwitchWithPendingLocalData?: boolean;
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

  public getCatalogRefreshState(): CatalogRefreshState {
    return this.input.catalog.getRefreshState();
  }

  public subscribeCatalogRefresh(listener: () => void): () => void {
    return this.input.catalog.subscribeRefresh(listener);
  }

  public downloadCatalog(
    signal: AbortSignal,
  ): Promise<SettingsCatalogSnapshot> {
    return abortChecked(signal, () => this.input.catalog.download(signal));
  }

  public testApiAddress(
    apiBaseUrl: string,
    signal: AbortSignal,
  ): Promise<boolean> {
    return abortChecked(signal, () =>
      this.input.apiConfiguration.probe(
        `${apiBaseUrl}/api/v1/health`,
        signal,
      ),
    );
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
    if (action.kind === "reset-catalog") {
      return this.executeDangerousActionGuarded(action, signal);
    }
    try {
      return await this.input.catalog.runExclusive(() =>
        this.executeDangerousActionGuarded(action, signal),
      );
    } catch (error) {
      if (
        error instanceof CatalogRefreshCoordinatorError &&
        (error.code === "CATALOG_REFRESH_OPERATION_CONFLICT" ||
          error.code === "CATALOG_REFRESH_COORDINATOR_SHUTDOWN")
      ) {
        return safetyBlocked();
      }
      throw error;
    }
  }

  private async executeDangerousActionGuarded(
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
  ): Promise<SettingsDangerousActionResult> {
    throwIfAborted(signal);
    if (this.catalogRefreshBlocks()) {
      return safetyBlocked();
    }
    const pending = await abortChecked(signal, () =>
      this.input.pendingData.read(signal),
    );
    const mayBypassPendingData =
      action.kind === "change-api-address" &&
      this.input.apiConfiguration.allowSwitchWithPendingLocalData === true;
    if (hasPendingLocalData(pending) && !mayBypassPendingData) {
      return Object.freeze({
        status: "blocked",
        reason: "pending-local-data",
      });
    }
    if (this.catalogRefreshBlocks()) {
      return safetyBlocked();
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
        if (this.catalogRefreshBlocks()) {
          return safetyBlocked();
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
        if (this.catalogRefreshBlocks()) {
          return safetyBlocked();
        }
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

  private catalogRefreshBlocks(): boolean {
    return this.input.catalog.getRefreshState().kind === "running";
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

function safetyBlocked(): SettingsDangerousActionResult {
  return Object.freeze({
    status: "blocked",
    reason: "safety-check-failed",
  });
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
