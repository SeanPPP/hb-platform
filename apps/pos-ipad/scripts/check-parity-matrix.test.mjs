import assert from "node:assert/strict";
import { existsSync, readFileSync, statSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const matrixUrl = new URL("../test-fixtures/wpf-parity/feature-matrix.json", import.meta.url);
const matrix = JSON.parse(readFileSync(matrixUrl, "utf8"));
const repositoryRoot = fileURLToPath(new URL("../../../", import.meta.url));
const permissionsSource = readFileSync(
  new URL("../src/core/contracts/pos-terminal-permissions.ts", import.meta.url),
  "utf8",
);
const canonicalPermissions = new Set(
  [...permissionsSource.matchAll(/"(Permissions\.PosTerminal\.[^"]+)"/g)].map(
    (match) => match[1],
  ),
);

const requiredFeatureIds = [
  "device-registration",
  "device-reregister",
  "cashier-login",
  "supervisor-authorization",
  "emergency-login",
  "screen-lock",
  "operation-audit",
  "catalog-snapshot",
  "barcode-search",
  "remote-product-fallback",
  "open-item",
  "line-editing",
  "discounts-promotions",
  "clear-cart",
  "hold-resume",
  "cash-sale",
  "zero-total-sale",
  "cash-rounding",
  "square-payment",
  "linkly-cloud-payment",
  "voucher-payment",
  "mixed-payment",
  "payment-recovery",
  "payment-success",
  "receipt-return",
  "no-receipt-return",
  "offline-cash-return",
  "local-history",
  "remote-history",
  "sync-recovery",
  "support-export",
  "special-products",
  "installments",
  "daily-close",
  "catalog-settings",
  "payment-settings",
  "attendance-qr",
  "locale-parity",
  "receipt-printing",
  "cash-drawer",
  "hid-scanner",
  "camera-scanner",
  "customer-display",
  "customer-display-adverts",
  "app-updates",
  "unlisted-distribution",
];

const blockerKinds = new Set([
  "automated-test",
  "external-release",
  "parity-gap",
  "production-wiring",
  "real-device",
]);

