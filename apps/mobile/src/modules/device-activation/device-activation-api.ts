import { apiClient } from "@/shared/api/client";
import type {
  MobileDeviceAccountTokenResponse,
  MobileDeviceActivationCommitResult,
  MobileDeviceActivationPreview,
  MobileDeviceSystem,
  PendingMobileDeviceActivation,
} from "./types";
import {
  normalizeMobileDeviceAccountToken,
  normalizeMobileDeviceActivationCommitResult,
  normalizeMobileDeviceActivationPreview,
} from "./device-activation-contract";
import { prepareMobileDeviceActivationCommitRequest } from "./device-activation-request";

const PRIVATE_REQUEST_HEADERS = {
  "X-Skip-Auth-Redirect": "1",
  "X-Skip-Auth-Recovery": "1",
  "X-Skip-Center-Log": "1",
} as const;

export async function previewMobileDeviceActivationApi(input: {
  activationCode: string;
  deviceSystem: MobileDeviceSystem;
}, apiHost?: string): Promise<MobileDeviceActivationPreview> {
  const response = await apiClient.post(
    "/mobile/v1/device-activation/preview",
    input,
    {
      headers: {
        ...PRIVATE_REQUEST_HEADERS,
        ...(apiHost ? { "X-Client-Api-Host": apiHost } : {}),
        "X-Client-Skip-Authentication": "1",
      },
    },
  );
  return normalizeMobileDeviceActivationPreview(response.data);
}

export async function commitMobileDeviceActivationApi(
  pending: PendingMobileDeviceActivation,
  recoveryOnly: boolean,
): Promise<MobileDeviceActivationCommitResult> {
  const prepared = await prepareMobileDeviceActivationCommitRequest(
    pending,
    recoveryOnly,
    exchangeMobileDeviceSessionApi,
  );

  const response = await apiClient.post(
    `/mobile/v1/device-activation/${pending.mode}`,
    prepared.body,
    {
      headers: {
        ...PRIVATE_REQUEST_HEADERS,
        "X-Client-Api-Host": pending.apiHost,
        ...(prepared.recoveryOnly
          ? {
              "X-HB-Mobile-Activation-Recovery-Only": "true",
            }
          : {}),
        ...(prepared.skipAuthentication
          ? { "X-Client-Skip-Authentication": "1" }
          : {}),
        ...(prepared.accessToken
          ? { Authorization: `Bearer ${prepared.accessToken}` }
          : {}),
      },
    },
  );
  return normalizeMobileDeviceActivationCommitResult(response.data);
}

export async function exchangeMobileDeviceSessionApi(input: {
  hardwareId: string;
  credential: string;
  apiHost?: string;
}): Promise<MobileDeviceAccountTokenResponse> {
  const response = await apiClient.post(
    "/mobile/v1/device-session/exchange",
    {
      hardwareId: input.hardwareId,
      credential: input.credential,
    },
    {
      headers: {
        ...PRIVATE_REQUEST_HEADERS,
        ...(input.apiHost ? { "X-Client-Api-Host": input.apiHost } : {}),
        "X-Client-Skip-Authentication": "1",
      },
    },
  );
  return normalizeMobileDeviceAccountToken(response.data);
}

export async function unbindMobileDeviceAccountApi(
  reason?: string,
  accessToken?: string,
  apiHost?: string,
) {
  await apiClient.post(
    "/mobile/v1/device-binding/unbind",
    reason ? { reason } : {},
    {
      headers: {
        "X-Skip-Center-Log": "1",
        ...(apiHost ? { "X-Client-Api-Host": apiHost } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
    },
  );
}
