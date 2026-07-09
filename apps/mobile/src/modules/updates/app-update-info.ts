export type AppUpdateInfoValueKey =
  | "updates.unknown"
  | "updates.noBuildVersion"
  | "updates.noChannel"
  | "updates.noUpdateId"
  | "updates.sourceEmbedded"
  | "updates.sourceOta"
  | "updates.sourceUnknown";

export type AppUpdateInfoRow = {
  key: "version" | "build" | "runtime" | "channel" | "source" | "updateId";
  labelKey: string;
  value?: string;
  valueKey?: AppUpdateInfoValueKey;
};

export type AppUpdateInfo = {
  appVersion: string | null;
  appBuildVersion: string | null;
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

function resolveAppUpdateSourceKey(info: AppUpdateInfo): AppUpdateInfoValueKey {
  if (info.updateId) {
    return "updates.sourceOta";
  }

  if (info.isEmbeddedLaunch) {
    return "updates.sourceEmbedded";
  }

  return "updates.sourceUnknown";
}

export function formatAppPackageVersion(
  info: Pick<AppUpdateInfo, "appVersion" | "appBuildVersion">,
  fallback: string
) {
  if (info.appVersion && info.appBuildVersion) {
    return `${info.appVersion} (${info.appBuildVersion})`;
  }

  return info.appVersion || info.appBuildVersion || fallback;
}

export function buildAppUpdateInfoRows(info: AppUpdateInfo): AppUpdateInfoRow[] {
  return [
    buildValueRow("version", "updates.version", info.appVersion, "updates.unknown"),
    buildValueRow("build", "updates.buildVersion", info.appBuildVersion, "updates.noBuildVersion"),
    buildValueRow("runtime", "updates.runtime", info.runtimeVersion, "updates.unknown"),
    buildValueRow("channel", "updates.channel", info.channel, "updates.noChannel"),
    {
      key: "source",
      labelKey: "updates.source",
      valueKey: resolveAppUpdateSourceKey(info),
    },
    buildValueRow("updateId", "updates.updateId", info.updateId, "updates.noUpdateId"),
  ];
}
