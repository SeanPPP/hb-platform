import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  EMPTY_SALE_CART,
  MIN_TOUCH_TARGET,
  SalesPresenter,
  type SalesCapabilities,
  type SalesCartPort,
  type SalesCashCompletion,
  type SalesWorkflowPort,
} from "./sales-presenter";
import { SalesScreen } from "./sales-screen";

import { createAud, type CartLine, type CartSnapshot } from "@/core/contracts";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";

let mockStatusStripProps: any;

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: (props: unknown) => {
    mockStatusStripProps = props;
    return null;
  },
}));

const ALL_CAPABILITIES: SalesCapabilities = {
  catalog: true,
  cartEditing: true,
  cashCheckout: true,
  hold: true,
  lock: true,
};

class ScreenCartPort implements SalesCartPort {
  public snapshot: CartSnapshot;
  public readonly clearSignals: string[] = [];
  public readonly discounts: { lineId: string; basisPoints: number }[] = [];
  public readonly edits: {
    operation: string;
    lineId?: string;
    value?: number;
  }[] = [];
  private readonly listeners = new Set<() => void>();

  public constructor(snapshot: CartSnapshot) {
    this.snapshot = snapshot;
  }

  public getSnapshot(): CartSnapshot {
    return this.snapshot;
  }

  public subscribe(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  public async increaseLine(lineId: string): Promise<void> {
    this.updateQuantity(lineId, 1);
  }

  public async decreaseLine(lineId: string): Promise<void> {
    this.updateQuantity(lineId, -1);
  }

  public async removeLine(lineId: string): Promise<void> {
    this.snapshot = {
      ...this.snapshot,
      revision: this.snapshot.revision + 1,
      lines: this.snapshot.lines.filter((line) => line.lineId !== lineId),
    };
    this.emit();
  }

  public async applyLineDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void> {
    this.discounts.push({ lineId, basisPoints });
  }

  public async setLineQuantity(
    lineId: string,
    quantity: number,
  ): Promise<void> {
    this.edits.push({ operation: "line-quantity", lineId, value: quantity });
  }

  public async setLineUnitPriceCents(
    lineId: string,
    unitPriceCents: number,
  ): Promise<void> {
    this.edits.push({ operation: "line-price", lineId, value: unitPriceCents });
  }

  public async applyLineDiscountAmountCents(
    lineId: string,
    discountCents: number,
  ): Promise<void> {
    this.edits.push({
      operation: "line-discount-amount",
      lineId,
      value: discountCents,
    });
  }

  public async applyLineManualDiscountBasisPoints(
    lineId: string,
    basisPoints: number,
  ): Promise<void> {
    this.edits.push({
      operation: "line-discount-percent",
      lineId,
      value: basisPoints,
    });
  }

  public async applyOrderDiscountAmountCents(
    discountCents: number,
  ): Promise<void> {
    this.edits.push({
      operation: "order-discount-amount",
      value: discountCents,
    });
  }

  public async applyOrderManualDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    this.edits.push({
      operation: "order-discount-percent",
      value: basisPoints,
    });
  }

  public async applyOrderQuickDiscountBasisPoints(
    basisPoints: number,
  ): Promise<void> {
    this.edits.push({
      operation: "order-discount-quick",
      value: basisPoints,
    });
  }

  public async clearCart(): Promise<void> {
    this.edits.push({ operation: "clear-cart" });
    this.snapshot = {
      ...EMPTY_SALE_CART,
      revision: this.snapshot.revision + 1,
    };
    this.emit();
  }

  public async clearAfterCommittedOrder(orderGuid: string): Promise<void> {
    this.clearSignals.push(orderGuid);
    this.snapshot = {
      ...EMPTY_SALE_CART,
      revision: this.snapshot.revision + 1,
    };
    this.emit();
  }

  private updateQuantity(lineId: string, delta: number): void {
    this.snapshot = {
      ...this.snapshot,
      revision: this.snapshot.revision + 1,
      lines: this.snapshot.lines.map((line) =>
        line.lineId === lineId
          ? {
              ...line,
              quantity: String(Math.max(1, Number(line.quantity) + delta)),
            }
          : line,
      ),
    };
    this.emit();
  }

  private emit(): void {
    for (const listener of this.listeners) {
      listener();
    }
  }
}

