import assert from "node:assert/strict";
import test from "node:test";

import { createAud } from "../../../core/contracts";

import { calculateCashSettlement, roundCashAmount } from "./index";

test("AUD cash rounding matches WPF boundaries for positive, negative and zero", () => {
  assert.equal(roundCashAmount(createAud(782)).cents, 780);
  assert.equal(roundCashAmount(createAud(783)).cents, 785);
  assert.equal(roundCashAmount(createAud(-782)).cents, -780);
  assert.equal(roundCashAmount(createAud(-783)).cents, -785);
  assert.equal(roundCashAmount(createAud(0)).cents, 0);
});

test("cash settlement rounds only the remaining cash portion and calculates change", () => {
  assert.deepEqual(
    calculateCashSettlement({
      actualAmount: createAud(783),
      nonCashAmount: createAud(0),
      cashTendered: createAud(1_000),
    }),
    {
      cashDue: createAud(785),
      normalizedCashTendered: createAud(1_000),
      change: createAud(215),
      roundingAdjustment: createAud(2),
    },
  );

  assert.deepEqual(
    calculateCashSettlement({
      actualAmount: createAud(783),
      nonCashAmount: createAud(300),
      cashTendered: createAud(500),
    }),
    {
      cashDue: createAud(485),
      normalizedCashTendered: createAud(500),
      change: createAud(15),
      roundingAdjustment: createAud(2),
    },
  );
});
test("refund cash rounding preserves negative direction without fabricating change", () => {
  assert.deepEqual(
    calculateCashSettlement({
      actualAmount: createAud(-783),
      nonCashAmount: createAud(0),
      cashTendered: createAud(-785),
    }),
    {
      cashDue: createAud(-785),
      normalizedCashTendered: createAud(-785),
      change: createAud(0),
      roundingAdjustment: createAud(-2),
    },
  );
});
