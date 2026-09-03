import type {
  PaymentEnvironment,
  SettingsLinklyHealthSnapshot,
  SettingsLinklyPairingPort,
  SettingsLinklyPairResult,
  SettingsLinklySetupControlPort,
  SettingsLinklyTerminal,
  SettingsLinklyTerminalSelectionSnapshot,
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
    terminals?: SettingsLinklyTerminalSelectionSnapshot | null,
  ): Promise<SettingsLinklyHealthSnapshot> {
    const params: Readonly<Record<string, string | number>> =
      terminals?.mode === "Active"
        ? activeHealthParams(environment, terminals)
        : { environment };
    const response = await this.transport.request<
      HbposEnvelope<LinklyHealthResponse>
    >({
      method: "GET",
      url: "/api/v1/linkly/cloud-backend/health",
      params,
      signal,
    });
    return normalizeHealth(
      environment,
      unwrapHbposEnvelope(response.data),
    );
  }

  public async pair(
    environment: PaymentEnvironment,
    terminalId: string,
    pairCode: string,
    signal: AbortSignal,
  ): Promise<SettingsLinklyPairResult> {
    const normalizedTerminalId = terminalId.trim();
    const normalizedPairCode = pairCode.trim();
    if (!normalizedTerminalId) {
      throw new Error("Linkly terminal id is required.");
    }
    if (!/^\d{6}$/u.test(normalizedPairCode)) {
      throw new Error("Linkly Pair Code must contain six digits.");
    }
    throwIfAborted(signal);
    try {
      const response = await this.transport.request<
        HbposEnvelope<unknown>
      >({
        method: "POST",
        url: `/api/v1/linkly/cloud-backend/terminals/${encodeURIComponent(normalizedTerminalId)}/pair`,
        data: {
          environment,
          pairCode: normalizedPairCode,
        },
        signal,
        timeoutMs: LINKLY_PAIR_REQUEST_TIMEOUT_MS,
      });
      const result = unwrapHbposEnvelope(response.data);
      if (!isRecord(result) ||
        boundedText(result.terminalId, 120) !== normalizedTerminalId ||
        result.environment !== environment ||
        result.pairingState !== "Ready") {
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

  public async readTerminals(
    environment: PaymentEnvironment,
    signal: AbortSignal,
  ): Promise<SettingsLinklyTerminalSelectionSnapshot> {
    const response = await this.transport.request<HbposEnvelope<unknown>>({
      method: "GET",
      url: "/api/v1/linkly/cloud-backend/terminals",
      params: { environment },
      signal,
    });
    return normalizeTerminalSelection(
      environment,
      unwrapHbposEnvelope(response.data),
    );
  }

  public async selectTerminal(
    environment: PaymentEnvironment,
    terminalId: string,
    expectedRevision: number,
    signal: AbortSignal,
  ): Promise<SettingsLinklyTerminalSelectionSnapshot> {
    const normalizedTerminalId = terminalId.trim();
    if (!normalizedTerminalId || !Number.isSafeInteger(expectedRevision) || expectedRevision < 0) {
      throw new Error("Linkly terminal selection is invalid.");
    }
    throwIfAborted(signal);
    await this.transport.request<HbposEnvelope<unknown>>({
      method: "PUT",
      url: "/api/v1/linkly/cloud-backend/terminal-selection",
      data: {
        environment,
        terminalId: normalizedTerminalId,
        expectedRevision,
      },
      signal,
    });
    // PUT 响应可能只含选择头；随后 GET 是终端状态与 revision 的唯一权威快照。
    return this.readTerminals(environment, signal);
  }
}

function activeHealthParams(
  environment: PaymentEnvironment,
  terminals: SettingsLinklyTerminalSelectionSnapshot,
): Readonly<{
  environment: PaymentEnvironment;
  terminalId: string;
  selectionRevision: number;
}> {
  const terminalId = terminals.selectedTerminalId?.trim() ?? "";
  if (
    terminals.environment !== environment ||
    !terminalId ||
    !Number.isSafeInteger(terminals.selectionRevision) ||
    terminals.selectionRevision <= 0
  ) {
    throw new HbposApiError("Linkly terminal selection was invalid.", {
      kind: "envelope",
      code: "LINKLY_TERMINAL_SELECTION_INVALID",
    });
  }
  return {
    environment,
    terminalId,
    selectionRevision: terminals.selectionRevision,
  };
}

function normalizeTerminalSelection(
  requestedEnvironment: PaymentEnvironment,
  value: unknown,
): SettingsLinklyTerminalSelectionSnapshot {
  if (!isRecord(value) || value.environment !== requestedEnvironment) {
    throw new HbposApiError("Linkly terminal environment mismatch.", {
      kind: "envelope",
      code: "LINKLY_TERMINAL_ENVIRONMENT_MISMATCH",
    });
  }
  const mode = normalizeTerminalMode(value.mode);
  const selectedTerminalId = optionalBoundedText(
    value.selectedTerminalId,
    120,
  );
  const revision =
    value.selectionRevision === null || value.selectionRevision === undefined
      ? 0
      : value.selectionRevision;
  if (
    !Number.isSafeInteger(revision) ||
    (revision as number) < 0 ||
    (selectedTerminalId !== null && revision === 0)
  ) {
    throw new HbposApiError("Linkly terminal revision was invalid.", {
      kind: "envelope",
      code: "LINKLY_TERMINAL_REVISION_INVALID",
    });
  }
  const terminals = Array.isArray(value.terminals)
    ? value.terminals.flatMap(normalizeTerminal)
    : [];
  if (
    selectedTerminalId !== null &&
    !terminals.some((terminal) => terminal.terminalId === selectedTerminalId)
  ) {
    throw new HbposApiError("Selected Linkly terminal was missing.", {
      kind: "envelope",
      code: "LINKLY_SELECTED_TERMINAL_MISSING",
    });
  }
  return Object.freeze({
    environment: requestedEnvironment,
    mode,
    selectedTerminalId,
    selectionRevision: revision as number,
    terminals: Object.freeze(terminals),
  });
}

function normalizeTerminalMode(value: unknown): "Active" | "Legacy" | "Draft" {
  // 旧服务没有 mode 时保持既有单终端支付路径；未知非空枚举不能静默降级。
  if (value === undefined || value === null || value === "") return "Legacy";
  if (value === "Active" || value === "Legacy" || value === "Draft") {
    return value;
  }
  throw new HbposApiError("Linkly terminal mode was invalid.", {
    kind: "envelope",
    code: "LINKLY_TERMINAL_MODE_INVALID",
  });
}

function normalizeTerminal(value: unknown): readonly SettingsLinklyTerminal[] {
  if (!isRecord(value)) return [];
  const terminalId = boundedText(value.terminalId, 120);
  const displayName = boundedText(value.displayName, 120);
  const laneNo = value.laneNo;
  const pairingState = value.pairingState;
  if (
    !terminalId ||
    !displayName ||
    !Number.isSafeInteger(laneNo) ||
    (laneNo as number) <= 0 ||
    (pairingState !== "Unpaired" &&
      pairingState !== "Ready" &&
      pairingState !== "Unknown" &&
      pairingState !== "NeedsRepair")
  ) {
    return [];
  }
  return [Object.freeze({
    terminalId,
    laneNo: laneNo as number,
    displayName,
    pairingState,
    isBusy: value.isBusy === true,
    isReady: value.isReady === true,
    lastHealthStatus: optionalBoundedText(value.lastHealthStatus, 80),
    lastHealthAt: optionalBoundedText(value.lastHealthAt, 80),
  })];
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

function isRecord(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isUnknownPairOutcome(error: unknown): boolean {
  return (
    error instanceof HbposApiError &&
    ((error.kind === "envelope" &&
      error.code === "LINKLY_PAIR_RESPONSE_INVALID") ||
      error.kind === "transport" ||
      (error.kind === "http" &&
        (error.status === 408 ||
          (typeof error.status === "number" && error.status >= 500))))
  );
}

function throwIfAborted(signal: AbortSignal): void {
  if (!signal.aborted) return;
  const error = new Error("Linkly setup request aborted.");
  error.name = "AbortError";
  throw error;
}
