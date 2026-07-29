import { expect, jest, test } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet, TextInput } from "react-native";

import {
  CashierLoginController,
  type CashierLoginRuntime,
  type CashierLoginStorePort,
} from "./cashier-login-controller";
import { CashierLoginScreen } from "./cashier-login-screen";

import { HbposApiError } from "@/core/api/hbpos-api";
import type { PosCashierSummary } from "@/core/runtime/production-pos-service-composition";

let mockStatusStripProps: any;

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: () => null,
}));

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: (props: unknown) => {
    mockStatusStripProps = props;
    return null;
  },
}));

function runtime(
  signIn: (barcode: string) => Promise<PosCashierSummary>,
): CashierLoginRuntime {
  return {
    state: { phase: "ready", device: "authorized-online" },
    services: { cashierSession: { signIn } },
  };
}

function cashier(): PosCashierSummary {
  return {
    cashierId: "C1",
    cashierName: "Cashier",
    deviceCode: "IPAD-1",
    permissions: [],
    source: "online",
    storeCode: "S1",
    userGuid: null,
  };
}

function store(): CashierLoginStorePort {
  return { clearActiveCashier: jest.fn(), setActiveCashier: jest.fn() };
}

test("登录屏把当前语言和切换回调传给状态条", async () => {
  const onSwitchLanguage = jest.fn();
  mockStatusStripProps = null;
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="en-AU"
      onSuccess={jest.fn()}
      onSwitchLanguage={onSwitchLanguage}
      runtime={runtime(async () => cashier())}
    />,
  );

  expect(mockStatusStripProps).toMatchObject({
    language: "en",
    onSwitchLanguage,
  });
  mockStatusStripProps.onSwitchLanguage();
  expect(onSwitchLanguage).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("手动输入和 HID 回车都经同一安全 controller 成功后才进入 sales", async () => {
  const onSuccess = jest.fn();
  const signIn = jest.fn(async () => cashier());
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onSuccess={onSuccess}
      runtime={runtime(signIn)}
    />,
  );
  const input = screen.getByTestId("cashier-login-barcode");
  expect(input.props.autoFocus).toBe(true);
  expect(input.props.showSoftInputOnFocus).toBe(false);
  await act(async () => {
    fireEvent.changeText(input, " HID-123 ");
  });
  await act(async () => {
    fireEvent(input, "submitEditing", { nativeEvent: { text: " HID-123 " } });
  });

  await waitFor(() => {
    expect(signIn.mock.calls[0]).toEqual(["HID-123"]);
    expect(signIn).toHaveBeenCalledTimes(1);
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });
});

test("扫码默认不弹软键盘，常驻键盘按钮可手动开启且失焦后复位", async () => {
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onSuccess={jest.fn()}
      runtime={runtime(async () => cashier())}
    />,
  );

  expect(screen.getByText("默认使用扫码枪；手动输入请点“键盘”。")).toBeTruthy();
  const keyboardButton = screen.getByTestId("cashier-login-show-keyboard");
  expect(keyboardButton.props.accessibilityRole).toBe("button");
  expect(keyboardButton.props.accessibilityLabel).toBe("键盘");
  expect(
    StyleSheet.flatten(keyboardButton.props.style).minHeight,
  ).toBeGreaterThanOrEqual(44);

  expect(
    screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
  ).toBe(false);
  await act(async () => {
    fireEvent.press(keyboardButton);
  });
  expect(
    screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
  ).toBe(true);

  jest.useFakeTimers();
  try {
    await act(async () => {
      fireEvent.press(keyboardButton);
    });
    expect(
      screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
    ).toBe(false);
    await act(() => {
      jest.runOnlyPendingTimers();
    });
    expect(
      screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
    ).toBe(true);
  } finally {
    jest.useRealTimers();
  }

  await act(async () => {
    fireEvent(screen.getByTestId("cashier-login-barcode"), "blur");
  });
  expect(
    screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
  ).toBe(false);
});

test("可见条码输入接管 HID 焦点，失焦和卸载都会释放", async () => {
  const onManualInputFocusChange = jest.fn();
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onManualInputFocusChange={onManualInputFocusChange}
      onSuccess={jest.fn()}
      runtime={runtime(async () => cashier())}
    />,
  );
  const input = screen.getByTestId("cashier-login-barcode");

  expect(input.props.autoFocus).toBe(true);
  await act(async () => {
    fireEvent(input, "focus");
  });
  expect(onManualInputFocusChange.mock.calls).toEqual([[true]]);

  await act(async () => {
    fireEvent(input, "blur");
  });
  expect(onManualInputFocusChange.mock.calls).toEqual([[true], [false]]);

  await act(async () => {
    fireEvent(input, "focus");
  });
  await screen.unmount();
  expect(onManualInputFocusChange.mock.calls).toEqual([
    [true],
    [false],
    [true],
    [false],
  ]);
});

