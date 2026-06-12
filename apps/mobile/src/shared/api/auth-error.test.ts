import { isUnauthenticatedApiPayload } from "./auth-error";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  isUnauthenticatedApiPayload({
    success: false,
    message: "未登录或登录已过期，请重新登录",
  }),
  true,
  "business envelope with not logged in message is treated as unauthenticated"
);

assertEqual(
  isUnauthenticatedApiPayload({
    isSuccess: false,
    Message: "Unauthorized",
  }),
  true,
  "business envelope with unauthorized message is treated as unauthenticated"
);

assertEqual(
  isUnauthenticatedApiPayload({
    success: false,
    message: "设备未授权，请重新登录",
  }),
  true,
  "device authorization failure returns to login"
);

assertEqual(
  isUnauthenticatedApiPayload({
    success: false,
    message: "请求参数验证失败",
  }),
  false,
  "validation envelope is not treated as unauthenticated"
);
