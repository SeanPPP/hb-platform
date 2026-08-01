import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  jest,
} from "@jest/globals";
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
  SettingsUnavailableScreen,
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
import { usePosSound } from "@/ui/feedback/pos-sound-context";
import { posColors } from "@/ui/theme";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.mock("@/ui/feedback/pos-sound-context", () => ({
  usePosSound: jest.fn(),
}));

const mockPlay = jest.fn();
const mockSetButtonSoundEnabled = jest.fn();
const mockSetSpecialNodeSoundEnabled = jest.fn();

const presenters: SettingsPresenter[] = [];

beforeEach(() => {
  jest.clearAllMocks();
  jest.mocked(usePosSound).mockReturnValue({
    buttonSoundEnabled: true,
    play: mockPlay,
    setButtonSoundEnabled: mockSetButtonSoundEnabled,
    setSpecialNodeSoundEnabled: mockSetSpecialNodeSoundEnabled,
    specialNodeSoundEnabled: false,
  });
});

afterEach(() => {
  for (const presenter of presenters.splice(0)) presenter.destroy();
  jest.restoreAllMocks();
});

describe("SettingsScreen", () => {
  it("运行时不可用页按当前语言显示", async () => {
    const onBack = jest.fn();
    const screen = await render(
      <SettingsUnavailableScreen locale="zh" onBack={onBack} />,
    );
    expect(screen.getByText("设置服务暂不可用")).toBeTruthy();
    expect(screen.queryByText("Settings unavailable")).toBeNull();
  });

  it("英文界面只呈现英文五分区，并保持导航与主要操作至少 44pt", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const onBack = jest.fn();
    const screen = await render(
      <SettingsScreen locale="en" onBack={onBack} presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByText("Settings")).toBeTruthy();
    expect(screen.queryByText("设置")).toBeNull();
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

  it("普通按钮与特殊节点音效以两个独立开关呈现，并提供中英文说明", async () => {
    const presenter = createPresenter(new ScreenSettingsPort());
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="en" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByText("Sound feedback")).toBeTruthy();
    expect(
      screen.getByText(
        "Covers buttons, keys, page navigation and dangerous actions. Turn it on to hear a sample; turn it off to keep them silent.",
      ),
    ).toBeTruthy();
    expect(
      screen.getByText(
        "Covers product lookup, cart add/increment, not-found and blocked results. Turn it on to hear a sample; turn it off to keep them silent.",
      ),
    ).toBeTruthy();

    const buttonSound = screen.getByTestId("settings-button-sound");
    const specialNodeSound = screen.getByTestId(
      "settings-special-node-sound",
    );
    expect(buttonSound.props).toEqual(
      expect.objectContaining({
        accessibilityLabel: "Button sounds",
        accessibilityRole: "switch",
        accessibilityState: { checked: true },
        value: true,
      }),
    );
    expect(specialNodeSound.props).toEqual(
      expect.objectContaining({
        accessibilityLabel: "Special event sounds",
        accessibilityRole: "switch",
        accessibilityState: { checked: false },
        value: false,
      }),
    );
    for (const control of [buttonSound, specialNodeSound]) {
      expect(StyleSheet.flatten(control.props.style).minHeight).toBeGreaterThanOrEqual(
        SETTINGS_MIN_TOUCH_TARGET,
      );
    }

    await fireEvent(buttonSound, "valueChange", false);
    await fireEvent(specialNodeSound, "valueChange", true);
    expect(mockSetButtonSoundEnabled).toHaveBeenCalledWith(false);
    expect(mockSetSpecialNodeSoundEnabled).toHaveBeenCalledWith(true);
    expect(mockPlay).not.toHaveBeenCalled();

    await screen.rerender(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    expect(screen.getByText("音效反馈")).toBeTruthy();
    expect(screen.getByLabelText("普通按钮音效")).toBeTruthy();
    expect(screen.getByLabelText("特殊节点音效")).toBeTruthy();
    expect(
      screen.getByText(
        "覆盖按钮、按键、页面导航和危险操作；开启时播放一次示例，关闭后保持静音。",
      ),
    ).toBeTruthy();
    expect(
      screen.getByText(
        "覆盖商品查询、加购/累加、未找到和受阻结果；开启时播放一次示例，关闭后保持静音。",
      ),
    ).toBeTruthy();
  });

  it("当前终端卡片按分店名称、设备代码、分店代码展示完整身份", async () => {
    const storeName = "Brisbane Central Shopping Centre Flagship Store";
    const port = new ScreenSettingsPort();
    port.snapshotValue = {
      ...snapshot(),
      device: {
        ...snapshot().device,
        storeName,
      },
    };
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    const card = screen.getByTestId("settings-device-badge");
    expect(
      card.children
        .slice(1)
        .map((child) => typeof child === "string" ? child : child.props.testID),
    ).toEqual([
      "settings-device-store-name",
      "settings-device-code",
      "settings-device-store-code",
    ]);
    expect(
      screen.getByTestId("settings-device-store-name").props,
    ).toEqual(
      expect.objectContaining({
        accessibilityLabel: storeName,
        ellipsizeMode: "tail",
        numberOfLines: 2,
      }),
    );
    expect(screen.getByTestId("settings-device-code").props.children).toBe(
      "POS-01",
    );
    expect(screen.getByTestId("settings-device-store-code").props.children).toBe(
      "BNE-01",
    );
  });

  it("当前终端缺少分店名称时显示占位符且不拿分店代码回退", async () => {
    const port = new ScreenSettingsPort();
    port.snapshotValue = {
      ...snapshot(),
      device: {
        ...snapshot().device,
        storeName: "",
      },
    };
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(
      screen.getByTestId("settings-device-store-name").props.children,
    ).toBe("—");
    expect(
      screen.getByTestId("settings-device-store-name").props.children,
    ).not.toBe("BNE-01");
    expect(screen.getByTestId("settings-device-store-code").props.children).toBe(
      "BNE-01",
    );
  });

  it("本地后端快捷按钮只填入开发地址，仍需显式申请切换", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.press(screen.getByTestId("settings-api-use-local"));

    await waitFor(() =>
      expect(screen.getByTestId("settings-api-address").props.value).toBe(
        "http://192.168.31.246:5159",
      ),
    );
    expect(
      StyleSheet.flatten(
        screen.getByTestId("settings-api-use-local").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(SETTINGS_MIN_TOUCH_TARGET);
    expect(
      StyleSheet.flatten(screen.getByTestId("settings-api-actions").props.style)
        .flexWrap,
    ).toBe("wrap");
    expect(port.apiAddresses).toEqual([]);
    expect(screen.queryByTestId("settings-confirmation")).toBeNull();

    await fireEvent.press(screen.getByTestId("settings-api-request-change"));
    await screen.findByTestId("settings-confirmation");
    expect(screen.getByText(/http:\/\/192\.168\.31\.246:5159/)).toBeTruthy();
    expect(port.apiAddresses).toEqual([]);
  });

  it("远程后端快捷按钮只填入生产地址，仍需显式申请切换", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.press(screen.getByTestId("settings-api-use-remote"));

    await waitFor(() =>
      expect(screen.getByTestId("settings-api-address").props.value).toBe(
        "https://hotbargain.vip/pos-api",
      ),
    );
    expect(
      StyleSheet.flatten(
        screen.getByTestId("settings-api-use-remote").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(SETTINGS_MIN_TOUCH_TARGET);
    expect(port.apiAddresses).toEqual([]);
    expect(screen.queryByTestId("settings-confirmation")).toBeNull();

    await fireEvent.press(screen.getByTestId("settings-api-request-change"));
    await screen.findByTestId("settings-confirmation");
    expect(screen.getByText(/https:\/\/hotbargain\.vip\/pos-api/)).toBeTruthy();
    expect(port.apiAddresses).toEqual([]);
  });

  it("测试连接按钮检查候选地址并保留当前后端", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.press(screen.getByTestId("settings-api-use-local"));
    await fireEvent.press(screen.getByTestId("settings-api-test"));

    await waitFor(() =>
      expect(port.apiAddressTests).toEqual(["http://192.168.31.246:5159"]),
    );
    expect(screen.getByText(/连接成功/)).toBeTruthy();
    expect(port.apiAddresses).toEqual([]);
    expect(presenter.getState().apiBaseUrl).toBe(
      "https://hotbargain.vip/pos-api",
    );
    expect(screen.queryByTestId("settings-confirmation")).toBeNull();
  });

  it("危险操作确认弹窗只声明应用支持的横屏方向", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    await fireEvent.changeText(
      screen.getByTestId("settings-api-address"),
      "http://localhost:5159",
    );
    await fireEvent.press(screen.getByTestId("settings-api-request-change"));

    await screen.findByTestId("settings-confirmation");
    expect(
      screen.getByTestId("settings-confirmation-modal").props
        .supportedOrientations,
    ).toEqual(["landscape-left", "landscape-right"]);
  });

  it("设置目录卡片恢复共享真实进度，刷新中可返回但阻断下载、重置与 API 切换", async () => {
    const port = new ScreenSettingsPort();
    port.publishCatalogRefresh({
      kind: "running",
      storeCode: "BNE-01",
      progress: {
        currentStep: "products",
        overallPercent: 35,
        elapsedMilliseconds: 116_000,
        steps: [
          { step: "prepare", percent: 100 },
          {
            step: "products",
            percent: 25,
            completedItemCount: 500,
            totalItemCount: 2_000,
            completedPageCount: 1,
            totalPageCount: 4,
          },
          { step: "promotions", percent: 0 },
          { step: "activate", percent: 0 },
        ],
      },
    });
    port.snapshotValue = {
      ...snapshot(),
      appUpdate: {
        ...snapshot().appUpdate,
        restartAvailable: true,
      },
    };
    const presenter = createPresenter(port);
    await presenter.load();
    const onBack = jest.fn();
    const screen = await render(
      <SettingsScreen locale="zh" onBack={onBack} presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByTestId("settings-catalog-refresh-state")).toBeTruthy();
    expect(
      screen.getByTestId("settings-catalog-refresh-progress").props
        .accessibilityValue,
    ).toEqual({ min: 0, max: 100, now: 35 });
    expect(screen.getByText("当前步骤：下载并校验商品")).toBeTruthy();
    expect(screen.getByText("已用时：01:56")).toBeTruthy();
    expect(screen.getByText(/500 \/ 2000/)).toBeTruthy();
    expect(screen.getByText(/1 \/ 4 页/)).toBeTruthy();
    expect(screen.getByText(/可离开本页.*应用内继续刷新/)).toBeTruthy();
    expect(
      screen.getByTestId("settings-catalog-download").props.accessibilityState
        .disabled,
    ).toBe(true);
    expect(
      screen.getByTestId("settings-catalog-reset").props.accessibilityState
        .disabled,
    ).toBe(true);
    expect(
      screen.getByTestId("settings-api-request-change").props
        .accessibilityState.disabled,
    ).toBe(true);
    expect(
      screen.getByTestId("settings-update-restart").props.accessibilityState
        .disabled,
    ).toBe(true);
    expect(
      screen.getByTestId("settings-back").props.accessibilityState.disabled,
    ).toBe(false);

    await fireEvent.press(screen.getByTestId("settings-nav-payments"));
    await screen.findByTestId("settings-pane-content-payments");
    expect(
      screen.getByTestId("settings-payment-save").props.accessibilityState
        .disabled,
    ).toBe(true);

    await fireEvent.press(screen.getByTestId("settings-nav-device"));
    await screen.findByTestId("settings-pane-content-device");
    expect(
      screen.getByTestId("settings-reregister-request").props
        .accessibilityState.disabled,
    ).toBe(true);

    await fireEvent.press(screen.getByTestId("settings-back"));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it("重新进入设置后显示共享目录完成摘要与安全告警码", async () => {
    const port = new ScreenSettingsPort();
    port.publishCatalogRefresh({
      kind: "warning",
      storeCode: "BNE-01",
      summary: {
        snapshotId: "catalog-background",
        catalogVersion: "v-background",
        itemCount: 63,
        activatedAt: "2026-07-29T02:00:00.000Z",
      },
      warningCode: "catalog-runtime-reload-failed",
      progress: {
        currentStep: "activate",
        overallPercent: 100,
        elapsedMilliseconds: 121_000,
        steps: [
          { step: "prepare", percent: 100 },
          { step: "products", percent: 100 },
          { step: "promotions", percent: 100 },
          { step: "activate", percent: 100 },
        ],
      },
    });
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByText("catalog-background")).toBeTruthy();
    expect(screen.getByText("63")).toBeTruthy();
    expect(screen.getByText(/目录已启用，但有安全警告/)).toBeTruthy();
    expect(
      screen.getByText(/catalog-runtime-reload-failed/),
    ).toBeTruthy();
  });

  it("重新进入设置后显示共享目录失败摘要，保留旧目录且不泄露异常详情", async () => {
    const port = new ScreenSettingsPort();
    port.publishCatalogRefresh({
      kind: "failed",
      storeCode: "BNE-01",
      errorCode: "catalog-refresh-network-failed",
      progress: {
        currentStep: "prepare",
        overallPercent: 0,
        elapsedMilliseconds: 14_000,
        steps: [
          { step: "prepare", percent: 0 },
          { step: "products", percent: 0 },
          { step: "promotions", percent: 0 },
          { step: "activate", percent: 0 },
        ],
      },
    });
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
    await screen.findByTestId("settings-pane-content-general");

    expect(screen.getByText("catalog-current")).toBeTruthy();
    expect(screen.getByText(/目录刷新未完成，旧目录仍可用/)).toBeTruthy();
    expect(
      screen.getByText(/catalog-refresh-network-failed/),
    ).toBeTruthy();
    expect(screen.queryByText(/Bearer|https?:\/\//)).toBeNull();
  });

  it("支付页只编辑公开 Square/Linkly 选择，不出现 token、secret 或密码输入", async () => {
    const port = new ScreenSettingsPort();
    port.snapshotValue = { ...snapshot(), paymentProvider: null };
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="en" presenter={presenter} />,
    );
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
        "Select exactly one terminal provider; card payments remain disabled when no available provider is selected.",
      ),
    ).toBeTruthy();
    expect(screen.queryByText(/必须明确选择一个终端提供方/)).toBeNull();
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
    expect(screen.getByText("Payment settings saved")).toBeTruthy();
    expect(screen.queryByText("支付终端设置已保存")).toBeNull();
    expect(screen.getByLabelText("Square location ID")).toBeTruthy();
    expect(screen.getByLabelText("Square device ID")).toBeTruthy();
  });

  it("中文危险操作显示明确确认，确认后才调用端口", async () => {
    const port = new ScreenSettingsPort();
    const presenter = createPresenter(port);
    await presenter.load();
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );
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
    expect(
      screen.getByText("API 地址已保存；运行时必须按适配器指引重新建立。"),
    ).toBeTruthy();
    expect(
      screen.queryByText(
        "API address saved. The runtime must reconnect as directed by its adapter.",
      ),
    ).toBeNull();
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

  it("不可用硬件不显示绿色健康状态，关键配置输入使用中文标签", async () => {
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
    const screen = await render(
      <SettingsScreen locale="zh" presenter={presenter} />,
    );

    await fireEvent.press(screen.getByTestId("settings-nav-peripherals"));
    await screen.findByTestId("settings-pane-content-peripherals");
    expect(screen.getByLabelText("打印机设备 ID")).toBeTruthy();

    await fireEvent.press(screen.getByTestId("settings-nav-device"));
    await screen.findByTestId("settings-pane-content-device");
    expect(screen.getByLabelText("目标门店代码")).toBeTruthy();
    expect(screen.getByLabelText("终端名称")).toBeTruthy();

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
  public readonly apiAddressTests: string[] = [];
  public readonly connectedPrinters: string[] = [];
  public readonly savedPayments: SettingsPaymentSettingsInput[] = [];
  public printerTests = 0;
  public scannerTests = 0;
  public displayTests = 0;
  public snapshotValue: SettingsSnapshot | null = null;
  private catalogRefreshState: ReturnType<
    SettingsControlPort["getCatalogRefreshState"]
  > = { kind: "idle" };
  private readonly catalogRefreshListeners = new Set<() => void>();

  public async loadSnapshot(): Promise<SettingsSnapshot> {
    return this.snapshotValue ?? snapshot();
  }

  public getCatalogRefreshState() {
    return this.catalogRefreshState;
  }

  public subscribeCatalogRefresh(listener: () => void): () => void {
    this.catalogRefreshListeners.add(listener);
    return () => this.catalogRefreshListeners.delete(listener);
  }

  public publishCatalogRefresh(
    state: ReturnType<SettingsControlPort["getCatalogRefreshState"]>,
  ): void {
    this.catalogRefreshState = state;
    for (const listener of this.catalogRefreshListeners) listener();
  }

  public async downloadCatalog() {
    return {
      snapshotId: "catalog-new",
      itemCount: 20,
      activatedAt: "2026-07-28T03:00:00.000Z",
    };
  }

  public async testApiAddress(apiBaseUrl: string): Promise<boolean> {
    this.apiAddressTests.push(apiBaseUrl);
    return true;
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
