import type {
  PaymentEnvironment,
  SettingsLinklyHealthSnapshot,
  SettingsLinklyPairingPort,
  SettingsLinklyPairResult,
  SettingsLinklySetupControlPort,
} from "../../features/settings/settings-presenter";
import type { components } from "@hb/pos-api-client/openapi";
import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "../api/hbpos-api";

export type { SettingsLinklyHealthSnapshot } from "../../features/settings/settings-presenter";

type LinklyHealthResponse =
  components["schemas"]["LinklyCloudBackendHealthResponse"];

type LinklyPairRequest =
  components["schemas"]["LinklyCloudBackendPairRequest"];

// 后端 Linkly 上游预算为 240 秒；额外预留受限持久化与 HTTP 收尾时间，
// 避免全局 15 秒默认值把正常慢配对误报为 unknown。
const LINKLY_PAIR_REQUEST_TIMEOUT_MS = 270_000;

export class HbposSettingsLinklySetupApi
  implements SettingsLinklySetupControlPort, SettingsLinklyPairingPort
{
  public constructor(private readonly transport: HbposTransport) {}

  public async readState(
    environment: PaymentEnvironment,
    signal: AbortSignal,
  ): Promise<SettingsLinklyHealthSnapshot> {
    const response = await this.transport.request<
      HbposEnvelope<LinklyHealthResponse>
    >({
      method: "GET",
      url: "/api/v1/linkly/cloud-backend/health",
      params: { environment },
      signal,
    });
    return normalizeHealth(
      environment,
      unwrapHbposEnvelope(response.data),
    );
  }

  public async pair(
    environment: PaymentEnvironment,
    pairCode: string,
    signal: AbortSignal,
  ): Promise<SettingsLinklyPairResult> {
    const normalizedPairCode = pairCode.trim();
    if (!/^\d{6}$/u.test(normalizedPairCode)) {
      throw new Error("Linkly Pair Code must contain six digits.");
    }
    throwIfAborted(signal);
    try {
      const response = await this.transport.request<
        HbposEnvelope<
          components["schemas"]["LinklyCloudBackendTerminalCredentialResponse"]
        >
      >({
        method: "POST",
        url: "/api/v1/linkly/cloud-backend/pair",
        data: {
          environment,
          pairCode: normalizedPairCode,
        } satisfies LinklyPairRequest,
        signal,
        timeoutMs: LINKLY_PAIR_REQUEST_TIMEOUT_MS,
      });
      const result = unwrapHbposEnvelope(response.data);
      if (
        result.environment !== environment ||
        result.hasSecret !== true ||
        !nonEmptyText(result.storeCode) ||
        !nonEmptyText(result.deviceCode) ||
        !isUuidV4(result.posId)
      ) {
        throw new HbposApiError("Linkly pairing response was incomplete.", {
          kind: "envelope",
          code: "LINKLY_PAIR_RESPONSE_INVALID",
        });
      }
      return { status: "completed" };
    } catch (error) {
      if (isUnknownPairOutcome(error)) {
        // POST 已经离开本机但没有 HTTP 终态；调用方只能刷新，不能重放 PairCode。
        return { status: "unknown" };
      }
      throw error;
    }
  }
}

function normalizeHealth(
  requestedEnvironment: PaymentEnvironment,
  response: LinklyHealthResponse,
): SettingsLinklyHealthSnapshot {
  if (response.environment !== requestedEnvironment) {
    throw new HbposApiError("Linkly health environment mismatch.", {
      kind: "envelope",
      code: "LINKLY_HEALTH_ENVIRONMENT_MISMATCH",
    });
  }
  return Object.freeze({
    environment: requestedEnvironment,
    storeCode: boundedText(response.storeCode, 80),
    deviceCode: boundedText(response.deviceCode, 80),
    isReady: response.isReady === true,
    checks: Object.freeze(
      (response.checks ?? []).flatMap((check) => {
        const code = boundedText(check.code, 80);
        if (!code) return [];
        return [
          Object.freeze({
            code,
            isReady: check.isReady === true,
            message: optionalBoundedText(check.message, 240),
          }),
        ];
      }),
    ),
  });
}

function boundedText(value: unknown, maxLength: number): string {
  return typeof value === "string" ? value.trim().slice(0, maxLength) : "";
}

function optionalBoundedText(
  value: unknown,
  maxLength: number,
): string | null {
  const normalized = boundedText(value, maxLength);
  return normalized || null;
}

function nonEmptyText(value: unknown): boolean {
  return typeof value === "string" && value.trim().length > 0;
}

function isUuidV4(value: unknown): boolean {
  return (
    typeof value === "string" &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(
      value.trim(),
    )
  );
}

function isUnknownPairOutcome(error: unknown): boolean {
  return (
    error instanceof HbposApiError &&
    ((error.kind === "envelope" &&
      error.code === "LINKLY_PAIR_RESPONSE_INVALID") ||
      (error.kind === "transport" && error.code !== "REQUEST_ABORTED") ||
      (error.kind === "http" &&
        (error.status === 502 ||
          error.status === 504 ||
          (error.status === 500 &&
            error.code ===
              "LINKLY_CLOUD_BACKEND_PAIR_PERSISTENCE_FAILED"))))
  );
}

function throwIfAborted(signal: AbortSignal): void {
  if (!signal.aborted) return;
  const error = new Error("Linkly setup request aborted.");
  error.name = "AbortError";
  throw error;
}