test("HID Enter 登录待定时保持可见输入所有权并拒绝重复提交", async () => {
  let resolveSignIn: ((value: PosCashierSummary) => void) | undefined;
  const signIn = jest.fn(
    () =>
      new Promise<PosCashierSummary>((resolve) => {
        resolveSignIn = resolve;
      }),
  );
  const onManualInputFocusChange = jest.fn();
  const onSuccess = jest.fn();
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onManualInputFocusChange={onManualInputFocusChange}
      onSuccess={onSuccess}
      runtime={runtime(signIn)}
    />,
  );
  const input = screen.getByTestId("cashier-login-barcode");

  expect(input.props.submitBehavior).toBe("submit");
  await act(async () => {
    input.props.onFocus();
    input.props.onChangeText(" HID-PENDING ");
  });
  const activeInput = screen.getByTestId("cashier-login-barcode");
  await act(async () => {
    activeInput.props.onSubmitEditing();
    await Promise.resolve();
    expect(signIn).toHaveBeenCalledTimes(1);

    activeInput.props.onSubmitEditing();
    activeInput.props.onBlur();
    expect(signIn).toHaveBeenCalledTimes(1);
    expect(onManualInputFocusChange.mock.calls).toEqual([[true]]);

    resolveSignIn?.(cashier());
    await Promise.resolve();
  });
  expect(onSuccess).toHaveBeenCalledTimes(1);
});

test("登录失败在输入恢复 editable 后重新取得扫码焦点", async () => {
  let rejectSignIn: ((reason: Error) => void) | undefined;
  const signIn = jest.fn(
    () =>
      new Promise<PosCashierSummary>((_resolve, reject) => {
        rejectSignIn = reject;
      }),
  );
  const onManualInputFocusChange = jest.fn();
  const textInputPrototype = (
    TextInput as unknown as {
      prototype: {
        focus: jest.Mock;
        setNativeProps: jest.Mock;
      };
    }
  ).prototype;
  textInputPrototype.focus.mockClear();
  textInputPrototype.setNativeProps.mockClear();
  let editableWhenFocusRequested: boolean | undefined;
  textInputPrototype.focus.mockImplementationOnce(function (this: {
    props: { editable?: boolean };
  }) {
    editableWhenFocusRequested = this.props.editable;
  });
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onManualInputFocusChange={onManualInputFocusChange}
      onSuccess={jest.fn()}
      runtime={runtime(signIn)}
    />,
  );
  const input = screen.getByTestId("cashier-login-barcode");

  await act(async () => {
    input.props.onFocus();
    input.props.onChangeText(" HID-REJECT ");
  });
  const activeInput = screen.getByTestId("cashier-login-barcode");
  await act(async () => {
    activeInput.props.onSubmitEditing();
    await Promise.resolve();
  });
  expect(signIn).toHaveBeenCalledTimes(1);
  expect(screen.getByTestId("cashier-login-barcode").props.editable).toBe(
    false,
  );

  await act(async () => {
    screen.getByTestId("cashier-login-barcode").props.onBlur();
  });
  expect(onManualInputFocusChange.mock.calls).toEqual([[true]]);

  await act(async () => {
    rejectSignIn?.(new Error("rejected"));
    await Promise.resolve();
    await Promise.resolve();
  });

  await waitFor(() => {
    expect(textInputPrototype.focus).toHaveBeenCalledTimes(1);
  });
  expect(editableWhenFocusRequested).toBe(true);
  expect(textInputPrototype.setNativeProps).toHaveBeenCalledWith({
    showSoftInputOnFocus: false,
  });
  expect(
    screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
  ).toBe(false);
  expect(onManualInputFocusChange.mock.calls).toEqual([[true]]);
});

test("紧急二维码时钟回拨显示专用安全提示且不进入 sales", async () => {
  const onSuccess = jest.fn();
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="zh"
      onSuccess={onSuccess}
      runtime={runtime(async () => {
        throw new HbposApiError("rejected", {
          kind: "envelope",
          code: "EMERGENCY_CLOCK_ROLLBACK",
        });
      })}
    />,
  );
  await act(async () => {
    fireEvent.changeText(
      screen.getByTestId("cashier-login-barcode"),
      "HBPOSE2-signed",
    );
  });
  await act(async () => {
    fireEvent.press(screen.getByTestId("cashier-login-submit"));
  });

  await waitFor(() => {
    expect(screen.getByText(/系统时间早于可信时间/)).toBeTruthy();
    expect(onSuccess).not.toHaveBeenCalled();
  });
});

test("明确拒绝显示错误且不进入 sales", async () => {
  const onSuccess = jest.fn();
  const screen = await render(
    <CashierLoginScreen
      controller={new CashierLoginController(store())}
      language="en"
      onSuccess={onSuccess}
      runtime={runtime(async () => {
        throw new Error("rejected");
      })}
    />,
  );
  expect(
    screen.getByText('Scanner ready; tap "Keyboard" for manual entry.'),
  ).toBeTruthy();
  await act(async () => {
    fireEvent.press(screen.getByTestId("cashier-login-show-keyboard"));
  });
  expect(
    screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
  ).toBe(true);
  await act(async () => {
    fireEvent.changeText(screen.getByTestId("cashier-login-barcode"), "NOPE");
  });
  await act(async () => {
    fireEvent.press(screen.getByTestId("cashier-login-submit"));
  });

  await waitFor(() => {
    expect(screen.getByText(/not accepted/)).toBeTruthy();
    expect(
      screen.getByTestId("cashier-login-barcode").props.showSoftInputOnFocus,
    ).toBe(false);
    expect(onSuccess).not.toHaveBeenCalled();
  });
});
