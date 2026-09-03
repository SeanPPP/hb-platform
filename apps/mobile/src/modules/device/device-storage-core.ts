import type { PersistedDeviceSession } from "./types";

const LEGACY_INSTALLATION_ID_KEY = "hbweb_device_installation_id";
const DEVICE_SESSION_PRESENTATION_KEY = "hbweb_device_session";
const SECURE_INSTALLATION_ID_KEY = "hbmobile.installation-id.v1";
const SECURE_LEGACY_AUTH_CODE_KEY = "hbmobile.legacy-device-auth-code.v1";

export interface DeviceStorageKeyValuePort {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
  removeItem(key: string): Promise<void>;
}

interface CreateDeviceStorageDependencies {
  presentation: DeviceStorageKeyValuePort;
  sensitive: DeviceStorageKeyValuePort;
  generateInstallationId(): string;
}

type DeviceSessionPresentation = Omit<PersistedDeviceSession, "authCode">;

function parsePresentation(raw: string): Partial<PersistedDeviceSession> | null {
  try {
    const parsed = JSON.parse(raw) as unknown;
    return parsed && typeof parsed === "object"
      ? (parsed as Partial<PersistedDeviceSession>)
      : null;
  } catch {
    return null;
  }
}

function toPresentation(
  session: PersistedDeviceSession,
): DeviceSessionPresentation {
  const { authCode: _authCode, ...presentation } = session;
  void _authCode;
  return presentation;
}

export function createDeviceStorage({
  presentation,
  sensitive,
  generateInstallationId,
}: CreateDeviceStorageDependencies) {
  return {
    async getInstallationId() {
      const secureValue = await sensitive.getItem(SECURE_INSTALLATION_ID_KEY);
      if (secureValue) {
        return secureValue;
      }

      const legacyValue = await presentation.getItem(LEGACY_INSTALLATION_ID_KEY);
      const nextValue = legacyValue || generateInstallationId();
      await sensitive.setItem(SECURE_INSTALLATION_ID_KEY, nextValue);
      if (legacyValue) {
        await presentation.removeItem(LEGACY_INSTALLATION_ID_KEY);
      }
      return nextValue;
    },

    async getSession(): Promise<PersistedDeviceSession | null> {
      const raw = await presentation.getItem(DEVICE_SESSION_PRESENTATION_KEY);
      if (!raw) {
        return null;
      }
      const parsed = parsePresentation(raw);
      if (
        !parsed ||
        typeof parsed.hardwareId !== "string" ||
        typeof parsed.storeCode !== "string"
      ) {
        return null;
      }

      const legacyAuthCode =
        typeof parsed.authCode === "string" && parsed.authCode
          ? parsed.authCode
          : null;
      let authCode = await sensitive.getItem(SECURE_LEGACY_AUTH_CODE_KEY);
      if (legacyAuthCode) {
        await sensitive.setItem(SECURE_LEGACY_AUTH_CODE_KEY, legacyAuthCode);
        authCode = legacyAuthCode;
        await presentation.setItem(
          DEVICE_SESSION_PRESENTATION_KEY,
          JSON.stringify(toPresentation(parsed as PersistedDeviceSession)),
        );
      }

      return {
        ...(parsed as DeviceSessionPresentation),
        authCode: authCode ?? "",
      };
    },

    async setSession(session: PersistedDeviceSession) {
      if (session.authCode) {
        await sensitive.setItem(SECURE_LEGACY_AUTH_CODE_KEY, session.authCode);
      }
      await presentation.setItem(
        DEVICE_SESSION_PRESENTATION_KEY,
        JSON.stringify(toPresentation(session)),
      );
    },

    async clearSession() {
      await Promise.all([
        presentation.removeItem(DEVICE_SESSION_PRESENTATION_KEY),
        sensitive.removeItem(SECURE_LEGACY_AUTH_CODE_KEY),
      ]);
    },
  };
}
