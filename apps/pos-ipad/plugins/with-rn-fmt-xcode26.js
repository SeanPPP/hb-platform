const fs = require("node:fs");
const path = require("node:path");

const {
  createRunOncePlugin,
  withDangerousMod,
} = require("@expo/config-plugins");

const LEGACY_PATCH_MARKER =
  "# HB POS Xcode 26 fmt consteval workaround";
const PATCH_MARKER =
  "# HB POS Xcode 26 fmt consteval workaround [v2]";
const EXPO_SQLITE_PATCH_MARKER =
  "# HB POS Xcode 26 ExpoSQLite explicit modules workaround";
const LEGACY_WRITE =
  "      File.write(fmt_base, fmt_patched) if fmt_patched != fmt_source";
const SAFE_WRITE = `      if fmt_patched != fmt_source
        File.chmod(0644, fmt_base)
        File.write(fmt_base, fmt_patched)
      end`;
const POST_INSTALL_ANCHOR = `    react_native_post_install(
      installer,
      config[:reactNativePath],
      :mac_catalyst_enabled => false,
      :ccache_enabled => ccache_enabled?(podfile_properties),
    )
`;
const FMT_PATCH = `
    ${PATCH_MARKER}
    fmt_base = File.join(
      installer.sandbox.pod_dir('fmt'),
      'include',
      'fmt',
      'base.h'
    )
    if File.exist?(fmt_base)
      fmt_source = File.read(fmt_base)
      fmt_consteval_enabled = "#elif defined(__cpp_consteval)\\n#  define FMT_USE_CONSTEVAL 1"
      fmt_consteval_disabled = "#elif defined(__cpp_consteval)\\n#  define FMT_USE_CONSTEVAL 0"
      fmt_patched = fmt_source.sub(
        fmt_consteval_enabled,
        fmt_consteval_disabled
      )
      if fmt_patched == fmt_source &&
         !fmt_source.include?(fmt_consteval_disabled)
        raise 'Unsupported fmt base.h: FMT_USE_CONSTEVAL anchor was not found.'
      end
      if fmt_patched != fmt_source
        File.chmod(0644, fmt_base)
        File.write(fmt_base, fmt_patched)
      end
    end
`;
const EXPO_SQLITE_PATCH = `
    ${EXPO_SQLITE_PATCH_MARKER}
    installer.pods_project.targets.each do |pod_target|
      next unless pod_target.name == 'ExpoSQLite'

      pod_target.build_configurations.each do |build_config|
        build_config.build_settings['SWIFT_ENABLE_EXPLICIT_MODULES'] = 'NO'
      end
    end
`;

/**
 * Expo SDK 54 / RN 0.81 从源码构建时，Xcode 26.4+ 会在 fmt 11.0.2 的
 * consteval 路径编译失败；Xcode 26.5 首次并行构建 ExpoSQLite 时还可能漏掉
 * _Builtin_stdarg 显式模块。补丁放进 post_install，保证 CNG 重建后仍可重复生成。
 */
function applyRnFmtXcode26Podfile(source) {
  let patchedSource = source;

  if (source.includes(PATCH_MARKER)) {
    if (!source.includes("File.chmod(0644, fmt_base)")) {
      throw new Error(
        "Xcode 26 fmt transform marker is incomplete; refusing to continue.",
      );
    }
  } else if (source.includes(LEGACY_PATCH_MARKER)) {
    if (!source.includes(LEGACY_WRITE)) {
      throw new Error(
        "Legacy Xcode 26 fmt transform is incomplete; refusing to continue.",
      );
    }
    patchedSource = source
      .replace(LEGACY_PATCH_MARKER, PATCH_MARKER)
      .replace(LEGACY_WRITE, SAFE_WRITE);
  } else if (!source.includes(POST_INSTALL_ANCHOR)) {
    throw new Error(
      "Expo SDK 54 Podfile post_install anchor was not found; refusing an unsafe fmt transform.",
    );
  } else {
    patchedSource = source.replace(
      POST_INSTALL_ANCHOR,
      `${POST_INSTALL_ANCHOR}${FMT_PATCH}`,
    );
  }

  if (patchedSource.includes(EXPO_SQLITE_PATCH_MARKER)) {
    if (
      !patchedSource.includes(
        "build_config.build_settings['SWIFT_ENABLE_EXPLICIT_MODULES'] = 'NO'",
      )
    ) {
      throw new Error(
        "Xcode 26 ExpoSQLite transform marker is incomplete; refusing to continue.",
      );
    }
    return patchedSource;
  }
  if (!patchedSource.includes(POST_INSTALL_ANCHOR)) {
    throw new Error(
      "Expo SDK 54 Podfile post_install anchor was not found; refusing an unsafe ExpoSQLite transform.",
    );
  }
  return patchedSource.replace(
    POST_INSTALL_ANCHOR,
    `${POST_INSTALL_ANCHOR}${EXPO_SQLITE_PATCH}`,
  );
}

function withRnFmtXcode26(config) {
  return withDangerousMod(config, [
    "ios",
    async (modConfig) => {
      const podfilePath = path.join(
        modConfig.modRequest.platformProjectRoot,
        "Podfile",
      );
      const source = fs.readFileSync(podfilePath, "utf8");
      fs.writeFileSync(
        podfilePath,
        applyRnFmtXcode26Podfile(source),
      );
      return modConfig;
    },
  ]);
}

const plugin = createRunOncePlugin(
  withRnFmtXcode26,
  "with-rn-fmt-xcode26",
  "0.2.0",
);

Object.assign(plugin, {
  applyRnFmtXcode26Podfile,
});

module.exports = plugin;
