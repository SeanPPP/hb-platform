import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import {
  cleanup,
  fireEvent,
  render,
} from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { usePosShellStore } from "./pos-shell-store";
import { PosStatusStrip } from "./status-strip";

import { usePosSound } from "@/ui/feedback/pos-sound-context";

jest.mock("@/ui/feedback/pos-sound-context", () => ({ usePosSound: jest.fn() }));

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: ({ name }: { name: string }) => {
    const { Text } = jest.requireActual<typeof import("react-native")>(
      "react-native",
    );

    return <Text>{name}</Text>;
  },
}));

const mockUsePosSound = jest.mocked(usePosSound);
const play = jest.fn();

let mockLanguage = "zh";

const mockTranslations: Readonly<Record<string, Readonly<Record<string, string>>>> =
  {
    en: {
      "status.languageSwitchHint": "Show all interface text in Chinese.",
      "status.languageSwitchLabel": "Switch interface language to Chinese",
      "status.device": "Device",
      "status.device.authorized": "Authorized",
      "status.deviceCode": "Device code",
      "status.display": "Display",
      "status.network": "Network",
      "status.network.online": "Online",
      "status.peripheral.disconnected": "Disconnected",
      "status.printer": "Printer",
      "status.scanner": "Scanner",
      "status.scanner.inactive": "Unfocused",
      "status.storeName": "Branch name",
      "status.sync": "Sync",
      "status.sync.pending": "0 pending",
    },
    zh: {
      "status.languageSwitchHint": "将所有界面文字显示为英文。",
      "status.languageSwitchLabel": "将界面语言切换为英文",
      "status.device": "设备",
      "status.device.authorized": "已授权",
      "status.deviceCode": "设备代码",
      "status.storeName": "分店名称",
    },
  };

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: mockLanguage, resolvedLanguage: mockLanguage },
    t: (key: string, values?: Readonly<{ count?: number }>) =>
      mockTranslations[mockLanguage]?.[key] ??
      (key === "status.sync.pending" ? String(values?.count ?? 0) : key),
  }),
}));

afterEach(() => {
  cleanup();
  mockLanguage = "zh";
  usePosShellStore.getState().reset();
});

beforeEach(() => {
  mockUsePosSound.mockReturnValue({
    buttonSoundEnabled: true,
    play,
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: true,
  });
});

test("没有切换回调时不显示语言入口", async () => {
  const screen = await render(<PosStatusStrip language="zh" />);

  expect(screen.queryByTestId("status-strip-language-switch")).toBeNull();
});

test("终端身份默认关闭，即使 shell 已有展示身份也不影响其他调用方", async () => {
  usePosShellStore.getState().setTerminalPresentation({
    storeName: "Brisbane CBD",
    deviceCode: "IPAD-07",
  });

  const screen = await render(<PosStatusStrip language="zh" />);

  expect(screen.queryByTestId("status-strip-terminal-identity")).toBeNull();
});

test("开启后按分店名称、设备代码展示静态身份，并保留设备授权状态", async () => {
  usePosShellStore.getState().setDeviceGate("authorized");
  usePosShellStore.getState().setTerminalPresentation({
    storeName: "Brisbane Central Superstore With A Long Name",
    deviceCode: "IPAD-07",
  });
  const screen = await render(
    <PosStatusStrip
      language="zh"
      onSwitchLanguage={jest.fn()}
      showTerminalIdentity
    />,
  );

  const group = screen.getByTestId("status-strip-terminal-identity");
  expect(group.children).toHaveLength(2);
  expect(
    screen.getByTestId("status-strip-store-identity").props.accessibilityLabel,
  ).toBe(
    "分店名称: Brisbane Central Superstore With A Long Name",
  );
  expect(
    screen.getByTestId("status-strip-device-code-identity").props
      .accessibilityLabel,
  ).toBe("设备代码: IPAD-07");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("status-strip-store-identity").props.style,
    ).flexShrink,
  ).toBe(1);
  expect(
    StyleSheet.flatten(
      screen.getByTestId("status-strip-device-code-identity").props.style,
    ).flexShrink,
  ).toBe(0);
  expect(
    StyleSheet.flatten(
      screen.getByTestId("status-strip-language-switch").props.style,
    ).flexShrink,
  ).toBe(0);
  expect(screen.getByLabelText("设备: 已授权")).toBeTruthy();
});

