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
const expoAudioRoots = appRoots.map((appRoot) =>
  dirname(
    realpathSync(
      require.resolve("expo-audio/package.json", { paths: [appRoot] }),
    ),
  ),
);
const expoAudioRoot = expoAudioRoots[0];
const audioPlayerPath = join(
  expoAudioRoot,
  "android",
  "src",
  "main",
  "java",
  "expo",
  "modules",
  "audio",
  "AudioPlayer.kt",
);
const patchPath = join(repositoryRoot, "patches", "expo-audio+1.1.1.patch");

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

test("expo-audio Android 媒体路由补丁只修改 Android 源码且由双端共享解析", () => {
  assert.equal(
    expoAudioRoots[0],
    expoAudioRoots[1],
    "两个 POS App 必须消费同一 hoisted expo-audio",
  );
  const expoAudioPackage = readJson(join(expoAudioRoot, "package.json"));
  assert.equal(
    expoAudioPackage.version,
    "1.1.1",
    "升级 expo-audio 时必须重新评估 Android 短音效路由补丁",
  );
  const source = readFileSync(audioPlayerPath, "utf8");
  assert.doesNotMatch(
    source,
    /\.setAudioAttributes\(AudioAttributes\.DEFAULT,\s*false\)/,
  );
  assert.match(source, /\.setUsage\(C\.USAGE_MEDIA\)/);
  assert.match(source, /\.setContentType\(C\.AUDIO_CONTENT_TYPE_MUSIC\)/);
});

test("根 workspace 只重放一次 expo-audio Android 补丁", () => {
  const rootPackage = readJson(join(repositoryRoot, "package.json"));
  assert.equal(
    rootPackage.scripts?.postinstall,
    "patch-package --patch-dir patches --error-on-fail",
  );
  assert.ok(existsSync(patchPath), "缺少根级 expo-audio patch-package 补丁");
  const patch = readFileSync(patchPath, "utf8");
  assert.match(
    patch,
    /node_modules\/expo-audio\/android\/src\/main\/java\/expo\/modules\/audio\/AudioPlayer\.kt/,
  );
  assert.match(patch, /\.setUsage\(C\.USAGE_MEDIA\)/);
  assert.match(patch, /\.setContentType\(C\.AUDIO_CONTENT_TYPE_MUSIC\)/);

  for (const appRoot of appRoots) {
    const appPackage = readJson(join(appRoot, "package.json"));
    assert.equal(appPackage.scripts?.postinstall, undefined);
    assert.equal(appPackage.devDependencies?.["patch-package"], undefined);
  }
});
