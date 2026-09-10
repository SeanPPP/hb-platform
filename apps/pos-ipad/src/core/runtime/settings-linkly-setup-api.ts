import type {
  PaymentEnvironment,
  SettingsLinklyHealthSnapshot,
  SettingsLinklyConnectionTestResult,
  SettingsLinklyAssignableDevice,
  SettingsLinklyPairingPort,
  SettingsLinklyPairResult,
  SettingsLinklySetupControlPort,
  SettingsLinklyTerminal,
  SettingsLinklyTerminalSelectionSnapshot,
  SettingsLinklyTerminalAssignmentInput,
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
type LinklyConnectionTestRequest =
  components["schemas"]["LinklyCloudTerminalConnectionTestRequest"];
type LinklyAssignmentRequest =
  components["schemas"]["LinklyCloudTerminalAssignmentRequest"];

// 后端 Linkly 上游预算为 240 秒；额外预留受限持久化与 HTTP 收尾时间，
// 避免全局 15 秒默认值把正常慢配对误报为 unknown。
const LINKLY_PAIR_REQUEST_TIMEOUT_MS = 270_000;

export class HbposSettingsLinklySetupApi
  implements SettingsLinklySetupControlPort, SettingsLinklyPairingPort
{
  public readonly supportsTerminalAssignment = true;
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

  public async testTerminalConnection(
    environment: PaymentEnvironment,
    terminal: Readonly<{
      terminalId: string;
      terminalVersion: string;
      assignedDeviceCode: string | null;
      assignmentRevision: number;
    }>,
    signal: AbortSignal,
  ): Promise<SettingsLinklyConnectionTestResult> {
    const terminalId = requiredOpaqueText(terminal.terminalId, 120);
    const terminalVersion = requiredOpaqueText(terminal.terminalVersion, 160);
    const assignedDeviceCode = optionalOpaqueText(
      terminal.assignedDeviceCode,
      120,
    );
    assertRevision(terminal.assignmentRevision);
    throwIfAborted(signal);
    try {
      const response = await this.transport.request<HbposEnvelope<unknown>>({
        method: "POST",
        url: `/api/v1/linkly/cloud-backend/terminals/${encodeURIComponent(terminalId)}/connection-test`,
        data: {
          environment,
          expectedTerminalVersion: terminalVersion,
          expectedAssignedDeviceCode: assignedDeviceCode,
          expectedAssignmentRevision: terminal.assignmentRevision,
        } satisfies LinklyConnectionTestRequest,
        signal,
      });
      const result = normalizeConnectionTest(
        environment,
        terminalId,
        unwrapHbposEnvelope(response.data),
      );
      if (
        result.terminalVersion !== terminalVersion ||
        result.assignedDeviceCode !== assignedDeviceCode ||
        result.assignmentRevision !== terminal.assignmentRevision
      ) {
        throw new HbposApiError("Linkly connection test scope changed.", {
          kind: "envelope",
          code: "LINKLY_CONNECTION_TEST_SCOPE_CHANGED",
        });
      }
      if (result.status === "unknown") {
        // 服务端也无法判定测试终态时只读核对目录，绝不再次 POST。
        await this.readTerminals(environment, signal);
      }
      return result;
    } catch (error) {
      if (isUnknownMutationOutcome(error)) {
        // POST 可能已到服务端；只读核对目录，绝不自动重放连接测试。
        try {
          await this.readTerminals(environment, signal);
        } catch {
          // GET 也失败时保留原始不确定终态。
        }
      }
      throw error;
    }
  }

  public async assignTerminal(
    environment: PaymentEnvironment,
    input: SettingsLinklyTerminalAssignmentInput,
    signal: AbortSignal,
  ): Promise<SettingsLinklyTerminalSelectionSnapshot> {
    const terminalId = requiredOpaqueText(input.terminalId, 120);
    const terminalVersion = requiredOpaqueText(input.terminalVersion, 160);
    const assignedDeviceCode = optionalOpaqueText(input.assignedDeviceCode, 120);
    const targetDeviceCode = optionalOpaqueText(input.targetDeviceCode, 120);
    const expectedTargetTerminalId = optionalOpaqueText(
      input.expectedTargetTerminalId,
      120,
    );
    assertRevision(input.assignmentRevision);
    assertRevision(input.expectedTargetSelectionRevision);
    throwIfAborted(signal);
    try {
      const response = await this.transport.request<HbposEnvelope<unknown>>({
        method: "PUT",
        url: `/api/v1/linkly/cloud-backend/terminals/${encodeURIComponent(terminalId)}/assignment`,
        data: {
          environment,
          expectedTerminalVersion: terminalVersion,
          expectedAssignedDeviceCode: assignedDeviceCode,
          expectedAssignmentRevision: input.assignmentRevision,
          targetDeviceCode,
          expectedTargetTerminalId,
          expectedTargetSelectionRevision: input.expectedTargetSelectionRevision,
        } satisfies LinklyAssignmentRequest,
        signal,
      });
      const snapshot = normalizeTerminalSelection(
        environment,
        unwrapHbposEnvelope(response.data),
      );
      if (!assignmentConfirmed(snapshot, terminalId, {
        ...input,
        terminalVersion,
        assignedDeviceCode,
        targetDeviceCode,
        expectedTargetTerminalId,
      })) {
        throw new HbposApiError("Linkly assignment response was unconfirmed.", {
          kind: "envelope",
          code: "LINKLY_ASSIGNMENT_RESPONSE_INVALID",
        });
      }
      return snapshot;
    } catch (error) {
      if (!isUnknownMutationOutcome(error)) throw error;
      // PUT 的 HTTP 终态未知时只允许 GET；确认目标状态后视为已提交，其他情况仍失败关闭。
      const snapshot = await this.readTerminals(environment, signal);
      if (assignmentConfirmed(snapshot, terminalId, {
        ...input,
        terminalVersion,
        assignedDeviceCode,
        targetDeviceCode,
        expectedTargetTerminalId,
      })) {
        return snapshot;
      }
      throw error;
    }
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
  const devices = Array.isArray(value.devices)
    ? value.devices.flatMap(normalizeAssignableDevice)
    : [];
  const lineManagementSupported =
    Array.isArray(value.devices) &&
    value.devices.every(assignableDeviceMetadataValid) &&
    Array.isArray(value.terminals) &&
    value.terminals.every(terminalManagementMetadataValid);
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
    devices: Object.freeze(devices),
    lineManagementSupported,
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
    assignedDeviceCode: optionalOpaqueText(value.assignedDeviceCode, 120),
    assignmentRevision: validRevision(value.assignmentRevision)
      ? value.assignmentRevision
      : 0,
    terminalVersion: optionalOpaqueText(value.terminalVersion, 160),
  })];
}

