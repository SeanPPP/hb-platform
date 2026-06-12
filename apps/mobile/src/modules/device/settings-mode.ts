export type SettingsAuthMode = "user" | "device" | "guest";

export function resolveSettingsAuthMode(input: {
  hasUser: boolean;
  hasDeviceSession: boolean;
}): SettingsAuthMode {
  if (input.hasUser) {
    return "user";
  }

  if (input.hasDeviceSession) {
    return "device";
  }

  return "guest";
}

export function shouldShowProfileAction(mode: SettingsAuthMode) {
  return mode === "user";
}
