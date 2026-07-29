import {
  resolveSettingsAccess,
  type SettingsAccess,
} from "./settings-authorization";

import {
  DEFAULT_RECEIPT_PRINTER_SETTINGS,
  type ReceiptPrinterSettings,
} from "@/core/db/pos-settings-repository";

export type SettingsPane =
  "general" | "payments" | "peripherals" | "device" | "hardware";

export type PaymentEnvironment = "Sandbox" | "Production";
export type SettingsPaymentProvider = "square" | "linkly";

export type SettingsCatalogSnapshot = Readonly<{
  snapshotId: string | null;
  itemCount: number;
  activatedAt: string | null;
}>;

export type SettingsPaymentProviderSnapshot = Readonly<{
  available: boolean;
  blockerCode: string | null;
  environment: PaymentEnvironment;
}>;

export type SettingsSquareSnapshot = SettingsPaymentProviderSnapshot &
  Readonly<{
    deviceId: string;
    locationId: string;
  }>;

export type SettingsExternalDisplaySnapshot = Readonly<{
  available: boolean;
  enabled: boolean;
  status: "connected" | "disconnected" | "unavailable";
}>;

export type SettingsHardwareSnapshot = Readonly<{
  printerStatus: "connected" | "disconnected" | "unavailable";
  scannerStatus: "ready" | "unavailable";
  externalDisplayStatus: "connected" | "disconnected" | "unavailable";
  lastScannerValue: string | null;
}>;

export type SettingsAppUpdateSnapshot = Readonly<{
  channel: string;
  currentVersion: string;
  availableVersion: string | null;
  updateRequired: boolean;
  restartAvailable: boolean;
}>;

export type SettingsDeviceSnapshot = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string;
  terminalName: string;
}>;

export type SettingsSnapshot = Readonly<{
  apiBaseUrl: string;
  appUpdate: SettingsAppUpdateSnapshot;
  catalog: SettingsCatalogSnapshot;
  device: SettingsDeviceSnapshot;
  externalDisplay: SettingsExternalDisplaySnapshot;
  hardware: SettingsHardwareSnapshot;
  linkly: SettingsPaymentProviderSnapshot;
  paymentProvider: SettingsPaymentProvider | null;
  printer: ReceiptPrinterSettings;
  square: SettingsSquareSnapshot;
}>;

export type SettingsPaymentDraft = Readonly<{
  square: Readonly<{
    environment: PaymentEnvironment;
    deviceId: string;
    locationId: string;
  }>;
  linkly: Readonly<{
    environment: PaymentEnvironment;
  }>;
}>;

export type SettingsPaymentSettingsInput =
  | Readonly<{
      provider: "square";
      square: SettingsPaymentDraft["square"];
      linkly: null;
    }>
  | Readonly<{
      provider: "linkly";
      square: null;
      linkly: SettingsPaymentDraft["linkly"];
    }>;

export type SettingsPendingDataSnapshot = Readonly<{
  hasActiveCart: boolean;
  pendingDurableWriteCount: number;
  pendingReturnCount: number;
  pendingSaleCount: number;
  unresolvedPaymentCount: number;
}>;

export type SettingsPrinterDevice = Readonly<{
  id: string;
  name: string;
  transport: string;
}>;

export type SettingsScannerTestResult = Readonly<{
  source: "camera" | "hid";
  value: string;
}>;

export type SettingsDangerousConfirmation =
  | Readonly<{
      kind: "change-api-address";
      apiBaseUrl: string;
    }>
  | Readonly<{
      kind: "change-payment-settings";
      input: SettingsPaymentSettingsInput;
    }>
  | Readonly<{ kind: "reset-catalog" }>
  | Readonly<{
      kind: "reregister-device";
      targetStoreCode: string;
      terminalName?: string;
    }>
  | Readonly<{ kind: "restart-app" }>;

export type SettingsDangerousActionResult =
  | Readonly<{
      status: "blocked";
      reason:
        "candidate-unreachable" | "pending-local-data" | "safety-check-failed";
    }>
  | Readonly<{
      status: "completed";
      kind:
        | "change-api-address"
        | "change-payment-settings"
        | "reregister-device"
        | "restart-app";
    }>
  | Readonly<{
      status: "completed";
      kind: "reset-catalog";
      catalog: SettingsCatalogSnapshot;
    }>;

