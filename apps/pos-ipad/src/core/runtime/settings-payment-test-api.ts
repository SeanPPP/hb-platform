import type { SettingsPaymentSettingsInput } from "../../features/settings/settings-presenter";
import {
  mergeSettingsSquareDevices,
  normalizeSettingsSquareDeviceId,
  type SettingsSquareDevice,
} from "../../features/settings/settings-square-setup";
import type { components } from "../../generated/hbpos/schema";
import {
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "../api/hbpos-api";

type SquareDevice = components["schemas"]["SquareDeviceDto"];
type LinklyLogon =
  components["schemas"]["LinklyCloudBackendLogonTestResponse"];

/**
 * 设置页的测试调用不会创建 checkout 或扣款。Square Sandbox 使用官方 checkout
 * 测试终端（Sandbox 不支持 Devices API），Production 才验证设备仍在后端可见；
 * Linkly 复用 WPF 的 Backend Async logon-test。
 */
export class HbposSettingsPaymentTestApi {
  public constructor(private readonly transport: HbposTransport) {}

  public async test(
    provider: "square" | "linkly",
    input: SettingsPaymentSettingsInput,
    signal: AbortSignal,
  ): Promise<void> {
    if (provider === "square") {
      await this.testSquare(input, signal);
      return;
    }
    await this.testLinkly(input, signal);
  }

  private async testSquare(
    input: SettingsPaymentSettingsInput,
    signal: AbortSignal,
  ): Promise<void> {
    const configuration = input.square;
    if (!configuration) {
      throw new Error("Square test configuration is unavailable.");
    }
    const locationId = requiredText(
      configuration.locationId,
      "Square location is required for payment test.",
    );
    const candidateDeviceId = normalizeSettingsSquareDeviceId(
      configuration.deviceId,
    );
    throwIfAborted(signal);
    const devices =
      configuration.environment === "Sandbox"
        ? mergeSettingsSquareDevices("Sandbox", locationId, [])
        : await this.listProductionSquareDevices(locationId, signal);
    const found = devices.some(
      (device) =>
        candidateDeviceId !== null &&
        device.id.toLowerCase() === candidateDeviceId.toLowerCase() &&
        normalizedText(device.locationId) === locationId &&
        normalizedText(device.status).toUpperCase() !== "DISABLED",
    );
    if (!found) {
      throw new Error(
        "Square device is not available at the selected location.",
      );
    }
  }

  private async listProductionSquareDevices(
    locationId: string,
    signal: AbortSignal,
  ): Promise<readonly SettingsSquareDevice[]> {
    const response = await this.transport.request<
      HbposEnvelope<readonly SquareDevice[]>
    >({
      method: "GET",
      url: "/api/v1/square/devices",
      params: {
        environment: "Production",
        locationId,
      },
      signal,
    });
    return mergeSettingsSquareDevices(
      "Production",
      locationId,
      unwrapHbposEnvelope(response.data).flatMap(
        (device): SettingsSquareDevice[] => {
          const id = normalizeSettingsSquareDeviceId(device.id);
          if (!id) return [];
          return [
            {
              id,
              code: normalizedOptionalText(device.code),
              name: normalizedOptionalText(device.name) ?? id,
              status: normalizedOptionalText(device.status),
              locationId: normalizedOptionalText(device.locationId),
              sandboxTest: false,
            },
          ];
        },
      ),
    );
  }

  private async testLinkly(
    input: SettingsPaymentSettingsInput,
    signal: AbortSignal,
  ): Promise<void> {
    const configuration = input.linkly;
    if (!configuration) {
      throw new Error("Linkly test configuration is unavailable.");
    }
    const response = await this.transport.request<
      HbposEnvelope<LinklyLogon>
    >({
      method: "POST",
      url: "/api/v1/linkly/cloud-backend/logon-test",
      params: {
        environment: configuration.environment,
      },
      signal,
    });
    const result = unwrapHbposEnvelope(response.data);
    if (result.succeeded !== true) {
      throw new Error("Linkly Cloud logon test was declined.");
    }
  }
}

function normalizedText(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function requiredText(value: unknown, message: string): string {
  const normalized = normalizedText(value);
  if (!normalized) throw new Error(message);
  return normalized;
}

function normalizedOptionalText(value: unknown): string | null {
  const normalized = normalizedText(value);
  return normalized || null;
}

function throwIfAborted(signal: AbortSignal): void {
  if (!signal.aborted) return;
  const error = new Error("Settings payment test aborted.");
  error.name = "AbortError";
  throw error;
}
