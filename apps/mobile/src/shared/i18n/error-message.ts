import { isAxiosError } from "axios";

type TranslationOptions = Record<string, unknown>;

type TranslateFn = (key: string, options?: TranslationOptions) => string;

type ResolveLocalizedErrorOptions = {
  language: string;
  t: TranslateFn;
  fallbackKey?: string;
  fallbackText?: string;
  allowRawMessageInChinese?: boolean;
};

type ApiErrorBody = {
  message?: unknown;
  Message?: unknown;
  error?: unknown;
  Error?: unknown;
};

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null ? (value as Record<string, unknown>) : null;
}

function asString(value: unknown) {
  return typeof value === "string" ? value.trim() : "";
}

function getStatus(error: unknown) {
  if (isAxiosError(error)) {
    return error.response?.status;
  }

  const record = asRecord(error);
  const response = asRecord(record?.response);
  return typeof response?.status === "number" ? response.status : undefined;
}

function getCode(error: unknown) {
  if (isAxiosError(error)) {
    return error.code;
  }

  const record = asRecord(error);
  return typeof record?.code === "string" ? record.code : undefined;
}

function getMessage(error: unknown): string {
  if (isAxiosError<ApiErrorBody>(error)) {
    return asString(
      error.response?.data?.message
        ?? error.response?.data?.Message
        ?? error.response?.data?.error
        ?? error.response?.data?.Error
        ?? error.message
    );
  }

  if (error instanceof Error) {
    return error.message.trim();
  }

  const record = asRecord(error);
  return asString(record?.message ?? record?.Message ?? record?.error ?? record?.Error);
}

function includesAny(message: string, needles: string[]) {
  const normalized = message.toLowerCase();
  return needles.some((needle) => normalized.includes(needle.toLowerCase()));
}

function isChineseLanguage(language: string) {
  return language.toLowerCase().startsWith("zh");
}

function resolveCommonErrorKey(error: unknown) {
  const status = getStatus(error);
  const code = getCode(error);
  const message = getMessage(error);

  if (code === "ECONNABORTED" || code === "ETIMEDOUT" || includesAny(message, ["timeout", "timed out", "超时"])) {
    return "common:errors.timeout";
  }

  if (message === "Network Error" || code === "ERR_NETWORK") {
    return "common:errors.network";
  }

  if (
    status === 401
    || status === 403
    || includesAny(message, [
      "未登录",
      "请登录",
      "登录已过期",
      "登陆已过期",
      "登录失效",
      "登陆失效",
      "unauthorized",
      "unauthenticated",
      "forbidden",
      "token expired",
      "session expired",
      "no refresh token",
      "auth",
      "设备未授权",
      "设备授权失败",
    ])
  ) {
    return "common:errors.unauthorized";
  }

  if (status && status >= 500) {
    return "common:errors.server";
  }

  if (includesAny(message, ["request failed"])) {
    return "common:errors.requestFailed";
  }

  return null;
}

export function resolveLocalizedErrorMessage(
  error: unknown,
  options: ResolveLocalizedErrorOptions
) {
  const commonErrorKey = resolveCommonErrorKey(error);
  if (commonErrorKey) {
    return options.t(commonErrorKey);
  }

  const rawMessage = getMessage(error);
  if (rawMessage && options.allowRawMessageInChinese !== false && isChineseLanguage(options.language)) {
    return rawMessage;
  }

  if (options.fallbackText) {
    return options.fallbackText;
  }

  if (options.fallbackKey) {
    return options.t(options.fallbackKey);
  }

  return options.t("common:errors.requestFailed");
}
