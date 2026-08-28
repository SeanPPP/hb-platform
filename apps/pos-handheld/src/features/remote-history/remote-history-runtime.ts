import { HbposRemoteHistoryApi } from "@hb/pos-api-client/features/remote-history/remote-history-api";
import {
  RemoteHistoryPresenter,
  type RemoteHistoryReprintPort,
  type RemoteHistoryPresenterOptions,
} from "@hb/pos-domain/features/remote-history/remote-history-presenter";

import type { HbposTransport } from "@/core/api/hbpos-api";

export type RemoteHistoryPresenterFactoryInput = Readonly<{
  online: boolean;
}>;

export type RemoteHistoryTrustedSession = Readonly<
  Pick<
    RemoteHistoryPresenterOptions,
    "trustedStoreCode" | "currentDeviceCode" | "permissionCodes"
  >
>;

export type ResolveRemoteHistoryTrustedSession =
  () => RemoteHistoryTrustedSession;
export type ResolveRemoteHistoryReprintPort =
  () => RemoteHistoryReprintPort | null;

export type RemoteHistoryPresenterFactory = Readonly<{
  createPresenter(
    input: RemoteHistoryPresenterFactoryInput,
  ): RemoteHistoryPresenter;
}>;

/**
 * 组合根可注入此 factory；feature 内固定使用只读 API adapter，路由只交付可信会话身份。
 */
export function createHbposRemoteHistoryPresenterFactory(
  transport: HbposTransport,
  resolveTrustedSession: ResolveRemoteHistoryTrustedSession,
  reprintPort:
    | RemoteHistoryReprintPort
    | ResolveRemoteHistoryReprintPort
    | null = null,
): RemoteHistoryPresenterFactory {
  return Object.freeze({
    createPresenter(input: RemoteHistoryPresenterFactoryInput) {
      const trusted = resolveTrustedSession();
      // 生产组合根可在 presenter 创建时绑定收银员 lease；静态 Port 仅保留给隔离测试。
      const boundReprintPort =
        typeof reprintPort === "function"
          ? reprintPort()
          : reprintPort;
      return new RemoteHistoryPresenter({
        ...trusted,
        online: input.online,
        port: new HbposRemoteHistoryApi(
          transport,
          trusted.trustedStoreCode,
        ),
        reprintPort: boundReprintPort,
      });
    },
  });
}

/**
 * ExpoPosRuntimeServices 尚未冻结 remoteHistory 字段时使用结构化解析。
 * 缺少接线会返回 null，由界面显示受控不可用状态，绝不绕过 transport。
 */
export function resolveRemoteHistoryPresenterFactory(
  services: unknown,
): RemoteHistoryPresenterFactory | null {
  if (!isRecord(services)) return null;
  const candidate = services.remoteHistory;
  if (!isRecord(candidate) || typeof candidate.createPresenter !== "function") {
    return null;
  }
  return candidate as RemoteHistoryPresenterFactory;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
