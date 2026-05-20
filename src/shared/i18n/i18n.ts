import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import commonEn from "@/locales/en/common.json";
import cartEn from "@/locales/en/screens/cart.json";
import employeeProfileEn from "@/locales/en/screens/employeeProfile.json";
import homeEn from "@/locales/en/screens/home.json";
import loginEn from "@/locales/en/screens/login.json";
import ordersEn from "@/locales/en/screens/orders.json";
import productQueryEn from "@/locales/en/screens/productQuery.json";
import settingsEn from "@/locales/en/screens/settings.json";
import warehouseEn from "@/locales/en/screens/warehouse.json";
import commonZh from "@/locales/zh/common.json";
import cartZh from "@/locales/zh/screens/cart.json";
import employeeProfileZh from "@/locales/zh/screens/employeeProfile.json";
import homeZh from "@/locales/zh/screens/home.json";
import loginZh from "@/locales/zh/screens/login.json";
import ordersZh from "@/locales/zh/screens/orders.json";
import productQueryZh from "@/locales/zh/screens/productQuery.json";
import settingsZh from "@/locales/zh/screens/settings.json";
import warehouseZh from "@/locales/zh/screens/warehouse.json";
import { getStoredLanguage, setStoredLanguage } from "@/shared/i18n/storage";
import {
  DEFAULT_APP_LANGUAGE,
  normalizeLanguageTag,
  type AppLanguage,
} from "@/shared/i18n/types";

const resources = {
  zh: {
    common: commonZh,
    login: loginZh,
    home: homeZh,
    cart: cartZh,
    employeeProfile: employeeProfileZh,
    orders: ordersZh,
    settings: settingsZh,
    productQuery: productQueryZh,
    warehouse: warehouseZh,
  },
  en: {
    common: commonEn,
    login: loginEn,
    home: homeEn,
    cart: cartEn,
    employeeProfile: employeeProfileEn,
    orders: ordersEn,
    settings: settingsEn,
    productQuery: productQueryEn,
    warehouse: warehouseEn,
  },
} as const;

let initPromise: Promise<void> | null = null;

function detectDeviceLanguage(): AppLanguage {
  try {
    const intlLocale = Intl.DateTimeFormat().resolvedOptions().locale;
    return normalizeLanguageTag(intlLocale);
  } catch {
    return DEFAULT_APP_LANGUAGE;
  }
}

async function detectInitialLanguage() {
  const storedLanguage = await getStoredLanguage();
  return storedLanguage ?? detectDeviceLanguage() ?? DEFAULT_APP_LANGUAGE;
}

if (!i18n.isInitialized) {
  void i18n.use(initReactI18next).init({
    resources,
    lng: DEFAULT_APP_LANGUAGE,
    fallbackLng: DEFAULT_APP_LANGUAGE,
    defaultNS: "common",
    ns: ["common", "login", "home", "cart", "employeeProfile", "orders", "settings", "productQuery", "warehouse"],
    interpolation: {
      escapeValue: false,
    },
    compatibilityJSON: "v4",
    returnNull: false,
  });
}

export async function initI18n() {
  if (!initPromise) {
    initPromise = (async () => {
      const language = await detectInitialLanguage();
      if (i18n.resolvedLanguage !== language) {
        await i18n.changeLanguage(language);
      }
    })();
  }

  await initPromise;
}

export async function setAppLanguage(language: AppLanguage) {
  await setStoredLanguage(language);
  await i18n.changeLanguage(language);
}

export { getStoredLanguage, i18n };
