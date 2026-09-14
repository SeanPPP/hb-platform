export const ROLE_PERMISSIONS = {
  view: "Roles.View",
  create: "Roles.Create",
  edit: "Roles.Edit",
  managePermissions: "Roles.ManagePermissions",
  manageUsers: "Roles.ManageUsers",
} as const;

export interface RoleDraft {
  roleName: string;
  description: string;
  isActive: boolean;
}

export interface RoleDraftErrors {
  roleName?: "required" | "tooShort" | "tooLong";
  description?: "tooLong";
}

export interface PermissionItemLike {
  name: string;
  displayName: string;
  description?: string;
}

export interface PermissionCategoryLike {
  category: string;
  displayName: string;
  description?: string;
  permissions: PermissionItemLike[];
}

export interface PermissionDraft {
  selected: string[];
  baseline: string[];
}

export interface RoleCapabilityContext {
  isDeviceMode: boolean;
  isAdmin: boolean;
  hasPermission: (permission: string) => boolean;
}

export interface RoleCapabilities {
  canView: boolean;
  canCreate: boolean;
  canEdit: boolean;
  canManagePermissions: boolean;
  canManageUsers: boolean;
}

export interface RoleMenuDefinition {
  key: string;
  title: string;
  platform: "web" | "mobile";
  permissionCodes: string[];
  fixed?: boolean;
  requireAdmin?: boolean;
}

export interface RoleMenuPreviewItem extends RoleMenuDefinition {
  visible: boolean;
  readOnly: boolean;
}

export interface PermissionAliasLike {
  canonicalCode: string;
  aliasCodes: string[];
}

export interface RolePermissionStateLike {
  roleName: string;
  isSuperAdmin: boolean;
  implicitAllPermissions: boolean;
  explicitPermissionCodes: string[];
  effectivePermissionCodes: string[];
}

function normalizeCodes(codes: Iterable<string>) {
  return Array.from(new Set(Array.from(codes).map((code) => code.trim()).filter(Boolean))).sort();
}

export function validateRoleDraft(draft: RoleDraft): RoleDraftErrors {
  const errors: RoleDraftErrors = {};
  const nameLength = draft.roleName.trim().length;
  if (nameLength === 0) errors.roleName = "required";
  else if (nameLength < 2) errors.roleName = "tooShort";
  else if (nameLength > 50) errors.roleName = "tooLong";
  if (draft.description.trim().length > 200) errors.description = "tooLong";
  return errors;
}

export function toRoleMutationInput(draft: RoleDraft) {
  return {
    roleName: draft.roleName.trim(),
    description: draft.description.trim() || undefined,
    isActive: draft.isActive,
  };
}

export function isRoleDraftVerified(draft: RoleDraft, saved: { roleName: string; description?: string; isActive: boolean }) {
  const expected = toRoleMutationInput(draft);
  return saved.roleName.trim() === expected.roleName
    && (saved.description?.trim() || undefined) === expected.description
    && saved.isActive === expected.isActive;
}

export function getRoleCapabilities(context: RoleCapabilityContext): RoleCapabilities {
  if (context.isDeviceMode) {
    return {
      canView: false,
      canCreate: false,
      canEdit: false,
      canManagePermissions: false,
      canManageUsers: false,
    };
  }
  const allowed = (code: string) => context.isAdmin || context.hasPermission(code);
  return {
    canView: allowed(ROLE_PERMISSIONS.view),
    // RoleService 的写接口仍有 SuperAdmin 二次校验，移动端必须与真实服务端能力一致。
    canCreate: context.isAdmin && allowed(ROLE_PERMISSIONS.create),
    canEdit: context.isAdmin && allowed(ROLE_PERMISSIONS.edit),
    canManagePermissions: context.isAdmin && allowed(ROLE_PERMISSIONS.managePermissions),
    canManageUsers: context.isAdmin && allowed(ROLE_PERMISSIONS.manageUsers),
  };
}

export function isImplicitAllRole(
  state: Pick<RolePermissionStateLike, "roleName" | "isSuperAdmin" | "implicitAllPermissions">,
  superAdminRoleNames: Iterable<string> = ["Admin", "管理员"],
) {
  const roleName = state.roleName.trim().toLocaleLowerCase();
  return state.isSuperAdmin
    || state.implicitAllPermissions
    || Array.from(superAdminRoleNames).some((name) => name.trim().toLocaleLowerCase() === roleName);
}

export function expandPermissionAliases(codes: Iterable<string>, aliases: PermissionAliasLike[]) {
  const expanded = new Set(normalizeCodes(codes));
  for (const alias of aliases) {
    const family = [alias.canonicalCode, ...alias.aliasCodes];
    if (family.some((code) => Array.from(expanded).some((selected) => selected.localeCompare(code, undefined, { sensitivity: "accent" }) === 0))) {
      family.forEach((code) => expanded.add(code));
    }
  }
  return normalizeCodes(expanded);
}

export function resolveRolePreviewCodes({
  selectedCodes,
  aliases,
}: {
  selectedCodes: Iterable<string>;
  aliases: PermissionAliasLike[];
}) {
  // 角色模板只是创建时的种子信息，不是运行时继承；预览必须完全服从当前显式草稿。
  return expandPermissionAliases(selectedCodes, aliases);
}

