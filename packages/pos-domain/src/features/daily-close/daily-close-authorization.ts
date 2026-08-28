export const DAILY_CLOSE_VIEW_PERMISSION =
  "Permissions.PosTerminal.DailyClose.View";
export const DAILY_CLOSE_SAVE_PERMISSION =
  "Permissions.PosTerminal.DailyClose.Save";
export const DAILY_CLOSE_REPRINT_PERMISSION =
  "Permissions.PosTerminal.DailyClose.Reprint";

export type DailyCloseAccess = Readonly<{
  canView: boolean;
  canSave: boolean;
  canReprint: boolean;
}>;

export function resolveDailyCloseAccess(
  permissions: readonly string[],
): DailyCloseAccess {
  const granted = new Set(permissions.map((permission) => permission.trim()));
  return Object.freeze({
    canView: granted.has(DAILY_CLOSE_VIEW_PERMISSION),
    canSave: granted.has(DAILY_CLOSE_SAVE_PERMISSION),
    canReprint: granted.has(DAILY_CLOSE_REPRINT_PERMISSION),
  });
}
