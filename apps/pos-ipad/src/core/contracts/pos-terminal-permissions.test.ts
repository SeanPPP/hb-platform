import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

import { ALL_POS_TERMINAL_PERMISSIONS } from "./pos-terminal-permissions";

test("紧急授权权限快照与后端 PosTerminal 常量逐项一致", async () => {
  const source = await readFile(
    "../../services/backend/BlazorApp.Shared/Constants/Permissions.cs",
    "utf8",
  );
  const start = source.indexOf("public static class PosTerminal");
  const end = source.indexOf(
    "public static class Orders",
    start,
  );
  assert.notEqual(start, -1);
  assert.notEqual(end, -1);
  const backend = [
    ...source
      .slice(start, end)
      .matchAll(
        /"(?<permission>Permissions\.PosTerminal\.[A-Za-z0-9.]+)"/gu,
      ),
  ]
    .map((match) => match.groups?.permission)
    .filter((value): value is string => Boolean(value))
    .sort();

  assert.deepEqual(
    [...ALL_POS_TERMINAL_PERMISSIONS],
    [...new Set(backend)],
  );
  assert.equal(
    new Set(ALL_POS_TERMINAL_PERMISSIONS).size,
    ALL_POS_TERMINAL_PERMISSIONS.length,
  );
  assert.equal(Object.isFrozen(ALL_POS_TERMINAL_PERMISSIONS), true);
});
