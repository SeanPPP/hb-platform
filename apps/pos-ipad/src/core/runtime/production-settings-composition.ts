import type { CurrentCashierSession } from "./current-cashier-session";
import type { RuntimePrinterAdapter } from "./lazy-printer-adapter";
import {
  ProductionSettingsControl,
} from "./production-settings-control";
import { createProductionSettingsRuntime } from "./production-settings-runtime";

import type { ExternalCustomerDisplayPort } from "@/core/contracts/external-display";
import type {
  ReceiptPrinterSettings,
} from "@/core/db/pos-settings-repository";
import type { CatalogRefreshState } from "@/features/catalog/catalog-refresh-coordinator";
import type { ActivePricingCartSession } from "@/features/sales/runtime";
import type {
  SettingsAppUpdateSnapshot,
  SettingsCatalogSnapshot,
  SettingsPaymentSettingsInput,
  SettingsPendingDataSnapshot,
  SettingsScannerTestResult,
  SettingsSnapshot,
} from "@/features/settings/settings-presenter";
import type { SettingsRuntimeFactory } from "@/features/settings/settings-runtime";

type TerminalScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

type SettingsDevicePresentation = Readonly<{
  deviceCode: string;
  storeCode: string;
  storeName: string;
  terminalName: string;
}>;

type PaymentAvailability = Readonly<{
  available: boolean;
  blockerCode: string | null;
}>;

