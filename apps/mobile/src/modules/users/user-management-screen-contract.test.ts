import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDirectory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(
  resolve(currentDirectory, "../../../app/(shell)/users/index.tsx"),
  "utf8"
);

assert.match(
  source,
  /const canCreateUsers = access\.isAdmin \|\| access\.hasPermission\(PERMISSIONS\.Users\.Create\)/,
  "新增店员必须使用独立 Users.Create 权限门槛"
);
assert.match(
  source,
  /const canEditUsers = access\.isAdmin \|\| access\.hasPermission\(PERMISSIONS\.Users\.Edit\)/,
  "编辑店员必须使用独立 Users.Edit 权限门槛"
);
assert.match(
  source,
  /const canResetPasswords = access\.isAdmin \|\| access\.hasPermission\(PERMISSIONS\.Users\.ResetPassword\)/,
  "重置店员密码必须使用独立 Users.ResetPassword 权限门槛"
);
assert.match(
  source,
  /const selectedStoreCanManageUsers =\s*\(canCreateUsers \|\| canEditUsers \|\| canResetPasswords\) &&/,
  "只具备创建或重置权限时，可管理分店不应显示为只读"
);
assert.match(
  source,
  /const canResetUserPassword = useCallback/,
  "密码重置必须使用独立的分店权限判断"
);
assert.match(
  source,
  /disabled=\{!canResetThisUser\}/,
  "密码重置按钮必须按 Users.ResetPassword 独立禁用"
);
assert.match(source, /createMutation/, "用户页必须接入创建 mutation");
assert.match(source, /onPress=\{openCreateDialog\}/, "标题区必须提供新增店员入口");
assert.match(
  source,
  /disabled=\{!selectedStoreCanCreate \|\| isBusy\}/,
  "未选择可管理分店时必须禁用新增入口"
);
assert.match(source, /t\("actions\.create"\)/, "新增入口必须使用本地化文案");
assert.match(source, /t\("dialogs\.createTitle"\)/, "创建弹窗必须使用创建标题");
assert.match(source, /t\("fields\.initialPassword"\)/, "创建弹窗必须要求初始密码");
assert.match(source, /createMutation\.mutateAsync/, "保存创建表单必须调用创建接口");
assert.match(
  source,
  /editingUserGuid \? t\("actions\.save"\) : t\("actions\.create"\)/,
  "创建弹窗的主按钮必须显示新增文案"
);
assert.match(source, /t\("messages\.userCreated"\)/, "创建成功必须给出明确反馈");
assert.match(
  source,
  /console\.warn\("\[store-users\] save failed", toSafeStoreUserErrorLog\(error\)\)/,
  "创建或编辑失败只能记录脱敏后的错误元数据"
);
assert.match(
  source,
  /console\.warn\("\[store-users\] password reset failed", toSafeStoreUserErrorLog\(error\)\)/,
  "密码重置失败只能记录脱敏后的错误元数据"
);
assert.doesNotMatch(
  source,
  /console\.warn\("\[store-users\] (?:save|password reset) failed", error\)/,
  "含密码请求失败时不得直接记录 Axios 错误对象"
);

console.log("user-management-screen-contract.test.ts: ok");