function cartSnapshot(actualAmountCents = 995): CartSnapshot {
  const line: CartLine = {
    lineId: "line-1",
    productCode: "P-001",
    itemNumber: "I-001",
    lookupCode: "930000000001",
    displayName: "Fresh milk",
    quantity: "1",
    unitPrice: createAud(actualAmountCents),
    discount: createAud(0),
    actualAmount: createAud(actualAmountCents),
    priceSource: "catalog",
    kind: "sale",
    returnSourceKey: null,
    originalOrderGuid: null,
    originalOrderDetailGuid: null,
  };
  return {
    revision: 1,
    mode: "sale",
    lines: [line],
    subtotal: createAud(actualAmountCents),
    discount: createAud(0),
    actualAmount: createAud(actualAmountCents),
  };
}

function workflow(
  completeCash: SalesWorkflowPort["completeCash"] = async () => ({
    completed: true,
    canClearCart: true,
    orderGuid: "order-ui-1",
    cashDueCents: 995,
    changeCents: 5,
    postCommit: { drawerDisposition: "queued" },
  }),
): SalesWorkflowPort {
  return {
    async searchProducts() {
      return [];
    },
    async addProduct() {},
    async addByLookupCode() {},
    async addOpenItem() {},
    completeCash,
    async holdCart() {},
    async lockTerminal() {},
  };
}

function presenter(
  cart: ScreenCartPort,
  options: Readonly<{
    capabilities?: SalesCapabilities;
    workflow?: SalesWorkflowPort;
  }> = {},
): SalesPresenter {
  return new SalesPresenter({
    cart,
    capabilities: options.capabilities ?? ALL_CAPABILITIES,
    workflow: options.workflow ?? workflow(),
    createCheckoutIntentId: () => "checkout-ui-1",
    canStartNewTransaction: () => true,
  });
}

async function pressKeypadKeys(
  screen: Awaited<ReturnType<typeof render>>,
  testIDPrefix: string,
  keys: readonly string[],
): Promise<void> {
  for (const key of keys) {
    await fireEvent.press(screen.getByTestId(`${testIDPrefix}-key-${key}`));
  }
}

afterEach(() => {
  mockStatusStripProps = null;
  usePosShellStore.getState().reset();
  jest.restoreAllMocks();
});

