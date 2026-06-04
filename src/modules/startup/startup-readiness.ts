export type StartupReadinessResult =
  | { ok: true }
  | { ok: false; error: unknown };

export async function waitForStartupReadiness(
  requiredTasks: Promise<unknown>[],
  minVisibleMs: number,
): Promise<StartupReadinessResult> {
  const minVisibleTimer = new Promise((resolve) => {
    setTimeout(resolve, minVisibleMs);
  });

  // 必需任务失败时保留错误结果，但仍等满最短展示时间，避免启动页闪退。
  const readiness = (async (): Promise<StartupReadinessResult> => {
    try {
      await Promise.all(requiredTasks);
      return { ok: true };
    } catch (error) {
      return { ok: false, error };
    }
  })();

  const [result] = await Promise.all([readiness, minVisibleTimer]);
  return result;
}