export type ProductionSettingsCompositionInput = Readonly<{
  currentCashier: CurrentCashierSession;
  terminal: TerminalScope;
  activeCart: Pick<
    ActivePricingCartSession,
    "getSnapshot" | "runExclusive"
  >;
  apiBaseUrl: string;
  appVersion: string;
  updateChannel: string;
  createId(): string;
  readDevicePresentation(): Promise<SettingsDevicePresentation>;
  catalog: Readonly<{
    getActiveMetadata(): Promise<SettingsCatalogSnapshot | null>;
    getRefreshState(): CatalogRefreshState;
    subscribeRefresh(listener: () => void): () => void;
    runExclusive<T>(operation: () => Promise<T>): Promise<T>;
    download(signal: AbortSignal): Promise<SettingsCatalogSnapshot>;
    reset(signal: AbortSignal): Promise<SettingsCatalogSnapshot>;
  }>;
  receiptSettings: Readonly<{
    get(): Promise<ReceiptPrinterSettings>;
    save(input: ReceiptPrinterSettings): Promise<unknown>;
  }>;
  paymentConfiguration: Readonly<{
    current: SettingsPaymentSettingsInput | null;
    availability: Readonly<{
      square: PaymentAvailability;
      linkly: PaymentAvailability;
    }>;
    test(
      provider: "square" | "linkly",
      input: SettingsPaymentSettingsInput,
      signal: AbortSignal,
    ): Promise<void>;
    save(input: SettingsPaymentSettingsInput): Promise<void>;
  }>;
  pendingData: Readonly<{
    read(): Promise<Omit<SettingsPendingDataSnapshot, "hasActiveCart">>;
  }>;
  apiConfiguration: Readonly<{
    /**
     * 仅允许开发构建在调试 API 时跨过本地待处理门禁。
     */
    allowSwitchWithPendingLocalData?: boolean;
    probe(healthUrl: string, signal: AbortSignal): Promise<boolean>;
    save(apiBaseUrl: string): Promise<void>;
  }>;
  runtimeReload: Readonly<{
    reload(signal: AbortSignal): Promise<void>;
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
  printer: RuntimePrinterAdapter;
  scanner: Readonly<{
    status: "ready" | "unavailable";
    test(signal: AbortSignal): Promise<SettingsScannerTestResult>;
  }>;
  externalDisplay?: ExternalCustomerDisplayPort | undefined;
  appUpdate: Readonly<{
    snapshot(): SettingsAppUpdateSnapshot;
    check(signal: AbortSignal): Promise<SettingsAppUpdateSnapshot>;
    restart(signal: AbortSignal): Promise<boolean>;
  }>;
}>;

/**
 * 把设置页的公开参数、硬件测试和危险动作门禁接入真实 POS 组合根。页面只能拿到
 * 零参数 presenter factory；活动购物车、SQLCipher 风险计数和可信 cashier lease
 * 均保留在闭包中。
 */
export function createProductionSettingsComposition(
  input: ProductionSettingsCompositionInput,
): SettingsRuntimeFactory {
  const terminal = normalizeTerminal(input.terminal);
  let externalDisplayEnabled = input.externalDisplay !== undefined;
  let displayRevision = 0;

  const control = new ProductionSettingsControl({
    readSnapshot: async (signal) => {
      throwIfAborted(signal);
      const [device, catalog, printer, printerStatus, displayStatus] =
        await Promise.all([
          input.readDevicePresentation(),
          input.catalog.getActiveMetadata(),
          input.receiptSettings.get(),
          input.printer.getStatus(),
          input.externalDisplay?.getStatus() ??
            Promise.resolve("disconnected" as const),
        ]);
      throwIfAborted(signal);
      assertDeviceScope(device, terminal);
      return Object.freeze({
        apiBaseUrl: input.apiBaseUrl,
        appUpdate: input.appUpdate.snapshot(),
        catalog: catalog ?? emptyCatalog(),
        device,
        externalDisplay: {
          available: input.externalDisplay !== undefined,
          enabled: externalDisplayEnabled,
          status: displayStatus === "ready"
            ? "connected"
            : input.externalDisplay
              ? "disconnected"
              : "unavailable",
        },
        hardware: {
          printerStatus: printerStatus === "ready"
            ? "connected"
            : printerStatus === "unavailable"
              ? "unavailable"
              : "disconnected",
          scannerStatus: input.scanner.status,
          externalDisplayStatus: displayStatus === "ready"
            ? "connected"
            : input.externalDisplay
              ? "disconnected"
              : "unavailable",
          lastScannerValue: null,
        },
        linkly: {
          ...input.paymentConfiguration.availability.linkly,
          environment:
            input.paymentConfiguration.current?.linkly?.environment ??
            "Production",
        },
        paymentProvider:
          input.paymentConfiguration.current?.provider ?? null,
        printer,
        square: {
          ...input.paymentConfiguration.availability.square,
          environment:
            input.paymentConfiguration.current?.square?.environment ??
            "Production",
          deviceId:
            input.paymentConfiguration.current?.square?.deviceId ?? "",
          locationId:
            input.paymentConfiguration.current?.square?.locationId ?? "",
        },
      } satisfies SettingsSnapshot);
    },
    catalog: {
      getRefreshState: input.catalog.getRefreshState,
      subscribeRefresh: input.catalog.subscribeRefresh,
      runExclusive: input.catalog.runExclusive,
      download: async (signal) => {
        throwIfAborted(signal);
        const result = await input.catalog.download(signal);
        throwIfAborted(signal);
        return result;
      },
      reset: async (signal) => {
        throwIfAborted(signal);
        const result = await input.catalog.reset(signal);
        throwIfAborted(signal);
        return result;
      },
    },
    payments: {
      test: (provider, configuration, signal) =>
        input.paymentConfiguration.test(
          provider,
          configuration,
          signal,
        ),
    },
    paymentConfiguration: {
      save: (configuration) =>
        input.paymentConfiguration.save(configuration),
    },
    runtimeReload: input.runtimeReload,
    printer: {
      saveSettings: async (settings, signal) => {
        throwIfAborted(signal);
        await input.receiptSettings.save(settings);
        throwIfAborted(signal);
      },
      scan: async (signal) => {
        throwIfAborted(signal);
        const devices = await input.printer.scan(8_000);
        throwIfAborted(signal);
        return Object.freeze(
          devices.map((device) =>
            Object.freeze({
              id: device.id,
              name: device.name,
              transport: "bluetooth-le",
            }),
          ),
        );
      },
      connect: async (peripheralId, signal) => {
        throwIfAborted(signal);
        await input.printer.connect(peripheralId);
        throwIfAborted(signal);
      },
      test: async (signal) => {
        throwIfAborted(signal);
        const settings = await input.receiptSettings.get();
        if (!settings.printEnabled || !settings.peripheralId) {
          throw new Error("SETTINGS_PRINTER_NOT_CONFIGURED");
        }
        await input.printer.connect(settings.peripheralId);
        throwIfAborted(signal);
        const result = await input.printer.print(
          `settings-test:${requiredId(input.createId())}`,
          printerTestDocument(),
        );
        throwIfAborted(signal);
        if (result.status !== "printed") {
          throw Object.assign(
            new Error("SETTINGS_PRINTER_TEST_NOT_CONFIRMED"),
            {
              code:
                result.errorCode ??
                "SETTINGS_PRINTER_TEST_NOT_CONFIRMED",
            },
          );
        }
      },
    },
    scanner: input.scanner,
    display: {
      setEnabled: async (enabled, signal) => {
        throwIfAborted(signal);
        if (!input.externalDisplay) {
          throw new Error("SETTINGS_EXTERNAL_DISPLAY_UNAVAILABLE");
        }
        await input.externalDisplay.setEnabled(enabled);
        throwIfAborted(signal);
        externalDisplayEnabled = enabled;
      },
      test: async (signal) => {
        throwIfAborted(signal);
        if (!input.externalDisplay) {
          throw new Error("SETTINGS_EXTERNAL_DISPLAY_UNAVAILABLE");
        }
        displayRevision += 1;
        await input.externalDisplay.publish({
          revision: displayRevision,
          mode: "idle",
          items: [],
          gst: { currency: "AUD", cents: 0 },
          discount: { currency: "AUD", cents: 0 },
          total: { currency: "AUD", cents: 0 },
          change: { currency: "AUD", cents: 0 },
          advert: null,
        });
        throwIfAborted(signal);
      },
    },
    appUpdate: input.appUpdate,
    pendingData: {
      read: async (signal) => {
        throwIfAborted(signal);
        const durable = await input.pendingData.read();
        throwIfAborted(signal);
        return Object.freeze({
          ...durable,
          hasActiveCart:
            input.activeCart.getSnapshot().lines.length > 0,
        });
      },
    },
    apiConfiguration: input.apiConfiguration,
    device: input.device,
  });

  return createProductionSettingsRuntime({
    createSessionLease: () => input.currentCashier.createLease(),
    control,
    runDangerousExclusive: (operation) =>
      input.activeCart.runExclusive(async () => operation()),
  });
}

function normalizeTerminal(input: TerminalScope): TerminalScope {
  return Object.freeze({
    storeCode: requiredText(input.storeCode, "store code"),
    deviceCode: requiredText(input.deviceCode, "device code"),
  });
}

function assertDeviceScope(
  device: SettingsDevicePresentation,
  terminal: TerminalScope,
): void {
  if (
    requiredText(device.storeCode, "device store code") !==
      terminal.storeCode ||
    requiredText(device.deviceCode, "device code") !== terminal.deviceCode
  ) {
    throw new Error("SETTINGS_DEVICE_SCOPE_MISMATCH");
  }
}

function emptyCatalog(): SettingsCatalogSnapshot {
  return Object.freeze({
    snapshotId: null,
    itemCount: 0,
    activatedAt: null,
  });
}

function printerTestDocument(): Uint8Array {
  return Uint8Array.from([
    0x1b, 0x40,
    ...new TextEncoder().encode("HB POS PRINTER TEST\n"),
    ...new TextEncoder().encode("PRINT / CUT / DRAWER SAFE TEST\n\n"),
    0x1d, 0x56, 0x00,
  ]);
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(`Settings ${label} is required.`);
  return normalized;
}

function requiredId(value: string): string {
  const normalized = value.trim();
  if (!normalized || normalized.length > 160) {
    throw new Error("SETTINGS_OPERATION_ID_INVALID");
  }
  return normalized;
}

function throwIfAborted(signal: AbortSignal): void {
  if (signal.aborted) {
    throw Object.assign(
      new Error("Settings operation aborted."),
      { name: "AbortError" },
    );
  }
}
