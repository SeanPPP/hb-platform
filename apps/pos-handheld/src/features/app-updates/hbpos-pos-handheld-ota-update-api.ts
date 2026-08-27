import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";

import {
  isTrustedPosHandheldOtaChannel,
  normalizePosHandheldOtaUpdatePolicy,
  POS_HANDHELD_PRODUCTION_CHANNEL,
  type PosHandheldOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";
import type { DeviceSystem } from "@/core/contracts/security";

export type PosHandheldOtaUpdateClientMetadata = Readonly<{
  runtimeVersion: string;
  currentUpdateId: string | null;
  currentUpdateGroupId: string | null;
}>;

export interface PosHandheldOtaUpdatePolicyRemotePort {
  getPolicy(
    metadata: PosHandheldOtaUpdateClientMetadata,
  ): Promise<PosHandheldOtaUpdatePolicy>;
}

type OtaUpdateQuery = Readonly<{
  runtimeVersion: string;
  currentUpdateId: string | undefined;
  currentUpdateGroupId: string | undefined;
}>;

/**
 * OTA 端点只读取标准 ApiResponse<T>.data，并将后端十一字段合同精确绑定到
 * 本机 platform、请求 runtimeVersion 与签名构建 channel。
 */
export class HbposPosHandheldOtaUpdateApi
  implements PosHandheldOtaUpdatePolicyRemotePort
{
  public constructor(
    private readonly transport: HbposTransport,
    private readonly platform: DeviceSystem,
    private readonly configuredChannel: string,
  ) {}

  public async getPolicy(
    metadata: PosHandheldOtaUpdateClientMetadata,
  ): Promise<PosHandheldOtaUpdatePolicy> {
    const platform = requiredDevicePlatform(this.platform);
    const expectedChannel = requiredToken(
      this.configuredChannel,
      "configuredChannel",
    );
    if (expectedChannel !== POS_HANDHELD_PRODUCTION_CHANNEL) {
      throw new TypeError(
        "Handheld OTA requires the compiled production channel.",
      );
    }
    const requestMetadata = normalizeMetadata(metadata);
    const response = await this.transport.request<
      HbposEnvelope<PosHandheldOtaUpdatePolicy>
    >({
      method: "GET",
      url: "/api/v1/app-updates/pos-handheld/ota",
      params: requestMetadata,
    });
    const policy = normalizePosHandheldOtaUpdatePolicy(
      unwrapHbposEnvelope(response.data),
    );
    if (policy.platform !== platform) {
      throw new TypeError(
        "Handheld OTA response platform does not match the requesting device.",
      );
    }
    if (policy.runtimeVersion !== requestMetadata.runtimeVersion) {
      throw new TypeError(
        "Handheld OTA response runtimeVersion does not match the request.",
      );
    }
    if (
      !isTrustedPosHandheldOtaChannel(
        policy.channel,
        expectedChannel,
        platform,
      )
    ) {
      throw new TypeError(
        "Handheld OTA response channel is outside the trusted production channel scope.",
      );
    }
    return policy;
  }
}

function requiredDevicePlatform(value: unknown): DeviceSystem {
  if (value !== "iOS" && value !== "Android") {
    throw new TypeError("Handheld OTA device platform is invalid.");
  }
  return value;
}

function normalizeMetadata(
  metadata: PosHandheldOtaUpdateClientMetadata,
): OtaUpdateQuery {
  if (!metadata || typeof metadata !== "object") {
    throw new TypeError("Handheld OTA update metadata is invalid.");
  }
  return Object.freeze({
    runtimeVersion: requiredToken(
      metadata.runtimeVersion,
      "runtimeVersion",
    ),
    currentUpdateId: optionalToken(
      metadata.currentUpdateId,
      "currentUpdateId",
      256,
    ),
    currentUpdateGroupId: optionalUuid(
      metadata.currentUpdateGroupId,
      "currentUpdateGroupId",
    ),
  });
}

function optionalUuid(
  value: unknown,
  field: string,
): string | undefined {
  if (value === null) return undefined;
  if (typeof value !== "string") {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  const normalized = value.trim().toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  return normalized;
}

function optionalToken(
  value: unknown,
  field: string,
  maximum: number,
): string | undefined {
  if (value === null) return undefined;
  return requiredToken(value, field, maximum);
}

function requiredToken(
  value: unknown,
  field: string,
  maximum = 120,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)
  ) {
    throw new TypeError(`Handheld OTA update ${field} is invalid.`);
  }
  return normalized;
}
