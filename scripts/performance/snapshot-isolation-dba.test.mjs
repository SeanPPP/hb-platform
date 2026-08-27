import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const sql = readFileSync(
  resolve(scriptDirectory, "dba-enable-sqlserver-snapshot-isolation.sql"),
  "utf8",
);
const executableSql = sql
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .replace(/--.*$/gm, "");

test("Snapshot DBA 脚本只启用显式 Snapshot 且不强制回滚现有事务", () => {
  assert.match(executableSql, /ALTER\s+DATABASE[\s\S]+ALLOW_SNAPSHOT_ISOLATION\s+ON/i);
  assert.match(executableSql, /snapshot_isolation_state\s*=\s*1/i);
  assert.match(executableSql, /@@TRANCOUNT/i);
  assert.doesNotMatch(executableSql, /ROLLBACK\s+IMMEDIATE/i);
  assert.doesNotMatch(executableSql, /READ_COMMITTED_SNAPSHOT\s+ON/i);
});
