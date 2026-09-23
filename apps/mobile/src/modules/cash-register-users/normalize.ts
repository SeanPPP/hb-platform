import type {
  CashRegisterUserGridParams,
  CashRegisterUserListItem,
  CashRegisterUserScope,
} from "@/modules/cash-register-users/types";

type ApiRecord = Record<string, unknown>;

export function asRecord(value: unknown): ApiRecord {
  return value && typeof value === "object" ? (value as ApiRecord) : {};
}

export function text(value: unknown) {
  return typeof value === "string" ? value.trim() : value == null ? "" : String(value).trim();
}

export function pick(record: ApiRecord, camel: string) {
  // 后端匿名对象与 DTO 的大小写不完全一致（如 HGUID / Items），两种写法都兼容。
  const pascal = camel.charAt(0).toUpperCase() + camel.slice(1);
  return record[camel] ?? record[pascal] ?? record[camel.toUpperCase()];
}

export function normalizeCashRegisterUser(payload: unknown): CashRegisterUserListItem {
  const record = asRecord(payload);
  return {
    hGuid: text(pick(record, "hGuid") ?? record.hguid),
    storeCode: text(pick(record, "storeCode")),
    storeName: text(pick(record, "storeName")),
    legacyStoreCode: text(pick(record, "legacyStoreCode")),
    userGuid: text(pick(record, "userGuid") ?? record.userGUID),
    username: text(pick(record, "username")),
    userFullName: text(pick(record, "userFullName")),
    operatorUser: text(pick(record, "operatorUser")),
    userBarcode: text(pick(record, "userBarcode")),
    loginRole: text(pick(record, "loginRole")),
    remark: text(pick(record, "remark")),
    printCount: Number(pick(record, "printCount")) || 0,
    status: pick(record, "status") === true,
    createDate: text(pick(record, "createDate")) || undefined,
  };
}

function textFilter(value: string) {
  return { filterType: "text", type: "equals", filter: value };
}

export function buildCashRegisterUserGridRequest(params: CashRegisterUserGridParams) {
  const filterModel: Record<string, unknown> = {};
  const storeCode = params.storeCode?.trim();
  if (storeCode) filterModel.storeCode = textFilter(storeCode);
  if (params.status !== "all") filterModel.status = textFilter(params.status === "active" ? "true" : "false");
  return {
    startRow: params.startRow,
    endRow: params.startRow + params.pageSize - 1,
    pageSize: params.pageSize,
    globalSearch: params.keyword?.trim() ?? "",
    filterModel,
  };
}

export function normalizeCashRegisterUserScope(payload: unknown): CashRegisterUserScope {
  const record = asRecord(payload);
  const stores = pick(record, "manageableStores");
  return {
    isAdmin: pick(record, "isAdmin") === true,
    canManage: pick(record, "canManage") === true,
    canPrint: pick(record, "canPrint") === true,
    manageableStores: (Array.isArray(stores) ? stores : [])
      .map((item) => {
        const store = asRecord(item);
        const storeCode = text(pick(store, "storeCode"));
        return { storeCode, storeName: text(pick(store, "storeName")) || storeCode };
      })
      .filter((store) => store.storeCode),
  };
}
