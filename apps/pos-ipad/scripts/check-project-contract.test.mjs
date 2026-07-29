import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";

const appRoot = new URL("../", import.meta.url);
const packageJson = JSON.parse(readFileSync(new URL("package.json", appRoot), "utf8"));
const appConfigSource = readFileSync(new URL("app.config.ts", appRoot), "utf8");
const easConfig = JSON.parse(readFileSync(new URL("eas.json", appRoot), "utf8"));
const appProvidersSource = readFileSync(
  new URL("src/app-providers.tsx", appRoot),
  "utf8",
);
const routeFiles = readdirSync(new URL("app/", appRoot), {
  recursive: true,
  withFileTypes: true,
})
  .filter((entry) => entry.isFile())
  .map((entry) => entry.name);

assert.equal(packageJson.name, "@hb/pos-ipad");
assert.equal(packageJson.main, "expo-router/entry");
assert.equal(packageJson.private, true);
assert.equal(packageJson.scripts.android, undefined);
assert.match(
  packageJson.scripts["test:sync-history"],
  /src\/features\/sync-history\/\*\.rntl\.test\.tsx/,
  "默认同步历史测试必须包含屏幕 RNTL 用例。",
);
assert.match(appConfigSource, /com\.hbweb\.posipad/);
assert.match(appConfigSource, /supportedInterfaceOrientations/);
assert.match(appConfigSource, /UIRequiresFullScreen/);
assert.match(appConfigSource, /useSQLCipher:\s*true/);
assert.match(appConfigSource, /\.\/plugins\/with-hb-printer/);
assert.match(appConfigSource, /\.\/plugins\/with-hb-external-display/);
assert.match(appProvidersSource, /PeripheralStatusBridge/);
assert.equal(
  routeFiles.some((name) => /\.(?:test|spec)\.[cm]?[jt]sx?$/.test(name)),
  false,
  "Expo Router app/ 目录不得包含测试文件，避免测试依赖进入生产 bundle。",
);
assert.equal(easConfig.build.development.developmentClient, true);
assert.equal(easConfig.build.preview.distribution, "internal");
assert.equal(easConfig.build.production.distribution, "store");

console.log("pos-ipad project contract: ok");
