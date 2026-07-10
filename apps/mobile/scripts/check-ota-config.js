const fs = require("fs");
const path = require("path");

const PROJECT_ROOT = path.resolve(__dirname, "..");
const APP_JSON_PATH = path.join(PROJECT_ROOT, "app.json");
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
  const expectedRuntimeVersion = appConfig.version;
  const expectedUpdateUrl = appConfig.updates?.url;
  const runtimeVersion = appConfig.runtimeVersion;

  const failures = [];

  if (typeof runtimeVersion !== "string") {
    failures.push("bare workflow 需要 app.json runtimeVersion 使用显式字符串，不能使用 policy 对象");
  } else if (runtimeVersion !== expectedRuntimeVersion) {
    failures.push(`app.json runtimeVersion=${runtimeVersion} 与 version=${expectedRuntimeVersion} 不一致`);
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

  if (androidRuntimeVersion !== expectedRuntimeVersion) {
    failures.push(
      `Android expo_runtime_version=${androidRuntimeVersion} 与 app.json version=${expectedRuntimeVersion} 不一致，请运行 npx expo prebuild --no-install`
    );
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