export interface SettingsControlPort {
  loadSnapshot(signal: AbortSignal): Promise<SettingsSnapshot>;
  downloadCatalog(signal: AbortSignal): Promise<SettingsCatalogSnapshot>;
  testPaymentProvider(
    provider: "square" | "linkly",
    input: SettingsPaymentSettingsInput,
    signal: AbortSignal,
  ): Promise<void>;
  savePrinterSettings(
    settings: ReceiptPrinterSettings,
    signal: AbortSignal,
  ): Promise<void>;
  scanPrinters(signal: AbortSignal): Promise<readonly SettingsPrinterDevice[]>;
  connectPrinter(peripheralId: string, signal: AbortSignal): Promise<void>;
  testPrinter(signal: AbortSignal): Promise<void>;
  testScanner(signal: AbortSignal): Promise<SettingsScannerTestResult>;
  setExternalDisplayEnabled(
    enabled: boolean,
    signal: AbortSignal,
  ): Promise<void>;
  testExternalDisplay(signal: AbortSignal): Promise<void>;
  checkForAppUpdate(signal: AbortSignal): Promise<SettingsAppUpdateSnapshot>;

  /**
   * 组合根必须在一个互斥临界区内重新读取活动购物车、待同步销售/退款、
   * 未决支付与耐久写入，并在临界区释放前执行动作，禁止新业务状态穿插。
   * API 切换还必须先 GET 候选地址的 /api/v1/health；失败时保留旧地址。
   * signal 中止时，硬件监听和协调器租约都必须立即释放。
   */
  executeDangerousAction(
    action: SettingsDangerousConfirmation,
    signal: AbortSignal,
  ): Promise<SettingsDangerousActionResult>;
}

export type SettingsStatusCode =
  | "api-address-saved"
  | "api-health-check-failed"
  | "app-restart-requested"
  | "app-update-check-failed"
  | "app-update-checked"
  | "catalog-download-failed"
  | "catalog-downloaded"
  | "catalog-reset"
  | "catalog-reset-failed"
  | "device-reregister-failed"
  | "device-reregister-started"
  | "display-setting-failed"
  | "display-setting-saved"
  | "display-test-failed"
  | "display-test-passed"
  | "invalid-api-address"
  | "invalid-device-registration"
  | "load-failed"
  | "payment-settings-invalid"
  | "payment-settings-save-failed"
  | "payment-settings-saved"
  | "payment-test-failed"
  | "payment-test-passed"
  | "pending-local-data"
  | "permission-required"
  | "printer-connect-failed"
  | "printer-connected"
  | "printer-scan-failed"
  | "printer-scan-finished"
  | "printer-settings-save-failed"
  | "printer-settings-saved"
  | "printer-test-failed"
  | "printer-test-passed"
  | "restart-failed"
  | "safety-check-failed"
  | "scanner-test-failed"
  | "scanner-test-passed";

export type SettingsState = Readonly<{
  access: SettingsAccess;
  activePane: SettingsPane;
  apiBaseUrl: string;
  apiAddressDraft: string;
  appUpdate: SettingsAppUpdateSnapshot;
  busy: boolean;
  catalog: SettingsCatalogSnapshot;
  confirmation: SettingsDangerousConfirmation | null;
  device: SettingsDeviceSnapshot;
  externalDisplay: SettingsExternalDisplaySnapshot;
  hardware: SettingsHardwareSnapshot;
  kind: "idle" | "loading" | "ready" | "unauthorized" | "failed";
  linkly: SettingsPaymentProviderSnapshot;
  linklyDraft: SettingsPaymentDraft["linkly"];
  paymentProvider: SettingsPaymentProvider | null;
  paymentProviderDraft: SettingsPaymentProvider | null;
  printer: ReceiptPrinterSettings;
  printerDevices: readonly SettingsPrinterDevice[];
  reregisterStoreCode: string;
  square: SettingsSquareSnapshot;
  squareDraft: SettingsPaymentDraft["square"];
  statusCode: SettingsStatusCode | null;
  terminalNameDraft: string;
}>;

export type SettingsPresenterOptions = Readonly<{
  permissions: readonly string[];
  port: SettingsControlPort;
}>;

/**
 * Settings 只编排公开配置与窄硬件端口。支付凭据、设备密钥、数据库和 HTTP
 * transport 都不能进入 state；危险动作还必须通过待同步数据的 fail-closed 门禁。
 */
export class SettingsPresenter {
  private readonly listeners = new Set<() => void>();
  private readonly lifetime = new AbortController();
  private state: SettingsState;
  private destroyed = false;
  private loadGeneration = 0;
  private actionInFlight: Promise<void> | null = null;

  public constructor(private readonly options: SettingsPresenterOptions) {
    const access = resolveSettingsAccess(options.permissions);
    this.state = initialState(access);
  }

