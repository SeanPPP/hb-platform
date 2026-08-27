import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const tabsDir = resolve(currentDir, "../../../app/(tabs)");

function readTabSource(fileName: string): string {
  return readFileSync(resolve(tabsDir, fileName), "utf8");
}

function assertFocusedHidOwner(fileName: string, componentName: string): void {
  const source = readTabSource(fileName);

  assert.match(
    source,
    /import \{[^}]*useIsFocused[^}]*\} from "@react-navigation\/native";/s,
    `${componentName} 必须读取当前路由焦点`,
  );
  assert.match(
    source,
    /const isFocused = useIsFocused\(\);/,
    `${componentName} 必须持有当前路由焦点状态`,
  );

  const hidUsage = source.match(/useHidBarcodeScanner\(\{[\s\S]*?\n\s*\}\);/)?.[0] ?? "";
  assert.ok(hidUsage, `${componentName} 必须接入 HID 扫码 Hook`);
  assert.match(
    hidUsage,
    /enabled:\s*isFocused,/,
    `${componentName} 只有在当前路由聚焦时才能接收 HID 扫码`,
  );
}

assertFocusedHidOwner("home.tsx", "Home");
assertFocusedHidOwner("cart.tsx", "Cart");
assertFocusedHidOwner("warehouse.tsx", "Warehouse");

const productQuerySource = readTabSource("product-query.tsx");
const productQueryContentStart = productQuerySource.indexOf("function ProductQueryContent()");
const productQueryScreenStart = productQuerySource.indexOf("export default function ProductQueryScreen()");
assert.notEqual(productQueryContentStart, -1, "商品维护内容组件必须存在");
assert.notEqual(productQueryScreenStart, -1, "商品维护路由组件必须存在");

const productQueryContentSource = productQuerySource.slice(
  productQueryContentStart,
  productQueryScreenStart,
);
const productQueryScreenSource = productQuerySource.slice(productQueryScreenStart);
const productQueryHidUsage =
  productQueryContentSource.match(/useHidBarcodeScanner\(\{[\s\S]*?\n\s*\}\);/)?.[0] ?? "";

assert.match(
  productQueryContentSource,
  /const isFocused = useIsFocused\(\);/,
  "商品维护内容必须自行读取路由焦点，失焦时仍保持挂载",
);
assert.match(
  productQueryHidUsage,
  /enabled:\s*isFocused\s*&&\s*!scannerInputBlocked,/,
  "商品维护 HID 必须同时受路由焦点和页面 busy 状态门禁",
);
assert.match(
  productQueryContentSource,
  /const cameraScanDisabled = !isFocused \|\| scannerInputBlocked;/,
  "商品维护相机事件必须同时受路由焦点和页面 busy 状态门禁",
);
assert.match(
  productQueryContentSource,
  /resetKey:\s*\[[\s\S]*?isFocused\s*\?\s*"focused"\s*:\s*"blurred"[\s\S]*?\]\.join\(":"\)/,
  "商品维护相机 resetKey 必须随路由焦点变化，使旧页面回调失效",
);
assert.match(
  productQueryContentSource,
  /if \(!isFocused\) \{\s*setCameraVisible\(false\);\s*\}/s,
  "商品维护失焦时必须关闭单次相机画面",
);
assert.match(
  productQueryContentSource,
  /if \(!isFocused\) \{\s*return null;\s*\}/s,
  "商品维护失焦时必须卸载相机预览",
);
assert.doesNotMatch(
  productQueryScreenSource,
  /if \(!isFocused\)/,
  "商品维护路由失焦时不得卸载内容和未保存编辑状态",
);
assert.match(
  productQueryScreenSource,
  /return <ProductQueryContent \/>;/,
  "商品维护路由必须始终保持内容组件挂载",
);

console.log("hid-route-ownership-source.test.ts: ok");
