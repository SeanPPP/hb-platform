/**
 * 解析「当前会话实际生效的 API 地址」。
 *
 * apiClient 的请求拦截器会用设备绑定时确定的 host 覆盖用户在设置页选择的 host
 * （见 client.ts 里的 resolveDeviceAccountRequestPolicy 调用）。可达性探测若仍按
 * 偏好 host 去探，两者不一致时就会分裂：探测打得通、业务请求却必然失败，于是
 * 离线态被判成「已恢复在线」→ 自动重跑查询 → 请求再次失败回到离线，形成毫秒级
 * 紧密循环。因此探测必须与业务请求共用同一套 host 解析。
 */
import {
  getAuthSessionMarker,
  type PersistedAuthSessionKind,
} from "@/modules/device-activation/auth-session-marker";
import {
  deriveEffectiveAuthSessionKind,
  resolveDeviceAccountRequestPolicy,
} from "@/modules/device-activation/device-account-request-policy";
import { buildApiBaseUrl, getStoredApiHost } from "@/shared/api/config";

/** 解析所需的副作用依赖，全部可注入以便单测。 */
export interface EffectiveApiHostPorts {
  getStoredApiHost: () => Promise<string>;
  getToken: () => Promise<string | null>;
  getRefreshToken: () => Promise<string | null>;
  getAuthSessionMarker: () => Promise<PersistedAuthSessionKind | null>;
  loadBinding: () => Promise<{ apiHost?: string | null } | null>;
}

type CredentialPorts = Pick<
  EffectiveApiHostPorts,
  "getToken" | "getRefreshToken" | "loadBinding"
>;

/**
 * 凭据读取依赖 expo-secure-store，顶层静态 import 会把 react-native 拉进依赖图、
 * 使 Node 下的单测无法转换本模块；因此延迟到真正需要时才加载。测试注入全部
 * 凭据端口时不会走到这里。
 */
async function loadCredentialPorts(): Promise<CredentialPorts> {
  const [{ SecureStorage }, { DeviceAccountStorage }] = await Promise.all([
    import("@/shared/storage/secure"),
    import("@/modules/device-activation/device-account-storage-runtime"),
  ]);
  return {
    getToken: () => SecureStorage.getToken(),
    getRefreshToken: () => SecureStorage.getRefreshToken(),
    loadBinding: () => DeviceAccountStorage.loadBinding(),
  };
}

/**
 * 解析当前生效的 API host，判定规则与 apiClient 请求拦截器完全一致：
 * 设备账号会话下以绑定 host 为准，其余情况用偏好 host。
 */
export async function resolveEffectiveApiHost(
  ports: Partial<EffectiveApiHostPorts> = {},
): Promise<string> {
  const lazy =
    ports.getToken && ports.getRefreshToken && ports.loadBinding
      ? null
      : await loadCredentialPorts();
  const getToken = ports.getToken ?? lazy!.getToken;
  const getRefreshToken = ports.getRefreshToken ?? lazy!.getRefreshToken;
  const loadBinding = ports.loadBinding ?? lazy!.loadBinding;

  const requestedApiHost = await (ports.getStoredApiHost ?? getStoredApiHost)();
  // 凭据读取失败（如设备锁定导致 SecureStore 不可读）不应让探测直接崩；
  // 单项失败按「无该项」处理，最终退回偏好 host，行为与修复前一致。
  const [token, refreshToken, persistedSessionKind, accountBinding] = await Promise.all([
    getToken().catch(() => null),
    getRefreshToken().catch(() => null),
    (ports.getAuthSessionMarker ?? getAuthSessionMarker)().catch(() => null),
    loadBinding().catch(() => null),
  ]);
  const sessionKind = deriveEffectiveAuthSessionKind({
    persistedKind: persistedSessionKind,
    hasAccessToken: Boolean(token),
    hasRefreshToken: Boolean(refreshToken),
    hasBinding: Boolean(accountBinding),
  });
  return resolveDeviceAccountRequestPolicy({
    requestedApiHost,
    bindingApiHost: accountBinding?.apiHost,
    sessionKind,
    skipAuthentication: false,
  }).apiHost;
}

/** 解析当前生效的 API 基础地址（含协议与 /api 前缀）。 */
export async function resolveEffectiveApiBaseUrl(
  ports: Partial<EffectiveApiHostPorts> = {},
): Promise<string> {
  return buildApiBaseUrl(await resolveEffectiveApiHost(ports));
}
