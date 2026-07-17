export interface AttendanceQrScanSession {
  generation: number;
}

export function createAttendanceQrScanSessionGate() {
  let generation = 0;
  let activeGeneration: number | null = null;
  let submitStarted = false;

  return {
    begin(): AttendanceQrScanSession {
      generation += 1;
      activeGeneration = generation;
      submitStarted = false;
      return { generation };
    },
    invalidate() {
      activeGeneration = null;
      submitStarted = false;
    },
    isActive(session: AttendanceQrScanSession) {
      return activeGeneration === session.generation;
    },
    tryStartSubmitting(session: AttendanceQrScanSession) {
      if (activeGeneration !== session.generation || submitStarted) {
        return false;
      }
      submitStarted = true;
      return true;
    },
    finishSubmitting(session: AttendanceQrScanSession) {
      // 仅当前有效会话可以释放自己的提交锁，避免旧请求的 finally 干扰新会话。
      if (activeGeneration === session.generation && submitStarted) {
        submitStarted = false;
      }
    },
    isSubmitting(session: AttendanceQrScanSession) {
      return activeGeneration === session.generation && submitStarted;
    },
  };
}
