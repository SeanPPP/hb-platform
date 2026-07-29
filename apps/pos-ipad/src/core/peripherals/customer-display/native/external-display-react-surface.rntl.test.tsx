import { expect, test } from "@jest/globals";
import { render } from "@testing-library/react-native";
import { createInstance } from "i18next";
import {
  I18nextProvider,
  initReactI18next,
} from "react-i18next";

import { ExternalDisplaySurface } from "./external-display-react-surface";

import type { CustomerDisplaySnapshot } from "@/core/contracts";
import en from "@/i18n/locales/en.json";
import zh from "@/i18n/locales/zh.json";

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
  expect(
    englishScreen.queryByText("customerDisplay.items"),
  ).toBeNull();
});

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
  mode: "cart" | "change",
): CustomerDisplaySnapshot {
  return {
    revision: 2,
    mode,
    items: [
      {
        name: "Tea",
        quantity: "2",
        amount: { currency: "AUD", cents: 1_234 },
      },
    ],
    gst: { currency: "AUD", cents: 112 },
    discount: { currency: "AUD", cents: 100 },
    total: { currency: "AUD", cents: 1_234 },
    change: { currency: "AUD", cents: 500 },
    advert: null,
  };
}
