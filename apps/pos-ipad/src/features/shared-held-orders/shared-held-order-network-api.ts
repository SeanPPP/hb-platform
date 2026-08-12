import { normalizeSharedSaleCartV1, type SharedSaleCartV1 } from "./shared-sale-cart-v1";

import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import type { components } from "@/generated/hbpos/schema";


type GeneratedCapabilities = components["schemas"]["SharedHeldOrderCapabilitiesResponse"];
type GeneratedListItem = components["schemas"]["SharedHeldOrderListItemDto"];
type GeneratedPublishRequest = components["schemas"]["SharedHeldOrderPublishRequest"];
type GeneratedPublishResponse = components["schemas"]["SharedHeldOrderPublishResponse"];
type GeneratedPrepareRequest = components["schemas"]["SharedHeldOrderClaimPrepareRequest"];
type GeneratedPrepareResponse = components["schemas"]["SharedHeldOrderClaimPrepareResponse"];
type GeneratedClaim = components["schemas"]["SharedHeldOrderClaimDto"];
type GeneratedRecoveryClaim = components["schemas"]["SharedHeldOrderRecoveryClaimDto"];
type GeneratedCancelResponse = components["schemas"]["SharedHeldOrderCancelResponse"];

export type SharedHeldOrderApiErrorKind =
  | "Disabled"
  | "Retryable"
  | "Conflict"
  | "Forbidden"
  | "Invalid";

/**
 * 服务端共享挂单 API 稳定错误分类。Message 只允许服务端通用文案或稳定机器码，
 * 绝不包含 canonical/购物车 payload。
 */
export class SharedHeldOrderApiError extends Error {
  public readonly kind: SharedHeldOrderApiErrorKind;
  public readonly status: number | undefined;
  public readonly code: string | undefined;

  public constructor(
    message: string,
    details: Readonly<{
      kind: SharedHeldOrderApiErrorKind;
      status?: number;
      code?: string;
    }>,
  ) {
    super(message);
    this.name = "SharedHeldOrderApiError";
    this.kind = details.kind;
    this.status = details.status;
    this.code = details.code;
  }
}

export type SharedHeldOrderCapabilities = Readonly<{
  enabled: boolean;
  payloadVersion: number;
  preparedTtlSeconds: number;
  forceReleaseSupported: boolean;
}>;

export type SharedHeldOrderPendingListItem = Readonly<{
  holdGuid: string;
  storeCode: string;
  deviceCode: string;
  heldByCashierId: string;
  heldByCashierName: string;
  heldAtIso: string;
  updatedAtIso: string;
  lineCount: number;
  totalCents: number;
  discountCents: number;
  actualCents: number;
  revision: number;
}>;

export type SharedHeldOrderPublishResult = Readonly<{
  holdGuid: string;
  status: SharedHeldOrderStatus;
  revision: number;
  createdAtIso: string;
  alreadyExists: boolean;
}>;

export type SharedHeldOrderStatus =
  | "Pending"
  | "Claimed"
  | "Completed"
  | "Cancelled";
export type SharedHeldOrderCancelResult = Readonly<{
  holdGuid: string;
  status: "Cancelled";
  revision: number;
  updatedAtIso: string;
  alreadyCancelled: boolean;
}>;
export type SharedHeldOrderClaimStatus =
  | "Prepared"
  | "Active"
  | "Released"
  | "Completed"
  | "Superseded";

export type SharedHeldOrderPrepareResult = Readonly<{
  holdGuid: string;
  claimGuid: string;
  status: SharedHeldOrderClaimStatus;
  payload: SharedSaleCartV1;
  claimantDeviceCode: string;
  claimantCashierId: string;
  claimantCashierName: string;
  createdAtIso: string;
  expiresAtIso: string | null;
  revision: number;
  alreadyExists: boolean;
}>;

export type SharedHeldOrderClaimDto = Readonly<{
  holdGuid: string;
  claimGuid: string;
  status: SharedHeldOrderClaimStatus;
  storeCode: string;
  claimantDeviceCode: string;
  claimantCashierId: string;
  claimantCashierName: string;
  createdAtIso: string;
  updatedAtIso: string;
  expiresAtIso: string | null;
  activatedAtIso: string | null;
  releasedAtIso: string | null;
  forceReleased: boolean;
  forceReleaseReason: string | null;
  forceReleaseCashierId: string | null;
  forceReleaseCashierName: string | null;
  forceReleasedAtIso: string | null;
  revision: number;
  alreadyExists: boolean;
}>;

