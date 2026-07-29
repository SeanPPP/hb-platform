import { expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";

import type { InstallmentDetails } from "./installment-models";
import type { InstallmentPresenterState } from "./installment-presenter";
import {
  INSTALLMENTS_MIN_TOUCH_TARGET,
  InstallmentScreen,
  InstallmentsUnavailableScreen,
} from "./installment-screen";
import type { InstallmentScreenPresenter } from "./installment-screen";

let mockLanguage = "en";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: mockLanguage, resolvedLanguage: mockLanguage },
  }),
}));

test("英文界面只显示英文分期文案", async () => {
  mockLanguage = "en";
  const { presenter } = createPresenter({ orders: [summary()] });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(screen.getByText("Installments")).toBeTruthy();
  expect(screen.getAllByText("Active")).toHaveLength(2);
  expect(screen.queryByText("分期")).toBeNull();
  expect(screen.queryByText("分期 / Installments")).toBeNull();
  await screen.unmount();
});

test("中文界面只显示中文分期文案", async () => {
  mockLanguage = "zh";
  try {
    const { presenter } = createPresenter({ orders: [summary()] });
    const screen = await render(<InstallmentScreen presenter={presenter} />);

    expect(screen.getByText("分期")).toBeTruthy();
    expect(screen.getAllByText("进行中")).toHaveLength(2);
    expect(screen.queryByText("Installments")).toBeNull();
    expect(screen.queryByText("分期 / Installments")).toBeNull();
    await screen.unmount();
  } finally {
    mockLanguage = "en";
  }
});

test("横屏工作台加载历史、显示离线只读提示并支持返回", async () => {
  const onBack = jest.fn();
  const { presenter, spies } = createPresenter({
    online: false,
    orders: [summary()],
  });
  const screen = await render(
    <InstallmentScreen onBack={onBack} presenter={presenter} />,
  );

  await waitFor(() => expect(spies.load).toHaveBeenCalledTimes(1));
  expect(screen.getByTestId("installments-offline-note")).toBeTruthy();
  expect(screen.getByText("IP-0001")).toBeTruthy();

  await fireEvent.press(screen.getByTestId(`installment-row-${GUID}`));
  expect(spies.select).toHaveBeenCalledWith(GUID);
  await fireEvent.press(screen.getByTestId("installments-back"));
  expect(onBack).toHaveBeenCalledTimes(1);
  expect(INSTALLMENTS_MIN_TOUCH_TARGET).toBe(44);
  await screen.unmount();
});

