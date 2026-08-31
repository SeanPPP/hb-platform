import assert from "node:assert/strict";
import {
  cpSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  symlinkSync,
  unlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import { analyzeWebBundle } from "./collect-web-bundle.mjs";
import {
  validateWebBundleBudget,
  verifyWebBundle,
} from "./verify-web-bundle.mjs";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const baseFixtureRoot = resolve(scriptDirectory, "fixtures/web-vite-dist");

const requiredDynamicEntries = Object.freeze([
  { id: "dashboard", manifestKey: "src/pages/Dashboard/index.tsx" },
  { id: "shop-home", manifestKey: "src/pages/ShopHome/index.tsx" },
  {
    id: "warehouse-products",
    manifestKey: "src/pages/Warehouse/Products/index.tsx",
  },
  {
    id: "store-order-invoice",
    manifestKey: "src/pages/Warehouse/StoreOrders/Invoice.tsx",
  },
]);

function createGateFixture() {
  const directory = mkdtempSync(join(tmpdir(), "web-bundle-gate-"));
  const distPath = join(directory, "dist");
  cpSync(baseFixtureRoot, distPath, { recursive: true });

  const manifestPath = join(distPath, ".vite/manifest.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  manifest["index.html"].dynamicImports = requiredDynamicEntries.map(
    (entry) => entry.manifestKey,
  );
  const records = [
    [requiredDynamicEntries[0].manifestKey, "dashboard", ["excel"]],
    [requiredDynamicEntries[1].manifestKey, "shop-home", []],
    [requiredDynamicEntries[2].manifestKey, "warehouse-products", ["leaflet", "zxing"]],
    [requiredDynamicEntries[3].manifestKey, "store-order-invoice", ["pdf"]],
  ];
  for (const [manifestKey, fileStem, dynamicImports] of records) {
    manifest[manifestKey] = {
      file: `assets/${fileStem}.js`,
      name: fileStem,
      src: manifestKey,
      isDynamicEntry: true,
      imports: ["_vendor.js"],
      dynamicImports,
    };
    writeFileSync(
      join(distPath, `assets/${fileStem}.js`),
      `export const ${fileStem.replaceAll("-", "_")} = true;\n`,
      "utf8",
    );
  }
  for (const chunk of ["excel", "pdf", "leaflet", "zxing"]) {
    manifest[chunk] = {
      file: `assets/${chunk}.js`,
      name: chunk,
      src: `node_modules/${chunk}/index.js`,
      isDynamicEntry: true,
    };
    writeFileSync(
      join(distPath, `assets/${chunk}.js`),
      `export const ${chunk} = true;\n`,
      "utf8",
    );
  }
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  const dependencyMapPath = join(distPath, ".vite/bundle-dependencies.json");
  const dependencyChunks = Object.fromEntries(
    [...new Set(Object.values(manifest).map((record) => record.file))]
      .filter((file) => /\.(?:m?js)$/iu.test(file))
      .sort()
      .map((file) => [
        file,
        ["excel", "pdf", "leaflet", "zxing"].filter((dependency) =>
          file.includes(`/${dependency}.`),
        ),
      ]),
  );
  writeFileSync(
    dependencyMapPath,
    `${JSON.stringify({ schemaVersion: "WebBundleDependencyMapV1", chunks: dependencyChunks }, null, 2)}\n`,
    "utf8",
  );

  return {
    directory,
    distPath,
    manifestPath,
    dependencyMapPath,
    cleanup() {
      rmSync(directory, { recursive: true, force: true });
    },
  };
}

function createExactBudget(distPath) {
  const report = analyzeWebBundle(distPath, {
    now: new Date("2026-08-31T00:00:00.000Z"),
  });
  const main = report.initialAssets.find((asset) => asset.file === "assets/main.js");
  const jsAssets = [
    ...report.initialAssets.filter((asset) => asset.type === "js"),
    ...report.routeDynamicChunks.map((chunk) => ({
      file: chunk.file,
      rawBytes: chunk.rawBytes,
      gzipBytes: chunk.gzipBytes,
    })),
  ];
  const byFile = new Map(jsAssets.map((asset) => [asset.file, asset]));
  const excel = byFile.get("assets/excel.js");
  const pdf = byFile.get("assets/pdf.js");
  assert.ok(main && excel && pdf, "fixture 必须包含 main/excel/pdf chunk");
  return {
    schemaVersion: "WebBundleBudgetV1",
    mainEntry: {
      manifestKey: "index.html",
      maxRawBytes: main.rawBytes,
      maxGzipBytes: main.gzipBytes,
    },
    firstScreen: {
      maxGzipBytes: report.measurements.firstScreenGzipBytes,
    },
    largestInitialJs: {
      maxGzipBytes: report.measurements.largestInitialChunkGzipBytes,
    },
    anyJsChunk: {
      maxRawBytes: Math.max(...jsAssets.map((asset) => asset.rawBytes)),
    },
    asyncChunks: [
      {
        id: "excel",
        patterns: ["excel"],
        maxRawBytes: excel.rawBytes,
        maxGzipBytes: excel.gzipBytes,
      },
      {
        id: "pdf",
        patterns: ["pdf"],
        maxRawBytes: pdf.rawBytes,
        maxGzipBytes: pdf.gzipBytes,
      },
    ],
    forbiddenInitialPatterns: ["excel", "pdf", "leaflet", "zxing"],
    requiredDynamicEntries,
  };
}

function withGateFixture(callback) {
  const fixture = createGateFixture();
  try {
    return callback(fixture);
  } finally {
    fixture.cleanup();
  }
}

test("包体等于每项硬上限时通过", () => {
  withGateFixture(({ distPath }) => {
    const budget = createExactBudget(distPath);
    assert.equal(validateWebBundleBudget(budget), budget);
    const result = verifyWebBundle({
      distPath,
      budget,
      now: new Date("2026-08-31T00:00:00.000Z"),
    });
    assert.equal(result.schemaVersion, "WebBundleVerificationV1");
    assert.equal(result.conclusion, "accepted");
    assert.equal(result.violations.length, 0);
  });
});

test("任一指标只超出 1 byte 就失败且错误包含稳定检查编号", () => {
  withGateFixture(({ distPath }) => {
    const budget = createExactBudget(distPath);
    budget.firstScreen.maxGzipBytes -= 1;
    assert.throws(
      () => verifyWebBundle({ distPath, budget }),
      /BUNDLE_FIRST_SCREEN_GZIP.*超出 1 bytes/u,
    );
  });
});

test("Excel 或 PDF 进入首屏闭包必须失败", () => {
  for (const chunk of ["excel", "pdf"]) {
    withGateFixture(({ distPath, manifestPath }) => {
      const budget = createExactBudget(distPath);
      const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
      manifest["index.html"].imports.push(chunk);
      writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
      assert.throws(
        () => verifyWebBundle({ distPath, budget }),
        new RegExp(`BUNDLE_FORBIDDEN_INITIAL.*${chunk}`, "iu"),
      );
    });
  }
});

test("被 index.html modulepreload 提升的 Excel 仍按首屏依赖拒绝", () => {
  withGateFixture(({ distPath }) => {
    const budget = createExactBudget(distPath);
    const indexPath = join(distPath, "index.html");
    const html = readFileSync(indexPath, "utf8").replace(
      "</head>",
      '    <link rel="modulepreload" href="/assets/excel.js" />\n  </head>',
    );
    writeFileSync(indexPath, html, "utf8");
    assert.throws(
      () => verifyWebBundle({ distPath, budget }),
      /BUNDLE_FORBIDDEN_INITIAL.*excel/iu,
    );
  });
});

test("缺少代表性页面动态入口时失败", () => {
  withGateFixture(({ distPath, manifestPath }) => {
    const budget = createExactBudget(distPath);
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].dynamicImports = manifest["index.html"].dynamicImports.filter(
      (key) => key !== requiredDynamicEntries[0].manifestKey,
    );
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    assert.throws(
      () => verifyWebBundle({ distPath, budget }),
      /BUNDLE_REQUIRED_DYNAMIC_ENTRY.*dashboard/u,
    );
  });
});