  public readonly getState = (): SettingsState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.loadGeneration += 1;
    this.lifetime.abort();
    this.listeners.clear();
  }

  public async load(): Promise<void> {
    if (this.destroyed) return;
    if (!this.state.access.canView) {
      this.patch({
        kind: "unauthorized",
        statusCode: "permission-required",
      });
      return;
    }
    const generation = ++this.loadGeneration;
    this.patch({ kind: "loading", statusCode: null });
    try {
      const snapshot = normalizeSnapshot(
        await this.options.port.loadSnapshot(this.lifetime.signal),
      );
      if (!this.isCurrentLoad(generation)) return;
      this.patch({
        apiBaseUrl: snapshot.apiBaseUrl,
        apiAddressDraft: snapshot.apiBaseUrl,
        appUpdate: snapshot.appUpdate,
        catalog: snapshot.catalog,
        device: snapshot.device,
        externalDisplay: snapshot.externalDisplay,
        hardware: snapshot.hardware,
        kind: "ready",
        linkly: snapshot.linkly,
        linklyDraft: {
          environment: snapshot.linkly.environment,
        },
        paymentProvider: snapshot.paymentProvider,
        paymentProviderDraft: snapshot.paymentProvider,
        printer: snapshot.printer,
        reregisterStoreCode: "",
        square: snapshot.square,
        squareDraft: {
          deviceId: snapshot.square.deviceId,
          environment: snapshot.square.environment,
          locationId: snapshot.square.locationId,
        },
        statusCode: null,
        terminalNameDraft: snapshot.device.terminalName,
      });
    } catch {
      if (!this.isCurrentLoad(generation)) return;
      this.patch({ kind: "failed", statusCode: "load-failed" });
    }
  }

  public selectPane(pane: SettingsPane): boolean {
    if (this.destroyed || this.state.busy || this.state.confirmation !== null) {
      return false;
    }
    this.patch({ activePane: pane, statusCode: null });
    return true;
  }

  public setApiAddressDraft(value: string): void {
    if (!this.canEdit()) return;
    this.patch({ apiAddressDraft: value, statusCode: null });
  }

  public setSquareEnvironment(environment: PaymentEnvironment): void {
    if (!this.canEditPayments()) return;
    this.patch({
      squareDraft: { ...this.state.squareDraft, environment },
      statusCode: null,
    });
  }

  public setSquareLocationId(locationId: string): void {
    if (!this.canEditPayments()) return;
    this.patch({
      squareDraft: { ...this.state.squareDraft, locationId },
      statusCode: null,
    });
  }

  public setSquareDeviceId(deviceId: string): void {
    if (!this.canEditPayments()) return;
    this.patch({
      squareDraft: { ...this.state.squareDraft, deviceId },
      statusCode: null,
    });
  }

  public setLinklyEnvironment(environment: PaymentEnvironment): void {
    if (!this.canEditPayments()) return;
    this.patch({
      linklyDraft: { environment },
      statusCode: null,
    });
  }

  public setPaymentProvider(provider: SettingsPaymentProvider): void {
    if (!this.canEditPayments()) return;
    if (!isPaymentProviderAvailable(provider, this.state)) {
      this.patch({ statusCode: "payment-settings-invalid" });
      return;
    }
    this.patch({ paymentProviderDraft: provider, statusCode: null });
  }

  public setPrinterEnabled(enabled: boolean): void {
    if (!this.canEditPrinter()) return;
    this.patch({
      printer: { ...this.state.printer, printEnabled: enabled },
      statusCode: null,
    });
  }

  public setDrawerEnabled(enabled: boolean): void {
    if (!this.canEditPrinter()) return;
    this.patch({
      printer: { ...this.state.printer, drawerEnabled: enabled },
      statusCode: null,
    });
  }

  public setPrinterPeripheralId(peripheralId: string): void {
    if (!this.canEditPrinter()) return;
    this.patch({
      printer: { ...this.state.printer, peripheralId },
      statusCode: null,
    });
  }

  public setPrinterPaper(paper: ReceiptPrinterSettings["paper"]): void {
    if (!this.canEditPrinter()) return;
    this.patch({
      printer: { ...this.state.printer, paper },
      statusCode: null,
    });
  }

  public setPrinterLocale(locale: ReceiptPrinterSettings["locale"]): void {
    if (!this.canEditPrinter()) return;
    this.patch({
      printer: { ...this.state.printer, locale },
      statusCode: null,
    });
  }

  public setReregisterStoreCode(value: string): void {
    if (!this.canEdit()) return;
    this.patch({ reregisterStoreCode: value, statusCode: null });
  }

  public setTerminalName(value: string): void {
    if (!this.canEdit()) return;
    this.patch({ terminalNameDraft: value, statusCode: null });
  }

  public savePaymentSettings(): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePayments)) {
      return Promise.resolve();
    }
    const input = normalizePaymentDraft(
      this.state.paymentProviderDraft,
      this.state.squareDraft,
      this.state.linklyDraft,
      isPaymentProviderAvailable("square", this.state),
      isPaymentProviderAvailable("linkly", this.state),
    );
    if (!input) {
      this.patch({ statusCode: "payment-settings-invalid" });
      return Promise.resolve();
    }
    const current = currentPaymentSettings(this.state);
    if (current && paymentSettingsEqual(input, current)) {
      this.patch({ statusCode: "payment-settings-saved" });
      return Promise.resolve();
    }
    this.requestConfirmation({
      kind: "change-payment-settings",
      input,
    });
    return Promise.resolve();
  }

  public testPaymentProvider(provider: "square" | "linkly"): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePayments)) {
      return Promise.resolve();
    }
    if (provider !== this.state.paymentProviderDraft) {
      this.patch({ statusCode: "payment-settings-invalid" });
      return Promise.resolve();
    }
    const input = normalizePaymentDraft(
      this.state.paymentProviderDraft,
      this.state.squareDraft,
      this.state.linklyDraft,
      isPaymentProviderAvailable("square", this.state),
      isPaymentProviderAvailable("linkly", this.state),
    );
    if (
      !input ||
      (provider === "square" ? input.square === null : input.linkly === null)
    ) {
      this.patch({ statusCode: "payment-settings-invalid" });
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        await this.options.port.testPaymentProvider(
          provider,
          input,
          this.lifetime.signal,
        );
        this.patch({ statusCode: "payment-test-passed" });
      } catch {
        this.patch({ statusCode: "payment-test-failed" });
      }
    });
  }

  public savePrinterSettings(): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePrinter)) {
      return Promise.resolve();
    }
    const settings = normalizePrinterSettings(this.state.printer);
    return this.runAction(async () => {
      try {
        await this.options.port.savePrinterSettings(
          settings,
          this.lifetime.signal,
        );
        this.patch({
          printer: settings,
          statusCode: "printer-settings-saved",
        });
      } catch {
        this.patch({ statusCode: "printer-settings-save-failed" });
      }
    });
  }

  public scanPrinters(): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePrinter)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        const devices = await this.options.port.scanPrinters(
          this.lifetime.signal,
        );
        this.patch({
          printerDevices: Object.freeze(devices.map(normalizePrinterDevice)),
          statusCode: "printer-scan-finished",
        });
      } catch {
        this.patch({ statusCode: "printer-scan-failed" });
      }
    });
  }

  public connectPrinter(peripheralId: string): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePrinter)) {
      return Promise.resolve();
    }
    const normalizedId = peripheralId.trim();
    if (!normalizedId) {
      this.patch({ statusCode: "printer-connect-failed" });
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        await this.options.port.connectPrinter(
          normalizedId,
          this.lifetime.signal,
        );
        this.patch({
          hardware: {
            ...this.state.hardware,
            printerStatus: "connected",
          },
          printer: {
            ...this.state.printer,
            peripheralId: normalizedId,
          },
          statusCode: "printer-connected",
        });
      } catch {
        this.patch({ statusCode: "printer-connect-failed" });
      }
    });
  }

  public testPrinter(): Promise<void> {
    if (!this.requirePermission(this.state.access.canConfigurePrinter)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        await this.options.port.testPrinter(this.lifetime.signal);
        this.patch({ statusCode: "printer-test-passed" });
      } catch {
        this.patch({ statusCode: "printer-test-failed" });
      }
    });
  }

  public testScanner(): Promise<void> {
    if (!this.requirePermission(this.state.access.canTestScanner)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        const result = await this.options.port.testScanner(
          this.lifetime.signal,
        );
        const value = result.value.trim();
        if (!value || value.length > 512) {
          throw new Error("invalid scanner result");
        }
        this.patch({
          hardware: {
            ...this.state.hardware,
            // 硬件测试只保留长度与脱敏尾码，避免员工码、券码等误扫后进入 UI state。
            lastScannerValue: maskScannerValue(value),
            scannerStatus: "ready",
          },
          statusCode: "scanner-test-passed",
        });
      } catch {
        this.patch({ statusCode: "scanner-test-failed" });
      }
    });
  }

  public setExternalDisplayEnabled(enabled: boolean): Promise<void> {
    if (!this.requirePermission(this.state.access.canManageCustomerDisplay)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        await this.options.port.setExternalDisplayEnabled(
          enabled,
          this.lifetime.signal,
        );
        this.patch({
          externalDisplay: { ...this.state.externalDisplay, enabled },
          statusCode: "display-setting-saved",
        });
      } catch {
        this.patch({ statusCode: "display-setting-failed" });
      }
    });
  }

  public testExternalDisplay(): Promise<void> {
    if (!this.requirePermission(this.state.access.canManageCustomerDisplay)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        await this.options.port.testExternalDisplay(this.lifetime.signal);
        this.patch({ statusCode: "display-test-passed" });
      } catch {
        this.patch({ statusCode: "display-test-failed" });
      }
    });
  }

  public downloadCatalog(): Promise<void> {
    if (this.actionInFlight) return this.actionInFlight;
    if (!this.requirePermission(this.state.access.canDownloadCatalog)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        const catalog = normalizeCatalog(
          await this.options.port.downloadCatalog(this.lifetime.signal),
        );
        this.patch({
          catalog,
          statusCode: "catalog-downloaded",
        });
      } catch {
        this.patch({ statusCode: "catalog-download-failed" });
      }
    });
  }

  public checkForAppUpdate(): Promise<void> {
    if (!this.requirePermission(this.state.access.canManageAppUpdate)) {
      return Promise.resolve();
    }
    return this.runAction(async () => {
      try {
        const appUpdate = normalizeAppUpdate(
          await this.options.port.checkForAppUpdate(this.lifetime.signal),
        );
        this.patch({
          appUpdate,
          statusCode: "app-update-checked",
        });
      } catch {
        this.patch({ statusCode: "app-update-check-failed" });
      }
    });
  }

  public requestApiAddressChange(): boolean {
    if (!this.requirePermission(this.state.access.canReregisterDevice)) {
      return false;
    }
    let apiBaseUrl: string;
    try {
      apiBaseUrl = normalizeApiAddress(this.state.apiAddressDraft);
    } catch {
      this.patch({ confirmation: null, statusCode: "invalid-api-address" });
      return false;
    }
    return this.requestConfirmation({
      kind: "change-api-address",
      apiBaseUrl,
    });
  }

  public requestCatalogReset(): boolean {
    if (!this.requirePermission(this.state.access.canResetCatalog)) {
      return false;
    }
    return this.requestConfirmation({ kind: "reset-catalog" });
  }

  public requestDeviceReregistration(): boolean {
    if (!this.requirePermission(this.state.access.canReregisterDevice)) {
      return false;
    }
    const targetStoreCode = this.state.reregisterStoreCode.trim();
    const terminalName = this.state.terminalNameDraft.trim();
    if (!targetStoreCode || targetStoreCode === this.state.device.storeCode) {
      this.patch({
        confirmation: null,
        statusCode: "invalid-device-registration",
      });
      return false;
    }
    return this.requestConfirmation({
      kind: "reregister-device",
      targetStoreCode,
      ...(terminalName ? { terminalName } : {}),
    });
  }

  public requestAppRestart(): boolean {
    if (!this.requirePermission(this.state.access.canManageAppUpdate)) {
      return false;
    }
    return this.requestConfirmation({ kind: "restart-app" });
  }

  public cancelConfirmation(): void {
    if (this.destroyed || this.state.busy) return;
    this.patch({ confirmation: null, statusCode: null });
  }

  public confirmDangerousAction(): Promise<void> {
    const confirmation = this.state.confirmation;
    if (!confirmation || this.destroyed) return Promise.resolve();
    return this.runAction(async () => {
      try {
        const result = await this.options.port.executeDangerousAction(
          confirmation,
          this.lifetime.signal,
        );
        if (result.status === "blocked") {
          this.patch({
            ...(confirmation.kind === "change-api-address"
              ? { apiAddressDraft: this.state.apiBaseUrl }
              : {}),
            confirmation: null,
            statusCode:
              result.reason === "candidate-unreachable"
                ? confirmation.kind === "change-api-address"
                  ? "api-health-check-failed"
                  : "safety-check-failed"
                : result.reason,
          });
          return;
        }
        if (result.kind !== confirmation.kind) {
          throw new Error("dangerous action result mismatch");
        }
        if (confirmation.kind === "change-api-address") {
          this.patch({
            apiBaseUrl: confirmation.apiBaseUrl,
            apiAddressDraft: confirmation.apiBaseUrl,
            confirmation: null,
            statusCode: "api-address-saved",
          });
          return;
        }
        if (confirmation.kind === "change-payment-settings") {
          this.applySavedPaymentSettings(confirmation.input);
          return;
        }
        if (
          confirmation.kind === "reset-catalog" &&
          result.kind === "reset-catalog"
        ) {
          this.patch({
            catalog: normalizeCatalog(result.catalog),
            confirmation: null,
            statusCode: "catalog-reset",
          });
          return;
        }
        this.patch({
          confirmation: null,
          statusCode:
            confirmation.kind === "reregister-device"
              ? "device-reregister-started"
              : "app-restart-requested",
        });
      } catch {
        this.patch({
          ...(confirmation.kind === "change-api-address"
            ? { apiAddressDraft: this.state.apiBaseUrl }
            : {}),
          confirmation: null,
          statusCode: dangerousActionFailureCode(confirmation.kind),
        });
      }
    });
  }

  private applySavedPaymentSettings(input: SettingsPaymentSettingsInput): void {
    this.patch({
      confirmation: null,
      paymentProvider: input.provider,
      paymentProviderDraft: input.provider,
      ...(input.linkly
        ? {
            linkly: { ...this.state.linkly, ...input.linkly },
            linklyDraft: input.linkly,
          }
        : {}),
      ...(input.square
        ? {
            square: { ...this.state.square, ...input.square },
            squareDraft: input.square,
          }
        : {}),
      statusCode: "payment-settings-saved",
    });
  }

  private requestConfirmation(
    confirmation: SettingsDangerousConfirmation,
  ): boolean {
    if (this.destroyed || this.state.busy || this.state.confirmation !== null) {
      return false;
    }
    this.patch({ confirmation, statusCode: null });
    return true;
  }

  private requirePermission(granted: boolean): boolean {
    if (
      this.destroyed ||
      this.state.busy ||
      this.state.confirmation !== null ||
      this.state.kind !== "ready" ||
      !granted
    ) {
      if (!granted && this.state.confirmation === null) {
        this.patch({ statusCode: "permission-required" });
      }
      return false;
    }
    return true;
  }

  private canEdit(): boolean {
    return (
      !this.destroyed &&
      !this.state.busy &&
      this.state.confirmation === null &&
      this.state.kind === "ready"
    );
  }

  private canEditPayments(): boolean {
    return this.canEdit() && this.state.access.canConfigurePayments;
  }

  private canEditPrinter(): boolean {
    return this.canEdit() && this.state.access.canConfigurePrinter;
  }

  private runAction(action: () => Promise<void>): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (this.actionInFlight) return this.actionInFlight;
    this.patch({ busy: true, statusCode: null });
    const operation = action().finally(() => {
      if (this.actionInFlight === operation) {
        this.actionInFlight = null;
        this.patch({ busy: false });
      }
    });
    this.actionInFlight = operation;
    return operation;
  }

  private isCurrentLoad(generation: number): boolean {
    return !this.destroyed && this.loadGeneration === generation;
  }

  private patch(patch: Partial<SettingsState>): void {
    if (this.destroyed) return;
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

export function normalizeApiAddress(value: string): string {
  const source = value.trim();
  let parsed: URL;
  try {
    parsed = new URL(source);
  } catch {
    throw new Error("invalid API address");
  }
  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    throw new Error("invalid API scheme");
  }
  if (parsed.protocol === "http:" && !isLoopbackHostname(parsed.hostname)) {
    throw new Error("remote API requires HTTPS");
  }
  if (parsed.username || parsed.password || parsed.search || parsed.hash) {
    throw new Error("unsafe API address");
  }
  const path = parsed.pathname.replace(/\/+$/, "");
  return `${parsed.origin}${path}`;
}

