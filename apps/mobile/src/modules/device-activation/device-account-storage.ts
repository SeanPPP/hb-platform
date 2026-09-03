import { parseDeviceActivationCode } from "./device-activation-code";
import type {
  MobileDeviceActivationBinding,
  PendingMobileDeviceActivation,
  StoredMobileDeviceAccountBinding,
} from "./types";

const DEVICE_ACCOUNT_CREDENTIAL_KEY = "hbmobile.device-account-credential.v1";
const DEVICE_ACCOUNT_PRESENTATION_KEY = "hbmobile.device-account-presentation.v1";
const DEVICE_ACCOUNT_PENDING_KEY = "hbmobile.device-account-pending.v1";

export interface DeviceAccountKeyValueStorage {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
}

interface DeviceAccountStorageDependencies {
  secure: DeviceAccountKeyValueStorage;
  presentation: DeviceAccountKeyValueStorage;
}

interface StoredBindingEnvelope {
  version: 1;
  binding: MobileDeviceActivationBinding;
  apiHost: string;
  hardwareId: string;
  credential: string;
}

interface StoredPresentationEnvelope {
  version: 1;
  binding: MobileDeviceActivationBinding;
  apiHost: string;
  hardwareId: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object";
}

function readRequiredString(value: unknown, field: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`Stored mobile device account ${field} is invalid.`);
  }
  return value;
}

function readRequiredNumber(value: unknown, field: string): number {
  const numberValue = Number(value);
  if (!Number.isSafeInteger(numberValue) || numberValue <= 0) {
    throw new Error(`Stored mobile device account ${field} is invalid.`);
  }
  return numberValue;
}

function parseBinding(value: unknown): MobileDeviceActivationBinding {
  if (!isRecord(value)) {
    throw new Error("Stored mobile device account binding is invalid.");
  }

  const bindingId = readRequiredString(value.bindingId, "bindingId");
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(bindingId)) {
    throw new Error("Stored mobile device account bindingId is invalid.");
  }

  return {
    bindingId,
    deviceRegistrationId: readRequiredNumber(
      value.deviceRegistrationId,
      "deviceRegistrationId",
    ),
    deviceCode: readRequiredString(value.deviceCode, "deviceCode"),
    storeCode: readRequiredString(value.storeCode, "storeCode"),
    storeName: readRequiredString(value.storeName, "storeName"),
    deviceSystem: readRequiredString(value.deviceSystem, "deviceSystem"),
    targetUserGuid: readRequiredString(value.targetUserGuid, "targetUserGuid"),
    targetUsername: readRequiredString(value.targetUsername, "targetUsername"),
    targetFullName:
      typeof value.targetFullName === "string" ? value.targetFullName : null,
    boundAtUtc: readRequiredString(value.boundAtUtc, "boundAtUtc"),
  };
}

function parseStoredBinding(raw: string): StoredMobileDeviceAccountBinding {
  const parsed = JSON.parse(raw) as unknown;
  if (!isRecord(parsed) || parsed.version !== 1) {
    throw new Error("Stored mobile device account credential is invalid.");
  }

  return {
    binding: parseBinding(parsed.binding),
    apiHost: readRequiredString(parsed.apiHost, "apiHost"),
    hardwareId: readRequiredString(parsed.hardwareId, "hardwareId"),
    credential: readRequiredString(parsed.credential, "credential"),
  };
}

