import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const appRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const reactNativeRoot = join(appRoot, "node_modules", "react-native");
const schedulerRoot = join(
  reactNativeRoot,
  "ReactCommon",
  "react",
  "renderer",
  "scheduler",
);
const schedulerHeaderPath = join(schedulerRoot, "Scheduler.h");
const schedulerSourcePath = join(schedulerRoot, "Scheduler.cpp");
const appConfigPath = join(appRoot, "app.config.ts");
const patchPath = join(appRoot, "patches", "react-native+0.81.5.patch");

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function section(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, `缺少起始标记: ${startMarker}`);

  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.notEqual(end, -1, `缺少结束标记: ${endMarker}`);

  return source.slice(start, end);
}

function assertPatchMatchesInstalledSource(patch, targetPath, installedSource) {
  const diffMarker = `diff --git a/${targetPath} b/${targetPath}`;
  const diffStart = patch.indexOf(diffMarker);
  assert.notEqual(diffStart, -1, `补丁缺少目标文件: ${targetPath}`);

  const nextDiff = patch.indexOf("\ndiff --git ", diffStart + diffMarker.length);
  const fileDiff = patch.slice(diffStart, nextDiff === -1 ? undefined : nextDiff);
  const hunks = fileDiff.split(/^@@[^\n]*@@[^\n]*\n/gm).slice(1);
  assert.ok(hunks.length > 0, `补丁缺少 hunk: ${targetPath}`);

  for (const hunk of hunks) {
    const expectedInstalledLines = hunk
      .split("\n")
      .filter((line) => line.startsWith(" ") || line.startsWith("+"))
      .map((line) => line.slice(1));
    const expectedInstalledText = expectedInstalledLines.join("\n").trimEnd();
    assert.ok(
      installedSource.includes(expectedInstalledText),
      `补丁结果与已安装源码不一致: ${targetPath}`,
    );
  }
}

test("已安装的 React Native Scheduler 会使过期 delegate 回调失效", () => {
  const reactNativePackage = readJson(join(reactNativeRoot, "package.json"));
  assert.equal(
    reactNativePackage.version,
    "0.81.5",
    "升级 React Native 时必须重新评估并更新 Scheduler 补丁",
  );

  const header = readFileSync(schedulerHeaderPath, "utf8");
  const source = readFileSync(schedulerSourcePath, "utf8");
  const constructor = section(
    source,
    "Scheduler::Scheduler(",
    "Scheduler::~Scheduler()",
  );
  const destructor = section(
    source,
    "Scheduler::~Scheduler()",
    "void Scheduler::registerSurface(",
  );
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
  const finishTransactionCallback = section(
    finishTransaction,
    "runtimeScheduler_->scheduleRenderingUpdate(",
    "});",
  );
  const dispatchCommandCallback = section(
    dispatchCommand,
    "runtimeScheduler_->scheduleRenderingUpdate(",
    "});",
  );

  assert.match(header, /#include <atomic>/);
  assert.match(
    header,
    /std::shared_ptr<std::atomic<bool>>\s+delegateInvalidated_;/,
  );
  assert.match(
    constructor,
    /delegateInvalidated_\s*=\s*std::make_shared<std::atomic<bool>>\(false\);/,
  );
  assert.match(destructor, /\*delegateInvalidated_\s*=\s*true;/);
  assert.match(setDelegate, /if\s*\(delegate_\s*!=\s*delegate\)/);
  assert.match(setDelegate, /\*delegateInvalidated_\s*=\s*true;/);
  assert.match(
    setDelegate,
    /delegateInvalidated_\s*=\s*std::make_shared<std::atomic<bool>>\(false\);/,
  );

  for (const deferredCallback of [
    finishTransactionCallback,
    dispatchCommandCallback,
  ]) {
    assert.match(
      deferredCallback,
      /invalidated\s*=\s*delegateInvalidated_/,
    );
    assert.match(
      deferredCallback,
      /if\s*\(\*invalidated\)\s*\{\s*return;\s*\}/,
      "delegate 失效后必须立即退出异步回调",
    );
    const guardIndex = deferredCallback.indexOf("if (*invalidated)");
    const dereferenceIndex = deferredCallback.indexOf("delegate->scheduler");
    assert.ok(guardIndex >= 0, "异步回调缺少 delegate 失效检查");
    assert.ok(
      dereferenceIndex > guardIndex,
      "必须先检查失效令牌，再解引用 delegate",
    );
  }
});

test("npm install 会严格重放 React Native Scheduler 补丁", () => {
  const appPackage = readJson(join(appRoot, "package.json"));
  const appConfig = readFileSync(appConfigPath, "utf8");

  assert.equal(appPackage.scripts?.postinstall, "patch-package --error-on-fail");
  assert.equal(
    appPackage.scripts?.["eas-build-post-install"],
    "npm run test:react-native-scheduler",
  );
  assert.equal(
    appPackage.scripts?.["test:react-native-scheduler"],
    "node --test scripts/check-react-native-scheduler-patch.test.mjs",
  );
  assert.equal(
    appPackage.scripts?.ios,
    "npm run test:react-native-scheduler && expo run:ios",
  );
  assert.equal(appPackage.devDependencies?.["patch-package"], "8.0.1");
  assert.ok(existsSync(patchPath), "缺少版本锁定的 react-native patch-package 补丁");
  assert.match(
    appConfig,
    /buildReactNativeFromSource:\s*true/,
    "原生补丁要求 React Native 从源码编译",
  );

  const patch = readFileSync(patchPath, "utf8");
  const patchTargets = [...patch.matchAll(/^diff --git a\/(.+?) b\/(.+)$/gm)].map(
    ([, before, after]) => [before, after],
  );
  assert.deepEqual(patchTargets, [
    [
      "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.cpp",
      "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.cpp",
    ],
    [
      "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.h",
      "node_modules/react-native/ReactCommon/react/renderer/scheduler/Scheduler.h",
    ],
  ]);
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
