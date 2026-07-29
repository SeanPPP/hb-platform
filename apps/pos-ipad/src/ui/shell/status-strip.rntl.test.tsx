import { afterEach, expect, jest, test } from "@jest/globals";
import {
  cleanup,
  fireEvent,
  render,
} from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { usePosShellStore } from "./pos-shell-store";
import { PosStatusStrip } from "./status-strip";

let mockLanguage = "zh";

const mockTranslations: Readonly<Record<string, Readonly<Record<string, string>>>> =
  {
    en: {
      "status.languageSwitchHint": "Show all interface text in Chinese.",
      "status.languageSwitchLabel": "Switch interface language to Chinese",
    },
    zh: {
      "status.languageSwitchHint": "将所有界面文字显示为英文。",
      "status.languageSwitchLabel": "将界面语言切换为英文",
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

test("没有切换回调时不显示语言入口", async () => {
  const screen = await render(<PosStatusStrip language="zh" />);

  expect(screen.queryByTestId("status-strip-language-switch")).toBeNull();
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
