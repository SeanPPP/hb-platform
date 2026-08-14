import { afterEach, expect, test } from "@jest/globals";
import { act, render } from "@testing-library/react-native";
import { createInstance } from "i18next";
import {
  I18nextProvider,
  initReactI18next,
} from "react-i18next";
import { StyleSheet } from "react-native";

import {
  ExternalDisplaySurface,
  registerExternalDisplayReactSurface,
} from "./external-display-react-surface";

import type { CustomerDisplaySnapshot } from "@/core/contracts";
import type { ExternalDisplayNativeModule } from "@/core/peripherals/customer-display/native/external-display-bridge";
import en from "@/i18n/locales/en.json";
import zh from "@/i18n/locales/zh.json";

const advert: NonNullable<CustomerDisplaySnapshot["advert"]> = {
  kind: "image",
  localUri: "file:///customer-display/adverts/welcome.png",
};

afterEach(() => {
  registerExternalDisplayReactSurface(null);
});

test("外接客显固定使用英文且整个 surface 永远不接收触摸", async () => {
  const chinese = await createTestI18n("zh");
  const screen = await render(
    <I18nextProvider i18n={chinese}>
      <ExternalDisplaySurface
        snapshot={snapshot("change")}
        surfaceId="external-1"
      />
    </I18nextProvider>,
  );

  expect(screen.getByTestId("external-display-surface").props.pointerEvents).toBe(
    "none",
  );
  expect(screen.getByTestId("external-display-surface").props.accessible).toBe(
    false,
  );
  expect(screen.getByText("Customer Display")).toBeTruthy();
  expect(screen.getByText("Your order")).toBeTruthy();
  expect(screen.getByText("Your change")).toBeTruthy();
  expect(screen.getByText("2 items · 1 SKU")).toBeTruthy();
  expect(screen.queryByText("客显")).toBeNull();
  expect(screen.queryByText("您的订单")).toBeNull();
  expect(screen.queryByText("找零")).toBeNull();
  expect(screen.getByText("Tea")).toBeTruthy();
  expect(screen.queryAllByRole("button")).toHaveLength(0);
  screen.unmount();

  const english = await createTestI18n("en");
  const englishScreen = await render(
    <I18nextProvider i18n={english}>
      <ExternalDisplaySurface
        snapshot={snapshot("cart")}
        surfaceId="external-2"
      />
    </I18nextProvider>,
  );
  expect(englishScreen.getByText("Your order")).toBeTruthy();
  expect(englishScreen.getByText("2 items · 1 SKU")).toBeTruthy();
  expect(englishScreen.getByText("Ready to pay")).toBeTruthy();
  expect(englishScreen.getByText("Discount")).toBeTruthy();
  expect(englishScreen.getByText("−$1.00")).toBeTruthy();
  expect(
    englishScreen.queryByText("customerDisplay.items"),
  ).toBeNull();
});

test("交易客显按参考图显示标题、四列表格、等宽广告和全宽汇总栏", async () => {
  const screen = await renderSurface(designedSnapshot("cart"));

  expect(screen.getByText("Customer Display")).toBeTruthy();
  expect(screen.queryByTestId("external-display-close")).toBeNull();
  expect(screen.getByText("Your order")).toBeTruthy();
  expect(screen.getByText("Product")).toBeTruthy();
  expect(screen.getByText("Qty")).toBeTruthy();
  expect(screen.getByText("Unit price")).toBeTruthy();
  expect(screen.getByText("Amount")).toBeTruthy();
  expect(screen.getByText("$6.67")).toBeTruthy();
  expect(screen.getByText("$13.34")).toBeTruthy();
  expect(screen.getByText("2 items · 1 SKU")).toBeTruthy();
  expect(screen.getByTestId("external-display-summary-panel")).toBeTruthy();
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-title-bar").props.style,
    ),
  ).toMatchObject({ height: 48 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-summary-panel").props.style,
    ),
  ).toMatchObject({ height: 132 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-summary-metrics").props.style,
    ),
  ).toMatchObject({ flex: 47 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-amount-due").props.style,
    ),
  ).toMatchObject({ flex: 26 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-status-region").props.style,
    ),
  ).toMatchObject({ flex: 27 });

  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-transaction-panel").props.style,
    ),
  ).toMatchObject({ flex: 1 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ flex: 1 });
});

test.each([
  ["idle", "Ready when you are", "Scan an item to begin"],
  ["cart", "Ready to pay", "Please follow the cashier's instructions"],
  ["payment", "Payment in progress", "Please follow the terminal prompts"],
  ["change", "Your change", "$5.00"],
  ["success", "Payment complete", "Change $5.00"],
] as const)(
  "%s 状态使用独立状态卡且不改变订单区标题",
  async (mode, title, subtitle) => {
    const screen = await renderSurface(
      designedSnapshot(mode, { change: { currency: "AUD", cents: 500 } }),
    );

    expect(screen.getByText("Your order")).toBeTruthy();
    expect(screen.getByTestId("external-display-status-card")).toBeTruthy();
    expect(screen.getByText(title)).toBeTruthy();
    expect(screen.getByText(subtitle)).toBeTruthy();
  },
);