test("终端身份空值显示破折号，且绝不以 storeCode 冒充分店名称", async () => {
  usePosShellStore.getState().setTerminalPresentation({
    storeName: null,
    deviceCode: "",
    storeCode: "S001",
  } as Parameters<
    ReturnType<typeof usePosShellStore.getState>["setTerminalPresentation"]
  >[0]);

  const screen = await render(
    <PosStatusStrip language="zh" showTerminalIdentity />,
  );

  expect(screen.getAllByText("—")).toHaveLength(2);
  expect(screen.queryByText("S001")).toBeNull();
  expect(screen.getByLabelText("分店名称: —")).toBeTruthy();
  expect(screen.getByLabelText("设备代码: —")).toBeTruthy();
});

test("英文状态栏用语义图标替代长分类标签，同时保留完整无障碍文案", async () => {
  mockLanguage = "en";
  const shell = usePosShellStore.getState();
  shell.setConnectivity("online");
  shell.setDeviceGate("authorized");
  shell.setDisplay("disconnected");
  shell.setPrinter("disconnected");
  shell.setScanner("inactive");
  shell.setTerminalPresentation({
    storeName: "Brisbane Central Superstore",
    deviceCode: "POS_1042_0547",
  });

  const screen = await render(
    <PosStatusStrip language="en" showTerminalIdentity />,
  );

  for (const icon of [
    "tablet",
    "store-outline",
    "identifier",
    "wifi",
    "sync",
    "printer-outline",
    "barcode-scan",
    "monitor",
  ]) {
    expect(screen.getByText(icon)).toBeTruthy();
  }
  for (const label of [
    "Device",
    "Branch name",
    "Device code",
    "Network",
    "Sync",
    "Printer",
    "Scanner",
    "Display",
  ]) {
    expect(screen.queryByText(label)).toBeNull();
  }
  expect(
    screen.getByLabelText("Branch name: Brisbane Central Superstore"),
  ).toBeTruthy();
  expect(screen.getByLabelText("Network: Online")).toBeTruthy();
});

test("中文界面显示目标 EN 图标，并保留完整无障碍文案和 44pt 触控目标", async () => {
  const onSwitchLanguage = jest.fn();
  const screen = await render(
    <PosStatusStrip
      language="zh-CN"
      onSwitchLanguage={onSwitchLanguage}
    />,
  );

  const button = screen.getByTestId("status-strip-language-switch");
  expect(button.props.accessibilityLabel).toBe("将界面语言切换为英文");
  expect(button.props.accessibilityHint).toBe("将所有界面文字显示为英文。");
  expect(
    screen.getByTestId("status-strip-language-icon").props.children,
  ).toBe("EN");
  expect(StyleSheet.flatten(button.props.style).minHeight).toBeGreaterThanOrEqual(
    44,
  );
  expect(StyleSheet.flatten(button.props.style).minWidth).toBeGreaterThanOrEqual(
    44,
  );
  expect(screen.queryByText("English")).toBeNull();

  fireEvent.press(button);
  expect(play).toHaveBeenCalledWith("navigate");
  expect(onSwitchLanguage).toHaveBeenCalledTimes(1);
});

test("英文界面显示目标中文字形图标，并使用英文无障碍文案", async () => {
  mockLanguage = "en";
  const screen = await render(
    <PosStatusStrip language="en-AU" onSwitchLanguage={jest.fn()} />,
  );

  const button = screen.getByTestId("status-strip-language-switch");
  expect(button.props.accessibilityLabel).toBe(
    "Switch interface language to Chinese",
  );
  expect(button.props.accessibilityHint).toBe(
    "Show all interface text in Chinese.",
  );
  expect(
    screen.getByTestId("status-strip-language-icon").props.children,
  ).toBe("中");
  expect(screen.queryByText("中文")).toBeNull();
});
