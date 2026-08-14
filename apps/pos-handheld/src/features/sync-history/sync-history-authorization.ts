/** 与 WPF `Permissions.PosTerminal.*` 保持同一组收银员权限码。 */
export const SYNC_HISTORY_VIEW_PERMISSION =
  "Permissions.PosTerminal.History.View";
export const SYNC_HISTORY_MANUAL_SYNC_PERMISSION =
  "Permissions.PosTerminal.System.Sync";
export const SYNC_HISTORY_EXPORT_PERMISSION =
  "Permissions.PosTerminal.Audit.View";

export type SyncHistoryAccess = Readonly<{
  canExport: boolean;
  canManualRetransmit: boolean;
  canView: boolean;
}>;

/**
 * 历史读取、手动补传和支持导出分别复用既有权限：仅去除首尾空白后精确匹配，
 * 不能把已登录或其他同步生命周期权限当成补传或诊断导出授权。
 */
export function resolveSyncHistoryAccess(
  permissionCodes: readonly string[],
): SyncHistoryAccess {
  const granted = new Set(permissionCodes.map((permission) => permission.trim()));
  const canView = granted.has(SYNC_HISTORY_VIEW_PERMISSION);
  return Object.freeze({
    canExport: canView && granted.has(SYNC_HISTORY_EXPORT_PERMISSION),
    canManualRetransmit: granted.has(SYNC_HISTORY_MANUAL_SYNC_PERMISSION),
    canView,
  });
}
