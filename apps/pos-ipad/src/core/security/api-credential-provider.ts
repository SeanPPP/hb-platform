import type {
  HbposAuthenticationFailureHandler,
  HbposRequestCredentialProvider,
  HbposRequestCredentials,
} from "../api/axios-transport";

import type { CashierSessionInvalidationBus } from "@hb/pos-domain/core/security/cashier-session-invalidation";
import { DeviceSessionCoordinator } from "./device-session";
import { CashierAuthorizationStore } from "./secure-storage";

/**
 * Axios 只通过该提供者读取 Keychain 中的授权，业务代码不得自行拼接敏感请求头。
 */
export class SecurityApiCredentialProvider implements HbposRequestCredentialProvider, HbposAuthenticationFailureHandler {
  public constructor(
    private readonly deviceSession: DeviceSessionCoordinator,
    private readonly cashierAuthorization: CashierAuthorizationStore,
    private readonly invalidation?: CashierSessionInvalidationBus,
  ) {}

  public async getCredentials(): Promise<HbposRequestCredentials> {
    // 先取得实际可出站的设备 scope；没有设备凭据时绝不能单独附加收银员 bearer。
    const device = await this.deviceSession.getTransportCredentials();
    if (!device) {
      return {};
    }
    const cashierAuthorization = await this.cashierAuthorization.get({
      storeCode: device.storeCode,
      deviceCode: device.deviceCode,
    });
    return {
      device,
      ...(cashierAuthorization ? { cashierAuthorization } : {})
    };
  }

  /** 401 只失效收银员票据；待同步订单仍由 outbox 保留，重新登录后再恢复。 */
  public async onUnauthorized(): Promise<void> {
    try {
      await this.cashierAuthorization.clear();
    } finally {
      // Keychain 清理失败时也必须让组合根立即撤销内存中的可信收银员会话。
      this.invalidation?.notify("unauthorized");
    }
  }

  /** 仅处理传输层已确认的设备撤销 403；普通权限拒绝不会进入此方法。 */
  public async onForbidden(): Promise<void> {
    const [lockResult, clearResult] = await Promise.allSettled([
      this.deviceSession.lockFromAuthorizationFailure("HBPOS request was forbidden."),
      this.cashierAuthorization.clear(),
    ]);

    // 任一持久化安全动作失败都不能阻止内存态 fail-closed；事件总线会隔离监听器异常。
    this.invalidation?.notify("forbidden");

    // 设备锁定是明确撤销的主要安全动作；两项均失败时稳定保留它的原始异常。
    if (lockResult.status === "rejected") {
      throw lockResult.reason;
    }
    if (clearResult.status === "rejected") {
      throw clearResult.reason;
    }
  }
}
