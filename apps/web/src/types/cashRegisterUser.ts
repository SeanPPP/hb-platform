export interface CashRegisterUserListDto {
  id: number
  hGuid: string
  storeCode?: string
  storeName?: string
  legacyStoreCode?: string
  userGUID?: string
  username?: string
  userFullName?: string
  operatorUser?: string
  userBarcode?: string
  loginRole?: string
  remark?: string
  printCount: number
  status: boolean
  createDate: string
  lastModifyDate: string
  lastModifier?: string
}

export interface CashRegisterUserDetailDto {
  id: number
  hGuid: string
  storeCode?: string
  storeName?: string
  legacyStoreCode?: string
  userGUID?: string
  username?: string
  userFullName?: string
  operatorUser?: string
  userBarcode?: string
  loginRole?: string
  remark?: string
  printCount: number
  status: boolean
  creator?: string
  createDate: string
  lastModifier?: string
  lastModifyDate: string
}

export interface CashRegisterUserUserOptionDto {
  userGUID: string
  username: string
  userFullName?: string
}

export interface CreateCashRegisterUserDto {
  storeCode?: string
  userGUID?: string
  operatorUser?: string
  userBarcode?: string
  loginRole?: string
  remark?: string
  status?: boolean
}

export type UpdateCashRegisterUserDto = CreateCashRegisterUserDto

export interface CashRegisterUserFilterDto {
  storeCode?: string
  operatorUser?: string
  userBarcode?: string
  loginRole?: string
  status?: boolean
}
