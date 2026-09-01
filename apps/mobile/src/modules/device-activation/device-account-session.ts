import type { CurrentUser } from "@/modules/auth/types";
import type {
  MobileDeviceAccountTokenResponse,
  StoredMobileDeviceAccountBinding,
} from "./types";

interface DeviceAccountSessionDependencies {
  exchange(
    binding: Pick<
      StoredMobileDeviceAccountBinding,
      "apiHost" | "hardwareId" | "credential"
    >,
  ): Promise<MobileDeviceAccountTokenResponse>;
  saveAccessToken(token: string): Promise<unknown>;
  removeRefreshToken(): Promise<unknown>;
  loadCurrentUser(): Promise<CurrentUser>;
  saveCurrentUser(user: CurrentUser): Promise<unknown>;
  markDeviceAccountSession(): Promise<unknown>;
  loadNavigationMenu(): Promise<unknown>;
}

export async function establishDeviceAccountSession(
  binding: Pick<
    StoredMobileDeviceAccountBinding,
    "apiHost" | "hardwareId" | "credential"
  >,
  dependencies: DeviceAccountSessionDependencies,
) {
  const token = await dependencies.exchange(binding);
  if (!token.accessToken || token.sessionKind !== "deviceAccount") {
    throw new Error("DEVICE_ACCOUNT_EXCHANGE_INVALID_RESPONSE");
  }

  await dependencies.saveAccessToken(token.accessToken);
  await dependencies.removeRefreshToken();
  // current-user 与菜单请求也必须先进入 deviceAccount host 隔离策略。
  await dependencies.markDeviceAccountSession();
  const user = await dependencies.loadCurrentUser();
  await dependencies.saveCurrentUser(user);
  await dependencies.loadNavigationMenu();
  return user;
}
