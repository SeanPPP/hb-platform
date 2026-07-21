import { extractApiErrorMessage } from "./error-message";

export function unwrapApiEnvelope<T>(payload: unknown): T {
  let current = payload;
  for (let depth = 0; depth < 3; depth++) {
    if (typeof current !== "object" || current === null) break;
    const envelope = current as Record<string, unknown>;
    const isExplicitFailure = envelope.success === false || envelope.isSuccess === false;
    if (isExplicitFailure) {
      const error = new Error(extractApiErrorMessage(envelope, "Request failed"));
      const errorCode = envelope.errorCode ?? envelope.ErrorCode ?? envelope.code ?? envelope.Code;
      if (typeof errorCode === "string" && errorCode.trim()) {
        // 保留业务错误码，页面才能稳定映射中英文提示，而不是解析后端文案。
        Object.assign(error, { code: errorCode.trim() });
      }
      throw error;
    }

    // 显式失败可能没有 data，必须先抛错；其余对象继续保持原有解包判定。
    if (!("data" in envelope)) break;
    const keys = Object.keys(current);
    const isEnvelope =
      keys.includes("data") &&
      (keys.includes("success") || keys.includes("isSuccess") || keys.includes("message"));
    if (!isEnvelope) break;
    current = envelope.data;
  }
  return current as T;
}
