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
  /reset\(options = \{\}\)[\s\S]*requestGeneration \+= 1/,
  "登出重置必须使在途请求失效"
);
// 缓存兜底时菜单是可用的：报错会让工作台清空功能列表并盖上「加载失败」，
// 而底部 tab 用的是同一份缓存菜单、入口是全的，两处自相矛盾。
assert.match(
  source,
  /errorMessage: servedFromCache \? null : errorMessage/,
  "缓存兜底不得对外表现为菜单加载失败"
);
assert.match(
  source,
  /servedFromCache: boolean/,
  "缓存兜底状态必须对外可见，供界面区分「不可用」与「用的是缓存」"
);
assert.match(
  source,
  /if \(error !== null && \(!hasUsableCurrentMenu \|\| servedFromCache\)\)[\s\S]{0,320}?scheduleNavigationRecovery/,
  "缓存兜底仍必须继续后台重试，否则联网后不会刷新成服务端最新菜单"
);

console.log("store-recovery.test.ts: ok");
