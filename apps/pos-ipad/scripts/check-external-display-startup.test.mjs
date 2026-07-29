import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("应用唯一根入口注册非交互外接屏第二 React surface", async () => {
  const source = await readFile(
    new URL("../app/_layout.tsx", import.meta.url),
    "utf8",
  );

  assert.match(
    source,
    /import\s+["']@\/core\/peripherals\/customer-display\/native["'];/u,
  );
});

test("Expo 生产 runtime 只向业务层暴露冻结的外接客显 Port", async () => {
  const source = await readFile(
    new URL("../src/core/runtime/expo-pos-runtime.ts", import.meta.url),
    "utf8",
  );

  assert.match(
    source,
    /import\s+\{[^}]*\bexternalDisplay\b[^}]*\}\s+from\s+["']\.\.\/peripherals\/customer-display\/native["'];/u,
  );
  assert.match(source, /\bexternalDisplay,\s*\n/u);
});

test("生成的 AppDelegate 在 Expo subscribers 运行前准备唯一主窗口和 main root", async () => {
  const source = await readFile(
    new URL("../ios/HBPOS/AppDelegate.swift", import.meta.url),
    "utf8",
  );
  const windowIndex = source.indexOf(
    "window = UIWindow(frame: UIScreen.main.bounds)",
  );
  const startIndex = source.indexOf("factory.startReactNative(");
  const superIndex = source.indexOf(
    "return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
  );

  assert.notEqual(windowIndex, -1, "AppDelegate 必须在启动时创建主窗口");
  assert.notEqual(startIndex, -1, "AppDelegate 必须启动唯一 main root");
  assert.notEqual(superIndex, -1, "必须保留 Expo AppDelegate subscriber 调用");
  assert.ok(windowIndex < startIndex, "主窗口必须先于 main root 创建");
  assert.ok(
    startIndex < superIndex,
    "Dev Launcher subscriber 运行前必须完成 autoSetupPrepare",
  );
});

test("主 Scene 接管同一个 AppDelegate 窗口且不启动第二个 main root", async () => {
  const source = await readFile(
    new URL(
      "../modules/hb-external-display/ios/HBPrimarySceneDelegate.swift",
      import.meta.url,
    ),
    "utf8",
  );

  assert.match(source, /appDelegate\.window/u);
  assert.match(source, /\.windowScene\s*=\s*windowScene/u);
  assert.match(source, /window\s*=\s*(?:primaryWindow|appWindow)/u);
  assert.match(
    source,
    /(?:primaryWindow|appWindow)\.makeKeyAndVisible\(\)/u,
  );
  assert.doesNotMatch(source, /UIWindow\(windowScene:\s*windowScene\)/u);
  assert.doesNotMatch(source, /factory\.startReactNative\(/u);
  assert.doesNotMatch(source, /didStartReactNative/u);
  assert.doesNotMatch(source, /asyncAfter/u);
});

test("外接客显 Scene 保持非交互角色且不接触主窗口或 main root", async () => {
  const [source, infoPlist] = await Promise.all([
    readFile(
      new URL(
        "../modules/hb-external-display/ios/HBExternalDisplaySceneDelegate.swift",
        import.meta.url,
      ),
      "utf8",
    ),
    readFile(new URL("../ios/HBPOS/Info.plist", import.meta.url), "utf8"),
  ]);

  assert.match(
    source,
    /session\.role\s*==\s*\.windowExternalDisplayNonInteractive/u,
  );
  assert.doesNotMatch(source, /AppDelegate\.window|appDelegate\.window/u);
  assert.doesNotMatch(source, /factory\.startReactNative\(/u);
  assert.match(
    infoPlist,
    /UIWindowSceneSessionRoleExternalDisplayNonInteractive/u,
  );
  assert.match(infoPlist, /HBExternalDisplaySceneDelegate/u);
});
