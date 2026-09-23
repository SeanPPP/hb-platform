const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const PROJECT_ROOT = path.resolve(__dirname, "..");
const APP_JSON_PATH = path.join(PROJECT_ROOT, "app.json");
const EAS_JSON_PATH = path.join(PROJECT_ROOT, "eas.json");
const ANDROID_STRINGS_PATH = path.join(PROJECT_ROOT, "android/app/src/main/res/values/strings.xml");
const ANDROID_MANIFEST_PATH = path.join(PROJECT_ROOT, "android/app/src/main/AndroidManifest.xml");

function readText(filePath) {
  return fs.readFileSync(filePath, "utf8");
}

function extractXmlValue(source, pattern, label) {
  const match = source.match(pattern);
  if (!match) {
    throw new Error(`缺少 ${label}`);
  }

  return match[1];
}

function main() {
  const appConfig = JSON.parse(readText(APP_JSON_PATH)).expo;
  const easBuildProfiles = JSON.parse(readText(EAS_JSON_PATH)).build;
  const expectedIosRuntimeVersion = appConfig.runtimeVersion;
  const expectedUpdateUrl = appConfig.updates?.url;

  const failures = [];

  if (typeof expectedIosRuntimeVersion !== "string") {
    failures.push("bare workflow 需要 app.json runtimeVersion 使用显式字符串，不能使用 policy 对象");
  } else if (expectedIosRuntimeVersion !== appConfig.version) {
    failures.push(`app.json runtimeVersion=${expectedIosRuntimeVersion} 与 version=${appConfig.version} 不一致`);
  }

  if (!expectedUpdateUrl) {
    failures.push("app.json updates.url 不能为空");
  }

  const androidStrings = readText(ANDROID_STRINGS_PATH);
  const androidManifest = readText(ANDROID_MANIFEST_PATH);
  const androidRuntimeVersion = extractXmlValue(
    androidStrings,
    /<string name="expo_runtime_version">([^<]+)<\/string>/,
    "Android expo_runtime_version"
  );

  if (appConfig.android?.runtimeVersion !== androidRuntimeVersion) {
    failures.push(`app.json Android runtimeVersion=${appConfig.android?.runtimeVersion} 与原生 expo_runtime_version=${androidRuntimeVersion} 不一致`);
  }

  // 用 Expo 实际解析验证无 profile 的 prebuild 与显式 OTA runtime，避免仅比对静态 JSON。
  for (const explicitRuntime of [undefined, "1.0.6"]) {
    const env = { ...process.env };
    delete env.EXPO_PUBLIC_RUNTIME_VERSION;
    if (explicitRuntime) env.EXPO_PUBLIC_RUNTIME_VERSION = explicitRuntime;
    const resolved = JSON.parse(execFileSync(process.execPath, [
      path.join(PROJECT_ROOT, "node_modules/expo/bin/cli"), "config", "--type", "public", "--json",
    ], { cwd: PROJECT_ROOT, env, encoding: "utf8" }));
    const resolvedAndroidRuntime = resolved.android?.runtimeVersion ?? resolved.runtimeVersion;
    if (resolvedAndroidRuntime !== (explicitRuntime ?? androidRuntimeVersion)
        || resolved.runtimeVersion !== (explicitRuntime ?? expectedIosRuntimeVersion)) {
      failures.push(`Expo 实际运行时解析不一致：override=${explicitRuntime ?? "none"}`);
    }
  }

  for (const profileName of ["development", "preview", "production"]) {
    const profile = easBuildProfiles[profileName];
    // EAS 会把平台 env 合并到通用 env；Android 原生资源必须与合并后的运行时一致。
    const iosRuntimeVersion = profile?.ios?.env?.EXPO_PUBLIC_RUNTIME_VERSION
      ?? profile?.env?.EXPO_PUBLIC_RUNTIME_VERSION;
    const androidProfileRuntimeVersion = profile?.android?.env?.EXPO_PUBLIC_RUNTIME_VERSION
      ?? profile?.env?.EXPO_PUBLIC_RUNTIME_VERSION;

    if (iosRuntimeVersion !== expectedIosRuntimeVersion) {
      failures.push(`${profileName} iOS runtimeVersion=${iosRuntimeVersion} 与 app.json runtimeVersion=${expectedIosRuntimeVersion} 不一致`);
    }
    if (androidProfileRuntimeVersion !== androidRuntimeVersion) {
      failures.push(
        `${profileName} Android runtimeVersion=${androidProfileRuntimeVersion} 与原生 expo_runtime_version=${androidRuntimeVersion} 不一致`
      );
    }
  }

  if (!androidManifest.includes('android:name="expo.modules.updates.ENABLED" android:value="true"')) {
    failures.push("Android Manifest 未启用 expo.modules.updates.ENABLED=true");
  }

  if (!androidManifest.includes(`android:name="expo.modules.updates.EXPO_UPDATE_URL" android:value="${expectedUpdateUrl}"`)) {
    failures.push("Android Manifest 的 EXPO_UPDATE_URL 与 app.json updates.url 不一致，请运行 npx expo prebuild --no-install");
  }

  if (!androidManifest.includes('android:name="expo.modules.updates.EXPO_RUNTIME_VERSION"')) {
    failures.push("Android Manifest 缺少 EXPO_RUNTIME_VERSION");
  }

  if (!androidManifest.includes('android:name="expo.modules.updates.EXPO_UPDATES_CHECK_ON_LAUNCH" android:value="NEVER"')) {
    failures.push("Android Manifest 必须禁用 expo-updates 原生启动自动检查，由 JS 层按 profile 控制自动更新");
  }

  if (failures.length) {
    console.error("OTA config check failed:");
    failures.forEach((failure) => console.error(`- ${failure}`));
    process.exitCode = 1;
    return;
  }

  console.log("OTA config check passed.");
}

main();
