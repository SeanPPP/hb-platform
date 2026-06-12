import { useQuery } from "@tanstack/react-query";
import { getCurrentUserApi } from "@/modules/auth/api";

export function useCurrentUser() {
  return useQuery({
    queryKey: ["currentUser"],
    queryFn: getCurrentUserApi,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
}
