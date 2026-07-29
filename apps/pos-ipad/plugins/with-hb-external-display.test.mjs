import assert from "node:assert/strict";
import test from "node:test";

import plugin from "./with-hb-external-display.js";

const {
  EXTERNAL_DISPLAY_CONFIGURATION_NAME,
  EXTERNAL_DISPLAY_DELEGATE_CLASS,
  EXTERNAL_DISPLAY_ROLE,
  PRIMARY_DISPLAY_CONFIGURATION_NAME,
  PRIMARY_DISPLAY_DELEGATE_CLASS,
  PRIMARY_DISPLAY_ROLE,
  applyExternalDisplayAppDelegate,
  applyExternalDisplayInfoPlist,
} = plugin;

const expoSdk54AppDelegate = `import Expo
import React
import ReactAppDependencyProvider

@UIApplicationMain
public class AppDelegate: ExpoAppDelegate {
  var window: UIWindow?

  var reactNativeDelegate: ExpoReactNativeFactoryDelegate?
  var reactNativeFactory: RCTReactNativeFactory?

  public override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
  ) -> Bool {
    let delegate = ReactNativeDelegate()
    let factory = ExpoReactNativeFactory(delegate: delegate)
    delegate.dependencyProvider = RCTAppDependencyProvider()

    reactNativeDelegate = delegate
    reactNativeFactory = factory
    bindReactNativeFactory(factory)

#if os(iOS) || os(tvOS)
    window = UIWindow(frame: UIScreen.main.bounds)
    factory.startReactNative(
      withModuleName: "main",
      in: window,
      launchOptions: launchOptions)
#endif

    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }
}
`;

const expoSdk54MainRootStartup = `#if os(iOS) || os(tvOS)
    window = UIWindow(frame: UIScreen.main.bounds)
    factory.startReactNative(
      withModuleName: "main",
      in: window,
      launchOptions: launchOptions)
#endif
`;

const v2AppDelegate = expoSdk54AppDelegate
  .replace(
    "import ReactAppDependencyProvider\n",
    "import ReactAppDependencyProvider\nimport HBExternalDisplay\n",
  )
  .replace(
    "public class AppDelegate: ExpoAppDelegate {",
    "public class AppDelegate: ExpoAppDelegate, HBPrimaryWindowAppDelegate {",
  )
  .replace("  var window: UIWindow?", "  public var window: UIWindow?")
  .replace(
    expoSdk54MainRootStartup,
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n",
  );

test("config plugin adds a launchable primary scene when Expo has none", () => {
  const result = applyExternalDisplayInfoPlist({});
  const configurations =
    result.UIApplicationSceneManifest.UISceneConfigurations;

  assert.deepEqual(configurations[PRIMARY_DISPLAY_ROLE], [
    {
      UISceneConfigurationName: PRIMARY_DISPLAY_CONFIGURATION_NAME,
      UISceneClassName: "UIWindowScene",
      UISceneDelegateClassName: PRIMARY_DISPLAY_DELEGATE_CLASS,
    },
  ]);
  assert.deepEqual(configurations[EXTERNAL_DISPLAY_ROLE], [
    {
      UISceneConfigurationName: EXTERNAL_DISPLAY_CONFIGURATION_NAME,
      UISceneClassName: "UIWindowScene",
      UISceneDelegateClassName: EXTERNAL_DISPLAY_DELEGATE_CLASS,
    },
  ]);
});

test("config plugin adds the noninteractive external-display scene and preserves main scenes", () => {
  const mainScene = {
    UISceneConfigurationName: "Default Configuration",
    UISceneDelegateClassName: "$(PRODUCT_MODULE_NAME).SceneDelegate",
  };
  const infoPlist = {
    UIApplicationSceneManifest: {
      UIApplicationSupportsMultipleScenes: false,
      UISceneConfigurations: {
        UIWindowSceneSessionRoleApplication: [mainScene],
      },
    },
  };

  const result = applyExternalDisplayInfoPlist(infoPlist);
  const manifest = result.UIApplicationSceneManifest;
  const configurations = manifest.UISceneConfigurations;

  assert.equal(manifest.UIApplicationSupportsMultipleScenes, true);
  assert.deepEqual(
    configurations[PRIMARY_DISPLAY_ROLE],
    [
      {
        UISceneConfigurationName: PRIMARY_DISPLAY_CONFIGURATION_NAME,
        UISceneClassName: "UIWindowScene",
        UISceneDelegateClassName: PRIMARY_DISPLAY_DELEGATE_CLASS,
      },
      mainScene,
    ],
  );
  assert.deepEqual(configurations[EXTERNAL_DISPLAY_ROLE], [
    {
      UISceneConfigurationName: EXTERNAL_DISPLAY_CONFIGURATION_NAME,
      UISceneClassName: "UIWindowScene",
      UISceneDelegateClassName: EXTERNAL_DISPLAY_DELEGATE_CLASS,
    },
  ]);
  assert.equal(
    Object.hasOwn(
      configurations,
      "UIWindowSceneSessionRoleExternalDisplay",
    ),
    false,
  );
});

test("config plugin is idempotent and keeps its scene first", () => {
  const once = applyExternalDisplayInfoPlist({});
  const twice = applyExternalDisplayInfoPlist(once);

  assert.deepEqual(twice, once);
  assert.equal(
    twice.UIApplicationSceneManifest.UISceneConfigurations[
      EXTERNAL_DISPLAY_ROLE
    ][0].UISceneConfigurationName,
    EXTERNAL_DISPLAY_CONFIGURATION_NAME,
  );
});

