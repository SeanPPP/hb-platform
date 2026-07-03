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

type NativeAppFileInfo = {
  exists: boolean;
  size?: number;
  isDirectory?: boolean;
  modificationTime?: number;
};

export type NativeAppUpdateDependencies = {
  apiClient: NativeAppUpdateApiClient;
  downloadFile: (url: string, fileUri: string) => Promise<{
    uri: string;
    status?: number;
    mimeType?: string | null;
  }>;
  deleteFile?: (fileUri: string) => Promise<void>;
  getFileInfo: (fileUri: string) => Promise<NativeAppFileInfo>;
  getCurrentBuildVersion: () => string | null;
  getBuildProfile: () => string | null;
  getDownloadDirectory: () => string | null;
  getDownloadUrl?: (build: NativeAppBuildInfo) => string | null;
  readDirectory?: (directory: string) => Promise<string[]>;
  platform: NativeAppUpdatePlatform;
};

const MAX_CACHED_APK_FILES = 3;
const APP_APK_FILE_NAME_PATTERN = /^hb-[^/]+\.apk$/i;

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

export function getStableNativeAppDownloadUrl(baseURL: string | undefined, profile: string) {
  if (!baseURL?.trim()) {
    return null;
  }

  try {
    const base = baseURL.endsWith("/") ? baseURL : `${baseURL}/`;
    const query = new URLSearchParams({ profile });
    // 后端稳定入口会在点击/下载时重新解析最新 APK，避免客户端持有过期 EAS artifact。
    return new URL(`mobile-app-builds/android-latest/download?${query.toString()}`, base).toString();
  } catch {
    return null;
  }
}

export function getBuildBoundNativeAppDownloadUrl(
  baseURL: string | undefined,
  build: NativeAppBuildInfo,
  fallbackProfile = "production"
) {
  if (!baseURL?.trim() || !build.easBuildId.trim()) {
    return null;
  }

  try {
    const base = baseURL.endsWith("/") ? baseURL : `${baseURL}/`;
    const profile = build.buildProfile?.trim() || fallbackProfile;
    const query = new URLSearchParams({ profile });
    // 新安装器必须绑定已判定的新 build，避免下载过程中 latest 指向另一个 APK。
    return new URL(
      `mobile-app-builds/android/${encodeURIComponent(build.easBuildId)}/download?${query.toString()}`,
      base
    ).toString();
  } catch {
    return null;
  }
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

  // 后端匿名接口返回 ApiResponse 包装；直接 DTO 和包装 DTO 都要兼容旧包自更新。
  const buildRoot = asRecord(root.data) ?? root;
  const artifactUrl = asString(buildRoot.artifactUrl);
  const easBuildId = asString(buildRoot.easBuildId);
  if (!artifactUrl || !easBuildId) {
    return null;
  }

  return {
    easBuildId,
    artifactUrl,
    appVersion: asString(buildRoot.appVersion),
    appBuildVersion: asString(buildRoot.appBuildVersion),
    buildProfile: asString(buildRoot.buildProfile),
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
  return buildFileUri(directory, `hb-${safeBuildId}.apk`);
}

function buildFileUri(directory: string, fileName: string) {
  return `${directory.replace(/\/?$/, "/")}${fileName}`;
}

function isUsableApkFile(info: NativeAppFileInfo) {
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

async function cleanupDownloadedApkFiles(
  dependencies: NativeAppUpdateDependencies,
  downloadDirectory: string,
  protectedFileUri?: string
) {
  if (!dependencies.readDirectory || !dependencies.deleteFile) {
    return;
  }

  let fileNames: string[];
  try {
    fileNames = await dependencies.readDirectory(downloadDirectory);
  } catch {
    return;
  }

  const apkFiles: Array<{ fileName: string; fileUri: string; modificationTime: number }> = [];
  for (const fileName of fileNames) {
    if (!APP_APK_FILE_NAME_PATTERN.test(fileName)) {
      continue;
    }

    const fileUri = buildFileUri(downloadDirectory, fileName);
    try {
      const info = await dependencies.getFileInfo(fileUri);
      if (info.exists && !info.isDirectory) {
        apkFiles.push({ fileName, fileUri, modificationTime: info.modificationTime ?? 0 });
      }
    } catch {
      // 缓存清理不应影响安装提示；单个文件异常时跳过即可。
    }
  }

  apkFiles.sort((left, right) => {
    if (right.modificationTime !== left.modificationTime) {
      return right.modificationTime - left.modificationTime;
    }
    return right.fileName.localeCompare(left.fileName);
  });

  const keepFileUris = new Set<string>();
  if (protectedFileUri) {
    // 当前准备安装的 APK 必须保留，即使它不是目录里 modificationTime 最新的文件。
    keepFileUris.add(protectedFileUri);
  }

  for (const apkFile of apkFiles) {
    if (keepFileUris.size >= MAX_CACHED_APK_FILES) {
      break;
    }
    keepFileUris.add(apkFile.fileUri);
  }

  for (const apkFile of apkFiles) {
    if (keepFileUris.has(apkFile.fileUri)) {
      continue;
    }
    try {
      await dependencies.deleteFile(apkFile.fileUri);
    } catch {
      // 删除失败留给下次检查重试，不能阻断本次更新流程。
    }
  }
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
    await cleanupDownloadedApkFiles(dependencies, downloadDirectory);
    return { status: "not-available" };
  }

  const fileUri = buildApkFileUri(downloadDirectory, build!);
  const existing = await dependencies.getFileInfo(fileUri);
  if (!isUsableApkFile(existing)) {
    if (existing.exists) {
      await dependencies.deleteFile?.(fileUri);
    }

    // APK 检查默认静默下载；优先走后端稳定入口，避免 EAS artifact 临时链接过期后出现“文件不存在”。
    const downloadUrl = dependencies.getDownloadUrl?.(build!) || build!.artifactUrl;
    const download = await dependencies.downloadFile(downloadUrl, fileUri);
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

  await cleanupDownloadedApkFiles(dependencies, downloadDirectory, fileUri);

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
