import { unwrapApiEnvelope } from "../../shared/api/api-envelope";
import type {
  AppBuild,
  AppDownloadAppKey,
  AppDownloadEnvironment,
  AppDownloadPlatform,
  AppUpdateApp,
  HandheldCandidate,
  HandheldPolicy,
  IpadOtaRelease,
  IpadOtaRollout,
  NativePolicy,
  NativeRelease,
  OtaPolicy,
  OtaRelease,
  Paged,
  Revision,
  StoreOption,
} from "./types";

type RecordValue = Record<string, unknown>;
const record = (value: unknown): RecordValue =>
  value && typeof value === "object" && !Array.isArray(value)
    ? (value as RecordValue)
    : {};
const str = (value: unknown) =>
  typeof value === "string" || typeof value === "number" ? String(value) : null;
const nullable = (value: unknown) => str(value)?.trim() || null;
const bool = (value: unknown) => value === true || value === "true";
const num = (value: unknown, fallback = 0) =>
  Number.isFinite(Number(value)) ? Number(value) : fallback;
const list = (value: unknown, key = "items") =>
  Array.isArray(value)
    ? value
    : Array.isArray(record(value)[key])
      ? (record(value)[key] as unknown[])
      : [];
const unwrap = (value: unknown) => unwrapApiEnvelope(value);