test("代表性页面即使仍标记动态入口也不得进入首屏静态闭包", () => {
  withGateFixture(({ distPath, manifestPath }) => {
    const budget = createExactBudget(distPath);
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].imports.push(requiredDynamicEntries[0].manifestKey);
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    assert.throws(
      () => verifyWebBundle({ distPath, budget }),
      /BUNDLE_REQUIRED_DYNAMIC_ENTRY.*dashboard/u,
    );
  });
});

test("禁用依赖静态合并进通用 chunk 时仍失败", () => {
  for (const dependency of ["leaflet", "zxing"]) {
    withGateFixture(({ distPath, dependencyMapPath }) => {
      const budget = createExactBudget(distPath);
      const dependencyMap = JSON.parse(readFileSync(dependencyMapPath, "utf8"));
      dependencyMap.chunks["assets/main.js"] = [dependency];
      writeFileSync(
        dependencyMapPath,
        `${JSON.stringify(dependencyMap, null, 2)}\n`,
        "utf8",
      );
      assert.throws(
        () => verifyWebBundle({ distPath, budget }),
        new RegExp(`BUNDLE_FORBIDDEN_INITIAL.*${dependency}`, "iu"),
      );
    });
  }
});

test("Excel 或 PDF 合并进通用异步 chunk 时按真实依赖分类计量", () => {
  for (const dependency of ["excel", "pdf"]) {
    withGateFixture(({ distPath, manifestPath, dependencyMapPath }) => {
      const budget = createExactBudget(distPath);
      const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
      const dependencyMap = JSON.parse(readFileSync(dependencyMapPath, "utf8"));
      const genericKey = requiredDynamicEntries[0].manifestKey;
      const genericFile = manifest[genericKey].file;
      writeFileSync(
        join(distPath, genericFile),
        `export const oversized = "${"x".repeat(4096)}";\n`,
        "utf8",
      );
      dependencyMap.chunks[`assets/${dependency}.js`] = [];
      dependencyMap.chunks[genericFile] = [dependency];
      writeFileSync(
        dependencyMapPath,
        `${JSON.stringify(dependencyMap, null, 2)}\n`,
        "utf8",
      );
      assert.throws(
        () => verifyWebBundle({ distPath, budget }),
        new RegExp(`BUNDLE_ASYNC_CHUNK_RAW.*${dependency}.*${genericFile}`, "iu"),
      );
    });
  }
});

