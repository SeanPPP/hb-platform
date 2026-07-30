import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import {
  normalizePosIpadOtaUpdatePolicy,
  type PosIpadOtaUpdatePolicy,
} from "@/core/contracts/ota-app-updates";

export type PosIpadOtaUpdateClientMetadata = Readonly<{
  runtimeVersion: string;
  currentUpdateId: string | null;
  currentUpdateGroupId: string | null;
}>;

export interface PosIpadOtaUpdatePolicyRemotePort {
  getPolicy(
    metadata: PosIpadOtaUpdateClientMetadata,
  ): Promise<PosIpadOtaUpdatePolicy>;
}

type OtaUpdateQuery = Readonly<{
  runtimeVersion: string;
  currentUpdateId: string | undefined;
  currentUpdateGroupId: string | undefined;
}>;

/**
 * OTA 端点暂以窄手写 DTO 接入；它只读取标准 ApiResponse<T>.data，
 * 不扩张已有原生版本 GET 的 OpenAPI 六字段合同。
 */
export class HbposPosIpadOtaUpdateApi
  implements PosIpadOtaUpdatePolicyRemotePort
{
  public constructor(private readonly transport: HbposTransport) {}

  public async getPolicy(
    metadata: PosIpadOtaUpdateClientMetadata,
  ): Promise<PosIpadOtaUpdatePolicy> {
    const response = await this.transport.request<
      HbposEnvelope<PosIpadOtaUpdatePolicy>
    >({
      method: "GET",
      url: "/api/v1/app-updates/pos-ipad/ota",
      params: normalizeMetadata(metadata),
    });
    return normalizePosIpadOtaUpdatePolicy(
      unwrapHbposEnvelope(response.data),
    );
  }
}

function normalizeMetadata(
  metadata: PosIpadOtaUpdateClientMetadata,
): OtaUpdateQuery {
  if (!metadata || typeof metadata !== "object") {
    throw new TypeError("iPad OTA update metadata is invalid.");
  }
  return Object.freeze({
    runtimeVersion: requiredToken(
      metadata.runtimeVersion,
      "runtimeVersion",
    ),
    currentUpdateId: optionalUuid(
      metadata.currentUpdateId,
      "currentUpdateId",
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
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  const normalized = value.trim().toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u.test(
      normalized,
    )
  ) {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  return normalized;
}

function requiredToken(value: unknown, field: string): string {
  if (typeof value !== "string") {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 120 ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)
  ) {
    throw new TypeError(`iPad OTA update ${field} is invalid.`);
  }
  return normalized;
}