function page<T>(
  value: unknown,
  map: (v: unknown) => T,
  key = "items",
): Paged<T> {
  const payload = unwrap(value);
  const raw = record(payload);
  const items = Array.isArray(payload)
    ? payload
    : Array.isArray(raw[key])
      ? (raw[key] as unknown[])
      : Array.isArray(raw.list)
        ? raw.list
        : Array.isArray(raw.data)
          ? raw.data
          : [];
  return {
    items: items.map(map),
    total: num(raw.total ?? raw.totalCount, items.length),
    page: num(raw.page, 1),
    pageSize: num(raw.pageSize, 20),
  };
}
function build(value: unknown): AppBuild {
  const r = record(value);
  return {
    id: str(r.id) ?? "",
    appKey: r.appKey === "pos-handheld" ? "pos-handheld" : "mobile",
    appVersion: nullable(r.appVersion),
    appBuildVersion: nullable(r.appBuildVersion),
    platform: nullable(r.platform),
    status: nullable(r.status),
    buildProfile: nullable(r.buildProfile),
    channel: nullable(r.channel),
    runtimeVersion: nullable(r.runtimeVersion),
    artifactUrl: nullable(r.artifactUrl),
    cosArtifactUrl: nullable(r.cosArtifactUrl),
    cosObjectKey: nullable(r.cosObjectKey),
    originalArtifactUrl: nullable(r.originalArtifactUrl),
    cosMirrorStatus: nullable(r.cosMirrorStatus),
    cosMirrorError: nullable(r.cosMirrorError),
    cosMirroredAt: nullable(r.cosMirroredAt),
    gitCommitHash: nullable(r.gitCommitHash),
    buildDetailsPageUrl: nullable(r.buildDetailsPageUrl),
    createdAt: nullable(r.createdAt),
    completedAt: nullable(r.completedAt),
    expirationDate: nullable(r.expirationDate),
  };
}
function otaRelease(value: unknown): OtaRelease {
  const r = record(value);
  return {
    id: str(r.id) ?? "",
    appKey: r.appKey === "pos-handheld" ? "pos-handheld" : "mobile",
    environment: r.environment === "preview" ? "preview" : "production",
    platform: r.platform === "ios" ? "ios" : "android",
    clientChannel: str(r.clientChannel) ?? "",
    releaseChannel: str(r.releaseChannel) ?? "",
    runtimeVersion: str(r.runtimeVersion) ?? "",
    updateGroupId: str(r.updateGroupId) ?? "",
    updateId: str(r.updateId) ?? "",
    message: nullable(r.message),
    dashboardUrl: nullable(r.dashboardUrl),
    publishedAtUtc: str(r.publishedAtUtc) ?? "",
    isRollback: bool(r.isRollback),
  };
}
function otaPolicy(value: unknown): OtaPolicy {
  const r = record(value);
  return {
    id: nullable(r.id),
    environment: r.environment === "preview" ? "preview" : "production",
    platform: r.platform === "ios" ? "ios" : "android",
    enabled: bool(r.enabled),
    required: bool(r.required),
    policyVersion: num(r.policyVersion),
    targetReleaseId: nullable(r.targetReleaseId),
    targetRuntimeVersion: nullable(r.targetRuntimeVersion),
    releaseMessage: nullable(r.releaseMessage),
    targetRelease: r.targetRelease ? otaRelease(r.targetRelease) : null,
  };
}
function nativeRelease(value: unknown): NativeRelease {
  const r = record(value);
  return {
    id: str(r.id) ?? "",
    app:
      r.app === "pos-ipad"
        ? "pos-ipad"
        : r.app === "pos-handheld"
          ? "pos-handheld"
          : "mobile-ios",
    appStoreId: str(r.appStoreId) ?? "",
    bundleIdentifier: str(r.bundleIdentifier) ?? "",
    version: str(r.version) ?? "",
    buildNumber: str(r.buildNumber) ?? "",
    storefront: str(r.storefront) ?? "",
    appStoreUrl: str(r.appStoreUrl) ?? "",
    appleVerifiedAtUtc: str(r.appleVerifiedAtUtc) ?? "",
    createdAt: str(r.createdAt) ?? "",
  };
}
function nativePolicy(value: unknown): NativePolicy {
  const r = record(value);
  return {
    id: nullable(r.id),
    enabled: bool(r.enabled),
    policyVersion: num(r.policyVersion),
    releaseId: nullable(r.releaseId),
    latestVersion: nullable(r.latestVersion),
    minimumSupportedVersion: nullable(r.minimumSupportedVersion),
    minimumSupportedBuildNumber:
      r.minimumSupportedBuildNumber == null
        ? null
        : num(r.minimumSupportedBuildNumber),
    appStoreUrl: nullable(r.appStoreUrl),
    releaseMessage: nullable(r.releaseMessage),
    targetScope: r.targetScope === "stores" ? "stores" : "all",
    targetStoreGuids: Array.isArray(r.targetStoreGuids)
      ? r.targetStoreGuids.filter((v): v is string => typeof v === "string")
      : [],
    updatedAt: nullable(r.updatedAt),
    updatedBy: nullable(r.updatedBy),
  };
}
function storeOption(value: unknown): StoreOption {
  const r = record(value);
  return {
    storeGuid: str(r.storeGuid) ?? "",
    storeCode: str(r.storeCode) ?? "",
    storeName: str(r.storeName) ?? "",
  };
}
function ipadRelease(value: unknown): IpadOtaRelease {
  const r = record(value);
  return {
    id: str(r.id) ?? "",
    environment: str(r.environment) ?? "",
    updateGroupId: str(r.updateGroupId) ?? "",
    iosUpdateId: str(r.iosUpdateId) ?? "",
    channel: str(r.channel) ?? "",
    runtimeVersion: str(r.runtimeVersion) ?? "",
    dashboardUrl: nullable(r.dashboardUrl),
    publishedAtUtc: str(r.publishedAtUtc) ?? "",
    isRollback: bool(r.isRollback),
  };
}
function ipadRollout(value: unknown): IpadOtaRollout {
  const r = record(value);
  return {
    id: nullable(r.id),
    enabled: bool(r.enabled),
    policyVersion: num(r.policyVersion),
    releaseId: nullable(r.releaseId),
    forceUpdate: bool(r.forceUpdate),
    targetScope: r.targetScope === "stores" ? "stores" : "all",
    targetStoreGuids: Array.isArray(r.targetStoreGuids)
      ? r.targetStoreGuids.filter((v): v is string => typeof v === "string")
      : [],
    releaseMessage: nullable(r.releaseMessage),
    release: r.release ? ipadRelease(r.release) : null,
  };
}
function normalizeLane(
  value: unknown,
  platform: unknown = "android",
  kind: unknown = "native",
): HandheldCandidate["lane"] {
  const lane = String(value ?? "")
    .trim()
    .toLowerCase();
  if (["android-native", "ios-native", "android-ota", "ios-ota"].includes(lane))
    return lane as HandheldCandidate["lane"];
  return `${String(platform).toLowerCase() === "ios" ? "ios" : "android"}-${String(kind).toLowerCase() === "ota" ? "ota" : "native"}`;
}
function candidate(value: unknown): HandheldCandidate {
  const r = record(value);
  return {
    id: str(r.id) ?? "",
    lane: normalizeLane(r.lane, r.platform, r.kind),
    platform: r.platform === "ios" ? "ios" : "android",
    kind: r.kind === "ota" ? "ota" : "native",
    version: nullable(r.version),
    buildNumber: nullable(r.buildNumber),
    runtimeVersion: nullable(r.runtimeVersion),
    channel: nullable(r.channel ?? r.releaseChannel),
    updateId: nullable(r.updateId),
    updateGroupId: nullable(r.updateGroupId),
    message: nullable(r.message ?? r.releaseMessage),
    dashboardUrl: nullable(r.dashboardUrl),
    downloadUrl: nullable(r.downloadUrl ?? r.artifactUrl),
    appStoreUrl:
      nullable(r.appStoreUrl) ??
      (r.platform === "ios" && r.kind === "native"
        ? nullable(r.downloadUrl ?? r.artifactUrl)
        : null),
    createdAt: str(r.createdAt ?? r.publishedAtUtc) ?? "",
    activatable: bool(r.activatable ?? r.isActivatable),
    blockedReason: nullable(r.blockedReason),
  };
}
function handheldPolicy(value: unknown): HandheldPolicy {
  const r = record(value);
  return {
    id: nullable(r.id),
    lane: normalizeLane(r.lane),
    enabled: bool(r.enabled),
    required: bool(r.required),
    policyVersion: num(r.policyVersion),
    candidateId: nullable(r.candidateId),
    candidateValid: bool(r.candidateValid),
    blockedReason: nullable(r.blockedReason),
    candidate: r.candidate ? candidate(r.candidate) : null,
    minimumSupportedVersion: nullable(r.minimumSupportedVersion),
    minimumSupportedBuildNumber:
      r.minimumSupportedBuildNumber == null
        ? null
        : num(r.minimumSupportedBuildNumber),
    releaseMessage: nullable(r.releaseMessage),
  };
}
function revision(value: unknown): Revision {
  const r = record(value);
  return {
    lane: r.lane ? normalizeLane(r.lane) : undefined,
    id: str(r.id) ?? "",
    policyVersion: num(r.policyVersion),
    operation: str(r.operation ?? r.action) ?? "",
    snapshotJson: str(r.snapshotJson) ?? JSON.stringify(r.snapshot ?? {}),
    createdAt: str(r.createdAt) ?? "",
    createdBy: nullable(r.createdBy),
  };
}

