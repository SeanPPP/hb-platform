import * as Localization from "expo-localization";
import { createInstance } from "i18next";
import { initReactI18next } from "react-i18next";

import en from "@/i18n/locales/en.json";
import zh from "@/i18n/locales/zh.json";
import {
  readStoredLanguage,
  saveStoredLanguage,
  type AppLanguage,
} from "@/ui/preferences/terminal-ui-preferences";

const i18n = createInstance();

function detectDeviceLanguage(): AppLanguage {
  return Localization.getLocales()[0]?.languageCode === "zh" ? "zh" : "en";
}

export function resolveInitialLanguage(): AppLanguage {
  return readStoredLanguage() ?? detectDeviceLanguage();
}

const preferredLanguage = resolveInitialLanguage();

void i18n.use(initReactI18next).init({
  compatibilityJSON: "v4",
  fallbackLng: "en",
  lng: preferredLanguage,
  resources: {
    en: { translation: en },
    zh: { translation: zh },
  },
  interpolation: {
    escapeValue: false,
  },
});

export async function changeAppLanguage(language: AppLanguage): Promise<void> {
  await i18n.changeLanguage(language);
  await saveStoredLanguage(language);
}

export async function toggleAppLanguage(): Promise<AppLanguage> {
  const currentLanguage = i18n.resolvedLanguage ?? i18n.language;
  const nextLanguage: AppLanguage = currentLanguage.startsWith("zh") ? "en" : "zh";
  await changeAppLanguage(nextLanguage);
  return nextLanguage;
}

export type { AppLanguage } from "@/ui/preferences/terminal-ui-preferences";
export default i18n;
