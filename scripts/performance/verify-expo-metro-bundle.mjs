import { execFile } from "node:child_process";
import { existsSync, mkdtempSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const PLATFORMS = new Set(["ios", "android"]);

export function buildExpoMetroBundleCommand({ platform, outputDirectory }) {
  if (!PLATFORMS.has(platform)) throw new Error("--platform 必须是 ios 或 android");
  return {
    command: "npx",
    args: ["expo", "export", "--platform", platform, "--output-dir", outputDirectory],
  };
}

function execute(command, cwd) {
  return new Promise((resolvePromise, reject) => {
    execFile(command.command, command.args, { cwd, shell: false }, (error) => {
      if (error) reject(error);
      else resolvePromise();
    });
  });
}

export async function verifyExpoMetroBundle({
  platform,
  cwd = process.cwd(),
  executeFn = execute,
} = {}) {
  const outputDirectory = mkdtempSync(join(tmpdir(), `hbpos-${platform}-metro-`));
  try {
    await executeFn(buildExpoMetroBundleCommand({ platform, outputDirectory }), cwd);
    // Metro 必须真实写出 bundle；临时目录会在 finally 清理，避免污染共享工作树。
    if (!existsSync(outputDirectory) || readdirSync(outputDirectory).length === 0) {
      throw new Error("Expo/Metro 未生成可验证的 bundle 产物");
    }
  } finally {
    rmSync(outputDirectory, { recursive: true, force: true });
  }
}

function parseArgs(argv) {
  if (argv.length !== 2 || argv[0] !== "--platform") {
    throw new Error("用法: node verify-expo-metro-bundle.mjs --platform ios|android");
  }
  return { platform: argv[1] };
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  verifyExpoMetroBundle(parseArgs(process.argv.slice(2))).catch((error) => {
    console.error(`Expo/Metro bundle 验证失败：${error.message}`);
    process.exitCode = 1;
  });
}
