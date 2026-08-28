import assert from "node:assert/strict";
import {
  IOS_NATIVE_REQUIRED_CACHE_KEY,
  IOS_NATIVE_OPTIONAL_REMINDER_KEY,
  checkIosNativeAppUpdate,
  createIosNativeOptionalReminderSession,
  deriveIosNativeOtaBarrier,
  getIosNativeUpdateBoundaryMode,
  markIosNativeOptionalReminder,
  normalizeIosNativeUpdateDecision,
  readCachedIosNativeRequiredDecision,
  shouldActivateIosNativeOptionalPrompt,
  shouldEnableIosNativeUpdate,
  shouldCheckIosNativeUpdateOnAppStateChange,
  shouldPauseAutomaticOtaForIosNativeUpdate,
  type IosNativeUpdateStorage,
} from "./ios-native-app-update";

class MemoryStorage implements IosNativeUpdateStorage {
  private readonly values = new Map<string, unknown>();

  async getObject<T>(key: string) {
    return (this.values.get(key) as T | undefined) ?? null;
  }

  async setObject(key: string, value: unknown) {
    this.values.set(key, value);
  }

  async removeItem(key: string) {
    this.values.delete(key);
  }

  seed(key: string, value: unknown) {
    this.values.set(key, value);
  }

  has(key: string) {
    return this.values.has(key);
  }
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

const context = {
  apiBaseUrl: "https://hotbargain.vip/api",
  installedVersion: "1.0.2",
  installedBuild: "28",
};

const requiredDecision = {
  state: "required" as const,
  policyVersion: "policy-2",
  latestVersion: "1.2.0",
  minimumSupportedVersion: "1.1.0",
  appStoreUrl: "https://apps.apple.com/au/app/hot-bargain/id6786073002",
  releaseMessage: "请升级后继续使用",
};

const optionalDecision = {
  ...requiredDecision,
  state: "optional" as const,
  minimumSupportedVersion: "1.0.0",
};

async function run() {
  assert.match(IOS_NATIVE_REQUIRED_CACHE_KEY, /:v2$/, "required 缓存键必须升级到 v2");
  assert.match(
    IOS_NATIVE_OPTIONAL_REMINDER_KEY,
    /:v2$/,
    "optional 提醒键必须升级到 v2",
  );

  {
    assert.equal(
      shouldEnableIosNativeUpdate({
        platform: "ios",
        buildProfile: "production",
        isDevelopment: false,
        reviewGuardActive: false,
      }),
      true,
      "正式 iOS 包应启用 App Store 更新检查",
    );
    for (const buildProfile of ["development", "preview", "test", "testing"]) {
      assert.equal(
        shouldEnableIosNativeUpdate({
          platform: "ios",
          buildProfile,
          isDevelopment: false,
          reviewGuardActive: false,
        }),
        false,
        `${buildProfile} 构建不应检查 App Store 更新`,
      );
    }
    assert.equal(
      shouldEnableIosNativeUpdate({
        platform: "android",
        buildProfile: "production",
        isDevelopment: false,
        reviewGuardActive: false,
      }),
      false,
      "Android 必须保留现有 APK 更新路径",
    );
    assert.equal(
      shouldEnableIosNativeUpdate({
        platform: "ios",
        buildProfile: "production",
        isDevelopment: false,
        reviewGuardActive: true,
      }),
      false,
      "iOS 审核保护状态不得访问线上更新接口",
    );

    assert.equal(
      shouldPauseAutomaticOtaForIosNativeUpdate({
        enabled: true,
        initialized: false,
        state: null,
        optionalPromptActive: false,
      }),
      true,
      "首次原生版本决策完成前必须暂停 OTA",
    );
    assert.equal(
      shouldPauseAutomaticOtaForIosNativeUpdate({
        enabled: true,
        initialized: true,
        state: "required",
        optionalPromptActive: false,
      }),
      true,
      "原生强制更新期间必须持续暂停 OTA",
    );
    assert.equal(
      shouldPauseAutomaticOtaForIosNativeUpdate({
        enabled: true,
        initialized: true,
        state: "optional",
        optionalPromptActive: false,
      }),
      false,
      "可选原生更新不得永久阻止 OTA",
    );
    assert.equal(
      shouldPauseAutomaticOtaForIosNativeUpdate({
        enabled: true,
        initialized: true,
        state: "optional",
        optionalPromptActive: true,
      }),
      true,
      "可选原生更新弹窗等待用户处理期间必须暂停 OTA，避免两个提示竞争",
    );
    assert.equal(
      shouldPauseAutomaticOtaForIosNativeUpdate({
        enabled: false,
        initialized: false,
        state: null,
        optionalPromptActive: true,
      }),
      false,
      "非目标构建必须完全保持现有 OTA 行为",
    );
    assert.equal(
      shouldActivateIosNativeOptionalPrompt({
        decision: optionalDecision,
        shouldPromptOptional: true,
      }),
      true,
      "首次可选决策应原子激活提示占用状态",
    );
    assert.equal(
      shouldActivateIosNativeOptionalPrompt({
        decision: optionalDecision,
        shouldPromptOptional: false,
      }),
      false,
      "24 小时内已提醒的可选决策不得再次占用 OTA",
    );
    assert.equal(
      shouldActivateIosNativeOptionalPrompt({
        decision: requiredDecision,
        shouldPromptOptional: true,
      }),
      false,
      "强制决策应由根门禁处理，不得进入可选提示通道",
    );
    assert.deepEqual(
      deriveIosNativeOtaBarrier({
        epoch: 11,
        outcome: {
          source: "server",
          decision: requiredDecision,
          shouldPromptOptional: false,
        },
      }),
      { allowed: false, epoch: 11 },
      "原生 required 必须先于 OTA",
    );
    assert.deepEqual(
      deriveIosNativeOtaBarrier({
        epoch: 12,
        outcome: {
          source: "server",
          decision: optionalDecision,
          shouldPromptOptional: true,
        },
      }),
      { allowed: true, epoch: 12 },
      "原生 optional 必须先让 OTA 完成 required 判定，再竞争 optional 提示",
    );
    assert.deepEqual(
      deriveIosNativeOtaBarrier({
        epoch: 13,
        outcome: {
          source: "server",
          decision: optionalDecision,
          shouldPromptOptional: false,
        },
      }),
      { allowed: true, epoch: 13 },
      "24 小时内已提醒的原生 optional 可继续 OTA",
    );
    assert.deepEqual(
      deriveIosNativeOtaBarrier({
        epoch: 14,
        outcome: {
          source: "server",
          decision: {
            state: "none",
            policyVersion: "policy-none",
            latestVersion: null,
            minimumSupportedVersion: null,
            appStoreUrl: null,
            releaseMessage: null,
          },
          shouldPromptOptional: false,
        },
      }),
      { allowed: true, epoch: 14 },
      "原生 none 应继续 OTA",
    );
    assert.deepEqual(
      deriveIosNativeOtaBarrier(null, 15),
      { allowed: false, epoch: 15 },
      "原生检查被取消或失效时没有 receipt，Root 必须 fail-closed",
    );
    assert.equal(
      shouldCheckIosNativeUpdateOnAppStateChange("background", "active"),
      true,
      "从后台回到前台应重新检查原生版本",
    );
    assert.equal(
      shouldCheckIosNativeUpdateOnAppStateChange("active", "active"),
      false,
      "持续 active 不应重复触发检查",
    );
    assert.equal(
      getIosNativeUpdateBoundaryMode({
        enabled: true,
        initialized: false,
        state: null,
      }),
      "checking",
    );
    assert.equal(
      getIosNativeUpdateBoundaryMode({
        enabled: true,
        initialized: true,
        state: "required",
      }),
      "required",
    );
    assert.equal(
      getIosNativeUpdateBoundaryMode({
        enabled: true,
        initialized: true,
        state: "optional",
      }),
      "content",
    );
    assert.equal(
      getIosNativeUpdateBoundaryMode({
        enabled: false,
        initialized: false,
        state: null,
      }),
      "content",
    );
  }

  {
    assert.deepEqual(
      normalizeIosNativeUpdateDecision({
        ...requiredDecision,
        releaseMessage: "  请升级后继续使用  ",
      }),
      requiredDecision,
      "合法响应应归一化为稳定客户端合同",
    );
    assert.throws(
      () => normalizeIosNativeUpdateDecision({ ...requiredDecision, state: "force" }),
      /state/,
      "未知状态必须拒绝，不能误判成可选更新",
    );
    assert.throws(
      () =>
        normalizeIosNativeUpdateDecision({
          ...requiredDecision,
          appStoreUrl: "https://example.com/download",
        }),
      /App Store URL/,
      "更新入口只能使用 Apple App Store 域名",
    );
    assert.equal(
      normalizeIosNativeUpdateDecision({
        ...requiredDecision,
        appStoreUrl: "https://itunes.apple.com/au/app/hot-bargain/id6786073002",
      }).appStoreUrl,
      "https://itunes.apple.com/au/app/hot-bargain/id6786073002",
      "应接受中央 Apple Lookup 合同允许的 itunes.apple.com 链接",
    );
    assert.throws(
      () =>
        normalizeIosNativeUpdateDecision({
          ...requiredDecision,
          appStoreUrl: "https://attacker@apps.apple.com/au/app/id6786073002",
        }),
      /App Store URL/,
      "带用户信息的伪装 App Store URL 必须拒绝",
    );
  }

  {
    const storage = new MemoryStorage();
    const outcome = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 1_000,
      fetchDecision: async () => requiredDecision,
    });

