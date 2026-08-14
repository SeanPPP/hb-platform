import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import {
  normalizePosHandheldUpdatePolicy,
  type PosHandheldUpdatePolicy,
} from "@/core/contracts/app-updates";
import type { DeviceSystem } from "@/core/contracts/security";

const JAVASCRIPT_SAFE_INTEGER_MAX = "9007199254740991";

export type PosHandheldUpdateClientMetadata = Readonly<{
  version: string;
  build: string;
}>;

export interface PosHandheldUpdatePolicyRemotePort {
  getPolicy(
    metadata: PosHandheldUpdateClientMetadata,
  ): Promise<PosHandheldUpdatePolicy>;
}

/**
 * 该适配器只调用 OpenAPI 生成声明中的 Handheld 专用 GET；本机版本信息与服务端策略都必须完整通过校验。
 */
export class HbposPosHandheldUpdateApi implements PosHandheldUpdatePolicyRemotePort {
  public constructor(
    private readonly transport: HbposTransport,
    private readonly platform: DeviceSystem,
  ) {}

  public async getPolicy(
    metadata: PosHandheldUpdateClientMetadata,
  ): Promise<PosHandheldUpdatePolicy> {
    const platform = requiredDevicePlatform(this.platform);
    const query = normalizeMetadata(metadata);
    const response = await this.transport.request<HbposEnvelope<unknown>>({
      method: "GET",
      url: "/api/v1/app-updates/pos-handheld",
      params: query,
    });
    const policy = unwrapHbposEnvelope(response.data);
    if (
      policy &&
      typeof policy === "object" &&
      !Array.isArray(policy) &&
      "platform" in policy &&
      policy.platform !== platform
    ) {
      throw new TypeError("Handheld update response platform does not match device.");
    }
    return normalizePosHandheldUpdatePolicy(policy);
  }
}

function requiredDevicePlatform(value: unknown): DeviceSystem {
  if (value !== "iOS" && value !== "Android") {
    throw new TypeError("Handheld update device platform is invalid.");
  }
  return value;
}

function normalizeMetadata(
  metadata: PosHandheldUpdateClientMetadata,
): Readonly<{ version: string; build: string }> {
  if (!metadata || typeof metadata !== "object") {
    throw new TypeError("Handheld update metadata is invalid.");
  }
  return Object.freeze({
    version: requiredVersion(metadata.version, "version"),
    build: requiredBuild(metadata.build),
  });
}

function requiredVersion(value: unknown, field: string): string {
  const normalized = requiredText(value, field);
  if (!/^v?\d+(?:\.\d+){0,3}$/iu.test(normalized)) {
    throw new TypeError(`Handheld update ${field} is invalid.`);
  }
  return normalized;
}

function requiredBuild(value: unknown): string {
  // 必须逐字符校验原始 build；任何 trim 后才能成立的值都不能进入网络请求。
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > JAVASCRIPT_SAFE_INTEGER_MAX.length ||
    value[0] === "0" ||
    [...value].some((character) => character < "0" || character > "9") ||
    (value.length === JAVASCRIPT_SAFE_INTEGER_MAX.length &&
      value > JAVASCRIPT_SAFE_INTEGER_MAX)
  ) {
    throw new TypeError("Handheld update build is invalid.");
  }
  return value;
}

function requiredText(
  value: unknown,
  field: string,
  maximum = 64,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`Handheld update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`Handheld update ${field} is invalid.`);
  }
  return normalized;
}
