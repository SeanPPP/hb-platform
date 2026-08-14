import assert from "node:assert/strict";
import test from "node:test";

import plugin from "./with-rn-fmt-xcode26.js";

const { applyRnFmtXcode26Podfile } = plugin;

const expoSdk54Podfile = `target 'HBPOS' do
  use_expo_modules!

  post_install do |installer|
    react_native_post_install(
      installer,
      config[:reactNativePath],
      :mac_catalyst_enabled => false,
      :ccache_enabled => ccache_enabled?(podfile_properties),
    )
  end
end
`;

test("Xcode 26 fmt 补丁插入 post_install 且重复执行保持幂等", () => {
  const once = applyRnFmtXcode26Podfile(expoSdk54Podfile);
  const twice = applyRnFmtXcode26Podfile(once);

  assert.equal(twice, once);
  assert.match(
    once,
    /HB POS Xcode 26 fmt consteval workaround \[v2\]/,
  );
  assert.match(once, /FMT_USE_CONSTEVAL/);
  assert.match(once, /installer\.sandbox\.pod_dir\('fmt'\)/);
  assert.match(once, /File\.chmod\(0644, fmt_base\)/);
});

test("Xcode 26 仅为 ExpoSQLite 关闭 Swift 显式模块且重复执行保持幂等", () => {
  const once = applyRnFmtXcode26Podfile(expoSdk54Podfile);
  const twice = applyRnFmtXcode26Podfile(once);

  assert.equal(twice, once);
  assert.match(
    once,
    /HB POS Xcode 26 ExpoSQLite explicit modules workaround/,
  );
  assert.match(once, /next unless pod_target\.name == 'ExpoSQLite'/);
  assert.match(
    once,
    /build_settings\['SWIFT_ENABLE_EXPLICIT_MODULES'\] = 'NO'/,
  );
});

test("已有 v1 补丁升级到可写安全的 v2，且不重复插入 post_install 块", () => {
  const v2 = applyRnFmtXcode26Podfile(expoSdk54Podfile);
  const v1 = v2
    .replace(
      "# HB POS Xcode 26 fmt consteval workaround [v2]",
      "# HB POS Xcode 26 fmt consteval workaround",
    )
    .replace(
      `      if fmt_patched != fmt_source
        File.chmod(0644, fmt_base)
        File.write(fmt_base, fmt_patched)
      end`,
      "      File.write(fmt_base, fmt_patched) if fmt_patched != fmt_source",
    );

  const upgraded = applyRnFmtXcode26Podfile(v1);

  assert.match(
    upgraded,
    /HB POS Xcode 26 fmt consteval workaround \[v2\]/,
  );
  assert.match(upgraded, /File\.chmod\(0644, fmt_base\)/);
  assert.equal(
    upgraded.match(/fmt_consteval_enabled/g)?.length,
    2,
  );
});

test("Podfile 模板漂移时失败关闭，禁止生成半配置工程", () => {
  assert.throws(
    () =>
      applyRnFmtXcode26Podfile(
        expoSdk54Podfile.replace(
          "react_native_post_install(",
          "future_react_native_post_install(",
        ),
      ),
    /Expo SDK 54 Podfile post_install anchor was not found/,
  );
});