export type SharedHeldOrderRecoveryClaimDto = Readonly<{
  holdGuid: string;
  claimGuid: string;
  status: SharedHeldOrderClaimStatus;
  storeCode: string;
  claimantDeviceCode: string;
  claimantCashierId: string;
  claimantCashierName: string;
  payload: SharedSaleCartV1;
  createdAtIso: string;
  updatedAtIso: string;
  expiresAtIso: string | null;
  activatedAtIso: string | null;
  revision: number;
}>;

export interface SharedHeldOrderNetworkApiPort {
  getCapabilities(): Promise<SharedHeldOrderCapabilities>;
  publish(input: Readonly<{
    holdGuid: string;
    storeCode: string;
    deviceCode: string;
    cart: SharedSaleCartV1;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPublishResult>;
  listPending(): Promise<readonly SharedHeldOrderPendingListItem[]>;
  cancel(holdGuid: string): Promise<SharedHeldOrderCancelResult>;
  prepare(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPrepareResult>;
  activate(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
  }>): Promise<SharedHeldOrderClaimDto>;
  release(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
  }>): Promise<SharedHeldOrderClaimDto>;
  forceRelease(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    reason: string;
  }>): Promise<SharedHeldOrderClaimDto>;
  claimsMine(): Promise<readonly SharedHeldOrderRecoveryClaimDto[]>;
}

/**
 * Hbpos transport 的共享挂单 API adapter。所有 wire DTO 严格解析为本地域类型，
 * payload 只经 normalizeSharedSaleCartV1 进入内存；任何路径不把 payload 拼进异常消息。
 */
export class SharedHeldOrderNetworkApi implements SharedHeldOrderNetworkApiPort {
  public constructor(private readonly transport: HbposTransport) {}

  public async getCapabilities(): Promise<SharedHeldOrderCapabilities> {
    return this.get<GeneratedCapabilities>("/api/v1/held-orders/capabilities")
      .then((body) => ({
        enabled: requiredBoolean(body.enabled, "capabilities enabled"),
        payloadVersion: requiredNonNegativeInteger(
          body.payloadVersion,
          "capabilities payloadVersion",
        ),
        preparedTtlSeconds: requiredNonNegativeInteger(
          body.preparedTtlSeconds,
          "capabilities preparedTtlSeconds",
        ),
        forceReleaseSupported:
          body.forceReleaseSupported === true,
      }));
  }

  public async listPending(): Promise<readonly SharedHeldOrderPendingListItem[]> {
    return this.get<readonly GeneratedListItem[]>("/api/v1/held-orders")
      .then((rows) => Object.freeze(requiredArray(rows, "held orders").map(mapListItem)));
  }

  public async cancel(holdGuidInput: string): Promise<SharedHeldOrderCancelResult> {
    const holdGuid = requiredText(holdGuidInput, "hold guid");
    const response = await wrapTransportCall(() => this.transport.request<
      HbposEnvelope<GeneratedCancelResponse>
    >({
      method: "POST",
      url: `/api/v1/held-orders/${encodeURIComponent(holdGuid)}/cancel`,
    }));
    const result = mapCancelResult(unwrapSharedEnvelope(response));
    if (result.holdGuid !== holdGuid) {
      throw new TypeError("Cancelled held order response holdGuid is invalid.");
    }
    return result;
  }

  public async publish(input: Readonly<{
    holdGuid: string;
    storeCode: string;
    deviceCode: string;
    cart: SharedSaleCartV1;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPublishResult> {
    const request: GeneratedPublishRequest = {
      holdGuid: requiredText(input.holdGuid, "hold guid"),
      storeCode: requiredText(input.storeCode, "store code"),
      deviceCode: requiredText(input.deviceCode, "device code"),
      cart: normalizeSharedSaleCartV1(input.cart) as NonNullable<GeneratedPublishRequest["cart"]>,
      idempotencyKey: requiredText(input.idempotencyKey, "idempotency key"),
    };
    return this.post<GeneratedPublishRequest, GeneratedPublishResponse>(
      "/api/v1/held-orders",
      request,
    ).then(mapPublishResult);
  }

  public async prepare(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPrepareResult> {
    const holdGuid = requiredText(input.holdGuid, "hold guid");
    const claimGuid = requiredText(input.claimGuid, "claim guid");
    const request: GeneratedPrepareRequest = {
      claimGuid,
      idempotencyKey: requiredText(input.idempotencyKey, "idempotency key"),
    };
    return this.post<GeneratedPrepareRequest, GeneratedPrepareResponse>(
      `/api/v1/held-orders/${encodeURIComponent(holdGuid)}/claims/prepare`,
      request,
    ).then(mapPrepareResult);
  }

  public async activate(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
  }>): Promise<SharedHeldOrderClaimDto> {
    return this.claimAction(input, "activate");
  }

  public async release(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
  }>): Promise<SharedHeldOrderClaimDto> {
    return this.claimAction(input, "release");
  }

  public async forceRelease(input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    reason: string;
  }>): Promise<SharedHeldOrderClaimDto> {
    const holdGuid = requiredText(input.holdGuid, "hold guid");
    const claimGuid = requiredText(input.claimGuid, "claim guid");
    const response = await wrapTransportCall(() => this.transport.request<
      HbposEnvelope<GeneratedClaim>
    >({
      method: "POST",
      url: `/api/v1/held-orders/${encodeURIComponent(holdGuid)}/claims/${encodeURIComponent(claimGuid)}/force-release`,
      data: { reason: requiredText(input.reason, "force release reason") },
    }));
    return mapClaim(unwrapSharedEnvelope(response));
  }

