export type EmployeeProfileReviewAppState =
  | "active"
  | "inactive"
  | "background"
  | "unknown"
  | "extension";

export function createEmployeeProfileReviewAppStateHandler({
  onInactive,
  onActive,
}: {
  onInactive: () => void;
  onActive: () => void;
}) {
  return (nextState: EmployeeProfileReviewAppState) => {
    if (nextState === "active") {
      onActive();
      return;
    }
    // inactive 发生在 iOS App 切换器快照前，必须与 background 同样立即遮罩。
    onInactive();
  };
}
