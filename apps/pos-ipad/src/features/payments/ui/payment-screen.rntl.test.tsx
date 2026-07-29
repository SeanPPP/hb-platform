import { expect, jest, test } from "@jest/globals";
import {
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { PaymentPresenter } from "./payment-presenter";
import {
  PAYMENT_MIN_TOUCH_TARGET,
  PaymentScreen,
  formatAud,
} from "./payment-screen";

import type {
  Money,
  PaymentProvider,
} from "@/core/contracts";
import type {
  LinklyOperatorPublicResult,
  LinklyOperatorRuntimePort,
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import type {
  PaymentCheckoutPublicSnapshot,
  PaymentCheckoutRuntimePort,
} from "@/features/payments/runtime/payment-checkout-runtime";
import type {
  PaymentProviderAvailability,
} from "@/features/payments/runtime/payment-provider-registry";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: () => null,
}));

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
