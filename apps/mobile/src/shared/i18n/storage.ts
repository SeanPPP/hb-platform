import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { isAppLanguage, type AppLanguage } from "@/shared/i18n/types";

const APP_LANGUAGE_STORAGE_KEY = "app_language";

export async function getStoredLanguage() {
  const value = await AppAsyncStorage.getString(APP_LANGUAGE_STORAGE_KEY);
  return isAppLanguage(value) ? value : null;
}

export async function setStoredLanguage(language: AppLanguage) {
  await AppAsyncStorage.setString(APP_LANGUAGE_STORAGE_KEY, language);
}
