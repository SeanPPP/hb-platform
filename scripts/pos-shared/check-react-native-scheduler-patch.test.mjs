import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { existsSync, readFileSync, realpathSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const appRoots = [
  join(repositoryRoot, "apps", "pos-ipad"),
  join(repositoryRoot, "apps", "pos-handheld"),
];
const reactNativeRoots = appRoots.map((appRoot) =>
  dirname(
    realpathSync(
      require.resolve("react-native/package.json", { paths: [appRoot] }),
    ),
  ),
);
const reactNativeRoot = reactNativeRoots[0];
const schedulerRoot = join(
  reactNativeRoot,
  "ReactCommon",
  "react",
  "renderer",
  "scheduler",
);
const schedulerHeaderPath = join(schedulerRoot, "Scheduler.h");
const schedulerSourcePath = join(schedulerRoot, "Scheduler.cpp");
const patchPath = join(
  repositoryRoot,
  "patches",
  "react-native+0.81.5.patch",
);

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function section(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, "缺少起始标记: " + startMarker);
  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.notEqual(end, -1, "缺少结束标记: " + endMarker);
  return source.slice(start, end);
}

function assertPatchMatchesInstalledSource(patch, targetPath, installedSource) {
  const diffMarker = "diff --git a/" + targetPath + " b/" + targetPath;
  const diffStart = patch.indexOf(diffMarker);
  assert.notEqual(diffStart, -1, "补丁缺少目标文件: " + targetPath);
  const nextDiff = patch.indexOf("\ndiff --git ", diffStart + diffMarker.length);
  const fileDiff = patch.slice(diffStart, nextDiff === -1 ? undefined : nextDiff);
  const hunks = fileDiff.split(/^@@[^\n]*@@[^\n]*\n/gm).slice(1);
  assert.ok(hunks.length > 0, "补丁缺少 hunk: " + targetPath);
  for (const hunk of hunks) {
    const expectedInstalledText = hunk
      .split("\n")
      .filter((line) => line.startsWith(" ") || line.startsWith("+"))
      .map((line) => line.slice(1))
      .join("\n")
      .trimEnd();
    assert.ok(
      installedSource.includes(expectedInstalledText),
      "补丁结果与已安装源码不一致: " + targetPath,
    );
  }
}

test("两个 POS App 解析到同一份已补丁 React Native", () => {
  assert.equal(
    reactNativeRoots[0],
    reactNativeRoots[1],
    "两个 POS App 必须消费同一 hoisted React Native",
  );
  const reactNativePackage = readJson(join(reactNativeRoot, "package.json"));
  assert.equal(
    reactNativePackage.version,
    "0.81.5",
    "升级 React Native 时必须重新评估并更新 Scheduler 补丁",
  );

  const header = readFileSync(schedulerHeaderPath, "utf8");
  const source = readFileSync(schedulerSourcePath, "utf8");
  const constructor = section(source, "Scheduler::Scheduler(", "Scheduler::~Scheduler()");
  const destructor = section(source, "Scheduler::~Scheduler()", "void Scheduler::registerSurface(");
  const setDelegate = section(
    source,
    "void Scheduler::setDelegate(",
    "SchedulerDelegate* Scheduler::getDelegate()",
  );
  const finishTransaction = section(
    source,
    "void Scheduler::uiManagerDidFinishTransaction(",
    "void Scheduler::uiManagerDidCreateShadowNode(",
  );
  const dispatchCommand = section(
    source,
    "void Scheduler::uiManagerDidDispatchCommand(",
    "void Scheduler::uiManagerDidSendAccessibilityEvent(",
  );

  assert.match(header, /#include <atomic>/);
  assert.match(header, /std::shared_ptr<std::atomic<bool>>\s+delegateInvalidated_;/);
  assert.match(
    constructor,
    /delegateInvalidated_\s*=\s*std::make_shared<std::atomic<bool>>\(false\);/,
  );
  assert.match(destructor, /\*delegateInvalidated_\s*=\s*true;/);
  assert.match(setDelegate, /if\s*\(delegate_\s*!=\s*delegate\)/);
  assert.match(setDelegate, /\*delegateInvalidated_\s*=\s*true;/);

  for (const callback of [finishTransaction, dispatchCommand]) {
    assert.match(callback, /invalidated\s*=\s*delegateInvalidated_/);
    assert.match(callback, /if\s*\(\*invalidated\)\s*\{\s*return;\s*\}/);
    assert.ok(
      callback.indexOf("delegate->scheduler") > callback.indexOf("if (*invalidated)"),
      "必须先检查失效令牌，再解引用 delegate",
    );
  }
});

test("根 workspace 只重放一次 React Native Scheduler 补丁", () => {
  const rootPackage = readJson(join(repositoryRoot, "package.json"));
  assert.equal(
    rootPackage.scripts?.postinstall,
    "patch-package --patch-dir patches --error-on-fail",
  );
  assert.equal(rootPackage.devDependencies?.["patch-package"], "8.0.1");
  assert.ok(existsSync(patchPath), "缺少根级 React Native patch-package 补丁");

  for (const appRoot of appRoots) {
    const appPackage = readJson(join(appRoot, "package.json"));
    const appConfig = readFileSync(join(appRoot, "app.config.ts"), "utf8");
    assert.equal(appPackage.scripts?.postinstall, undefined);
    assert.equal(appPackage.devDependencies?.["patch-package"], undefined);
    assert.equal(
      appPackage.scripts?.["test:react-native-scheduler"],
      "node --test scripts/check-react-native-scheduler-patch.test.mjs",
    );
    assert.equal(
      appPackage.scripts?.["eas-build-post-install"],
      "npm run test:react-native-scheduler",
      "EAS 安装后必须验证根 workspace 应用的 React Native 补丁",
    );
    assert.match(appConfig, /buildReactNativeFromSource:\s*true/);
  }

  const patch = readFileSync(patchPath, "utf8");
  assert.match(patch, /delegateInvalidated_/);
  assertPatchMatchesInstalledSource(
    patch,
    "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.cpp",
    readFileSync(schedulerSourcePath, "utf8"),
  );
  assertPatchMatchesInstalledSource(
    patch,
    "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.h",
    readFileSync(schedulerHeaderPath, "utf8"),
  );
});
