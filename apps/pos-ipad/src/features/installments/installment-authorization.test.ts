import assert from "node:assert/strict";
import test from "node:test";

import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_VIEW_PERMISSION,
  resolveInstallmentsAccess,
} from "./installment-authorization";

test("分期权限与 WPF PosTerminal 权限码完全一致", () => {
  assert.deepEqual(
    [
      INSTALLMENTS_VIEW_PERMISSION,
      INSTALLMENTS_CREATE_PERMISSION,
      INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
      INSTALLMENTS_CANCEL_PERMISSION,
      INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
    ],
    [
      "Permissions.PosTerminal.Installments.View",
      "Permissions.PosTerminal.Installments.Create",
      "Permissions.PosTerminal.Installments.AddRepayment",
      "Permissions.PosTerminal.Installments.Cancel",
      "Permissions.PosTerminal.Installments.ConfirmPickup",
    ],
  );
});

test("权限解析不做前缀、大小写或空字符串放宽", () => {
  assert.deepEqual(
    resolveInstallmentsAccess([
      INSTALLMENTS_VIEW_PERMISSION,
      INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
      INSTALLMENTS_CANCEL_PERMISSION,
      " Permissions.PosTerminal.Installments.Create ",
      "permissions.posterminal.installments.confirmpickup",
      "",
    ]),
    {
      canAddRepayment: true,
      canCancel: true,
      canConfirmPickup: false,
      canCreate: true,
      canView: true,
    },
  );
});
