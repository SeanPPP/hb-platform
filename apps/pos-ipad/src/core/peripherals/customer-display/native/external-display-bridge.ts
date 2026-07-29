import {
  CustomerDisplaySnapshotSchema,
  type CustomerDisplaySnapshot,
  type DisplayStatus,
  type ExternalCustomerDisplayPort,
} from "../../../contracts/external-display";
import { normalizeLocalAdvertisementUri } from "../local-advertisement-uri";

export type NativeExternalDisplayStatus = {
  state: DisplayStatus;
  enabled: boolean;
  connected: boolean;
  revision: number;
  widthPixels: number;
  heightPixels: number;
  scale: number;
  reason: string;
};

export type NativeExternalDisplayStatusEventKind =
  | "connected"
  | "disconnected"
  | "resolutionChanged"
  | "ready"
  | "failed"
  | "enabledChanged";

export type NativeExternalDisplayStatusEvent = NativeExternalDisplayStatus & {
  event: NativeExternalDisplayStatusEventKind;
};

export type NativeExternalDisplayPublishResult = {
  accepted: boolean;
  revision: number;
  latestRevision: number;
  reason: string;
};

export type NativeExternalDisplaySubscription = {
  remove(): void;
};

export type NativeExternalDisplayEventMap = {
  onStatusChanged: NativeExternalDisplayStatusEvent;
  onSnapshotChanged: CustomerDisplaySnapshot;
};

export type ExternalDisplayNativeModule = {
  getStatus(): Promise<NativeExternalDisplayStatus>;
  setEnabled(enabled: boolean): Promise<NativeExternalDisplayStatus>;
  forceBlank?(): Promise<NativeExternalDisplayStatus>;
  publishSnapshot(
    snapshot: CustomerDisplaySnapshot,
  ): Promise<NativeExternalDisplayPublishResult>;
  markReactSurfaceReady(): Promise<void>;
  markReactSurfaceRendered(surfaceId: string): Promise<void>;
  addListener<EventName extends keyof NativeExternalDisplayEventMap>(
    eventName: EventName,
    listener: (event: NativeExternalDisplayEventMap[EventName]) => void,
  ): NativeExternalDisplaySubscription;
};

export type ExternalCustomerDisplaySafetyPort = Readonly<{
  forceBlank(): Promise<void>;
  disableForSafety(): Promise<void>;
}>;

export type ExternalCustomerDisplayBridge =
  ExternalCustomerDisplayPort & ExternalCustomerDisplaySafetyPort;

const displayStatuses = new Set<DisplayStatus>([
  "disconnected",
  "connecting",
  "ready",
  "failed",
]);
/**
 * 冻结契约本身使用 strict schema；这里再收紧广告地址，避免原生层触发任何网络加载。
 */
export function sanitizeCustomerDisplaySnapshot(
  value: unknown,
  advertisementCacheRootUri?: string | null,
): CustomerDisplaySnapshot {
  const snapshot = CustomerDisplaySnapshotSchema.parse(value);

  if (snapshot.advert !== null) {
    if (!advertisementCacheRootUri) {
      throw new TypeError(
        "advert.localUri requires an advertisement cache root",
      );
    }
    normalizeLocalAdvertisementUri(
      snapshot.advert.localUri,
      advertisementCacheRootUri,
    );
  }

  return snapshot;
}

function normalizeStatus(
  status: NativeExternalDisplayStatus,
): DisplayStatus {
  return displayStatuses.has(status.state) ? status.state : "failed";
}

/**
 * 原生客显是可选外设：连接、断开或渲染异常都不得把错误传播到主收银流程。
 */
export function createExternalDisplayBridge({
  advertisementCacheRootUri,
  nativeModule,
}: {
  advertisementCacheRootUri?: string | null;
  nativeModule: ExternalDisplayNativeModule | null;
}): ExternalCustomerDisplayBridge {
  return {
    async getStatus() {
      if (nativeModule === null) {
        return "disconnected";
      }

      try {
        return normalizeStatus(await nativeModule.getStatus());
      } catch {
        return "failed";
      }
    },

    async setEnabled(enabled) {
      if (nativeModule === null) {
        return;
      }

      try {
        await nativeModule.setEnabled(enabled);
      } catch {
        // 客显不可用不能阻断主收银界面。
      }
    },

    async forceBlank() {
      if (nativeModule === null) {
        return;
      }
      if (typeof nativeModule.forceBlank !== "function") {
        throw new Error(
          "External display native safe-blank capability is unavailable.",
        );
      }

      const result = await nativeModule.forceBlank();
      if (result.reason !== "sensitive-content-reset") {
        throw Object.assign(
          new Error(
            `External display safe blank was rejected: ${result.reason || "invalid-result"}.`,
          ),
          { code: "EXTERNAL_DISPLAY_SAFE_BLANK_REJECTED" },
        );
      }
    },

    async disableForSafety() {
      if (nativeModule === null) {
        return;
      }

      // 普通 setEnabled 保持可选外设的吞错语义；安全收口必须传播原生异常，
      // 并验证旧原生确实隐藏了 window，不能把过期 producer 状态当作成功。
      const result = await nativeModule.setEnabled(false);
      if (result.enabled !== false) {
        throw Object.assign(
          new Error(
            `External display safe disable was rejected: ${result.reason || "invalid-result"}.`,
          ),
          { code: "EXTERNAL_DISPLAY_SAFE_DISABLE_REJECTED" },
        );
      }
    },

    async publish(value) {
      const snapshot = sanitizeCustomerDisplaySnapshot(
        value,
        advertisementCacheRootUri,
      );
      if (nativeModule === null) {
        throw new Error("External display native module is unavailable.");
      }

      const result = await nativeModule.publishSnapshot(snapshot);
      if (
        result.accepted !== true ||
        result.revision !== snapshot.revision ||
        !Number.isSafeInteger(result.latestRevision) ||
        result.latestRevision < snapshot.revision
      ) {
        throw Object.assign(
          new Error(
            `External display rejected snapshot: ${result.reason || "invalid-result"}.`,
          ),
          { code: "EXTERNAL_DISPLAY_SNAPSHOT_REJECTED" },
        );
      }
    },

    subscribe(listener) {
      if (nativeModule === null) {
        return () => {};
      }

      try {
        const subscription = nativeModule.addListener(
          "onStatusChanged",
          (event) => listener(normalizeStatus(event)),
        );
        return () => subscription.remove();
      } catch {
        return () => {};
      }
    },
  };
}
