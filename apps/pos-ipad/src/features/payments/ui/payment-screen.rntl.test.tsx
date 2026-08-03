import { expect, jest, test } from "@jest/globals";
import {
  act,
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import { Dimensions, StyleSheet } from "react-native";

import {
  PaymentPresenter,
  type PaymentConfirmOptions,
  type PaymentPresenterState,
  type PaymentScreenPresenter,
  type PaymentUiMethod,
} from "./payment-presenter";
import {
  PAYMENT_MIN_TOUCH_TARGET,
  PaymentScreen,
  formatAud,
  type PaymentReceiptPrintOutcome,
} from "./payment-screen";

import type {
  Money,
  PaymentProvider,
} from "@/core/contracts";
import {
  INSTALLMENTS_CREATE_PERMISSION,
} from "@/features/installments/installment-authorization";
import { InstallmentCheckoutPresenter } from "@/features/installments/installment-checkout-presenter";
import type { InstallmentWorkflowPort } from "@/features/installments/installment-presenter";
import type {
  LinklyOperatorPublicResult,
  LinklyOperatorRuntimePort,
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import {
  PAYMENT_PERMISSION,
  type PaymentCheckoutPublicSnapshot,
  type PaymentCheckoutRuntimePort,
} from "@/features/payments/runtime/payment-checkout-runtime";
import type {
  PaymentProviderAvailability,
} from "@/features/payments/runtime/payment-provider-registry";
import { installmentCreatePaymentEntry } from "@/features/payments/ui/unified-payment-entry";

let mockPrintStateWrite: ((value: unknown) => void) | null = null;

jest.mock("react", () => {
  const actual = jest.requireActual<typeof import("react")>("react");
  return {
    ...actual,
    useState: ((initialState: unknown) => {
      const [value, setValue] = actual.useState<unknown>(initialState);
      if (initialState !== "idle" || !mockPrintStateWrite) {
        return [value, setValue];
      }
      return [
        value,
        (nextValue: unknown) => {
          mockPrintStateWrite?.(nextValue);
          setValue(nextValue);
        },
      ];
    }) as typeof actual.useState,
  };
});

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.mock("@/ui/feedback", () => ({
  usePosSound: () => ({
    buttonSoundEnabled: false,
    play: jest.fn(),
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: false,
  }),
}));

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: () => null,
}));

