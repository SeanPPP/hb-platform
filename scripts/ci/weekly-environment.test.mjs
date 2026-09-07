import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, chmodSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";

const repositoryRoot = join(import.meta.dirname, "../..");
const macosScript = join(repositoryRoot, "scripts/ci/run-macos-component.sh");
const weeklySqlScript = join(repositoryRoot, "scripts/ci/run-weekly-sql.sh");

function makeExecutable(path, contents) {
  writeFileSync(path, contents, { mode: 0o755 });
  chmodSync(path, 0o755);
}

function run(script, env, args = []) {
  return spawnSync("bash", [script, ...args], {
    cwd: repositoryRoot,
    env: { ...process.env, ...env },
    encoding: "utf8",
  });
}

test("weekly macOS 原生构建把 Sentry 自动上传禁用变量传给 xcodebuild", (t) => {
  const root = mkdtempSync(join(tmpdir(), "weekly-macos-env-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const bin = join(root, "bin");
  const xcode = join(root, "Xcode", "Contents", "Developer");
  mkdirSync(join(bin), { recursive: true });
  mkdirSync(join(xcode, "usr", "bin"), { recursive: true });
  const buildCapture = join(root, "xcodebuild.env");

  makeExecutable(join(xcode, "usr/bin/xcodebuild"), "#!/usr/bin/env bash\nexit 0\n");
  makeExecutable(
    join(bin, "xcodebuild"),
    `#!/usr/bin/env bash
if [[ "$1" == "-version" ]]; then
  printf 'Xcode 26.5\\nBuild version weekly-test\\n'
else
  printf 'SENTRY_DISABLE_AUTO_UPLOAD=%s\\n' "\${SENTRY_DISABLE_AUTO_UPLOAD:-}" > "$CI_BUILD_CAPTURE"
fi
`,
  );
  // 只让脚本完成前置命令，测试真实的 xcodebuild 子进程环境。
  makeExecutable(join(bin, "npm"), "#!/usr/bin/env bash\nexit 0\n");
  makeExecutable(join(bin, "node"), "#!/usr/bin/env bash\nexit 0\n");

  for (const component of ["pos-ipad-native", "pos-handheld-native"]) {
    const result = run(macosScript, {
      PATH: `${bin}:${process.env.PATH}`,
      DEVELOPER_DIR: xcode,
      CI_PROFILE: "weekly",
      RUNNER_TEMP: join(root, "runner-temp"),
      CI_BUILD_CAPTURE: buildCapture,
      SENTRY_DISABLE_AUTO_UPLOAD: "false",
    }, [component]);

    assert.equal(result.status, 0, result.stderr || result.stdout);
    assert.equal(readFileSync(buildCapture, "utf8").trim(), "SENTRY_DISABLE_AUTO_UPLOAD=true");
  }
});

test("weekly SQL preflight 要求跨连接锁与成本回填测试的全部连接变量", (t) => {
  const root = mkdtempSync(join(tmpdir(), "weekly-sql-env-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const bin = join(root, "bin");
  const capture = join(root, "dotnet.env");
  const bashEnv = join(root, "bash.env");
  mkdirSync(bin, { recursive: true });
  // macOS 自带 Bash 3 没有 mapfile；只为测试提供等价的最小数组读取函数。
  writeFileSync(
    bashEnv,
    'mapfile() { if [[ "$1" == "-t" ]]; then shift; fi; local target="$1" line; eval "$target=()"; while IFS= read -r line; do eval "$target+=(\\"$line\\")"; done; }\n',
  );

  makeExecutable(
    join(bin, "docker"),
    `#!/usr/bin/env bash
if [[ "$1" == "ps" ]]; then
  printf 'weekly-test-container\\n'
  exit 0
fi
if [[ "$1" == "exec" ]]; then
  if [[ "$*" == *"test -x"* ]]; then exit 0; fi
  exit 0
fi
exit 0
`,
  );
  makeExecutable(join(bin, "dotnet"), `#!/usr/bin/env bash
printf 'HB_TEST_SQLSERVER_CONNECTION=%s\\n' "\${HB_TEST_SQLSERVER_CONNECTION:-}" >> "$CI_SQL_CAPTURE"
printf 'CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION=%s\\n' "\${CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION:-}" >> "$CI_SQL_CAPTURE"
printf 'COST_BACKFILL_SQLSERVER_TEST_CONNECTION=%s\\n' "\${COST_BACKFILL_SQLSERVER_TEST_CONNECTION:-}" >> "$CI_SQL_CAPTURE"
exit 0
`);
  makeExecutable(join(bin, "node"), "#!/usr/bin/env bash\nexit 0\n");
  makeExecutable(join(bin, "sleep"), "#!/usr/bin/env bash\nexit 0\n");

  const connection = "Server=localhost,1433;User Id=sa;Password=test;Encrypt=False";
  const baseEnv = {
    PATH: `${bin}:${process.env.PATH}`,
    BASH_ENV: bashEnv,
    RUNNER_TEMP: join(root, "runner-temp"),
    GITHUB_RUN_ID: "weekly-test",
    CI_SQL_PASSWORD: "test",
    CI_SQL_CAPTURE: capture,
    HB_TEST_SQLSERVER_CONNECTION: connection,
    CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION: connection,
    COST_BACKFILL_SQLSERVER_TEST_CONNECTION: connection,
    LOCAL_PURCHASE_DASHBOARD_SQLSERVER_TEST_CONNECTION: connection,
    PREORDER_SQLSERVER_TEST_CONNECTION: connection,
    SET_CHILD_PURCHASE_PRICE_SQLSERVER_TEST_CONNECTION: connection,
    STORE_PRICE_TRANSFER_SQLSERVER_TEST_CONNECTION: connection,
    DEVICE_ACTIVATION_SQLSERVER_TEST_CONNECTION: connection,
    HBWEB_SCHEMA_SQLSERVER_TEST_CONNECTION: connection,
  };

  for (const missingVariable of [
    "HB_TEST_SQLSERVER_CONNECTION",
    "CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION",
    "COST_BACKFILL_SQLSERVER_TEST_CONNECTION",
  ]) {
    const workflow = readFileSync(join(repositoryRoot, ".github/workflows/pr-ci.yml"), "utf8");
    const weeklyJob = workflow.slice(workflow.indexOf("  weekly_sql:"), workflow.indexOf("  weekly_performance:"));
    assert.match(weeklyJob, new RegExp(`^      ${missingVariable}: Server=localhost,1433;`, "m"),
      "GitHub weekly 必须为测试提供专用 SQL Server 容器连接");
    const missingResult = run(weeklySqlScript, {
      ...baseEnv,
      [missingVariable]: "",
    });
    assert.equal(missingResult.status, 1, missingResult.stderr || missingResult.stdout);
    assert.match(missingResult.stderr, new RegExp(missingVariable));
  }

  const result = run(weeklySqlScript, {
    ...baseEnv,
    HB_TEST_SQLSERVER_CONNECTION: connection,
    CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION: connection,
  });

  assert.equal(result.status, 0, result.stderr || result.stdout);
  assert.match(readFileSync(capture, "utf8"), /HB_TEST_SQLSERVER_CONNECTION=Server=localhost,1433/);
  assert.match(
    readFileSync(capture, "utf8"),
    /CONTAINER_MUTATION_SQLSERVER_TEST_CONNECTION=Server=localhost,1433/,
  );
  assert.match(readFileSync(capture, "utf8"), /COST_BACKFILL_SQLSERVER_TEST_CONNECTION=Server=localhost,1433/);
});
