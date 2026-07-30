import axios from "axios";
import { unwrapApiEnvelope } from "@/shared/api/api-envelope";
import {
  normalizeIosNativeUpdateDecision,
  type IosNativeUpdateContext,
  type IosNativeUpdateDecision,
} from "./ios-native-app-update";

type PublicDecisionRequestConfig = {
  params: {
    version: string;
    build: string;
  };
  timeout: number;
  headers: {
    Accept: string;
  };
  signal?: AbortSignal;
};

type PublicDecisionHttpGet = (
  url: string,
  config: PublicDecisionRequestConfig,
) => Promise<{ data: unknown }>;

const DECISION_FIELDS = [
  "state",
  "policyVersion",
  "latestVersion",
  "minimumSupportedVersion",
  "appStoreUrl",
  "releaseMessage",
] as const;

const ACTIVE_POLICY_VERSION_PATTERN = /^[1-9]\d*$/;
const MARKETING_VERSION_PATTERN = /^v?\d+(?:\.\d+){0,3}$/i;
const MAX_POLICY_VERSION = 9_223_372_036_854_775_807n;

function isExactDecisionObject(value: unknown): value is Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return false;
  }

  const keys = Object.keys(value);
  return (
    keys.length === DECISION_FIELDS.length
    && DECISION_FIELDS.every((field) => Object.prototype.hasOwnProperty.call(value, field))
  );
}

function isNormalizedNullableText(value: unknown) {
  return value === null
    || (typeof value === "string" && value.length > 0 && value === value.trim());
}

function isMarketingVersion(value: unknown) {
  return typeof value === "string"
    && value === value.trim()
    && MARKETING_VERSION_PATTERN.test(value);
}

function isActivePolicyVersion(value: unknown) {
  if (
    typeof value !== "string"
    || !ACTIVE_POLICY_VERSION_PATTERN.test(value)
  ) {
    return false;
  }

  try {
    return BigInt(value) <= MAX_POLICY_VERSION;
  } catch {
    return false;
  }
}

function assertExactDecisionContract(value: unknown): asserts value is IosNativeUpdateDecision {
  if (!isExactDecisionObject(value)) {
    throw new Error("iOS native update decision fields are invalid");
  }

  const {
    state,
    policyVersion,
    latestVersion,
    minimumSupportedVersion,
    appStoreUrl,
    releaseMessage,
  } = value;
  if (!isNormalizedNullableText(releaseMessage)) {
    throw new Error("iOS native update decision releaseMessage is invalid");
  }

  if (state === "none") {
    if (
      policyVersion !== "none"
      || latestVersion !== null
      || minimumSupportedVersion !== null
      || appStoreUrl !== null
      || releaseMessage !== null
    ) {
      throw new Error("iOS native update decision none state is invalid");
    }
    return;
  }

  if (
    (state !== "optional" && state !== "required")
    || !isActivePolicyVersion(policyVersion)
    || !isMarketingVersion(latestVersion)
    || (minimumSupportedVersion !== null && !isMarketingVersion(minimumSupportedVersion))
    || (state === "required" && minimumSupportedVersion === null)
    || typeof appStoreUrl !== "string"
    || appStoreUrl !== appStoreUrl.trim()
  ) {
    throw new Error("iOS native update decision active state is invalid");
  }
}

export async function fetchIosNativeUpdateDecision(
  context: IosNativeUpdateContext,
  httpGet: PublicDecisionHttpGet = (url, config) => axios.get(url, config),
  signal?: AbortSignal,
) {
  const apiBaseUrl = context.apiBaseUrl.trim().replace(/\/+$/, "");
  const response = await httpGet(`${apiBaseUrl}/app-updates/mobile-ios`, {
    params: {
      version: context.installedVersion,
      build: context.installedBuild,
    },
    // 启动门禁不能无限等待；失败后由上层按可信强制缓存决定拦截或放行。
    timeout: 8_000,
    headers: {
      Accept: "application/json",
    },
    signal,
  });

  const decision = unwrapApiEnvelope<unknown>(response.data);
  // 中央公开响应是强制门禁的信任边界；缺字段、额外字段或状态组合异常都必须失败关闭。
  assertExactDecisionContract(decision);
  return normalizeIosNativeUpdateDecision(decision);
}