function normalizeAssignableDevice(
  value: unknown,
): readonly SettingsLinklyAssignableDevice[] {
  if (!isRecord(value)) return [];
  const deviceCode = optionalOpaqueText(value.deviceCode, 120);
  const deviceSystem = boundedText(value.deviceSystem, 80);
  const selectedTerminalId = optionalOpaqueText(value.selectedTerminalId, 120);
  if (!deviceCode || !deviceSystem || !validRevision(value.selectionRevision)) {
    return [];
  }
  return [Object.freeze({
    deviceCode,
    deviceSystem,
    isAvailable: value.isAvailable === true,
    selectedTerminalId,
    selectionRevision: value.selectionRevision,
  })];
}

function terminalManagementMetadataValid(value: unknown): boolean {
  return isRecord(value) &&
    optionalOpaqueText(value.terminalVersion, 160) !== null &&
    validRevision(value.assignmentRevision) &&
    "assignedDeviceCode" in value &&
    (value.assignedDeviceCode === null ||
      optionalOpaqueText(value.assignedDeviceCode, 120) !== null);
}

function assignableDeviceMetadataValid(value: unknown): boolean {
  return isRecord(value) &&
    optionalOpaqueText(value.deviceCode, 120) !== null &&
    boundedText(value.deviceSystem, 80).length > 0 &&
    typeof value.isAvailable === "boolean" &&
    validRevision(value.selectionRevision) &&
    (value.selectedTerminalId === null ||
      optionalOpaqueText(value.selectedTerminalId, 120) !== null);
}

function normalizeConnectionTest(
  environment: PaymentEnvironment,
  terminalId: string,
  value: unknown,
): SettingsLinklyConnectionTestResult {
  if (
    !isRecord(value) ||
    value.environment !== environment ||
    boundedText(value.terminalId, 120) !== terminalId
  ) {
    throw new HbposApiError("Linkly connection test scope mismatch.", {
      kind: "envelope",
      code: "LINKLY_CONNECTION_TEST_SCOPE_MISMATCH",
    });
  }
  const status = value.status;
  const terminalVersion = optionalOpaqueText(value.terminalVersion, 160);
  const checkedAt = boundedText(value.checkedAt, 80);
  const message = boundedText(value.message, 400);
  if (
    !terminalVersion ||
    !checkedAt ||
    !message ||
    !validRevision(value.assignmentRevision) ||
    (status !== "connected" &&
      status !== "unreachable" &&
      status !== "unknown" &&
      status !== "needs-repair") ||
    (value.succeeded === true) !== (status === "connected")
  ) {
    throw new HbposApiError("Linkly connection test response was invalid.", {
      kind: "envelope",
      code: "LINKLY_CONNECTION_TEST_RESPONSE_INVALID",
    });
  }
  return Object.freeze({
    terminalId,
    environment,
    terminalVersion,
    assignedDeviceCode: optionalOpaqueText(value.assignedDeviceCode, 120),
    assignmentRevision: value.assignmentRevision,
    succeeded: value.succeeded === true,
    status,
    checkedAt,
    message,
    responseCode: optionalBoundedText(value.responseCode, 120),
  });
}

