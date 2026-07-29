import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  SETTINGS_APP_UPDATE_PERMISSION,
  SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
  SETTINGS_CATALOG_RESET_PERMISSION,
  SETTINGS_CUSTOMER_DISPLAY_PERMISSION,
  SETTINGS_DEVICE_REGISTRATION_PERMISSION,
  SETTINGS_MIN_TOUCH_TARGET,
  SETTINGS_PAYMENT_TERMINAL_PERMISSION,
  SETTINGS_RECEIPT_PRINTER_PERMISSION,
  SETTINGS_VIEW_PERMISSION,
  SettingsPresenter,
  SettingsScreen,
  type SettingsControlPort,
  type SettingsDangerousConfirmation,
  type SettingsDangerousActionResult,
  type SettingsPaymentSettingsInput,
  type SettingsSnapshot,
} from "./index";

import {
  DEFAULT_RECEIPT_PRINTER_SETTINGS,
  type ReceiptPrinterSettings,
} from "@/core/db/pos-settings-repository";
import { posColors } from "@/ui/theme";

const presenters: SettingsPresenter[] = [];

afterEach(() => {
  for (const presenter of presenters.splice(0)) presenter.destroy();
  jest.restoreAllMocks();
});

describe("SettingsScreen", () => {
  it("呈现横屏中英双语五分区，并保持导航与主要操作至少 44pt", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const onBack = jest.fn();
    const screen = await render(
      <SettingsScreen onBack={onBack} presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByText(/设置.*Settings/i)).toBeTruthy();
    expect(
      StyleSheet.flatten(screen.getByTestId("settings-workspace").props.style)
        .flexDirection,
    ).toBe("row");
    expect(screen.getAllByTestId(/^settings-nav-/)).toHaveLength(5);

    for (const testID of [
      "settings-back",
      "settings-nav-general",
      "settings-nav-payments",
      "settings-nav-peripherals",
      "settings-nav-device",
      "settings-nav-hardware",
      "settings-catalog-download",
      "settings-api-request-change",
    ]) {
      expect(
        StyleSheet.flatten(screen.getByTestId(testID).props.style).minHeight,
      ).toBeGreaterThanOrEqual(SETTINGS_MIN_TOUCH_TARGET);
    }

    await fireEvent.press(screen.getByTestId("settings-back"));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it("支付页只编辑公开 Square/Linkly 选择，不出现 token、secret 或密码输入", async () => {
    const port = new ScreenSettingsPort();
    port.snapshotValue = { ...snapshot(), paymentProvider: null };
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(<SettingsScreen presenter={presenter} />);
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.press(screen.getByTestId("settings-nav-payments"));
    await waitFor(() =>
      expect(screen.getByTestId("settings-pane-content-payments")).toBeTruthy(),
    );
    expect(screen.getAllByText("Square")).toHaveLength(2);
    expect(screen.getAllByText("Linkly")).toHaveLength(2);
    expect(screen.queryByText(/token/i)).toBeNull();
    expect(screen.queryByText(/secret/i)).toBeNull();
    expect(screen.queryByText(/密码|password/i)).toBeNull();
    expect(
      screen.getByText(
        /必须明确选择一个终端提供方.*Select exactly one terminal provider/i,
      ),
    ).toBeTruthy();
    expect(
      screen.getByTestId("settings-payment-provider-square").props
        .accessibilityState.selected,
    ).toBe(false);
    expect(
      screen.getByTestId("settings-payment-provider-linkly").props
        .accessibilityState.selected,
    ).toBe(false);

    await fireEvent.press(screen.getByTestId("settings-payment-save"));
    expect(screen.queryByTestId("settings-confirmation")).toBeNull();
    await screen.findByText(/\[payment-settings-invalid\]/);

    await fireEvent.press(
      screen.getByTestId("settings-payment-provider-square"),
    );
    await fireEvent.press(screen.getByTestId("settings-square-sandbox"));
    await fireEvent.changeText(
      screen.getByTestId("settings-square-location"),
      "location-2",
    );
    await fireEvent.changeText(
      screen.getByTestId("settings-square-device"),
      "device-2",
    );
    await fireEvent.press(screen.getByTestId("settings-linkly-sandbox"));
    await fireEvent.press(screen.getByTestId("settings-payment-save"));

    await screen.findByTestId("settings-confirmation");
    expect(port.savedPayments).toEqual([]);
    await fireEvent.press(screen.getByTestId("settings-confirm"));

    expect(port.savedPayments).toEqual([
      {
        provider: "square",
        square: {
          environment: "Sandbox",
          locationId: "location-2",
          deviceId: "device-2",
        },
        linkly: null,
      },
    ]);
    await screen.findByText(/\[payment-settings-saved\]/);
    expect(
      screen.getByLabelText("Square 门店位置 ID / Square location ID"),
    ).toBeTruthy();
    expect(
      screen.getByLabelText("Square 终端设备 ID / Square device ID"),
    ).toBeTruthy();
  });

  it("危险操作显示明确确认，确认后才调用端口", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(<SettingsScreen presenter={presenter} />);
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.changeText(
      screen.getByTestId("settings-api-address"),
      "https://staging.example.com/pos-api/",
    );
    await fireEvent.press(screen.getByTestId("settings-api-request-change"));

    await waitFor(() =>
      expect(screen.getByTestId("settings-confirmation")).toBeTruthy(),
    );
    expect(
      screen.getByTestId("settings-confirmation-modal").props.visible,
    ).toBe(true);
    expect(
      screen.getByTestId("settings-nav-payments").props.accessibilityState
        .disabled,
    ).toBe(true);
    expect(screen.getByText(/待同步数据.*不会被清除/)).toBeTruthy();
    expect(port.apiAddresses).toEqual([]);

    await fireEvent.press(screen.getByTestId("settings-confirm"));
    await waitFor(() =>
      expect(port.apiAddresses).toEqual([
        "https://staging.example.com/pos-api",
      ]),
    );
    await screen.findByText(/\[api-address-saved\]/);
  });

  it("外设与硬件测试页可扫描/连接打印机并测试打印、扫码和客显", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(<SettingsScreen presenter={presenter} />);
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.press(screen.getByTestId("settings-nav-peripherals"));
    await screen.findByTestId("settings-pane-content-peripherals");
    await fireEvent.press(screen.getByTestId("settings-printer-scan"));
    await screen.findByText(/\[printer-scan-finished\]/);
    await screen.findByTestId("settings-printer-device-printer-2");
    await fireEvent.press(
      screen.getByTestId("settings-printer-connect-printer-2"),
    );
    await screen.findByText(/\[printer-connected\]/);
    expect(port.connectedPrinters).toEqual(["printer-2"]);

    await fireEvent.press(screen.getByTestId("settings-nav-hardware"));
    await screen.findByTestId("settings-pane-content-hardware");
    await fireEvent.press(screen.getByTestId("settings-hardware-printer"));
    await screen.findByText(/\[printer-test-passed\]/);
    await fireEvent.press(screen.getByTestId("settings-hardware-scanner"));
    await screen.findByText(/\[scanner-test-passed\]/);
    await fireEvent.press(screen.getByTestId("settings-hardware-display"));
    await screen.findByText(/\[display-test-passed\]/);

    expect(port.printerTests).toBe(1);
    expect(port.scannerTests).toBe(1);
    expect(port.displayTests).toBe(1);
    expect(screen.getByText("••••0001 · 12 chars")).toBeTruthy();
    expect(screen.queryByText("930000000001")).toBeNull();
  });

  it("不可用硬件不显示绿色健康状态，关键配置输入具备双语标签", async () => {
    const port = new ScreenSettingsPort();
    const current = snapshot();
    port.snapshotValue = {
      ...current,
      hardware: {
        ...current.hardware,
        scannerStatus: "unavailable",
      },
    };
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(<SettingsScreen presenter={presenter} />);

    await fireEvent.press(screen.getByTestId("settings-nav-peripherals"));
    await screen.findByTestId("settings-pane-content-peripherals");
    expect(
      screen.getByLabelText("打印机设备 ID / Printer peripheral ID"),
    ).toBeTruthy();

    await fireEvent.press(screen.getByTestId("settings-nav-device"));
    await screen.findByTestId("settings-pane-content-device");
    expect(
      screen.getByLabelText("目标门店代码 / Target store code"),
    ).toBeTruthy();
    expect(screen.getByLabelText("终端名称 / Terminal name")).toBeTruthy();

    await fireEvent.press(screen.getByTestId("settings-nav-hardware"));
    const scannerStatus = screen.getByTestId(
      "settings-hardware-scanner-status",
    );
    expect(StyleSheet.flatten(scannerStatus.props.style).color).toBe(
      posColors.mutedInk,
    );
    expect(StyleSheet.flatten(scannerStatus.props.style).color).not.toBe(
      posColors.green,
    );
  });
});

