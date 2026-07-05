import assert from "node:assert/strict";
import { formatGrowthRate, getGrowthTone } from "./growth-rate";

assert.equal(formatGrowthRate(120, 100, "新增"), "+20.0%");
assert.equal(getGrowthTone(120, 100), "up");

assert.equal(formatGrowthRate(80, 100, "新增"), "-20.0%");
assert.equal(getGrowthTone(80, 100), "down");

assert.equal(formatGrowthRate(100, 100, "新增"), "0.0%");
assert.equal(getGrowthTone(100, 100), "flat");

assert.equal(formatGrowthRate(50, 0, "新增"), "新增");
assert.equal(getGrowthTone(50, 0), "up");

assert.equal(formatGrowthRate(0, 0, "新增"), "--");
assert.equal(getGrowthTone(0, 0), "flat");

