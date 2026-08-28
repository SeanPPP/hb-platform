import {
  mergeSettingsSquareDevices,
  normalizeSettingsSquareDeviceId,
  type SettingsSquareCreateDeviceCodeInput,
  type SettingsSquareDevice,
  type SettingsSquareDeviceCode,
  type SettingsSquareEnvironment,
  type SettingsSquareLocation,
  type SettingsSquareSetupPort,
  type SettingsSquareTokenStatus,
} from "@hb/pos-domain/features/settings/settings-square-setup";
import type { components } from "@hb/pos-api-client/openapi";
import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "../api/hbpos-api";

type SquareTokenStatusResponse =
  components["schemas"]["SquareTokenStatusResponse"];
type SquareLocationDto = components["schemas"]["SquareLocationDto"];
type SquareDeviceDto = components["schemas"]["SquareDeviceDto"];
type SquareDeviceCodeDto = components["schemas"]["SquareDeviceCodeDto"];
type SquareCreateDeviceCodeRequest =
  components["schemas"]["SquareCreateDeviceCodeRequest"];

/**
 * Square token 只保存在 Hbpos API；客户端 setup 端口仅消费公开状态和设备资料。
 */
export class HbposSettingsSquareSetupApi implements SettingsSquareSetupPort {
  public constructor(private readonly transport: HbposTransport) {}

  public async getSquareTokenStatus(
    environment: SettingsSquareEnvironment,
    signal: AbortSignal,
  ): Promise<SettingsSquareTokenStatus> {
    const response = await this.transport.request<
      HbposEnvelope<SquareTokenStatusResponse>
    >({
      method: "GET",
      url: "/api/v1/square/token",
      params: { environment },
      signal,
    });
    const payload = unwrapHbposEnvelope(response.data);
    const returnedEnvironment = requiredEnvironment(payload.environment);
    if (returnedEnvironment !== environment) {
      throw new Error("SETTINGS_SQUARE_ENVIRONMENT_MISMATCH");
    }
    return Object.freeze({
      environment: returnedEnvironment,
      configured: payload.configured === true,
      enabled: payload.enabled === true,
      updatedAt: normalizedOptionalText(payload.updatedAt),
    });
  }

