type AttendanceQrStage = "resolve" | "today" | "location" | "punch" | "refresh" | "tracking";

export function createAttendanceQrPerformance() {
  const startedAt = performance.now();
  const report = (stage: AttendanceQrStage | "success", started: number, outcome: string) => {
    // 耗时诊断仅记录阶段、时长和结果，不记录二维码、用户身份或定位坐标。
    console.info("[attendance] qr timing", {
      stage,
      durationMs: Math.round(performance.now() - started),
      outcome,
    });
  };
  return {
    async measure<T>(stage: AttendanceQrStage, action: () => Promise<T>): Promise<T> {
      const started = performance.now();
      try {
        const result = await action();
        report(stage, started, "ok");
        return result;
      } catch (error) {
        report(stage, started, "error");
        throw error;
      }
    },
    saved() { report("success", startedAt, "ok"); },
  };
}