export function hasPendingLocalData(
  snapshot: SettingsPendingDataSnapshot,
): boolean {
  return (
    snapshot.hasActiveCart ||
    snapshot.pendingDurableWriteCount > 0 ||
    snapshot.pendingReturnCount > 0 ||
    snapshot.pendingSaleCount > 0 ||
    snapshot.unresolvedPaymentCount > 0
  );
}

function initialState(access: SettingsAccess): SettingsState {
  const printer = Object.freeze({
    ...DEFAULT_RECEIPT_PRINTER_SETTINGS,
  });
  const square = Object.freeze({
    available: false,
    blockerCode: null,
    environment: "Production" as const,
    deviceId: "",
    locationId: "",
  });
  const linkly = Object.freeze({
    available: false,
    blockerCode: null,
    environment: "Production" as const,
  });
  return Object.freeze({
    access,
    activePane: "general",
    apiBaseUrl: "",
    apiAddressDraft: "",
    appUpdate: Object.freeze({
      channel: "production",
      currentVersion: "",
      availableVersion: null,
      updateRequired: false,
      restartAvailable: false,
    }),
    busy: false,
    catalog: Object.freeze({
      snapshotId: null,
      itemCount: 0,
      activatedAt: null,
    }),
    confirmation: null,
    device: Object.freeze({
      deviceCode: "",
      storeCode: "",
      storeName: "",
      terminalName: "",
    }),
    externalDisplay: Object.freeze({
      available: false,
      enabled: false,
      status: "unavailable",
    }),
    hardware: Object.freeze({
      printerStatus: "unavailable",
      scannerStatus: "unavailable",
      externalDisplayStatus: "unavailable",
      lastScannerValue: null,
    }),
    kind: access.canView ? "idle" : "unauthorized",
    linkly,
    linklyDraft: Object.freeze({
      environment: linkly.environment,
    }),
    paymentProvider: null,
    paymentProviderDraft: null,
    printer,
    printerDevices: Object.freeze([]),
    reregisterStoreCode: "",
    square,
    squareDraft: Object.freeze({
      deviceId: "",
      environment: square.environment,
      locationId: "",
    }),
    statusCode: access.canView ? null : "permission-required",
    terminalNameDraft: "",
  });
}

