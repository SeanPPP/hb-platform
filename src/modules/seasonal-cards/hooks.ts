import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  fetchSeasonalCardCatalog,
  fetchSeasonalCardSubmissionDetail,
  fetchSeasonalCardSubmissions,
  submitSeasonalCardSubmission,
} from "@/modules/seasonal-cards/api";
import type {
  SeasonalCardSubmissionPayload,
  SeasonalCardSubmissionQuery,
} from "@/modules/seasonal-cards/types";

export function seasonalCardQueryKeys() {
  return {
    all: ["seasonalCards"] as const,
    catalog: ["seasonalCards", "catalog"] as const,
    submissions: (query: SeasonalCardSubmissionQuery) =>
      ["seasonalCards", "submissions", query] as const,
    detail: (submissionGuid: string) =>
      ["seasonalCards", "detail", submissionGuid] as const,
  };
}

export function shouldEnableSeasonalCardCatalog(canSubmit: boolean) {
  return canSubmit;
}

export function useSeasonalCardCatalog(canSubmit = false) {
  return useQuery({
    queryKey: seasonalCardQueryKeys().catalog,
    enabled: shouldEnableSeasonalCardCatalog(canSubmit),
    queryFn: fetchSeasonalCardCatalog,
  });
}

export function useSeasonalCardSubmissions(
  query: SeasonalCardSubmissionQuery,
  enabled = true
) {
  return useQuery({
    queryKey: seasonalCardQueryKeys().submissions(query),
    enabled,
    queryFn: () => fetchSeasonalCardSubmissions(query),
  });
}

export function useSeasonalCardSubmissionDetail(
  submissionGuid: string | null,
  enabled = true
) {
  return useQuery({
    queryKey: seasonalCardQueryKeys().detail(submissionGuid ?? ""),
    enabled: enabled && Boolean(submissionGuid),
    queryFn: () => fetchSeasonalCardSubmissionDetail(submissionGuid ?? ""),
  });
}

export function useSubmitSeasonalCardSubmission() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: SeasonalCardSubmissionPayload) =>
      submitSeasonalCardSubmission(payload),
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: ["seasonalCards", "submissions"],
      });
    },
  });
}