    assert.equal(outcome.source, "server");
    assert.deepEqual(outcome.decision, requiredDecision);
    assert.equal(outcome.shouldPromptOptional, false);
    assert.ok(
      await storage.getObject(IOS_NATIVE_REQUIRED_CACHE_KEY),
      "强制策略必须持久化，离线重启后仍可拦截",
    );
  }

  {
    const storage = new MemoryStorage();
    await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 1_000,
      fetchDecision: async () => requiredDecision,
    });

    const outcome = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 2_000,
      fetchDecision: async () => {
        throw new Error("offline");
      },
    });

    assert.equal(outcome.source, "cache");
    assert.deepEqual(outcome.decision, requiredDecision);
    assert.match(String(outcome.error), /offline/);

    assert.deepEqual(
      await readCachedIosNativeRequiredDecision(storage, {
        ...context,
        installedBuild: "29",
      }),
      null,
      "同一营销版本更换 build 后不得沿用旧构建的离线 required 缓存",
    );
    assert.equal(
      await readCachedIosNativeRequiredDecision(storage, {
        ...context,
        installedVersion: "1.1.0",
        installedBuild: "29",
      }),
      null,
      "营销版本升级后必须重新获取决策，不能盲目沿用旧版本 required 缓存",
    );
    assert.equal(
      await readCachedIosNativeRequiredDecision(storage, {
        ...context,
        apiBaseUrl: "http://192.168.31.247:5002/api",
      }),
      null,
      "required 缓存只能绑定可信中央更新地址，不能被业务或局域网 Host 复用",
    );
  }

  {
    const storage = new MemoryStorage();
    storage.seed("mobile-ios-native-update:required:v1", {
      schemaVersion: 1,
      scope: `${context.apiBaseUrl}|${context.installedVersion}`,
      decision: requiredDecision,
    });
    storage.seed("mobile-ios-native-update:optional-reminder:v1", {
      schemaVersion: 1,
      scope: `${context.apiBaseUrl}|${context.installedVersion}`,
      targetVersion: optionalDecision.latestVersion,
      promptedAt: 1_000,
    });

    assert.equal(
      await readCachedIosNativeRequiredDecision(storage, context),
      null,
      "v1 required 缓存必须视为不兼容，不能继续拦截新构建",
    );
    assert.equal(
      storage.has("mobile-ios-native-update:required:v1"),
      false,
      "读取 v2 缓存时应安全清理旧 required 记录",
    );
    assert.equal(
      storage.has("mobile-ios-native-update:optional-reminder:v1"),
      false,
      "读取 v2 缓存时应安全清理旧 optional 提醒记录",
    );
  }

  {
    const storage = new MemoryStorage();
    const outcome = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 1_000,
      fetchDecision: async () => {
        throw new Error("first request failed");
      },
    });

    assert.equal(outcome.source, "none");
    assert.equal(outcome.decision, null, "首次离线且无可信缓存时必须放行");
    assert.deepEqual(
      deriveIosNativeOtaBarrier({
        epoch: 16,
        outcome,
      }),
      { allowed: true, epoch: 16 },
      "真实网络失败仍应返回合法 receipt，首次离线策略保持放行",
    );
  }

  {
    const storage = new MemoryStorage();
    const fetchResult = deferred<typeof requiredDecision>();
    const abortController = new AbortController();
    let receivedSignal: AbortSignal | undefined;
    const check = checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 1_000,
      signal: abortController.signal,
      fetchDecision: async (signal) => {
        receivedSignal = signal;
        return fetchResult.promise;
      },
    });

    abortController.abort();
    fetchResult.resolve(requiredDecision);

    await assert.rejects(
      check,
      (error: unknown) =>
        error instanceof Error && error.name === "AbortError",
      "generation 失效后，迟到的原生决策必须作为取消而不是网络失败 receipt",
    );
    assert.equal(receivedSignal, abortController.signal);
    assert.equal(
      await storage.getObject(IOS_NATIVE_REQUIRED_CACHE_KEY),
      null,
      "取消后的迟到响应不得继续写入本轮 required 缓存",
    );
  }

  {
    const failingStorage: IosNativeUpdateStorage = {
      getObject: async () => {
        throw new Error("storage unavailable");
      },
      setObject: async () => {
        throw new Error("storage unavailable");
      },
      removeItem: async () => {
        throw new Error("storage unavailable");
      },
    };

    const required = await checkIosNativeAppUpdate({
      context,
      storage: failingStorage,
      now: () => 1_000,
      fetchDecision: async () => requiredDecision,
    });
    assert.deepEqual(
      required.decision,
      requiredDecision,
      "服务端已明确强制时，即使缓存写入失败也必须立即拦截",
    );

    const offline = await checkIosNativeAppUpdate({
      context,
      storage: failingStorage,
      now: () => 2_000,
      fetchDecision: async () => {
        throw new Error("offline");
      },
      memoryRequiredDecision: required.decision,
    });
    assert.equal(
      offline.source,
      "memory",
      "required 已进入同代内存后，缓存故障与离线不得降级为首次放行",
    );
    assert.deepEqual(
      offline.decision,
      requiredDecision,
      "同一 context/generation 内既有 required 必须单调保留",
    );

    const optionalReminderSession = createIosNativeOptionalReminderSession();
    const firstOptional = await checkIosNativeAppUpdate({
      context,
      storage: failingStorage,
      now: () => 3_000,
      fetchDecision: async () => optionalDecision,
      optionalReminderSession,
    });
    assert.equal(
      firstOptional.shouldPromptOptional,
      true,
      "提醒存储异常时，本进程首次发现可选版本仍应提示",
    );

    await assert.rejects(
      () =>
        markIosNativeOptionalReminder(
          failingStorage,
          context,
          optionalDecision,
          3_000,
        ),
      /storage unavailable/,
      "持久化提醒失败不得跳过后续进程内会话去重",
    );
    optionalReminderSession.markSeen(context, optionalDecision);
    const repeatedOptional = await checkIosNativeAppUpdate({
      context,
      storage: failingStorage,
      now: () => 4_000,
      fetchDecision: async () => optionalDecision,
      optionalReminderSession,
    });
    assert.equal(
      repeatedOptional.shouldPromptOptional,
      false,
      "同一进程已展示可选版本后，存储持续异常也不得重复提示并阻塞 OTA",
    );

    const newerOptional = await checkIosNativeAppUpdate({
      context,
      storage: failingStorage,
      now: () => 5_000,
      fetchDecision: async () => ({
        ...optionalDecision,
        latestVersion: "1.3.0",
      }),
      optionalReminderSession,
    });
    assert.equal(
      newerOptional.shouldPromptOptional,
      true,
      "本进程会话去重必须按目标版本隔离，发现新版本仍应提示",
    );
  }

  {
    const storage = new MemoryStorage();
    await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 1_000,
      fetchDecision: async () => requiredDecision,
    });

    const optional = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 2_000,
      fetchDecision: async () => optionalDecision,
    });
    assert.equal(optional.shouldPromptOptional, true, "首次发现可选版本应提示");
    assert.equal(
      await storage.getObject(IOS_NATIVE_REQUIRED_CACHE_KEY),
      null,
      "服务端明确降级为可选后必须解除强制缓存",
    );

    await markIosNativeOptionalReminder(storage, context, optionalDecision, 2_000);
    assert.ok(await storage.getObject(IOS_NATIVE_OPTIONAL_REMINDER_KEY));

    const within24Hours = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 2_000 + 24 * 60 * 60 * 1_000 - 1,
      fetchDecision: async () => optionalDecision,
    });
    assert.equal(within24Hours.shouldPromptOptional, false, "24 小时内同一目标版本不得重复提示");

    const nextBuild = await checkIosNativeAppUpdate({
      context: { ...context, installedBuild: "29" },
      storage,
      now: () => 2_001,
      fetchDecision: async () => optionalDecision,
    });
    assert.equal(
      nextBuild.shouldPromptOptional,
      true,
      "同一营销版本换 build 后不得沿用旧构建的 24 小时提醒记录",
    );

    const after24Hours = await checkIosNativeAppUpdate({
      context,
      storage,
      now: () => 2_000 + 24 * 60 * 60 * 1_000,
      fetchDecision: async () => optionalDecision,
    });
    assert.equal(after24Hours.shouldPromptOptional, true, "满 24 小时后可再次提醒");
  }

  {
    const storage = new MemoryStorage();
    const optionalReminderSession = createIosNativeOptionalReminderSession();
    optionalReminderSession.markSeen(context, optionalDecision);

    const nextBuild = await checkIosNativeAppUpdate({
      context: { ...context, installedBuild: "29" },
      storage,
      now: () => 8_000,
      fetchDecision: async () => optionalDecision,
      optionalReminderSession,
    });
    assert.equal(
      nextBuild.shouldPromptOptional,
      true,
      "进程内 optional 去重也必须按 installedBuild 隔离",
    );
  }

  console.log("ios-native-app-update.test.ts: ok");
}

void run();
