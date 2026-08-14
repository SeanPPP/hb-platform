import type { SpecialProductsPresenter } from "./special-products-presenter";

/**
 * Wave4 的 DB/HTTP/cart 组合根由上层接线；路由暂时只识别这一项零参数工厂，
 * 不向 React 暴露裸 repository、transport、数据库或可信 cashier lease。
 */
export interface SpecialProductsRuntimeFactory {
  createPresenter(): SpecialProductsPresenter;
}

export function resolveSpecialProductsRuntimeFactory(
  services: object,
): SpecialProductsRuntimeFactory | null {
  if (!("specialProducts" in services)) return null;
  const candidate = services.specialProducts;
  if (
    typeof candidate !== "object" ||
    candidate === null ||
    !("createPresenter" in candidate) ||
    typeof candidate.createPresenter !== "function"
  ) {
    return null;
  }
  return candidate as SpecialProductsRuntimeFactory;
}
