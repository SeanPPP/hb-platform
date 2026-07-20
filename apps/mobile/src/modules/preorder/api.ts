import { apiClient } from "@/shared/api/client";
import {
  normalizeActivationDetail,
  normalizeActivePreorders,
  normalizeSubmitResult,
} from "./normalization";
import type {
  ActivePreordersResult,
  PreorderActivationDetail,
  PreorderSubmitResult,
  SavePreorderDraftInput,
  SubmitPreorderInput,
} from "./types";

const PREORDER_BASE = "/react/v1/preorders";

export {
  isPreorderRequiredError,
  normalizeActivationDetail,
  normalizeActivePreorders,
  normalizeSubmitResult,
  readPreorderErrorCode,
} from "./normalization";

export async function fetchActivePreorders(
  storeCode: string,
  signal?: AbortSignal
): Promise<ActivePreordersResult> {
  const response = await apiClient.get(`${PREORDER_BASE}/active`, {
    params: { storeCode },
    signal,
  });
  return normalizeActivePreorders(response.data);
}

export async function fetchPreorderActivation(
  activationGuid: string,
  storeCode: string
): Promise<PreorderActivationDetail> {
  const response = await apiClient.get(
    `${PREORDER_BASE}/activations/${encodeURIComponent(activationGuid)}`,
    { params: { storeCode } }
  );
  return normalizeActivationDetail(response.data);
}

export async function savePreorderDraft(
  activationGuid: string,
  input: SavePreorderDraftInput
): Promise<PreorderActivationDetail> {
  const response = await apiClient.put(
    `${PREORDER_BASE}/activations/${encodeURIComponent(activationGuid)}/draft`,
    input
  );
  return normalizeActivationDetail(response.data);
}

export async function submitPreorder(
  activationGuid: string,
  input: SubmitPreorderInput,
  submissionId?: string
): Promise<PreorderSubmitResult> {
  const response = await apiClient.post(
    `${PREORDER_BASE}/activations/${encodeURIComponent(activationGuid)}/submit`,
    input,
    submissionId ? { headers: { "X-Preorder-Submission-Id": submissionId } } : undefined
  );
  return normalizeSubmitResult(response.data);
}
