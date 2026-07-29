const {
  createRunOncePlugin,
  withAppDelegate,
  withInfoPlist,
} = require("@expo/config-plugins");

const EXTERNAL_DISPLAY_ROLE =
  "UIWindowSceneSessionRoleExternalDisplayNonInteractive";
const EXTERNAL_DISPLAY_CONFIGURATION_NAME = "HB External Customer Display";
const EXTERNAL_DISPLAY_DELEGATE_CLASS =
  "HBExternalDisplay.HBExternalDisplaySceneDelegate";
const PRIMARY_DISPLAY_ROLE = "UIWindowSceneSessionRoleApplication";
const PRIMARY_DISPLAY_CONFIGURATION_NAME = "HB POS Main Display";
const PRIMARY_DISPLAY_DELEGATE_CLASS =
  "HBExternalDisplay.HBPrimarySceneDelegate";
const APP_DELEGATE_V2_SCENE_MARKER =
  "// HBPrimarySceneDelegate owns the main React Native root. [v2]";
const APP_DELEGATE_WINDOW_HANDOFF_MARKER =
  "// HBPrimarySceneDelegate takes over the AppDelegate main React Native window. [v3]";
const APP_DELEGATE_IMPORT_ANCHOR =
  "import ReactAppDependencyProvider\n";
const APP_DELEGATE_CLASS_ANCHOR =
  "public class AppDelegate: ExpoAppDelegate {";
const APP_DELEGATE_WINDOW_ANCHOR = "  var window: UIWindow?";
const APP_DELEGATE_FACTORY_BINDING_ANCHOR =
  "    bindReactNativeFactory(factory)";
const APP_DELEGATE_SUPER_RETURN_ANCHOR =
  "    return super.application(application, didFinishLaunchingWithOptions: launchOptions)";
const APP_DELEGATE_DID_FINISH_LAUNCHING_ANCHOR =
  "didFinishLaunchingWithOptions launchOptions:";
const APP_DELEGATE_SCENE_CLASS =
  "public class AppDelegate: ExpoAppDelegate, HBPrimaryWindowAppDelegate {";
const APP_DELEGATE_PUBLIC_WINDOW = "  public var window: UIWindow?";
const EXPO_SDK_54_MAIN_ROOT_PATTERN =
  /\n#if os\(iOS\) \|\| os\(tvOS\)\r?\n[ \t]*window = UIWindow\(frame: UIScreen\.main\.bounds\)\r?\n[ \t]*factory\.startReactNative\(\r?\n[ \t]*withModuleName: "main",\r?\n[ \t]*in: window,\r?\n[ \t]*launchOptions: launchOptions\)\r?\n#endif\r?\n/;
