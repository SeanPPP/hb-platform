import type { PersistedAuthSessionKind } from "./auth-session-marker";

interface DeviceAccountRequestPolicyInput {
  requestedApiHost: string;
  bindingApiHost?: string | null;
  sessionKind: PersistedAuthSessionKind | null;
  skipAuthentication: boolean;
}

interface DeviceAccountRequestPolicy {
  apiHost: string;
  allowDeviceHeaders: boolean;
  allowBearerToken: boolean;
}

function comparableHost(value?: string | null) {
  return value?.trim().replace(/\.$/u, "").toLocaleLowerCase("en-US") ?? "";
}

export function deriveEffectiveAuthSessionKind(input: {
  persistedKind: PersistedAuthSessionKind | null;
  hasAccessToken: boolean;
  hasRefreshToken: boolean;
  hasBinding: boolean;
}): PersistedAuthSessionKind | null {
  if (input.persistedKind === "deviceAccount") {
    return "deviceAccount";
  }
  if (input.hasBinding && input.hasAccessToken && !input.hasRefreshToken) {
    // access token 先于 marker 落盘的崩溃窗口仍必须绑定到原 apiHost。
    return "deviceAccount";
  }
  return input.persistedKind;
}

export function isRelativeApiClientUrl(value?: string | null) {
  const url = value?.trim() ?? "";
  return !/^\/\//u.test(url) && !/^[a-z][a-z\d+.-]*:/iu.test(url);
}

export function removeRequestHeader(headers: unknown, name: string) {
  if (!headers || typeof headers !== "object") {
    return;
  }
  const headerCollection = headers as Record<string, unknown> & {
    delete?: (headerName: string) => unknown;
  };
  if (typeof headerCollection.delete === "function") {
    headerCollection.delete.call(headers, name);
  }
  const normalizedName = name.toLowerCase();
  for (const existingName of Object.keys(headerCollection)) {
    if (existingName.toLowerCase() === normalizedName) {
      delete headerCollection[existingName];
    }
  }
}

export function resolveDeviceAccountRequestPolicy({
  requestedApiHost,
  bindingApiHost,
  sessionKind,
  skipAuthentication,
}: DeviceAccountRequestPolicyInput): DeviceAccountRequestPolicy {
  if (skipAuthentication) {
    return {
      apiHost: requestedApiHost,
      allowDeviceHeaders: false,
      allowBearerToken: false,
    };
  }

  const normalizedBindingHost = comparableHost(bindingApiHost);
  if (sessionKind === "deviceAccount") {
    // 设备账号的 token 和兼容凭据只能发往绑定时确定的服务器。
    return normalizedBindingHost
      ? {
          apiHost: bindingApiHost!.trim(),
          allowDeviceHeaders: true,
          allowBearerToken: true,
        }
      : {
          apiHost: requestedApiHost,
          allowDeviceHeaders: false,
          allowBearerToken: false,
        };
  }

  if (
    normalizedBindingHost &&
    comparableHost(requestedApiHost) !== normalizedBindingHost
  ) {
    return {
      apiHost: requestedApiHost,
      allowDeviceHeaders: false,
      allowBearerToken: true,
    };
  }

  return {
    apiHost: requestedApiHost,
    allowDeviceHeaders: true,
    allowBearerToken: true,
  };
}
