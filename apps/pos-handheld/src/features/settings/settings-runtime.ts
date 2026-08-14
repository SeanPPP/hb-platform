import type { SettingsPresenter } from "./settings-presenter";

/**
 * 可信 cashier lease、设备身份、数据库和硬件 adapter 都留在组合根。
 * 路由只能调用零参数工厂，不能从 React 传入门店或权限伪造 Settings 作用域。
 */
export interface SettingsRuntimeFactory {
  createPresenter(): SettingsPresenter;
}

export function resolveSettingsRuntimeFactory(
  services: object,
): SettingsRuntimeFactory | null {
  if (!("settings" in services)) return null;
  const candidate = services.settings;
  if (
    typeof candidate !== "object" ||
    candidate === null ||
    !("createPresenter" in candidate) ||
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as SettingsRuntimeFactory;
}