export interface AppDownloadsTransport {
  get(
    url: string,
    config?: { params?: Record<string, unknown> },
  ): Promise<{ data: unknown }>;
  post(url: string, payload: unknown): Promise<{ data: unknown }>;
  put(url: string, payload: unknown): Promise<{ data: unknown }>;
}

export function createAppDownloadsApi(apiClient: AppDownloadsTransport) {
  return {
    async getBuilds(
      appKey: AppDownloadAppKey,
      profile: AppDownloadEnvironment,
      pageNumber = 1,
    ) {
      const response = await apiClient.get("/mobile-app-builds", {
        params: { page: pageNumber, pageSize: 20, profile, appKey },
      });
      return page(response.data, build);
    },
    async getLatestBuild(
      appKey: AppDownloadAppKey,
      profile: AppDownloadEnvironment,
    ) {
      const response = await apiClient.get("/mobile-app-builds/latest", {
        params: { profile, appKey },
      });
      const value = unwrap(response.data);
      return value ? build(value) : null;
    },
    async getOtaBuildHistory(
      appKey: AppDownloadAppKey,
      profile: AppDownloadEnvironment,
      runtimeVersion?: string,
      pageNumber = 1,
    ) {
      const response = await apiClient.get("/mobile-app-builds/ota-updates", {
        params: {
          page: pageNumber,
          pageSize: 20,
          channel:
            appKey === "pos-handheld" ? `pos-handheld-${profile}` : profile,
          appKey,
          runtimeVersion: runtimeVersion?.trim() || undefined,
        },
      });
      return page(response.data, (v) => {
        const r = record(v);
        return {
          id: str(r.id) ?? "",
          appKey,
          environment: profile,
          platform: r.platform === "ios" ? "ios" : "android",
          clientChannel: str(r.channel) ?? "",
          releaseChannel: str(r.channel) ?? "",
          runtimeVersion: str(r.runtimeVersion) ?? "",
          updateGroupId: str(r.updateGroupId) ?? "",
          updateId: str(r.updateId) ?? "",
          message: nullable(r.message),
          dashboardUrl: nullable(r.dashboardUrl),
          publishedAtUtc: str(r.publishedAt) ?? "",
          isRollback: bool(r.isRollback),
        } as OtaRelease;
      });
    },
    async getIosReleases(app: AppUpdateApp) {
      const response = await apiClient.get("/app-update-releases/ios", {
        params: { app, storefront: "au" },
      });
      return list(unwrap(response.data)).map(nativeRelease);
    },
    async registerIosRelease(payload: {
      app: AppUpdateApp;
      appStoreId: string;
      buildNumber: string;
      storefront: string;
    }) {
      const response = await apiClient.post(
        "/app-update-releases/ios",
        payload,
      );
      return nativeRelease(unwrap(response.data));
    },
    async getNativePolicy(app: "mobile-ios" | "pos-ipad") {
      const response = await apiClient.get(
        app === "mobile-ios"
          ? "/app-update-policies/mobile-ios"
          : "/app-update-policies/pos-ipad/native",
      );
      return nativePolicy(unwrap(response.data));
    },
    async saveNativePolicy(
      app: "mobile-ios" | "pos-ipad",
      payload: Record<string, unknown>,
    ) {
      const response = await apiClient.put(
        app === "mobile-ios"
          ? "/app-update-policies/mobile-ios"
          : "/app-update-policies/pos-ipad/native",
        payload,
      );
      return nativePolicy(unwrap(response.data));
    },
    async getStoreOptions() {
      const response = await apiClient.get(
        "/app-update-policies/pos-ipad/store-options",
      );
      return list(unwrap(response.data)).map(storeOption);
    },
    async getMobileOtaReleases(
      environment: AppDownloadEnvironment,
      platform: AppDownloadPlatform,
    ) {
      const response = await apiClient.get("/app-ota-releases", {
        params: { appKey: "mobile", environment, platform },
      });
      return list(unwrap(response.data))
        .filter((value) => {
          const raw = record(value);
          return (
            String(raw.appKey ?? "")
              .trim()
              .toLowerCase() === "mobile" &&
            String(raw.environment ?? "")
              .trim()
              .toLowerCase() === environment &&
            String(raw.platform ?? "")
              .trim()
              .toLowerCase() === platform &&
            String(raw.clientChannel ?? "")
              .trim()
              .toLowerCase() === environment
          );
        })
        .map(otaRelease);
    },
    async getMobileOtaPolicy(
      environment: AppDownloadEnvironment,
      platform: AppDownloadPlatform,
    ) {
      const response = await apiClient.get(
        `/app-update-policies/mobile-ota/${environment}/${platform}`,
      );
      return otaPolicy(unwrap(response.data));
    },
    async saveMobileOtaPolicy(
      environment: AppDownloadEnvironment,
      platform: AppDownloadPlatform,
      payload: Record<string, unknown>,
    ) {
      const response = await apiClient.put(
        `/app-update-policies/mobile-ota/${environment}/${platform}`,
        payload,
      );
      return otaPolicy(unwrap(response.data));
    },
    async getMobileOtaRevisions(
      environment: AppDownloadEnvironment,
      platform: AppDownloadPlatform,
    ) {
      const response = await apiClient.get(
        `/app-update-policies/mobile-ota/${environment}/${platform}/revisions`,
      );
      return list(unwrap(response.data)).map(revision);
    },
    async getIpadOtaReleases() {
      const response = await apiClient.get("/pos-ipad/ota-releases");
      return list(unwrap(response.data)).map(ipadRelease);
    },
    async getIpadOtaRollout() {
      const response = await apiClient.get("/pos-ipad/ota-rollout");
      return ipadRollout(unwrap(response.data));
    },
    async saveIpadOtaRollout(payload: Record<string, unknown>) {
      const response = await apiClient.put("/pos-ipad/ota-rollout", payload);
      return ipadRollout(unwrap(response.data));
    },
    async getHandheldPolicies() {
      const response = await apiClient.get("/app-update-policies/pos-handheld");
      return list(unwrap(response.data), "policies").map(handheldPolicy);
    },
    async getHandheldCandidates(
      platform: AppDownloadPlatform,
      kind: "native" | "ota",
    ) {
      const path =
        kind === "native"
          ? `/app-update-policies/pos-handheld/candidates/native/${platform}`
          : "/app-update-policies/pos-handheld/candidates/ota";
      const response = await apiClient.get(
        path,
        kind === "ota" ? { params: { platform } } : undefined,
      );
      return list(unwrap(response.data))
        .map(candidate)
        .filter(
          (item) =>
            item.platform === platform &&
            item.kind === kind &&
            item.lane === `${platform}-${kind}`,
        );
    },
    async saveHandheldPolicy(lane: string, payload: Record<string, unknown>) {
      const response = await apiClient.put(
        `/app-update-policies/pos-handheld/${lane}`,
        payload,
      );
      return handheldPolicy(unwrap(response.data));
    },
    async getHandheldRevisions(lane: string) {
      const response = await apiClient.get(
        "/app-update-policies/pos-handheld/revisions",
        { params: { lane } },
      );
      return list(unwrap(response.data)).map(revision);
    },
  };
}

export {
  build,
  candidate,
  handheldPolicy,
  ipadRelease,
  ipadRollout,
  nativePolicy,
  nativeRelease,
  otaPolicy,
  otaRelease,
  revision,
};