export function isUncertainRoleWrite(error: unknown) {
  const candidate = error as { response?: { status?: number }; status?: number; apiBusinessError?: boolean } | null;
  const status = candidate?.response?.status ?? candidate?.status;
  if (status !== undefined) return status === 408 || status >= 500;
  // success=false 已获得服务端确定拒绝，只有没有服务端结论的异常才冻结防重。
  return candidate?.apiBusinessError !== true;
}

export function createPermissionDraft(codes: Iterable<string>): PermissionDraft {
  const normalized = normalizeCodes(codes);
  return { selected: normalized, baseline: normalized };
}

export function isPermissionDraftDirty(draft: PermissionDraft) {
  return !arePermissionCodesEqual(draft.selected, draft.baseline);
}

export function arePermissionCodesEqual(left: Iterable<string>, right: Iterable<string>) {
  const leftCodes = normalizeCodes(left);
  const rightCodes = normalizeCodes(right);
  return leftCodes.length === rightCodes.length && leftCodes.every((code, index) => code === rightCodes[index]);
}

export function togglePermission(draft: PermissionDraft, code: string): PermissionDraft {
  const selected = new Set(draft.selected);
  if (selected.has(code)) selected.delete(code);
  else selected.add(code);
  return { ...draft, selected: normalizeCodes(selected) };
}

export function togglePermissionCategory(draft: PermissionDraft, codes: Iterable<string>): PermissionDraft {
  const categoryCodes = normalizeCodes(codes);
  const selected = new Set(draft.selected);
  const allSelected = categoryCodes.length > 0 && categoryCodes.every((code) => selected.has(code));
  categoryCodes.forEach((code) => (allSelected ? selected.delete(code) : selected.add(code)));
  return { ...draft, selected: normalizeCodes(selected) };
}

export function acceptSavedPermissionDraft(draft: PermissionDraft): PermissionDraft {
  const selected = normalizeCodes(draft.selected);
  return { selected, baseline: selected };
}

export function reconcilePermissionDraft(current: PermissionDraft | null, remoteCodes: Iterable<string>) {
  // 后台刷新不能覆盖用户尚未保存的勾选；仅干净草稿接受服务器最新值。
  return current && isPermissionDraftDirty(current) ? current : createPermissionDraft(remoteCodes);
}

export function filterPermissionCategories<T extends PermissionCategoryLike>(categories: T[], keyword: string): T[] {
  const normalized = keyword.trim().toLocaleLowerCase();
  if (!normalized) return categories;
  return categories
    .map((category) => ({
      ...category,
      permissions: category.permissions.filter((permission) =>
        [permission.name, permission.displayName, permission.description ?? "", category.displayName]
          .some((value) => value.toLocaleLowerCase().includes(normalized)),
      ),
    }))
    .filter((category) => category.permissions.length > 0) as T[];
}

export function buildRoleMenuPreview(
  definitions: RoleMenuDefinition[],
  permissionCodes: Iterable<string>,
  access: { isSuperAdmin: boolean; implicitAllPermissions: boolean },
): RoleMenuPreviewItem[] {
  const selected = new Set(Array.from(permissionCodes).map((code) => code.toLocaleLowerCase()));
  return definitions.map((definition) => ({
    ...definition,
    visible:
      access.isSuperAdmin ||
      Boolean(definition.fixed) ||
      (!definition.requireAdmin && (access.implicitAllPermissions ||
        definition.permissionCodes.length === 0 ||
        definition.permissionCodes.some((code) => selected.has(code.toLocaleLowerCase()))
      )),
    readOnly: access.isSuperAdmin || access.implicitAllPermissions || Boolean(definition.fixed) || Boolean(definition.requireAdmin),
  }));
}

export function applyMenuPermissionChange(
  currentCodes: Iterable<string>,
  item: RoleMenuPreviewItem,
  visible: boolean,
  aliases: PermissionAliasLike[] = [],
  assignableCodes: Iterable<string> = item.permissionCodes,
) {
  if (item.readOnly || item.permissionCodes.length === 0) return normalizeCodes(currentCodes);
  const next = new Map(Array.from(currentCodes).map((code) => [code.toLocaleLowerCase(), code]));
  // 菜单满足任一权限即可；开启仅授予第一个直接权限，避免顺带扩大到编辑或管理权限。
  if (visible) {
    const assignable = new Map(Array.from(assignableCodes, (code) => [code.toLocaleLowerCase(), code]));
    const permissionCode = item.permissionCodes
      .flatMap((code) => [code, ...expandPermissionAliases([code], aliases)])
      .map((code) => assignable.get(code.toLocaleLowerCase()))
      .find((code): code is string => Boolean(code));
    if (permissionCode) next.set(permissionCode.toLocaleLowerCase(), permissionCode);
  }
  else {
    // 关闭时同时移除目录声明权限的等价别名，避免旧别名继续让入口可见。
    expandPermissionAliases(item.permissionCodes, aliases).forEach((code) => next.delete(code.toLocaleLowerCase()));
  }
  return normalizeCodes(next.values());
}
