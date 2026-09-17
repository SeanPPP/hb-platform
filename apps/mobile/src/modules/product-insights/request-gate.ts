/** 每次新查询同步作废前一查询，网络即使无法及时取消也不能回写旧结果。 */
export function createProductInsightRequestGate() {
  let current: AbortController | null = null;
  return {
    begin() {
      current?.abort();
      const controller = new AbortController();
      current = controller;
      return {
        signal: controller.signal,
        isCurrent: () => current === controller && !controller.signal.aborted,
      };
    },
    cancel() {
      current?.abort();
      current = null;
    },
  };
}
