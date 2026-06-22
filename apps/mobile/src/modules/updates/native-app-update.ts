export type NativeAppBuildInfo = {
  easBuildId: string;
  appVersion: string | null;
  appBuildVersion: string | null;
  artifactUrl: string;
  buildProfile: string | null;
};

export type NativeAppUpdateCheckResult =
  | { status: "unsupported-platform" }
  | { status: "not-available" }
  | { status: "downloaded"; build: NativeAppBuildInfo; fileUri: string };

export type LegacyNativeAppUpdateCheckResult =
  | { status: "unsupported-platform" }
  | { status: "not-available" }
  | { status: "available"; build: NativeAppBuildInfo; url: string };

export type NativeAppUpdatePlatform = "android" | "ios" | "web" | string;

export type NativeAppUpdateApiClient = {
  get: (
    url: string,
    config?: {
      params?: Record<string, unknown>;
      headers?: Record<string, string>;
    }
  ) => Promise<{ data: unknown }>;
};

export type NativeAppUpdateDependencies = {
  apiClient: NativeAppUpdateApiClient;
  downloadFile: (url: string, fileUri: string) => Promise<{
    uri: string;
    status?: number;
    mimeType?: string | null;
  }>;
  deleteFile?: (fileUri: string) => Promise<void>;
  getFileInfo: (fileUri: string) => Promise<{ exists: boolean; size?: number }>;
  getCurrentBuildVersion: () => string | null;
  getBuildProfile: () => string | null;
  getDownloadDirectory: () => string | null;
  platform: NativeAppUpdatePlatform;
};

type LegacyNativeAppUpdateDependencies = Pick<
  NativeAppUpdateDependencies,
  "apiClient" | "getCurrentBuildVersion" | "getBuildProfile" | "platform"
> & {
  getDownloadUrl?: (build: NativeAppBuildInfo) => string | null;
};

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asString(value: unknown): string | null {
  if (typeof value === "string" && value.trim()) {
    return value.trim();
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return null;
}

function toBuildNumber(value: string | null): number | null {
  if (!value) {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function normalizeLatestBuild(payload: unknown): NativeAppBuildInfo | null {
  const root = asRecord(payload);
  if (!root) {
    return null;
  }

  const artifactUrl = asString(root.artifactUrl);
  const easBuildId = asString(root.easBuildId);
  if (!artifactUrl || !easBuildId) {
    return null;
  }

  return {
    easBuildId,
    artifactUrl,
    appVersion: asString(root.appVersion),
    appBuildVersion: asString(root.appBuildVersion),
    buildProfile: asString(root.buildProfile),
  };
}

async function fetchLatestBuild(dependencies: {
  apiClient: NativeAppUpdateApiClient;
  getBuildProfile: () => string | null;
}) {
  const response = await dependencies.apiClient.get("/mobile-app-builds/android-latest", {
    params: { profile: dependencies.getBuildProfile() || "production" },
    headers: { "X-Skip-Center-Log": "1" },
  });
  return normalizeLatestBuild(response.data);
}

function isNewerBuild(build: NativeAppBuildInfo | null, currentBuild: number | null) {
  const latestBuild = toBuildNumber(build?.appBuildVersion ?? null);
  return Boolean(build && currentBuild != null && latestBuild != null && latestBuild > currentBuild);
}

function buildApkFileUri(directory: string, build: NativeAppBuildInfo) {
  const safeBuildId = build.easBuildId.replace(/[^a-zA-Z0-9._-]/g, "-");
  return `${directory.replace(/\/?$/, "/")}hb-${safeBuildId}.apk`;
}

function isUsableApkFile(info: { exists: boolean; size?: number }) {
  return info.exists && (info.size == null || info.size > 0);
}

function isRejectedApkMimeType(mimeType: string | null | undefined) {
  const normalized = mimeType?.split(";")[0]?.trim().toLowerCase();
  if (!normalized) {
    return false;
  }

  return (
    normalized.startsWith("text/") ||
    normalized === "application/json" ||
    normalized === "application/xml" ||
    normalized === "application/xhtml+xml"
  );
}

export async function checkAndDownloadNativeAppUpdate(
  dependencies: NativeAppUpdateDependencies
): Promise<NativeAppUpdateCheckResult> {
  if (dependencies.platform !== "android") {
    return { status: "unsupported-platform" };
  }

  const currentBuild = toBuildNumber(dependencies.getCurrentBuildVersion());
  const downloadDirectory = dependencies.getDownloadDirectory();
  if (currentBuild == null || !downloadDirectory) {
    return { status: "not-available" };
  }

  const build = await fetchLatestBuild(dependencies);
  if (!isNewerBuild(build, currentBuild)) {
    return { status: "not-available" };
  }

  const fileUri = buildApkFileUri(downloadDirectory, build!);
  const existing = await dependencies.getFileInfo(fileUri);
  if (!isUsableApkFile(existing)) {
    if (existing.exists) {
      await dependencies.deleteFile?.(fileUri);
    }

    // APK 检查默认静默下载；只有下载完成后才提示用户安装，避免先弹窗再等待大文件。
    const download = await dependencies.downloadFile(build!.artifactUrl, fileUri);
    if (download.status != null && (download.status < 200 || download.status >= 300)) {
      await dependencies.deleteFile?.(fileUri);
      throw new Error(`APK 下载失败，HTTP 状态码: ${download.status}`);
    }
    if (isRejectedApkMimeType(download.mimeType)) {
      await dependencies.deleteFile?.(fileUri);
      // 下载地址偶发返回 HTML/JSON 错误页时不能继续提示安装，否则用户会打开一个无效 APK。
      throw new Error(`APK 下载失败，文件类型异常: ${download.mimeType}`);
    }

    const downloaded = await dependencies.getFileInfo(fileUri);
    if (!isUsableApkFile(downloaded)) {
      await dependencies.deleteFile?.(fileUri);
      throw new Error("APK 下载失败，文件为空或不存在");
    }
  }

  return { status: "downloaded", build: build!, fileUri };
}

export async function checkLegacyNativeAppUpdate(
  dependencies: LegacyNativeAppUpdateDependencies
): Promise<LegacyNativeAppUpdateCheckResult> {
  if (dependencies.platform !== "android") {
    return { status: "unsupported-platform" };
  }

  const currentBuild = toBuildNumber(dependencies.getCurrentBuildVersion());
  const build = await fetchLatestBuild(dependencies);
  if (!isNewerBuild(build, currentBuild)) {
    return { status: "not-available" };
  }

  const stableDownloadUrl = dependencies.getDownloadUrl?.(build!);
  if (!stableDownloadUrl) {
    return { status: "not-available" };
  }

  // 旧 APK 的 OTA 只能打开浏览器下载，不能依赖新安装包才具备的原生安装器能力。
  return { status: "available", build: build!, url: stableDownloadUrl };
}