test("缺少 Create 权限时不显示新建入口", async () => {
  const { presenter } = createPresenter({
    access: {
      canAddRepayment: true,
      canCancel: true,
      canConfirmPickup: true,
      canCreate: false,
      canView: true,
    },
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(screen.queryByTestId("installments-create-tab")).toBeNull();
  await screen.unmount();
});

test("传入新建支付回调时跳转统一支付且不打开旧内联表单", async () => {
  const onStartCreate = jest.fn();
  const { presenter, spies } = createPresenter({});
  const screen = await render(
    <InstallmentScreen
      onStartCreate={onStartCreate}
      presenter={presenter}
    />,
  );

  await fireEvent.press(screen.getByTestId("installments-create-tab"));
  expect(onStartCreate).toHaveBeenCalledTimes(1);
  expect(spies.showCreate).not.toHaveBeenCalled();
  expect(screen.queryByTestId("installment-create-workspace")).toBeNull();
  await screen.unmount();
});

test("传入续付支付回调时携带分期号跳转且不显示旧内联续付表单", async () => {
  const onStartRepayment = jest.fn();
  const { presenter, spies } = createPresenter({
    details: details("Active"),
    selectedGuid: GUID,
  });
  const screen = await render(
    <InstallmentScreen
      onStartRepayment={onStartRepayment}
      presenter={presenter}
    />,
  );

  expect(screen.queryByTestId("installment-repayment-amount")).toBeNull();
  expect(screen.queryByTestId("installment-add-repayment")).toBeNull();
  await fireEvent.press(
    screen.getByTestId("installment-continue-to-payment"),
  );
  expect(onStartRepayment).toHaveBeenCalledWith(GUID);
  expect(spies.addRepayment).not.toHaveBeenCalled();
  await screen.unmount();
});

test("创建页只收集券码并说明在线 query+lock，不显示 reservation token 输入", async () => {
  const { presenter, spies } = createPresenter({
    pane: "create",
    createPaymentMethod: "voucher",
    createDraft: {
      revision: 1,
      totalCents: 10_000,
      lines: [
        {
          lineKey: "L1",
          displayName: "Tea",
          quantity: "1.25",
          actualAmountCents: 10_000,
        },
      ],
    },
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(screen.getByTestId("installment-create-workspace")).toBeTruthy();
  expect(screen.getByText("Tea")).toBeTruthy();
  await fireEvent.changeText(
    screen.getByTestId("installment-create-customer-name"),
    "Bob",
  );
  await fireEvent.changeText(
    screen.getByTestId("installment-create-customer-phone"),
    "0400000000",
  );
  await fireEvent.changeText(
    screen.getByTestId("installment-create-note"),
    "Friday",
  );
  await fireEvent.changeText(
    screen.getByTestId("installment-create-down-payment"),
    "20.00",
  );
  await fireEvent.press(
    screen.getByTestId("installment-create-method-voucher"),
  );
  await fireEvent.changeText(
    screen.getByTestId("installment-create-voucher-reference"),
    "V1",
  );
  expect(
    screen.getByTestId("installment-create-voucher-help"),
  ).toHaveTextContent(
    "Enter the voucher code only; the online payment provider will query and lock it after submission.",
  );
  expect(screen.queryByTestId("installment-create-voucher-token")).toBeNull();
  await fireEvent.press(screen.getByTestId("installment-create-submit"));

  expect(spies.setCustomerName).toHaveBeenCalledWith("Bob");
  expect(spies.setCustomerPhone).toHaveBeenCalledWith("0400000000");
  expect(spies.setCreateNote).toHaveBeenCalledWith("Friday");
  expect(spies.setCreateDownPayment).toHaveBeenCalledWith("20.00");
  expect(spies.setCreatePaymentMethod).toHaveBeenCalledWith("voucher");
  expect(spies.setCreateVoucherReference).toHaveBeenCalledWith("V1");
  expect(spies.create).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("续付券同样只显示券码和在线 query+lock 说明", async () => {
  const { presenter, spies } = createPresenter({
    details: details("Active"),
    selectedGuid: GUID,
    repaymentMethod: "voucher",
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  await fireEvent.changeText(
    screen.getByTestId("installment-repayment-voucher-reference"),
    "V-REPAY",
  );

  expect(
    screen.getByTestId("installment-repayment-voucher-help"),
  ).toHaveTextContent(
    "Enter the voucher code only; the online payment provider will query and lock it after submission.",
  );
  expect(
    screen.queryByTestId("installment-repayment-voucher-token"),
  ).toBeNull();
  expect(spies.setRepaymentVoucherReference).toHaveBeenCalledWith("V-REPAY");
  await screen.unmount();
});

test("Active 详情显示补款、退款取消与作废，破坏性操作必须二次确认", async () => {
  const { presenter, spies } = createPresenter({
    details: details("Active"),
    selectedGuid: GUID,
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(screen.getByTestId("installment-repayment-amount")).toBeTruthy();
  await fireEvent.changeText(
    screen.getByTestId("installment-repayment-amount"),
    "80.00",
  );
  await fireEvent.press(
    screen.getByTestId("installment-repayment-method-card"),
  );
  await fireEvent.press(screen.getByTestId("installment-add-repayment"));
  expect(spies.setRepaymentAmount).toHaveBeenCalledWith("80.00");
  expect(spies.setRepaymentMethod).toHaveBeenCalledWith("card");
  expect(spies.addRepayment).toHaveBeenCalledTimes(1);

  await fireEvent.press(screen.getByTestId("installment-cancel-refund"));
  expect(spies.cancelWithRefund).not.toHaveBeenCalled();
  expect(screen.getByTestId("installment-confirm-cancel")).toBeTruthy();
  await fireEvent.press(
    screen.getByTestId("installment-confirm-operation-submit"),
  );
  expect(spies.cancelWithRefund).toHaveBeenCalledTimes(1);

  await fireEvent.press(screen.getByTestId("installment-void"));
  expect(spies.voidSelected).not.toHaveBeenCalled();
  expect(screen.getByTestId("installment-confirm-void")).toBeTruthy();
  await fireEvent.press(
    screen.getByTestId("installment-confirm-operation-submit"),
  );
  expect(spies.voidSelected).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("PaidOff 详情仅显示取货确认，并要求二次确认", async () => {
  const { presenter, spies } = createPresenter({
    details: details("PaidOff"),
    selectedGuid: GUID,
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(screen.queryByTestId("installment-add-repayment")).toBeNull();
  expect(screen.queryByTestId("installment-cancel-refund")).toBeNull();
  await fireEvent.changeText(
    screen.getByTestId("installment-pickup-note"),
    "ID checked",
  );
  await fireEvent.press(screen.getByTestId("installment-confirm-pickup"));
  expect(spies.confirmPickup).not.toHaveBeenCalled();
  await fireEvent.press(
    screen.getByTestId("installment-confirm-operation-submit"),
  );
  expect(spies.setPickupNote).toHaveBeenCalledWith("ID checked");
  expect(spies.confirmPickup).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("Unknown 支付恢复状态禁用新写操作，只允许恢复 durable action", async () => {
  const { presenter, spies } = createPresenter({
    details: details("Active"),
    recoveryRequired: true,
    selectedGuid: GUID,
    statusCode: "payment-recovery-required",
  });
  const screen = await render(<InstallmentScreen presenter={presenter} />);

  expect(
    screen.getByTestId("installments-payment-recovery-required"),
  ).toBeTruthy();
  expect(
    screen.getByTestId("installment-add-repayment").props.accessibilityState
      .disabled,
  ).toBe(true);
  await fireEvent.press(
    screen.getByTestId("installments-recover-blocking-action"),
  );
  expect(spies.recoverBlocking).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

test("运行时未接线页只说明不可用并安全返回", async () => {
  const onBack = jest.fn();
  const screen = await render(
    <InstallmentsUnavailableScreen onBack={onBack} />,
  );

  expect(screen.getByTestId("installments-runtime-unavailable")).toBeTruthy();
  await fireEvent.press(screen.getByTestId("installments-unavailable-back"));
  expect(onBack).toHaveBeenCalledTimes(1);
  await screen.unmount();
});

function createPresenter(overrides: Partial<InstallmentPresenterState>) {
  const state: InstallmentPresenterState = {
    access: {
      canAddRepayment: true,
      canCancel: true,
      canConfirmPickup: true,
      canCreate: true,
      canView: true,
    },
    busy: false,
    cancelReason: "",
    createDownPayment: "20.00",
    createDraft: null,
    createNote: "",
    createPaymentMethod: "cash",
    createVoucherReference: "",
    customerName: "",
    customerPhone: "",
    details: null,
    detailsLoading: false,
    kind: "ready",
    online: true,
    orders: [],
    pane: "history",
    pickupNote: "",
    query: "",
    recoveryRequired: false,
    repaymentAmount: "",
    repaymentMethod: "cash",
    repaymentVoucherReference: "",
    selectedGuid: null,
    statusCode: null,
    statusFilter: null,
    voidReason: "",
    ...overrides,
  };
  const spies = {
    addRepayment: jest.fn(() => Promise.resolve()),
    cancelWithRefund: jest.fn(() => Promise.resolve()),
    confirmPickup: jest.fn(() => Promise.resolve()),
    create: jest.fn(() => Promise.resolve()),
    load: jest.fn(() => Promise.resolve()),
    recoverBlocking: jest.fn(() => Promise.resolve()),
    select: jest.fn((_installmentGuid: string) => Promise.resolve()),
    setCancelReason: jest.fn((_value: string) => undefined),
    setCreateDownPayment: jest.fn((_value: string) => undefined),
    setCreateNote: jest.fn((_value: string) => undefined),
    setCreatePaymentMethod: jest.fn(
      (_value: "cash" | "card" | "voucher") => undefined,
    ),
    setCreateVoucherReference: jest.fn((_value: string) => undefined),
    setCustomerName: jest.fn((_value: string) => undefined),
    setCustomerPhone: jest.fn((_value: string) => undefined),
    setPickupNote: jest.fn((_value: string) => undefined),
    setRepaymentAmount: jest.fn((_value: string) => undefined),
    setRepaymentMethod: jest.fn(
      (_value: "cash" | "card" | "voucher") => undefined,
    ),
    setRepaymentVoucherReference: jest.fn((_value: string) => undefined),
    setSearchQuery: jest.fn((_value: string) => undefined),
    setStatusFilter: jest.fn(
      (_value: "Active" | "PaidOff" | "PickedUp" | "Cancelled" | null) =>
        undefined,
    ),
    setVoidReason: jest.fn((_value: string) => undefined),
    showCreate: jest.fn(),
    showHistory: jest.fn(),
    subscribe: jest.fn(() => () => undefined),
    voidSelected: jest.fn(() => Promise.resolve()),
  };
  const presenter: InstallmentScreenPresenter = {
    ...spies,
    getState: () => state,
  };
  return { presenter, spies };
}

const GUID = "10000000-0000-4000-8000-000000000001";

function summary() {
  return {
    installmentGuid: GUID,
    installmentNumber: "IP-0001",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    cashierName: "Alice",
    customerName: "Bob",
    customerPhone: "0400000000",
    createdAtIso: "2026-07-27T01:02:03.000Z",
    totalCents: 10_000,
    downPaymentCents: 2_000,
    paidCents: 2_000,
    balanceCents: 8_000,
    status: "Active" as const,
    updatedAtIso: "2026-07-27T02:03:04.000Z",
  };
}

function details(status: "Active" | "PaidOff"): InstallmentDetails {
  const balanceCents = status === "Active" ? 8_000 : 0;
  return {
    ...summary(),
    status,
    paidCents: 10_000 - balanceCents,
    balanceCents,
    cashierId: "C1",
    minimumDownPaymentCents: 2_000,
    lines: [
      {
        installmentLineGuid: "30000000-0000-4000-8000-000000000001",
        productCode: "P1",
        referenceCode: "R1",
        displayName: "Tea",
        lookupCode: "930001",
        quantity: "1",
        unitPriceCents: 10_000,
        discountCents: 0,
        actualAmountCents: 10_000,
        itemNumber: "I1",
      },
    ],
    payments: [],
    pickupInfo: null,
    cancellationInfo: null,
    note: "Friday",
  };
}
