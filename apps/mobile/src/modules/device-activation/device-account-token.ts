import axios from "axios";
import { buildApiBaseUrl } from "@/shared/api/config";
import { DeviceAccountStorage } from "./device-account-storage-runtime";
import { normalizeMobileDeviceAccountToken } from "./device-activation-contract";

export async function exchangeStoredDeviceAccountToken() {
  const binding = await DeviceAccountStorage.loadBinding();
  if (!binding) {
    throw new Error("DEVICE_ACCOUNT_BINDING_NOT_FOUND");
  }
  const response = await axios.post(
    `${buildApiBaseUrl(binding.apiHost)}/mobile/v1/device-session/exchange`,
    {
      hardwareId: binding.hardwareId,
      credential: binding.credential,
    },
    {
      timeout: 30_000,
      headers: { "Content-Type": "application/json" },
    },
  );
  const envelope = response.data as { data?: unknown; Data?: unknown };
  return normalizeMobileDeviceAccountToken(
    envelope?.data ?? envelope?.Data ?? response.data,
  );
}
