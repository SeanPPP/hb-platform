import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";

const source = readFileSync(join(__dirname, "store.ts"), "utf8");

assert.match(
  source,
  /requestGeneration !== generation[\s\S]*return get\(\)\.items/,
  "迟到的菜单请求不得跨会话写回状态"
);
assert.match(
  source,
  /fetchMenu\(\{ background: true \}\)/,
  "自动恢复必须使用后台加载模式"
);
assert.match(
  source,
  /replaceMenu\(items\)[\s\S]*requestGeneration \+= 1/,
  "替换 Review 菜单前必须使在途请求失效"
);
assert.match(
  source,
  /reset\(\)[\s\S]*requestGeneration \+= 1/,
  "登出重置必须使在途请求失效"
);

console.log("store-recovery.test.ts: ok");