function assignmentConfirmed(
  snapshot: SettingsLinklyTerminalSelectionSnapshot,
  terminalId: string,
  input: SettingsLinklyTerminalAssignmentInput,
): boolean {
  const terminal = snapshot.terminals.find(
    (item) => item.terminalId === terminalId,
  );
  if (snapshot.lineManagementSupported !== true) return false;
  if (terminal?.assignedDeviceCode !== input.targetDeviceCode) {
    return false;
  }
  const noAssignmentChange =
    input.assignedDeviceCode === input.targetDeviceCode &&
    (input.targetDeviceCode === null ||
      input.expectedTargetTerminalId === terminalId);
  if (noAssignmentChange) {
    // 幂等重复分配不会提升版本或清除健康状态；仍需逐项确认 CAS 快照未漂移。
    if (
      terminal.assignmentRevision !== input.assignmentRevision ||
      terminal.terminalVersion !== input.terminalVersion
    ) {
      return false;
    }
  } else if (
    (terminal.assignmentRevision ?? 0) <= input.assignmentRevision ||
    terminal.terminalVersion === input.terminalVersion ||
    terminal.lastHealthStatus !== null ||
    terminal.lastHealthAt !== null
  ) {
    // 实际换线必须让来源线路版本前进并清空旧健康结果。
    return false;
  }
  const target = input.targetDeviceCode === null
    ? null
    : snapshot.devices?.find(
        (device) => device.deviceCode === input.targetDeviceCode,
      );
  if (
    input.targetDeviceCode !== null &&
    (!target ||
      target.selectedTerminalId !== terminalId ||
      (noAssignmentChange
        ? target.selectionRevision !== input.expectedTargetSelectionRevision
        : target.selectionRevision <= input.expectedTargetSelectionRevision))
  ) {
    return false;
  }
  if (
    !noAssignmentChange &&
    input.expectedTargetTerminalId !== null &&
    input.expectedTargetTerminalId !== terminalId
  ) {
    const replaced = snapshot.terminals.find(
      (item) => item.terminalId === input.expectedTargetTerminalId,
    );
    if (
      !replaced ||
      replaced.assignedDeviceCode !== null ||
      replaced.lastHealthStatus !== null ||
      replaced.lastHealthAt !== null
    ) return false;
  }
  if (
    input.assignedDeviceCode !== null &&
    input.assignedDeviceCode !== input.targetDeviceCode
  ) {
    const previousOwner = snapshot.devices?.find(
      (device) => device.deviceCode === input.assignedDeviceCode,
    );
    if (previousOwner?.selectedTerminalId === terminalId) return false;
  }
  return true;
}

function validRevision(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) >= 0;
}

function assertRevision(value: unknown): asserts value is number {
  if (!validRevision(value)) {
    throw new Error("Linkly assignment revision is invalid.");
  }
}

function requiredOpaqueText(value: unknown, maxLength: number): string {
  const opaque = optionalOpaqueText(value, maxLength);
  if (opaque === null) {
    throw new Error("Linkly assignment metadata is incomplete.");
  }
  return opaque;
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

function optionalOpaqueText(
  value: unknown,
  maxLength: number,
): string | null {
  if (value === null || value === undefined) return null;
  // TerminalVersion 与并发前置事实是 opaque token；只验证，禁止 trim 或截断。
  if (
    typeof value !== "string" ||
    value.length > maxLength ||
    value.trim().length === 0
  ) {
    throw new Error("Linkly assignment metadata is invalid.");
  }
  return value;
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

function isUnknownMutationOutcome(error: unknown): boolean {
  return error instanceof HbposApiError && (
    error.kind === "transport" ||
    (error.kind === "http" &&
      (error.status === 408 ||
        (typeof error.status === "number" && error.status >= 500)))
  );
}

function throwIfAborted(signal: AbortSignal): void {
  if (!signal.aborted) return;
  const error = new Error("Linkly setup request aborted.");
  error.name = "AbortError";
  throw error;
}
