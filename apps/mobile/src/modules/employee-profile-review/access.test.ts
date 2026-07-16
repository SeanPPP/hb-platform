import assert from "node:assert/strict";
import {
  EMPLOYEE_PROFILE_REVIEW_PERMISSION,
  getEmployeeProfileReviewAccess,
} from "./access";

function access(
  roleNames: string[],
  permissions: string[] = [],
  menuRouteNames: string[] = ["employee-profile-review"],
  sessionKind: "account" | "device" | "iosReview" = "account"
) {
  return getEmployeeProfileReviewAccess({
    roleNames,
    permissions,
    menuRouteNames,
    sessionKind,
  });
}

for (const role of ["Admin", "管理员", "SuperAdmin", "超级管理员"]) {
  assert.equal(access([role], [], []).allowed, true, `${role} 可直接审核`);
}

for (const role of ["StoreManager", "店长", "经理"]) {
  assert.equal(
    access([role], [EMPLOYEE_PROFILE_REVIEW_PERMISSION]).allowed,
    true,
    `${role} 具备权限和菜单时可审核`
  );
  assert.equal(access([role], []).reason, "permission");
  assert.equal(
    access([role], [EMPLOYEE_PROFILE_REVIEW_PERMISSION], []).reason,
    "menu"
  );
}

assert.equal(
  access(["WarehouseManager"], [EMPLOYEE_PROFILE_REVIEW_PERMISSION]).reason,
  "role"
);
assert.equal(
  access(["仓库经理", "店长"], [EMPLOYEE_PROFILE_REVIEW_PERMISSION]).reason,
  "role"
);
assert.equal(
  access(["Admin"], [EMPLOYEE_PROFILE_REVIEW_PERMISSION], ["employee-profile-review"], "iosReview").reason,
  "iosReview"
);
assert.equal(
  access(["Admin"], [EMPLOYEE_PROFILE_REVIEW_PERMISSION], ["employee-profile-review"], "device").reason,
  "device"
);
assert.equal(access(["User"], [EMPLOYEE_PROFILE_REVIEW_PERMISSION]).reason, "role");
