import { prepareDeviceLoginSession, prepareStoredDeviceSession } from "./device-login-session";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const calls: string[] = [];

async function main() {
  calls.length = 0;

  const ready = await prepareDeviceLoginSession(
    {
      id: 1,
      hardwareId: "device-001",
      systemDeviceNumber: "D001",
      authCode: "auth-001",
      status: 1,
      statusDescription: "启用",
      deviceType: "Mobile",
      deviceSystem: "Android",
      storeCode: "S001",
    },
    {
      clearAccountSession: async () => {
        calls.push("clear-account");
      },
      syncDeviceFromProfile: async () => {
        calls.push("sync-device");
        return {
          hardwareId: "device-001",
          authCode: "auth-001",
          storeCode: "S001",
          storeName: null,
          systemDeviceNumber: "D001",
          status: 1,
          statusDescription: "启用",
          resolvedFromExisting: true,
        };
      },
      validateDevice: async () => {
        calls.push("validate-device");
        return true;
      },
    }
  );

  assertEqual(ready, true, "device login returns validation result");
  assertEqual(
    calls.join(","),
    "clear-account,sync-device,validate-device",
    "device login clears account credentials before validating device auth"
  );

  calls.length = 0;

  const restored = await prepareStoredDeviceSession({
    clearAccountSession: async () => {
      calls.push("clear-account");
    },
    validateDevice: async () => {
      calls.push("validate-device");
      return true;
    },
  });

  assertEqual(restored, true, "stored device restore returns validation result");
  assertEqual(
    calls.join(","),
    "clear-account,validate-device",
    "stored device restore clears account credentials before validating device auth"
  );
}

void main();
