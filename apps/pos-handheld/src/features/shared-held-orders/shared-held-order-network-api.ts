import type {
  SharedLineDiscountStateV1,
  SharedSaleLineV1,
} from "./shared-sale-cart-v1";
import {
  normalizeSharedSaleCart,
  type SharedSaleCartPayload,
} from "./shared-sale-cart-v2";

import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import type { components } from "@/generated/hbpos/schema";

type GeneratedCapabilities =
  components["schemas"]["SharedHeldOrderCapabilitiesResponse"];
type GeneratedListItem = components["schemas"]["SharedHeldOrderListItemDto"];
type GeneratedPublishRequest =
  components["schemas"]["SharedHeldOrderPublishRequest"];
type GeneratedPublishResponse = components["schemas"]["SharedHeldOrderPublishResponse"];
type GeneratedPrepareRequest = components["schemas"]["SharedHeldOrderClaimPrepareRequest"];
type GeneratedPrepareResponse =
  components["schemas"]["SharedHeldOrderClaimPrepareResponse"];
type GeneratedClaim = components["schemas"]["SharedHeldOrderClaimDto"];
type GeneratedRecoveryClaim =
  components["schemas"]["SharedHeldOrderRecoveryClaimDto"];
type GeneratedCancelResponse = components["schemas"]["SharedHeldOrderCancelResponse"];
type GeneratedCart = NonNullable<GeneratedPublishRequest["cart"]>;
type GeneratedLineV1 = components["schemas"]["SharedSaleLineV1"];
type GeneratedLineV2 = components["schemas"]["SharedSaleLineV2"];
type GeneratedDiscount = components["schemas"]["SharedLineDiscountStateV1"];

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
  /** 兼容旧服务端保留字段；当前服务端主字段仍固定为 V1。 */
  payloadVersion: 1;
  supportedPayloadVersions: readonly (1 | 2)[];
  preferredPayloadVersion: 1 | 2;
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
  payload: SharedSaleCartPayload;
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
  payload: SharedSaleCartPayload;
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
    cart: SharedSaleCartPayload;
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
 * payload 只经按 version 分派的 normalizeSharedSaleCart 进入内存；任何路径不把
 * payload 拼进异常消息。版本 query 使用重复键 URL，避免 Axios 默认 [] 后缀。
 */
export class SharedHeldOrderNetworkApi implements SharedHeldOrderNetworkApiPort {
  public constructor(private readonly transport: HbposTransport) {}

