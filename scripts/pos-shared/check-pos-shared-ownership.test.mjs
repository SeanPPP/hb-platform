import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const manifestPath = join(
  repositoryRoot,
  "scripts",
  "pos-shared",
  "pos-shared-ownership.json",
);
const allowlistPath = join(
  repositoryRoot,
  "scripts",
  "pos-shared",
  "pos-app-local-allowlist.json",
);
const migrationStatePath = join(
  repositoryRoot,
  "scripts",
  "pos-shared",
  "pos-shared-migration-state.json",
);

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function listTree(root, baseline) {
  const output = execFileSync(
    "git",
    ["ls-tree", "-r", "--full-tree", baseline, "--", root],
    { cwd: repositoryRoot, encoding: "utf8" },
  );
  const prefix = `${root}/`;
  return new Map(
    output
      .trim()
      .split("\n")
      .filter(Boolean)
      .map((line) => {
        const [metadata, fullPath] = line.split("\t");
        const [, , blob] = metadata.split(" ");
        return [fullPath.slice(prefix.length), blob];
      }),
  );
}

test("归属清单锁定当前整合基线的双端字节一致文件", () => {
  const manifest = readJson(manifestPath);
  const migrationState = readJson(migrationStatePath);
  const ipad = listTree("apps/pos-ipad/src", manifest.baseline);
  const handheld = listTree("apps/pos-handheld/src", manifest.baseline);
  const identical = [...ipad.entries()]
    .filter(([path, blob]) => handheld.get(path) === blob)
    .sort(([left], [right]) => left.localeCompare(right));

  assert.equal(manifest.baseline, migrationState.baseline);
  assert.equal(manifest.files.length, identical.length);
  assert.deepEqual(
    manifest.files.map(({ path, blob }) => [path, blob]),
    identical,
  );
  assert.equal(
    manifest.summary.production,
    manifest.files.filter(({ kind }) => kind === "production").length,
  );
  assert.equal(
    manifest.summary.tests,
    manifest.files.filter(({ kind }) => kind === "test").length,
  );
});

test("生产文件归属汇总、迁移数量与后续专题边界一致", () => {
  const manifest = readJson(manifestPath);
  const migrationState = readJson(migrationStatePath);
  const actualByOwner = Object.fromEntries(
    Object.keys(manifest.summary.productionByOwner).map((owner) => [
      owner,
      manifest.files.filter(({ kind, owner: candidate }) =>
        kind === "production" && candidate === owner
      ).length,
    ]),
  );
  assert.deepEqual(manifest.summary.productionByOwner, actualByOwner);
  const migrated = Object.values(migrationState.migratedPaths).flat();
  assert.equal(migrated.length, 93);
  assert.equal(migrationState.reconciledPaths.length, 5);
  assert.equal(new Set([
    ...migrated,
    ...migrationState.reconciledPaths.map(({ path }) => path),
  ]).size, 98);

  for (const deferredPath of [
    "core/contracts/device-reregistration.ts",
    "features/app-updates/update-transition-lease-coordinator.ts",
  ]) {
    assert.equal(
      manifest.files.find(({ path }) => path === deferredPath)?.owner,
      "app-local",
      `${deferredPath} 必须保留在后续专题`,
    );
    assert.equal(migrated.includes(deferredPath), false);
  }

  for (const appLocalPath of [
    "core/observability/sentry-config.ts",
    "core/performance/business-startup-clock.ts",
    "core/performance/business-startup-origin.ts",
    "core/performance/business-timings.test.ts",
    "core/performance/client-metrics.ts",
    "core/performance/payment-performance.ts",
    "core/runtime/local-device-state.test.ts",
    "core/runtime/local-device-state.ts",
    "core/security/cashier-authentication.ts",
    "core/security/device-activation-code.test.ts",
    "core/security/device-activation-code.ts",
    "core/security/device-registration-api-partition-guard.test.ts",
    "core/security/device-registration-api-partition-guard.ts",
    "features/sales/runtime/scan-timing.test.ts",
    "features/sales/runtime/scan-timing.ts",
    "ui/scanner/scanner-route-bridge.tsx",
  ]) {
    assert.equal(
      manifest.files.find(({ path }) => path === appLocalPath)?.owner,
      "app-local",
      `${appLocalPath} 是 current-main App-local 边界`,
    );
  }
  assert.equal(
    manifest.files.find(({ path }) => path === "generated/hbpos/schema.d.ts")?.owner,
    "pos-api-client",
    "当前生成 DTO 必须集中到 pos-api-client",
  );

  for (const file of manifest.files) {
    assert.ok(file.owner, `${file.path} 缺少 owner`);
    assert.ok(file.reason, `${file.path} 缺少归属原因`);
  }
});

test("App-local allowlist 使用精确路径并为每项记录原因", () => {
  const manifest = readJson(manifestPath);
  const allowlist = readJson(allowlistPath);
  const expected = manifest.files
    .filter(({ owner }) => owner === "app-local")
    .map(({ path, reason }) => ({ path, reason }));

  assert.deepEqual(allowlist.baseline, manifest.baseline);
  assert.deepEqual(allowlist.files, expected);
  assert.equal(new Set(allowlist.files.map(({ path }) => path)).size, allowlist.files.length);
  assert.ok(allowlist.files.every(({ reason }) => reason.length >= 12));
});
