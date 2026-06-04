export type AppUpdateInfoValueKey =
  | "updates.unknown"
  | "updates.noChannel"
  | "updates.noUpdateId"
  | "updates.sourceEmbedded"
  | "updates.sourceOta";

export type AppUpdateInfoRow = {
  key: "version" | "runtime" | "channel" | "source" | "updateId";
  labelKey: string;
  value?: string;
  valueKey?: AppUpdateInfoValueKey;
};

export type AppUpdateInfo = {
  appVersion: string | null;
  runtimeVersion: string | null;
  channel: string | null;
  updateId: string | null;
  isEmbeddedLaunch: boolean;
};

export type AppUpdateCheckResult =
  | { status: "development-disabled" }
  | { status: "configuration-disabled" }
  | { status: "not-available" }
  | { status: "downloaded" };

export type AppUpdateCheckAvailability =
  | "available"
  | "development-disabled"
  | "configuration-disabled";

export function resolveAppUpdateCheckAvailability(options: {
  isDev: boolean;
  isEnabled: boolean;
}): AppUpdateCheckAvailability {
  if (options.isDev) {
    return "development-disabled";
  }

  if (!options.isEnabled) {
    return "configuration-disabled";
  }

  return "available";
}

function buildValueRow(
  key: AppUpdateInfoRow["key"],
  labelKey: string,
  value: string | null,
  fallbackKey: AppUpdateInfoValueKey
): AppUpdateInfoRow {
  if (value) {
    return { key, labelKey, value };
  }

  return { key, labelKey, valueKey: fallbackKey };
}

export function buildAppUpdateInfoRows(info: AppUpdateInfo): AppUpdateInfoRow[] {
  return [
    buildValueRow("version", "updates.version", info.appVersion, "updates.unknown"),
    buildValueRow("runtime", "updates.runtime", info.runtimeVersion, "updates.unknown"),
    buildValueRow("channel", "updates.channel", info.channel, "updates.noChannel"),
    {
      key: "source",
      labelKey: "updates.source",
      valueKey: info.isEmbeddedLaunch ? "updates.sourceEmbedded" : "updates.sourceOta",
    },
    buildValueRow("updateId", "updates.updateId", info.updateId, "updates.noUpdateId"),
  ];
}
