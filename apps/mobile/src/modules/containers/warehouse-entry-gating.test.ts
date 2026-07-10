import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = dirname(fileURLToPath(import.meta.url));
const warehouseSource = readFileSync(resolve(currentDir, "../../../app/(tabs)/warehouse.tsx"), "utf8");

const gateIndex = warehouseSource.indexOf("if (!canUseWarehouseTools)");
const entryIndex = warehouseSource.indexOf("{renderContainerEntry()}", gateIndex);
const segmentedIndex = warehouseSource.indexOf("<SegmentedButtons", gateIndex);

assert.ok(gateIndex >= 0, "Container.View only sessions must have a dedicated warehouse-screen branch");
assert.ok(entryIndex > gateIndex, "container-only branch should still render the container entry");
assert.ok(segmentedIndex > entryIndex, "legacy warehouse tools must stay after the container-only return");

console.log("warehouse-entry-gating.test.ts: ok");
