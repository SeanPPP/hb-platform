import type { HbposEnvelope } from "@/core/api/hbpos-api";
import type { components } from "@/generated/hbpos/schema";

type GeneratedRegistrationRequest =
  components["schemas"]["AttendanceSigningKeyRegistrationRequest"];
type GeneratedRegistrationResponse =
  components["schemas"]["AttendanceSigningKeyRegistrationResponse"];
type GeneratedPublicKeyPackage =
  components["schemas"]["EmergencyLoginPublicKeyPackage"];
type GeneratedPublicKeyAckResponse =
  components["schemas"]["EmergencyLoginPublicKeyAckResponse"];

export type AttendanceSecurityTransportRequest = Readonly<{
  method: "GET" | "POST" | "PUT";
  url: string;
  data?: unknown;
  headers?: Readonly<Record<string, string>>;
  acceptedStatuses?: readonly number[];
}>;

export type AttendanceSecurityTransportResponse<T> = Readonly<{
  status: number;
  data: T;
}>;

/**
 * HbposTransport 暂不支持 PUT 与条件请求；生产组合根用同一 Axios 实例实现此窄 Port，
 * 继续复用设备认证、硬件头、401/403 门禁与日志脱敏策略。
 */
export interface AttendanceSecurityTransport {
  request<T>(
    request: AttendanceSecurityTransportRequest,
  ): Promise<AttendanceSecurityTransportResponse<T>>;
}

export type AttendanceSigningKeyRegistration = Readonly<{
  kid: string;
  algorithm: "A256GCM";
  keyMaterialBase64Url: string;
}>;

export type RegisteredAttendanceSigningKey = Readonly<{
  kid: string;
  registeredAtEpochMs: number;
  serverTimeEpochMs: number;
}>;

export type EmergencyPublicKey = Readonly<{
  kid: string;
  algorithm: "ES256";
  publicKeyPem: string;
  fingerprintHex: string;
}>;

export type EmergencyPublicKeyPackage = Readonly<{
  version: number;
  activeKeyId: string | null;
  generatedAtEpochMs: number;
  keys: readonly EmergencyPublicKey[];
}>;

export type EmergencyPublicKeyFetchResult =
  | Readonly<{ kind: "not-modified" }>
  | Readonly<{
      kind: "changed";
      package: EmergencyPublicKeyPackage;
    }>;

export type EmergencyPublicKeyAckResult = Readonly<{
  acknowledged: boolean;
  serverVersion: number;
  serverTimeEpochMs: number;
}>;

export type AttendanceSecurityApiErrorKind =
  | "http"
  | "invalid-request"
  | "invalid-response"
  | "rejected";

export class AttendanceSecurityApiError extends Error {
  public constructor(
    public readonly kind: AttendanceSecurityApiErrorKind,
    message: string,
    public readonly status?: number,
    public readonly code?: string,
  ) {
    super(message);
    this.name = "AttendanceSecurityApiError";
  }
}

export interface AttendanceSecurityRemotePort {
  registerAttendanceKey(
    request: AttendanceSigningKeyRegistration,
  ): Promise<RegisteredAttendanceSigningKey>;
  fetchEmergencyPublicKeys(
    currentVersion: number | null,
  ): Promise<EmergencyPublicKeyFetchResult>;
  acknowledgeEmergencyPublicKeys(
    version: number,
  ): Promise<EmergencyPublicKeyAckResult>;
}

export class HbposAttendanceSecurityApi
  implements AttendanceSecurityRemotePort
{
  public constructor(
    private readonly transport: AttendanceSecurityTransport,
  ) {}

  public async registerAttendanceKey(
    input: AttendanceSigningKeyRegistration,
  ): Promise<RegisteredAttendanceSigningKey> {
    const kid = requestAttendanceKid(input.kid);
    if (input.algorithm !== "A256GCM") {
      throw invalidRequest("algorithm");
    }
    const keyMaterial = requestA256Key(input.keyMaterialBase64Url);
    const request: GeneratedRegistrationRequest = {
      algorithm: "A256GCM",
      keyMaterial,
      kid,
    };
    const response = await this.transport.request<
      HbposEnvelope<GeneratedRegistrationResponse>
    >({
      method: "PUT",
      url: "/api/v1/attendance/signing-key",
      data: request,
    });
    if (response.status !== 200) {
      throw new AttendanceSecurityApiError(
        "http",
        "Attendance signing-key registration failed.",
        response.status,
      );
    }
    const payload = unwrapEnvelope(response.data);
    const returnedKid = responseAttendanceKid(payload.kid, "kid");
    if (returnedKid !== kid) throw invalidResponse("kid");
    const registeredAtEpochMs = responseEpoch(
      payload.registeredAtUtc,
      "registeredAtUtc",
    );
    const serverTimeEpochMs = responseEpoch(
      payload.serverTimeUtc,
      "serverTimeUtc",
    );
    if (registeredAtEpochMs > serverTimeEpochMs) {
      throw invalidResponse("registeredAtUtc");
    }
    return Object.freeze({
      kid,
      registeredAtEpochMs,
      serverTimeEpochMs,
    });
  }

  public async fetchEmergencyPublicKeys(
    currentVersion: number | null,
  ): Promise<EmergencyPublicKeyFetchResult> {
    const version =
      currentVersion === null
        ? null
        : requestVersion(currentVersion, "currentVersion");
    const request: AttendanceSecurityTransportRequest =
      version === null
        ? {
            method: "GET",
            url: "/api/v1/emergency-login/public-keys",
          }
        : {
            method: "GET",
            url: "/api/v1/emergency-login/public-keys",
            headers: {
              "If-None-Match": `"emergency-login-keys-v${version}"`,
            },
            acceptedStatuses: [304],
          };
    const response =
      await this.transport.request<GeneratedPublicKeyPackage | null>(
        request,
      );
    if (response.status === 304) {
      return Object.freeze({ kind: "not-modified" });
    }
    if (response.status !== 200) {
      throw new AttendanceSecurityApiError(
        "http",
        "Emergency public-key fetch failed.",
        response.status,
      );
    }
    return Object.freeze({
      kind: "changed",
      package: mapPublicKeyPackage(response.data),
    });
  }

  public async acknowledgeEmergencyPublicKeys(
    version: number,
  ): Promise<EmergencyPublicKeyAckResult> {
    const requestedVersion = requestVersion(version, "version");
    const response =
      await this.transport.request<GeneratedPublicKeyAckResponse>({
        method: "POST",
        url: "/api/v1/emergency-login/public-keys/ack",
        data: { version: requestedVersion },
        acceptedStatuses: [409],
      });
    if (response.status !== 200 && response.status !== 409) {
      throw new AttendanceSecurityApiError(
        "http",
        "Emergency public-key acknowledgement failed.",
        response.status,
      );
    }
    const serverVersion = responseVersion(
      response.data?.version,
      "version",
    );
    const serverTimeEpochMs = responseEpoch(
      response.data?.serverTimeUtc,
      "serverTimeUtc",
    );
    return Object.freeze({
      acknowledged: response.status === 200,
      serverVersion,
      serverTimeEpochMs,
    });
  }
}