function normalizeSnapshot(snapshot: SettingsSnapshot): SettingsSnapshot {
  const linkly = Object.freeze({
    available: booleanValue(snapshot.linkly.available),
    blockerCode: safeBlockerCode(snapshot.linkly.blockerCode),
    environment: paymentEnvironment(snapshot.linkly.environment),
  });
  const square = Object.freeze({
    available: booleanValue(snapshot.square.available),
    blockerCode: safeBlockerCode(snapshot.square.blockerCode),
    environment: paymentEnvironment(snapshot.square.environment),
    deviceId: boundedPublicIdentifier(snapshot.square.deviceId),
    locationId: boundedPublicIdentifier(snapshot.square.locationId),
  });
  const requestedProvider = settingsPaymentProvider(snapshot.paymentProvider);
  const paymentProvider =
    requestedProvider &&
    isNormalizedPaymentProviderConfigured(requestedProvider, square, linkly)
      ? requestedProvider
      : null;
  return Object.freeze({
    apiBaseUrl: normalizeApiAddress(snapshot.apiBaseUrl),
    appUpdate: normalizeAppUpdate(snapshot.appUpdate),
    catalog: normalizeCatalog(snapshot.catalog),
    device: Object.freeze({
      deviceCode: snapshot.device.deviceCode.trim(),
      storeCode: snapshot.device.storeCode.trim(),
      storeName: snapshot.device.storeName.trim(),
      terminalName: snapshot.device.terminalName.trim(),
    }),
    externalDisplay: Object.freeze({
      available: booleanValue(snapshot.externalDisplay.available),
      enabled: booleanValue(snapshot.externalDisplay.enabled),
      status: externalDisplayStatus(snapshot.externalDisplay.status),
    }),
    hardware: Object.freeze({
      printerStatus: externalDisplayStatus(snapshot.hardware.printerStatus),
      scannerStatus: scannerStatus(snapshot.hardware.scannerStatus),
      externalDisplayStatus: externalDisplayStatus(
        snapshot.hardware.externalDisplayStatus,
      ),
      lastScannerValue: safeMaskedScannerValue(
        snapshot.hardware.lastScannerValue,
      ),
    }),
    linkly,
    paymentProvider,
    printer: normalizePrinterSettings(snapshot.printer),
    square,
  });
}

