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

test("外接客显使用中英文键渲染且整个 surface 永远不接收触摸", async () => {
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
  expect(screen.getAllByText("找零")).toHaveLength(2);
  expect(screen.getByText("1 件商品")).toBeTruthy();
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
  expect(englishScreen.getByText("1 item")).toBeTruthy();
  expect(englishScreen.getByText("Discount")).toBeTruthy();
  expect(englishScreen.getByText("−$1.00")).toBeTruthy();
  expect(
    englishScreen.queryByText("customerDisplay.items"),
  ).toBeNull();
});

test("idle 空购物篮且有广告时 RN surface 全屏透明且不渲染交易面板", async () => {
  const screen = await renderSurface(
    snapshot("idle", { advert, items: [] }),
  );
  const surfaceStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-surface").props.style,
  );

  expect(
    screen.queryByTestId("external-display-transaction-panel"),
  ).toBeNull();
  expect(surfaceStyle).toMatchObject({ backgroundColor: "transparent" });
  expect(surfaceStyle).not.toHaveProperty("gap");
  expect(surfaceStyle).not.toHaveProperty("paddingHorizontal");
  expect(surfaceStyle).not.toHaveProperty("paddingVertical");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ backgroundColor: "transparent", flex: 1 });
  expect(screen.queryByText("Welcome")).toBeNull();
});

test("idle 无广告时保留安全双栏占位", async () => {
  const screen = await renderSurface(
    snapshot("idle", { advert: null, items: [] }),
  );
  const surfaceStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-surface").props.style,
  );
  const transactionPanelStyle = StyleSheet.flatten(
    screen.getByTestId("external-display-transaction-panel").props.style,
  );

  expect(screen.getByText("Welcome")).toBeTruthy();
  expect(screen.getByText("Your basket is empty")).toBeTruthy();
  expect(surfaceStyle).toMatchObject({
    gap: 32,
    paddingHorizontal: 34,
    paddingVertical: 28,
  });
  expect(transactionPanelStyle).toMatchObject({ flex: 3 });
  expect(transactionPanelStyle).not.toHaveProperty("paddingHorizontal");
  expect(transactionPanelStyle).not.toHaveProperty("paddingVertical");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ flex: 2 });
});

test("idle 有商品时即使有广告也保留 3:2 交易布局", async () => {
  const screen = await renderSurface(snapshot("idle", { advert }));

  expect(screen.getByText("Tea")).toBeTruthy();
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-transaction-panel").props.style,
    ),
  ).toMatchObject({ flex: 3 });
  expect(
    StyleSheet.flatten(
      screen.getByTestId("external-display-advert-window").props.style,
    ),
  ).toMatchObject({ flex: 2 });
});

test.each(["cart", "payment", "change", "success"] as const)(
  "%s 模式即使有广告也保留 3:2 交易布局",
  async (mode) => {
    const screen = await renderSurface(snapshot(mode, { advert }));

    expect(screen.getByText("Tea")).toBeTruthy();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("external-display-transaction-panel").props.style,
      ),
    ).toMatchObject({ flex: 3 });
    expect(
      StyleSheet.flatten(
        screen.getByTestId("external-display-advert-window").props.style,
      ),
    ).toMatchObject({ flex: 2 });
  },
);

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
  expect(screen.getByText("Thank you")).toBeTruthy();
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

  expect(screen.getByText("Thank you")).toBeTruthy();
  expect(screen.getByText("Latest")).toBeTruthy();
  expect(screen.queryByText("Payment")).toBeNull();
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
