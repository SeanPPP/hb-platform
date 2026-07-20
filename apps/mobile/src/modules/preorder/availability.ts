import type { PreorderActivationSummary, PreorderOrderStatus } from "./types";

export function isEditablePreorderOrderStatus(status: PreorderOrderStatus | null | undefined) {
  return !status || status === "Draft" || status === "ReturnedForRevision";
}

export type PreorderActivationReadOnlyReason =
  | "scheduled"
  | "ended"
  | "closed"
  | "cancelled"
  | "unavailable";

export function getPreorderActivationReadOnlyReason(
  activation: Pick<PreorderActivationSummary, "status" | "startAtUtc" | "endAtUtc"> | null | undefined,
  nowMs = Date.now()
): PreorderActivationReadOnlyReason | null {
  if (!activation) return "unavailable";
  if (activation.status === "Closed") return "closed";
  if (activation.status === "Cancelled") return "cancelled";

  const startAt = Date.parse(activation.startAtUtc);
  const endAt = Date.parse(activation.endAtUtc);
  if (!Number.isFinite(startAt) || !Number.isFinite(endAt)) return "unavailable";
  if (activation.status === "Scheduled" || nowMs < startAt) return "scheduled";
  if (activation.status !== "Active") return "unavailable";
  return nowMs < endAt ? null : "ended";
}
