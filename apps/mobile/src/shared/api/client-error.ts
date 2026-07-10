import type { AxiosError } from "axios";
import { extractApiErrorMessage } from "./error-message";

export function preserveApiClientError(error: AxiosError): AxiosError {
  // 保留 AxiosError 的 response/status/data，页面才能读取 409 等业务快照。
  error.message = extractApiErrorMessage(error, error.message);
  return error;
}