describe("SalesScreen", () => {
  it("无码商品使用自定义小数键盘且不会暴露系统文本输入", async () => {
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-open-item-button"));

    const valueDisplay = screen.getByTestId("sales-open-item-price");
    expect(valueDisplay.props.onChangeText).toBeUndefined();
    expect(valueDisplay.props.autoFocus).toBeUndefined();
    expect(valueDisplay.props.showSoftInputOnFocus).toBeUndefined();
    expect(valueDisplay.props.accessibilityValue).toEqual({ text: "0.00" });
    expect(screen.getByText("0.5")).toBeTruthy();
    expect(screen.getByText("0.99")).toBeTruthy();
    expect(
      screen.getByTestId("sales-open-item-key-clear").props.accessibilityLabel,
    ).toBe("清除");
    expect(
      screen.getByTestId("sales-open-item-key-backspace").props
        .accessibilityLabel,
    ).toBe("退格");

    const firstKey = screen.getByTestId("sales-open-item-key-1");
    expect(
      StyleSheet.flatten(firstKey.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(firstKey);
    await fireEvent.press(screen.getByTestId("sales-open-item-key-2"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-quick-50"));
    expect(
      screen.getByTestId("sales-open-item-price").props.accessibilityValue,
    ).toEqual({ text: "12.50" });

    await fireEvent.press(screen.getByTestId("sales-open-item-key-quick-99"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-decimal"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-8"));
    expect(
      screen.getByTestId("sales-open-item-price").props.accessibilityValue,
    ).toEqual({ text: "12.99" });

    await fireEvent.press(screen.getByTestId("sales-open-item-key-backspace"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-5"));
    expect(
      screen.getByTestId("sales-open-item-price").props.accessibilityValue,
    ).toEqual({ text: "12.95" });

    await fireEvent.press(screen.getByTestId("sales-open-item-key-clear"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-decimal"));
    await fireEvent.press(screen.getByTestId("sales-open-item-key-5"));
    expect(
      screen.getByTestId("sales-open-item-price").props.accessibilityValue,
    ).toEqual({ text: "0.5" });

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("语言切换入口移入状态条并从销售工具栏移除", async () => {
    const onSwitchLanguage = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onSwitchLanguage={onSwitchLanguage}
        presenter={salesPresenter}
      />,
    );

    expect(screen.queryByTestId("sales-switch-language")).toBeNull();
    expect(mockStatusStripProps).toMatchObject({
      language: "zh",
      onSwitchLanguage,
    });
    mockStatusStripProps.onSwitchLanguage();
    expect(onSwitchLanguage).toHaveBeenCalledTimes(1);

    salesPresenter.destroy();
    await screen.unmount();

    const englishPresenter = presenter(
      new ScreenCartPort(EMPTY_SALE_CART),
    );
    const englishScreen = await render(
      <SalesScreen
        locale="en"
        onSwitchLanguage={onSwitchLanguage}
        presenter={englishPresenter}
      />,
    );
    expect(englishScreen.queryByTestId("sales-switch-language")).toBeNull();
    expect(mockStatusStripProps).toMatchObject({
      language: "en",
      onSwitchLanguage,
    });
    englishPresenter.destroy();
    await englishScreen.unmount();
  });

  it("空购物车禁用结账，并为操作按钮保留至少 44pt 触控目标", async () => {
    usePosShellStore.getState().setConnectivity("offline");
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    expect(screen.getByTestId("sales-cart-empty")).toBeTruthy();
    expect(screen.getByTestId("sales-offline-cash-only")).toBeTruthy();
    const checkout = screen.getByTestId("sales-cash-checkout");
    expect(checkout.props.accessibilityState).toEqual({ disabled: true });
    const flattenedStyle = StyleSheet.flatten(checkout.props.style);
    expect(flattenedStyle.minHeight).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("仅在注入导航回调时显示管理入口，并保持 44pt 触控目标", async () => {
    const onOpenHeldOrders = jest.fn();
    const onOpenDailyClose = jest.fn();
    const onOpenRemoteHistory = jest.fn();
    const onOpenReturns = jest.fn();
    const onOpenSpecialProducts = jest.fn();
    const onOpenSyncHistory = jest.fn();
    const onOpenCatalogMaintenance = jest.fn();
    const onOpenCameraScanner = jest.fn();
    const onOpenInstallments = jest.fn();
    const onOpenSettings = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onOpenCameraScanner={onOpenCameraScanner}
        onOpenCatalogMaintenance={onOpenCatalogMaintenance}
        onOpenDailyClose={onOpenDailyClose}
        onOpenHeldOrders={onOpenHeldOrders}
        onOpenInstallments={onOpenInstallments}
        onOpenRemoteHistory={onOpenRemoteHistory}
        onOpenReturns={onOpenReturns}
        onOpenSettings={onOpenSettings}
        onOpenSpecialProducts={onOpenSpecialProducts}
        onOpenSyncHistory={onOpenSyncHistory}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    const heldOrders = screen.getByTestId("sales-open-held-orders");
    expect(screen.getByText("挂单管理")).toBeTruthy();
    expect(
      StyleSheet.flatten(heldOrders.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(heldOrders);
    expect(onOpenHeldOrders).toHaveBeenCalledTimes(1);

    const dailyClose = screen.getByTestId("sales-open-daily-close");
    expect(screen.getByText("日结")).toBeTruthy();
    expect(
      StyleSheet.flatten(dailyClose.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(dailyClose);
    expect(onOpenDailyClose).toHaveBeenCalledTimes(1);

    const returns = screen.getByTestId("sales-open-returns");
    expect(screen.getByText("退货")).toBeTruthy();
    expect(
      StyleSheet.flatten(returns.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(returns);
    expect(onOpenReturns).toHaveBeenCalledTimes(1);

    const remoteHistory = screen.getByTestId("sales-open-remote-history");
    expect(screen.getByText("远程历史")).toBeTruthy();
    expect(
      StyleSheet.flatten(remoteHistory.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(remoteHistory);
    expect(onOpenRemoteHistory).toHaveBeenCalledTimes(1);

    const specialProducts = screen.getByTestId("sales-open-special-products");
    expect(screen.getByText("特殊商品")).toBeTruthy();
    expect(
      StyleSheet.flatten(specialProducts.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(specialProducts);
    expect(onOpenSpecialProducts).toHaveBeenCalledTimes(1);

    const installments = screen.getByTestId("sales-open-installments");
    expect(screen.getByText("分期")).toBeTruthy();
    expect(
      StyleSheet.flatten(installments.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(installments);
    expect(onOpenInstallments).toHaveBeenCalledTimes(1);

    const settings = screen.getByTestId("sales-open-settings");
    expect(screen.getByText("设置")).toBeTruthy();
    expect(
      StyleSheet.flatten(settings.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(settings);
    expect(onOpenSettings).toHaveBeenCalledTimes(1);

    const syncHistory = screen.getByTestId("sales-open-sync-history");
    expect(screen.getByText("同步历史")).toBeTruthy();
    expect(
      StyleSheet.flatten(syncHistory.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(syncHistory);
    expect(onOpenSyncHistory).toHaveBeenCalledTimes(1);

    const catalogMaintenance = screen.getByTestId(
      "sales-open-catalog-maintenance",
    );
    expect(screen.getByText("目录更新")).toBeTruthy();
    expect(
      StyleSheet.flatten(catalogMaintenance.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(catalogMaintenance);
    expect(onOpenCatalogMaintenance).toHaveBeenCalledTimes(1);

    const cameraScanner = screen.getByTestId("sales-open-camera-scanner");
    expect(screen.getByText("相机扫码")).toBeTruthy();
    expect(
      StyleSheet.flatten(cameraScanner.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(cameraScanner);
    expect(onOpenCameraScanner).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("sales-hold")).toBeTruthy();
    expect(screen.getByTestId("sales-lock")).toBeTruthy();

    salesPresenter.destroy();
    await screen.unmount();

    const noNavigationPresenter = presenter(
      new ScreenCartPort(EMPTY_SALE_CART),
    );
    const withoutNavigation = await render(
      <SalesScreen
        locale="en"
        presenter={noNavigationPresenter}
        showStatusStrip={false}
      />,
    );

    expect(
      withoutNavigation.queryByTestId("sales-open-held-orders"),
    ).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-daily-close"),
    ).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-remote-history"),
    ).toBeNull();
    expect(withoutNavigation.queryByTestId("sales-open-returns")).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-special-products"),
    ).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-installments"),
    ).toBeNull();
    expect(withoutNavigation.queryByTestId("sales-open-settings")).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-sync-history"),
    ).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-catalog-maintenance"),
    ).toBeNull();
    expect(
      withoutNavigation.queryByTestId("sales-open-camera-scanner"),
    ).toBeNull();

    noNavigationPresenter.destroy();
    await withoutNavigation.unmount();
  });

  it("强制更新或门禁关闭时空车禁止开始交易，并保留明确更新入口", async () => {
    const onOpenRequiredUpdate = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        newTransactionGate={{
          state: "force-update",
          canStartNewTransaction: false,
          canContinueRecovery: true,
        }}
        onOpenRequiredUpdate={onOpenRequiredUpdate}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    expect(screen.getByTestId("sales-new-transaction-gate")).toBeTruthy();
    expect(screen.getByTestId("sales-search-input").props.editable).toBe(false);
    await fireEvent.press(screen.getByTestId("sales-open-required-update"));
    expect(onOpenRequiredUpdate).toHaveBeenCalledTimes(1);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("搜索系统键盘切换到自定义编辑器时隐藏键盘并保持 HID 暂停", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    const searchInput = screen.getByTestId("sales-search-input");
    expect(searchInput.props.showSoftInputOnFocus).toBe(false);
    expect(searchInput.props.submitBehavior).toBe("blurAndSubmit");
    expect(
      screen.getByText(
        "点击搜索框默认只接收 HID 扫码，并以回车提交；触摸或中文输入请点击上方“键盘”按钮。",
      ),
    ).toBeTruthy();

    jest.useFakeTimers();
    try {
      await fireEvent(searchInput, "focus");
      await fireEvent.press(screen.getByTestId("sales-show-keyboard"));
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(true);
      await fireEvent.press(screen.getByTestId("sales-open-item-button"));
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(false);
      await act(() => {
        jest.runOnlyPendingTimers();
      });
      expect(onManualInputFocusChange).toHaveBeenCalledTimes(1);
      expect(onManualInputFocusChange).toHaveBeenLastCalledWith(true);

      await fireEvent.press(screen.getByText("取消"));
      await act(() => {
        jest.runOnlyPendingTimers();
      });
      expect(onManualInputFocusChange).toHaveBeenNthCalledWith(2, false);
    } finally {
      jest.useRealTimers();
    }

    salesPresenter.destroy();
    await screen.unmount();

    const englishPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const englishScreen = await render(
      <SalesScreen
        locale="en"
        presenter={englishPresenter}
        showStatusStrip={false}
      />,
    );
    expect(
      englishScreen.getByText(
        'Tapping the search field keeps HID-only input and submits scans with Enter. For touch or Chinese input, tap the "Keyboard" button above.',
      ),
    ).toBeTruthy();
    expect(englishScreen.getByText("Keyboard")).toBeTruthy();

    englishPresenter.destroy();
    await englishScreen.unmount();
  });

  it("搜索默认保持 HID 模式，只有键盘按钮开启软键盘且失焦后复位", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    const keyboardButton = screen.getByTestId("sales-show-keyboard");
    expect(keyboardButton.props.accessibilityRole).toBe("button");
    expect(keyboardButton.props.accessibilityLabel).toBe("键盘");
    expect(
      StyleSheet.flatten(keyboardButton.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);

    const searchInput = screen.getByTestId("sales-search-input");
    expect(searchInput.props.showSoftInputOnFocus).toBe(false);
    expect(searchInput.props.submitBehavior).toBe("blurAndSubmit");

    jest.useFakeTimers();
    try {
      await fireEvent(searchInput, "focus");
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(false);

      await fireEvent.press(keyboardButton);
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(true);

      // 已在软键盘模式时再次请求，仍完成 false -> true 的原生刷新。
      await fireEvent.press(keyboardButton);
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(false);
      await act(() => {
        jest.runOnlyPendingTimers();
      });
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(true);

      await fireEvent(screen.getByTestId("sales-search-input"), "blur");
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(false);

      await fireEvent(screen.getByTestId("sales-search-input"), "focus");
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(false);

      await fireEvent.press(keyboardButton);
      expect(
        screen.getByTestId("sales-search-input").props.showSoftInputOnFocus,
      ).toBe(true);
    } finally {
      jest.useRealTimers();
    }

    expect(onManualInputFocusChange.mock.calls).toEqual([[true]]);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("现金收款输入占用和释放手动输入焦点", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(cartSnapshot()));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-cash-checkout"));
    const cashValue = screen.getByTestId("sales-cash-tendered");
    expect(cashValue.props.onChangeText).toBeUndefined();
    expect(cashValue.props.showSoftInputOnFocus).toBeUndefined();

    jest.useFakeTimers();
    try {
      expect(onManualInputFocusChange).toHaveBeenNthCalledWith(1, true);
      await fireEvent.press(screen.getByTestId("sales-cash-exact"));
      expect(
        screen.getByTestId("sales-cash-tendered").props.accessibilityValue,
      ).toEqual({ text: "9.95" });
      await fireEvent.press(screen.getByTestId("sales-cash-cancel"));
      await act(() => {
        jest.runOnlyPendingTimers();
      });
    } finally {
      jest.useRealTimers();
    }
    expect(onManualInputFocusChange).toHaveBeenNthCalledWith(1, true);
    expect(onManualInputFocusChange).toHaveBeenNthCalledWith(2, false);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("无码商品输入取消卸载时释放手动输入焦点", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    jest.useFakeTimers();
    try {
      await fireEvent.press(screen.getByTestId("sales-open-item-button"));
      await fireEvent.press(screen.getByText("取消"));
      await act(() => {
        jest.runOnlyPendingTimers();
      });
    } finally {
      jest.useRealTimers();
    }

    expect(onManualInputFocusChange).toHaveBeenNthCalledWith(1, true);
    expect(onManualInputFocusChange).toHaveBeenNthCalledWith(2, false);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("现金输入在关闭或成功卸载时释放手动输入焦点", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(cartSnapshot()));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    jest.useFakeTimers();
    try {
      await fireEvent.press(screen.getByTestId("sales-cash-checkout"));
      await fireEvent.press(screen.getByTestId("sales-cash-cancel"));
      await act(() => {
        jest.runOnlyPendingTimers();
      });

      await fireEvent.press(screen.getByTestId("sales-cash-checkout"));
      await pressKeypadKeys(screen, "sales-cash", [
        "1",
        "0",
        "decimal",
        "0",
        "0",
      ]);
      await fireEvent.press(screen.getByTestId("sales-cash-confirm"));
      expect(await screen.findByTestId("sales-success")).toBeTruthy();
      await act(() => {
        jest.runOnlyPendingTimers();
      });
    } finally {
      jest.useRealTimers();
    }

    expect(onManualInputFocusChange.mock.calls).toEqual([
      [true],
      [false],
      [true],
      [false],
    ]);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("SalesScreen 卸载时释放仍占用的手动输入焦点", async () => {
    const onManualInputFocusChange = jest.fn();
    const salesPresenter = presenter(new ScreenCartPort(EMPTY_SALE_CART));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onManualInputFocusChange={onManualInputFocusChange}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent(screen.getByTestId("sales-search-input"), "focus");
    await screen.unmount();
    expect(onManualInputFocusChange.mock.calls).toEqual([[true], [false]]);

    salesPresenter.destroy();
  });

  it("在线且购物车非空时开放银行卡/礼券入口，离线时保持禁用", async () => {
    const onOpenPayment = jest.fn();
    usePosShellStore.getState().setConnectivity("online");
    const salesPresenter = presenter(new ScreenCartPort(cartSnapshot()));
    const screen = await render(
      <SalesScreen
        locale="zh"
        onOpenPayment={onOpenPayment}
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    const onlineCheckout = screen.getByTestId("sales-online-checkout");
    expect(onlineCheckout.props.accessibilityState).toMatchObject({
      disabled: false,
    });
    expect(
      StyleSheet.flatten(onlineCheckout.props.style).minHeight,
    ).toBeGreaterThanOrEqual(MIN_TOUCH_TARGET);
    await fireEvent.press(onlineCheckout);
    expect(onOpenPayment).toHaveBeenCalledTimes(1);

    await act(async () => {
      usePosShellStore.getState().setConnectivity("offline");
      await Promise.resolve();
    });
    expect(
      screen.getByTestId("sales-online-checkout").props.accessibilityState,
    ).toMatchObject({ disabled: true });

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("购物车行提供数量、折扣和删除触控入口", async () => {
    const cart = new ScreenCartPort(cartSnapshot());
    const salesPresenter = presenter(cart);
    const screen = await render(
      <SalesScreen
        locale="en"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-line-line-1-increase"));
    expect(salesPresenter.getState().cart.lines[0]?.quantity).toBe("2");

    await fireEvent.press(screen.getByTestId("sales-line-line-1-discount"));
    expect(screen.getByTestId("sales-discount-modal")).toBeTruthy();
    await fireEvent.press(screen.getByTestId("sales-discount-1000"));
    expect(cart.discounts).toEqual([{ lineId: "line-1", basisPoints: 1_000 }]);

    await fireEvent.press(screen.getByTestId("sales-line-line-1-remove"));
    expect(salesPresenter.getState().cart.lines).toHaveLength(0);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("商品行预填值退格后继续逐位输入而不会覆盖整个值", async () => {
    const initial = cartSnapshot(1_234);
    const cart = new ScreenCartPort({
      ...initial,
      lines: initial.lines.map((line) => ({ ...line, quantity: "12" })),
    });
    const salesPresenter = presenter(cart);
    const screen = await render(
      <SalesScreen
        locale="zh"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-line-line-1-edit"));
    await pressKeypadKeys(screen, "sales-line-edit", ["backspace", "3"]);
    expect(
      screen.getByTestId("sales-line-edit-value").props.accessibilityValue,
    ).toEqual({ text: "13" });

    await fireEvent.press(screen.getByTestId("sales-line-edit-price"));
    await pressKeypadKeys(screen, "sales-line-edit", ["backspace", "5"]);
    expect(
      screen.getByTestId("sales-line-edit-value").props.accessibilityValue,
    ).toEqual({ text: "12.35" });

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("OPENITEM、数量、价格和手动行折扣通过触控编辑器调用对应 Port", async () => {
    const cart = new ScreenCartPort(cartSnapshot(2_000));
    const addOpenItem = jest.fn(async (_unitPriceCents: number) => undefined);
    const salesPresenter = presenter(cart, {
      workflow: {
        ...workflow(),
        addOpenItem,
      },
    });
    const screen = await render(
      <SalesScreen
        locale="zh"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-open-item-button"));
    expect(screen.getByTestId("sales-open-item-modal")).toBeTruthy();
    await pressKeypadKeys(screen, "sales-open-item", [
      "1",
      "2",
      "decimal",
      "3",
      "4",
    ]);
    await fireEvent.press(screen.getByTestId("sales-open-item-confirm"));
    expect(addOpenItem).toHaveBeenCalledWith(1_234);

    await fireEvent.press(screen.getByTestId("sales-line-line-1-edit"));
    expect(screen.getByTestId("sales-line-edit-modal")).toBeTruthy();
    expect(
      screen.getByTestId("sales-line-edit-value").props.onChangeText,
    ).toBeUndefined();
    expect(
      screen.queryByTestId("sales-line-edit-key-decimal"),
    ).toBeNull();
    expect(
      screen.queryByTestId("sales-line-edit-key-quick-50"),
    ).toBeNull();
    await pressKeypadKeys(screen, "sales-line-edit", ["3"]);
    await fireEvent.press(screen.getByTestId("sales-line-edit-confirm"));

    await fireEvent.press(screen.getByTestId("sales-line-line-1-edit"));
    await fireEvent.press(screen.getByTestId("sales-line-edit-price"));
    await pressKeypadKeys(screen, "sales-line-edit", ["quick-99"]);
    expect(
      screen.getByTestId("sales-line-edit-value").props.accessibilityValue,
    ).toEqual({ text: "20.99" });
    await pressKeypadKeys(screen, "sales-line-edit", ["clear"]);
    await pressKeypadKeys(screen, "sales-line-edit", [
      "8",
      "quick-50",
    ]);
    await fireEvent.press(screen.getByTestId("sales-line-edit-confirm"));

    await fireEvent.press(screen.getByTestId("sales-line-line-1-discount"));
    await fireEvent.press(screen.getByTestId("sales-line-discount-amount"));
    await pressKeypadKeys(screen, "sales-line-edit", [
      "1",
      "decimal",
      "2",
      "5",
    ]);
    await fireEvent.press(screen.getByTestId("sales-line-edit-confirm"));

    await fireEvent.press(screen.getByTestId("sales-line-line-1-discount"));
    await fireEvent.press(screen.getByTestId("sales-line-discount-percent"));
    await pressKeypadKeys(screen, "sales-line-edit", [
      "1",
      "2",
      "decimal",
      "5",
    ]);
    await fireEvent.press(screen.getByTestId("sales-line-edit-confirm"));

    expect(cart.edits).toEqual([
      { operation: "line-quantity", lineId: "line-1", value: 3 },
      { operation: "line-price", lineId: "line-1", value: 850 },
      {
        operation: "line-discount-amount",
        lineId: "line-1",
        value: 125,
      },
      {
        operation: "line-discount-percent",
        lineId: "line-1",
        value: 1_250,
      },
    ]);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("整单快捷/手动折扣和清空购物车均要求明确确认", async () => {
    const cart = new ScreenCartPort(cartSnapshot(2_000));
    const salesPresenter = presenter(cart);
    const screen = await render(
      <SalesScreen
        locale="en"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-order-discount"));
    expect(screen.getByTestId("sales-order-discount-modal")).toBeTruthy();
    await fireEvent.press(screen.getByTestId("sales-order-discount-2000"));

    await fireEvent.press(screen.getByTestId("sales-order-discount"));
    await fireEvent.press(screen.getByTestId("sales-order-discount-amount"));
    expect(
      screen.getByTestId("sales-order-edit-value").props.onChangeText,
    ).toBeUndefined();
    await pressKeypadKeys(screen, "sales-order-edit", [
      "3",
      "decimal",
      "0",
      "0",
    ]);
    await fireEvent.press(screen.getByTestId("sales-order-edit-confirm"));

    await fireEvent.press(screen.getByTestId("sales-order-discount"));
    await fireEvent.press(screen.getByTestId("sales-order-discount-percent"));
    await pressKeypadKeys(screen, "sales-order-edit", [
      "1",
      "2",
      "decimal",
      "5",
    ]);
    await fireEvent.press(screen.getByTestId("sales-order-edit-confirm"));

    await fireEvent.press(screen.getByTestId("sales-clear-cart"));
    expect(screen.getByTestId("sales-clear-cart-modal")).toBeTruthy();
    await fireEvent.press(screen.getByTestId("sales-clear-cart-cancel"));
    expect(cart.edits).toHaveLength(3);

    await fireEvent.press(screen.getByTestId("sales-clear-cart"));
    await fireEvent.press(screen.getByTestId("sales-clear-cart-confirm"));
    expect(cart.edits).toEqual([
      { operation: "order-discount-quick", value: 2_000 },
      { operation: "order-discount-amount", value: 300 },
      { operation: "order-discount-percent", value: 1_250 },
      { operation: "clear-cart" },
    ]);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("搜索、扫码回车、挂单和锁屏都只调用注入的业务 Port", async () => {
    const searchProducts = jest.fn(async (_query: string) => [
      {
        productCode: "P-SEARCH",
        itemNumber: "I-SEARCH",
        lookupCode: "930000000099",
        displayName: "Search result",
        unitPriceCents: 250,
      },
    ]);
    const addByLookupCode = jest.fn(async (_lookupCode: string) => undefined);
    const holdCart = jest.fn(async () => undefined);
    const lockTerminal = jest.fn(async () => undefined);
    const onSwitchLanguage = jest.fn();
    const injectedWorkflow: SalesWorkflowPort = {
      ...workflow(),
      searchProducts,
      addByLookupCode,
      holdCart,
      lockTerminal,
    };
    const salesPresenter = presenter(new ScreenCartPort(cartSnapshot()), {
      workflow: injectedWorkflow,
    });
    const screen = await render(
      <SalesScreen
        locale="en"
        onSwitchLanguage={onSwitchLanguage}
        presenter={salesPresenter}
      />,
    );

    const searchInput = screen.getByTestId("sales-search-input");
    await fireEvent.changeText(searchInput, "milk");
    await fireEvent.press(screen.getByTestId("sales-search-button"));
    expect(searchProducts).toHaveBeenCalledWith("milk");
    expect(
      await screen.findByTestId("sales-product-P-SEARCH-add"),
    ).toBeTruthy();

    await fireEvent.changeText(searchInput, "930000000099");
    await fireEvent(searchInput, "submitEditing");
    expect(addByLookupCode).toHaveBeenCalledWith("930000000099");

    await fireEvent.press(screen.getByTestId("sales-hold"));
    expect(holdCart).toHaveBeenCalledTimes(1);
    mockStatusStripProps = null;
    await fireEvent.press(screen.getByTestId("sales-lock"));
    expect(lockTerminal).toHaveBeenCalledTimes(1);
    expect(await screen.findByTestId("sales-locked")).toBeTruthy();
    expect(mockStatusStripProps).toMatchObject({
      language: "en",
      onSwitchLanguage,
    });

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("现金确认防重复提交，提交成功后显示找零并清空信号", async () => {
    let resolveCompletion: ((result: SalesCashCompletion) => void) | undefined;
    const pending = new Promise<SalesCashCompletion>((resolve) => {
      resolveCompletion = resolve;
    });
    const completeCash = jest.fn(() => pending);
    const onSwitchLanguage = jest.fn();
    const cart = new ScreenCartPort(cartSnapshot());
    const salesPresenter = presenter(cart, {
      workflow: workflow(completeCash),
    });
    const screen = await render(
      <SalesScreen
        locale="en"
        onSwitchLanguage={onSwitchLanguage}
        presenter={salesPresenter}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-cash-checkout"));
    expect(screen.getByTestId("sales-cash-modal")).toBeTruthy();
    await pressKeypadKeys(screen, "sales-cash", [
      "1",
      "0",
      "decimal",
      "0",
      "0",
    ]);
    const confirm = screen.getByTestId("sales-cash-confirm");
    await fireEvent.press(confirm);
    expect(
      screen.getByTestId("sales-cash-key-1").props.accessibilityState,
    ).toEqual({ disabled: true });
    await fireEvent.press(confirm);
    expect(completeCash).toHaveBeenCalledTimes(1);

    mockStatusStripProps = null;
    await act(async () => {
      resolveCompletion?.({
        completed: true,
        canClearCart: true,
        orderGuid: "order-ui-safe",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "queued" },
      });
      await pending;
    });

    expect(await screen.findByTestId("sales-success")).toBeTruthy();
    expect(mockStatusStripProps).toMatchObject({
      language: "en",
      onSwitchLanguage,
    });
    expect(screen.getByText("$0.05")).toBeTruthy();
    expect(cart.clearSignals).toEqual(["order-ui-safe"]);

    salesPresenter.destroy();
    await screen.unmount();
  });

  it("现金交易已提交但钱箱权限被拒绝时显示非阻塞主管提示", async () => {
    const cart = new ScreenCartPort(cartSnapshot());
    const salesPresenter = presenter(cart, {
      workflow: workflow(async () => ({
        completed: true,
        canClearCart: true,
        orderGuid: "order-drawer-denied",
        cashDueCents: 995,
        changeCents: 5,
        postCommit: { drawerDisposition: "permission-denied" },
      })),
    });
    const screen = await render(
      <SalesScreen
        locale="zh"
        presenter={salesPresenter}
        showStatusStrip={false}
      />,
    );

    await fireEvent.press(screen.getByTestId("sales-cash-checkout"));
    await pressKeypadKeys(screen, "sales-cash", [
      "1",
      "0",
      "decimal",
      "0",
      "0",
    ]);
    await fireEvent.press(screen.getByTestId("sales-cash-confirm"));

    expect(await screen.findByTestId("sales-drawer-warning")).toBeTruthy();
    expect(
      screen.getByText("钱箱未打开：当前收银员没有开钱箱权限，请联系主管。"),
    ).toBeTruthy();

    salesPresenter.destroy();
    await screen.unmount();
  });
});
