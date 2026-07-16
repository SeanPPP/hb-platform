import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import commonEn from "@/locales/en/common.json";
import attendanceEn from "@/locales/en/screens/attendance.json";
import cartEn from "@/locales/en/screens/cart.json";
import domesticPurchaseEn from "@/locales/en/screens/domesticPurchase.json";
import deviceManagementEn from "@/locales/en/screens/deviceManagement.json";
import employeeProfileEn from "@/locales/en/screens/employeeProfile.json";
import employeeProfileReviewEn from "@/locales/en/screens/employeeProfileReview.json";
import homeEn from "@/locales/en/screens/home.json";
import installmentOrdersEn from "@/locales/en/screens/installmentOrders.json";
import localSupplierInvoicesEn from "@/locales/en/screens/localSupplierInvoices.json";
import loginEn from "@/locales/en/screens/login.json";
import ordersEn from "@/locales/en/screens/orders.json";
import advertisementsEn from "@/locales/en/screens/advertisements.json";
import promotionsEn from "@/locales/en/screens/promotions.json";
import productQueryEn from "@/locales/en/screens/productQuery.json";
import seasonalCardsEn from "@/locales/en/screens/seasonalCards.json";
import settingsEn from "@/locales/en/screens/settings.json";
import storeVouchersEn from "@/locales/en/screens/storeVouchers.json";
import userManagementEn from "@/locales/en/screens/userManagement.json";
import warehouseEn from "@/locales/en/screens/warehouse.json";
import commonZh from "@/locales/zh/common.json";
import attendanceZh from "@/locales/zh/screens/attendance.json";
import cartZh from "@/locales/zh/screens/cart.json";
import domesticPurchaseZh from "@/locales/zh/screens/domesticPurchase.json";
import deviceManagementZh from "@/locales/zh/screens/deviceManagement.json";
import employeeProfileZh from "@/locales/zh/screens/employeeProfile.json";
import employeeProfileReviewZh from "@/locales/zh/screens/employeeProfileReview.json";
import homeZh from "@/locales/zh/screens/home.json";
import installmentOrdersZh from "@/locales/zh/screens/installmentOrders.json";
import localSupplierInvoicesZh from "@/locales/zh/screens/localSupplierInvoices.json";
import loginZh from "@/locales/zh/screens/login.json";
import ordersZh from "@/locales/zh/screens/orders.json";
import advertisementsZh from "@/locales/zh/screens/advertisements.json";
import promotionsZh from "@/locales/zh/screens/promotions.json";
import productQueryZh from "@/locales/zh/screens/productQuery.json";
import seasonalCardsZh from "@/locales/zh/screens/seasonalCards.json";
import settingsZh from "@/locales/zh/screens/settings.json";
import storeVouchersZh from "@/locales/zh/screens/storeVouchers.json";
import userManagementZh from "@/locales/zh/screens/userManagement.json";
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
    attendance: attendanceZh,
    login: loginZh,
    home: homeZh,
    installmentOrders: installmentOrdersZh,
    localSupplierInvoices: localSupplierInvoicesZh,
    cart: cartZh,
    domesticPurchase: domesticPurchaseZh,
    deviceManagement: deviceManagementZh,
    employeeProfile: employeeProfileZh,
    employeeProfileReview: employeeProfileReviewZh,
    orders: ordersZh,
    advertisements: advertisementsZh,
    promotions: promotionsZh,
    settings: settingsZh,
    seasonalCards: seasonalCardsZh,
    storeVouchers: storeVouchersZh,
    productQuery: productQueryZh,
    userManagement: userManagementZh,
    warehouse: warehouseZh,
  },
  en: {
    common: commonEn,
    attendance: attendanceEn,
    login: loginEn,
    home: homeEn,
    installmentOrders: installmentOrdersEn,
    localSupplierInvoices: localSupplierInvoicesEn,
    cart: cartEn,
    domesticPurchase: domesticPurchaseEn,
    deviceManagement: deviceManagementEn,
    employeeProfile: employeeProfileEn,
    employeeProfileReview: employeeProfileReviewEn,
    orders: ordersEn,
    advertisements: advertisementsEn,
    promotions: promotionsEn,
    settings: settingsEn,
    seasonalCards: seasonalCardsEn,
    storeVouchers: storeVouchersEn,
    productQuery: productQueryEn,
    userManagement: userManagementEn,
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
    ns: ["common", "advertisements", "attendance", "login", "home", "cart", "domesticPurchase", "deviceManagement", "employeeProfile", "employeeProfileReview", "installmentOrders", "localSupplierInvoices", "orders", "promotions", "seasonalCards", "settings", "storeVouchers", "productQuery", "userManagement", "warehouse"],
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
