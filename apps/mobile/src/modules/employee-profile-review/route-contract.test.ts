import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const moduleDir = dirname(fileURLToPath(import.meta.url));
const mobileRoot = resolve(moduleDir, "../../..");
const read = (path: string) => readFile(resolve(mobileRoot, path), "utf8");

async function main() {
const [tabsLayout, tabRoute, detailRoute, rootLayout, iosReviewMenu, detailScreen] = await Promise.all([
  read("app/(tabs)/_layout.tsx"),
  read("app/(tabs)/employee-profile-review.tsx"),
  read("app/employee-profile-review/[requestId].tsx"),
  read("app/_layout.tsx"),
  read("src/modules/ios-review/menu.ts"),
  read("src/modules/employee-profile-review/employee-profile-review-detail-screen.tsx"),
]);

assert.match(tabsLayout, /name="employee-profile-review"/);
assert.match(tabsLayout, /tabs\.employeeProfileReview/);
assert.match(tabRoute, /EmployeeProfileReviewListScreen/);
assert.match(detailRoute, /EmployeeProfileReviewDetailScreen/);
assert.match(detailScreen, /maskSensitiveValue/);
assert.match(detailScreen, /toggleReveal/);
assert.match(detailScreen, /approveEmployeeProfileReviewApi/);
assert.match(detailScreen, /rejectEmployeeProfileReviewApi/);
assert.match(detailScreen, /getReviewFailureKind/);
assert.match(detailScreen, /setStaleAfterConflict\(true\)/);
assert.match(detailScreen, /detailQuery\.refetch\(\)/);
assert.match(detailScreen, /isRejectReasonValid/);
assert.match(rootLayout, /name="employee-profile-review"/);
assert.doesNotMatch(
  iosReviewMenu,
  /routeName:\s*"employee-profile-review"/,
  "iOS 审核账号菜单不得包含真实敏感资料审核入口"
);

// 列表组件不允许读取完整敏感字段，详情组件才可在授权后读取。
const listScreen = await read("src/modules/employee-profile-review/employee-profile-review-list-screen.tsx");
assert.doesNotMatch(listScreen, /\.bankAccountNumber|\.superannuationAccountNumber|\.identityId/);
assert.match(listScreen, /getEmployeeProfileReviewAccess/);
assert.match(detailRoute, /EmployeeProfileReviewDetailScreen/);

}

void main();
