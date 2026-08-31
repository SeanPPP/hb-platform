/** Native Android APK downloader's integrity-bound input. */
export type DownloadApkRequest = Readonly<{
  url: string;
  destinationFileUri: string;
  expectedSizeBytes: number;
  expectedSha256Hex: string;
  trustedOrigins: readonly string[];
}>;

export type DownloadedApkResult = Readonly<{
  fileUri: string;
  sizeBytes: number;
  sha256Hex: string;
  finalUrl: string;
}>;

export type InstallPermissionStatus = "granted" | "denied";

export type InstallVerifiedApkRequest = Readonly<{
  fileUri: string;
  expectedSizeBytes: number;
  expectedSha256Hex: string;
  expectedPackageName: string;
  expectedVersionCode: number;
  expectedVersionName: string;
}>;

export type InstallVerifiedApkResult = Readonly<{
  launched: true;
  packageName: string;
  versionCode: number;
}>;