function createPresenter(port: ScreenSettingsPort): SettingsPresenter {
  const presenter = new SettingsPresenter({
    permissions: [
      SETTINGS_VIEW_PERMISSION,
      SETTINGS_PAYMENT_TERMINAL_PERMISSION,
      SETTINGS_RECEIPT_PRINTER_PERMISSION,
      SETTINGS_CATALOG_DOWNLOAD_PERMISSION,
      SETTINGS_CATALOG_RESET_PERMISSION,
      SETTINGS_DEVICE_REGISTRATION_PERMISSION,
      SETTINGS_APP_UPDATE_PERMISSION,
      SETTINGS_CUSTOMER_DISPLAY_PERMISSION,
    ],
    port,
  });
  presenters.push(presenter);
  return presenter;
}

class ScreenSettingsPort implements SettingsControlPort {
  public readonly apiAddresses: string[] = [];
  public readonly connectedPrinters: string[] = [];
  public readonly savedPayments: SettingsPaymentSettingsInput[] = [];
  public printerTests = 0;
  public scannerTests = 0;
  public displayTests = 0;
  public snapshotValue: SettingsSnapshot | null = null;

  public async loadSnapshot(): Promise<SettingsSnapshot> {
    return this.snapshotValue ?? snapshot();
  }

  public async downloadCatalog() {
    return {
      snapshotId: "catalog-new",
      itemCount: 20,
      activatedAt: "2026-07-28T03:00:00.000Z",
    };
  }

