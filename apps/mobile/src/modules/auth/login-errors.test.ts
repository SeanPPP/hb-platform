import {
  getFriendlyDeviceLoginErrorDescriptor,
  getFriendlyLoginErrorDescriptor,
} from "./login-errors";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  getFriendlyLoginErrorDescriptor(new Error("用户名或密码错误")).key,
  "errors.invalidCredentials",
  "wrapped backend credential message stays friendly"
);

assertEqual(
  getFriendlyLoginErrorDescriptor(new Error("账号已停用")).key,
  "errors.accountUnavailable",
  "disabled account gets admin-contact guidance"
);

assertEqual(
  getFriendlyLoginErrorDescriptor({ message: "Network Error" }).key,
  "errors.network",
  "network failure gets connection guidance"
);

assertEqual(
  getFriendlyDeviceLoginErrorDescriptor(new Error("设备未授权，请重新绑定")).key,
  "device.loginUnauthorized",
  "device auth failure gets device-specific guidance"
);
