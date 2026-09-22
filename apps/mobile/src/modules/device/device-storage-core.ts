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
  let sessionRevision = 0;
  let activeSessionWrites = 0;
  function beginSessionWrite() {
    sessionRevision += 1;
    activeSessionWrites += 1;
  }
  function endSessionWrite() {
    sessionRevision += 1;
    activeSessionWrites -= 1;
  }

  async function readSession(migrateLegacy: boolean): Promise<{
    session: PersistedDeviceSession | null;
    requiresMigration: boolean;
  }> {
    // 安全存储读取可能先失败；立即收敛结果，避免无效资料提前返回时留下未处理拒绝。
    const authCodeResult = Promise.resolve()
      .then(() => sensitive.getItem(SECURE_LEGACY_AUTH_CODE_KEY))
      .then(
        (value) => ({ ok: true as const, value }),
        (error: unknown) => ({ ok: false as const, error }),
      );
    const raw = await presentation.getItem(DEVICE_SESSION_PRESENTATION_KEY);
    if (!raw) return { session: null, requiresMigration: false };
    const parsed = parsePresentation(raw);
    if (
      !parsed ||
      typeof parsed.hardwareId !== "string" ||
      typeof parsed.storeCode !== "string"
    ) {
      return { session: null, requiresMigration: false };
    }

    const legacyAuthCode =
      typeof parsed.authCode === "string" && parsed.authCode
        ? parsed.authCode
        : null;
    const secureResult = await authCodeResult;
    if (!secureResult.ok) throw secureResult.error;
    let authCode = secureResult.value;
    if (legacyAuthCode && migrateLegacy) {
      beginSessionWrite();
      try {
        await sensitive.setItem(SECURE_LEGACY_AUTH_CODE_KEY, legacyAuthCode);
        authCode = legacyAuthCode;
        await presentation.setItem(
          DEVICE_SESSION_PRESENTATION_KEY,
          JSON.stringify(toPresentation(parsed as PersistedDeviceSession)),
        );
      } finally {
        endSessionWrite();
      }
    }

    return {
      session: {
        ...(parsed as DeviceSessionPresentation),
        authCode: legacyAuthCode ?? authCode ?? "",
      },
      requiresMigration: Boolean(legacyAuthCode),
    };
  }

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
      return (await readSession(true)).session;
    },

    async peekSession() {
      // 请求预读只读取凭据；旧格式由真正需要设备会话的调用方触发迁移。
      const revision = sessionRevision;
      const startedStable = activeSessionWrites === 0;
      const result = await readSession(false);
      return {
        ...result,
        revision,
        stable: startedStable && revision === sessionRevision && activeSessionWrites === 0,
      };
    },

    isSessionSnapshotCurrent(snapshot: { revision: number; stable: boolean }) {
      return snapshot.stable && snapshot.revision === sessionRevision && activeSessionWrites === 0;
    },

    async setSession(session: PersistedDeviceSession) {
      beginSessionWrite();
      try {
        if (session.authCode) {
          await sensitive.setItem(SECURE_LEGACY_AUTH_CODE_KEY, session.authCode);
        }
        await presentation.setItem(
          DEVICE_SESSION_PRESENTATION_KEY,
          JSON.stringify(toPresentation(session)),
        );
      } finally {
        endSessionWrite();
      }
    },

    async clearSession() {
      beginSessionWrite();
      try {
        // 一项删除失败时也要等另一项结束，避免提前把仍在变化的会话标记为稳定。
        const results = await Promise.allSettled([
          presentation.removeItem(DEVICE_SESSION_PRESENTATION_KEY),
          sensitive.removeItem(SECURE_LEGACY_AUTH_CODE_KEY),
        ]);
        const failure = results.find((result) => result.status === "rejected");
        if (failure?.status === "rejected") throw failure.reason;
      } finally {
        endSessionWrite();
      }
    },
  };
}
