export const STORE_STAFF_ROLE = "StoreStaff";

export interface StoreUserListItem {
  userGUID: string;
  username: string;
  fullName?: string;
  email?: string;
  phone?: string;
  status: number;
  storeCode?: string;
  storeName?: string;
  roleNames: string[];
  lastLoginTime?: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface StoreUserDetail extends StoreUserListItem {
  remarks?: string;
}

export interface StoreUserProfile extends StoreUserDetail {
  employmentType?: string;
  identityId?: string;
  birthday?: string;
  gender?: string;
  avatarUrl?: string;
  address?: string;
  bankBsb?: string;
  bankAccountNumber?: string;
  superannuationCompanyName?: string;
  superannuationCompanyCode?: string;
  superannuationAccountNumber?: string;
}

export interface StoreUserGridParams {
  storeCode: string;
  keyword?: string;
}

export interface StoreUserMutationInput {
  username: string;
  fullName?: string;
  email?: string;
  phone?: string;
  password?: string;
  status: number;
}

export interface StoreUserCreatePayload extends StoreUserMutationInput {
  storeCode: string;
  roleNames?: string[];
  employmentType?: "casual";
}

export interface StoreUserUpdatePayload extends StoreUserMutationInput {
  userGuid: string;
  storeCode: string;
  roleNames?: string[];
}

export interface StoreUserStatusPayload {
  userGuid: string;
  storeCode: string;
  status: number;
}

export interface StoreUserPasswordPayload {
  userGuid: string;
  storeCode: string;
  newPassword: string;
}

export interface StoreUserFormValues {
  username: string;
  fullName: string;
  email: string;
  phone: string;
  password: string;
  status: boolean;
}
