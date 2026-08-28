import { readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..", "..");
const statePath = join(repositoryRoot, "scripts", "pos-shared", "pos-shared-migration-state.json");
const ownershipPath = join(repositoryRoot, "scripts", "pos-shared", "pos-shared-ownership.json");
const state = JSON.parse(readFileSync(statePath, "utf8"));
const ownership = JSON.parse(readFileSync(ownershipPath, "utf8"));
const migrated = new Set(Object.values(state.migratedPaths).flat());
const reconciled = new Set((state.reconciledPaths ?? []).map(({ path }) => path));
const centralized = new Set((state.centralizedPaths ?? []).map(({ path }) => path));
const existingReasons = new Map(
  (state.retainedPaths ?? []).map(({ path, reason }) => [path, reason]),
);

const productionReasons = {
  "pos-domain": "仍依赖 App UI/runtime、反向层或双端分叉模块，需先提取纯 port 才能迁移",
  "pos-api-client": "仍依赖 App API facade、分叉 DTO 或平台策略 wrapper，保留为 App adapter",
  "pos-db": "仍耦合 App SQLite 组合实现或支付、小票具体类型，需先下沉 domain port",
  "pos-sync": "仍依赖 App 生命周期、分叉同步组合或未共享数据库实现，保留在 App",
  "pos-payments-core": "仍依赖 provider adapter、App runtime 或数据库具体实现，保留在 App",
  "pos-receipt-core": "仍依赖 App 打印设置、支付 adapter 或平台副作用，保留在 App",
  "pos-testing": "仅适合 App 级集成测试，尚未形成可跨包复用的 fixture 或 contract suite",
};

state.retainedPaths = ownership.files
  .filter(({ owner, path }) =>
    owner !== "app-local" &&
    !migrated.has(path) &&
    !reconciled.has(path) &&
    !centralized.has(path)
  )
  .map(({ kind, owner, path }) => ({
    path,
    owner,
    kind,
    reason: existingReasons.get(path) ?? (
      kind === "test"
        ? "测试依赖各 App 私有组合、平台分支或分叉 fixture，保留为双端集成契约门禁"
        : productionReasons[owner]
    ),
  }))
  .sort((left, right) => left.path.localeCompare(right.path));

writeFileSync(statePath, `${JSON.stringify(state, null, 2)}\n`);
