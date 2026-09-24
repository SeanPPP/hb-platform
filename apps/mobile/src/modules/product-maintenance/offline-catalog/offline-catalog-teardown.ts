/**
 * 会话结束时释放离线商品目录。
 *
 * 单独成文件是为了避开循环依赖：auth-store 与 device-store 互相引用，而
 * offline-catalog store 又要经 api 层读 device-store 取设备头。这里只做动态加载，
 * 两个 store 都能安全引入。
 */
export async function closeOfflineCatalogRuntime(): Promise<void> {
  try {
    const { useOfflineCatalogStore } = await import("./offline-catalog-store");
    await useOfflineCatalogStore.getState().close();
  } catch (error) {
    // 离线目录不可用不应阻断会话清理。
    console.warn("[offline-catalog] close on session clear failed", error);
  }
}
