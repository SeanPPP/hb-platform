// 保存结果已经由服务端确认，列表刷新不得继续占用打卡成功反馈；定位生命周期仍须完整执行。
export async function completeAttendancePostSave(handlers: {
  notifySaved: () => void;
  refresh: () => Promise<unknown>;
  track: () => Promise<void>;
  onRefreshError: (error: unknown) => void;
}) {
  try {
    handlers.notifySaved();
  } finally {
    // 展示层异常或刷新同步抛错也不能跳过已经承诺的定位启停。
    void Promise.resolve().then(handlers.refresh).catch(handlers.onRefreshError).catch(() => undefined);
    await handlers.track();
  }
}
