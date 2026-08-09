import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";

test("特殊商品拖动通过显式触控适配绑定 responder，禁止动态 spread", () => {
  const source = readFileSync(
    join(process.cwd(), "src/features/special-products/special-products-screen.tsx"),
    "utf8",
  );

  // 门禁要求每个 responder 回调在 JSX 中显式可审计，避免动态 spread
  // 意外接管点击或横向手势。
  assert.doesNotMatch(source, /\{\.\.\.panResponder\.panHandlers\}/u);
  assert.match(
    source,
    /<PosPanResponderView[\s\S]*panHandlers=\{panHandlers\}/u,
  );
});