  public async getCapabilities(): Promise<SharedHeldOrderCapabilities> {
    return this.get<GeneratedCapabilities>("/api/v1/held-orders/capabilities")
      .then((body) => ({
        enabled: requiredBoolean(body.enabled, "capabilities enabled"),
        payloadVersion: requiredPayloadVersion(body.payloadVersion),
        supportedPayloadVersions: normalizeSupportedPayloadVersions(
          body.supportedPayloadVersions,
        ),
        preferredPayloadVersion: normalizePreferredPayloadVersion(
          body.preferredPayloadVersion,
          body.supportedPayloadVersions,
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
    return this.get<readonly GeneratedListItem[]>(
      withSupportedPayloadVersions("/api/v1/held-orders"),
    )
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
    cart: SharedSaleCartPayload;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPublishResult> {
    const request: GeneratedPublishRequest = {
      holdGuid: requiredText(input.holdGuid, "hold guid"),
      storeCode: requiredText(input.storeCode, "store code"),
      deviceCode: requiredText(input.deviceCode, "device code"),
      cart: toGeneratedCart(input.cart),
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
      withSupportedPayloadVersions(
        `/api/v1/held-orders/${encodeURIComponent(holdGuid)}/claims/prepare`,
      ),
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
    return this.get<readonly GeneratedRecoveryClaim[]>(
      withSupportedPayloadVersions("/api/v1/held-orders/claims/mine"),
    )
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
    payload: normalizeResponsePayload(value.payload),
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
    payload: normalizeResponsePayload(value.payload),
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

function normalizeResponsePayload(value: unknown): SharedSaleCartPayload {
  try {
    return normalizeSharedSaleCart(value);
  } catch {
    // 服务端成功 envelope 中的 payload 损坏属于稳定 Invalid；错误文案不得
    // 携带字段名、商品信息或原始 JSON。
    throw new SharedHeldOrderApiError("共享挂单载荷无效。", {
      kind: "Invalid",
    });
  }
}

/**
 * 领域 canonical 使用 readonly 集合；OpenAPI DTO 使用可变数组。这里显式复制到
 * generated union，既保持 V1/V2 的真实 schema 约束，也避免把领域对象交给 transport
 * 后被意外修改。OpenAPI 将 nullable provenance 表达为可选字段，因此 null 在请求 DTO
 * 中省略；服务端反序列化后仍恢复为 null，canonical/指纹语义不变。
 */
function toGeneratedCart(value: SharedSaleCartPayload): GeneratedCart {
  const cart = normalizeSharedSaleCart(value);
  const promotions = cart.pricingState.promotions.map((promotion) => ({
    ...promotion,
    products: promotion.products.map((product) => ({ ...product })),
  }));
  const pricing = {
    revision: cart.pricingState.revision,
    mode: cart.pricingState.mode,
    asOfIso: cart.pricingState.asOfIso,
    promotions,
  };

  if (cart.version === 1) {
    return {
      version: 1,
      pricingState: {
        ...pricing,
        lines: cart.pricingState.lines.map(toGeneratedLineV1),
      },
    } as GeneratedCart;
  }

  return {
    version: 2,
    pricingState: {
      ...pricing,
      lines: cart.pricingState.lines.map((line): GeneratedLineV2 => ({
        ...toGeneratedLineV1(line),
        catalogDiscountBasisPoints: line.catalogDiscountBasisPoints,
      })),
    },
  } as GeneratedCart;
}

function toGeneratedLineV1(line: SharedSaleLineV1): GeneratedLineV1 {
  return {
    lineId: line.lineId,
    productCode: line.productCode,
    itemNumber: line.itemNumber,
    lookupCode: line.lookupCode,
    displayName: line.displayName,
    quantity: line.quantity,
    unitPriceCents: line.unitPriceCents,
    basePriceSource: line.basePriceSource,
    ...(line.syncProvenance === null
      ? {}
      : { syncProvenance: { ...line.syncProvenance } }),
    kind: line.kind,
    returnSourceKey: line.returnSourceKey,
    originalOrderGuid: line.originalOrderGuid,
    originalOrderDetailGuid: line.originalOrderDetailGuid,
    discountState: toGeneratedDiscount(line.discountState),
  };
}

function toGeneratedDiscount(discount: SharedLineDiscountStateV1): GeneratedDiscount {
  switch (discount.mode) {
    case "manual-amount":
      return { mode: discount.mode, cents: discount.cents };
    case "manual-percent":
      return { mode: discount.mode, basisPoints: discount.basisPoints };
    case "promotion":
      return {
        mode: discount.mode,
        cents: discount.cents,
        promotionIds: [...discount.promotionIds],
      };
    default:
      return { mode: "none" };
  }
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

function requiredPayloadVersion(value: number | undefined): 1 {
  if (value !== 1) {
    throw new TypeError("capabilities payloadVersion must remain 1.");
  }
  return 1;
}

function normalizeSupportedPayloadVersions(value: unknown): readonly (1 | 2)[] {
  // 老服务端没有新字段：只按冻结的 V1 能力处理，绝不猜测 V2。
  if (value === undefined) return Object.freeze([1] as const);
  if (!Array.isArray(value) || value.length === 0) {
    throw new TypeError("capabilities supportedPayloadVersions must be a non-empty array.");
  }
  const versions = value.map((version) => {
    if (version !== 1 && version !== 2) {
      throw new TypeError("capabilities supportedPayloadVersions contains an unsupported version.");
    }
    return version;
  });
  if (new Set(versions).size !== versions.length) {
    throw new TypeError("capabilities supportedPayloadVersions must not contain duplicates.");
  }
  return Object.freeze(versions);
}

function normalizePreferredPayloadVersion(
  value: unknown,
  supported: unknown,
): 1 | 2 {
  const preferred = value === undefined ? 1 : value;
  if (preferred !== 1 && preferred !== 2) {
    throw new TypeError("capabilities preferredPayloadVersion is invalid.");
  }
  const supportedVersions = normalizeSupportedPayloadVersions(supported);
  if (!supportedVersions.includes(preferred)) {
    throw new TypeError("capabilities preferredPayloadVersion is not supported.");
  }
  return preferred;
}

const SUPPORTED_PAYLOAD_VERSIONS_QUERY =
  "supportedPayloadVersions=1&supportedPayloadVersions=2";

function withSupportedPayloadVersions(path: string): string {
  return `${path}${path.includes("?") ? "&" : "?"}${SUPPORTED_PAYLOAD_VERSIONS_QUERY}`;
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
