import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  renameSync,
  rmSync,
  symlinkSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { gzipSync } from "node:zlib";

import {
  analyzeWebBundle,
  collectWebBundle,
  validateWebBundleReport,
} from "./collect-web-bundle.mjs";

const fixtureRoot = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "fixtures/web-vite-dist",
);
const metricBatchScript = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "build-metric-batch.mjs",
);

function expectedAsset(file) {
  const bytes = readFileSync(join(fixtureRoot, file));
  return {
    rawBytes: bytes.byteLength,
    gzipBytes: gzipSync(bytes, { level: 9 }).byteLength,
  };
}

function withFixture(callback) {
  const directory = mkdtempSync(join(tmpdir(), "web-bundle-fixture-"));
  const distPath = join(directory, "dist");
  cpSync(fixtureRoot, distPath, { recursive: true });
  try {
    return callback({ directory, distPath });
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test("Vite fixture 按真实 gzip 汇总首屏 JS/CSS、最大初始 chunk 和路由动态 chunk", () => {
  const report = analyzeWebBundle(fixtureRoot, {
    now: new Date("2026-08-25T08:00:00.000Z"),
  });
  const initialFiles = [
    "assets/main.css",
    "assets/main.js",
    "assets/theme.css",
    "assets/vendor.js",
  ];
  const expected = initialFiles.map(expectedAsset);
  const initialJs = ["assets/main.js", "assets/vendor.js"].map((file) => ({
    file,
    ...expectedAsset(file),
  }));
  const largest = initialJs.toSorted((left, right) => right.gzipBytes - left.gzipBytes)[0];

  assert.equal(report.schemaVersion, "WebBundleReportV1");
  assert.equal(report.manifestPath, ".vite/manifest.json");
  assert.deepEqual(
    report.initialAssets.map((asset) => asset.file),
    initialFiles,
  );
  assert.equal(
    report.measurements.firstScreenRawBytes,
    expected.reduce((sum, asset) => sum + asset.rawBytes, 0),
  );
  assert.equal(
    report.measurements.firstScreenGzipBytes,
    expected.reduce((sum, asset) => sum + asset.gzipBytes, 0),
  );
  assert.equal(report.measurements.largestInitialChunkFile, largest.file);
  assert.equal(report.measurements.largestInitialChunkRawBytes, largest.rawBytes);
  assert.equal(report.measurements.largestInitialChunkGzipBytes, largest.gzipBytes);
  assert.deepEqual(
    report.routeDynamicChunks.map((chunk) => chunk.file),
    ["assets/orders.js"],
  );
  assert.equal(
    report.routeDynamicChunks[0].gzipBytes,
    expectedAsset("assets/orders.js").gzipBytes,
  );
  assert.deepEqual(report.routeDynamicChunks[0].cssAssets, [
    {
      file: "assets/orders.css",
      type: "css",
      ...expectedAsset("assets/orders.css"),
    },
  ]);
  assert.equal(validateWebBundleReport(report), report);
});

test("被 modulepreload 提升为首屏资源的动态 chunk 只计入首屏且不重复报错", () => {
  withFixture(({ distPath }) => {
    const manifestPath = join(distPath, ".vite/manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].dynamicImports.push("_vendor.js");
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");

    const report = analyzeWebBundle(distPath);

    assert.ok(report.initialAssets.some((asset) => asset.file === "assets/vendor.js"));
    assert.ok(
      !report.routeDynamicChunks.some((chunk) => chunk.file === "assets/vendor.js"),
    );
    assert.deepEqual(
      report.routeDynamicChunks.map((chunk) => chunk.file),
      ["assets/orders.js"],
    );
  });
});

test("collector 原子写入 artifact report，并支持根 manifest.json", () => {
  withFixture(({ directory, distPath }) => {
    renameSync(join(distPath, ".vite/manifest.json"), join(distPath, "manifest.json"));
    const outputPath = join(directory, "artifacts/web-bundle.json");
    const report = collectWebBundle({
      distPath,
      outputPath,
      now: new Date("2026-08-25T08:00:00.000Z"),
    });
    assert.equal(report.manifestPath, "manifest.json");
    assert.deepEqual(JSON.parse(readFileSync(outputPath, "utf8")), report);
  });
});

test("缺失 manifest 明确失败；合法的单入口构建不强制要求 modulepreload", () => {
  withFixture(({ distPath }) => {
    unlinkSync(join(distPath, ".vite/manifest.json"));
    assert.throws(() => analyzeWebBundle(distPath), /manifest/i);
  });
  withFixture(({ distPath }) => {
    const indexPath = join(distPath, "index.html");
    const html = readFileSync(indexPath, "utf8")
      .replace(/<link rel="modulepreload"[^>]*>/u, (tag) => `<!-- ${tag} -->`)
      .replace(/<link rel="preload"[^>]*>/u, (tag) => `<!-- ${tag} -->`);
    writeFileSync(indexPath, html, "utf8");
    const report = analyzeWebBundle(distPath);
    assert.ok(report.initialAssets.some((asset) => asset.file === "assets/main.js"));
    assert.ok(report.initialAssets.some((asset) => asset.file === "assets/vendor.js"));
    assert.ok(report.initialAssets.some((asset) => asset.file === "assets/main.css"));
  });
});

test("manifest 路径穿越和 dist 内符号链接均被拒绝", () => {
  withFixture(({ directory, distPath }) => {
    const manifestPath = join(distPath, ".vite/manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].file = "../outside.js";
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    writeFileSync(join(directory, "outside.js"), "outside", "utf8");
    assert.throws(() => analyzeWebBundle(distPath), /越界|穿越|dist|\.\./i);
  });
  withFixture(({ directory, distPath }) => {
    const target = join(directory, "outside.js");
    writeFileSync(target, "outside", "utf8");
    unlinkSync(join(distPath, "assets/main.js"));
    symlinkSync(target, join(distPath, "assets/main.js"));
    assert.throws(() => analyzeWebBundle(distPath), /符号链接|symlink/i);
  });
});

test("dist 根必须是普通目录，不能借目录符号链接读取其他位置", () => {
  const directory = mkdtempSync(join(tmpdir(), "web-bundle-root-link-"));
  const realDist = join(directory, "real-dist");
  const linkedDist = join(directory, "dist");
  cpSync(fixtureRoot, realDist, { recursive: true });
  symlinkSync(realDist, linkedDist, "dir");
  try {
    assert.throws(() => analyzeWebBundle(linkedDist), /符号链接|symlink/i);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("collector artifact 经 CLI 与 Web lane timing 合并为三个 Production 事件", () => {
  const directory = mkdtempSync(join(tmpdir(), "web-bundle-batch-cli-"));
  const laneDirectory = join(directory, "lanes");
  const webReportPath = join(directory, "web-bundle.json");
  const batchPath = join(directory, "metric-batch.json");
  mkdirSync(laneDirectory, { recursive: true });
  try {
    collectWebBundle({
      distPath: fixtureRoot,
      outputPath: webReportPath,
      now: new Date("2026-08-25T08:00:00.000Z"),
    });
    writeFileSync(
      join(laneDirectory, "web.json"),
      `${JSON.stringify(
        {
          schemaVersion: "QualityLaneResultV1",
          lane: "web",
          startedAtUtc: "2026-08-25T07:59:00.000Z",
          finishedAtUtc: "2026-08-25T08:01:00.000Z",
          durationMs: 120000,
          conclusion: "accepted",
        },
        null,
        2,
      )}\n`,
      "utf8",
    );
    const result = spawnSync(
      process.execPath,
      [
        metricBatchScript,
        "--results-dir",
        laneDirectory,
        "--web-bundle-file",
        webReportPath,
        "--output",
        batchPath,
      ],
      {
        encoding: "utf8",
        env: {
          ...process.env,
          QUALITY_EXPECTED_LANES: '["web"]',
          GITHUB_REPOSITORY: "hotbargain/hb-platform",
          GITHUB_EVENT_NAME: "schedule",
          GITHUB_REF: "refs/heads/main",
          GITHUB_SHA: "9".repeat(40),
          GITHUB_WORKFLOW: "quality-baseline",
          GITHUB_RUN_ID: "12345",
          GITHUB_RUN_ATTEMPT: "1",
        },
      },
    );
    assert.equal(result.status, 0, result.stderr);
    const batch = JSON.parse(readFileSync(batchPath, "utf8"));
    assert.deepEqual(
      batch.events.map((event) => event.metric),
      [
        "ci.run.duration",
        "web.first_screen.bytes",
        "web.largest_initial_chunk.bytes",
      ],
    );
    assert.ok(batch.events.every((event) => event.dimensions.environment === "Production"));
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("跨 job 的 Web bundle report 会复核总量和动态 chunk 类型", () => {
  const report = analyzeWebBundle(fixtureRoot, {
    now: new Date("2026-08-25T08:00:00.000Z"),
  });
  const wrongTotal = structuredClone(report);
  wrongTotal.measurements.firstScreenGzipBytes += 1;
  assert.throws(() => validateWebBundleReport(wrongTotal), /总量|不一致/i);

  const wrongRouteType = structuredClone(report);
  wrongRouteType.routeDynamicChunks[0].file = "assets/orders.css";
  assert.throws(() => validateWebBundleReport(wrongRouteType), /JS|chunk|类型/i);
});

test("首屏闭包只从 index.html 的 module script 入口展开，不混入其他 Vite entry", () => {
  withFixture(({ distPath }) => {
    const manifestPath = join(distPath, ".vite/manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["admin.html"] = {
      file: "assets/admin.js",
      src: "admin.html",
      isEntry: true,
    };
    manifest["src/routes/admin.tsx"] = {
      file: "assets/admin-route.js",
      src: "src/routes/admin.tsx",
      isDynamicEntry: true,
    };
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    writeFileSync(join(distPath, "assets/admin.js"), "export const admin = true;\n", "utf8");
    writeFileSync(
      join(distPath, "assets/admin-route.js"),
      "export const adminRoute = true;\n",
      "utf8",
    );

    const report = analyzeWebBundle(distPath);
    assert.ok(!report.initialAssets.some((asset) => asset.file === "assets/admin.js"));
    assert.ok(
      !report.routeDynamicChunks.some((chunk) => chunk.file === "assets/admin-route.js"),
    );
  });
});

test("动态 chunk 排序对 Vite 的大小写文件名使用同一比较器", () => {
  withFixture(({ distPath }) => {
    const manifestPath = join(distPath, ".vite/manifest.json");
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].dynamicImports.push("src/routes/Invoice.tsx", "excel");
    manifest["src/routes/Invoice.tsx"] = {
      file: "assets/Invoice.js",
      src: "src/routes/Invoice.tsx",
      isDynamicEntry: true,
    };
    manifest.excel = {
      file: "assets/excel.js",
      name: "excel",
      isDynamicEntry: true,
    };
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    writeFileSync(join(distPath, "assets/Invoice.js"), "export const invoice = true;\n", "utf8");
    writeFileSync(join(distPath, "assets/excel.js"), "export const excel = true;\n", "utf8");

    const report = analyzeWebBundle(distPath);
    assert.deepEqual(
      report.routeDynamicChunks.map((chunk) => chunk.file),
      ["assets/Invoice.js", "assets/excel.js", "assets/orders.js"].sort(),
    );
  });
});
