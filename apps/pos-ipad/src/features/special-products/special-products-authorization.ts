export const SPECIAL_PRODUCTS_VIEW_PERMISSION =
  "Permissions.PosTerminal.SpecialProducts.View";
export const SPECIAL_PRODUCTS_MANAGE_PERMISSION =
  "Permissions.PosTerminal.SpecialProducts.Manage";
export const SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION =
  "Permissions.PosTerminal.SpecialProducts.AddToCart";

export type SpecialProductsAccess = Readonly<{
  canAddToCart: boolean;
  canManage: boolean;
  canView: boolean;
}>;

export function resolveSpecialProductsAccess(
  permissions: readonly string[],
): SpecialProductsAccess {
  const granted = new Set(permissions.map((permission) => permission.trim()));
  return {
    canAddToCart: granted.has(SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION),
    canManage: granted.has(SPECIAL_PRODUCTS_MANAGE_PERMISSION),
    canView: granted.has(SPECIAL_PRODUCTS_VIEW_PERMISSION),
  };
}