function normalizePaymentDraft(
  provider: SettingsPaymentProvider | null,
  square: SettingsPaymentDraft["square"],
  linkly: SettingsPaymentDraft["linkly"],
  squareAvailable: boolean,
  linklyAvailable: boolean,
): SettingsPaymentSettingsInput | null {
  if (provider === "square") {
    if (!squareAvailable) return null;
    const locationId = square.locationId.trim();
    const deviceId = square.deviceId.trim();
    if (!locationId || !deviceId) return null;
    return Object.freeze({
      provider,
      square: Object.freeze({
        environment: paymentEnvironment(square.environment),
        locationId: boundedPublicIdentifier(locationId),
        deviceId: boundedPublicIdentifier(deviceId),
      }),
      linkly: null,
    });
  }
  if (provider === "linkly") {
    if (!linklyAvailable) return null;
    return Object.freeze({
      provider,
      square: null,
      linkly: Object.freeze({
        environment: paymentEnvironment(linkly.environment),
      }),
    });
  }
  return null;
}

function normalizePrinterSettings(
  settings: ReceiptPrinterSettings,
): ReceiptPrinterSettings {
  return Object.freeze({
    printEnabled: booleanValue(settings.printEnabled),
    drawerEnabled: booleanValue(settings.drawerEnabled),
    peripheralId: settings.peripheralId
      ? boundedPublicIdentifier(settings.peripheralId)
      : null,
    paper: settings.paper === "58mm" ? "58mm" : "80mm",
    locale: settings.locale === "zh-CN" ? "zh-CN" : "en",
    brandName: boundedPublicText(settings.brandName, 120),
    storeName: boundedPublicText(settings.storeName, 120),
    address: boundedPublicText(settings.address, 240),
    phone: boundedPublicText(settings.phone, 60),
    abn: boundedPublicText(settings.abn, 32),
  });
}