function unwrapEnvelope<T>(envelope: HbposEnvelope<T>): T {
  if (envelope.success !== true || envelope.data === undefined) {
    const code =
      typeof envelope.errorCode === "string" &&
      envelope.errorCode.trim().length > 0
        ? envelope.errorCode.trim()
        : undefined;
    throw new AttendanceSecurityApiError(
      "rejected",
      "Attendance security request was rejected.",
      undefined,
      code,
    );
  }
  return envelope.data;
}

function mapPublicKeyPackage(
  value: GeneratedPublicKeyPackage | null,
): EmergencyPublicKeyPackage {
  if (!value || !Array.isArray(value.keys)) {
    throw invalidResponse("package");
  }
  const version = responseVersion(value.version, "version");
  const generatedAtEpochMs = responseEpoch(
    value.generatedAtUtc,
    "generatedAtUtc",
  );
  if (value.keys.length === 0 || value.keys.length > 128) {
    throw invalidResponse("keys");
  }
  const keyIds = new Set<string>();
  const keys = value.keys.map((key, index) => {
    const prefix = `keys[${index}]`;
    const kid = responseEmergencyKid(key.kid, `${prefix}.kid`);
    if (keyIds.has(kid)) throw invalidResponse(`${prefix}.kid`);
    keyIds.add(kid);
    if (key.algorithm !== "ES256") {
      throw invalidResponse(`${prefix}.algorithm`);
    }
    const publicKeyPem = responsePublicKeyPem(
      key.pem,
      `${prefix}.pem`,
    );
    const fingerprintHex = responseFingerprint(
      key.fingerprint,
      `${prefix}.fingerprint`,
    );
    return Object.freeze({
      kid,
      algorithm: "ES256" as const,
      publicKeyPem,
      fingerprintHex,
    });
  });
  const activeKeyId = optionalEmergencyKid(
    value.activeKeyId,
    "activeKeyId",
  );
  if (activeKeyId && !keyIds.has(activeKeyId)) {
    throw invalidResponse("activeKeyId");
  }
  return Object.freeze({
    version,
    activeKeyId,
    generatedAtEpochMs,
    keys: Object.freeze(keys),
  });
}

function requestVersion(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw invalidRequest(field);
  }
  return Number(value);
}

function responseVersion(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw invalidResponse(field);
  }
  return Number(value);
}

function requestAttendanceKid(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9_-]{1,64}$/u.test(value)
  ) {
    throw invalidRequest("kid");
  }
  return value;
}

function responseAttendanceKid(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9_-]{1,64}$/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function responseEmergencyKid(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9]{1,32}$/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function optionalEmergencyKid(
  value: unknown,
  field: string,
): string | null {
  if (value === null || value === undefined || value === "") return null;
  return responseEmergencyKid(value, field);
}

function requestA256Key(value: unknown): string {
  if (
    typeof value !== "string" ||
    !/^[A-Za-z0-9_-]{43}$/u.test(value)
  ) {
    throw invalidRequest("keyMaterialBase64Url");
  }
  return value;
}

function responseEpoch(value: unknown, field: string): number {
  if (
    typeof value !== "string" ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(
      value,
    )
  ) {
    throw invalidResponse(field);
  }
  const epoch = Date.parse(value);
  if (!Number.isSafeInteger(epoch)) throw invalidResponse(field);
  return epoch;
}

function responsePublicKeyPem(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    value.length < 64 ||
    value.length > 8_192 ||
    !value.includes("-----BEGIN PUBLIC KEY-----") ||
    !value.includes("-----END PUBLIC KEY-----") ||
    value.includes("PRIVATE KEY") ||
    /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function responseFingerprint(value: unknown, field: string): string {
  if (typeof value !== "string" || !/^[A-Fa-f0-9]{64}$/u.test(value)) {
    throw invalidResponse(field);
  }
  return value.toUpperCase();
}

function invalidRequest(field: string): AttendanceSecurityApiError {
  return new AttendanceSecurityApiError(
    "invalid-request",
    `Invalid attendance security request field: ${field}.`,
  );
}

function invalidResponse(field: string): AttendanceSecurityApiError {
  return new AttendanceSecurityApiError(
    "invalid-response",
    `Invalid attendance security response field: ${field}.`,
  );
}