function parsePending(raw: string): PendingMobileDeviceActivation {
  const parsed = JSON.parse(raw) as unknown;
  if (!isRecord(parsed) || parsed.version !== 1) {
    throw new Error("Stored pending mobile device activation is invalid.");
  }

  const activationCode =
    typeof parsed.activationCode === "string"
      ? parseDeviceActivationCode(parsed.activationCode)
      : null;
  const credentialVerifier = readRequiredString(
    parsed.credentialVerifier,
    "credentialVerifier",
  );
  if (!activationCode || !/^[0-9a-f]{64}$/u.test(credentialVerifier)) {
    throw new Error("Stored pending mobile device activation is invalid.");
  }
  if (parsed.mode !== "redeem" && parsed.mode !== "rebind") {
    throw new Error("Stored pending mobile device activation mode is invalid.");
  }
  if (parsed.deviceSystem !== "Android" && parsed.deviceSystem !== "iOS") {
    throw new Error("Stored pending mobile device activation system is invalid.");
  }
  const currentHardwareId =
    typeof parsed.currentHardwareId === "string" && parsed.currentHardwareId
      ? parsed.currentHardwareId
      : null;
  const currentCredential =
    typeof parsed.currentCredential === "string" && parsed.currentCredential
      ? parsed.currentCredential
      : null;
  if (parsed.mode === "rebind" && (!currentHardwareId || !currentCredential)) {
    throw new Error("Stored pending mobile device activation recovery identity is invalid.");
  }

  return {
    version: 1,
    mode: parsed.mode,
    activationCode,
    apiHost: readRequiredString(parsed.apiHost, "apiHost"),
    hardwareId: readRequiredString(parsed.hardwareId, "hardwareId"),
    deviceSystem: parsed.deviceSystem,
    credential: readRequiredString(parsed.credential, "credential"),
    credentialVerifier,
    ...(typeof parsed.deviceName === "string" && parsed.deviceName
      ? { deviceName: parsed.deviceName }
      : {}),
    ...(currentHardwareId ? { currentHardwareId } : {}),
    ...(currentCredential ? { currentCredential } : {}),
  };
}

export function createDeviceAccountStorage({
  secure,
  presentation,
}: DeviceAccountStorageDependencies) {
  return {
    async loadBinding(): Promise<StoredMobileDeviceAccountBinding | null> {
      const raw = await secure.getItem(DEVICE_ACCOUNT_CREDENTIAL_KEY);
      return raw ? parseStoredBinding(raw) : null;
    },

    async loadPresentation(): Promise<StoredPresentationEnvelope | null> {
      const raw = await presentation.getItem(DEVICE_ACCOUNT_PRESENTATION_KEY);
      if (!raw) {
        return null;
      }
      const parsed = JSON.parse(raw) as unknown;
      if (!isRecord(parsed) || parsed.version !== 1) {
        return null;
      }
      try {
        return {
          version: 1,
          binding: parseBinding(parsed.binding),
          apiHost: readRequiredString(parsed.apiHost, "apiHost"),
          hardwareId: readRequiredString(parsed.hardwareId, "hardwareId"),
        };
      } catch {
        return null;
      }
    },

    async saveBinding(value: StoredMobileDeviceAccountBinding) {
      const envelope: StoredBindingEnvelope = {
        version: 1,
        binding: value.binding,
        apiHost: value.apiHost,
        hardwareId: value.hardwareId,
        credential: value.credential,
      };
      const display: StoredPresentationEnvelope = {
        version: 1,
        binding: value.binding,
        apiHost: value.apiHost,
        hardwareId: value.hardwareId,
      };

      // 先写安全凭据；即使展示缓存写入失败，认证材料也不会落入 AsyncStorage。
      await secure.setItem(DEVICE_ACCOUNT_CREDENTIAL_KEY, JSON.stringify(envelope));
      await presentation.setItem(
        DEVICE_ACCOUNT_PRESENTATION_KEY,
        JSON.stringify(display),
      );
    },

    async clearBinding() {
      await Promise.all([
        secure.removeItem(DEVICE_ACCOUNT_CREDENTIAL_KEY),
        presentation.removeItem(DEVICE_ACCOUNT_PRESENTATION_KEY),
      ]);
    },

    async loadPending(): Promise<PendingMobileDeviceActivation | null> {
      const raw = await secure.getItem(DEVICE_ACCOUNT_PENDING_KEY);
      return raw ? parsePending(raw) : null;
    },

    async savePending(value: PendingMobileDeviceActivation) {
      const normalized = parsePending(JSON.stringify(value));
      await secure.setItem(DEVICE_ACCOUNT_PENDING_KEY, JSON.stringify(normalized));
    },

    async clearPending() {
      await secure.removeItem(DEVICE_ACCOUNT_PENDING_KEY);
    },
  };
}