function normalizeCatalog(
  catalog: SettingsCatalogSnapshot,
): SettingsCatalogSnapshot {
  if (!Number.isSafeInteger(catalog.itemCount) || catalog.itemCount < 0) {
    throw new Error("invalid catalog count");
  }
  return Object.freeze({
    snapshotId: catalog.snapshotId?.trim() || null,
    itemCount: catalog.itemCount,
    activatedAt: catalog.activatedAt?.trim() || null,
  });
}

function normalizeAppUpdate(
  update: SettingsAppUpdateSnapshot,
): SettingsAppUpdateSnapshot {
  return Object.freeze({
    channel: update.channel.trim() || "production",
    currentVersion: update.currentVersion.trim(),
    availableVersion: update.availableVersion?.trim() || null,
    updateRequired: update.updateRequired,
    restartAvailable: update.restartAvailable,
  });
}

function maskScannerValue(value: string): string {
  const characters = Array.from(value);
  const suffix =
    characters.length >= 8
      ? characters
          .slice(-4)
          .join("")
          .replace(/[^A-Za-z0-9]/gu, "•")
      : "";
  return `${suffix ? `••••${suffix}` : "••••"} · ${characters.length} chars`;
}

function currentPaymentSettings(
  state: SettingsState,
): SettingsPaymentSettingsInput | null {
  return normalizePaymentDraft(
    state.paymentProvider,
    {
      environment: state.square.environment,
      deviceId: state.square.deviceId,
      locationId: state.square.locationId,
    },
    { environment: state.linkly.environment },
    isPaymentProviderAvailable("square", state),
    isPaymentProviderAvailable("linkly", state),
  );
}