test("AppDelegate transform preserves the Expo main-root startup for same-window handoff", () => {
  const result = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);

  assert.match(
    result,
    /let factory = ExpoReactNativeFactory\(delegate: delegate\)/,
  );
  assert.match(result, /bindReactNativeFactory\(factory\)/);
  assert.match(result, /import HBExternalDisplay/);
  assert.match(
    result,
    /public class AppDelegate: ExpoAppDelegate, HBPrimaryWindowAppDelegate/,
  );
  assert.match(result, /public var window: UIWindow\?/);
  assert.match(
    result,
    /HBPrimarySceneDelegate takes over the AppDelegate main React Native window\. \[v3\]/,
  );
  assert.match(
    result,
    /window = UIWindow\(frame: UIScreen\.main\.bounds\)/,
  );
  assert.match(result, /factory\.startReactNative\(/);
  assert.ok(
    result.indexOf("window = UIWindow(frame: UIScreen.main.bounds)") <
      result.indexOf("return super.application"),
  );
  assert.ok(
    result.indexOf("factory.startReactNative(") <
      result.indexOf("return super.application"),
  );
});

test("AppDelegate transform is idempotent", () => {
  const once = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const twice = applyExternalDisplayAppDelegate(once);

  assert.equal(twice, once);
});

test("AppDelegate transform upgrades a complete v2 transform to v3 and restores the Expo startup", () => {
  const result = applyExternalDisplayAppDelegate(v2AppDelegate);

  assert.match(
    result,
    /HBPrimarySceneDelegate takes over the AppDelegate main React Native window\. \[v3\]/,
  );
  assert.doesNotMatch(result, /HBPrimarySceneDelegate owns the main React Native root\. \[v2\]/);
  assert.match(
    result,
    /window = UIWindow\(frame: UIScreen\.main\.bounds\)/,
  );
  assert.match(result, /factory\.startReactNative\(/);
  assert.equal(applyExternalDisplayAppDelegate(result), result);
});

test("AppDelegate transform rejects a v2 marker when its startup handoff anchor drifted", () => {
  const drifted = v2AppDelegate.replace(
    "bindReactNativeFactory(factory)",
    "bindLegacyFactory(factory)",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(drifted),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform rejects a v2 marker outside the pre-super startup position", () => {
  const misplaced = v2AppDelegate.replace(
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)\n\n    // HBPrimarySceneDelegate owns the main React Native root. [v2]",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(misplaced),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform rejects a partial prior transform", () => {
  const corrupted = applyExternalDisplayAppDelegate(
    expoSdk54AppDelegate,
  ).replace("import HBExternalDisplay\n", "");

  assert.throws(
    () => applyExternalDisplayAppDelegate(corrupted),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v3 marker outside the main-root handoff position", () => {
  const transformed = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const misplaced = transformed
    .replace(
      "    // HBPrimarySceneDelegate takes over the AppDelegate main React Native window. [v3]\n",
      "",
    )
    .replace(
      "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
      "    // HBPrimarySceneDelegate takes over the AppDelegate main React Native window. [v3]\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    );

  assert.throws(
    () => applyExternalDisplayAppDelegate(misplaced),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v3 body with a duplicate main UIWindow", () => {
  const transformed = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const duplicated = transformed.replace(
    "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    "    window=UIWindow(frame:UIScreen.main.bounds)\n\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v3 body with a single-line duplicate start", () => {
  const transformed = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const duplicated = transformed.replace(
    "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    '    factory.startReactNative(withModuleName: "main", in: window, launchOptions: launchOptions)\n\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)',
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v3 body with a duplicate no-argument UIWindow", () => {
  const transformed = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const duplicated = transformed.replace(
    "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    "    window = UIWindow()\n\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v3 body with an optional-chained duplicate start", () => {
  const transformed = applyExternalDisplayAppDelegate(expoSdk54AppDelegate);
  const duplicated = transformed.replace(
    "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)",
    '    reactNativeFactory?.startReactNative(withModuleName: "shadow", in: secondaryWindow, launchOptions: nil)\n\n    return super.application(application, didFinishLaunchingWithOptions: launchOptions)',
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate window-handoff transform marker is incomplete/,
  );
});

test("AppDelegate transform rejects a v2 body with an extra formatted main UIWindow", () => {
  const duplicated = v2AppDelegate.replace(
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]",
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n    window=UIWindow(frame:UIScreen.main.bounds)",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform rejects a v2 body with an extra variant start", () => {
  const duplicated = v2AppDelegate.replace(
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]",
    '    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n    factory.startReactNative(withModuleName: "shadow", in: secondaryWindow, launchOptions: nil)',
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform rejects a v2 body with an extra no-argument UIWindow", () => {
  const duplicated = v2AppDelegate.replace(
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]",
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n    window = UIWindow()",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform rejects a v2 body with an extra optional-chained start", () => {
  const duplicated = v2AppDelegate.replace(
    "    // HBPrimarySceneDelegate owns the main React Native root. [v2]",
    '    // HBPrimarySceneDelegate owns the main React Native root. [v2]\n    reactNativeFactory?.startReactNative(withModuleName: "shadow", in: secondaryWindow, launchOptions: nil)',
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(duplicated),
    /AppDelegate v2 scene transform is incomplete/,
  );
});

test("AppDelegate transform fails fast when the Expo SDK 54 startup anchor drifts", () => {
  const unexpectedTemplate = expoSdk54AppDelegate.replace(
    "window = UIWindow(frame: UIScreen.main.bounds)",
    "window = makeLegacyWindow()",
  );

  assert.throws(
    () => applyExternalDisplayAppDelegate(unexpectedTemplate),
    /Expo SDK 54 AppDelegate main-root startup block was not found/,
  );
});
