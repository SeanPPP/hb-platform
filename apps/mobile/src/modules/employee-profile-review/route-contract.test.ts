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
assert.match(tabsLayout, /filterEmployeeProfileReviewRouteNames/);
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
assert.match(detailScreen, /AppState\.addEventListener/);
assert.match(detailScreen, /createEmployeeProfileReviewAppStateHandler/);
assert.match(detailScreen, /privacyShielded/);
assert.match(detailScreen, /queryClient\.fetchQuery/);
assert.match(detailScreen, /clearEmployeeProfileReviewDetailCache/);
assert.match(detailScreen, /employeeProfileReviewDetailQueryKey/);
assert.match(detailScreen, /gcTime:\s*0/);
assert.match(detailScreen, /getReviewFailureKind\(detailQuery\.error\)/);
assert.match(detailScreen, /setRevealedFields\(new Set\(\)\)/);
assert.match(detailScreen, /leaveDetail\("\/\(tabs\)\/settings"\)/);
assert.match(detailScreen, /getIdentityPhotoRefreshDelay/);
assert.match(detailScreen, /createIdentityPhotoErrorRefetchGuard/);
assert.match(detailScreen, /onError=/);
assert.doesNotMatch(
  detailScreen,
  /queryClient\.setQueryData\(/,
  "审核成功后不得把含完整敏感值的详情重新写入缓存"
);
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
assert.match(listScreen, /clearEmployeeProfileReviewListCache/);
assert.match(listScreen, /mergeUniqueEmployeeProfileReviewPages/);
assert.match(listScreen, /useFocusEffect/);
assert.match(listScreen, /resetQueries/);
assert.match(detailRoute, /EmployeeProfileReviewDetailScreen/);

}

void main();
