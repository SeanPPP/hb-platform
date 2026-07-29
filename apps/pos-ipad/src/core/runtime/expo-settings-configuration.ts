import type {
  SettingsAppUpdateSnapshot,
  SettingsPaymentSettingsInput,
} from "../../features/settings/settings-presenter";
import type { PosIpadUpdatePolicy } from "../contracts/app-updates";

import type { PosPaymentPublicExtra } from "./payment-runtime-config";

export type SettingsFetchPort = (
  url: string,
  init: Readonly<{ method: "GET"; signal: AbortSignal }>,
) => Promise<Readonly<{ ok: boolean }>>;

export function settingsPaymentConfiguration(
  input: PosPaymentPublicExtra | null | undefined,
): SettingsPaymentSettingsInput | null {
  if (input?.provider === "square") {
    const square = validSquare(input.square);
    return square
      ? Object.freeze({
          provider: "square" as const,
          square,
          linkly: null,
        })
      : null;
  }
  if (input?.provider === "linkly") {
    const linkly = validLinkly(input.linkly);
    return linkly
      ? Object.freeze({
          provider: "linkly" as const,
          square: null,
          linkly,
        })
      : null;
  }
  return null;
}

export function settingsAppUpdateSnapshot(input: Readonly<{
  channel: string;
  currentVersion: string;
  policy: PosIpadUpdatePolicy | null;
  restartAvailable: boolean;
}>): SettingsAppUpdateSnapshot {
  return Object.freeze({
    channel: requiredText(input.channel, 64),
    currentVersion: requiredText(input.currentVersion, 64),
    availableVersion: input.policy?.latestVersion ?? null,
    updateRequired: input.policy?.forceUpdate === true,
    restartAvailable: input.restartAvailable,
  });
}

export function createSettingsApiHealthProbe(
  fetcher: SettingsFetchPort,
): (healthUrl: string, signal: AbortSignal) => Promise<boolean> {
  return async (healthUrl, signal) => {
    if (signal.aborted) throw abortError();
    try {
      const response = await fetcher(healthUrl, {
        method: "GET",
        signal,
      });
      if (signal.aborted) throw abortError();
      return response.ok === true;
    } catch {
      if (signal.aborted) throw abortError();
      return false;
    }
  };
}

function validSquare(
  input: PosPaymentPublicExtra["square"] | undefined,
): Readonly<{
  environment: "Sandbox" | "Production";
  deviceId: string;
  locationId: string;
}> | null {
  if (
    !input ||
    !validEnvironment(input.environment) ||
    !validIdentifier(input.deviceId) ||
    !validIdentifier(input.locationId)
  ) {
    return null;
  }
  return Object.freeze({
    environment: input.environment,
    deviceId: input.deviceId.trim(),
    locationId: input.locationId.trim(),
  });
}

function validLinkly(
  input: PosPaymentPublicExtra["linkly"] | undefined,
): Readonly<{
  environment: "Sandbox" | "Production";
}> | null {
  if (!input || !validEnvironment(input.environment)) return null;
  return Object.freeze({ environment: input.environment });
}

function validEnvironment(
  value: unknown,
): value is "Sandbox" | "Production" {
  return value === "Sandbox" || value === "Production";
}

function validIdentifier(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.trim().length > 0 &&
    value.trim().length <= 256 &&
    !/[\u0000-\u001f\u007f]/u.test(value)
  );
}

function requiredText(value: string, maximum: number): string {
  const normalized = value.trim();
  if (
    !normalized ||
    normalized.length > maximum ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw new Error("Settings public metadata is invalid.");
  }
  return normalized;
}

function abortError(): Error {
  return Object.assign(new Error("Settings request aborted."), {
    name: "AbortError",
  });
}
