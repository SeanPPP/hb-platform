export type SecureAccountSessionRemover = {
  clearCashierBarcodePrintSession(): Promise<void>;
  removeToken(): Promise<void>;
  removeRefreshToken(): Promise<void>;
  removeUser(): Promise<void>;
};

export async function clearSecureAccountSession(storage: SecureAccountSessionRemover) {
  let pendingCleared = true;
  try {
    // 收银条码属于登录凭证；必须先尝试清除，再删除可识别当前账号的 token。
    await storage.clearCashierBarcodePrintSession();
  } catch (error) {
    pendingCleared = false;
    console.warn("[cashier-barcode] 清理安全打印记录失败", error);
  }

  await Promise.all([
    storage.removeToken(),
    storage.removeRefreshToken(),
    storage.removeUser(),
  ]);
  return { pendingCleared };
}