test("idle 空购物篮且有广告时 RN surface 全屏透明且不渲染交易面板", async () => {
  const screen = await renderSurface(
    snapshot("idle", { advert, items: [] }),
  );
  const surfaceStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-surface").props.style,
  );
  const advertWindowStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-advert-window").props.style,
  );

  expect(
    screen.queryByTestId("external-display-transaction-panel"),
  ).toBeNull();
  expect(surfaceStyle).toMatchObject({
    backgroundColor: "transparent",
    flex: 1,
  });
  expect(surfaceStyle).not.toHaveProperty("gap");
  expect(surfaceStyle).not.toHaveProperty("paddingHorizontal");
  expect(surfaceStyle).not.toHaveProperty("paddingVertical");
  expect(advertWindowStyle).toMatchObject({
    backgroundColor: "transparent",
    flex: 1,
  });
  expect(advertWindowStyle).not.toHaveProperty("borderRadius");
  expect(advertWindowStyle).not.toHaveProperty("borderWidth");
  expect(advertWindowStyle).not.toHaveProperty("margin");
  expect(advertWindowStyle).not.toHaveProperty("padding");
  expect(screen.queryByText("Welcome")).toBeNull();
});

test("idle 无广告时保留标题、等宽双栏和结算占位", async () => {
  const screen = await renderSurface(
    snapshot("idle", { advert: null, items: [] }),
  );
  const surfaceStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-surface").props.style,
  );
  const transactionPanelStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-transaction-panel").props.style,
  );

  expect(screen.getByText("Customer Display")).toBeTruthy();
  expect(screen.getByText("Your order")).toBeTruthy();
  expect(screen.getByText("Your basket is empty")).toBeTruthy();
  expect(screen.getByText("Ready when you are")).toBeTruthy();
  expect(surfaceStyle).toMatchObject({ flex: 1, flexDirection: "column" });
  expect(transactionPanelStyle).toMatchObject({ flex: 1 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ flex: 1 });
});

test("idle 有商品时即使有广告也保留左右等宽交易布局", async () => {
  const screen = await renderSurface(snapshot("idle", { advert }));

  expect(screen.getByText("Tea")).toBeTruthy();
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-transaction-panel").props.style,
    ),
  ).toMatchObject({ flex: 1 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ flex: 1 });
});

test.each(["cart", "payment", "change", "success"] as const)(
  "%s 模式即使有广告也保留左右等宽交易布局",
  async (mode) => {
    const screen = await renderSurface(snapshot(mode, { advert }));

    expect(screen.getByText("Tea")).toBeTruthy();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("external-display-transaction-panel").props.style,
      ),
    ).toMatchObject({ flex: 1 });
    expect(
      StyleSheet.flatten(
        screen.getByTestId("external-display-advert-window").props.style,
      ),
    ).toMatchObject({ flex: 1 });
  },
);

test("旧快照默认显示末尾 12 行，商品行固定 32pt 并从顶部依次排列", async () => {
  const items = Array.from({ length: 14 }, (_, index) =>
    item(`Item ${index + 1}`),
  );
  const screen = await renderSurface(snapshot("cart", { items }));

  expect(screen.queryByText("Item 1")).toBeNull();
  expect(screen.queryByText("Item 2")).toBeNull();
  expect(screen.getByText("Item 3")).toBeTruthy();
  expect(screen.getByText("Item 14")).toBeTruthy();
  expect(screen.getByText("2 earlier")).toBeTruthy();
  expect(screen.getAllByText("—")).toHaveLength(12);
  const rows = screen.getAllByTestId("external-display-item-row");
  expect(rows).toHaveLength(12);
  for (const row of rows) {
    const style = StyleSheet.flatten(row.props.style);
    expect(style).toMatchObject({
      height: 32,
      flexGrow: 0,
      flexShrink: 0,
      borderBottomWidth: 1,
    });
    expect(style).not.toHaveProperty("flex");
  }
});

test("显式窗口保持原顺序，并在订单标题右侧显示上下隐藏数量", async () => {
  const items = Array.from({ length: 20 }, (_, index) =>
    item(`Item ${index + 1}`),
  );
  const screen = await renderSurface(
    snapshot("cart", { items, visibleItemStart: 4 }),
  );

  expect(screen.queryByText("Item 4")).toBeNull();
  expect(screen.getByText("Item 5")).toBeTruthy();
  expect(screen.getByText("Item 16")).toBeTruthy();
  expect(screen.queryByText("Item 17")).toBeNull();
  expect(screen.getByText("4 earlier · 4 later")).toBeTruthy();
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-order-heading").props.style,
    ),
  ).toMatchObject({ flexDirection: "row" });
  expect(
    screen.getByTestId("external-display-hidden-items").parent?.props.testID,
  ).toBe("external-display-order-heading");
});

