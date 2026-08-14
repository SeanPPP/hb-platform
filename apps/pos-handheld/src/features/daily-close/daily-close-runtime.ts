import type { DailyClosePresenter } from "./daily-close-presenter";

/**
 * 组合根持有可信 cashier lease、门店时区、repository 与打印 Port；
 * React 路由只能调用零参数工厂，不能伪造身份或数据作用域。
 */
export interface DailyCloseRuntimeFactory {
  createPresenter(): DailyClosePresenter;
}

export function resolveDailyCloseRuntimeFactory(
  services: object,
): DailyCloseRuntimeFactory | null {
  if (!("dailyClose" in services)) return null;
  const candidate = services.dailyClose;
  if (
    typeof candidate !== "object" ||
    candidate === null ||
    !("createPresenter" in candidate) ||
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as DailyCloseRuntimeFactory;
}
