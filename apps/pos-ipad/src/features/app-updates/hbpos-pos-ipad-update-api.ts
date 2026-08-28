import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import {
  normalizePosIpadUpdatePolicy,
  type PosIpadUpdatePolicy,
} from "@/core/contracts/app-updates";
import type { components, paths } from "@hb/pos-api-client/openapi";

type GeneratedGet = paths["/api/v1/app-updates/pos-ipad"]["get"];
type GeneratedQuery = NonNullable<GeneratedGet["parameters"]["query"]>;
type GeneratedResponse = components["schemas"]["PosIpadAppUpdateResponse"];

export type PosIpadUpdateClientMetadata = Readonly<{
  version: string;
  build: string;
  runtimeVersion: string;
}>;

export interface PosIpadUpdatePolicyRemotePort {
  getPolicy(
    metadata: PosIpadUpdateClientMetadata,
  ): Promise<PosIpadUpdatePolicy>;
}

/**
 * 该适配器只调用 OpenAPI 生成声明中的 iPad 专用 GET；本机版本信息与服务端策略都必须完整通过校验。
 */
export class HbposPosIpadUpdateApi implements PosIpadUpdatePolicyRemotePort {
  public constructor(private readonly transport: HbposTransport) {}

  public async getPolicy(
    metadata: PosIpadUpdateClientMetadata,
  ): Promise<PosIpadUpdatePolicy> {
    const query = normalizeMetadata(metadata);
    const response = await this.transport.request<
      HbposEnvelope<GeneratedResponse>
    >({
      method: "GET",
      url: "/api/v1/app-updates/pos-ipad",
      params: query,
    });
    return normalizePosIpadUpdatePolicy(unwrapHbposEnvelope(response.data));
  }
}

function normalizeMetadata(
  metadata: PosIpadUpdateClientMetadata,
): GeneratedQuery {
  if (!metadata || typeof metadata !== "object") {
    throw new TypeError("iPad update metadata is invalid.");
  }
  return {
    version: requiredVersion(metadata.version, "version"),
    build: requiredBuild(metadata.build),
    runtimeVersion: requiredRuntimeVersion(metadata.runtimeVersion),
  };
}

function requiredVersion(value: unknown, field: string): string {
  const normalized = requiredText(value, field);
  if (!/^v?\d+(?:\.\d+){0,3}$/iu.test(normalized)) {
    throw new TypeError(`iPad update ${field} is invalid.`);
  }
  return normalized;
}

function requiredRuntimeVersion(value: unknown): string {
  const normalized = requiredText(value, "runtimeVersion", 120);
  if (!/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(normalized)) {
    throw new TypeError("iPad update runtimeVersion is invalid.");
  }
  return normalized;
}

function requiredBuild(value: unknown): string {
  const normalized = requiredText(value, "build");
  if (!/^\d+(?:\.\d+){0,3}$/u.test(normalized)) {
    throw new TypeError("iPad update build is invalid.");
  }
  return normalized;
}

function requiredText(
  value: unknown,
  field: string,
  maximum = 64,
): string {
  if (typeof value !== "string") {
    throw new TypeError(`iPad update ${field} is invalid.`);
  }
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new TypeError(`iPad update ${field} is invalid.`);
  }
  return normalized;
}
