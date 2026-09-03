import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../", import.meta.url);
const read = (path) => readFile(new URL(path, root), "utf8");
const [bridge, types, moduleSource, securitySource, downloader, manifest, paths] = await Promise.all([
  read("src/HBAppInstallerModule.ts"),
  read("src/HBAppInstaller.types.ts"),
  read("android/src/main/java/expo/modules/hbappinstaller/HBAppInstallerModule.kt"),
  read("android/src/main/java/expo/modules/hbappinstaller/HBAppInstallerSecurity.kt"),
  read("android/src/main/java/expo/modules/hbappinstaller/HBAppInstallerDownloader.kt"),
  read("android/src/main/AndroidManifest.xml"),
  read("android/src/main/res/xml/hb_app_installer_paths.xml"),
]);

test("bridge is optional for Runtime 1.0.3 build 16 compatibility", () => {
  assert.match(bridge, /requireOptionalNativeModule/);
  assert.match(bridge, /"HBAppInstaller"/);
  assert.match(types, /expectedSizeBytes/);
  assert.match(types, /expectedSha256Hex/);
});

test("download verifies bytes before publishing an APK", () => {
  assert.match(downloader, /\.part/);
  assert.match(downloader, /output\.fd\.sync\(\)/);
  assert.match(downloader, /APP_DOWNLOAD_SHA256_MISMATCH/);
  assert.match(downloader, /renameTo\(destination\)/);
  assert.match(downloader, /instanceFollowRedirects = false/);
  assert.match(downloader, /MAXIMUM_REDIRECTS = 5/);
  assert.match(downloader, /APK_DOWNLOAD_MAX_SIZE_BYTES/);
});

test("verify is prompt-safe and install independently re-verifies identity", () => {
  const installRequest = types.match(/export type InstallVerifiedApkRequest[\s\S]*?\n\}>;/)?.[0] ?? "";
  for (const field of [
    "fileUri",
    "expectedSizeBytes",
    "expectedSha256Hex",
    "expectedPackageName",
    "expectedVersionCode",
    "expectedVersionName",
  ]) assert.match(installRequest, new RegExp(`\\b${field}\\b`));
  assert.match(moduleSource, /AsyncFunction\("verifyApk"\)/);
  assert.match(moduleSource, /AsyncFunction\("installVerifiedApk"\)/);
  assert.match(moduleSource, /installCoordinator\(context\)\.verifyApk\(target, metadata\)/);
  assert.match(moduleSource, /installCoordinator\(context\)\.installVerifiedApk\(target, metadata\)/);
  assert.match(moduleSource, /APP_INSTALL_SHA256_MISMATCH/);
  assert.match(moduleSource, /APP_INSTALL_PACKAGE_MISMATCH/);
  assert.match(moduleSource, /APP_INSTALL_VERSION_NOT_NEWER/);
  assert.match(moduleSource, /HBAppInstallerSignerPolicy\.validate/);
  assert.doesNotMatch(types, /expectedSigningCertificateSha256/);
});

test("FileProvider is private and limited to the update directory", () => {
  assert.match(manifest, /android:exported="false"/);
  assert.match(manifest, /android:grantUriPermissions="true"/);
  assert.match(paths, /hb-app-updates\//);
  assert.doesNotMatch(paths, /external-path/);
  assert.match(moduleSource, /isManagedApkPath/);
  assert.match(securitySource, /MANAGED_APK_FILE_NAME/);
});

test("system settings and package installer launch without package visibility preflight", () => {
  assert.doesNotMatch(moduleSource, /resolveActivity/);
  assert.match(moduleSource, /launchSystemActivity/);
  assert.match(moduleSource, /ACTION_MANAGE_UNKNOWN_APP_SOURCES/);
  assert.match(moduleSource, /ACTION_SECURITY_SETTINGS/);
});

test("API 26 package-install permission is isolated behind the runtime policy", () => {
  assert.match(moduleSource, /if \(Build\.VERSION\.SDK_INT >= Build\.VERSION_CODES\.O\)/);
  assert.match(moduleSource, /context\.packageManager\.canRequestPackageInstalls\(\)/);
  assert.match(securitySource, /requiresAppSpecificPermission/);
});

test("API 28 long version code is behind a direct runtime guard", () => {
  assert.match(moduleSource, /if \(Build\.VERSION\.SDK_INT >= Build\.VERSION_CODES\.P\)/);
  assert.match(moduleSource, /longVersionCode/);
  assert.match(moduleSource, /resolveLegacyPackageVersionCode/);
});
