import { useTranslation } from "react-i18next";
import { i18n } from "@/shared/i18n/i18n";
import { DEFAULT_APP_LANGUAGE, type AppLanguage } from "@/shared/i18n/types";

export function useAppTranslation(namespace?: string | string[]) {
  const translation = useTranslation(namespace ?? "common");

  return {
    ...translation,
    language: (i18n.resolvedLanguage as AppLanguage | undefined) ?? DEFAULT_APP_LANGUAGE,
  };
}
