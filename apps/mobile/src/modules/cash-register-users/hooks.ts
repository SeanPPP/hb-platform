import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  confirmCashRegisterUserPrint,
  createCashRegisterUser,
  fetchCashRegisterUserOptions,
  fetchCashRegisterUserScope,
  fetchCashRegisterUsers,
  updateCashRegisterUser,
} from "@/modules/cash-register-users/api";
import type {
  CashRegisterUserFormValues,
  CashRegisterUserStatusFilter,
} from "@/modules/cash-register-users/types";

const PAGE_SIZE = 30;
const QUERY_ROOT = "cashRegisterUsers";

export function useCashRegisterUsers(
  enabled: boolean,
  storeCode: string | null,
  keyword: string,
  status: CashRegisterUserStatusFilter
) {
  return useInfiniteQuery({
    queryKey: [QUERY_ROOT, "grid", storeCode ?? "", keyword.trim(), status],
    enabled,
    initialPageParam: 0,
    queryFn: ({ pageParam }) =>
      fetchCashRegisterUsers({ storeCode, keyword, status, startRow: pageParam, pageSize: PAGE_SIZE }),
    getNextPageParam: (lastPage, pages) => {
      const loaded = pages.reduce((count, page) => count + page.items.length, 0);
      return lastPage.items.length === PAGE_SIZE && loaded < lastPage.total ? loaded : undefined;
    },
  });
}

export function useCashRegisterUserScope(enabled: boolean) {
  return useQuery({
    queryKey: [QUERY_ROOT, "scope"],
    enabled,
    queryFn: fetchCashRegisterUserScope,
    retry: false,
  });
}

export function useCashRegisterUserOptions(enabled: boolean) {
  return useQuery({
    queryKey: [QUERY_ROOT, "userOptions"],
    enabled,
    queryFn: fetchCashRegisterUserOptions,
  });
}

export function useCashRegisterUserMutations() {
  const queryClient = useQueryClient();
  const invalidate = () => queryClient.invalidateQueries({ queryKey: [QUERY_ROOT, "grid"] });

  const createMutation = useMutation({
    mutationFn: (values: CashRegisterUserFormValues) => createCashRegisterUser(values),
    onSuccess: invalidate,
  });
  const updateMutation = useMutation({
    mutationFn: ({ hGuid, values }: { hGuid: string; values: CashRegisterUserFormValues }) =>
      updateCashRegisterUser(hGuid, values),
    onSuccess: invalidate,
  });
  const printConfirmMutation = useMutation({
    mutationFn: ({ hGuid, userBarcode }: { hGuid: string; userBarcode: string }) =>
      confirmCashRegisterUserPrint(hGuid, userBarcode),
    onSettled: invalidate,
  });

  return { createMutation, updateMutation, printConfirmMutation };
}
