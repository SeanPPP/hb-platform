/** 老收银系统登录角色：后端存字符串，"1" 管理员、"2" 收银员，其余值原样展示。 */
export type CashRegisterUserLoginRole = "1" | "2";

export interface CashRegisterUserListItem {
  hGuid: string;
  storeCode: string;
  storeName: string;
  legacyStoreCode: string;
  userGuid: string;
  username: string;
  userFullName: string;
  operatorUser: string;
  userBarcode: string;
  loginRole: string;
  remark: string;
  printCount: number;
  status: boolean;
  createDate?: string;
}

export interface CashRegisterUserUserOption {
  userGuid: string;
  username: string;
  userFullName: string;
}

export type CashRegisterUserStatusFilter = "all" | "active" | "disabled";

export interface CashRegisterUserGridParams {
  storeCode?: string | null;
  keyword?: string;
  status: CashRegisterUserStatusFilter;
  startRow: number;
  pageSize: number;
}

export interface CashRegisterUserPage {
  items: CashRegisterUserListItem[];
  total: number;
}

export interface CashRegisterUserFormValues {
  storeCode: string;
  userGuid: string;
  operatorUser: string;
  userBarcode: string;
  loginRole: CashRegisterUserLoginRole;
  remark: string;
  status: boolean;
}

/** 后端判定的当前账号管理范围与实时权限（不依赖登录时缓存的权限）。 */
export interface CashRegisterUserScope {
  isAdmin: boolean;
  canManage: boolean;
  canPrint: boolean;
  manageableStores: Array<{ storeCode: string; storeName: string }>;
}
