import assert from "node:assert/strict";
import {
  createAutomaticAppUpdateController,
  type AutomaticAppUpdateDependencies,
} from "./automatic-app-update";

function createDependencies(
  results: AutomaticAppUpdateDependencies["checkAndDownload"] extends () => Promise<infer Result> ? Result[] : never[]
) {
  const prompts: number[] = [];
  const calls: string[] = [];

  const dependencies: AutomaticAppUpdateDependencies = {
    checkAndDownload: async () => {
      calls.push("check");
      const result = results.shift();
      assert.ok(result, "测试应提供足够的更新检查结果");
      return result;
    },
    promptRestart: () => {
      prompts.push(Date.now());
    },
    warn: () => {
      calls.push("warn");
    },
  };

  return { dependencies, prompts, calls };
}

async function run() {
  {
    const { dependencies, prompts, calls } = createDependencies([{ status: "downloaded" }]);
    const controller = createAutomaticAppUpdateController(dependencies);

    await controller.check({ enabled: false });

    assert.deepEqual(calls, [], "未启用自动更新时不应检查 OTA");
    assert.equal(prompts.length, 0, "未启用自动更新时不应提示重启");
  }

  {
    const { dependencies, prompts, calls } = createDependencies([{ status: "not-available" }]);
    const controller = createAutomaticAppUpdateController(dependencies);

    await controller.check({ enabled: true });

    assert.deepEqual(calls, ["check"], "启用后应执行一次自动检查");
    assert.equal(prompts.length, 0, "没有可用更新时不应提示重启");
  }

  {
    const { dependencies, prompts, calls } = createDependencies([
      { status: "development-disabled" },
      { status: "configuration-disabled" },
    ]);
    const controller = createAutomaticAppUpdateController(dependencies);

    await controller.check({ enabled: true });
    await controller.handleAppStateChange("background", "active", { enabled: true });

    assert.deepEqual(calls, ["check", "check"], "开发模式或配置未启用时只应静默跳过");
    assert.equal(prompts.length, 0, "不可检查更新时不应打扰用户");
  }

  {
    let finishCheck: () => void = () => undefined;
    const prompts: number[] = [];
    const calls: string[] = [];
    const controller = createAutomaticAppUpdateController({
      checkAndDownload: async () => {
        calls.push("check");
        await new Promise<void>((resolve) => {
          finishCheck = resolve;
        });
        return { status: "not-available" };
      },
      promptRestart: () => {
        prompts.push(Date.now());
      },
      warn: () => {
        calls.push("warn");
      },
    });

    const firstCheck = controller.check({ enabled: true });
    const secondCheck = controller.check({ enabled: true });
    finishCheck();
    await Promise.all([firstCheck, secondCheck]);

    assert.deepEqual(calls, ["check"], "并发触发时只应保留一个 OTA 检查任务");
    assert.equal(prompts.length, 0, "无更新的并发检查不应提示重启");
  }

  {
    const { dependencies, prompts, calls } = createDependencies([
      { status: "downloaded" },
      { status: "downloaded" },
    ]);
    const controller = createAutomaticAppUpdateController(dependencies);

    await controller.check({ enabled: true });
    await controller.handleAppStateChange("background", "active", { enabled: true });

    assert.deepEqual(calls, ["check"], "已下载更新后不应重复检查并反复弹窗");
    assert.equal(prompts.length, 1, "下载成功后只提示一次重启");
  }

  {
    const { dependencies, prompts, calls } = createDependencies([{ status: "downloaded" }]);
    const controller = createAutomaticAppUpdateController(dependencies);

    await controller.handleAppStateChange("active", "active", { enabled: true });
    await controller.handleAppStateChange("inactive", "active", { enabled: true });

    assert.deepEqual(calls, ["check"], "只有从后台或非活跃状态回到前台才触发自动检查");
    assert.equal(prompts.length, 1, "回到前台下载成功后应提示重启");
  }

  console.log("automatic-app-update.test.ts: ok");
}

void run();
