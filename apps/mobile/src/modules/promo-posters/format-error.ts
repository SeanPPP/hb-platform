import { resolveLocalizedErrorMessage } from "@/shared/i18n/error-message";

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/** 海报相关错误提示：分享不可用单独映射，其余走通用本地化（中文界面直接展示后端给出的原因）。 */
export function formatPromoPosterError(error: unknown, t: TranslateFn, language: string, fallbackKey: string) {
  const code = (error as { code?: unknown } | null)?.code;
  if (code === "PROMO_POSTER_SHARE_UNAVAILABLE") {
    return t("poster.messages.shareUnavailable");
  }
  if (code === "PROMO_POSTER_PDF_EMPTY" || code === "PROMO_POSTER_DEFAULTS_EMPTY") {
    return t(fallbackKey);
  }
  return resolveLocalizedErrorMessage(error, { t, language, fallbackKey });
}
