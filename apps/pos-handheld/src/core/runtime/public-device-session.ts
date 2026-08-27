import type {
  DeviceActivationPreviewResponse,
  DeviceRegistrationStore,
} from "../api/hbpos-api";
import type {
  DevicePresentation,
  DeviceSessionCoordinator,
  DeviceSessionState,
} from "../security/device-session";

export type PosDeviceSessionRuntimeService = Readonly<{
  listRegistrationStores(): Promise<readonly DeviceRegistrationStore[]>;
  register(input: Readonly<{
    storeCode: string;
  }>): Promise<DeviceSessionState>;
  previewActivationCode(
    activationCode: string,
  ): Promise<DeviceActivationPreviewResponse>;
  redeemActivationCode(input: Readonly<{
    activationCode: string;
  }>): Promise<DeviceSessionState>;
  rebindActivationCode(input: Readonly<{
    activationCode: string;
    terminalName?: string;
  }>): Promise<DeviceSessionState>;
  restorePendingActivationCode(): Promise<string | null>;
  poll(): Promise<DeviceSessionState>;
  reregister(input: Readonly<{
    targetStoreCode: string;
    terminalName?: string;
  }>): Promise<DeviceSessionState>;
  getDeviceIdentity(): Promise<Readonly<{
    deviceCode: string;
    storeCode: string;
  }> | null>;
  getDevicePresentation(): Promise<DevicePresentation | null>;
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
    previewActivationCode: (activationCode) =>
      coordinator.previewActivationCode(activationCode),
    redeemActivationCode: (input) =>
      coordinator.redeemActivationCode(input),
    rebindActivationCode: (input) =>
      coordinator.rebindActivationCode(input),
    restorePendingActivationCode: () =>
      coordinator.restorePendingActivationCode(),
    poll: () => coordinator.poll(),
    reregister: (input) => coordinator.reregister(input),
    getDeviceIdentity: () => coordinator.getDeviceIdentity(),
    getDevicePresentation: () =>
      coordinator.getDevicePresentation(),
  });
}
