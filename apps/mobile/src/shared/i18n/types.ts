export const APP_LANGUAGES = ["zh", "en"] as const;

export type AppLanguage = (typeof APP_LANGUAGES)[number];

export const DEFAULT_APP_LANGUAGE: AppLanguage = "zh";

export function isAppLanguage(value: string | null | undefined): value is AppLanguage {
  return value === "zh" || value === "en";
}

export function normalizeLanguageTag(value: string | null | undefined): AppLanguage {
  if (!value) {
    return DEFAULT_APP_LANGUAGE;
  }

  const normalized = value.toLowerCase();
  return normalized.startsWith("en") ? "en" : "zh";
}

export function resolveLocaleTag(language: AppLanguage) {
  return language === "en" ? "en-AU" : "zh-CN";
}
