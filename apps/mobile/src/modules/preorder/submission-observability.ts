export type PreorderSubmissionTelemetryStage =
  | "confirm"
  | "wait-save-start"
  | "wait-save-end"
  | "post-start"
  | "post-end"
  | "success-feedback"
  | "background-active-refresh-finish";

export interface PreorderSubmissionTelemetryEvent {
  event: "preorder_submission";
  stage: PreorderSubmissionTelemetryStage;
  submissionId: string;
  outcome?: "success" | "error";
  requestCounts: {
    draftPut: number;
    submitPost: number;
    activeGet: number;
    detailGet: number;
  };
  itemCount: number;
  requestBodyBytes: number;
}

interface CreatePreorderSubmissionTelemetryOptions {
  submissionId: string;
  itemCount: number;
  requestBody: unknown;
  hasInFlightSave: boolean;
  logger?: (event: PreorderSubmissionTelemetryEvent) => void;
}

function utf8ByteLength(value: string) {
  let bytes = 0;
  for (const character of value) {
    const codePoint = character.codePointAt(0) ?? 0;
    bytes += codePoint <= 0x7f ? 1 : codePoint <= 0x7ff ? 2 : codePoint <= 0xffff ? 3 : 4;
  }
  return bytes;
}

function jsonByteLength(value: unknown) {
  const serialized = JSON.stringify(value);
  return utf8ByteLength(serialized === undefined ? "" : serialized);
}

/** 仅记录固定白名单元数据；请求体只用于计算大小，绝不会进入日志。 */
export function createPreorderSubmissionTelemetry({
  submissionId,
  itemCount,
  requestBody,
  hasInFlightSave,
  logger = (event) => console.info("[preorder-submit]", event),
}: CreatePreorderSubmissionTelemetryOptions) {
  const requestCounts = {
    draftPut: hasInFlightSave ? 1 : 0,
    submitPost: 0,
    activeGet: 0,
    detailGet: 0,
  };
  let requestBodyBytes = jsonByteLength(requestBody);

  const record = (
    stage: PreorderSubmissionTelemetryStage,
    outcome?: "success" | "error"
  ) => {
    logger({
      event: "preorder_submission",
      stage,
      submissionId,
      outcome,
      requestCounts: { ...requestCounts },
      itemCount,
      requestBodyBytes,
    });
  };

  return {
    confirm: () => record("confirm"),
    waitSaveStart: () => record("wait-save-start"),
    waitSaveEnd: () => record("wait-save-end"),
    updateRequestBody: (nextRequestBody: unknown) => {
      requestBodyBytes = jsonByteLength(nextRequestBody);
    },
    postStart: () => {
      requestCounts.submitPost += 1;
      record("post-start");
    },
    postEnd: (outcome: "success" | "error") => record("post-end", outcome),
    successFeedback: () => record("success-feedback", "success"),
    activeGet: () => {
      requestCounts.activeGet += 1;
    },
    detailGet: () => {
      requestCounts.detailGet += 1;
    },
    backgroundActiveRefreshFinish: (outcome: "success" | "error") =>
      record("background-active-refresh-finish", outcome),
  };
}

export type PreorderSubmissionTelemetry = ReturnType<typeof createPreorderSubmissionTelemetry>;
