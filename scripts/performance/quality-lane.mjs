import { spawn } from "node:child_process";
import {
  appendFileSync,
  mkdirSync,
  readFileSync,
  renameSync,
  writeFileSync,
} from "node:fs";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import {
  ValidationError,
  assertCanonicalUtcTimestamp,
  assertEnum,
  assertExactKeys,
  assertFiniteNumber,
  assertSafeString,
  assertValidDate,
} from "./lib/validation.mjs";

const QUALITY_LANES = ["backend", "web", "pos-ipad", "pos-handheld"];

const LANE_COMMANDS = Object.freeze({
  backend: [
    {
      label: ".NET restore",
      command: "dotnet",
      args: [
        "restore",
        "services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj",
      ],
      cwd: ".",
    },
    {
      label: ".NET Release build",
      command: "dotnet",
      args: [
        "build",
        "services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj",
        "--configuration",
        "Release",
        "--no-restore",
      ],
      cwd: ".",
    },
    {
      label: ".NET test",
      command: "dotnet",
      args: [
        "test",
        "services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj",
        "--configuration",
        "Release",
        "--no-restore",
        "--no-build",
        "--filter",
        "Category!=SQL&Category!=Performance&Category!=LiveE2e",
      ],
      cwd: ".",
    },
  ],
  web: [
    { label: "npm ci", command: "npm", args: ["ci"], cwd: "apps/web" },
    {
      label: "web build",
      command: "npm",
      args: ["run", "build", "--", "--manifest"],
      cwd: "apps/web",
      // 质量构建产物不会部署；使用非凭据占位值通过 production 配置完整性校验。
      environment: {
        VITE_CENTER_LOG_KEY: "quality-baseline-ci-placeholder",
        VITE_CENTER_LOG_PROJECT: "hbweb_rv",
        VITE_CENTER_LOG_ENVIRONMENT: "Production",
        VITE_CENTER_LOG_SERVICE_NAME: "hbweb_rv-web",
      },
    },
    { label: "web test", command: "npm", args: ["test"], cwd: "apps/web" },
  ],
  "pos-ipad": [
    { label: "npm ci", command: "npm", args: ["ci"], cwd: "apps/pos-ipad" },
    {
      label: "pos-ipad typecheck",
      command: "npm",
      args: ["run", "typecheck"],
      cwd: "apps/pos-ipad",
    },
    {
      label: "pos-ipad Expo/Metro bundle",
      command: "npm",
      args: ["run", "verify:metro-bundle"],
      cwd: "apps/pos-ipad",
    },
    {
      label: "pos-ipad test",
      command: "npm",
      args: ["run", "test:ci"],
      cwd: "apps/pos-ipad",
      environment: { TZ: "Australia/Brisbane" },
    },
  ],
  "pos-handheld": [
    { label: "npm ci", command: "npm", args: ["ci"], cwd: "apps/pos-handheld" },
    {
      label: "pos-handheld typecheck",
      command: "npm",
      args: ["run", "typecheck"],
      cwd: "apps/pos-handheld",
    },
    {
      label: "pos-handheld Expo/Metro bundle",
      command: "npm",
      args: ["run", "verify:metro-bundle"],
      cwd: "apps/pos-handheld",
    },
    {
      label: "pos-handheld test",
      command: "npm",
      args: ["run", "test:ci"],
      cwd: "apps/pos-handheld",
      environment: { TZ: "Australia/Brisbane" },
    },
  ],
});

function validateLaneName(lane) {
  return assertEnum(lane, QUALITY_LANES, "lane");
}

export function getLaneCommands(lane) {
  validateLaneName(lane);
  return LANE_COMMANDS[lane].map((spec) => ({
    ...spec,
    args: [...spec.args],
    ...(spec.environment ? { environment: { ...spec.environment } } : {}),
  }));
}

export function buildLaneProcessEnvironment(spec, baseEnvironment = process.env) {
  return {
    ...baseEnvironment,
    ...(spec.environment ?? {}),
    CI: "true",
  };
}

function executeCommand(spec) {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(spec.command, spec.args, {
      cwd: resolve(spec.cwd),
      env: buildLaneProcessEnvironment(spec),
      shell: false,
      stdio: "inherit",
    });
    child.once("error", reject);
    child.once("close", (code, signal) => {
      if (signal) reject(new Error(`${spec.label} 被信号 ${signal} 中止`));
      else resolvePromise(code ?? 1);
    });
  });
}