function paymentSettingsEqual(
  left: SettingsPaymentSettingsInput,
  right: SettingsPaymentSettingsInput,
): boolean {
  if (left.provider !== right.provider) return false;
  if (left.provider === "square" && right.provider === "square") {
    return (
      left.square.environment === right.square.environment &&
      left.square.deviceId === right.square.deviceId &&
      left.square.locationId === right.square.locationId
    );
  }
  return (
    left.provider === "linkly" &&
    right.provider === "linkly" &&
    left.linkly.environment === right.linkly.environment
  );
}

function isPaymentProviderAvailable(
  provider: SettingsPaymentProvider,
  state: Pick<SettingsState, "linkly" | "square">,
): boolean {
  const snapshot = provider === "square" ? state.square : state.linkly;
  return snapshot.available && snapshot.blockerCode === null;
}

function isNormalizedPaymentProviderConfigured(
  provider: SettingsPaymentProvider,
  square: SettingsSquareSnapshot,
  linkly: SettingsPaymentProviderSnapshot,
): boolean {
  if (
    !isPaymentProviderAvailable(provider, {
      square,
      linkly,
    })
  ) {
    return false;
  }
  return (
    provider === "linkly" ||
    (square.locationId.length > 0 && square.deviceId.length > 0)
  );
}

function settingsPaymentProvider(
  value: unknown,
): SettingsPaymentProvider | null {
  return value === "square" || value === "linkly" ? value : null;
}

function dangerousActionFailureCode(
  kind: SettingsDangerousConfirmation["kind"],
): SettingsStatusCode {
  switch (kind) {
    case "change-payment-settings":
      return "payment-settings-save-failed";
    case "reset-catalog":
      return "catalog-reset-failed";
    case "reregister-device":
      return "device-reregister-failed";
    default:
      return "restart-failed";
  }
}

function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase().replace(/\.$/u, "");
  return (
    normalized === "localhost" ||
    normalized.endsWith(".localhost") ||
    normalized === "[::1]" ||
    normalized === "::1" ||
    /^127(?:\.\d{1,3}){3}$/u.test(normalized)
  );
}

function booleanValue(value: unknown): boolean {
  if (typeof value !== "boolean") throw new Error("invalid boolean");
  return value;
}

function paymentEnvironment(value: unknown): PaymentEnvironment {
  if (value !== "Sandbox" && value !== "Production") {
    throw new Error("invalid payment environment");
  }
  return value;
}

function externalDisplayStatus(
  value: unknown,
): "connected" | "disconnected" | "unavailable" {
  if (
    value !== "connected" &&
    value !== "disconnected" &&
    value !== "unavailable"
  ) {
    throw new Error("invalid hardware status");
  }
  return value;
}

function scannerStatus(value: unknown): "ready" | "unavailable" {
  if (value !== "ready" && value !== "unavailable") {
    throw new Error("invalid scanner status");
  }
  return value;
}

function safeBlockerCode(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== "string" || !/^[a-z0-9-]{1,64}$/u.test(value)) {
    return "invalid-provider-config";
  }
  return value;
}

function boundedPublicIdentifier(value: unknown): string {
  if (
    typeof value !== "string" ||
    value.length > 128 ||
    /[\u0000-\u001F\u007F]/u.test(value)
  ) {
    throw new Error("invalid public identifier");
  }
  return value.trim();
}

function boundedPublicText(value: unknown, maxLength: number): string {
  if (
    typeof value !== "string" ||
    value.length > maxLength ||
    /[\u0000-\u001F\u007F]/u.test(value)
  ) {
    throw new Error("invalid public text");
  }
  return value.trim();
}

function safeMaskedScannerValue(value: unknown): string | null {
  if (value === null) return null;
  if (
    typeof value !== "string" ||
    !/^••••(?:[A-Za-z0-9•]{4})? · \d{1,3} chars$/u.test(value)
  ) {
    return null;
  }
  return value;
}

function normalizePrinterDevice(
  device: SettingsPrinterDevice,
): SettingsPrinterDevice {
  return Object.freeze({
    id: boundedPublicIdentifier(device.id),
    name: boundedPublicText(device.name, 120),
    transport: boundedPublicText(device.transport, 32),
  });
}
