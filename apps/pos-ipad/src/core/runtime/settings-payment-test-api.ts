import type { SettingsPaymentSettingsInput } from "../../features/settings/settings-presenter";
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
 * 设置页的测试调用不会创建 checkout 或扣款。Square 仅验证候选 location/device
 * 仍在后端可见；Linkly 复用 WPF 的 Backend Async logon-test。
 */
export class HbposSettingsPaymentTestApi {
  public constructor(private readonly transport: HbposTransport) {}

  public async test(
    provider: "square" | "linkly",
    input: SettingsPaymentSettingsInput,
  ): Promise<void> {
    if (provider === "square") {
      await this.testSquare(input);
      return;
    }
    await this.testLinkly(input);
  }

  private async testSquare(
    input: SettingsPaymentSettingsInput,
  ): Promise<void> {
    const configuration = input.square;
    if (!configuration) {
      throw new Error("Square test configuration is unavailable.");
    }
    const response = await this.transport.request<
      HbposEnvelope<readonly SquareDevice[]>
    >({
      method: "GET",
      url: "/api/v1/square/devices",
      params: {
        environment: configuration.environment,
        locationId: configuration.locationId,
      },
    });
    const devices = unwrapHbposEnvelope(response.data);
    const found = devices.some(
      (device) =>
        normalizedText(device.id) === configuration.deviceId &&
        normalizedText(device.locationId) ===
          configuration.locationId &&
        normalizedText(device.status).toUpperCase() !== "DISABLED",
    );
    if (!found) {
      throw new Error(
        "Square device is not available at the selected location.",
      );
    }
  }

  private async testLinkly(
    input: SettingsPaymentSettingsInput,
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
