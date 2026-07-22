import type { AttendanceApprovalPayload } from "./types";

export type OvertimeApprovalAction = "approve" | "reject";

export type KnownAttendanceApprovalSourceType =
  | "Punch"
  | "Leave"
  | "PunchAdjustment"
  | "Overtime"
  | "MissingClockOut";

export type OvertimeApprovalValidationError =
  | "outOfRange"
  | "invalidIncrement"
  | "remarkRequired";

const knownSourceTypes = new Set<KnownAttendanceApprovalSourceType>([
  "Punch",
  "Leave",
  "PunchAdjustment",
  "Overtime",
  "MissingClockOut",
]);

export function isKnownAttendanceApprovalSourceType(
  value: string,
): value is KnownAttendanceApprovalSourceType {
  return knownSourceTypes.has(value as KnownAttendanceApprovalSourceType);
}

export function getSupplementalAttendanceApprovalDetail(input: {
  sourceType: string;
  detail?: string;
  displayedTitle: string;
}): string | undefined {
  if (input.sourceType !== "Punch" && input.sourceType !== "Leave") return undefined;
  const detail = input.detail?.trim();
  if (!detail || detail === input.displayedTitle.trim()) return undefined;
  return detail;
}

export function buildAttendanceApprovalReviewRequest(
  payload: AttendanceApprovalPayload,
) {
  return {
    reviewRemark: payload.remark?.trim() || undefined,
    ...(payload.approvedOvertimeMinutes !== undefined
      ? { approvedOvertimeMinutes: payload.approvedOvertimeMinutes }
      : {}),
  };
}

export function validateAttendanceOvertimeApproval(input: {
  candidateMinutes: number;
  approvedMinutes: number;
  action: OvertimeApprovalAction;
  remark?: string;
}): OvertimeApprovalValidationError | null {
  if (
    !Number.isInteger(input.approvedMinutes)
    || input.approvedMinutes < 0
    || input.approvedMinutes > input.candidateMinutes
  ) {
    return "outOfRange";
  }
  if (input.approvedMinutes % 15 !== 0) return "invalidIncrement";
  if (
    (input.action === "reject" || input.approvedMinutes < input.candidateMinutes)
    && !input.remark?.trim()
  ) {
    return "remarkRequired";
  }
  return null;
}