test("中文主界面下客显窗口提示仍固定为英文", async () => {
  const chinese = await createTestI18n("zh");
  const items = Array.from({ length: 15 }, (_, index) =>
    item(`商品 ${index + 1}`),
  );
  const screen = await render(
    <I18nextProvider i18n={chinese}>
      <ExternalDisplaySurface
        snapshot={snapshot("cart", { items, visibleItemStart: 2 })}
        surfaceId="external-zh-window"
      />
    </I18nextProvider>,
  );

  expect(screen.getByText("商品 3")).toBeTruthy();
  expect(screen.getByText("商品 14")).toBeTruthy();
  expect(screen.getByText("2 earlier · 1 later")).toBeTruthy();
  expect(screen.queryByText("上方 2 件 · 下方 1 件")).toBeNull();
});

test("成功且无需找零时状态卡显示感谢语", async () => {
  const screen = await renderSurface(
    designedSnapshot("success", {
      change: { currency: "AUD", cents: 0 },
    }),
  );

  expect(screen.getByText("Payment complete")).toBeTruthy();
  expect(screen.getByText("Thank you for shopping with us")).toBeTruthy();
});

test("快照事件只接受严格更高 revision 并忽略相同或迟到结果", async () => {
  const nativeModule = installNativeModule();
  const screen = await renderSurface(
    snapshot("cart", {
      items: [item("Initial")],
      revision: 5,
    }),
  );

  await nativeModule.emit(
    snapshot("success", {
      items: [item("Latest")],
      revision: 7,
    }),
  );
  expect(screen.getByText("Payment complete")).toBeTruthy();
  expect(screen.getByText("Latest")).toBeTruthy();

  await nativeModule.emit(
    snapshot("payment", {
      items: [item("Same revision")],
      revision: 7,
    }),
  );
  await nativeModule.emit(
    snapshot("payment", {
      items: [item("Late")],
      revision: 6,
    }),
  );

  expect(screen.getByText("Payment complete")).toBeTruthy();
  expect(screen.getByText("Latest")).toBeTruthy();
  expect(screen.queryByText("Payment in progress")).toBeNull();
  expect(screen.queryByText("Same revision")).toBeNull();
  expect(screen.queryByText("Late")).toBeNull();
});

async function renderSurface(value: CustomerDisplaySnapshot | null) {
  const i18n = await createTestI18n("en");
  return render(
    <I18nextProvider i18n={i18n}>
      <ExternalDisplaySurface
        snapshot={value}
        surfaceId="external-test"
      />
    </I18nextProvider>,
  );
}

async function createTestI18n(language: "en" | "zh") {
  const instance = createInstance();
  await instance.use(initReactI18next).init({
    compatibilityJSON: "v4",
    fallbackLng: "en",
    lng: language,
    resources: {
      en: { translation: en },
      zh: { translation: zh },
    },
    interpolation: { escapeValue: false },
  });
  return instance;
}

function snapshot(
  mode: CustomerDisplaySnapshot["mode"],
  overrides: Partial<CustomerDisplaySnapshot> = {},
): CustomerDisplaySnapshot {
  return {
    revision: 2,
    mode,
    items: [item("Tea")],
    gst: { currency: "AUD", cents: 112 },
    discount: { currency: "AUD", cents: 100 },
    total: { currency: "AUD", cents: 1_234 },
    change: { currency: "AUD", cents: 500 },
    advert: null,
    ...overrides,
  };
}

function designedSnapshot(
  mode: CustomerDisplaySnapshot["mode"],
  overrides: Partial<CustomerDisplaySnapshot> = {},
): CustomerDisplaySnapshot {
  const current = snapshot(mode, overrides);
  return {
    ...current,
    items: current.items.map((value) => ({
      ...value,
      unitPrice: { currency: "AUD", cents: 667 },
    })),
    summary: {
      itemQuantity: "2",
      skuCount: 1,
      subtotal: { currency: "AUD", cents: 1_334 },
    },
  };
}

function item(name: string): CustomerDisplaySnapshot["items"][number] {
  return {
    name,
    quantity: "2",
    amount: { currency: "AUD", cents: 1_234 },
  };
}

function installNativeModule() {
  let snapshotListener:
    | ((value: CustomerDisplaySnapshot) => void)
    | null = null;
  const status = {
    state: "ready" as const,
    enabled: true,
    connected: true,
    revision: 0,
    widthPixels: 1920,
    heightPixels: 1080,
    scale: 1,
    reason: "ready",
  };
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return status;
    },
    async setEnabled() {
      return status;
    },
    async publishSnapshot(value) {
      return {
        accepted: true,
        revision: value.revision,
        latestRevision: value.revision,
        reason: "accepted",
      };
    },
    async markReactSurfaceReady() {},
    async markReactSurfaceRendered() {},
    addListener(eventName, listener) {
      if (eventName === "onSnapshotChanged") {
        snapshotListener = listener as (
          value: CustomerDisplaySnapshot,
        ) => void;
      }
      return { remove() {} };
    },
  };
  registerExternalDisplayReactSurface(nativeModule);

  return {
    async emit(value: CustomerDisplaySnapshot) {
      await act(async () => {
        snapshotListener?.(value);
      });
    },
  };
}