test("付款内容滚动区采用系统键盘避让且金额输入仍禁用软键盘", async () => {
  const { presenter } = createUiPresenter({
    phase: "ready",
    providers: [
      providerAvailability("square"),
      providerAvailability("linkly-cloud"),
      providerAvailability("voucher"),
    ],
    selectedMethod: "cash",
    orderGuid: "order-ui-1",
    total: aud(1_000),
    remaining: aud(1_000),
  });
  const screen = await render(
    <PaymentScreen
      locale="en"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(screen.getByTestId("payment-content-scroll").props).toMatchObject({
    automaticallyAdjustKeyboardInsets: true,
    keyboardDismissMode: "interactive",
    keyboardShouldPersistTaps: "handled",
  });
  expect(screen.getByTestId("payment-amount").props.showSoftInputOnFocus).toBe(
    false,
  );
});

test("横屏付款台保持 44pt 触控，礼券输入只安全遮罩且成功后不回显", async () => {
  const runtime = new ScreenPaymentRuntime();
  runtime.startImpl = async () =>
    snapshot({
      status: "completed",
      remaining: aud(0),
      provider: "voucher",
      attemptId: "attempt-voucher-ui",
      tenders: [
        {
          tenderGuid: "tender-voucher-ui",
          method: "voucher",
          amount: aud(1_000),
          reversible: true,
        },
      ],
    });
  const onComplete = jest.fn();
  const presenter = screenPresenter(runtime);
  const screen = await render(
    <PaymentScreen
      locale="zh"
      onComplete={onComplete}
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  await waitFor(() =>
    expect(screen.getByTestId("payment-status-ready")).toBeTruthy(),
  );
  await fireEvent.press(screen.getByTestId("payment-key-1"));
  expect(screen.getByTestId("payment-amount").props.value).toBe("1");
  const voucherMethod = screen.getByTestId("payment-method-voucher");
  expect(
    StyleSheet.flatten(voucherMethod.props.style).minHeight,
  ).toBeGreaterThanOrEqual(PAYMENT_MIN_TOUCH_TARGET);
  await fireEvent.press(voucherMethod);

  const voucherInput = screen.getByTestId("payment-voucher-code");
  expect(voucherInput.props.secureTextEntry).toBe(true);
  await fireEvent.changeText(voucherInput, "VOUCHER-UI-SECRET");
  expect(screen.queryByText("VOUCHER-UI-SECRET")).toBeNull();
  expect(screen.getByTestId("payment-voucher-captured")).toBeTruthy();

  await fireEvent.press(screen.getByTestId("payment-submit"));
  await waitFor(() =>
    expect(screen.getByTestId("payment-status-success")).toBeTruthy(),
  );
  expect(runtime.startCalls).toHaveLength(1);
  expect(
    JSON.stringify(presenter.getState()).includes("VOUCHER-UI-SECRET"),
  ).toBe(false);
  expect(screen.queryByTestId("payment-voucher-code")).toBeNull();
  await fireEvent.press(screen.getByTestId("payment-complete"));
  expect(onComplete).toHaveBeenCalledWith("order-ui-1");
});

test("支付成功切换为独立结算页，显示现金找零与小票动作并隐藏付款控件", async () => {
  const { presenter } = createUiPresenter({
    phase: "success",
    providers: [
      {
        provider: "square",
        available: false,
        blocker: "SQUARE_CONFIGURATION_MISSING",
      },
      providerAvailability("linkly-cloud"),
      providerAvailability("voucher"),
    ],
    selectedMethod: "cash",
    orderGuid: "order-ui-1",
    total: aud(1_000),
    remaining: aud(0),
    tenders: [
      {
        tenderGuid: "cash-success",
        method: "cash",
        amount: aud(1_000),
        reversible: true,
      },
    ],
    allowedActions: actions(),
    checkout: {
      flow: "regular",
      lines: [
        {
          lineKey: "open-item-success",
          displayName: "OPEN ITEM",
          quantity: "1",
          actualAmountCents: 1_000,
        },
      ],
      installmentCustomer: null,
      cash: {
        tenderedCents: 1_500,
        appliedCents: 1_000,
        changeCents: 500,
      },
      canConfirm: false,
      fullInstallmentConfirmationRequired: false,
    },
  });
  const onComplete = jest.fn();
  const onPrintReceipt = jest.fn<
    (orderGuid: string) => Promise<PaymentReceiptPrintOutcome>
  >(async () => "completed");
  const screen = await render(
    <PaymentScreen
      locale="zh"
      onComplete={onComplete}
      onPrintReceipt={onPrintReceipt}
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(screen.getByTestId("payment-success-layout")).toBeTruthy();
  expect(screen.getByText("支付完成")).toBeTruthy();
  expect(screen.getAllByText("order-ui-1")).toHaveLength(2);
  expect(screen.getByText(formatAud(1_500, "zh"))).toBeTruthy();
  expect(screen.getByText(formatAud(500, "zh"))).toBeTruthy();
  expect(screen.getByText("订单已安全保存，并进入同步队列。")).toBeTruthy();
  expect(screen.getByTestId("payment-success-receipt-preview")).toBeTruthy();
  expect(screen.getByText("OPEN ITEM")).toBeTruthy();
  expect(screen.getByText("× 1")).toBeTruthy();
  expect(screen.getByText("打印小票")).toBeTruthy();
  expect(screen.getByText("开始下一单")).toBeTruthy();

  expect(screen.queryByTestId("payment-entry-pane")).toBeNull();
  expect(screen.queryByTestId("payment-summary")).toBeNull();
  expect(screen.queryByTestId("payment-method-cash")).toBeNull();
  expect(screen.queryByTestId("payment-method-square")).toBeNull();
  expect(screen.queryByTestId("payment-method-linkly-cloud")).toBeNull();
  expect(screen.queryByTestId("payment-method-voucher")).toBeNull();
  expect(screen.queryByTestId("payment-provider-blockers")).toBeNull();
  expect(screen.queryByTestId("payment-remove-cash-success")).toBeNull();

  await fireEvent.press(screen.getByTestId("payment-success-print"));
  expect(onPrintReceipt).toHaveBeenCalledWith("order-ui-1");
  await fireEvent.press(screen.getByTestId("payment-complete"));
  expect(onComplete).toHaveBeenCalledWith("order-ui-1");
  await screen.unmount();
});

test("手动打印在结果返回前防重复，并依次显示完成、未知和失败结果", async () => {
  const { presenter } = createUiPresenter({
    phase: "success",
    orderGuid: "order-print-state",
    total: aud(1_000),
    remaining: aud(0),
    tenders: [
      {
        tenderGuid: "cash-print-state",
        method: "cash",
        amount: aud(1_000),
        reversible: true,
      },
    ],
    allowedActions: actions(),
  });
  const pending = createDeferred<"completed">();
  const onPrintReceipt = jest.fn<
    (orderGuid: string) => Promise<PaymentReceiptPrintOutcome>
  >(() => pending.promise);
  const screen = await render(
    <PaymentScreen
      locale="zh"
      onPrintReceipt={onPrintReceipt}
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  const print = screen.getByTestId("payment-success-print");
  await fireEvent.press(print);
  await fireEvent.press(print);
  expect(onPrintReceipt).toHaveBeenCalledTimes(1);
  expect(onPrintReceipt).toHaveBeenCalledWith("order-print-state");
  expect(print.props.accessibilityState).toEqual({ disabled: true });
  expect(screen.getByText("正在打印小票…")).toBeTruthy();

  pending.resolve("completed");
  await waitFor(() =>
    expect(screen.getByText("小票打印完成。")).toBeTruthy(),
  );
  expect(
    screen.getByTestId("payment-success-print").props.accessibilityState,
  ).toEqual({ disabled: false });

  onPrintReceipt.mockResolvedValueOnce("unknown");
  await fireEvent.press(screen.getByTestId("payment-success-print"));
  await waitFor(() =>
    expect(
      screen.getByText("打印结果未知，请先检查打印机再决定是否重试。"),
    ).toBeTruthy(),
  );

  onPrintReceipt.mockResolvedValueOnce("failed");
  await fireEvent.press(screen.getByTestId("payment-success-print"));
  await waitFor(() =>
    expect(
      screen.getByText("小票打印失败，请检查打印机后重试。"),
    ).toBeTruthy(),
  );
  expect(onPrintReceipt).toHaveBeenCalledTimes(3);
  await screen.unmount();
});

test("pending 打印在成功页卸载后不得再写 React 状态", async () => {
  const { presenter } = createUiPresenter({
    phase: "success",
    orderGuid: "order-print-unmount",
    total: aud(1_000),
    remaining: aud(0),
    allowedActions: actions(),
  });
  const pending = createDeferred<PaymentReceiptPrintOutcome>();
  const onPrintReceipt = jest.fn(() => pending.promise);
  const printStateWrites = jest.fn<(value: unknown) => void>();
  mockPrintStateWrite = printStateWrites;

  try {
    const screen = await render(
      <PaymentScreen
        locale="zh"
        onPrintReceipt={onPrintReceipt}
        presenter={presenter}
        showStatusStrip={false}
      />,
    );
    await fireEvent.press(screen.getByTestId("payment-success-print"));
    expect(printStateWrites).toHaveBeenCalledWith("printing");
    printStateWrites.mockClear();

    await screen.unmount();
    await act(async () => {
      pending.resolve("completed");
      await pending.promise;
    });
    expect(printStateWrites).not.toHaveBeenCalled();
  } finally {
    mockPrintStateWrite = null;
  }
});

test("pending 打印切换订单时清空状态并忽略旧订单迟到结果", async () => {
  const first = createUiPresenter({
    phase: "success",
    orderGuid: "order-print-first",
    total: aud(1_000),
    remaining: aud(0),
    allowedActions: actions(),
  });
  const second = createUiPresenter({
    phase: "success",
    orderGuid: "order-print-second",
    total: aud(2_000),
    remaining: aud(0),
    allowedActions: actions(),
  });
  const pending = createDeferred<PaymentReceiptPrintOutcome>();
  const onPrintReceipt = jest.fn(() => pending.promise);
  const screen = await render(
    <PaymentScreen
      locale="zh"
      onPrintReceipt={onPrintReceipt}
      presenter={first.presenter}
      showStatusStrip={false}
    />,
  );

  await fireEvent.press(screen.getByTestId("payment-success-print"));
  expect(screen.getByText("正在打印小票…")).toBeTruthy();
  await screen.rerender(
    <PaymentScreen
      locale="zh"
      onPrintReceipt={onPrintReceipt}
      presenter={second.presenter}
      showStatusStrip={false}
    />,
  );
  await waitFor(() => {
    expect(screen.getAllByText("order-print-second")).toHaveLength(2);
    expect(screen.queryByTestId("payment-success-print-status")).toBeNull();
  });

  await act(async () => {
    pending.resolve("completed");
    await pending.promise;
  });
  expect(screen.queryByText("小票打印完成。")).toBeNull();
  expect(
    screen.getByTestId("payment-success-print").props.accessibilityState,
  ).toEqual({ disabled: false });
  await screen.unmount();
});

test("Linkly 支付成功仍保留回执确认与终端确认入口", async () => {
  const { presenter, spies } = createUiPresenter({
    phase: "success",
    selectedMethod: "linkly-cloud",
    orderGuid: "order-linkly-success",
    total: aud(1_000),
    remaining: aud(0),
    tenders: [
      {
        tenderGuid: "card-linkly-success",
        method: "card",
        amount: aud(1_000),
        reversible: true,
      },
    ],
    attemptId: "attempt-linkly-success",
    provider: "linkly-cloud",
    runtimeStatus: "completed",
    allowedActions: actions(),
    linkly: {
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    },
  });
  const screen = await render(
    <PaymentScreen
      locale="zh"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(screen.getByTestId("payment-linkly-controls")).toBeTruthy();
  expect(
    screen.getByTestId("payment-success-print").props.accessibilityState,
  ).toEqual({ disabled: true });
  await fireEvent.press(
    screen.getByTestId("payment-linkly-receipt-printed"),
  );
  expect(spies.markLinklyReceiptPrinted).toHaveBeenCalledTimes(1);
  await fireEvent.press(screen.getByTestId("payment-linkly-acknowledge"));
  expect(spies.acknowledgeLinkly).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("Unknown 隐藏新付款和 Linkly 按键，只允许恢复同一 attempt", async () => {
  const runtime = new ScreenPaymentRuntime();
  runtime.recovery = snapshot({
    status: "unknown",
    provider: "linkly-cloud",
    attemptId: "attempt-linkly-unknown",
    errorCode: "PAYMENT_STATUS_UNKNOWN",
    allowedActions: actions({ recover: true }),
  });
  runtime.recoverImpl = async () =>
    snapshot({
      status: "completed",
      remaining: aud(0),
      provider: "linkly-cloud",
      attemptId: "attempt-linkly-unknown",
      tenders: [
        {
          tenderGuid: "tender-linkly-ui",
          method: "card",
          amount: aud(1_000),
          reversible: true,
        },
      ],
    });
  const linkly = new ScreenLinklyOperator();
  const screen = await render(
    <PaymentScreen
      locale="en"
      presenter={screenPresenter(runtime, linkly)}
      showStatusStrip={false}
    />,
  );

  await waitFor(() =>
    expect(screen.getByTestId("payment-status-unknown")).toBeTruthy(),
  );
  expect(screen.queryByTestId("payment-entry-form")).toBeNull();
  expect(screen.queryByTestId("payment-submit")).toBeNull();
  expect(screen.queryByTestId("payment-linkly-yes")).toBeNull();
  expect(screen.queryByTestId("payment-cancel")).toBeNull();

  await fireEvent.press(screen.getByTestId("payment-recover"));
  await waitFor(() =>
    expect(screen.getByTestId("payment-status-success")).toBeTruthy(),
  );
  expect(runtime.recoverInputs).toEqual([
    {
      orderGuid: "order-ui-1",
      attemptId: "attempt-linkly-unknown",
    },
  ]);
  expect(linkly.sendCalls).toHaveLength(0);
});

test("部分付款显示脱敏 tender、余额与可控 reversal，不把银行卡归因到 provider reference", async () => {
  const runtime = new ScreenPaymentRuntime();
  runtime.recovery = snapshot({
    status: "partial",
    remaining: aud(400),
    provider: "square",
    attemptId: "attempt-square-approved",
    tenders: [
      {
        tenderGuid: "public-tender-guid",
        method: "card",
        amount: aud(600),
        reversible: true,
      },
    ],
    allowedActions: actions({
      start: true,
      changeProvider: true,
      addCash: true,
      removeTender: true,
    }),
  });
  runtime.removeImpl = async () =>
    snapshot({
      status: "draft-prepared",
      remaining: aud(1_000),
      allowedActions: actions({
        start: true,
        changeProvider: true,
        cancel: true,
        addCash: true,
      }),
    });
  const screen = await render(
    <PaymentScreen
      locale="zh"
      presenter={screenPresenter(runtime)}
      showStatusStrip={false}
    />,
  );

  await waitFor(() =>
    expect(screen.getByTestId("payment-status-partial")).toBeTruthy(),
  );
  expect(screen.getByText("银行卡")).toBeTruthy();
  expect(screen.getByText(formatAud(400, "zh"))).toBeTruthy();
  expect(screen.getByTestId("payment-method-square").props.accessibilityState)
    .toEqual({ disabled: true, selected: false });
  expect(screen.getByTestId("payment-method-linkly-cloud").props.accessibilityState)
    .toEqual({ disabled: true, selected: false });

  await fireEvent.press(
    screen.getByTestId("payment-remove-public-tender-guid"),
  );
  await waitFor(() =>
    expect(runtime.removeInputs).toHaveLength(1),
  );
  expect(runtime.removeInputs[0]).toEqual({
    orderGuid: "order-ui-1",
    actionId: "ui-action-1",
    tenderGuid: "public-tender-guid",
  });
});

test("Linkly Pending 仅渲染枚举安全键，点击只传 attemptId 和 key", async () => {
  const runtime = new ScreenPaymentRuntime();
  runtime.recovery = snapshot({
    status: "pending",
    provider: "linkly-cloud",
    attemptId: "attempt-linkly-pending",
    allowedActions: actions({ recover: true, cancel: true }),
  });
  const linkly = new ScreenLinklyOperator();
  linkly.sendImpl = async (attemptId) => ({
    attemptId,
    status: "in-progress",
    errorCode: null,
    allowedKeys: ["yes", "no"],
  });
  const screen = await render(
    <PaymentScreen
      locale="en"
      presenter={screenPresenter(runtime, linkly)}
      showStatusStrip={false}
    />,
  );

  await waitFor(() =>
    expect(screen.getByTestId("payment-linkly-controls")).toBeTruthy(),
  );
  expect(screen.getByTestId("payment-linkly-yes")).toBeTruthy();
  expect(screen.getByTestId("payment-linkly-authorise")).toBeTruthy();
  await fireEvent.press(screen.getByTestId("payment-linkly-yes"));
  await waitFor(() => expect(linkly.sendCalls).toHaveLength(1));
  expect(linkly.sendCalls[0]).toEqual({
    attemptId: "attempt-linkly-pending",
    key: "yes",
  });
  expect(
    screen.getByTestId("payment-linkly-authorise").props.accessibilityState,
  ).toEqual({ disabled: true });
});

test("Square Pending 按 WPF 节奏自动恢复且卸载后停止轮询", async () => {
  jest.useFakeTimers();
  const { presenter, spies } = createUiPresenter({
    phase: "pending",
    provider: "square",
    runtimeStatus: "pending",
    orderGuid: "order-square-auto-recovery",
    attemptId: "attempt-square-auto-recovery",
    allowedActions: actions({ recover: true, cancel: true }),
  });
  const screen = await render(
    <PaymentScreen
      locale="zh"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  try {
    await act(async () => {
      await jest.advanceTimersByTimeAsync(1_999);
    });
    expect(spies.recover).not.toHaveBeenCalled();

    await act(async () => {
      await jest.advanceTimersByTimeAsync(1);
    });
    expect(spies.recover).toHaveBeenCalledTimes(1);

    await screen.unmount();
    await jest.advanceTimersByTimeAsync(10_000);
    expect(spies.recover).toHaveBeenCalledTimes(1);
  } finally {
    jest.useRealTimers();
  }
});

test("Square 慢恢复在 90 秒后不再发起新请求且 Linkly Pending 不受影响", async () => {
  jest.useFakeTimers();
  const square = createUiPresenter({
    phase: "pending",
    provider: "square",
    runtimeStatus: "pending",
    orderGuid: "order-square-bounded-recovery",
    attemptId: "attempt-square-bounded-recovery",
    allowedActions: actions({ recover: true, cancel: true }),
  });
  square.spies.recover.mockImplementation(async () => {
    square.publish({
      ...square.presenter.getState(),
      phase: "submitting",
      busy: true,
    });
    await new Promise<void>((resolve) => setTimeout(resolve, 5_000));
    square.publish({
      ...square.presenter.getState(),
      phase: "pending",
      busy: false,
    });
    return false;
  });
  const squareScreen = await render(
    <PaymentScreen
      locale="zh"
      presenter={square.presenter}
      showStatusStrip={false}
    />,
  );

  try {
    await act(async () => {
      await jest.advanceTimersByTimeAsync(100_000);
    });
    const callsAtDeadline = square.spies.recover.mock.calls.length;
    expect(callsAtDeadline).toBeGreaterThan(1);
    expect(callsAtDeadline).toBeLessThan(45);
    await act(async () => {
      await jest.advanceTimersByTimeAsync(100_000);
    });
    expect(square.spies.recover).toHaveBeenCalledTimes(callsAtDeadline);
    await squareScreen.unmount();

    const linkly = createUiPresenter({
      phase: "pending",
      provider: "linkly-cloud",
      runtimeStatus: "pending",
      orderGuid: "order-linkly-no-auto-recovery",
      attemptId: "attempt-linkly-no-auto-recovery",
      allowedActions: actions({ recover: true, cancel: true }),
    });
    const linklyScreen = await render(
      <PaymentScreen
        locale="zh"
        presenter={linkly.presenter}
        showStatusStrip={false}
      />,
    );
    await act(async () => {
      await jest.advanceTimersByTimeAsync(10_000);
    });
    expect(linkly.spies.recover).not.toHaveBeenCalled();
    await linklyScreen.unmount();
  } finally {
    jest.useRealTimers();
  }
});

test("同一 Square attempt 重挂载不会重置 90 秒恢复窗口", async () => {
  jest.useFakeTimers();
  const pendingState: Partial<PaymentPresenterState> = {
    phase: "pending",
    provider: "square",
    runtimeStatus: "pending",
    orderGuid: "order-square-remount-deadline",
    attemptId: "attempt-square-remount-deadline",
    allowedActions: actions({ recover: true, cancel: true }),
  };
  const first = createUiPresenter(pendingState);
  const firstScreen = await render(
    <PaymentScreen
      locale="zh"
      presenter={first.presenter}
      showStatusStrip={false}
    />,
  );

  try {
    await act(async () => {
      await jest.advanceTimersByTimeAsync(89_000);
    });
    expect(first.spies.recover).toHaveBeenCalledTimes(1);
    await firstScreen.unmount();

    const remounted = createUiPresenter(pendingState);
    const remountedScreen = await render(
      <PaymentScreen
        locale="zh"
        presenter={remounted.presenter}
        showStatusStrip={false}
      />,
    );
    await act(async () => {
      await jest.advanceTimersByTimeAsync(2_000);
    });
    expect(remounted.spies.recover).not.toHaveBeenCalled();
    await remountedScreen.unmount();
  } finally {
    jest.useRealTimers();
  }
});

test("统一支付保持 30/42/28 三栏，并只提供五个现金快捷金额", async () => {
  setPaymentWindowSize(1366, 1024);
  const { presenter, spies } = createUiPresenter({
    selectedMethod: "cash",
  });
  const screen = await render(
    <PaymentScreen
      locale="en"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(
    StyleSheet.flatten(screen.getByTestId("payment-context-pane").props.style)
      .flex,
  ).toBe(30);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-entry-pane").props.style)
      .flex,
  ).toBe(42);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-summary").props.style).flex,
  ).toBe(28);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-workspace").props.style)
      .flexDirection,
  ).toBe("row");
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-entry-pane").props.style)
      .padding,
  ).toBe(20);
  expect(screen.getByTestId("payment-keypad")).toBeTruthy();
  for (const key of [
    "1",
    "2",
    "3",
    "4",
    "5",
    "6",
    "7",
    "8",
    "9",
    "decimal",
    "0",
    "backspace",
  ]) {
    expect(screen.getByTestId(`payment-key-${key}`)).toBeTruthy();
  }
  expect(screen.getByTestId("payment-amount").props.showSoftInputOnFocus)
    .toBe(false);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-key-1").props.style)
      .minHeight,
  ).toBe(54);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-cash-quick").props.style)
      .flexWrap,
  ).toBe("nowrap");

  const banknoteColors = {
    5: "#E7C5DD",
    10: "#B9DCEB",
    20: "#EDB5AA",
    50: "#F4DB7F",
    100: "#B9D8B4",
  } as const;
  for (const amount of [5, 10, 20, 50, 100] as const) {
    expect(
      screen.getByTestId(`payment-cash-quick-${amount}`),
    ).toBeTruthy();
    expect(
      StyleSheet.flatten(
        screen.getByTestId(`payment-cash-quick-${amount}`).props.style,
      ).backgroundColor,
    ).toBe(banknoteColors[amount]);
  }
  expect(screen.queryByText(/exacta?/i)).toBeNull();

  await fireEvent.press(screen.getByTestId("payment-key-1"));
  expect(spies.setAmountText).toHaveBeenCalledWith("1");
  await fireEvent.press(screen.getByTestId("payment-cash-quick-50"));
  expect(spies.setAmountText).toHaveBeenCalledWith("50.00");
  await screen.unmount();
  setPaymentWindowSize(750, 1334);
});

test("11 英寸横屏现金支付使用紧凑高度，数字键盘和支付动作保持首屏可达", async () => {
  setPaymentWindowSize(1194, 834);
  const { presenter } = createUiPresenter({
    selectedMethod: "cash",
  });
  const screen = await render(
    <PaymentScreen
      locale="zh"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(
    StyleSheet.flatten(screen.getByTestId("payment-entry-pane").props.style)
      .padding,
  ).toBeLessThanOrEqual(14);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-key-1").props.style)
      .minHeight,
  ).toBe(PAYMENT_MIN_TOUCH_TARGET);
  expect(
    StyleSheet.flatten(screen.getByTestId("payment-cash-quick").props.style)
      .flexWrap,
  ).toBe("nowrap");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("payment-cash-quick-5").props.style,
    ).minWidth,
  ).toBe(PAYMENT_MIN_TOUCH_TARGET);
  expect(
    StyleSheet.flatten(
      screen.getByTestId("payment-cash-quick-5").props.style,
    ).flexBasis,
  ).toBe(0);
  expect(screen.getByTestId("payment-submit")).toBeTruthy();

  await screen.unmount();
  setPaymentWindowSize(750, 1334);
});

test("窄屏支付页改为单列且分期开关保持可达", async () => {
  setPaymentWindowSize(750, 1334);
  const { presenter } = createUiPresenter();
  const screen = await render(
    <PaymentScreen
      installmentModeControl={{
        enabled: false,
        locked: false,
        issue: null,
        onToggle: jest.fn(),
      }}
      locale="zh"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  expect(
    StyleSheet.flatten(screen.getByTestId("payment-workspace").props.style)
      .flexDirection,
  ).toBe("column");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("payment-context-pane").props.style,
    ).maxHeight,
  ).toBe(420);
  expect(screen.getByTestId("payment-installment-toggle")).toBeTruthy();
  await screen.unmount();
});