const EXPO_SDK_54_MAIN_ROOT_BLOCK = `#if os(iOS) || os(tvOS)
    window = UIWindow(frame: UIScreen.main.bounds)
    factory.startReactNative(
      withModuleName: "main",
      in: window,
      launchOptions: launchOptions)
#endif`;
const MAIN_UI_WINDOW_ASSIGNMENT_PATTERN =
  /(?:^|[^\w.])(?:self\s*\.\s*)?window\s*=\s*UIWindow\s*\(/gm;
const START_REACT_NATIVE_CALL_PATTERN =
  /(?:\?|!)?\s*\.\s*startReactNative\s*\(/g;
const EXPECTED_FACTORY_START_REACT_NATIVE_PATTERN =
  /\bfactory\s*\.\s*startReactNative\s*\(\s*withModuleName\s*:\s*"main"\s*,\s*in\s*:\s*window\s*,\s*launchOptions\s*:\s*launchOptions\s*\)/;

const externalDisplayConfiguration = {
  UISceneConfigurationName: EXTERNAL_DISPLAY_CONFIGURATION_NAME,
  UISceneClassName: "UIWindowScene",
  UISceneDelegateClassName: EXTERNAL_DISPLAY_DELEGATE_CLASS,
};
const primaryDisplayConfiguration = {
  UISceneConfigurationName: PRIMARY_DISPLAY_CONFIGURATION_NAME,
  UISceneClassName: "UIWindowScene",
  UISceneDelegateClassName: PRIMARY_DISPLAY_DELEGATE_CLASS,
};

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

/**
 * CNG 每次重建原生工程时都以相同方式合并 Scene Manifest。
 */
function applyExternalDisplayInfoPlist(infoPlist) {
  const source = isRecord(infoPlist) ? infoPlist : {};
  const manifest = isRecord(source.UIApplicationSceneManifest)
    ? source.UIApplicationSceneManifest
    : {};
  const configurations = isRecord(manifest.UISceneConfigurations)
    ? manifest.UISceneConfigurations
    : {};
  const existingPrimaryScenes = Array.isArray(
    configurations[PRIMARY_DISPLAY_ROLE],
  )
    ? configurations[PRIMARY_DISPLAY_ROLE]
    : [];
  const existingExternalScenes = Array.isArray(
    configurations[EXTERNAL_DISPLAY_ROLE],
  )
    ? configurations[EXTERNAL_DISPLAY_ROLE]
    : [];
  const unrelatedPrimaryScenes = existingPrimaryScenes.filter(
    (configuration) =>
      !isRecord(configuration) ||
      (configuration.UISceneConfigurationName !==
        PRIMARY_DISPLAY_CONFIGURATION_NAME &&
        configuration.UISceneDelegateClassName !==
          PRIMARY_DISPLAY_DELEGATE_CLASS),
  );
  const unrelatedExternalScenes = existingExternalScenes.filter(
    (configuration) =>
      !isRecord(configuration) ||
      (configuration.UISceneConfigurationName !==
        EXTERNAL_DISPLAY_CONFIGURATION_NAME &&
        configuration.UISceneDelegateClassName !==
          EXTERNAL_DISPLAY_DELEGATE_CLASS),
  );

  return {
    ...source,
    UIApplicationSceneManifest: {
      ...manifest,
      UIApplicationSupportsMultipleScenes: true,
      UISceneConfigurations: {
        ...configurations,
        [PRIMARY_DISPLAY_ROLE]: [
          { ...primaryDisplayConfiguration },
          ...unrelatedPrimaryScenes,
        ],
        [EXTERNAL_DISPLAY_ROLE]: [
          { ...externalDisplayConfiguration },
          ...unrelatedExternalScenes,
        ],
      },
    },
  };
}

/**
 * 只检查 didFinishLaunchingWithOptions，避免把其他方法或注释中的锚点算入主 root。
 */
function getDidFinishLaunchingBody(source) {
  const signatureIndex = source.indexOf(
    APP_DELEGATE_DID_FINISH_LAUNCHING_ANCHOR,
  );
  if (
    signatureIndex === -1 ||
    source.indexOf(
      APP_DELEGATE_DID_FINISH_LAUNCHING_ANCHOR,
      signatureIndex + APP_DELEGATE_DID_FINISH_LAUNCHING_ANCHOR.length,
    ) !== -1
  ) {
    return null;
  }

  const bodyStartIndex = source.indexOf("{", signatureIndex);
  if (bodyStartIndex === -1) {
    return null;
  }

  let braceDepth = 0;
  for (let index = bodyStartIndex; index < source.length; index += 1) {
    if (source[index] === "{") {
      braceDepth += 1;
    } else if (source[index] === "}") {
      braceDepth -= 1;
      if (braceDepth === 0) {
        return source.slice(bodyStartIndex + 1, index);
      }
    }
  }

  return null;
}

/**
 * Expo SDK 54 仍在 AppDelegate 创建并启动主 UIWindow，随后由主 scene 接管同一窗口。
 */
function applyExternalDisplayAppDelegate(source) {
  const applicationBody = getDidFinishLaunchingBody(source) ?? "";
  const mainRootStartupIndex = applicationBody.search(
    EXPO_SDK_54_MAIN_ROOT_PATTERN,
  );
  const factoryBindingIndex = applicationBody.indexOf(
    APP_DELEGATE_FACTORY_BINDING_ANCHOR,
  );
  const superReturnIndex = applicationBody.indexOf(
    APP_DELEGATE_SUPER_RETURN_ANCHOR,
  );
  const mainWindowCreations = [
    ...applicationBody.matchAll(MAIN_UI_WINDOW_ASSIGNMENT_PATTERN),
  ];
  const startReactNativeCalls = [
    ...applicationBody.matchAll(START_REACT_NATIVE_CALL_PATTERN),
  ];

  if (source.includes(APP_DELEGATE_WINDOW_HANDOFF_MARKER)) {
    const v3MarkerIndex = applicationBody.indexOf(
      APP_DELEGATE_WINDOW_HANDOFF_MARKER,
    );

    if (
      !source.includes("import HBExternalDisplay\n") ||
      !source.includes(APP_DELEGATE_SCENE_CLASS) ||
      !source.includes(APP_DELEGATE_PUBLIC_WINDOW) ||
      factoryBindingIndex === -1 ||
      mainRootStartupIndex === -1 ||
      mainRootStartupIndex < factoryBindingIndex ||
      mainRootStartupIndex > superReturnIndex ||
      v3MarkerIndex < factoryBindingIndex ||
      v3MarkerIndex > mainRootStartupIndex ||
      mainWindowCreations.length !== 1 ||
      startReactNativeCalls.length !== 1 ||
      !EXPECTED_FACTORY_START_REACT_NATIVE_PATTERN.test(applicationBody) ||
      mainWindowCreations[0].index < v3MarkerIndex ||
      mainWindowCreations[0].index > startReactNativeCalls[0].index ||
      startReactNativeCalls[0].index > superReturnIndex ||
      source.includes(APP_DELEGATE_V2_SCENE_MARKER)
    ) {
      throw new Error(
        "AppDelegate window-handoff transform marker is incomplete; refusing to continue.",
      );
    }
    return source;
  }

  if (source.includes(APP_DELEGATE_V2_SCENE_MARKER)) {
    const v2MarkerIndex = applicationBody.indexOf(
      APP_DELEGATE_V2_SCENE_MARKER,
    );

    if (
      !source.includes("import HBExternalDisplay\n") ||
      !source.includes(APP_DELEGATE_SCENE_CLASS) ||
      !source.includes(APP_DELEGATE_PUBLIC_WINDOW) ||
      factoryBindingIndex === -1 ||
      mainRootStartupIndex !== -1 ||
      v2MarkerIndex < factoryBindingIndex ||
      v2MarkerIndex > superReturnIndex ||
      mainWindowCreations.length !== 0 ||
      startReactNativeCalls.length !== 0
    ) {
      throw new Error(
        "AppDelegate v2 scene transform is incomplete; refusing to upgrade.",
      );
    }

    return source.replace(
      APP_DELEGATE_V2_SCENE_MARKER,
      `${APP_DELEGATE_WINDOW_HANDOFF_MARKER}\n${EXPO_SDK_54_MAIN_ROOT_BLOCK}`,
    );
  }

  if (
    mainRootStartupIndex === -1 ||
    mainRootStartupIndex < factoryBindingIndex ||
    mainRootStartupIndex > superReturnIndex ||
    mainWindowCreations.length !== 1 ||
    startReactNativeCalls.length !== 1 ||
    !EXPECTED_FACTORY_START_REACT_NATIVE_PATTERN.test(applicationBody) ||
    mainWindowCreations[0].index < factoryBindingIndex ||
    mainWindowCreations[0].index > startReactNativeCalls[0].index ||
    startReactNativeCalls[0].index > superReturnIndex
  ) {
    throw new Error(
      "Expo SDK 54 AppDelegate main-root startup block was not found; refusing an unsafe transform.",
    );
  }
  if (
    !source.includes(APP_DELEGATE_IMPORT_ANCHOR) ||
    !source.includes(APP_DELEGATE_CLASS_ANCHOR) ||
    !source.includes(APP_DELEGATE_WINDOW_ANCHOR) ||
    factoryBindingIndex === -1
  ) {
    throw new Error(
      "Expo SDK 54 AppDelegate protocol anchors were not found; refusing an unsafe transform.",
    );
  }

  return source
    .replace(
      APP_DELEGATE_IMPORT_ANCHOR,
      `${APP_DELEGATE_IMPORT_ANCHOR}import HBExternalDisplay\n`,
    )
    .replace(
      APP_DELEGATE_CLASS_ANCHOR,
      APP_DELEGATE_SCENE_CLASS,
    )
    .replace(
      APP_DELEGATE_WINDOW_ANCHOR,
      APP_DELEGATE_PUBLIC_WINDOW,
    )
    .replace(
      EXPO_SDK_54_MAIN_ROOT_PATTERN,
      (mainRootStartup) =>
        `\n    ${APP_DELEGATE_WINDOW_HANDOFF_MARKER}${mainRootStartup}`,
    );
}

function withHbExternalDisplay(config) {
  let nextConfig = withInfoPlist(config, (infoPlistConfig) => {
    infoPlistConfig.modResults = applyExternalDisplayInfoPlist(
      infoPlistConfig.modResults,
    );
    return infoPlistConfig;
  });

  nextConfig = withAppDelegate(nextConfig, (appDelegateConfig) => {
    if (appDelegateConfig.modResults.language !== "swift") {
      throw new Error(
        "HB external display requires the Expo Swift AppDelegate template.",
      );
    }

    appDelegateConfig.modResults.contents =
      applyExternalDisplayAppDelegate(
        appDelegateConfig.modResults.contents,
      );
    return appDelegateConfig;
  });

  return nextConfig;
}

const plugin = createRunOncePlugin(
  withHbExternalDisplay,
  "with-hb-external-display",
  "0.1.0",
);

Object.assign(plugin, {
  EXTERNAL_DISPLAY_CONFIGURATION_NAME,
  EXTERNAL_DISPLAY_DELEGATE_CLASS,
  EXTERNAL_DISPLAY_ROLE,
  PRIMARY_DISPLAY_CONFIGURATION_NAME,
  PRIMARY_DISPLAY_DELEGATE_CLASS,
  PRIMARY_DISPLAY_ROLE,
  applyExternalDisplayAppDelegate,
  applyExternalDisplayInfoPlist,
});

module.exports = plugin;
