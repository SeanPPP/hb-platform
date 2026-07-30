export const IOS_NATIVE_REQUIRED_CACHE_KEY = "mobile-ios-native-update:required:v1";
export const IOS_NATIVE_OPTIONAL_REMINDER_KEY = "mobile-ios-native-update:optional-reminder:v1";

const OPTIONAL_REMINDER_INTERVAL_MS = 24 * 60 * 60 * 1_000;

export type IosNativeUpdateState = "none" | "optional" | "required";

export type IosNativeUpdateDecision = {
  state: IosNativeUpdateState;
  policyVersion: string;
  latestVersion: string | null;
  minimumSupportedVersion: string | null;
  appStoreUrl: string | null;
  releaseMessage: string | null;
};

export type IosNativeUpdateContext = {
  apiBaseUrl: string;
  installedVersion: string;
  installedBuild: string;
};

export interface IosNativeUpdateStorage {
  getObject<T>(key: string): Promise<T | null>;
  setObject(key: string, value: unknown): Promise<void>;
  removeItem(key: string): Promise<void>;
}

export interface IosNativeOptionalReminderSession {
  hasSeen(
    context: IosNativeUpdateContext,
    decision: IosNativeUpdateDecision,
  ): boolean;
  markSeen(
    context: IosNativeUpdateContext,
    decision: IosNativeUpdateDecision,
  ): void;
}

export type IosNativeUpdateCheckOutcome = {
  source: "server" | "cache" | "memory" | "none";
  decision: IosNativeUpdateDecision | null;
  shouldPromptOptional: boolean;
  error?: unknown;
  storageError?: unknown;
};

export type IosNativeUpdateCheckReceipt = {
  epoch: number;
  outcome: IosNativeUpdateCheckOutcome;
};

export type IosNativeOtaBarrier = {
  allowed: boolean;
  epoch: number;
};

type RequiredCacheRecord = {
  schemaVersion: 1;
  scope: string;
  decision: IosNativeUpdateDecision;
};

type OptionalReminderRecord = {
  schemaVersion: 1;
  scope: string;
  targetVersion: string;
  promptedAt: number;
};

function asRecord(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("iOS native update decision must be an object");
  }
  return value as Record<string, unknown>;
}

function requiredText(value: unknown, field: string) {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`iOS native update ${field} is required`);
  }
  return value.trim();
}

