import type { AxiosRequestConfig } from "axios";
import { EXPECTED_ACCOUNT_HEADER } from "@/shared/api/account-bound-request-header";

export function accountBoundRequestConfig(actorGuid: string, signal?: AbortSignal): AxiosRequestConfig {
  const expectedActorGuid = actorGuid.trim();
  if (!expectedActorGuid) {
    throw Object.assign(new Error("ACCOUNT_SESSION_CHANGED"), { code: "ACCOUNT_SESSION_CHANGED" });
  }
  return {
    ...(signal ? { signal } : {}),
    headers: { [EXPECTED_ACCOUNT_HEADER]: expectedActorGuid },
  };
}
