// src/layout/noAutomaticReload.test.ts
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function collectSourceFiles(directory) {
  const entries = readdirSync(directory);
  const files = [];
  for (const entry of entries) {
    const fullPath = path.join(directory, entry);
    const stat = statSync(fullPath);
    if (stat.isDirectory()) {
      files.push(...collectSourceFiles(fullPath));
      continue;
    }
    if (/\.(tsx?|jsx?)$/.test(entry)) {
      files.push(fullPath);
    }
  }
  return files;
}
var root = process.cwd();
var pagesDirectory = path.join(root, "src/pages");
var pageReloadFiles = collectSourceFiles(pagesDirectory).filter(
  (file) => readFileSync(file, "utf8").includes("window.location.reload")
);
assert(
  pageReloadFiles.length === 0,
  `\u9875\u9762\u6587\u4EF6\u4E0D\u5E94\u81EA\u52A8 hard reload: ${pageReloadFiles.map((file) => path.relative(root, file)).join(", ")}`
);
var mobileLayoutSource = readFileSync(path.join(root, "src/layout/MobileLayout.tsx"), "utf8");
var adminLayoutSource = readFileSync(path.join(root, "src/layout/AdminLayout.tsx"), "utf8");
var shopLayoutSource = readFileSync(path.join(root, "src/layout/ShopLayout.tsx"), "utf8");
var shopCartDrawerSource = readFileSync(path.join(root, "src/components/ShopCartDrawer.tsx"), "utf8");
var errorBoundarySource = readFileSync(path.join(root, "src/components/GlobalErrorBoundary.tsx"), "utf8");
var containerDetailSource = readFileSync(path.join(root, "src/pages/Warehouse/ContainerDetail/index.tsx"), "utf8");
assert(
  mobileLayoutSource.includes("window.location.reload()"),
  "\u79FB\u52A8\u7AEF\u624B\u52A8\u5237\u65B0\u6309\u94AE\u5E94\u7EE7\u7EED\u4FDD\u7559\u4E3B\u52A8\u5237\u65B0\u80FD\u529B"
);
assert(
  mobileLayoutSource.includes('<div className="mobile-content" key={location.pathname}>'),
  "\u79FB\u52A8\u7AEF\u5F53\u524D\u9875\u9762\u5BB9\u5668\u5E94\u6309 pathname \u8BBE\u7F6E key\uFF0C\u907F\u514D\u4E0D\u540C\u8BE6\u60C5\u9875\u590D\u7528\u540C\u4E00\u7EC4\u4EF6\u5B9E\u4F8B"
);
assert(
  !adminLayoutSource.includes("openKeys={collapsed ? [] : openKeys}"),
  "\u684C\u9762\u4FA7\u8FB9\u680F\u6298\u53E0\u6001\u4E0D\u80FD\u53D7\u63A7\u4F20\u5165\u7A7A openKeys\uFF0C\u5426\u5219\u56FE\u6807\u5B50\u83DC\u5355\u65E0\u6CD5\u5F39\u51FA"
);
assert(
  adminLayoutSource.includes("{...(!collapsed") && adminLayoutSource.includes("openKeys,") && adminLayoutSource.includes("onOpenChange: (keys) => setOpenKeys(keys as string[])"),
  "\u684C\u9762\u4FA7\u8FB9\u680F\u5E94\u53EA\u5728\u5C55\u5F00\u6001\u53D7\u63A7 openKeys\uFF0C\u6298\u53E0\u6001\u4EA4\u7ED9 AntD \u7BA1\u7406\u5F39\u51FA\u5B50\u83DC\u5355"
);
assert(
  errorBoundarySource.includes("window.location.reload()"),
  "\u9519\u8BEF\u6062\u590D\u6309\u94AE\u5E94\u7EE7\u7EED\u4FDD\u7559\u4E3B\u52A8\u5237\u65B0\u80FD\u529B"
);
assert(
  shopLayoutSource.includes("window.addEventListener('focus', refreshFocusedCart)") && shopLayoutSource.includes("document.addEventListener('visibilitychange', refreshVisibleCart)"),
  "\u5546\u57CE\u5E03\u5C40\u5E94\u5728 Web \u9875\u9762\u56DE\u5230\u524D\u53F0\u65F6\u5237\u65B0\u8D2D\u7269\u8F66\uFF0C\u786E\u4FDD PDA \u52A0\u8D2D\u540E\u9876\u90E8\u8D2D\u7269\u8F66\u540C\u6B65"
);
assert(
  shopLayoutSource.includes("getActiveStoreOrderCartSummary") && shopLayoutSource.includes("const refreshCartSummary = useCallback") && shopLayoutSource.includes("const refreshFullCart = useCallback") && shopLayoutSource.includes("void refreshFullCart()") && shopLayoutSource.includes("cartDrawerOpen ? refreshFullCart() : refreshCartSummary()") && shopLayoutSource.includes("cartDrawerOpenRef.current ? refreshFullCart() : refreshCartSummary()"),
  "\u5546\u57CE\u5E03\u5C40\u767B\u5F55\u3001\u5207\u5E97\u548C\u666E\u901A\u524D\u53F0\u5237\u65B0\u5E94\u5148\u62C9\u8D2D\u7269\u8F66\u6458\u8981\uFF0C\u62BD\u5C49\u6253\u5F00\u65F6\u524D\u53F0\u5237\u65B0\u5E94\u4FDD\u7559\u5168\u91CF\u660E\u7EC6"
);
assert(
  shopLayoutSource.includes("// \u5207\u6362\u5206\u5E97\u5148\u6E05\u6389\u65E7\u8D2D\u7269\u8F66") && shopLayoutSource.includes("setCart(null)") && shopLayoutSource.includes("cartDrawerOpenRef.current ? refreshFullCart() : refreshCartSummary()"),
  "\u5546\u57CE\u5E03\u5C40\u5207\u6362\u5206\u5E97\u65F6\u5E94\u5148\u6E05\u7A7A\u65E7\u8D2D\u7269\u8F66\uFF0C\u62BD\u5C49\u6253\u5F00\u5219\u76F4\u63A5\u52A0\u8F7D\u65B0\u95E8\u5E97\u660E\u7EC6"
);
assert(
  shopLayoutSource.includes("selectedStoreCodeRef.current === storeCode"),
  "\u5546\u57CE\u8D2D\u7269\u8F66\u5237\u65B0\u54CD\u5E94\u56DE\u6765\u524D\u5E94\u786E\u8BA4\u4ECD\u662F\u5F53\u524D\u95E8\u5E97\uFF0C\u907F\u514D\u65E7\u95E8\u5E97\u8BF7\u6C42\u8986\u76D6\u65B0\u95E8\u5E97\u8D2D\u7269\u8F66"
);
assert(
  shopCartDrawerSource.includes("const canSubmitCart = !isCartDetailLoading && cartItems.length > 0") && shopCartDrawerSource.includes("disabled={!canSubmitCart || submitting}") && shopCartDrawerSource.includes("disabled={!canSubmitCart || preorderBlocked}"),
  "\u8D2D\u7269\u8F66\u62BD\u5C49\u5728 summary-only \u6216\u660E\u7EC6\u52A0\u8F7D\u4E2D\u65F6\u4E0D\u80FD\u5141\u8BB8\u63D0\u4EA4\u672A\u5C55\u793A\u660E\u7EC6\u7684\u8BA2\u5355"
);
assert(
  !shopLayoutSource.includes("setInterval("),
  "\u5546\u57CE\u8D2D\u7269\u8F66\u540C\u6B65\u4E0D\u5E94\u4F7F\u7528\u540E\u53F0\u8F6E\u8BE2\uFF0C\u907F\u514D\u6162\u67E5\u8BE2\u6301\u7EED\u6253\u5230\u670D\u52A1\u7AEF"
);
var viewportHookMatch = containerDetailSource.match(/function useContainerDetailViewport\(\) \{[\s\S]*?\n\}/);
if (viewportHookMatch) {
  assert(
    !viewportHookMatch[0].includes("loadData"),
    "\u8D27\u67DC\u660E\u7EC6\u6A2A\u7AD6\u5C4F\u76D1\u542C\u53EA\u80FD\u66F4\u65B0\u89C6\u53E3\u72B6\u6001\uFF0C\u4E0D\u5E94\u89E6\u53D1 loadData"
  );
}
assert(
  /void loadHeader\(shouldShowInitialLoading\)[\s\S]{0,220}\}, \[active, containerGuid\]\)/.test(containerDetailSource),
  "\u8D27\u67DC\u660E\u7EC6\u8868\u5934\u9996\u6B21\u52A0\u8F7D effect \u5E94\u53EA\u4F9D\u8D56 active \u548C containerGuid\uFF0C\u907F\u514D\u6A2A\u7AD6\u5C4F\u5207\u6362\u91CD\u65B0\u52A0\u8F7D"
);
assert(
  /void loadDetailChunk\(1, 'reset'\)[\s\S]{0,520}\}, \[active, activeLoadQueryKey\]\)/.test(containerDetailSource),
  "\u8D27\u67DC\u660E\u7EC6\u8FDC\u7A0B\u5206\u9875\u52A0\u8F7D effect \u5E94\u53EA\u4F9D\u8D56 active \u548C activeLoadQueryKey\uFF0C\u907F\u514D\u6A2A\u7AD6\u5C4F\u5207\u6362\u91CD\u65B0\u52A0\u8F7D"
);
console.log("noAutomaticReload.test: ok");
