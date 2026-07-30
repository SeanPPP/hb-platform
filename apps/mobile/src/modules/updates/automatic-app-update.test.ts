import assert from "node:assert/strict";
import {
  createAutomaticAppUpdateApplyHandler,
  createAutomaticAppUpdateController,
  type AutomaticAppUpdateDependencies,
} from "./automatic-app-update";

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

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

  {
    const nativeBarrier = deferred<{
      allowed: boolean;
      epoch: number;
    }>();
    const { dependencies, prompts, calls } = createDependencies([
      { status: "not-available" },
    ]);
    const controller = createAutomaticAppUpdateController(dependencies);
    let barrierCalls = 0;
    const options = {
      enabled: true,
      beforeCheck: () => {
        barrierCalls += 1;
        return nativeBarrier.promise;
      },
      getEpoch: () => 1,
    };
    const foreground = controller.handleAppStateChange(
      "background",
      "active",
      options,
    );
    const concurrent = controller.check(options);

    await Promise.resolve();
    assert.deepEqual(
      calls,
      [],
      "原生版本检查屏障完成前不得启动 OTA",
    );
    assert.equal(
      barrierCalls,
      1,
      "并发前台触发必须复用同一条协调任务",
    );
    nativeBarrier.resolve({ allowed: false, epoch: 1 });
    await Promise.all([foreground, concurrent]);
    assert.deepEqual(
      calls,
      [],
      "原生 required 或正在展示 optional 提示时不得启动 OTA",
    );
    assert.equal(prompts.length, 0);
  }

  {
    const download = deferred<void>();
    let epoch = 7;
    const prompts: number[] = [];
    const calls: string[] = [];
    const controller = createAutomaticAppUpdateController({
      checkAndDownload: async () => {
        calls.push("check");
        await download.promise;
        return { status: "downloaded" };
      },
      promptRestart: () => {
        prompts.push(Date.now());
      },
      warn: () => {
        calls.push("warn");
      },
    });
    const check = controller.check({
      enabled: true,
      beforeCheck: async () => ({ allowed: true, epoch: 7 }),
      getEpoch: () => epoch,
    });

    await Promise.resolve();
    assert.deepEqual(calls, ["check"]);
    epoch = 8;
    download.resolve(undefined);
    await check;
    assert.equal(
      prompts.length,
      0,
      "OTA await 期间原生决策 epoch 改变时不得弹出重启提示",
    );
  }

  {
    let epoch = 21;
    let barrierCalls = 0;
    let beforeApply: (() => Promise<boolean>) | undefined;
    const controller = createAutomaticAppUpdateController({
      checkAndDownload: async () => ({ status: "downloaded" }),
      promptRestart: (...args: unknown[]) => {
        beforeApply = (
          args[0] as { beforeApply?: () => Promise<boolean> } | undefined
        )?.beforeApply;
      },
      warn: () => undefined,
    });
    const options = {
      enabled: true,
      beforeCheck: async () => {
        barrierCalls += 1;
        if (barrierCalls === 1) {
          return { allowed: true, epoch };
        }
        epoch += 1;
        return { allowed: false, epoch };
      },
      getEpoch: () => epoch,
    };

    await controller.check(options);
    assert.equal(typeof beforeApply, "function", "重启提示必须携带点击时的异步应用守卫");
    assert.equal(
      await beforeApply!(),
      false,
      "Alert 展示后原生策略变为 required 时不得应用已下载 OTA",
    );
    assert.equal(
      barrierCalls,
      2,
      "点击旧 Alert 必须 fresh recheck 原生策略，不能只比较下载时 epoch",
    );
  }

  {
    const permission = deferred<boolean>();
    let beforeApplyCalls = 0;
    let applyCalls = 0;
    const warnings: unknown[] = [];
    const apply = createAutomaticAppUpdateApplyHandler({
      beforeApply: async () => {
        beforeApplyCalls += 1;
        return permission.promise;
      },
      apply: async () => {
        applyCalls += 1;
      },
      warn: (error) => warnings.push(error),
    });

    const firstPress = apply();
    const duplicatePress = apply();
    assert.equal(beforeApplyCalls, 1, "重启按钮连续点击只能启动一个应用前校验");
    permission.resolve(true);
    await Promise.all([firstPress, duplicatePress]);
    assert.equal(applyCalls, 1, "同一轮按钮点击只能 reload 一次");
    assert.deepEqual(warnings, []);
  }

  {
    const warnings: unknown[] = [];
    const apply = createAutomaticAppUpdateApplyHandler({
      beforeApply: async () => true,
      apply: async () => {
        throw new Error("reload failed");
      },
      warn: (error) => warnings.push(error),
    });

    await assert.doesNotReject(apply(), "Alert 异步 onPress 错误必须在内部处理");
    assert.match(String(warnings[0]), /reload failed/);
  }

  console.log("automatic-app-update.test.ts: ok");
}

void run();
