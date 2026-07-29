import type { DeviceRegistrationStore } from "../api/hbpos-api";
import type {
  DeviceSessionCoordinator,
  DeviceSessionState,
} from "../security/device-session";

export type PosDeviceSessionRuntimeService = Readonly<{
  listRegistrationStores(): Promise<readonly DeviceRegistrationStore[]>;
  register(input: Readonly<{
    storeCode: string;
  }>): Promise<DeviceSessionState>;
  poll(): Promise<DeviceSessionState>;
  reregister(input: Readonly<{
    targetStoreCode: string;
    terminalName?: string;
  }>): Promise<DeviceSessionState>;
  getDeviceIdentity(): Promise<Readonly<{
    deviceCode: string;
    storeCode: string;
  }> | null>;
}>;

/**
 * React route 只能取得注册与脱敏身份能力。协调器的 authorizationCode、
 * hardwareId、请求头和锁机入口始终留在 Expo 组合根。
 */
export function createPublicDeviceSession(
  coordinator: DeviceSessionCoordinator,
  listRegistrationStores: () => Promise<
    readonly DeviceRegistrationStore[]
  >,
): PosDeviceSessionRuntimeService {
  return Object.freeze({
    listRegistrationStores,
    register: (input) => coordinator.register(input),
    poll: () => coordinator.poll(),
    reregister: (input) => coordinator.reregister(input),
    getDeviceIdentity: () => coordinator.getDeviceIdentity(),
  });
}
