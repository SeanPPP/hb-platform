import assert from "node:assert/strict";
import test from "node:test";

import {
  resolveReleaseCommit,
  selectReleaseEventCommit,
} from "./release-commit.mjs";

test("release commit 优先显式配置、允许本地 git fallback，并只优先有效 EAS SHA", () => {
  const explicit = "A".repeat(40);
  const github = "b".repeat(40);
  assert.equal(
    resolveReleaseCommit({
      environment: {
        PERFORMANCE_RELEASE_COMMIT_SHA: explicit,
        GITHUB_SHA: github,
      },
      readGitHeadFn: () => "c".repeat(40),
    }),
    explicit.toLowerCase(),
  );
  assert.equal(
    resolveReleaseCommit({
      environment: {},
      readGitHeadFn: () => ` ${github}\n`,
    }),
    github,
  );
  assert.throws(
    () => resolveReleaseCommit({ environment: { GITHUB_SHA: "short" } }),
    /40 位十六进制/,
  );
  assert.equal(
    selectReleaseEventCommit({ payloadCommit: explicit, resolvedCommit: github }),
    explicit.toLowerCase(),
  );
  assert.equal(
    selectReleaseEventCommit({ payloadCommit: "short", resolvedCommit: github }),
    github,
  );
});
