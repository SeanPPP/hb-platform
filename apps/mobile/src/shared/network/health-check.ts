/**
 * 通用后端可达性检查（健康探测）。
 *
 * 复用考勤模块 verifyAttendanceNetworkReachability 的既有模式（GET /health + 超时中止），
 * 作为通用基础设施对外暴露，供网络恢复补传、页面刷新等场景调用。
 * 所有副作用依赖均可注入，便于单元测试与在 iOS 审核态下保持一致行为。
 */
import { reviewAwareFetch } from "@/modules/ios-review/network";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { buildApiBaseUrl, getStoredApiHost } from "@/shared/api/config";

/** 健康探测默认超时（毫秒），与考勤模块一致。 */
export const NETWORK_CHECK_TIMEOUT_MS = 5000;

/** 后端可达性检查结果：ok 表示后端可达（health 端点正常响应）。 */
export type BackendReachability = Readonly<{
  ok: boolean;
  checkedAtIso: string;
}>;

export type BackendHealthCheckOptions = {
  /** 探测超时（毫秒），默认 5000。 */
  timeoutMs?: number;
  /** iOS 审核态判定函数；默认读取 ios-review session 状态。 */
  isReviewActive?: () => boolean;
  /** 请求执行器；默认 reviewAwareFetch（审核态外等同全局 fetch）。 */
  fetchImpl?: typeof fetch;
  /** 当前 API 基础地址来源；默认 getStoredApiHost + buildApiBaseUrl。 */
  getApiBaseUrl?: () => Promise<string>;
  /** 时间戳来源，便于测试固定时间。 */
  nowIso?: () => string;
};

/**
 * 由 API 基础地址推导健康检查 URL。
 * apiBaseUrl 形如 `https://host/api`（生产）或 `http://host:5002/api`（本地），
 * 后端 health 端点位于根路径，因此去掉尾部 `/api` 后拼接 `/health`。
 */
export function buildHealthUrl(apiBaseUrl: string): string {
  return `${apiBaseUrl.replace(/\/api$/, "")}/health`;
}

/**
 * 检查后端是否可达。
 *
 * 行为约定：
 * - iOS 审核态：不发起真实网络请求，直接视为可达（离线 Demo 由本地 adapter 处理）。
 * - 普通态：GET /health，超时或任何异常一律返回 { ok: false }，不向上抛错，
 *   保证调用方无需处理异常分支。
 */
export async function checkBackendReachable(
  options: BackendHealthCheckOptions = {},
): Promise<BackendReachability> {
  const nowIso = options.nowIso ?? (() => new Date().toISOString());
  const isReviewActive =
    options.isReviewActive ?? isIosReviewSessionActive;
  const getApiBaseUrl =
    options.getApiBaseUrl ??
    (async () => buildApiBaseUrl(await getStoredApiHost()));

  if (isReviewActive()) {
    // 审核态下健康检查不触网，直接报告可用，避免审核环境产生真实请求。
    return { ok: true, checkedAtIso: nowIso() };
  }

  const controller = new AbortController();
  const timeoutId = setTimeout(
    () => controller.abort(),
    options.timeoutMs ?? NETWORK_CHECK_TIMEOUT_MS,
  );
  const fetcher = options.fetchImpl ?? reviewAwareFetch;

  try {
    const apiBaseUrl = await getApiBaseUrl();
    const response = await fetcher(buildHealthUrl(apiBaseUrl), {
      method: "GET",
      headers: { Accept: "application/json" },
      signal: controller.signal,
    });
    // 只有 2xx（response.ok）才算后端真正可达；5xx/4xx 视为不可达。
    return { ok: response.ok === true, checkedAtIso: nowIso() };
  } catch {
    // 超时、网络不可达或地址解析失败统一视为不可达，不暴露底层异常。
    return { ok: false, checkedAtIso: nowIso() };
  } finally {
    clearTimeout(timeoutId);
  }
}
