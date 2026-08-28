import { appendFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";

import { ValidationError, assertCommitSha, assertSafeString } from "./lib/validation.mjs";

export const QUALITY_LANES = Object.freeze([
  "backend",
  "web",
  "pos-ipad",
  "pos-handheld",
]);

const SHARED_PATHS = new Set([
  ".github/workflows/quality-baseline.yml",
  "quality-baseline-budget.json",
  "scripts/test-all.sh",
]);

const POS_SHARED_ROOT_PATHS = new Set([
  "package.json",
  "package-lock.json",
  "eslint.config.mjs",
  "tsconfig.pos-packages.json",
]);

function normalizeChangedPath(filePath) {
  assertSafeString(filePath, "changed path", { maxLength: 1024 });
  if (filePath.startsWith("/") || filePath.includes("\\") || filePath.split("/").includes("..")) {
    throw new ValidationError(`changed path 不是仓库相对 POSIX 路径：${filePath}`);
  }
  return filePath.replace(/^\.\//u, "");
}

export function selectLanesForPaths(paths) {
  if (!Array.isArray(paths)) throw new ValidationError("paths 必须是数组");
  const selected = new Set();
  for (const rawPath of paths) {
    if (typeof rawPath !== "string" || rawPath.trim().length === 0) continue;
    const filePath = normalizeChangedPath(rawPath);
    if (SHARED_PATHS.has(filePath) || filePath.startsWith("scripts/performance/")) {
      return [...QUALITY_LANES];
    }
    if (
      POS_SHARED_ROOT_PATHS.has(filePath) ||
      filePath.startsWith("packages/pos-") ||
      filePath.startsWith("scripts/pos-shared/") ||
      filePath.startsWith("patches/")
    ) {
      selected.add("pos-ipad");
      selected.add("pos-handheld");
    }
    if (filePath === "global.json" || filePath.startsWith("services/backend/")) {
      selected.add("backend");
    }
    if (filePath.startsWith("apps/web/")) selected.add("web");
    if (filePath.startsWith("apps/pos-ipad/")) selected.add("pos-ipad");
    if (filePath.startsWith("apps/pos-handheld/")) selected.add("pos-handheld");
  }
  return QUALITY_LANES.filter((lane) => selected.has(lane));
}

function defaultDiffProvider(
  baseSha,
  headSha,
  { mergeBase = false, includeDeleted = true } = {},
) {
  const range = mergeBase ? `${baseSha}...${headSha}` : `${baseSha}..${headSha}`;
  const diffFilter = includeDeleted ? "ACMRD" : "ACMR";
  const output = execFileSync(
    "git",
    ["diff", "--name-only", "--no-renames", `--diff-filter=${diffFilter}`, range, "--"],
    {
      encoding: "utf8",
      maxBuffer: 5 * 1024 * 1024,
      stdio: ["ignore", "pipe", "inherit"],
    },
  );
  return output.split(/\r?\n/u).filter(Boolean);
}

export function resolveLanesForEvent({
  eventName,
  baseSha,
  headSha,
  beforeSha,
  diffProvider = defaultDiffProvider,
}) {
  if (eventName === "schedule" || eventName === "workflow_dispatch") {
    return [...QUALITY_LANES];
  }
  if (eventName === "push" && /^0{40}$/u.test(beforeSha ?? "")) {
    return [...QUALITY_LANES];
  }
  if (eventName !== "pull_request" && eventName !== "push") {
    throw new ValidationError(`不支持的 GitHub event：${eventName}`);
  }
  const effectiveBaseSha = eventName === "pull_request" ? baseSha : beforeSha;
  assertCommitSha(effectiveBaseSha, "base SHA");
  assertCommitSha(headSha, "head SHA");
  return selectLanesForPaths(
    diffProvider(effectiveBaseSha, headSha, {
      mergeBase: eventName === "pull_request",
      includeDeleted: true,
    }),
  );
}

function main(env = process.env) {
  const lanes = resolveLanesForEvent({
    eventName: env.QUALITY_EVENT_NAME,
    baseSha: env.QUALITY_BASE_SHA,
    headSha: env.QUALITY_HEAD_SHA,
    beforeSha: env.QUALITY_BEFORE_SHA,
  });
  const output = [
    `has_lanes=${lanes.length > 0}`,
    `matrix=${JSON.stringify(lanes)}`,
    ...QUALITY_LANES.map((lane) => `${lane.replaceAll("-", "_")}=${lanes.includes(lane)}`),
  ].join("\n");
  if (env.GITHUB_OUTPUT) {
    appendFileSync(env.GITHUB_OUTPUT, `${output}\n`, "utf8");
  } else {
    console.log(output);
  }
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  try {
    main();
  } catch (error) {
    console.error(`Quality lane 路径选择失败：${error.message}`);
    process.exitCode = 1;
  }
}
