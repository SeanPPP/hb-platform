import { sha256 } from "js-sha256";

export const IOS_REVIEW_USERNAME = "ios_app_review";

export interface IosReviewConfig {
  enabled?: string | boolean;
  passwordSha256?: string;
}

export interface IosReviewBuildContext extends IosReviewConfig {
  platform: string;
  buildProfile?: string;
}

export type IosReviewAuthenticationResult =
  | { status: "authenticated" }
  | { status: "invalid-password" }
  | { status: "not-applicable" };

const SHA256_PATTERN = /^[a-f0-9]{64}$/i;

export function isIosReviewBuildEnabled(context: IosReviewBuildContext) {
  const enabled =
    context.enabled === true ||
    (typeof context.enabled === "string" &&
      context.enabled.trim().toLowerCase() === "true");

  return (
    context.platform.trim().toLowerCase() === "ios" &&
    context.buildProfile?.trim().toLowerCase() === "production" &&
    enabled &&
    SHA256_PATTERN.test(context.passwordSha256?.trim() ?? "")
  );
}

export function isIosReviewUsername(username: string) {
  return username.trim().toLowerCase() === IOS_REVIEW_USERNAME;
}

function hashesEqual(left: string, right: string) {
  if (left.length !== right.length) return false;

  // 本地凭据不通过网络传输；固定长度逐字符比较，避免过早返回。
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

export function tryAuthenticateIosReview(input: {
  username: string;
  password: string;
  buildContext: IosReviewBuildContext;
}): IosReviewAuthenticationResult {
  if (!isIosReviewBuildEnabled(input.buildContext)) {
    return { status: "not-applicable" };
  }

  if (!isIosReviewUsername(input.username)) {
    return { status: "not-applicable" };
  }

  const expectedHash = input.buildContext.passwordSha256!.trim().toLowerCase();
  const passwordHash = sha256(input.password);
  return hashesEqual(passwordHash, expectedHash)
    ? { status: "authenticated" }
    : { status: "invalid-password" };
}
