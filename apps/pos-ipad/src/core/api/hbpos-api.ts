import type { components } from "@/generated/hbpos/schema";

export const HBPOS_DEVICE_SYSTEM = "iPadOS" as const;

export type HbposEnvelope<T> = Readonly<{
  success?: boolean;
  data?: T;
  errorCode?: string | null;
  message?: string | null;
}>;

export type HbposTransportRequest = Readonly<{
  method: "GET" | "POST" | "PUT";
  url: string;
  data?: unknown;
  params?: Readonly<Record<string, string | number | boolean | undefined>>;
  headers?: Readonly<Record<string, string>>;
  /** 请求级取消信号；页面离开时用于中止仍在等待的目录下载。 */
  signal?: AbortSignal;
  /**
   * 请求级超时覆盖。0 表示不设置固定超时，适用于可由用户主动取消的长目录下载；
   * 未提供时继续使用 transport 的全局默认值。
   */
  timeoutMs?: number;
  /**
   * Axios 默认只接受 2xx；条件 GET 的 304 与幂等冲突的 409 必须由领域
   * 适配器读取并恢复，不能提前折叠成通用传输异常。
   */
  acceptedStatuses?: readonly number[];
  /**
   * 某些端点的 401 是业务身份校验失败，而非设备或当前收银员会话失效。
   * 该策略仅在返回 CASHIER_LOGIN_FAILED 时抑制全局 401 清理；
   * 403 只有携带明确设备撤销码时才允许锁定设备。
   */
  authenticationFailurePolicy?: "default" | "suppress-unauthorized";
}>;

export type HbposTransportResponse<T> = Readonly<{
  status: number;
  data: T;
}>;

export interface HbposTransport {
  request<T>(request: HbposTransportRequest): Promise<HbposTransportResponse<T>>;
}

export type HbposApiErrorKind = "transport" | "http" | "envelope";

export class HbposApiError extends Error {
  public readonly kind: HbposApiErrorKind;
  public readonly status: number | undefined;
  public readonly code: string | undefined;

  public constructor(
    message: string,
    details: Readonly<{ kind: HbposApiErrorKind; status?: number; code?: string }>
  ) {
    super(message);
    this.name = "HbposApiError";
    this.kind = details.kind;
    this.status = details.status;
    this.code = details.code;
  }
}

export function unwrapHbposEnvelope<T>(envelope: HbposEnvelope<T>): T {
  if (envelope.success !== true || envelope.data === undefined) {
    const code = envelope.errorCode ?? undefined;
    throw new HbposApiError(
      envelope.message ?? "Hbpos API request was rejected.",
      code ? { kind: "envelope", code } : { kind: "envelope" }
    );
  }

  return envelope.data;
}

export type DeviceRegisterRequest = components["schemas"]["DeviceRegisterRequest"];
export type DeviceRegisterResponse = components["schemas"]["DeviceRegisterResponse"];
export type DeviceVerifyRequest = components["schemas"]["DeviceVerifyRequest"];
export type DeviceVerifyResponse = components["schemas"]["DeviceVerifyResponse"];
export type DeviceReregisterRequest = components["schemas"]["DeviceReregisterRequest"];
export type DeviceReregisterResponse = components["schemas"]["DeviceReregisterResponse"];
export type CashierBarcodeLoginRequest = components["schemas"]["CashierBarcodeLoginRequest"];
export type CashierSessionDto = components["schemas"]["CashierSessionDto"];
type StoreDto = components["schemas"]["StoreDto"];

export type DeviceRegistrationStore = Readonly<{
  storeCode: string;
  storeName: string;
}>;

export class HbposDeviceApi {
  public constructor(private readonly transport: HbposTransport) {}

  public async listRegistrationStores(): Promise<
    readonly DeviceRegistrationStore[]
  > {
    const response = await this.transport.request<
      HbposEnvelope<readonly StoreDto[]>
    >({
      method: "GET",
      url: "/api/v1/catalog/stores"
    });

    // 未注册设备只能使用匿名目录；客户端仍严格过滤不完整或非活动记录。
    return unwrapHbposEnvelope(response.data)
      .flatMap((store): DeviceRegistrationStore[] => {
        const storeCode = store.storeCode?.trim() ?? "";
        const storeName = store.storeName?.trim() ?? "";
        return store.isActive === true && storeCode && storeName
          ? [{ storeCode, storeName }]
          : [];
      })
      .sort(
        (left, right) =>
          left.storeName.localeCompare(right.storeName) ||
          left.storeCode.localeCompare(right.storeCode)
      );
  }

  public async register(
    input: Omit<DeviceRegisterRequest, "deviceSystem">
  ): Promise<DeviceRegisterResponse> {
    const response = await this.transport.request<HbposEnvelope<DeviceRegisterResponse>>({
      method: "POST",
      url: "/api/v1/devices/register",
      data: { ...input, deviceSystem: HBPOS_DEVICE_SYSTEM }
    });
    return unwrapHbposEnvelope(response.data);
  }

  public async verify(
    input: Omit<DeviceVerifyRequest, "deviceSystem">
  ): Promise<DeviceVerifyResponse> {
    const response = await this.transport.request<HbposEnvelope<DeviceVerifyResponse>>({
      method: "POST",
      url: "/api/v1/devices/verify",
      data: { ...input, deviceSystem: HBPOS_DEVICE_SYSTEM }
    });
    return unwrapHbposEnvelope(response.data);
  }

  public async reregister(input: DeviceReregisterRequest): Promise<DeviceReregisterResponse> {
    const response = await this.transport.request<HbposEnvelope<DeviceReregisterResponse>>({
      method: "POST",
      url: "/api/v1/devices/reregister",
      data: input
    });
    return unwrapHbposEnvelope(response.data);
  }
}

export class HbposCashierApi {
  public constructor(private readonly transport: HbposTransport) {}

  public async barcodeLogin(input: CashierBarcodeLoginRequest): Promise<CashierSessionDto> {
    const response = await this.transport.request<HbposEnvelope<CashierSessionDto>>({
      method: "POST",
      url: "/api/v1/cashiers/barcode-login",
      data: input,
      authenticationFailurePolicy: "suppress-unauthorized",
    });
    return unwrapHbposEnvelope(response.data);
  }
}