  public async claimsMine(): Promise<readonly SharedHeldOrderRecoveryClaimDto[]> {
    return this.get<readonly GeneratedRecoveryClaim[]>("/api/v1/held-orders/claims/mine")
      .then((rows) => Object.freeze(requiredArray(rows, "claims mine").map(mapRecoveryClaim)));
  }

  private async claimAction(
    input: Readonly<{ holdGuid: string; claimGuid: string }>,
    action: "activate" | "release",
  ): Promise<SharedHeldOrderClaimDto> {
    const holdGuid = requiredText(input.holdGuid, "hold guid");
    const claimGuid = requiredText(input.claimGuid, "claim guid");
    const response = await wrapTransportCall(() => this.transport.request<
      HbposEnvelope<GeneratedClaim>
    >({
      method: "POST",
      url: `/api/v1/held-orders/${encodeURIComponent(holdGuid)}/claims/${encodeURIComponent(claimGuid)}/${action}`,
    }));
    return mapClaim(unwrapSharedEnvelope(response));
  }

  private async get<T>(url: string): Promise<T> {
    const response = await wrapTransportCall(() => this.transport.request<HbposEnvelope<T>>({
      method: "GET",
      url,
    }));
    return unwrapSharedEnvelope(response);
  }

  private async post<TRequest, TResponse>(
    url: string,
    data: TRequest,
  ): Promise<TResponse> {
    const response = await wrapTransportCall(() => this.transport.request<HbposEnvelope<TResponse>>({
      method: "POST",
      url,
      data,
    }));
    return unwrapSharedEnvelope(response);
  }
}

function wrapTransportCall<T>(operation: () => Promise<T>): Promise<T> {
  return operation().catch((error: unknown) => {
    if (error instanceof HbposApiError) {
      throw new SharedHeldOrderApiError(
        error.message || "共享挂单服务请求失败。",
        {
          kind: classifyHttp(error.kind, error.status, error.code),
          ...(error.status !== undefined ? { status: error.status } : {}),
          ...(error.code ? { code: error.code } : {}),
        },
      );
    }
    throw error;
  });
}

function unwrapSharedEnvelope<T>(
  response: Readonly<{ status: number; data: HbposEnvelope<T> }>,
): T {
  try {
    return unwrapHbposEnvelope(response.data);
  } catch (error) {
    if (error instanceof HbposApiError) {
      throw new SharedHeldOrderApiError(
        error.message || "共享挂单服务请求失败。",
        {
          kind: classifyHttp(error.kind, error.status, error.code),
          ...(error.status !== undefined ? { status: error.status } : {}),
          ...(error.code ? { code: error.code } : {}),
        },
      );
    }
    throw error;
  }
}

function classifyHttp(
  kind: HbposApiError["kind"],
  status: number | undefined,
  code: string | undefined,
): SharedHeldOrderApiErrorKind {
  if (kind === "transport") {
    // 网络断开/连接拒绝/超时/取消：一律可重试；调用方本地挂单不受影响。
    return "Retryable";
  }
  if (kind === "envelope") {
    return code === "SHARED_HELD_ORDER_DISABLED" ? "Disabled" : "Invalid";
  }
  return classifyHttpStatus(status, code);
}

