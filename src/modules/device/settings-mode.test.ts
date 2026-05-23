import { resolveSettingsAuthMode, shouldShowProfileAction } from "./settings-mode";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  resolveSettingsAuthMode({ hasUser: true, hasDeviceSession: true }),
  "user",
  "user session takes priority over stored device session"
);

assertEqual(
  resolveSettingsAuthMode({ hasUser: false, hasDeviceSession: true }),
  "device",
  "device session without user is device mode"
);

assertEqual(
  shouldShowProfileAction("device"),
  false,
  "device mode hides profile action"
);

assertEqual(
  shouldShowProfileAction("user"),
  true,
  "user mode shows profile action"
);
