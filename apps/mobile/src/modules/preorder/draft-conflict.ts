import {
  buildDraftItems,
  createPackCounts,
  serializePackCounts,
  type PreorderPackCounts,
} from "./order-state";
import type { PreorderActivationDetail } from "./types";

export type DraftConflictChoice = "server" | "local";

/** 把冲突选择转换成一次原子状态更新，避免 revision 与数量来自不同版本。 */
export function resolveDraftConflict(
  serverDetail: PreorderActivationDetail,
  localPackCounts: PreorderPackCounts,
  choice: DraftConflictChoice
) {
  const packCounts = choice === "server"
    ? createPackCounts(serverDetail.items)
    : { ...localPackCounts };

  return {
    detail: serverDetail,
    packCounts,
    draftRevision: serverDetail.draftRevision,
    savedFingerprint: choice === "server"
      ? serializePackCounts(buildDraftItems(serverDetail.items, packCounts))
      : undefined,
    shouldRetry: choice === "local",
  };
}
