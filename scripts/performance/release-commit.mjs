import { execFileSync } from "node:child_process";

const COMMIT_SHA_PATTERN = /^[0-9a-f]{40}$/iu;

function normalizeCommitSha(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  return COMMIT_SHA_PATTERN.test(normalized) ? normalized.toLowerCase() : null;
}

function readGitHead() {
  return execFileSync("git", ["rev-parse", "HEAD"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"],
  });
}

export function resolveReleaseCommit({
  environment = process.env,
  readGitHeadFn = readGitHead,
} = {}) {
  for (const name of ["PERFORMANCE_RELEASE_COMMIT_SHA", "GITHUB_SHA"]) {
    const value = environment[name];
    if (value === undefined || value === "") continue;
    const commit = normalizeCommitSha(value);
    if (!commit) throw new Error(`${name} 必须是 40 位十六进制 commit SHA`);
    return commit;
  }

  let gitHead;
  try {
    gitHead = readGitHeadFn();
  } catch {
    throw new Error("发布前无法解析 40 位 commit SHA；请设置 PERFORMANCE_RELEASE_COMMIT_SHA");
  }
  const commit = normalizeCommitSha(gitHead);
  if (!commit) {
    throw new Error("git rev-parse HEAD 未返回 40 位十六进制 commit SHA");
  }
  return commit;
}

export function selectReleaseEventCommit({ payloadCommit, resolvedCommit }) {
  // EAS 载荷中的完整 SHA 代表实际发布内容，合法时优先用于幂等 release event。
  return normalizeCommitSha(payloadCommit) ?? resolvedCommit;
}
