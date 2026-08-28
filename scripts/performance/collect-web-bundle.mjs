import { gzipSync } from "node:zlib";
import {
  appendFileSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  realpathSync,
  renameSync,
  writeFileSync,
} from "node:fs";
import { dirname, extname, isAbsolute, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import {
  ValidationError,
  assertCanonicalUtcTimestamp,
  assertEnum,
  assertExactKeys,
  assertFiniteNumber,
  assertPlainObject,
  assertSafeString,
  assertValidDate,
} from "./lib/validation.mjs";

const MANIFEST_CANDIDATES = [".vite/manifest.json", "manifest.json"];
const MAX_MANIFEST_BYTES = 4 * 1024 * 1024;
const MAX_INDEX_BYTES = 2 * 1024 * 1024;
const MAX_ASSET_BYTES = 512 * 1024 * 1024;
const MAX_MANIFEST_ENTRIES = 5_000;
const MAX_ASSETS = 5_000;
const ASSET_TYPES = ["js", "css"];

function tryLstat(filePath) {
  try {
    return lstatSync(filePath);
  } catch (error) {
    if (error?.code === "ENOENT") return null;
    throw error;
  }
}

function validateRelativePath(value, path, { allowLeadingSlash = false } = {}) {
  assertSafeString(value, path, { maxLength: 2_048 });
  let decoded;
  try {
    decoded = decodeURIComponent(value.split(/[?#]/u, 1)[0]);
  } catch {
    throw new ValidationError(`${path} 包含无效 URL 编码`);
  }
  if (/^[A-Za-z][A-Za-z0-9+.-]*:/u.test(decoded) || decoded.startsWith("//")) {
    throw new ValidationError(`${path} 必须引用 dist 内文件，不能使用外部 URL`);
  }
  if (decoded.includes("\\") || decoded.includes("\0")) {
    throw new ValidationError(`${path} 包含不安全路径字符`);
  }
  if (decoded.startsWith("/") && !allowLeadingSlash) {
    throw new ValidationError(`${path} 必须是 dist 相对路径`);
  }
  const normalized = allowLeadingSlash ? decoded.replace(/^\/+/, "") : decoded;
  const segments = normalized.split("/");
  if (
    normalized.length === 0 ||
    segments.some((segment) => segment.length === 0 || segment === "." || segment === "..")
  ) {
    throw new ValidationError(`${path} 存在路径穿越或无效路径段`);
  }
  return segments.join("/");
}

function assertDistRoot(distPath) {
  assertSafeString(distPath, "dist path", { maxLength: 4_096 });
  const absoluteRoot = resolve(distPath);
  const stat = tryLstat(absoluteRoot);
  if (!stat) throw new ValidationError("Web dist 目录不存在");
  if (stat.isSymbolicLink()) {
    throw new ValidationError("Web dist 根目录不能是符号链接（symlink）");
  }
  if (!stat.isDirectory()) throw new ValidationError("Web dist 必须是普通目录");
  return realpathSync(absoluteRoot);
}

function isOutsideRoot(relativePath) {
  return (
    relativePath === ".." ||
    relativePath.startsWith(`..${process.platform === "win32" ? "\\" : "/"}`) ||
    isAbsolute(relativePath)
  );
}

function resolveSafeDistFile(
  realRoot,
  rawPath,
  pathLabel,
  { allowLeadingSlash = false, maxBytes = MAX_ASSET_BYTES } = {},
) {
  const relativePath = validateRelativePath(rawPath, pathLabel, { allowLeadingSlash });
  const candidate = resolve(realRoot, ...relativePath.split("/"));
  const relativeCandidate = relative(realRoot, candidate);
  if (relativeCandidate.length === 0 || isOutsideRoot(relativeCandidate)) {
    throw new ValidationError(`${pathLabel} 越界，必须限制在 dist 内`);
  }

  let current = realRoot;
  const segments = relativePath.split("/");
  for (let index = 0; index < segments.length; index += 1) {
    current = join(current, segments[index]);
    const stat = tryLstat(current);
    if (!stat) throw new ValidationError(`${pathLabel} 引用文件不存在：${relativePath}`);
    if (stat.isSymbolicLink()) {
      throw new ValidationError(`${pathLabel} 不得经过符号链接（symlink）：${relativePath}`);
    }
    if (index < segments.length - 1 && !stat.isDirectory()) {
      throw new ValidationError(`${pathLabel} 的中间路径不是目录：${relativePath}`);
    }
    if (index === segments.length - 1) {
      if (!stat.isFile()) throw new ValidationError(`${pathLabel} 必须引用普通文件`);
      if (stat.size > maxBytes) {
        throw new ValidationError(`${pathLabel} 文件超过 ${maxBytes} bytes`);
      }
    }
  }

  const resolvedFile = realpathSync(candidate);
  if (isOutsideRoot(relative(realRoot, resolvedFile))) {
    throw new ValidationError(`${pathLabel} 解析后越出 dist`);
  }
  return { absolutePath: resolvedFile, relativePath };
}

function readUtf8File(file, label) {
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(readFileSync(file.absolutePath));
  } catch {
    throw new ValidationError(`${label} 不是有效 UTF-8 文件`);
  }
}

function validateStringArray(value, path) {
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.length > MAX_ASSETS) {
    throw new ValidationError(`${path} 必须是最多 ${MAX_ASSETS} 项的数组`);
  }
  return value.map((item, index) =>
    assertSafeString(item, `${path}[${index}]`, { maxLength: 2_048 }),
  );
}

function validateManifest(manifest) {
  assertPlainObject(manifest, "Vite manifest");
  const entries = Object.entries(manifest);
  if (entries.length < 1 || entries.length > MAX_MANIFEST_ENTRIES) {
    throw new ValidationError(`Vite manifest 必须包含 1 至 ${MAX_MANIFEST_ENTRIES} 项`);
  }
  const normalized = new Map();
  for (const [key, rawRecord] of entries) {
    assertSafeString(key, "Vite manifest key", { maxLength: 2_048 });
    assertPlainObject(rawRecord, `Vite manifest.${key}`);
    const file = assertSafeString(rawRecord.file, `Vite manifest.${key}.file`, {
      maxLength: 2_048,
    });
    validateRelativePath(file, `Vite manifest.${key}.file`);
    if (rawRecord.isEntry !== undefined && typeof rawRecord.isEntry !== "boolean") {
      throw new ValidationError(`Vite manifest.${key}.isEntry 必须是布尔值`);
    }
    if (
      rawRecord.isDynamicEntry !== undefined &&
      typeof rawRecord.isDynamicEntry !== "boolean"
    ) {
      throw new ValidationError(`Vite manifest.${key}.isDynamicEntry 必须是布尔值`);
    }
    const css = validateStringArray(rawRecord.css, `Vite manifest.${key}.css`);
    css.forEach((asset, index) =>
      validateRelativePath(asset, `Vite manifest.${key}.css[${index}]`),
    );
    normalized.set(key, {
      file,
      isEntry: rawRecord.isEntry === true,
      isDynamicEntry: rawRecord.isDynamicEntry === true,
      imports: validateStringArray(rawRecord.imports, `Vite manifest.${key}.imports`),
      dynamicImports: validateStringArray(
        rawRecord.dynamicImports,
        `Vite manifest.${key}.dynamicImports`,
      ),
      css,
    });
  }
  return normalized;
}

function readManifest(realRoot) {
  for (const candidate of MANIFEST_CANDIDATES) {
    if (!tryLstat(join(realRoot, ...candidate.split("/")))) continue;
    const file = resolveSafeDistFile(realRoot, candidate, "Vite manifest", {
      maxBytes: MAX_MANIFEST_BYTES,
    });
    let manifest;
    try {
      manifest = JSON.parse(readUtf8File(file, "Vite manifest"));
    } catch (error) {
      if (error instanceof ValidationError) throw error;
      throw new ValidationError("Vite manifest 不是有效 JSON");
    }
    return { manifest: validateManifest(manifest), manifestPath: candidate };
  }
  throw new ValidationError("缺少 Vite manifest（.vite/manifest.json 或 manifest.json）");
}

function parseTagAttributes(tag, tagName) {
  const body = tag
    .replace(new RegExp(`^<\\s*${tagName}\\b`, "iu"), "")
    .replace(/\/?\s*>$/u, "");
  const attributes = new Map();
  const pattern = /([^\s"'<>\/=]+)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+)))?/gu;
  for (const match of body.matchAll(pattern)) {
    attributes.set(match[1].toLowerCase(), match[2] ?? match[3] ?? match[4] ?? "");
  }
  return attributes;
}

function classifyAsset(filePath, path) {
  const extension = extname(filePath).toLowerCase();
  if (extension === ".css") return "css";
  if ([".js", ".mjs"].includes(extension)) return "js";
  throw new ValidationError(`${path} 仅允许 JS 或 CSS 资源：${filePath}`);
}

function parseIndexAssets(realRoot) {
  const indexFile = resolveSafeDistFile(realRoot, "index.html", "Vite index.html", {
    maxBytes: MAX_INDEX_BYTES,
  });
  // 注释中的 preload/script 不是浏览器初始请求，必须先剔除，避免伪造首屏依赖。
  const html = readUtf8File(indexFile, "Vite index.html").replace(/<!--[\s\S]*?-->/gu, "");
  const initialReferences = [];
  const preloadReferences = [];
  const moduleScripts = [];

  for (const match of html.matchAll(/<link\b[^>]*>/giu)) {
    const attributes = parseTagAttributes(match[0], "link");
    const rel = (attributes.get("rel") ?? "")
      .toLowerCase()
      .split(/\s+/u)
      .filter(Boolean);
    const href = attributes.get("href");
    if (!href) continue;
    if (rel.includes("modulepreload")) {
      classifyAsset(
        validateRelativePath(href, "modulepreload href", { allowLeadingSlash: true }),
        "modulepreload href",
      );
      preloadReferences.push(href);
      initialReferences.push(href);
    } else if (rel.includes("preload")) {
      const as = (attributes.get("as") ?? "").toLowerCase();
      if (as === "script" || as === "style" || /\.(?:m?js|css)(?:[?#]|$)/iu.test(href)) {
        classifyAsset(
          validateRelativePath(href, "preload href", { allowLeadingSlash: true }),
          "preload href",
        );
        preloadReferences.push(href);
        initialReferences.push(href);
      }
    } else if (rel.includes("stylesheet")) {
      initialReferences.push(href);
    }
  }

  for (const match of html.matchAll(/<script\b[^>]*>/giu)) {
    const attributes = parseTagAttributes(match[0], "script");
    const src = attributes.get("src");
    if (!src || (attributes.get("type") ?? "").toLowerCase() !== "module") continue;
    moduleScripts.push(src);
    initialReferences.push(src);
  }

  if (preloadReferences.length === 0) {
    throw new ValidationError("Vite index.html 缺少 modulepreload/preload 初始资源");
  }
  if (moduleScripts.length === 0) {
    throw new ValidationError("Vite index.html 缺少 module script 入口");
  }
  return { initialReferences, moduleScripts };
}

function requireManifestRecord(manifest, key, sourcePath) {
  const record = manifest.get(key);
  if (!record) throw new ValidationError(`${sourcePath} 引用了不存在的 manifest key：${key}`);
  return record;
}

function collectStaticManifestAssets(manifest, moduleScriptPaths) {
  const moduleScriptSet = new Set(moduleScriptPaths);
  const entryKeys = [...manifest.entries()]
    .filter(([, record]) => moduleScriptSet.has(record.file))
    .map(([key]) => key)
    .sort();
  if (entryKeys.length === 0) {
    throw new ValidationError("Vite manifest 缺少与 index.html module script 对应的 entry");
  }

  const visited = new Set();
  const pending = [...entryKeys];
  const assetPaths = new Set();
  const dynamicKeys = new Set();
  while (pending.length > 0) {
    const key = pending.shift();
    if (visited.has(key)) continue;
    visited.add(key);
    const record = requireManifestRecord(manifest, key, "静态 import");
    assetPaths.add(record.file);
    record.css.forEach((file) => assetPaths.add(file));
    record.imports.forEach((importKey) => pending.push(importKey));
    record.dynamicImports.forEach((dynamicKey) => dynamicKeys.add(dynamicKey));
  }
  return { assetPaths, dynamicKeys };
}

function measureAsset(realRoot, rawPath, pathLabel, { allowLeadingSlash = false } = {}) {
  const file = resolveSafeDistFile(realRoot, rawPath, pathLabel, { allowLeadingSlash });
  const bytes = readFileSync(file.absolutePath);
  return {
    file: file.relativePath,
    type: classifyAsset(file.relativePath, pathLabel),
    rawBytes: bytes.byteLength,
    gzipBytes: gzipSync(bytes, { level: 9 }).byteLength,
  };
}

function collectDynamicChunks(realRoot, manifest, seedKeys, initialFiles) {
  const pending = [...seedKeys].sort();
  const visited = new Set();
  const byFile = new Map();
  while (pending.length > 0) {
    const key = pending.shift();
    if (visited.has(key)) continue;
    visited.add(key);
    const record = requireManifestRecord(manifest, key, "dynamic import");
    const measured = measureAsset(realRoot, record.file, `dynamic chunk ${key}`);
    if (measured.type !== "js") {
      throw new ValidationError(`dynamic chunk ${key} 必须是 JS 文件`);
    }
    if (initialFiles.has(measured.file)) {
      // Vite 的 manualChunks 可能让同一文件既是静态 import，又保留在
      // dynamicImports 中。浏览器已通过 modulepreload 加载时，它只计入首屏；
      // 但仍需继续遍历其下游动态依赖，避免漏掉真正的路由 chunk。
      record.dynamicImports.forEach((dynamicKey) => pending.push(dynamicKey));
      continue;
    }
    const cssAssets = record.css
      .map((file) => measureAsset(realRoot, file, `dynamic chunk ${key} CSS`))
      .sort((left, right) => left.file.localeCompare(right.file));
    if (cssAssets.some((asset) => asset.type !== "css")) {
      throw new ValidationError(`dynamic chunk ${key}.css 只能引用 CSS`);
    }
    if (!byFile.has(measured.file)) {
      byFile.set(measured.file, {
        manifestKey: key,
        file: measured.file,
        rawBytes: measured.rawBytes,
        gzipBytes: measured.gzipBytes,
        cssAssets,
      });
    }
    record.dynamicImports.forEach((dynamicKey) => pending.push(dynamicKey));
  }
  return [...byFile.values()].sort((left, right) => left.file.localeCompare(right.file));
}

function validateAssetMeasurement(asset, path, { requiredType = null } = {}) {
  assertExactKeys(
    asset,
    { required: ["file", "type", "rawBytes", "gzipBytes"] },
    path,
  );
  validateRelativePath(asset.file, `${path}.file`);
  assertEnum(asset.type, ASSET_TYPES, `${path}.type`);
  if (requiredType && asset.type !== requiredType) {
    throw new ValidationError(`${path}.type 必须是 ${requiredType}`);
  }
  assertFiniteNumber(asset.rawBytes, `${path}.rawBytes`, {
    min: 0,
    max: Number.MAX_SAFE_INTEGER,
    integer: true,
  });
  assertFiniteNumber(asset.gzipBytes, `${path}.gzipBytes`, {
    min: 1,
    max: Number.MAX_SAFE_INTEGER,
    integer: true,
  });
  return asset;
}

export function validateWebBundleReport(report) {
  assertExactKeys(
    report,
    {
      required: [
        "schemaVersion",
        "generatedAtUtc",
        "manifestPath",
        "indexPath",
        "measurements",
        "initialAssets",
        "routeDynamicChunks",
      ],
    },
    "web bundle report",
  );
  if (report.schemaVersion !== "WebBundleReportV1") {
    throw new ValidationError("web bundle report.schemaVersion 必须为 WebBundleReportV1");
  }
  assertCanonicalUtcTimestamp(report.generatedAtUtc, "web bundle report.generatedAtUtc");
  validateRelativePath(report.manifestPath, "web bundle report.manifestPath");
  if (!MANIFEST_CANDIDATES.includes(report.manifestPath)) {
    throw new ValidationError("web bundle report.manifestPath 不是允许的 Vite manifest");
  }
  if (report.indexPath !== "index.html") {
    throw new ValidationError("web bundle report.indexPath 必须为 index.html");
  }
  assertExactKeys(
    report.measurements,
    {
      required: [
        "firstScreenRawBytes",
        "firstScreenGzipBytes",
        "largestInitialChunkFile",
        "largestInitialChunkRawBytes",
        "largestInitialChunkGzipBytes",
      ],
    },
    "web bundle report.measurements",
  );
  for (const key of [
    "firstScreenRawBytes",
    "firstScreenGzipBytes",
    "largestInitialChunkRawBytes",
    "largestInitialChunkGzipBytes",
  ]) {
    assertFiniteNumber(report.measurements[key], `web bundle report.measurements.${key}`, {
      min: key.includes("Gzip") ? 1 : 0,
      max: Number.MAX_SAFE_INTEGER,
      integer: true,
    });
  }
  validateRelativePath(
    report.measurements.largestInitialChunkFile,
    "web bundle report.measurements.largestInitialChunkFile",
  );
  if (
    !Array.isArray(report.initialAssets) ||
    report.initialAssets.length < 1 ||
    report.initialAssets.length > MAX_ASSETS
  ) {
    throw new ValidationError(`web bundle report.initialAssets 必须包含 1 至 ${MAX_ASSETS} 项`);
  }
  const initialFiles = new Set();
  report.initialAssets.forEach((asset, index) => {
    validateAssetMeasurement(asset, `web bundle report.initialAssets[${index}]`);
    if (initialFiles.has(asset.file)) {
      throw new ValidationError(`web bundle report.initialAssets 包含重复文件 ${asset.file}`);
    }
    initialFiles.add(asset.file);
  });
  const sortedInitialFiles = [...initialFiles].sort();
  if (report.initialAssets.some((asset, index) => asset.file !== sortedInitialFiles[index])) {
    throw new ValidationError("web bundle report.initialAssets 必须按文件名排序");
  }
  const initialJs = report.initialAssets.filter((asset) => asset.type === "js");
  if (initialJs.length === 0) throw new ValidationError("web bundle report 缺少首屏 JS");
  const rawTotal = report.initialAssets.reduce((sum, asset) => sum + asset.rawBytes, 0);
  const gzipTotal = report.initialAssets.reduce((sum, asset) => sum + asset.gzipBytes, 0);
  if (
    report.measurements.firstScreenRawBytes !== rawTotal ||
    report.measurements.firstScreenGzipBytes !== gzipTotal
  ) {
    throw new ValidationError("web bundle report 首屏总量与 initialAssets 不一致");
  }
  const largest = [...initialJs].sort(
    (left, right) => right.gzipBytes - left.gzipBytes || left.file.localeCompare(right.file),
  )[0];
  if (
    report.measurements.largestInitialChunkFile !== largest.file ||
    report.measurements.largestInitialChunkRawBytes !== largest.rawBytes ||
    report.measurements.largestInitialChunkGzipBytes !== largest.gzipBytes
  ) {
    throw new ValidationError("web bundle report 最大初始 chunk 与 initialAssets 不一致");
  }

  if (!Array.isArray(report.routeDynamicChunks) || report.routeDynamicChunks.length > MAX_ASSETS) {
    throw new ValidationError(`web bundle report.routeDynamicChunks 最多允许 ${MAX_ASSETS} 项`);
  }
  const routeFiles = new Set();
  report.routeDynamicChunks.forEach((chunk, index) => {
    const path = `web bundle report.routeDynamicChunks[${index}]`;
    assertExactKeys(
      chunk,
      { required: ["manifestKey", "file", "rawBytes", "gzipBytes", "cssAssets"] },
      path,
    );
    assertSafeString(chunk.manifestKey, `${path}.manifestKey`, { maxLength: 2_048 });
    validateRelativePath(chunk.file, `${path}.file`);
    if (classifyAsset(chunk.file, `${path}.file`) !== "js") {
      throw new ValidationError(`${path}.file 必须是 JS chunk`);
    }
    assertFiniteNumber(chunk.rawBytes, `${path}.rawBytes`, {
      min: 0,
      max: Number.MAX_SAFE_INTEGER,
      integer: true,
    });
    assertFiniteNumber(chunk.gzipBytes, `${path}.gzipBytes`, {
      min: 1,
      max: Number.MAX_SAFE_INTEGER,
      integer: true,
    });
    if (initialFiles.has(chunk.file)) {
      throw new ValidationError(`${path}.file 不能与首屏资源重复`);
    }
    if (routeFiles.has(chunk.file)) {
      throw new ValidationError(`web bundle report.routeDynamicChunks 包含重复文件 ${chunk.file}`);
    }
    routeFiles.add(chunk.file);
    if (!Array.isArray(chunk.cssAssets) || chunk.cssAssets.length > MAX_ASSETS) {
      throw new ValidationError(`${path}.cssAssets 必须是受限数组`);
    }
    const cssFiles = new Set();
    chunk.cssAssets.forEach((asset, cssIndex) => {
      validateAssetMeasurement(asset, `${path}.cssAssets[${cssIndex}]`, {
        requiredType: "css",
      });
      if (cssFiles.has(asset.file)) throw new ValidationError(`${path}.cssAssets 包含重复文件`);
      cssFiles.add(asset.file);
    });
    const sortedCssFiles = [...cssFiles].sort();
    if (chunk.cssAssets.some((asset, cssIndex) => asset.file !== sortedCssFiles[cssIndex])) {
      throw new ValidationError(`${path}.cssAssets 必须按文件名排序`);
    }
  });
  const sortedRouteFiles = [...routeFiles].sort();
  if (report.routeDynamicChunks.some((chunk, index) => chunk.file !== sortedRouteFiles[index])) {
    throw new ValidationError("web bundle report.routeDynamicChunks 必须按文件名排序");
  }
  return report;
}

export function analyzeWebBundle(distPath, { now = new Date() } = {}) {
  assertValidDate(now, "now");
  const realRoot = assertDistRoot(distPath);
  const { manifest, manifestPath } = readManifest(realRoot);
  const { initialReferences, moduleScripts } = parseIndexAssets(realRoot);
  const manifestFiles = new Set([...manifest.values()].map((record) => record.file));
  const moduleScriptPaths = [];
  for (const moduleScript of moduleScripts) {
    const scriptPath = validateRelativePath(moduleScript, "module script src", {
      allowLeadingSlash: true,
    });
    if (!manifestFiles.has(scriptPath)) {
      throw new ValidationError(`module script 未出现在 Vite manifest：${scriptPath}`);
    }
    moduleScriptPaths.push(scriptPath);
  }
  const { assetPaths, dynamicKeys } = collectStaticManifestAssets(
    manifest,
    moduleScriptPaths,
  );
  initialReferences.forEach((file) => assetPaths.add(file));

  const byFile = new Map();
  for (const rawPath of assetPaths) {
    const measured = measureAsset(realRoot, rawPath, "首屏资源", {
      allowLeadingSlash: rawPath.startsWith("/"),
    });
    byFile.set(measured.file, measured);
  }
  const initialAssets = [...byFile.values()].sort((left, right) =>
    left.file.localeCompare(right.file),
  );
  const initialFiles = new Set(initialAssets.map((asset) => asset.file));
  const initialJs = initialAssets.filter((asset) => asset.type === "js");
  if (initialJs.length === 0) throw new ValidationError("首屏资源没有 JS chunk");
  const largestInitialChunk = [...initialJs].sort(
    (left, right) => right.gzipBytes - left.gzipBytes || left.file.localeCompare(right.file),
  )[0];

  const report = {
    schemaVersion: "WebBundleReportV1",
    generatedAtUtc: now.toISOString(),
    manifestPath,
    indexPath: "index.html",
    measurements: {
      firstScreenRawBytes: initialAssets.reduce((sum, asset) => sum + asset.rawBytes, 0),
      firstScreenGzipBytes: initialAssets.reduce((sum, asset) => sum + asset.gzipBytes, 0),
      largestInitialChunkFile: largestInitialChunk.file,
      largestInitialChunkRawBytes: largestInitialChunk.rawBytes,
      largestInitialChunkGzipBytes: largestInitialChunk.gzipBytes,
    },
    initialAssets,
    routeDynamicChunks: collectDynamicChunks(
      realRoot,
      manifest,
      dynamicKeys,
      initialFiles,
    ),
  };
  return validateWebBundleReport(report);
}

function writeJsonAtomic(filePath, value) {
  mkdirSync(dirname(filePath), { recursive: true });
  const temporaryPath = `${filePath}.${process.pid}.tmp`;
  writeFileSync(temporaryPath, `${JSON.stringify(value, null, 2)}\n`, {
    encoding: "utf8",
    mode: 0o600,
  });
  renameSync(temporaryPath, filePath);
}

function appendSummary(summaryPath, report) {
  if (!summaryPath) return;
  const routeRows = report.routeDynamicChunks.length
    ? report.routeDynamicChunks
        .map((chunk) => `| \`${chunk.file}\` | ${chunk.rawBytes} | ${chunk.gzipBytes} |`)
        .join("\n")
    : "| — | — | — |";
  appendFileSync(
    summaryPath,
    `\n## Web bundle baseline\n\n- 首屏 JS+CSS raw：\`${report.measurements.firstScreenRawBytes} bytes\`\n- 首屏 JS+CSS gzip：\`${report.measurements.firstScreenGzipBytes} bytes\`\n- 最大初始 JS chunk gzip：\`${report.measurements.largestInitialChunkGzipBytes} bytes\`（\`${report.measurements.largestInitialChunkFile}\`）\n\n| 路由动态 chunk | Raw bytes | Gzip bytes |\n| --- | ---: | ---: |\n${routeRows}\n`,
    "utf8",
  );
}

export function collectWebBundle({ distPath, outputPath, summaryPath = null, now = new Date() }) {
  assertSafeString(outputPath, "output path", { maxLength: 4_096 });
  if (summaryPath !== null) assertSafeString(summaryPath, "summary path", { maxLength: 4_096 });
  const report = analyzeWebBundle(distPath, { now });
  writeJsonAtomic(resolve(outputPath), report);
  appendSummary(summaryPath ? resolve(summaryPath) : null, report);
  return report;
}

function parseArgs(argv) {
  const options = {};
  const valueOptions = new Map([
    ["--dist", "distPath"],
    ["--output", "outputPath"],
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
  if (!options.help && (!options.distPath || !options.outputPath)) {
    throw new ValidationError("必须提供 --dist 和 --output");
  }
  return options;
}

function main(argv = process.argv.slice(2), env = process.env) {
  const options = parseArgs(argv);
  if (options.help) {
    console.log(
      "用法: node scripts/performance/collect-web-bundle.mjs --dist <dist> --output <report.json> [--summary <markdown>]",
    );
    return;
  }
  const report = collectWebBundle({
    ...options,
    summaryPath: options.summaryPath ?? env.GITHUB_STEP_SUMMARY ?? null,
  });
  console.log(
    `Web bundle 已采集：首屏 gzip ${report.measurements.firstScreenGzipBytes} bytes，最大初始 chunk gzip ${report.measurements.largestInitialChunkGzipBytes} bytes，动态 chunk ${report.routeDynamicChunks.length} 个`,
  );
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  try {
    main();
  } catch (error) {
    console.error(`Web bundle 采集失败：${error.message}`);
    process.exitCode = 1;
  }
}
