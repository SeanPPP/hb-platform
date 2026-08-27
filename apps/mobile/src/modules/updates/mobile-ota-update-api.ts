import axios from "axios";
import { unwrapApiEnvelope } from "@/shared/api/api-envelope";
import type {
  MobileOtaUpdateContext,
  MobileOtaUpdateDecision,
} from "./mobile-ota-update";

type PublicDecisionRequestConfig = Readonly<{
  params: Record<string, unknown>;
  timeout: number;
  headers: Record<string, string>;
  signal?: AbortSignal;
}>;

type PublicDecisionHttpGet = (
  url: string,
  config: PublicDecisionRequestConfig,
) => Promise<{ data: unknown }>;

const DECISION_FIELDS = [
  "state",
  "policyVersion",
  "appKey",
  "platform",
  "required",
  "clientChannel",
  "releaseChannel",
  "runtimeVersion",
  "updateId",
  "updateGroupId",
  "releaseMessage",
] as const;

const ACTIVE_POLICY_VERSION_PATTERN = /^[1-9]\d*$/;
const MAX_POLICY_VERSION = 9_223_372_036_854_775_807n;

function fail(message: string): never {
  throw new Error(`Mobile OTA decision ${message}`);
}

function isExactObject(value: unknown): value is Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const keys = Object.keys(value);
  return (
    keys.length === DECISION_FIELDS.length
    && DECISION_FIELDS.every((key) => Object.prototype.hasOwnProperty.call(value, key))
  );
}

function isNormalizedNullableText(value: unknown) {
  return value === null
    || (typeof value === "string" && value.length > 0 && value === value.trim());
}

function isActivePolicyVersion(value: unknown) {
  if (typeof value !== "string" || !ACTIVE_POLICY_VERSION_PATTERN.test(value)) {
    return false;
  }
  try {
    return BigInt(value) <= MAX_POLICY_VERSION;
  } catch {
    return false;
  }
}

function expectedReleaseChannelPrefix(context: MobileOtaUpdateContext) {
  return `mobile-${context.clientChannel}-${context.platform.toLowerCase()}-release-`;
}

function assertExactDecisionContract(
  value: unknown,
  context: MobileOtaUpdateContext,
): asserts value is MobileOtaUpdateDecision {
  if (!isExactObject(value)) fail("fields are invalid");

  if (
    value.appKey !== "mobile"
    || value.platform !== context.platform
    || value.clientChannel !== context.clientChannel
    || value.runtimeVersion !== context.runtimeVersion
    || !isNormalizedNullableText(value.releaseMessage)
  ) {
    fail("scope echo is invalid");
  }

  if (value.state === "none") {
    if (
      (value.policyVersion !== "none" && !isActivePolicyVersion(value.policyVersion))
      || value.required !== false
      || value.releaseChannel !== null
      || value.updateId !== null
      || value.updateGroupId !== null
      || value.releaseMessage !== null
    ) {
      fail("none state is invalid");
    }
    return;
  }

  const expectedRequired = value.state === "required";
  const releaseChannelPrefix = expectedReleaseChannelPrefix(context);
  if (
    (value.state !== "optional" && value.state !== "required")
    || value.required !== expectedRequired
    || !isActivePolicyVersion(value.policyVersion)
    || typeof value.releaseChannel !== "string"
    || value.releaseChannel !== value.releaseChannel.trim()
    || !value.releaseChannel.startsWith(releaseChannelPrefix)
    || value.releaseChannel.length <= releaseChannelPrefix.length
    || typeof value.updateId !== "string"
    || !value.updateId.trim()
    || value.updateId !== value.updateId.trim()
    || typeof value.updateGroupId !== "string"
    || !value.updateGroupId.trim()
    || value.updateGroupId !== value.updateGroupId.trim()
  ) {
    fail("active state is invalid");
  }
}

export async function fetchMobileOtaUpdateDecision(
  context: MobileOtaUpdateContext,
  httpGet: PublicDecisionHttpGet = (url, config) => axios.get(url, config),
  signal?: AbortSignal,
) {
  const apiBaseUrl = context.apiBaseUrl.trim().replace(/\/+$/, "");
  const response = await httpGet(`${apiBaseUrl}/app-updates/mobile-ota`, {
    params: {
      platform: context.platform,
      clientChannel: context.clientChannel,
      runtimeVersion: context.runtimeVersion,
      currentUpdateId: context.currentUpdateId,
      currentUpdateGroupId: context.currentUpdateGroupId,
    },
    timeout: 8_000,
    headers: { Accept: "application/json" },
    signal,
  });

  const decision = unwrapApiEnvelope<unknown>(response.data);
  // 公开响应会形成离线强制门禁，额外字段和跨 scope 回显同样视为不可信。
  assertExactDecisionContract(decision, context);
  return Object.freeze({ ...decision });
}
