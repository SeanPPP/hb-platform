/**
 * 网络类错误识别：区分「服务器不可达」与「服务器已响应但业务/权限失败」。
 *
 * 只有前者才允许商品查询页降级到离线数据、允许冷启动用本地缓存恢复会话；
 * 4xx/5xx 说明后端可达，必须继续按在线错误处理，绝不能被误判为离线。
 */
import { isAxiosError } from "axios";

/** axios 在无响应时可能给出的错误码；ECONNABORTED 是超时，ERR_NETWORK 是连接失败。 */
const NETWORK_UNAVAILABLE_AXIOS_CODES = new Set([
  "ECONNABORTED",
  "ERR_NETWORK",
  "ETIMEDOUT",
  "ECONNREFUSED",
  "ENOTFOUND",
  "EAI_AGAIN",
]);

/** React Native fetch 在断网时抛出的 TypeError 文案。 */
const NETWORK_UNAVAILABLE_MESSAGE_PATTERN = /network request failed|failed to fetch|network error/i;

export function isNetworkUnavailableError(error: unknown): boolean {
  if (!error || typeof error !== "object") {
    return false;
  }

  // 主动取消（切店抢占、离开页面中止在途请求）不是「服务器不可达」。
  // 把它算成不可达会让页面在网络完全正常时切进离线模式并禁用全部编辑。
  const cancellation = error as { code?: unknown; name?: unknown };
  if (cancellation.code === "ERR_CANCELED" || cancellation.name === "CanceledError") {
    return false;
  }

  if (isAxiosError(error)) {
    // 有响应就说明后端可达，无论状态码是什么都不是网络不可用。
    if (error.response) {
      return false;
    }
    if (error.code && NETWORK_UNAVAILABLE_AXIOS_CODES.has(error.code)) {
      return true;
    }
    // axios 某些平台在断网时 code 为空，只能靠 request 已发出且无响应来判断。
    return Boolean(error.request) || NETWORK_UNAVAILABLE_MESSAGE_PATTERN.test(error.message ?? "");
  }

  const candidate = error as { name?: unknown; message?: unknown; code?: unknown };
  if (typeof candidate.code === "string" && NETWORK_UNAVAILABLE_AXIOS_CODES.has(candidate.code)) {
    return true;
  }
  if (candidate.name === "AbortError") {
    // 主动取消（超时探测）视为不可达，避免把中止当成服务器错误。
    return true;
  }
  return (
    typeof candidate.message === "string" &&
    NETWORK_UNAVAILABLE_MESSAGE_PATTERN.test(candidate.message)
  );
}