test("verifier 继承 collector 的 manifest 路径与 symlink 安全边界", () => {
  withGateFixture(({ directory, distPath, manifestPath }) => {
    const budget = createExactBudget(distPath);
    const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
    manifest["index.html"].file = "../outside.js";
    writeFileSync(join(directory, "outside.js"), "outside", "utf8");
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
    assert.throws(() => verifyWebBundle({ distPath, budget }), /穿越|越界|dist|\.\./iu);
  });

  withGateFixture(({ directory, distPath }) => {
    const budget = createExactBudget(distPath);
    const outside = join(directory, "outside.js");
    writeFileSync(outside, "outside", "utf8");
    unlinkSync(join(distPath, "assets/main.js"));
    symlinkSync(outside, join(distPath, "assets/main.js"));
    assert.throws(() => verifyWebBundle({ distPath, budget }), /符号链接|symlink/iu);
  });
});

test("预算 schema 拒绝未知字段、非整数和不安全匹配模式", () => {
  withGateFixture(({ distPath }) => {
    const budget = createExactBudget(distPath);
    assert.throws(
      () => validateWebBundleBudget({ ...budget, unexpected: true }),
      /未知字段/u,
    );
    assert.throws(
      () =>
        validateWebBundleBudget({
          ...budget,
          anyJsChunk: { maxRawBytes: 1.5 },
        }),
      /有限整数/u,
    );
    const unsafe = structuredClone(budget);
    unsafe.asyncChunks[0].patterns = ["../excel"];
    assert.throws(() => validateWebBundleBudget(unsafe), /patterns|格式|安全/u);
  });
});