  public async listSquareLocations(
    environment: SettingsSquareEnvironment,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareLocation[]> {
    const response = await this.transport.request<
      HbposEnvelope<readonly SquareLocationDto[]>
    >({
      method: "GET",
      url: "/api/v1/square/locations",
      params: { environment },
      signal,
    });
    return Object.freeze(
      requiredArray<SquareLocationDto>(
        unwrapHbposEnvelope(response.data),
        "locations",
      )
        .flatMap((location): SettingsSquareLocation[] => {
          const id = normalizedOptionalText(location.id);
          if (!id) return [];
          return [
            Object.freeze({
              id,
              name: normalizedOptionalText(location.name) ?? id,
              status: normalizedOptionalText(location.status),
              currency: normalizedOptionalText(location.currency),
              country: normalizedOptionalText(location.country),
            }),
          ];
        }),
    );
  }

  public async listSquareDevices(
    environment: SettingsSquareEnvironment,
    locationId: string,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareDevice[]> {
    const normalizedLocationId = requiredText(
      locationId,
      "SETTINGS_SQUARE_LOCATION_ID_REQUIRED",
    );
    throwIfAborted(signal);
    // Square Sandbox 不支持 Devices API，必须直接使用官方 checkout 测试终端。
    if (environment === "Sandbox") {
      return mergeSettingsSquareDevices("Sandbox", normalizedLocationId, []);
    }
    const response = await this.transport.request<
      HbposEnvelope<readonly SquareDeviceDto[]>
    >({
      method: "GET",
      url: "/api/v1/square/devices",
      params: { environment, locationId: normalizedLocationId },
      signal,
    });
    const devices = requiredArray<SquareDeviceDto>(
      unwrapHbposEnvelope(response.data),
      "devices",
    ).flatMap((device): SettingsSquareDevice[] => {
      const id = normalizeSettingsSquareDeviceId(device.id);
      if (!id) return [];
      return [
        Object.freeze({
          id,
          code: normalizedOptionalText(device.code),
          name: normalizedOptionalText(device.name) ?? id,
          status: normalizedOptionalText(device.status),
          locationId: normalizedOptionalText(device.locationId),
          sandboxTest: false,
        }),
      ];
    });
    return mergeSettingsSquareDevices(
      environment,
      normalizedLocationId,
      devices,
    );
  }

  public async listSquareDeviceCodes(
    environment: SettingsSquareEnvironment,
    locationId: string,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareDeviceCode[]> {
    const normalizedLocationId = requiredText(
      locationId,
      "SETTINGS_SQUARE_LOCATION_ID_REQUIRED",
    );
    const response = await this.transport.request<
      HbposEnvelope<readonly SquareDeviceCodeDto[]>
    >({
      method: "GET",
      url: "/api/v1/square/device-codes",
      params: { environment, locationId: normalizedLocationId },
      signal,
    });
    return Object.freeze(
      requiredArray<SquareDeviceCodeDto>(
        unwrapHbposEnvelope(response.data),
        "device codes",
      ).flatMap((deviceCode): SettingsSquareDeviceCode[] => {
        const mapped = mapDeviceCode(deviceCode);
        return mapped ? [mapped] : [];
      }),
    );
  }

  public async createSquareDeviceCode(
    input: SettingsSquareCreateDeviceCodeInput,
    signal: AbortSignal,
  ): Promise<SettingsSquareDeviceCode> {
    const request: SquareCreateDeviceCodeRequest = {
      environment: input.environment,
      idempotencyKey: requiredText(
        input.idempotencyKey,
        "SETTINGS_SQUARE_IDEMPOTENCY_KEY_REQUIRED",
      ),
      locationId: requiredText(
        input.locationId,
        "SETTINGS_SQUARE_LOCATION_ID_REQUIRED",
      ),
      ...(normalizedOptionalText(input.name)
        ? { name: normalizedOptionalText(input.name)! }
        : {}),
      ...(normalizedOptionalText(input.productType)
        ? { productType: normalizedOptionalText(input.productType)! }
        : {}),
    };
    // 该 POST 只执行一次；响应不确定时由调用方决定是否以同一耐久键恢复。
    const response = await this.transport.request<
      HbposEnvelope<SquareDeviceCodeDto>
    >({
      method: "POST",
      url: "/api/v1/square/device-codes",
      data: request,
      signal,
    });
    return requiredDeviceCode(unwrapHbposEnvelope(response.data));
  }

  public async getSquareDeviceCode(
    environment: SettingsSquareEnvironment,
    deviceCodeId: string,
    signal: AbortSignal,
  ): Promise<SettingsSquareDeviceCode> {
    const normalizedDeviceCodeId = requiredText(
      deviceCodeId,
      "SETTINGS_SQUARE_DEVICE_CODE_ID_REQUIRED",
    );
    const response = await this.transport.request<
      HbposEnvelope<SquareDeviceCodeDto>
    >({
      method: "GET",
      url:
        `/api/v1/square/device-codes/` +
        encodeURIComponent(normalizedDeviceCodeId),
      params: { environment },
      signal,
    });
    return requiredDeviceCode(unwrapHbposEnvelope(response.data));
  }
}

function mapDeviceCode(
  payload: SquareDeviceCodeDto | null | undefined,
): SettingsSquareDeviceCode | null {
  if (!payload) return null;
  const id = normalizedOptionalText(payload.id);
  if (!id) return null;
  return Object.freeze({
    id,
    code: normalizedOptionalText(payload.code),
    status: normalizedOptionalText(payload.status),
    deviceId: normalizeSettingsSquareDeviceId(payload.deviceId),
    locationId: normalizedOptionalText(payload.locationId),
    name: normalizedOptionalText(payload.name) ?? id,
  });
}

function requiredDeviceCode(
  payload: SquareDeviceCodeDto | null | undefined,
): SettingsSquareDeviceCode {
  const mapped = mapDeviceCode(payload);
  if (!mapped) throw new Error("SETTINGS_SQUARE_DEVICE_CODE_INVALID");
  return mapped;
}

function requiredArray<T>(value: unknown, label: string): readonly T[] {
  if (!Array.isArray(value)) {
    throw new Error(`SETTINGS_SQUARE_${label.toUpperCase().replaceAll(" ", "_")}_INVALID`);
  }
  return value as readonly T[];
}

function requiredEnvironment(value: unknown): SettingsSquareEnvironment {
  if (value === "Sandbox" || value === "Production") return value;
  throw new Error("SETTINGS_SQUARE_ENVIRONMENT_INVALID");
}

function requiredText(value: unknown, code: string): string {
  const normalized = normalizedOptionalText(value);
  if (!normalized) throw new Error(code);
  return normalized;
}

function normalizedOptionalText(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function throwIfAborted(signal: AbortSignal): void {
  if (!signal.aborted) return;
  const error = new Error("Settings Square setup aborted.");
  error.name = "AbortError";
  throw error;
}