test("无耐久支付事实的初始化失败仍允许返回收银", async () => {
  const { presenter } = createUiPresenter({
    phase: "recovery-required",
    runtimeErrorCode: "ONLINE_REQUIRED",
    allowedActions: actions(),
  });
  const onBack = jest.fn();
  const screen = await render(
    <PaymentScreen
      locale="zh"
      onBack={onBack}
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  const back = screen.getByTestId("payment-back");
  expect(back.props.accessibilityState).toEqual({ disabled: false });
  await fireEvent.press(back);
  expect(onBack).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("分期开关保持 44pt、switch 语义和稳定失败提示", async () => {
  const { presenter } = createUiPresenter();
  const onToggle = jest.fn();
  const screen = await render(
    <PaymentScreen
      installmentModeControl={{
        enabled: false,
        locked: false,
        issue: "unavailable",
        onToggle,
      }}
      locale="en"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  const toggle = screen.getByTestId("payment-installment-toggle");
  expect(toggle.props.accessibilityRole).toBe("switch");
  expect(toggle.props.accessibilityState).toEqual({
    checked: false,
    disabled: false,
  });
  expect(
    StyleSheet.flatten(toggle.props.style).minHeight,
  ).toBeGreaterThanOrEqual(PAYMENT_MIN_TOUCH_TARGET);
  expect(
    screen.getByText(
      "Installment mode is unavailable. Return to sale and try again.",
    ),
  ).toBeTruthy();

  await fireEvent.press(toggle);
  expect(onToggle).toHaveBeenCalledWith(true);

  await screen.rerender(
    <PaymentScreen
      installmentModeControl={{
        enabled: true,
        locked: true,
        issue: null,
        onToggle,
      }}
      locale="en"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );
  const lockedToggle = screen.getByTestId("payment-installment-toggle");
  expect(lockedToggle.props.accessibilityState).toEqual({
    checked: true,
    disabled: true,
  });
  await fireEvent.press(lockedToggle);
  expect(onToggle).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("分期现金超付显示入账与找零，并可确认付款", async () => {
  const { presenter, spies } = createUiPresenter(
    {
      checkout: {
        flow: "installment-create",
        lines: [
          {
            lineKey: "line-ui-1",
            displayName: "Tea",
            quantity: "1",
            actualAmountCents: 5_000,
          },
        ],
        installmentCustomer: {
          name: "Bob",
          phone: "0400000000",
          editable: true,
          editorOpen: false,
          draftName: "Bob",
          draftPhone: "0400000000",
          installmentNumber: null,
        },
        cash: {
          tenderedCents: 0,
          appliedCents: 0,
          changeCents: 0,
        },
        canConfirm: false,
        fullInstallmentConfirmationRequired: false,
      },
      selectedMethod: "cash",
      total: aud(5_000),
      remaining: aud(5_000),
    },
    (state) => ({
      ...state,
      remaining: aud(0),
      tenders: [
        {
          tenderGuid: "cash-ui-1",
          method: "cash",
          amount: aud(5_000),
          reversible: true,
        },
      ],
      allowedActions: actions(),
      checkout: {
        ...state.checkout,
        cash: {
          tenderedCents: 6_000,
          appliedCents: 5_000,
          changeCents: 1_000,
        },
        canConfirm: true,
        fullInstallmentConfirmationRequired: true,
      },
    }),
  );
  const screen = await render(
    <PaymentScreen
      locale="en"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  await fireEvent.changeText(screen.getByTestId("payment-amount"), "60.00");
  await fireEvent.press(screen.getByTestId("payment-submit"));

  await waitFor(() =>
    expect(screen.getByTestId("payment-cash-applied")).toHaveTextContent(
      formatAud(5_000, "en"),
    ),
  );
  expect(screen.getByTestId("payment-change")).toHaveTextContent(
    formatAud(1_000, "en"),
  );
  await fireEvent.press(screen.getByTestId("payment-confirm"));
  expect(
    screen.getByTestId("payment-full-installment-confirmation"),
  ).toBeTruthy();
  expect(spies.confirm).not.toHaveBeenCalled();

  await fireEvent.press(
    screen.getByTestId("payment-full-installment-cancel"),
  );
  expect(
    screen.queryByTestId("payment-full-installment-confirmation"),
  ).toBeNull();
  expect(screen.getByTestId("payment-remove-cash-ui-1")).toBeTruthy();

  await fireEvent.press(screen.getByTestId("payment-confirm"));
  await fireEvent.press(
    screen.getByTestId("payment-full-installment-confirm"),
  );
  expect(spies.confirm).toHaveBeenCalledWith({
    acknowledgeFullInstallmentPayment: true,
  });
  await screen.unmount();
});

test("新建分期顾客可编辑，续付顾客只读且已选 provider 冻结", async () => {
  const create = createUiPresenter({
    checkout: {
      flow: "installment-create",
      lines: [],
      installmentCustomer: {
        name: "Bob",
        phone: "0400000000",
        editable: true,
        editorOpen: false,
        draftName: "Bob",
        draftPhone: "0400000000",
        installmentNumber: null,
      },
      cash: {
        tenderedCents: 0,
        appliedCents: 0,
        changeCents: 0,
      },
      canConfirm: false,
      fullInstallmentConfirmationRequired: false,
    },
  });
  const createScreen = await render(
    <PaymentScreen
      locale="en"
      presenter={create.presenter}
      showStatusStrip={false}
    />,
  );

  await fireEvent.press(createScreen.getByTestId("payment-customer-edit"));
  await fireEvent.changeText(
    createScreen.getByTestId("payment-customer-name"),
    "Alice",
  );
  await fireEvent.changeText(
    createScreen.getByTestId("payment-customer-phone"),
    "0411111111",
  );
  await fireEvent.press(createScreen.getByTestId("payment-customer-save"));
  expect(create.spies.setInstallmentCustomerDraftName).toHaveBeenCalledWith(
    "Alice",
  );
  expect(create.spies.setInstallmentCustomerDraftPhone).toHaveBeenCalledWith(
    "0411111111",
  );
  expect(create.spies.saveInstallmentCustomer).toHaveBeenCalledTimes(1);
  await createScreen.unmount();

  const repayment = createUiPresenter({
    allowedActions: actions(),
    selectedMethod: "linkly-cloud",
    provider: "linkly-cloud",
    tenders: [
      {
        tenderGuid: "card-ui-1",
        method: "card",
        amount: aud(1_000),
        reversible: false,
        provider: "linkly-cloud",
      },
    ],
    checkout: {
      flow: "installment-repayment",
      lines: [],
      installmentCustomer: {
        name: "Bob",
        phone: "0400000000",
        editable: false,
        editorOpen: false,
        draftName: "Bob",
        draftPhone: "0400000000",
        installmentNumber: "IP-0001",
      },
      cash: {
        tenderedCents: 0,
        appliedCents: 0,
        changeCents: 0,
      },
      canConfirm: true,
      fullInstallmentConfirmationRequired: false,
    },
  });
  const repaymentScreen = await render(
    <PaymentScreen
      locale="en"
      presenter={repayment.presenter}
      showStatusStrip={false}
    />,
  );

  expect(repaymentScreen.queryByTestId("payment-customer-edit")).toBeNull();
  expect(repaymentScreen.queryByTestId("payment-customer-name")).toBeNull();
  expect(
    repaymentScreen.getByTestId("payment-method-square").props
      .accessibilityState,
  ).toEqual({ disabled: true, selected: false });
  expect(
    repaymentScreen.getByTestId("payment-method-linkly-cloud").props
      .accessibilityState,
  ).toEqual({ disabled: true, selected: true });
  await fireEvent.press(
    repaymentScreen.getByTestId("payment-method-square"),
  );
  expect(repayment.spies.selectMethod).not.toHaveBeenCalled();
  await repaymentScreen.unmount();
});

test("真实分期 presenter 点击客户编辑仍保留实例上下文", async () => {
  const unusedWorkflowOperation = async (): Promise<never> => {
    throw new Error("本测试不应执行分期写操作");
  };
  const workflow: InstallmentWorkflowPort = {
    listPaymentProviderAvailability: async () => [
      providerAvailability("square"),
      providerAvailability("linkly-cloud"),
      providerAvailability("voucher"),
    ],
    list: async () => [],
    getDetails: async () => null,
    recoverBlocking: unusedWorkflowOperation,
    create: unusedWorkflowOperation,
    addRepayment: unusedWorkflowOperation,
    cancelWithRefund: unusedWorkflowOperation,
    void: unusedWorkflowOperation,
    confirmPickup: unusedWorkflowOperation,
  };
  const presenter = new InstallmentCheckoutPresenter({
    entry: installmentCreatePaymentEntry({
      checkoutIntentId: "11111111-1111-4111-8111-111111111111",
      expectedCartRevision: 7,
    }),
    createDrafts: {
      getSnapshot: () => ({
        revision: 7,
        totalCents: 5_000,
        lines: [
          {
            lineKey: "line-real-presenter",
            displayName: "真实 presenter 商品",
            quantity: "1",
            actualAmountCents: 5_000,
          },
        ],
      }),
      subscribe: () => () => undefined,
    },
    initialOnline: true,
    permissions: [
      INSTALLMENTS_CREATE_PERMISSION,
      PAYMENT_PERMISSION.view,
      PAYMENT_PERMISSION.confirm,
      PAYMENT_PERMISSION.takeCash,
    ],
    workflow,
    createTenderId: () => "tender-real-presenter",
  });
  const screen = await render(
    <PaymentScreen
      locale="zh"
      presenter={presenter}
      showStatusStrip={false}
    />,
  );

  await waitFor(() =>
    expect(screen.getByTestId("payment-customer-edit")).toBeTruthy(),
  );
  await fireEvent.changeText(
    screen.getByTestId("payment-amount"),
    "50.00",
  );
  await fireEvent.press(screen.getByTestId("payment-submit"));
  await fireEvent.press(screen.getByTestId("payment-confirm"));
  expect(
    screen.getByText("请填写分期顾客姓名和联系电话。"),
  ).toBeTruthy();
  expect(
    screen.queryByTestId("payment-full-installment-confirmation"),
  ).toBeNull();

  await fireEvent.press(screen.getByTestId("payment-customer-edit"));
  expect(screen.getByTestId("payment-customer-editor")).toBeTruthy();
  await fireEvent.changeText(
    screen.getByTestId("payment-customer-name"),
    "顾客乙",
  );
  await fireEvent.changeText(
    screen.getByTestId("payment-customer-phone"),
    "0412345678",
  );
  await fireEvent.press(screen.getByTestId("payment-customer-save"));
  expect(screen.getByText("顾客乙")).toBeTruthy();
  expect(screen.getByText("0412345678")).toBeTruthy();
  await screen.unmount();
});

function createUiPresenter(
  override: Partial<PaymentPresenterState> = {},
  applySubmittedState?: (
    state: PaymentPresenterState,
  ) => PaymentPresenterState,
) {
  let state: PaymentPresenterState = {
    phase: "ready",
    busy: false,
    initialized: true,
    providers: [
      providerAvailability("square"),
      providerAvailability("linkly-cloud"),
      providerAvailability("voucher"),
    ],
    selectedMethod: "square",
    amountText: "10.00",
    voucherCaptured: false,
    sensitiveInputRevision: 0,
    fieldIssue: null,
    runtimeErrorCode: null,
    orderGuid: null,
    total: aud(1_000),
    remaining: aud(1_000),
    tenders: [],
    attemptId: null,
    provider: null,
    runtimeStatus: null,
    allowedActions: actions({
      start: true,
      changeProvider: true,
      cancel: true,
      addCash: true,
    }),
    tenderReversalRecovery: null,
    checkout: {
      flow: "regular",
      lines: [],
      installmentCustomer: null,
      cash: {
        tenderedCents: 0,
        appliedCents: 0,
        changeCents: 0,
      },
      canConfirm: false,
      fullInstallmentConfirmationRequired: false,
    },
    linkly: {
      status: null,
      errorCode: null,
      allowedKeys: [],
    },
    ...override,
  };
  const listeners = new Set<() => void>();
  const publish = (next: PaymentPresenterState) => {
    state = next;
    listeners.forEach((listener) => listener());
  };
  const patchCustomer = (
    update: (
      customer: NonNullable<
        PaymentPresenterState["checkout"]["installmentCustomer"]
      >,
    ) => NonNullable<
      PaymentPresenterState["checkout"]["installmentCustomer"]
    >,
  ) => {
    const customer = state.checkout.installmentCustomer;
    if (!customer) return;
    publish({
      ...state,
      checkout: {
        ...state.checkout,
        installmentCustomer: update(customer),
      },
    });
  };
  const spies = {
    selectMethod: jest.fn((method: PaymentUiMethod) => {
      publish({ ...state, selectedMethod: method });
      return true;
    }),
    setAmountText: jest.fn((value: string) => {
      publish({ ...state, amountText: value });
    }),
    setVoucherCode: jest.fn((_value: string) => undefined),
    dismissError: jest.fn(() => undefined),
    submitSelected: jest.fn(async () => {
      if (applySubmittedState) publish(applySubmittedState(state));
      return true;
    }),
    recover: jest.fn(async () => true),
    cancel: jest.fn(async () => true),
    removeTender: jest.fn(async (_tenderGuid: string) => true),
    sendLinklyKey: jest.fn(async (_key: LinklySafeOperatorKey) => true),
    markLinklyReceiptPrinted: jest.fn(async () => true),
    acknowledgeLinkly: jest.fn(async () => true),
    confirm: jest.fn(async (_options?: PaymentConfirmOptions) => true),
    openInstallmentCustomerEditor: jest.fn(() => {
      patchCustomer((customer) => ({
        ...customer,
        editorOpen: customer.editable,
      }));
    }),
    setInstallmentCustomerDraftName: jest.fn((value: string) => {
      patchCustomer((customer) => ({ ...customer, draftName: value }));
    }),
    setInstallmentCustomerDraftPhone: jest.fn((value: string) => {
      patchCustomer((customer) => ({ ...customer, draftPhone: value }));
    }),
    saveInstallmentCustomer: jest.fn(() => {
      patchCustomer((customer) => ({
        ...customer,
        name: customer.draftName,
        phone: customer.draftPhone,
        editorOpen: false,
      }));
    }),
    cancelInstallmentCustomerEditor: jest.fn(() => {
      patchCustomer((customer) => ({ ...customer, editorOpen: false }));
    }),
  };
  const presenter: PaymentScreenPresenter = {
    getState: () => state,
    subscribe: (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    initialize: async () => true,
    destroy: () => listeners.clear(),
    ...spies,
  };
  return { presenter, publish, spies };
}

class ScreenPaymentRuntime implements PaymentCheckoutRuntimePort {
  public recovery: PaymentCheckoutPublicSnapshot | null = null;
  public readonly startCalls: unknown[] = [];
  public readonly recoverInputs: unknown[] = [];
  public readonly removeInputs: unknown[] = [];
  public startImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "pending" });
  public recoverImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "pending" });
  public removeImpl: () => Promise<PaymentCheckoutPublicSnapshot> =
    async () => snapshot({ status: "partial" });

  public listProviderAvailability(): readonly PaymentProviderAvailability[] {
    return [
      providerAvailability("square"),
      providerAvailability("linkly-cloud"),
      providerAvailability("voucher"),
    ];
  }

  public async read(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot();
  }

  public async findRecoveryRequired(): Promise<PaymentCheckoutPublicSnapshot | null> {
    return this.recovery;
  }

  public async resumeCurrent(): Promise<PaymentCheckoutPublicSnapshot | null> {
    return snapshot({ status: "pending" });
  }

  public start(
    input: Parameters<PaymentCheckoutRuntimePort["start"]>[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.startCalls.push(input);
    return this.startImpl();
  }

  public recover(
    input: Parameters<PaymentCheckoutRuntimePort["recover"]>[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.recoverInputs.push(input);
    return this.recoverImpl();
  }

  public async cancel(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot({ status: "cancelled" });
  }

  public async abandonPrepared(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot({ status: "cancelled" });
  }

  public async addCash(): Promise<PaymentCheckoutPublicSnapshot> {
    return snapshot({ status: "partial" });
  }

  public removeTender(
    input: Parameters<PaymentCheckoutRuntimePort["removeTender"]>[0],
  ): Promise<PaymentCheckoutPublicSnapshot> {
    this.removeInputs.push(input);
    return this.removeImpl();
  }
}

class ScreenLinklyOperator implements LinklyOperatorRuntimePort {
  public readonly sendCalls: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }[] = [];
  public sendImpl: (
    attemptId: string,
  ) => Promise<LinklyOperatorPublicResult> = async (attemptId) => ({
    attemptId,
    status: "in-progress",
    errorCode: null,
    allowedKeys: ["yes"],
  });

  public sendKey(input: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }): Promise<LinklyOperatorPublicResult> {
    this.sendCalls.push(input);
    return this.sendImpl(input.attemptId);
  }

  public async markReceiptPrinted(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    return {
      attemptId,
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    };
  }

  public async acknowledge(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    return {
      attemptId,
      status: "completed",
      errorCode: null,
      allowedKeys: [],
    };
  }
}

function screenPresenter(
  runtime: ScreenPaymentRuntime,
  linklyOperator?: LinklyOperatorRuntimePort,
): PaymentPresenter {
  let action = 0;
  return new PaymentPresenter({
    runtime,
    ...(linklyOperator ? { linklyOperator } : {}),
    entry: {
      checkoutIntentId: "checkout-ui-1",
      expectedCartRevision: 3,
      total: aud(1_000),
    },
    createActionId: () => `ui-action-${++action}`,
  });
}

function snapshot(
  override: Partial<PaymentCheckoutPublicSnapshot> = {},
): PaymentCheckoutPublicSnapshot {
  return {
    orderGuid: "order-ui-1",
    total: aud(1_000),
    remaining: aud(1_000),
    tenders: [],
    attemptId: null,
    provider: null,
    status: "draft-prepared",
    errorCode: null,
    allowedActions: actions({
      start: true,
      changeProvider: true,
      cancel: true,
      addCash: true,
    }),
    ...override,
  };
}

function actions(
  override: Partial<PaymentCheckoutPublicSnapshot["allowedActions"]> = {},
): PaymentCheckoutPublicSnapshot["allowedActions"] {
  return {
    start: false,
    changeProvider: false,
    recover: false,
    cancel: false,
    addCash: false,
    removeTender: false,
    ...override,
  };
}

function providerAvailability(
  provider: PaymentProvider,
): PaymentProviderAvailability {
  return {
    provider,
    available: true,
    blocker: null,
  };
}

function aud(cents: number): Money {
  return { currency: "AUD", cents };
}

function setPaymentWindowSize(width: number, height: number): void {
  const metrics = {
    width,
    height,
    scale: 2,
    fontScale: 1,
  };
  Dimensions.set({ window: metrics, screen: metrics });
}

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, reject, resolve };
}