function assertRepositoryFile(relativePath, label) {
  assert.equal(typeof relativePath, "string", `${label} must be a string`);
  assert.match(relativePath, /^(apps|docs|packages)\//, `${label} must be repository-relative`);
  assert.ok(!relativePath.includes("\\"), `${label} must use POSIX separators`);
  assert.ok(!relativePath.split("/").includes(".."), `${label} must not escape the repository`);

  const absolutePath = resolve(repositoryRoot, relativePath);
  assert.ok(existsSync(absolutePath), `${label} does not exist: ${relativePath}`);
  assert.ok(statSync(absolutePath).isFile(), `${label} must reference a file: ${relativePath}`);
}

assert.equal(matrix.version, 2);
assert.equal(matrix.target, "apps/pos-ipad");
assert.ok(Array.isArray(matrix.modules));
assert.ok(Array.isArray(matrix.allowedPlatformAdaptations));
assertRepositoryFile(matrix.implementationAudit, "implementationAudit");
const implementationAudit = JSON.parse(
  readFileSync(resolve(repositoryRoot, matrix.implementationAudit), "utf8"),
);
assert.equal(implementationAudit.version, 1);
assert.equal(implementationAudit.target, matrix.target);
assert.ok(
  implementationAudit.features &&
    typeof implementationAudit.features === "object",
);

const features = matrix.modules.flatMap((module) => module.features);
const ids = features.map((feature) => feature.id);
const featuresById = new Map(features.map((feature) => [feature.id, feature]));
assert.equal(new Set(ids).size, ids.length, "feature ids must be unique");
assert.deepEqual(
  [...ids].sort(),
  [...requiredFeatureIds].sort(),
  "feature matrix must contain exactly the frozen WPF parity feature ids",
);
assert.deepEqual(
  Object.keys(implementationAudit.features).sort(),
  [...requiredFeatureIds].sort(),
  "implementation audit must contain exactly one entry for every feature",
);
assert.deepEqual(
  featuresById.get("local-history")?.permissions,
  ["Permissions.PosTerminal.History.View"],
  "local history must keep the WPF History.View boundary",
);
assert.deepEqual(
  featuresById.get("support-export")?.permissions,
  [
    "Permissions.PosTerminal.History.View",
    "Permissions.PosTerminal.Audit.View",
  ],
  "support export must require both history and diagnostic audit access",
);

for (const feature of features) {
  assert.match(feature.workPackage, /^W[0-5]\.\d+$/);
  assert.ok(
    ["planned", "in-progress", "implemented-and-tested"].includes(feature.status),
    `unsupported status for ${feature.id}: ${feature.status}`,
  );
  assert.ok(Array.isArray(feature.acceptance) && feature.acceptance.length > 0);
  assert.ok(Array.isArray(feature.sourceEvidence) && feature.sourceEvidence.length > 0);
  assert.ok(["online", "offline", "hybrid", "local"].includes(feature.connectivity));
  assert.ok(Array.isArray(feature.permissions), `${feature.id} permissions must be an array`);
  assert.equal(
    new Set(feature.permissions).size,
    feature.permissions.length,
    `${feature.id} permissions must be unique`,
  );

  for (const permission of feature.permissions) {
    assert.ok(
      canonicalPermissions.has(permission),
      `${feature.id} uses an unknown POS permission: ${permission}`,
    );
  }
  for (const [index, path] of feature.sourceEvidence.entries()) {
    assertRepositoryFile(path, `${feature.id}.sourceEvidence[${index}]`);
  }

  const evidence = implementationAudit.features[feature.id];
  assert.ok(evidence && typeof evidence === "object", `${feature.id} evidence is required`);
  assert.ok(Array.isArray(evidence.ipad), `${feature.id}.ipad must be an array`);
  assert.ok(Array.isArray(evidence.tests), `${feature.id}.tests must be an array`);
  assert.ok(Array.isArray(evidence.blockers), `${feature.id}.blockers must be an array`);

  for (const [index, path] of evidence.ipad.entries()) {
    assert.match(
      path,
      /^(?:apps\/pos-ipad|packages\/pos-[^/]+)\//,
      `${feature.id}.ipad[${index}] must be iPad or shared POS code`,
    );
    assertRepositoryFile(path, `${feature.id}.ipad[${index}]`);
  }
  for (const [index, path] of evidence.tests.entries()) {
    assert.match(
      path,
      /^(?:apps\/pos-ipad\/(?:e2e\/|.*(?:\.test|\.spec)\.[cm]?[jt]sx?$)|packages\/pos-[^/]+\/src\/.*(?:\.test|\.spec)\.[cm]?[jt]sx?$)/,
      `${feature.id}.tests[${index}] must be an automated iPad or shared POS test`,
    );
    assertRepositoryFile(path, `${feature.id}.tests[${index}]`);
  }
  for (const blocker of evidence.blockers) {
    assert.ok(blocker && typeof blocker === "object", `${feature.id} blocker must be an object`);
    assert.match(blocker.code, /^[A-Z][A-Z0-9_]*$/, `${feature.id} blocker code is invalid`);
    assert.ok(blockerKinds.has(blocker.kind), `${feature.id} blocker kind is invalid`);
    assert.equal(typeof blocker.detail, "string", `${feature.id} blocker detail must be a string`);
    assert.ok(blocker.detail.trim().length > 0, `${feature.id} blocker detail is required`);
  }

  if (feature.status === "implemented-and-tested") {
    assert.ok(evidence.ipad.length > 0, `${feature.id} needs production iPad evidence`);
    assert.ok(evidence.tests.length > 0, `${feature.id} needs automated iPad test evidence`);
    assert.equal(evidence.blockers.length, 0, `${feature.id} cannot be complete with blockers`);
  } else {
    assert.ok(evidence.blockers.length > 0, `${feature.id} must explain why it is incomplete`);
  }
}

const complete = features.filter((feature) => feature.status === "implemented-and-tested").length;
console.log(`wpf parity matrix: ${features.length} mapped, ${complete} implemented and tested`);
