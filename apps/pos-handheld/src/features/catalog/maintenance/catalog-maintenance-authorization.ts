export const CATALOG_DOWNLOAD_PERMISSION =
  "Permissions.PosTerminal.Settings.CatalogDownload";

/**
 * 目录快照会改变整台终端的本地售卖数据，因此入口和直链路由都必须使用
 * WPF 已有的精确权限码，不能把“已登录”误当成维护授权。
 */
export function canDownloadCatalog(
  permissions: readonly string[],
): boolean {
  return permissions.some(
    (permission) => permission.trim() === CATALOG_DOWNLOAD_PERMISSION,
  );
}
