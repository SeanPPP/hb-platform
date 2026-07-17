export type UserAccessStoreState = "unassigned" | "view" | "manage";

export interface AccessStoreOption {
  storeGUID: string;
  storeCode: string;
  storeName: string;
  isActive?: boolean;
}

export interface UserAccessStore extends AccessStoreOption {
  isManageable: boolean;
  assignedAt?: string;
}

export interface UserAccessStoreAssignment {
  storeGUID: string;
  isPrimary: boolean;
}

export interface UserAccessRole {
  roleGUID: string;
  roleName: string;
  description?: string;
  isActive?: boolean;
}

export interface AccessPermission {
  name: string;
  displayName: string;
  description?: string;
  category: string;
  isSystemPermission: boolean;
}

export interface AccessPermissionCategory {
  category: string;
  displayName: string;
  description?: string;
  permissions: AccessPermission[];
}

export interface UserAccessPermissionInheritedSource {
  roleName: string;
  permissionCodes: string[];
}

export interface UserAccessPermissionState {
  userGuid: string;
  isSuperAdmin: boolean;
  implicitAllPermissions: boolean;
  inheritedPermissionCodes: string[];
  directPermissionCodes: string[];
  effectivePermissionCodes: string[];
  inheritedSources: UserAccessPermissionInheritedSource[];
}

export interface UserAccessPermissionAccess {
  state: UserAccessPermissionState;
  categories: AccessPermissionCategory[];
}

export interface DirectPermissionDraft {
  baselineCodes: string[];
  selectedCodes: string[];
  inheritedPermissionCodes: string[];
}

export interface AssignUserAccessStoresInput {
  userGuid: string;
  assignments: UserAccessStoreAssignment[];
}

export interface AssignUserAccessRolesInput {
  userGuid: string;
  roleGuids: string[];
  roleCatalog: UserAccessRole[];
}

export interface AssignUserDirectPermissionsInput {
  userGuid: string;
  permissions: string[];
}

export type UserAccessSectionMode = "hidden" | "readOnly" | "editable";

export type UserAccessEligibilityReason =
  "deviceMode" | "self" | "privilegedTarget" | "noPermission";

export interface UserAccessEligibilityContext {
  isDeviceMode: boolean;
  isAdmin: boolean;
  isStoreManager: boolean;
  canManageStores: boolean;
  canManageRoles: boolean;
  canManagePos: boolean;
  currentUserGuid?: string | null;
  targetUserGuid: string;
  targetStatus: number;
  targetRoleNames: string[];
  hasManageableStores: boolean;
}

export interface UserAccessEligibility {
  canOpen: boolean;
  storesMode: UserAccessSectionMode;
  rolesMode: UserAccessSectionMode;
  permissionsMode: UserAccessSectionMode;
  canManagePos: boolean;
  canGrantStoreManagement: boolean;
  reason: UserAccessEligibilityReason | null;
}

export type UserAccessErrorKind =
  "unauthorized" | "forbidden" | "notFound" | "network";
