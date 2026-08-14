import assert from "node:assert/strict";
import test from "node:test";

import {
  SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION,
  SPECIAL_PRODUCTS_MANAGE_PERMISSION,
  SPECIAL_PRODUCTS_VIEW_PERMISSION,
  resolveSpecialProductsAccess,
} from "./special-products-authorization";

test("精确区分 View、Manage 与 AddToCart 权限", () => {
  assert.deepEqual(
    resolveSpecialProductsAccess([
      SPECIAL_PRODUCTS_VIEW_PERMISSION,
      SPECIAL_PRODUCTS_ADD_TO_CART_PERMISSION,
    ]),
    {
      canAddToCart: true,
      canManage: false,
      canView: true,
    },
  );

  assert.deepEqual(
    resolveSpecialProductsAccess([SPECIAL_PRODUCTS_MANAGE_PERMISSION]),
    {
      canAddToCart: false,
      canManage: true,
      canView: false,
    },
  );
});

test("空白、近似和未知权限不会被误放行", () => {
  assert.deepEqual(
    resolveSpecialProductsAccess([
      "",
      " Permissions.PosTerminal.SpecialProducts.View ",
      "Permissions.PosTerminal.SpecialProducts",
      "Permissions.PosTerminal.SpecialProducts.View.Other",
    ]),
    {
      canAddToCart: false,
      canManage: false,
      canView: true,
    },
  );
});
