import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const appRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const expoAudioRoot = join(appRoot, "node_modules", "expo-audio");
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
const patchPath = join(appRoot, "patches", "expo-audio+1.1.1.patch");

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

test("expo-audio Android 播放器显式路由到媒体音轨", () => {
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

test("npm install 会重放 expo-audio Android 媒体路由补丁", () => {
  const appPackage = readJson(join(appRoot, "package.json"));
  assert.equal(appPackage.scripts?.postinstall, "patch-package --error-on-fail");
  assert.ok(existsSync(patchPath), "缺少版本锁定的 expo-audio patch-package 补丁");

  const patch = readFileSync(patchPath, "utf8");
  assert.match(
    patch,
    /node_modules\/expo-audio\/android\/src\/main\/java\/expo\/modules\/audio\/AudioPlayer\.kt/,
  );
  assert.match(patch, /\.setUsage\(C\.USAGE_MEDIA\)/);
  assert.match(patch, /\.setContentType\(C\.AUDIO_CONTENT_TYPE_MUSIC\)/);
});
