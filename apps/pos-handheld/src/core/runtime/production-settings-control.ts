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
  type SettingsDeviceActivationPreviewResponse,
  type SettingsPaymentSettingsInput,
  type SettingsPendingDataSnapshot,
  type SettingsPrinterDevice,
  type SettingsReceiptProfileDraft,
  type SettingsScannerTestResult,
  type SettingsLinklyPairingPort,
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
  paymentConfigurationTransition: Readonly<{
    run<T>(operation: () => Promise<T>): Promise<T>;
  }>;
  linklySetup?: SettingsLinklyPairingPort | undefined;
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
  receiptProfile: Readonly<{
    load(signal: AbortSignal): Promise<SettingsReceiptProfileDraft | null>;
  }>;
  scanner: Readonly<{
    test(signal: AbortSignal): Promise<SettingsScannerTestResult>;
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
    runSwitchGuarded?<T>(operation: () => Promise<T>): Promise<
      | Readonly<{ blocked: true }>
      | Readonly<{ blocked: false; value: T }>
    >;
  }>;
  device: Readonly<{
    previewActivationCode?:
      | ((
          activationCode: string,
          signal: AbortSignal,
        ) => Promise<SettingsDeviceActivationPreviewResponse>)
      | undefined;
    reregister(
      input: Readonly<{
        activationCode: string;
        terminalName?: string;
      }>,
      signal: AbortSignal,
    ): Promise<void>;
    resetRegistration(
      employeeBarcode: string,
      signal: AbortSignal,
    ): Promise<"completed" | "pending-recovery">;
    hasRegistrationRecoveryRisk?(): Promise<boolean>;
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

  public previewDeviceActivationCode(
    activationCode: string,
    signal: AbortSignal,
  ): Promise<SettingsDeviceActivationPreviewResponse> {
    const previewActivationCode = this.input.device.previewActivationCode;
    if (!previewActivationCode) {
      return Promise.reject(
        new Error("Device activation preview is unavailable."),
      );
    }
    return abortChecked(signal, () =>
      previewActivationCode(activationCode, signal),
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

  public loadReceiptProfile(
    signal: AbortSignal,
  ): Promise<SettingsReceiptProfileDraft | null> {
    return abortChecked(signal, () => this.input.receiptProfile.load(signal));
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

  public checkForAppUpdate(
    signal: AbortSignal,
  ): Promise<SettingsAppUpdateSnapshot> {
    return abortChecked(signal, () => this.input.appUpdate.check(signal));
  }

  public async executeDangerousAction(
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
    employeeBarcodeOrAssertActive?: string | (() => void),
    explicitAssertActive?: () => void,
  ): Promise<SettingsDangerousActionResult> {
    const employeeBarcode =
      typeof employeeBarcodeOrAssertActive === "string"
        ? employeeBarcodeOrAssertActive
        : undefined;
    const assertActive =
      typeof employeeBarcodeOrAssertActive === "function"
        ? employeeBarcodeOrAssertActive
        : explicitAssertActive ?? (() => undefined);
    throwIfAborted(signal);
    if (
      action.kind === "change-payment-settings" ||
      action.kind === "pair-linkly"
    ) {
      // transition 已按目录→购物车固定锁序封住新业务并等待在途 operation；
      // 这里直接进入 guarded，不能再次申请同一目录门造成自锁。
      return this.input.paymentConfigurationTransition.run(() =>
        this.executeDangerousActionGuarded(
          action,
          signal,
          employeeBarcode,
          assertActive,
        ),
      );
    }
    if (
      action.kind === "reset-catalog" ||
      action.kind === "restart-app"
    ) {
      // App 更新会自行取得 transition 的目录独占门；此处预拿普通门会与其等待
      // operation 清零形成自锁。目录重置则由共享后台 coordinator 自己互斥。
      return this.executeDangerousActionGuarded(
        action,
        signal,
        employeeBarcode,
      );
    }
    if (action.kind === "reset-device-registration") {
      // 已由全局 transition 取得目录→购物车 barrier；不得重复申请目录门。
      return this.executeDangerousActionGuarded(
        action,
        signal,
        employeeBarcode,
        assertActive,
      );
    }
    if (action.kind === "change-api-address") {
      // 旧组合或测试替身若未提供分区门闩，绝不能退回到可切换路径。
      if (!this.input.apiConfiguration.runSwitchGuarded) {
        return safetyBlocked();
      }
      const guarded = await this.input.apiConfiguration.runSwitchGuarded(
        async () => {
          try {
            return await this.input.catalog.runExclusive(() =>
              this.executeDangerousActionGuarded(
                action,
                signal,
                employeeBarcode,
              ),
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
        },
      );
      return guarded.blocked ? safetyBlocked() : guarded.value;
    }
    try {
      return await this.input.catalog.runExclusive(() =>
        this.executeDangerousActionGuarded(
          action,
          signal,
          employeeBarcode,
        ),
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
    employeeBarcode?: string,
    assertActive: () => void = () => undefined,
  ): Promise<SettingsDangerousActionResult> {
    throwIfAborted(signal);
    assertActive();
    if (
      action.kind === "change-api-address" &&
      await this.registrationRecoveryBlocks(signal)
    ) {
      return safetyBlocked();
    }
    if (this.catalogRefreshBlocks()) {
      return safetyBlocked();
    }
    const pending = await abortChecked(signal, () =>
      this.input.pendingData.read(signal),
    );
    const mayBypassPendingData =
      action.kind === "change-api-address" &&
      this.input.apiConfiguration.allowSwitchWithPendingLocalData === true;
    if (pendingDataBlocksAction(action, pending) && !mayBypassPendingData) {
      return Object.freeze({
        status: "blocked",
        reason: "pending-local-data",
      });
    }
    if (this.catalogRefreshBlocks()) {
      return safetyBlocked();
    }
    // 安全快照可能跨越异步数据库与恢复读取；任何持久化提交前必须重验会话。
    assertActive();

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
        if (
          await this.registrationRecoveryBlocks(signal)
        ) {
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
      case "pair-linkly": {
        if (!this.input.linklySetup) {
          throw new Error("Linkly setup adapter is unavailable.");
        }
        // 这是不可逆外部提交：只在提交前检查 abort/lease，POST 返回后保留
        // completed/unknown 终态，避免 signal 或 cashier lease 变化诱导重放 PairCode。
        throwIfAborted(signal);
        assertActive();
        const result = await this.input.linklySetup.pair(
          action.environment,
          action.pairCode,
          signal,
        );
        return result.status === "unknown"
          ? Object.freeze({ status: "unknown", kind: action.kind })
          : completed(action.kind);
      }
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
              activationCode: action.activationCode,
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
      case "reset-device-registration": {
        const barcode = employeeBarcode?.trim() ?? "";
        if (!barcode) {
          throw new Error("SETTINGS_DEVICE_RESET_EMPLOYEE_BARCODE_REQUIRED");
        }
        // 服务端提交后动作不可逆；协调器负责 response-loss marker 与本机 fail-close。
        throwIfAborted(signal);
        assertActive();
        const result = await this.input.device.resetRegistration(
          barcode,
          signal,
        );
        if (result === "pending-recovery") {
          return Object.freeze({
            status: "pending-recovery" as const,
            kind: action.kind,
          });
        }
        await this.input.runtimeReload.reload(signal);
        return completed(action.kind);
      }
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

  private async registrationRecoveryBlocks(
    signal: AbortSignal,
  ): Promise<boolean> {
    const hasRecoveryRisk = this.input.device.hasRegistrationRecoveryRisk;
    if (!hasRecoveryRisk) return true;
    try {
      return await abortChecked(signal, () =>
        hasRecoveryRisk.call(this.input.device),
      );
    } catch (error) {
      if (signal.aborted) throw error;
      return true;
    }
  }
}

function pendingDataBlocksAction(
  action: SettingsDangerousConfirmation,
  pending: SettingsPendingDataSnapshot,
): boolean {
  if (
    action.kind === "change-payment-settings" ||
    action.kind === "pair-linkly"
  ) {
    // 普通已耐久队列可在 reload 后继续处理；内存购物车、进行中的外部动作，
    // 以及仍依赖旧 provider/environment 的订单或恢复必须保持失败关闭。
    return (
      pending.hasActiveCart ||
      pending.hasFulfilmentInFlight ||
      pending.hasSyncOrAuditInFlight ||
      pending.paymentConfigurationSensitiveOrderCount > 0 ||
      pending.unresolvedPaymentCount > 0
    );
  }
  return hasPendingLocalData(pending);
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
