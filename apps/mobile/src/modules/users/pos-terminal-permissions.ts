import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
} from "./types";

export interface PosPermissionDraft {
  baselineCodes: string[];
  selectedCodes: string[];
}

export interface PosPermissionGroup {
  group: string;
  permissions: PosTerminalPermissionOption[];
}

export interface PosPermissionEntryContext {
  canManagePosTerminalPermissions: boolean;
  canManageStore: boolean;
  currentUserGuid?: string | null;
  targetUserGuid: string;
  targetStatus: number;
  targetRoleNames: string[];
  storeGuid?: string | null;
}

export type PosPermissionEntryState = "enabled" | "hidden" | "disabled";
export type PosPermissionErrorKind =
  | "unauthorized"
  | "forbidden"
  | "notFound"
  | "network";

const PRIVILEGED_ROLE_NAMES = new Set([
  "admin",
  "管理员",
  "superadmin",
  "超级管理员",
  "storemanager",
  "店长",
  "经理",
  "warehousemanager",
  "仓库经理",
  "仓库管理员",
  "warehouseadmin",
]);

function normalizeIdentityValue(value: string) {
  return value.trim().toLowerCase();
}

function uniquePermissionCodes(permissionCodes: string[]) {
  return Array.from(new Set(permissionCodes));
}

function filterAssignablePermissionCodes(
  permissionCodes: string[],
  assignablePermissions: PosTerminalPermissionOption[]
) {
  const assignableCodes = new Set(
    assignablePermissions.map((permission) => permission.code)
  );

  // 后端白名单是保存和展示的唯一边界，未知代码不能进入移动端草稿。
  return uniquePermissionCodes(
    permissionCodes.filter((permissionCode) =>
      assignableCodes.has(permissionCode)
    )
  );
}

export function getEffectivePosPermissionCodes(
  permissions: StoreUserPosTerminalPermissions
) {
  return filterAssignablePermissionCodes(
    permissions.effectivePermissionCodes,
    permissions.assignablePermissions
  );
}

export function buildGrantedPosPermissionCodes(
  selectedCodes: string[],
  assignablePermissions: PosTerminalPermissionOption[]
): string[] {
  return filterAssignablePermissionCodes(
    selectedCodes,
    assignablePermissions
  );
}

export function buildPosPermissionDraft(
  permissions: StoreUserPosTerminalPermissions
): PosPermissionDraft {
  const effectiveCodes = getEffectivePosPermissionCodes(permissions);

  return {
    baselineCodes: [...effectiveCodes],
    selectedCodes: [...effectiveCodes],
  };
}

export function groupPosPermissions(
  permissions: PosTerminalPermissionOption[]
): PosPermissionGroup[] {
  const groups = new Map<string, PosTerminalPermissionOption[]>();

  permissions.forEach((permission) => {
    const groupPermissions = groups.get(permission.group) ?? [];
    groupPermissions.push(permission);
    groups.set(permission.group, groupPermissions);
  });

  return Array.from(groups, ([group, groupPermissions]) => ({
    group,
    // 现代 JavaScript 的稳定排序会保留同名权限原有顺序。
    permissions: [...groupPermissions].sort((left, right) =>
      left.name.localeCompare(right.name, "zh-CN")
    ),
  }));
}

export function togglePosPermissionCode(
  selectedCodes: string[],
  permissionCode: string
) {
  const nextCodes = new Set(selectedCodes);

  if (nextCodes.has(permissionCode)) nextCodes.delete(permissionCode);
  else nextCodes.add(permissionCode);

  return Array.from(nextCodes);
}

export function setPosPermissionGroupSelection(
  selectedCodes: string[],
  groupPermissionCodes: string[],
  checked: boolean
) {
  const nextCodes = new Set(selectedCodes);

  // 批量操作只改当前分组，避免覆盖用户在其他分组的草稿选择。
  groupPermissionCodes.forEach((permissionCode) => {
    if (checked) nextCodes.add(permissionCode);
    else nextCodes.delete(permissionCode);
  });

  return Array.from(nextCodes);
}

export function arePermissionCodeSetsEqual(
  leftCodes: string[],
  rightCodes: string[]
) {
  const leftSet = new Set(leftCodes);
  const rightSet = new Set(rightCodes);

  return (
    leftSet.size === rightSet.size &&
    Array.from(leftSet).every((permissionCode) => rightSet.has(permissionCode))
  );
}

export function getPosPermissionEntryState({
  canManagePosTerminalPermissions,
  canManageStore,
  currentUserGuid,
  targetUserGuid,
  targetStatus,
  targetRoleNames,
  storeGuid,
}: PosPermissionEntryContext): PosPermissionEntryState {
  const normalizedCurrentUserGuid = currentUserGuid
    ? normalizeIdentityValue(currentUserGuid)
    : "";
  const normalizedTargetUserGuid = normalizeIdentityValue(targetUserGuid);
  const isCurrentUser =
    normalizedCurrentUserGuid.length > 0 &&
    normalizedCurrentUserGuid === normalizedTargetUserGuid;
  const hasPrivilegedRole = targetRoleNames.some((roleName) =>
    PRIVILEGED_ROLE_NAMES.has(normalizeIdentityValue(roleName))
  );

  // 隐藏条件属于业务资格判断，应优先于分店参数是否完整。
  if (
    !canManagePosTerminalPermissions ||
    !canManageStore ||
    isCurrentUser ||
    targetStatus !== 1 ||
    hasPrivilegedRole
  ) {
    return "hidden";
  }

  // 仅有空白的分店标识不可用于请求，按缺失参数显示禁用入口。
  return storeGuid?.trim() ? "enabled" : "disabled";
}

function getHttpStatus(error: unknown): number | undefined {
  if (!error || typeof error !== "object") return undefined;

  const directStatus = Reflect.get(error, "status");
  if (typeof directStatus === "number") return directStatus;

  const response = Reflect.get(error, "response");
  if (!response || typeof response !== "object") return undefined;

  const responseStatus = Reflect.get(response, "status");
  return typeof responseStatus === "number" ? responseStatus : undefined;
}

export function classifyPosPermissionError(
  error: unknown
): PosPermissionErrorKind {
  switch (getHttpStatus(error)) {
    case 401:
      return "unauthorized";
    case 403:
      return "forbidden";
    case 404:
      return "notFound";
    default:
      return "network";
  }
}
