import assert from "node:assert/strict";
import test from "node:test";

import { multiplyCentsAwayFromZero } from "./money";

test("multiplyCentsAwayFromZero：0.29 × 50 必须为 15（C# decimal 半离零语义）", () => {
  // 0.29 * 50 在 JS double 中是 14.499999999999998，Math.round 会给 14；
  // C# decimal 精确为 14.5，AwayFromZero 取 15。必须用十进制字符串/整数算法。
  assert.equal(multiplyCentsAwayFromZero(0.29, 50), 15);
});

test("multiplyCentsAwayFromZero：精确半值与非半值小数按 AwayFromZero 取整", () => {
  assert.equal(multiplyCentsAwayFromZero(0.5, 501), 251);
  assert.equal(multiplyCentsAwayFromZero(1.25, 1_000), 1_250);
  assert.equal(multiplyCentsAwayFromZero(1.5, 501), 752);
  assert.equal(multiplyCentsAwayFromZero(2.75, 4), 11);
  assert.equal(multiplyCentsAwayFromZero(1.005, 100), 101);
});

test("multiplyCentsAwayFromZero：负数量同样远离零取整", () => {
  assert.equal(multiplyCentsAwayFromZero(-0.29, 50), -15);
  assert.equal(multiplyCentsAwayFromZero(-0.5, 501), -251);
});

test("multiplyCentsAwayFromZero：整数路径保持精确 BigInt，不回归", () => {
  assert.equal(multiplyCentsAwayFromZero(2, 501), 1_002);
  assert.equal(multiplyCentsAwayFromZero(0, 501), 0);
  assert.equal(
    multiplyCentsAwayFromZero(69_431, 129_728_784_761),
    Number.MAX_SAFE_INTEGER,
  );
});

test("multiplyCentsAwayFromZero：拒绝非有限数量与非安全整数分币", () => {
  for (const quantity of [NaN, Infinity, -Infinity]) {
    assert.throws(
      () => multiplyCentsAwayFromZero(quantity, 50),
      RangeError,
    );
  }
  for (const cents of [NaN, Infinity, 0.5, 1.5, Number.MAX_SAFE_INTEGER + 1]) {
    assert.throws(
      () => multiplyCentsAwayFromZero(1, cents),
      RangeError,
    );
  }
});

test("multiplyCentsAwayFromZero：越界结果拒绝（正负两个方向）", () => {
  assert.throws(
    () => multiplyCentsAwayFromZero(69_432, 129_728_784_761),
    RangeError,
  );
  assert.throws(
    () => multiplyCentsAwayFromZero(-69_432, 129_728_784_761),
    RangeError,
  );
  // 1e-7 与 1e21 走指数展开路径，也必须被精确处理或拒绝。
  assert.equal(multiplyCentsAwayFromZero(1e-7, 50), 0);
  assert.throws(() => multiplyCentsAwayFromZero(1e21, 50), RangeError);
});
