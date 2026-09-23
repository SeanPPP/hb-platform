import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const workbenchSource = readFileSync(resolve(currentDir, "workbench-screen.tsx"), "utf8");
const authStoreSource = readFileSync(resolve(currentDir, "../../store/auth-store.ts"), "utf8");

// 后台改角色权限后必须能免重新登录生效：工作台下拉刷新同时刷新账号权限、菜单与分店。
assert.match(workbenchSource, /refreshControl=\{<RefreshControl/, "工作台需要下拉刷新");
assert.match(workbenchSource, /await refreshCurrentUser\(\)/, "下拉刷新必须重新拉取当前账号权限");
assert.match(workbenchSource, /fetchMenu\(\{ background: true \}\)/, "下拉刷新必须重新拉取菜单");
assert.match(workbenchSource, /invalidateQueries\(\{ queryKey: \["userStores"\] \}\)/, "下拉刷新必须刷新分店列表");

// 刷新只覆盖同一在线账号，避免请求期间退出或切换账号后写回旧账号数据。
assert.match(authStoreSource, /async refreshCurrentUser\(\)/);
assert.match(authStoreSource, /if \(!current\.isAuthenticated \|\| !sameUser\)/);
assert.match(authStoreSource, /set\(\{ user, access: buildAccess\(user\) \}\)/);

console.log("workbench-refresh-source.test.ts: ok");