export async function runLaneCommands(lane, { execute = executeCommand } = {}) {
  for (const spec of getLaneCommands(lane)) {
    console.log(`开始：${spec.label}`);
    const exitCode = await execute(spec);
    if (!Number.isInteger(exitCode) || exitCode !== 0) {
      throw new Error(`${spec.label} 失败，退出码 ${exitCode}`);
    }
    console.log(`完成：${spec.label}`);
  }
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

export function startLane({ lane, statePath, now = new Date() }) {
  validateLaneName(lane);
  assertSafeString(statePath, "statePath", { maxLength: 4096 });
  assertValidDate(now, "now");
  const state = {
    schemaVersion: "QualityLaneStateV1",
    lane,
    startedAtUtc: now.toISOString(),
    startedEpochMs: now.getTime(),
  };
  writeJsonAtomic(resolve(statePath), state);
  return state;
}

function readLaneState(statePath, lane) {
  let state;
  try {
    state = JSON.parse(readFileSync(resolve(statePath), "utf8"));
  } catch {
    throw new ValidationError("无法读取 quality lane 起点状态");
  }
  assertExactKeys(
    state,
    { required: ["schemaVersion", "lane", "startedAtUtc", "startedEpochMs"] },
    "lane state",
  );
  if (state.schemaVersion !== "QualityLaneStateV1" || state.lane !== lane) {
    throw new ValidationError("quality lane 起点状态与当前 lane 不匹配");
  }
  assertCanonicalUtcTimestamp(state.startedAtUtc, "lane state.startedAtUtc");
  assertFiniteNumber(state.startedEpochMs, "lane state.startedEpochMs", {
    min: 0,
    max: Number.MAX_SAFE_INTEGER,
    integer: true,
  });
  return state;
}

function mapConclusion(jobStatus) {
  if (jobStatus === "success") return "accepted";
  if (jobStatus === "cancelled") return "cancelled";
  return "failed";
}

export function finishLane({
  lane,
  statePath,
  resultPath,
  jobStatus,
  verificationOutcome = "skipped",
  summaryPath,
  now = new Date(),
}) {
  validateLaneName(lane);
  assertEnum(jobStatus, ["success", "failure", "cancelled"], "jobStatus");
  if (!verificationOutcome) verificationOutcome = "skipped";
  assertEnum(
    verificationOutcome,
    ["success", "failure", "cancelled", "skipped"],
    "verificationOutcome",
  );
  assertValidDate(now, "now");
  const state = readLaneState(statePath, lane);
  const durationMs = now.getTime() - state.startedEpochMs;
  if (durationMs < 0) throw new ValidationError("quality lane 结束时间早于开始时间");
  const conclusion = mapConclusion(jobStatus);
  const result = {
    schemaVersion: "QualityLaneResultV1",
    lane,
    startedAtUtc: state.startedAtUtc,
    finishedAtUtc: now.toISOString(),
    durationMs,
    conclusion,
    ...(conclusion === "accepted"
      ? {}
      : {
          errorCode:
            conclusion === "cancelled"
              ? "lane_cancelled"
              : verificationOutcome === "skipped"
                ? "lane_setup_failed"
                : "lane_verification_failed",
        }),
  };
  writeJsonAtomic(resolve(resultPath), result);
  if (summaryPath) {
    appendFileSync(
      resolve(summaryPath),
      `\n## Quality lane: ${lane}\n\n- 开始：\`${result.startedAtUtc}\`\n- 结束：\`${result.finishedAtUtc}\`\n- 用时：\`${result.durationMs} ms\`\n- 结论：\`${result.conclusion}\`\n`,
      "utf8",
    );
  }
  return result;
}

function parseArgs(argv) {
  const [command, ...rest] = argv;
  const options = { command };
  const valueOptions = new Map([
    ["--lane", "lane"],
    ["--state", "statePath"],
    ["--output", "resultPath"],
    ["--job-status", "jobStatus"],
    ["--verification-outcome", "verificationOutcome"],
    ["--summary", "summaryPath"],
  ]);
  for (let index = 0; index < rest.length; index += 1) {
    const argument = rest[index];
    if (valueOptions.has(argument)) {
      const value = rest[++index];
      if (!value || value.startsWith("--")) throw new ValidationError(`${argument} 缺少值`);
      options[valueOptions.get(argument)] = value;
    } else {
      throw new ValidationError(`未知参数 ${argument}`);
    }
  }
  return options;
}

async function main(argv = process.argv.slice(2)) {
  const options = parseArgs(argv);
  if (options.command === "start") {
    startLane(options);
  } else if (options.command === "run") {
    await runLaneCommands(options.lane);
  } else if (options.command === "finish") {
    finishLane(options);
  } else {
    throw new ValidationError("命令必须为 start、run 或 finish");
  }
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  main().catch((error) => {
    console.error(`Quality lane 执行失败：${error.message}`);
    process.exitCode = 1;
  });
}
