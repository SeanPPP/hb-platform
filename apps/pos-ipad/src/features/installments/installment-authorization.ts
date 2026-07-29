export const INSTALLMENTS_VIEW_PERMISSION =
  "Permissions.PosTerminal.Installments.View";
export const INSTALLMENTS_CREATE_PERMISSION =
  "Permissions.PosTerminal.Installments.Create";
export const INSTALLMENTS_ADD_REPAYMENT_PERMISSION =
  "Permissions.PosTerminal.Installments.AddRepayment";
export const INSTALLMENTS_CANCEL_PERMISSION =
  "Permissions.PosTerminal.Installments.Cancel";
export const INSTALLMENTS_CONFIRM_PICKUP_PERMISSION =
  "Permissions.PosTerminal.Installments.ConfirmPickup";

export type InstallmentsAccess = Readonly<{
  canAddRepayment: boolean;
  canCancel: boolean;
  canConfirmPickup: boolean;
  canCreate: boolean;
  canView: boolean;
}>;

export function resolveInstallmentsAccess(
  permissions: readonly string[],
): InstallmentsAccess {
  const granted = new Set(
    permissions.map((permission) => permission.trim()),
  );
  return Object.freeze({
    canAddRepayment: granted.has(
      INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
    ),
    canCancel: granted.has(INSTALLMENTS_CANCEL_PERMISSION),
    canConfirmPickup: granted.has(
      INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
    ),
    canCreate: granted.has(INSTALLMENTS_CREATE_PERMISSION),
    canView: granted.has(INSTALLMENTS_VIEW_PERMISSION),
  });
}
