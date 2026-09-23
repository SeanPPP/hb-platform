import { apiClient } from "@/shared/api/client";
import {
  asRecord,
  buildCashRegisterUserGridRequest,
  normalizeCashRegisterUser,
  normalizeCashRegisterUserScope,
  pick,
  text,
} from "@/modules/cash-register-users/normalize";
import type {
  CashRegisterUserFormValues,
  CashRegisterUserGridParams,
  CashRegisterUserPage,
  CashRegisterUserUserOption,
} from "@/modules/cash-register-users/types";

const API_BASE = "/react/v1/cash-register-users";

export async function fetchCashRegisterUsers(params: CashRegisterUserGridParams): Promise<CashRegisterUserPage> {
  const response = await apiClient.post(`${API_BASE}/grid`, buildCashRegisterUserGridRequest(params));
  const data = asRecord(response.data);
  const items = pick(data, "items");
  return {
    items: Array.isArray(items) ? items.map(normalizeCashRegisterUser).filter((item) => item.hGuid) : [],
    total: Number(pick(data, "total")) || 0,
  };
}

export async function fetchCashRegisterUserOptions(): Promise<CashRegisterUserUserOption[]> {
  const response = await apiClient.get(`${API_BASE}/user-options`);
  const items = Array.isArray(response.data) ? response.data : [];
  return items
    .map((item) => {
      const record = asRecord(item);
      return {
        userGuid: text(record.userGUID ?? pick(record, "userGuid")),
        username: text(pick(record, "username")),
        userFullName: text(pick(record, "userFullName")),
      };
    })
    .filter((item) => item.userGuid);
}

function toMutationPayload(values: CashRegisterUserFormValues) {
  return {
    storeCode: values.storeCode.trim(),
    userGUID: values.userGuid.trim(),
    operatorUser: values.operatorUser.trim(),
    userBarcode: values.userBarcode.trim(),
    loginRole: values.loginRole,
    remark: values.remark.trim(),
    status: values.status,
  };
}

export async function createCashRegisterUser(values: CashRegisterUserFormValues) {
  const response = await apiClient.post(API_BASE, toMutationPayload(values));
  return normalizeCashRegisterUser(response.data);
}

export async function updateCashRegisterUser(hGuid: string, values: CashRegisterUserFormValues) {
  const response = await apiClient.put(`${API_BASE}/${encodeURIComponent(hGuid)}`, toMutationPayload(values));
  return normalizeCashRegisterUser(response.data);
}

export async function confirmCashRegisterUserPrint(hGuid: string, userBarcode: string) {
  const response = await apiClient.post(`${API_BASE}/${encodeURIComponent(hGuid)}/print-confirmation`, {
    userBarcode: userBarcode.trim(),
  });
  return { printCount: Number(pick(asRecord(response.data), "printCount")) || 0 };
}

export async function fetchCashRegisterUserScope() {
  const response = await apiClient.get(`${API_BASE}/scope`);
  return normalizeCashRegisterUserScope(response.data);
}
