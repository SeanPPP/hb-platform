import { gzipSync } from "node:zlib";
import {
  appendFileSync,
  lstatSync,
  readFileSync,
  realpathSync,
} from "node:fs";
import { isAbsolute, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { analyzeWebBundle } from "./collect-web-bundle.mjs";
import {
  ValidationError,
  assertExactKeys,
  assertFiniteNumber,
  assertPlainObject,
  assertSafeString,
  assertValidDate,
} from "./lib/validation.mjs";

const MAX_BUDGET_BYTES = 1024 * 1024;
const MAX_MANIFEST_BYTES = 4 * 1024 * 1024;
const MAX_DEPENDENCY_MAP_BYTES = 4 * 1024 * 1024;
const MAX_ASSET_BYTES = 512 * 1024 * 1024;
const MAX_LIST_ITEMS = 5_000;
const SAFE_IDENTIFIER_PATTERN = /^[a-z0-9][a-z0-9._-]*$/iu;
const SAFE_MATCH_PATTERN = /^[a-z0-9@][a-z0-9@._/-]*$/iu;
const BUNDLE_DEPENDENCY_MAP_PATH = ".vite/bundle-dependencies.json";
const BUNDLE_DEPENDENCY_IDS = new Set(["excel", "pdf", "leaflet", "zxing"]);

export class BundleGateError extends Error {
  constructor(violations) {
    super(
      `Web bundle 硬门禁失败：\n${violations
        .map((violation) => `- [${violation.code}] ${violation.message}`)
        .join("\n")}`,
    );
    this.name = "BundleGateError";
    this.violations = violations;
  }
}

function tryLstat(filePath) {
  try {
    return lstatSync(filePath);
  } catch (error) {
    if (error?.code === "ENOENT") return null;
    throw error;
  }
}

function validatePositiveBytes(value, path) {
  return assertFiniteNumber(value, path, {
    min: 1,
    max: Number.MAX_SAFE_INTEGER,
    integer: true,
  });
}

function validateIdentifier(value, path) {
  return assertSafeString(value, path, {
    maxLength: 128,
    pattern: SAFE_IDENTIFIER_PATTERN,
  });
}

function validateManifestKey(value, path) {
  assertSafeString(value, path, { maxLength: 2_048 });
  if (
    value.includes("\\") ||
    value.includes("\0") ||
    value.startsWith("/") ||
    value.split("/").some((segment) => segment === "" || segment === "." || segment === "..")
  ) {
    throw new ValidationError(`${path} 不是安全的 Vite manifest key`);
  }
  return value;
}

function validatePattern(value, path) {
  assertSafeString(value, path, {
    maxLength: 128,
    pattern: SAFE_MATCH_PATTERN,
  });
  if (value.split("/").some((segment) => segment === "." || segment === "..")) {
    throw new ValidationError(`${path} 不是安全匹配模式`);
  }
  return value.toLowerCase();
}

function validateBoundedArray(value, path, { minLength = 1 } = {}) {
  if (!Array.isArray(value) || value.length < minLength || value.length > MAX_LIST_ITEMS) {
    throw new ValidationError(`${path} 必须是 ${minLength} 至 ${MAX_LIST_ITEMS} 项的数组`);
  }
  return value;
}

export function validateWebBundleBudget(budget) {
  assertExactKeys(
    budget,
    {
      required: [
        "schemaVersion",
        "mainEntry",
        "firstScreen",
        "largestInitialJs",
        "anyJsChunk",
        "asyncChunks",
        "forbiddenInitialPatterns",
        "requiredDynamicEntries",
      ],
    },
    "web bundle budget",
  );
  if (budget.schemaVersion !== "WebBundleBudgetV1") {
    throw new ValidationError("web bundle budget.schemaVersion 必须为 WebBundleBudgetV1");
  }

  assertExactKeys(
    budget.mainEntry,
    { required: ["manifestKey", "maxRawBytes", "maxGzipBytes"] },
    "web bundle budget.mainEntry",
  );
  validateManifestKey(budget.mainEntry.manifestKey, "web bundle budget.mainEntry.manifestKey");
  validatePositiveBytes(budget.mainEntry.maxRawBytes, "web bundle budget.mainEntry.maxRawBytes");
  validatePositiveBytes(budget.mainEntry.maxGzipBytes, "web bundle budget.mainEntry.maxGzipBytes");

  for (const [key, path] of [
    ["firstScreen", "web bundle budget.firstScreen"],
    ["largestInitialJs", "web bundle budget.largestInitialJs"],
  ]) {
    assertExactKeys(budget[key], { required: ["maxGzipBytes"] }, path);
    validatePositiveBytes(budget[key].maxGzipBytes, `${path}.maxGzipBytes`);
  }
  assertExactKeys(
    budget.anyJsChunk,
    { required: ["maxRawBytes"] },
    "web bundle budget.anyJsChunk",
  );
  validatePositiveBytes(budget.anyJsChunk.maxRawBytes, "web bundle budget.anyJsChunk.maxRawBytes");

  const asyncIds = new Set();
  validateBoundedArray(budget.asyncChunks, "web bundle budget.asyncChunks").forEach(
    (chunk, index) => {
      const path = `web bundle budget.asyncChunks[${index}]`;
      assertExactKeys(
        chunk,
        { required: ["id", "patterns", "maxRawBytes", "maxGzipBytes"] },
        path,
      );
      validateIdentifier(chunk.id, `${path}.id`);
      if (asyncIds.has(chunk.id)) {
        throw new ValidationError(`web bundle budget.asyncChunks 包含重复 id ${chunk.id}`);
      }
      asyncIds.add(chunk.id);
      chunk.patterns = validateBoundedArray(chunk.patterns, `${path}.patterns`).map(
        (pattern, patternIndex) => validatePattern(pattern, `${path}.patterns[${patternIndex}]`),
      );
      validatePositiveBytes(chunk.maxRawBytes, `${path}.maxRawBytes`);
      validatePositiveBytes(chunk.maxGzipBytes, `${path}.maxGzipBytes`);
    },
  );

  budget.forbiddenInitialPatterns = validateBoundedArray(
    budget.forbiddenInitialPatterns,
    "web bundle budget.forbiddenInitialPatterns",
  ).map((pattern, index) =>
    validatePattern(pattern, `web bundle budget.forbiddenInitialPatterns[${index}]`),
  );

  const dynamicIds = new Set();
  const dynamicKeys = new Set();
  validateBoundedArray(
    budget.requiredDynamicEntries,
    "web bundle budget.requiredDynamicEntries",
  ).forEach((entry, index) => {
    const path = `web bundle budget.requiredDynamicEntries[${index}]`;
    assertExactKeys(entry, { required: ["id", "manifestKey"] }, path);
    validateIdentifier(entry.id, `${path}.id`);
    validateManifestKey(entry.manifestKey, `${path}.manifestKey`);
    if (dynamicIds.has(entry.id) || dynamicKeys.has(entry.manifestKey)) {
      throw new ValidationError(
        "web bundle budget.requiredDynamicEntries 的 id 与 manifestKey 必须唯一",
      );
    }
    dynamicIds.add(entry.id);
    dynamicKeys.add(entry.manifestKey);
  });
  return budget;
}

function readJsonFile(filePath, path, maxBytes) {
  assertSafeString(filePath, path, { maxLength: 4_096 });
  const absolutePath = resolve(filePath);
  const stat = tryLstat(absolutePath);
  if (!stat) throw new ValidationError(`${path} 不存在`);
  if (stat.isSymbolicLink()) throw new ValidationError(`${path} 不能是符号链接（symlink）`);
  if (!stat.isFile()) throw new ValidationError(`${path} 必须是普通文件`);
  if (stat.size > maxBytes) throw new ValidationError(`${path} 超过 ${maxBytes} bytes`);
  try {
    return JSON.parse(readFileSync(absolutePath, "utf8"));
  } catch (error) {
    if (error instanceof ValidationError) throw error;
    throw new ValidationError(`${path} 不是有效 JSON`);
  }
}

export function readWebBundleBudget(budgetPath) {
  return validateWebBundleBudget(
    readJsonFile(budgetPath, "web bundle budget 文件", MAX_BUDGET_BYTES),
  );
}

function normalizeManifestPath(value, path) {
  assertSafeString(value, path, { maxLength: 2_048 });
  if (
    /^[A-Za-z][A-Za-z0-9+.-]*:/u.test(value) ||
    value.startsWith("/") ||
    value.startsWith("//") ||
    value.includes("\\") ||
    value.includes("\0") ||
    value.split("/").some((segment) => segment === "" || segment === "." || segment === "..")
  ) {
    throw new ValidationError(`${path} 必须是安全的 dist 相对路径`);
  }
  return value;
}

function normalizeStringArray(value, path) {
  if (value === undefined) return [];
  return validateBoundedArray(value, path, { minLength: 0 }).map((item, index) =>
    validateManifestKey(item, `${path}[${index}]`),
  );
}

function readManifestGraph(realRoot, manifestPath) {
  const manifest = readJsonFile(
    resolve(realRoot, ...manifestPath.split("/")),
    "Vite manifest",
    MAX_MANIFEST_BYTES,
  );
  assertPlainObject(manifest, "Vite manifest");
  const entries = Object.entries(manifest);
  if (entries.length < 1 || entries.length > MAX_LIST_ITEMS) {
    throw new ValidationError(`Vite manifest 必须包含 1 至 ${MAX_LIST_ITEMS} 项`);
  }
  const graph = new Map();
  for (const [key, rawRecord] of entries) {
    validateManifestKey(key, "Vite manifest key");
    assertPlainObject(rawRecord, `Vite manifest.${key}`);
    const file = normalizeManifestPath(rawRecord.file, `Vite manifest.${key}.file`);
    const optionalText = {};
    for (const field of ["name", "src"]) {
      if (rawRecord[field] !== undefined) {
        optionalText[field] = assertSafeString(rawRecord[field], `Vite manifest.${key}.${field}`, {
          maxLength: 2_048,
        });
      }
    }
    if (rawRecord.isEntry !== undefined && typeof rawRecord.isEntry !== "boolean") {
      throw new ValidationError(`Vite manifest.${key}.isEntry 必须是布尔值`);
    }
    if (rawRecord.isDynamicEntry !== undefined && typeof rawRecord.isDynamicEntry !== "boolean") {
      throw new ValidationError(`Vite manifest.${key}.isDynamicEntry 必须是布尔值`);
    }
    graph.set(key, {
      key,
      file,
      imports: normalizeStringArray(rawRecord.imports, `Vite manifest.${key}.imports`),
      dynamicImports: normalizeStringArray(
        rawRecord.dynamicImports,
        `Vite manifest.${key}.dynamicImports`,
      ),
      isEntry: rawRecord.isEntry === true,
      isDynamicEntry: rawRecord.isDynamicEntry === true,
      ...optionalText,
    });
  }
  return graph;
}

function requireRecord(graph, key, source) {
  const record = graph.get(key);
  if (!record) {
    throw new ValidationError(`${source} 引用了不存在的 Vite manifest key：${key}`);
  }
  return record;
}

function collectClosure(graph, seedKeys, fields) {
  const visited = new Set();
  const pending = [...seedKeys].sort();
  while (pending.length > 0) {
    const key = pending.shift();
    if (visited.has(key)) continue;
    visited.add(key);
    const record = requireRecord(graph, key, "bundle 依赖图");
    for (const field of fields) {
      record[field].forEach((dependency) => pending.push(dependency));
    }
  }
  return visited;
}

function collectAsyncClosure(graph, staticKeys) {
  const seeds = new Set();
  for (const key of staticKeys) {
    requireRecord(graph, key, "首屏静态闭包").dynamicImports.forEach((dependency) =>
      seeds.add(dependency),
    );
  }
  return collectClosure(graph, seeds, ["imports", "dynamicImports"]);
}

function resolveSafeAsset(realRoot, rawPath) {
  const safePath = normalizeManifestPath(rawPath, "Vite chunk file");
  const candidate = resolve(realRoot, ...safePath.split("/"));
  const candidateRelative = relative(realRoot, candidate);
  if (
    candidateRelative === ".." ||
    candidateRelative.startsWith(`..${process.platform === "win32" ? "\\" : "/"}`) ||
    isAbsolute(candidateRelative)
  ) {
    throw new ValidationError(`Vite chunk file 越界：${safePath}`);
  }
  let current = realRoot;
  for (const segment of safePath.split("/")) {
    current = resolve(current, segment);
    const stat = tryLstat(current);
    if (!stat) throw new ValidationError(`Vite chunk file 不存在：${safePath}`);
    if (stat.isSymbolicLink()) {
      throw new ValidationError(`Vite chunk file 不得经过符号链接（symlink）：${safePath}`);
    }
  }
  const stat = lstatSync(candidate);
  if (!stat.isFile()) throw new ValidationError(`Vite chunk file 必须是普通文件：${safePath}`);
  if (stat.size > MAX_ASSET_BYTES) {
    throw new ValidationError(`Vite chunk file 超过 ${MAX_ASSET_BYTES} bytes：${safePath}`);
  }
  return candidate;
}

function measureJsChunks(realRoot, graph) {
  const byFile = new Map();
  for (const record of graph.values()) {
    if (!/\.(?:m?js)$/iu.test(record.file)) continue;
    if (!byFile.has(record.file)) {
      const bytes = readFileSync(resolveSafeAsset(realRoot, record.file));
      byFile.set(record.file, {
        file: record.file,
        rawBytes: bytes.byteLength,
        gzipBytes: gzipSync(bytes, { level: 9 }).byteLength,
        manifestKeys: [],
      });
    }
    byFile.get(record.file).manifestKeys.push(record.key);
  }
  return [...byFile.values()]
    .map((chunk) => ({ ...chunk, manifestKeys: chunk.manifestKeys.sort() }))
    .sort((left, right) => left.file.localeCompare(right.file));
}

function readBundleDependencyMap(realRoot, jsChunks) {
  const dependencyMapPath = resolveSafeAsset(realRoot, BUNDLE_DEPENDENCY_MAP_PATH);
  const dependencyMapStat = lstatSync(dependencyMapPath);
  if (dependencyMapStat.size > MAX_DEPENDENCY_MAP_BYTES) {
    throw new ValidationError(
      `Web bundle dependency map 超过 ${MAX_DEPENDENCY_MAP_BYTES} bytes`,
    );
  }

  let rawMap;
  try {
    rawMap = JSON.parse(readFileSync(dependencyMapPath, "utf8"));
  } catch {
    throw new ValidationError("Web bundle dependency map 不是有效 JSON");
  }
  assertExactKeys(
    rawMap,
    { required: ["schemaVersion", "chunks"] },
    "Web bundle dependency map",
  );
  if (rawMap.schemaVersion !== "WebBundleDependencyMapV1") {
    throw new ValidationError(
      "Web bundle dependency map.schemaVersion 必须为 WebBundleDependencyMapV1",
    );
  }
  assertPlainObject(rawMap.chunks, "Web bundle dependency map.chunks");

  const dependenciesByFile = new Map();
  for (const [rawFile, rawDependencies] of Object.entries(rawMap.chunks)) {
    const file = normalizeManifestPath(rawFile, "Web bundle dependency map chunk");
    const dependencies = validateBoundedArray(
      rawDependencies,
      `Web bundle dependency map.chunks.${file}`,
      { minLength: 0 },
    ).map((dependency, index) => {
      const id = validateIdentifier(
        dependency,
        `Web bundle dependency map.chunks.${file}[${index}]`,
      );
      if (!BUNDLE_DEPENDENCY_IDS.has(id)) {
        throw new ValidationError(`Web bundle dependency map 包含未知依赖分类 ${id}`);
      }
      return id;
    });
    if (new Set(dependencies).size !== dependencies.length) {
      throw new ValidationError(`Web bundle dependency map chunk ${file} 包含重复依赖分类`);
    }
    dependenciesByFile.set(file, dependencies);
  }

  const manifestJsFiles = new Set(jsChunks.map((chunk) => chunk.file));
  const missingFiles = [...manifestJsFiles].filter((file) => !dependenciesByFile.has(file));
  const extraFiles = [...dependenciesByFile.keys()].filter((file) => !manifestJsFiles.has(file));
  if (missingFiles.length > 0 || extraFiles.length > 0) {
    throw new ValidationError(
      `Web bundle dependency map 与 manifest JS 不一致：missing=${missingFiles.join(",") || "-"}; extra=${extraFiles.join(",") || "-"}`,
    );
  }
  return dependenciesByFile;
}

function recordDescriptor(record) {
  return [record.key, record.file, record.name ?? "", record.src ?? ""]
    .join(" ")
    .toLowerCase();
}

function matchesPatterns(record, patterns) {
  const descriptor = recordDescriptor(record);
  return patterns.some((pattern) => descriptor.includes(pattern));
}

function addLimitViolation(violations, code, label, actual, maximum, unit = "bytes") {
  if (actual <= maximum) return;
  violations.push({
    code,
    message: `${label} ${actual} ${unit}，上限 ${maximum} ${unit}，超出 ${actual - maximum} ${unit}`,
  });
}

export function verifyWebBundle({ distPath, budget, now = new Date() }) {
  assertValidDate(now, "now");
  validateWebBundleBudget(budget);
  const report = analyzeWebBundle(distPath, { now });
  const realRoot = realpathSync(resolve(distPath));
  const graph = readManifestGraph(realRoot, report.manifestPath);
  const mainRecord = requireRecord(graph, budget.mainEntry.manifestKey, "web bundle budget.mainEntry");
  if (!mainRecord.isEntry) {
    throw new ValidationError(
      `主入口 ${budget.mainEntry.manifestKey} 必须在 Vite manifest 标记 isEntry=true`,
    );
  }
  const staticKeys = collectClosure(graph, [mainRecord.key], ["imports"]);
  const asyncKeys = collectAsyncClosure(graph, staticKeys);
  const initialFiles = new Set(report.initialAssets.map((asset) => asset.file));
  const asyncFiles = new Set([...asyncKeys].map((key) => graph.get(key).file));
  const jsChunks = measureJsChunks(realRoot, graph);
  const dependenciesByFile = readBundleDependencyMap(realRoot, jsChunks);
  const jsByFile = new Map(jsChunks.map((chunk) => [chunk.file, chunk]));
  const mainChunk = jsByFile.get(mainRecord.file);
  if (!mainChunk) throw new ValidationError(`主入口不是 JS chunk：${mainRecord.file}`);

  const violations = [];

  for (const pattern of budget.forbiddenInitialPatterns) {
    const reportedFiles = new Set();
    for (const file of initialFiles) {
      const dependencies = dependenciesByFile.get(file) ?? [];
      if (!dependencies.some((dependency) => dependency.includes(pattern))) continue;
      reportedFiles.add(file);
      violations.push({
        code: "BUNDLE_FORBIDDEN_INITIAL",
        message: `首屏静态闭包包含禁用依赖 ${pattern}：${file}`,
      });
    }
    const matches = [...graph.values()].filter(
      (record) => initialFiles.has(record.file) && matchesPatterns(record, [pattern]),
    );
    for (const record of matches) {
      if (reportedFiles.has(record.file)) continue;
      violations.push({
        code: "BUNDLE_FORBIDDEN_INITIAL",
        message: `首屏静态闭包包含禁用依赖 ${pattern}：${record.key} -> ${record.file}`,
      });
    }
  }

  for (const entry of budget.requiredDynamicEntries) {
    const record = graph.get(entry.manifestKey);
    if (
      !record ||
      !record.isDynamicEntry ||
      !asyncKeys.has(entry.manifestKey) ||
      initialFiles.has(record.file)
    ) {
      violations.push({
        code: "BUNDLE_REQUIRED_DYNAMIC_ENTRY",
        message: `缺少页面级动态入口 ${entry.id}：${entry.manifestKey}`,
      });
    }
  }

  const asyncMeasurements = [];
  for (const chunkBudget of budget.asyncChunks) {
    const descriptorRecords = [...graph.values()].filter((record) =>
      matchesPatterns(record, chunkBudget.patterns),
    );
    const descriptorFiles = new Set(descriptorRecords.map((record) => record.file));
    const classifiedFiles = new Set(
      [...dependenciesByFile.entries()]
        .filter(([, dependencies]) => dependencies.includes(chunkBudget.id))
        .map(([file]) => file),
    );
    const matchingFiles = [...classifiedFiles].sort();

    for (const file of new Set([...descriptorFiles, ...classifiedFiles])) {
      if (descriptorFiles.has(file) === classifiedFiles.has(file)) continue;
      violations.push({
        code: "BUNDLE_ASYNC_CHUNK_METADATA_MISMATCH",
        message: `异步资源 ${chunkBudget.id} 的文件名与依赖分类不一致：${file}`,
      });
    }
    if (matchingFiles.length === 0) {
      violations.push({
        code: "BUNDLE_ASYNC_CHUNK_MISSING",
        message: `未找到异步资源 ${chunkBudget.id}`,
      });
      continue;
    }
    for (const file of matchingFiles) {
      const measurement = jsByFile.get(file);
      if (!measurement) {
        violations.push({
          code: "BUNDLE_ASYNC_CHUNK_TYPE",
          message: `异步资源 ${chunkBudget.id} 不是 JS chunk：${file}`,
        });
        continue;
      }
      if (initialFiles.has(file) || !asyncFiles.has(file)) {
        violations.push({
          code: "BUNDLE_ASYNC_CHUNK_SYNC",
          message: `资源 ${chunkBudget.id} 必须只在异步闭包：${file}`,
        });
      }
      addLimitViolation(
        violations,
        "BUNDLE_ASYNC_CHUNK_RAW",
        `${chunkBudget.id} chunk ${file} raw`,
        measurement.rawBytes,
        chunkBudget.maxRawBytes,
      );
      addLimitViolation(
        violations,
        "BUNDLE_ASYNC_CHUNK_GZIP",
        `${chunkBudget.id} chunk ${file} gzip`,
        measurement.gzipBytes,
        chunkBudget.maxGzipBytes,
      );
      asyncMeasurements.push({ id: chunkBudget.id, ...measurement });
    }
  }

  addLimitViolation(
    violations,
    "BUNDLE_MAIN_RAW",
    `主入口 ${mainRecord.file} raw`,
    mainChunk.rawBytes,
    budget.mainEntry.maxRawBytes,
  );
  addLimitViolation(
    violations,
    "BUNDLE_MAIN_GZIP",
    `主入口 ${mainRecord.file} gzip`,
    mainChunk.gzipBytes,
    budget.mainEntry.maxGzipBytes,
  );
  addLimitViolation(
    violations,
    "BUNDLE_FIRST_SCREEN_GZIP",
    "首屏 JS+CSS gzip",
    report.measurements.firstScreenGzipBytes,
    budget.firstScreen.maxGzipBytes,
  );
  addLimitViolation(
    violations,
    "BUNDLE_LARGEST_INITIAL_JS_GZIP",
    `最大初始 JS ${report.measurements.largestInitialChunkFile} gzip`,
    report.measurements.largestInitialChunkGzipBytes,
    budget.largestInitialJs.maxGzipBytes,
  );
  for (const chunk of jsChunks) {
    addLimitViolation(
      violations,
      "BUNDLE_ANY_JS_RAW",
      `JS chunk ${chunk.file} raw`,
      chunk.rawBytes,
      budget.anyJsChunk.maxRawBytes,
    );
  }

  if (violations.length > 0) throw new BundleGateError(violations);

  return {
    schemaVersion: "WebBundleVerificationV1",
    generatedAtUtc: now.toISOString(),
    conclusion: "accepted",
    violations: [],
    measurements: {
      mainEntryFile: mainChunk.file,
      mainEntryRawBytes: mainChunk.rawBytes,
      mainEntryGzipBytes: mainChunk.gzipBytes,
      firstScreenGzipBytes: report.measurements.firstScreenGzipBytes,
      largestInitialJsFile: report.measurements.largestInitialChunkFile,
      largestInitialJsGzipBytes: report.measurements.largestInitialChunkGzipBytes,
      largestJsRawBytes: Math.max(...jsChunks.map((chunk) => chunk.rawBytes)),
    },
    asyncChunks: asyncMeasurements.sort(
      (left, right) => left.id.localeCompare(right.id) || left.file.localeCompare(right.file),
    ),
    requiredDynamicEntries: budget.requiredDynamicEntries.map((entry) => entry.id),
  };
}

function appendSummary(summaryPath, result) {
  if (!summaryPath) return;
  appendFileSync(
    resolve(summaryPath),
    `\n## Web bundle 硬门禁\n\n- 结论：\`${result.conclusion}\`\n- 主入口：\`${result.measurements.mainEntryFile}\`，raw \`${result.measurements.mainEntryRawBytes} bytes\` / gzip \`${result.measurements.mainEntryGzipBytes} bytes\`\n- 首屏 JS+CSS gzip：\`${result.measurements.firstScreenGzipBytes} bytes\`\n- 最大初始 JS gzip：\`${result.measurements.largestInitialJsGzipBytes} bytes\`（\`${result.measurements.largestInitialJsFile}\`）\n- 任一 JS 最大 raw：\`${result.measurements.largestJsRawBytes} bytes\`\n`,
    "utf8",
  );
}

function parseArgs(argv) {
  const options = {};
  const valueOptions = new Map([
    ["--dist", "distPath"],
    ["--budget", "budgetPath"],
    ["--summary", "summaryPath"],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help") {
      options.help = true;
    } else if (valueOptions.has(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) throw new ValidationError(`${argument} 缺少值`);
      options[valueOptions.get(argument)] = value;
    } else {
      throw new ValidationError(`未知参数 ${argument}`);
    }
  }
  if (!options.help && (!options.distPath || !options.budgetPath)) {
    throw new ValidationError("必须提供 --dist 和 --budget");
  }
  return options;
}

function main(argv = process.argv.slice(2), env = process.env) {
  const options = parseArgs(argv);
  if (options.help) {
    console.log(
      "用法: node scripts/performance/verify-web-bundle.mjs --dist <dist> --budget <budget.json> [--summary <markdown>]",
    );
    return;
  }
  const result = verifyWebBundle({
    distPath: options.distPath,
    budget: readWebBundleBudget(options.budgetPath),
  });
  appendSummary(options.summaryPath ?? env.GITHUB_STEP_SUMMARY ?? null, result);
  console.log(
    `Web bundle 硬门禁通过：主入口 gzip ${result.measurements.mainEntryGzipBytes} bytes，首屏 gzip ${result.measurements.firstScreenGzipBytes} bytes，最大初始 JS gzip ${result.measurements.largestInitialJsGzipBytes} bytes`,
  );
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  try {
    main();
  } catch (error) {
    console.error(
      error instanceof BundleGateError
        ? error.message
        : `Web bundle 硬门禁失败：${error.message}`,
    );
    process.exitCode = 1;
  }
}
