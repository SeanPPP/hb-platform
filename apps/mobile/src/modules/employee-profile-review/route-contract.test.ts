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
assert.equal(
  detailScreen.match(/detailQuery\.refetch\(/g)?.length,
  1,
  "敏感详情只能在统一 refetch guard 内调用一次底层 refetch"
);
assert.match(
  detailScreen,
  /sensitiveDetailActivityGuard\.runIfActive\(\(\) => detailQuery\.refetch\(\)\)/
);
assert.match(detailScreen, /isRejectReasonValid/);
assert.match(detailScreen, /AppState\.addEventListener/);
assert.match(detailScreen, /createEmployeeProfileReviewAppStateHandler/);
assert.match(detailScreen, /privacyShielded/);
assert.match(detailScreen, /queryClient\.fetchQuery/);
assert.match(detailScreen, /createEmployeeProfileSensitiveDetailActivityGuard/);
assert.match(detailScreen, /sensitiveDetailActivityGuard\.runIfActive/);
assert.match(detailScreen, /sensitiveDetailActivityGuard\.fetch/);
assert.match(detailScreen, /clearEmployeeProfileReviewDetailCache/);
assert.match(detailScreen, /employeeProfileReviewDetailQueryKey/);
assert.match(detailScreen, /gcTime:\s*0/);
const reviewMutationSource = detailScreen.slice(
  detailScreen.indexOf("const reviewMutation = useMutation"),
  detailScreen.indexOf("const toggleReveal")
);
assert.ok(reviewMutationSource, "必须能定位审核 mutation 源码片段");
assert.doesNotMatch(
  reviewMutationSource,
  /gcTime\s*:/,
  "白名单审核结果可按默认生命周期回收，不能用 0ms GC 触发 observer 循环"
);
assert.match(detailScreen, /getReviewFailureKind\(detailQuery\.error\)/);
assert.match(detailScreen, /setRevealedFields\(new Set\(\)\)/);
assert.match(detailScreen, /leaveDetail\("\/\(tabs\)\/settings"\)/);
const headerBackSource = detailScreen.slice(
  detailScreen.indexOf("useLayoutEffect(() =>"),
  detailScreen.indexOf("const resumeDetailAfterForeground")
);
assert.ok(headerBackSource, "必须能定位详情页导航栏返回入口");
assert.match(headerBackSource, /navigation\.setOptions\(/);
assert.match(headerBackSource, /header: \(\) =>/);
assert.match(headerBackSource, /<SafeAreaView/);
assert.match(headerBackSource, /edges=\{\["top"\]\}/);
assert.match(headerBackSource, /<Pressable/);
assert.match(headerBackSource, /<MaterialCommunityIcons/);
assert.match(headerBackSource, /name="chevron-left"/);
assert.match(headerBackSource, /size=\{28\}/);
assert.match(headerBackSource, /color=\{theme\.colors\.primary\}/);
assert.match(headerBackSource, /<Text variant="titleLarge" style=\{styles\.headerTitle\} numberOfLines=\{1\}>/);
assert.match(headerBackSource, /\{t\("detail\.title"\)\}/);
assert.match(headerBackSource, /accessibilityRole="button"/);
assert.match(headerBackSource, /accessibilityLabel=\{t\("actions\.back"\)\}/);
assert.match(headerBackSource, /accessibilityState=\{\{ disabled: isLeavingSensitiveDetail \}\}/);
assert.match(headerBackSource, /disabled=\{isLeavingSensitiveDetail\}/);
assert.match(headerBackSource, /hitSlop=\{4\}/);
assert.match(
  headerBackSource,
  /onPress=\{\(\) => void leaveDetail\("\/\(tabs\)\/employee-profile-review"\)\}/,
  "导航栏返回按钮必须复用清理敏感缓存的安全退出流程"
);
assert.match(
  detailScreen,
  /headerBackButton:\s*\{[\s\S]*?width: 44,[\s\S]*?height: 44,[\s\S]*?alignItems: "center",[\s\S]*?justifyContent: "center"[\s\S]*?\}/
);
assert.match(detailScreen, /headerRow:\s*\{[\s\S]*?height: 44,[\s\S]*?borderBottomWidth: StyleSheet\.hairlineWidth/);
assert.doesNotMatch(headerBackSource, /headerLeft|unstable_headerLeftItems|IconButton/);
const headerBackButtonStyleSource = detailScreen.slice(
  detailScreen.indexOf("headerBackButton:"),
  detailScreen.indexOf("headerBackButtonPressed:")
);
assert.doesNotMatch(headerBackButtonStyleSource, /backgroundColor|borderRadius/);
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
