import { execFileSync } from "node:child_process";
import { cpSync, mkdtempSync, rmSync, symlinkSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const ipadAppRoot = join(repositoryRoot, "apps", "pos-ipad");
const expectedWorkspaces = ["@hb/pos-ipad", "@hb/pos-handheld"];

if (
  process.argv.length !== 4 ||
  expectedWorkspaces.some((workspace, index) => process.argv[index + 2] !== workspace)
) {
  throw new Error(
    `必须按顺序运行 ${expectedWorkspaces.join("、")} 的完整测试`,
  );
}

const temporaryRoot = mkdtempSync(join(tmpdir(), "hb-pos-ipad-prebuild-"));
const temporaryAppRoot = join(temporaryRoot, "pos-ipad");
const skippedTopLevelPaths = new Set([".expo", "dist", "ios", "node_modules"]);
const require = createRequire(import.meta.url);
const expoCli = require.resolve("expo/bin/cli", { paths: [repositoryRoot] });
const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";

function run(command, args, options = {}) {
  execFileSync(command, args, {
    cwd: repositoryRoot,
    env: process.env,
    stdio: "inherit",
    ...options,
  });
}

try {
  cpSync(ipadAppRoot, temporaryAppRoot, {
    recursive: true,
    filter(source) {
      const path = relative(ipadAppRoot, source);
      if (!path) return true;
      const topLevelPath = path.split(sep)[0];
      return !skippedTopLevelPaths.has(topLevelPath);
    },
  });
  symlinkSync(
    join(repositoryRoot, "node_modules"),
    join(temporaryAppRoot, "node_modules"),
    "dir",
  );

  run(process.execPath, [expoCli, "prebuild", "--platform", "ios", "--no-install"], {
    cwd: temporaryAppRoot,
  });
  run(npmCommand, ["test", "--workspace=@hb/pos-ipad"], {
    env: {
      ...process.env,
      HB_POS_IPAD_GENERATED_IOS_ROOT: join(temporaryAppRoot, "ios"),
    },
  });
  run(npmCommand, ["test", "--workspace=@hb/pos-handheld"]);
} finally {
  rmSync(temporaryRoot, { force: true, recursive: true });
}
