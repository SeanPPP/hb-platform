export const IOS_NATIVE_UPDATE_CENTER_BASE_URL = "https://hotbargain.vip/api";

function normalizeBuildProfile(value: unknown) {
  return typeof value === "string" ? value.trim().toLowerCase() : "";
}

function normalizeNonProductionOverride(value: unknown) {
  if (typeof value !== "string" || !value.trim()) {
    return null;
  }

  let url: URL;
  try {
    url = new URL(value.trim());
  } catch {
    throw new Error("iOS native update center URL is not trusted");
  }

  // 更新决策可产生离线 required 门禁；非生产显式覆盖也必须避免明文、凭据和重定向参数。
  if (
    url.protocol !== "https:"
    || !url.hostname
    || url.username
    || url.password
    || url.search
    || url.hash
  ) {
    throw new Error("iOS native update center URL is not trusted");
  }

  return url.toString().replace(/\/+$/, "");
}

/**
 * iOS 原生版本策略与日常业务 API 完全隔离，避免门店 Host 被写入后伪造 required 策略。
 * 正式包固定中心地址；开发者显式覆盖仅用于非生产手工验证，且自动检查仍由 profile 门禁关闭。
 */
export function resolveIosNativeUpdateCenterBaseUrl(input: {
  buildProfile: unknown;
  override?: unknown;
}) {
  if (normalizeBuildProfile(input.buildProfile) === "production") {
    return IOS_NATIVE_UPDATE_CENTER_BASE_URL;
  }

  return normalizeNonProductionOverride(input.override)
    ?? IOS_NATIVE_UPDATE_CENTER_BASE_URL;
}