function classifyHttpStatus(
  status: number | undefined,
  code: string | undefined,
): SharedHeldOrderApiErrorKind {
  if (code === "SHARED_HELD_ORDER_DISABLED") return "Disabled";
  if (
    code === "SHARED_HELD_ORDER_BUSY" ||
    status === 429 ||
    (status !== undefined && status >= 500)
  ) {
    return "Retryable";
  }
  if (
    code === "SHARED_HELD_ORDER_MISMATCH" ||
    code === "SHARED_HELD_ORDER_CLAIM_EXPIRED" ||
    status === 409
  ) {
    return "Conflict";
  }
  if (
    status === 401 ||
    status === 403 ||
    code === "SHARED_HELD_ORDER_PERMISSION_DENIED" ||
    code === "SHARED_HELD_ORDER_CROSS_STORE" ||
    code === "DEVICE_SCOPE_FORBIDDEN" ||
    code === "CASHIER_AUTH_REQUIRED"
  ) {
    return "Forbidden";
  }
  if (
    status === 400 ||
    status === 404 ||
    code === "SHARED_HELD_ORDER_INVALID" ||
    code === "SHARED_HELD_ORDER_NOT_FOUND"
  ) {
    return "Invalid";
  }
  return status !== undefined && status >= 200 && status < 300
    ? "Invalid"
    : "Retryable";
}

function mapListItem(value: GeneratedListItem): SharedHeldOrderPendingListItem {
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "held order holdGuid"),
    storeCode: requiredText(value.storeCode, "held order storeCode"),
    deviceCode: requiredText(value.deviceCode, "held order deviceCode"),
    heldByCashierId: requiredText(value.heldByCashierId, "held order cashier id"),
    heldByCashierName: requiredText(value.heldByCashierName, "held order cashier name"),
    heldAtIso: requiredIso(value.heldAtUtc, "held order heldAt"),
    updatedAtIso: requiredIso(value.updatedAtUtc, "held order updatedAt"),
    lineCount: requiredNonNegativeInteger(value.lineCount, "held order lineCount"),
    totalCents: requiredNonNegativeInteger(value.totalCents, "held order totalCents"),
    discountCents: requiredNonNegativeInteger(value.discountCents, "held order discountCents"),
    actualCents: requiredNonNegativeInteger(value.actualCents, "held order actualCents"),
    revision: requiredNonNegativeInteger(value.revision, "held order revision"),
  });
}

function mapPublishResult(
  value: GeneratedPublishResponse,
): SharedHeldOrderPublishResult {
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "publish holdGuid"),
    status: mapHoldStatus(value.status),
    revision: requiredNonNegativeInteger(value.revision, "publish revision"),
    createdAtIso: requiredIso(value.createdAtUtc, "publish createdAt"),
    alreadyExists: value.alreadyExists === true,
  });
}

function mapCancelResult(
  value: GeneratedCancelResponse,
): SharedHeldOrderCancelResult {
  const status = mapHoldStatus(value.status);
  if (status !== "Cancelled") {
    throw new TypeError("Cancelled held order response status is invalid.");
  }
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "cancel holdGuid"),
    status,
    revision: requiredNonNegativeInteger(value.revision, "cancel revision"),
    updatedAtIso: requiredIso(value.updatedAtUtc, "cancel updatedAt"),
    alreadyCancelled: value.alreadyCancelled === true,
  });
}

function mapPrepareResult(
  value: GeneratedPrepareResponse,
): SharedHeldOrderPrepareResult {
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "prepare holdGuid"),
    claimGuid: requiredText(value.claimGuid, "prepare claimGuid"),
    status: mapClaimStatus(value.status),
    payload: normalizeSharedSaleCartV1(value.payload),
    claimantDeviceCode: requiredText(value.claimantDeviceCode, "prepare device"),
    claimantCashierId: requiredText(value.claimantCashierId, "prepare cashier id"),
    claimantCashierName: requiredText(value.claimantCashierName, "prepare cashier name"),
    createdAtIso: requiredIso(value.createdAtUtc, "prepare createdAt"),
    expiresAtIso: nullableIso(value.expiresAtUtc, "prepare expiresAt"),
    revision: requiredNonNegativeInteger(value.revision, "prepare revision"),
    alreadyExists: value.alreadyExists === true,
  });
}