function optionalText(value: unknown) {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function normalizeAppStoreUrl(value: unknown, required: boolean) {
  const text = optionalText(value);
  if (!text) {
    if (required) {
      throw new Error("iOS native update App Store URL is required");
    }
    return null;
  }

  let url: URL;
  try {
    url = new URL(text);
  } catch {
    throw new Error("iOS native update App Store URL is invalid");
  }

  const hostname = url.hostname.toLowerCase();
  const isAppleStoreHost =
    hostname === "apps.apple.com" || hostname === "itunes.apple.com";
  if (
    url.protocol !== "https:"
    || !isAppleStoreHost
    || Boolean(url.username)
    || Boolean(url.password)
  ) {
    throw new Error("iOS native update App Store URL is not trusted");
  }
  return url.toString();
}

export function normalizeIosNativeUpdateDecision(value: unknown): IosNativeUpdateDecision {
  const record = asRecord(value);
  const state = record.state;
  if (state !== "none" && state !== "optional" && state !== "required") {
    throw new Error("iOS native update state must be none, optional, or required");
  }

  const latestVersion = optionalText(record.latestVersion);
  if (state !== "none" && !latestVersion) {
    throw new Error("iOS native update latestVersion is required");
  }

  return {
    state,
    policyVersion: requiredText(record.policyVersion, "policyVersion"),
    latestVersion,
    minimumSupportedVersion: optionalText(record.minimumSupportedVersion),
    appStoreUrl: normalizeAppStoreUrl(record.appStoreUrl, state !== "none"),
    releaseMessage: optionalText(record.releaseMessage),
  };
}

export function shouldEnableIosNativeUpdate(input: {
  platform: string;
  buildProfile: unknown;
  isDevelopment: boolean;
  reviewGuardActive: boolean;
}) {
  const profile =
    typeof input.buildProfile === "string" ? input.buildProfile.trim().toLowerCase() : "";
  return (
    input.platform === "ios"
    && profile === "production"
    && !input.isDevelopment
    && !input.reviewGuardActive
  );
}

export function shouldPauseAutomaticOtaForIosNativeUpdate(input: {
  enabled: boolean;
  initialized: boolean;
  state: IosNativeUpdateState | null;
  optionalPromptActive: boolean;
}) {
  return (
    input.enabled
    && (
      !input.initialized
      || input.state === "required"
      || input.optionalPromptActive
    )
  );
}

export function shouldActivateIosNativeOptionalPrompt(input: {
  decision: IosNativeUpdateDecision | null;
  shouldPromptOptional: boolean;
}) {
  return Boolean(
    input.decision?.state === "optional"
    && input.shouldPromptOptional
    && input.decision.appStoreUrl,
  );
}

export function deriveIosNativeOtaBarrier(
  receipt: IosNativeUpdateCheckReceipt,
): IosNativeOtaBarrier {
  const { decision, shouldPromptOptional } = receipt.outcome;
  return {
    allowed:
      decision?.state !== "required" &&
      !shouldActivateIosNativeOptionalPrompt({
        decision,
        shouldPromptOptional,
      }),
    epoch: receipt.epoch,
  };
}

export function shouldCheckIosNativeUpdateOnAppStateChange(
  previousState: string,
  nextState: string,
) {
  return previousState !== "active" && nextState === "active";
}

export function getIosNativeUpdateBoundaryMode(input: {
  enabled: boolean;
  initialized: boolean;
  state: IosNativeUpdateState | null;
}): "content" | "checking" | "required" {
  if (!input.enabled) {
    return "content";
  }
  if (!input.initialized) {
    return "checking";
  }
  return input.state === "required" ? "required" : "content";
}

function buildContextScope(context: IosNativeUpdateContext) {
  const apiBaseUrl = context.apiBaseUrl.trim().replace(/\/+$/, "");
  // build number 仅随服务端请求用于审计；required 门禁按营销版本延续，不能因同版本换 build 失效。
  return `${apiBaseUrl}|${context.installedVersion.trim()}`;
}

function buildOptionalReminderSessionKey(
  context: IosNativeUpdateContext,
  decision: IosNativeUpdateDecision,
) {
  if (decision.state !== "optional" || !decision.latestVersion) {
    return null;
  }
  return JSON.stringify([
    buildContextScope(context),
    decision.latestVersion,
  ]);
}

export function createIosNativeOptionalReminderSession(): IosNativeOptionalReminderSession {
  const seenTargets = new Set<string>();
  return {
    hasSeen(context, decision) {
      const key = buildOptionalReminderSessionKey(context, decision);
      return key !== null && seenTargets.has(key);
    },
    markSeen(context, decision) {
      const key = buildOptionalReminderSessionKey(context, decision);
      if (key) {
        seenTargets.add(key);
      }
    },
  };
}

export async function readCachedIosNativeRequiredDecision(
  storage: IosNativeUpdateStorage,
  context: IosNativeUpdateContext,
) {
  try {
    const cached = await storage.getObject<RequiredCacheRecord>(IOS_NATIVE_REQUIRED_CACHE_KEY);
    if (
      !cached
      || cached.schemaVersion !== 1
      || cached.scope !== buildContextScope(context)
    ) {
      return null;
    }

    const decision = normalizeIosNativeUpdateDecision(cached.decision);
    return decision.state === "required" ? decision : null;
  } catch {
    // 损坏缓存不具备可信度；清掉后允许后续服务端响应重新建立强制策略。
    try {
      await storage.removeItem(IOS_NATIVE_REQUIRED_CACHE_KEY);
    } catch {
      // 存储整体不可用时仍按“没有可信缓存”处理，避免启动页永久阻塞。
    }
    return null;
  }
}

async function saveRequiredDecision(
  storage: IosNativeUpdateStorage,
  context: IosNativeUpdateContext,
  decision: IosNativeUpdateDecision,
) {
  const record: RequiredCacheRecord = {
    schemaVersion: 1,
    scope: buildContextScope(context),
    decision,
  };
  await storage.setObject(IOS_NATIVE_REQUIRED_CACHE_KEY, record);
}

async function shouldPromptOptionalDecision(
  storage: IosNativeUpdateStorage,
  context: IosNativeUpdateContext,
  decision: IosNativeUpdateDecision,
  now: number,
) {
  if (decision.state !== "optional" || !decision.latestVersion) {
    return false;
  }

  const reminder = await storage.getObject<OptionalReminderRecord>(
    IOS_NATIVE_OPTIONAL_REMINDER_KEY,
  );
  if (
    !reminder
    || reminder.schemaVersion !== 1
    || reminder.scope !== buildContextScope(context)
    || reminder.targetVersion !== decision.latestVersion
    || !Number.isFinite(reminder.promptedAt)
  ) {
    return true;
  }

  return now - reminder.promptedAt >= OPTIONAL_REMINDER_INTERVAL_MS;
}

export async function markIosNativeOptionalReminder(
  storage: IosNativeUpdateStorage,
  context: IosNativeUpdateContext,
  decision: IosNativeUpdateDecision,
  now: number,
) {
  if (decision.state !== "optional" || !decision.latestVersion) {
    return;
  }

  const record: OptionalReminderRecord = {
    schemaVersion: 1,
    scope: buildContextScope(context),
    targetVersion: decision.latestVersion,
    promptedAt: now,
  };
  await storage.setObject(IOS_NATIVE_OPTIONAL_REMINDER_KEY, record);
}

export async function checkIosNativeAppUpdate(input: {
  context: IosNativeUpdateContext;
  storage: IosNativeUpdateStorage;
  now: () => number;
  fetchDecision: () => Promise<unknown>;
  optionalReminderSession?: IosNativeOptionalReminderSession;
  memoryRequiredDecision?: IosNativeUpdateDecision | null;
}): Promise<IosNativeUpdateCheckOutcome> {
  let decision: IosNativeUpdateDecision;
  try {
    decision = normalizeIosNativeUpdateDecision(await input.fetchDecision());
  } catch (error) {
    const cachedDecision = await readCachedIosNativeRequiredDecision(
      input.storage,
      input.context,
    );
    const memoryRequiredDecision =
      input.memoryRequiredDecision?.state === "required"
        ? input.memoryRequiredDecision
        : null;
    const fallbackDecision = cachedDecision ?? memoryRequiredDecision;
    return {
      source: cachedDecision
        ? "cache"
        : memoryRequiredDecision
          ? "memory"
          : "none",
      decision: fallbackDecision,
      shouldPromptOptional: false,
      error,
    };
  }

  let storageError: unknown;
  try {
    if (decision.state === "required") {
      await saveRequiredDecision(input.storage, input.context, decision);
    } else {
      await input.storage.removeItem(IOS_NATIVE_REQUIRED_CACHE_KEY);
    }

    if (decision.state === "none") {
      await input.storage.removeItem(IOS_NATIVE_OPTIONAL_REMINDER_KEY);
    }
  } catch (error) {
    // 服务端决策仍然可信；缓存故障只能影响离线延续，不能解除本次强制门禁。
    storageError = error;
  }

  let shouldPromptOptional = false;
  if (decision.state === "optional") {
    if (!input.optionalReminderSession?.hasSeen(input.context, decision)) {
      try {
        shouldPromptOptional = await shouldPromptOptionalDecision(
          input.storage,
          input.context,
          decision,
          input.now(),
        );
      } catch (error) {
        // 持久化不可用时本进程仍提示首次；会话去重会避免后续重复弹窗和 OTA 饥饿。
        shouldPromptOptional = true;
        storageError ??= error;
      }
    }
  }

  return {
    source: "server",
    decision,
    shouldPromptOptional,
    storageError,
  };
}