  public async executeDangerousAction(
    action: SettingsDangerousConfirmation,
  ): Promise<SettingsDangerousActionResult> {
    if (action.kind === "change-api-address") {
      this.apiAddresses.push(action.apiBaseUrl);
      return { status: "completed", kind: action.kind };
    }
    if (action.kind === "change-payment-settings") {
      this.savedPayments.push(action.input);
      return { status: "completed", kind: action.kind };
    }
    if (action.kind === "reset-catalog") {
      return {
        status: "completed",
        kind: action.kind,
        catalog: { snapshotId: null, itemCount: 0, activatedAt: null },
      };
    }
    return { status: "completed", kind: action.kind };
  }

  public async testPaymentProvider(): Promise<void> {}

  public async savePrinterSettings(
    _settings: ReceiptPrinterSettings,
  ): Promise<void> {}

  public async scanPrinters() {
    return [{ id: "printer-2", name: "Counter printer", transport: "usb" }];
  }

  public async connectPrinter(peripheralId: string): Promise<void> {
    this.connectedPrinters.push(peripheralId);
  }

  public async testPrinter(): Promise<void> {
    this.printerTests += 1;
  }

  public async testScanner() {
    this.scannerTests += 1;
    return { source: "hid" as const, value: "930000000001" };
  }

  public async setExternalDisplayEnabled(): Promise<void> {}

  public async testExternalDisplay(): Promise<void> {
    this.displayTests += 1;
  }

  public async checkForAppUpdate() {
    return snapshot().appUpdate;
  }
}

function snapshot(): SettingsSnapshot {
  return {
    apiBaseUrl: "https://hotbargain.vip/pos-api",
    appUpdate: {
      channel: "production",
      currentVersion: "1.0.0",
      availableVersion: null,
      updateRequired: false,
      restartAvailable: false,
    },
    catalog: {
      snapshotId: "catalog-current",
      itemCount: 10,
      activatedAt: "2026-07-27T03:00:00.000Z",
    },
    device: {
      deviceCode: "POS-01",
      storeCode: "BNE-01",
      storeName: "Brisbane",
      terminalName: "Front",
    },
    externalDisplay: {
      available: true,
      enabled: false,
      status: "connected",
    },
    hardware: {
      printerStatus: "connected",
      scannerStatus: "ready",
      externalDisplayStatus: "connected",
      lastScannerValue: null,
    },
    paymentProvider: "square",
    linkly: {
      available: true,
      blockerCode: null,
      environment: "Production",
    },
    printer: {
      ...DEFAULT_RECEIPT_PRINTER_SETTINGS,
      printEnabled: true,
      peripheralId: "printer-1",
    },
    square: {
      available: true,
      blockerCode: null,
      environment: "Production",
      deviceId: "device-1",
      locationId: "location-1",
    },
  };
}