function mapClaim(value: GeneratedClaim): SharedHeldOrderClaimDto {
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "claim holdGuid"),
    claimGuid: requiredText(value.claimGuid, "claim claimGuid"),
    status: mapClaimStatus(value.status),
    storeCode: requiredText(value.storeCode, "claim storeCode"),
    claimantDeviceCode: requiredText(value.claimantDeviceCode, "claim device"),
    claimantCashierId: requiredText(value.claimantCashierId, "claim cashier id"),
    claimantCashierName: requiredText(value.claimantCashierName, "claim cashier name"),
    createdAtIso: requiredIso(value.createdAtUtc, "claim createdAt"),
    updatedAtIso: requiredIso(value.updatedAtUtc, "claim updatedAt"),
    expiresAtIso: nullableIso(value.expiresAtUtc, "claim expiresAt"),
    activatedAtIso: nullableIso(value.activatedAtUtc, "claim activatedAt"),
    releasedAtIso: nullableIso(value.releasedAtUtc, "claim releasedAt"),
    forceReleased: value.forceReleased === true,
    forceReleaseReason: nullableText(value.forceReleaseReason, "claim force release reason"),
    forceReleaseCashierId: nullableText(value.forceReleaseCashierId, "claim force release cashier id"),
    forceReleaseCashierName: nullableText(value.forceReleaseCashierName, "claim force release cashier name"),
    forceReleasedAtIso: nullableIso(value.forceReleasedAtUtc, "claim forceReleasedAt"),
    revision: requiredNonNegativeInteger(value.revision, "claim revision"),
    alreadyExists: value.alreadyExists === true,
  });
}

function mapRecoveryClaim(
  value: GeneratedRecoveryClaim,
): SharedHeldOrderRecoveryClaimDto {
  return Object.freeze({
    holdGuid: requiredText(value.holdGuid, "recovery holdGuid"),
    claimGuid: requiredText(value.claimGuid, "recovery claimGuid"),
    status: mapClaimStatus(value.status),
    storeCode: requiredText(value.storeCode, "recovery storeCode"),
    claimantDeviceCode: requiredText(value.claimantDeviceCode, "recovery device"),
    claimantCashierId: requiredText(value.claimantCashierId, "recovery cashier id"),
    claimantCashierName: requiredText(value.claimantCashierName, "recovery cashier name"),
    payload: normalizeSharedSaleCartV1(value.payload),
    createdAtIso: requiredIso(value.createdAtUtc, "recovery createdAt"),
    updatedAtIso: requiredIso(value.updatedAtUtc, "recovery updatedAt"),
    expiresAtIso: nullableIso(value.expiresAtUtc, "recovery expiresAt"),
    activatedAtIso: nullableIso(value.activatedAtUtc, "recovery activatedAt"),
    revision: requiredNonNegativeInteger(value.revision, "recovery revision"),
  });
}

function mapHoldStatus(
  value: components["schemas"]["SharedHeldOrderStatus"] | undefined,
): SharedHeldOrderStatus {
  if (value === 1) return "Pending";
  if (value === 2) return "Claimed";
  if (value === 3) return "Completed";
  if (value === 4) return "Cancelled";
  throw new TypeError("Shared held order status is invalid.");
}

function mapClaimStatus(value: components["schemas"]["SharedHeldOrderClaimStatus"] | undefined): SharedHeldOrderClaimStatus {
  if (value === 1) return "Prepared";
  if (value === 2) return "Active";
  if (value === 3) return "Released";
  if (value === 4) return "Completed";
  if (value === 5) return "Superseded";
  throw new TypeError("Shared held order claim status is invalid.");
}

function requiredArray<T>(value: readonly T[] | null | undefined, label: string): readonly T[] {
  if (!Array.isArray(value)) {
    throw new TypeError(`${label} must be an array.`);
  }
  return value;
}

function requiredBoolean(value: boolean | undefined, label: string): boolean {
  if (typeof value !== "boolean") {
    throw new TypeError(`${label} must be a boolean.`);
  }
  return value;
}

function requiredText(value: string | null | undefined, label: string): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new TypeError(`${label} must be a non-empty string.`);
  }
  return value;
}

function nullableText(value: string | null | undefined, label: string): string | null {
  if (value === null || value === undefined) return null;
  return requiredText(value, label);
}

function requiredNonNegativeInteger(
  value: number | undefined,
  label: string,
): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) {
    throw new TypeError(`${label} must be a non-negative safe integer.`);
  }
  return value;
}

function requiredIso(value: string | null | undefined, label: string): string {
  const text = requiredText(value, label);
  if (!Number.isFinite(Date.parse(text))) {
    throw new TypeError(`${label} must be a valid ISO timestamp.`);
  }
  return text;
}

function nullableIso(value: string | null | undefined, label: string): string | null {
  if (value === null || value === undefined) return null;
  return requiredIso(value, label);
}
