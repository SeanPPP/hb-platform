import assert from "node:assert/strict";
import {
  buildWarehouseLocationCode,
  getAvailableWarehouseLocationSlots,
  resolveAvailableWarehouseLocationParts,
  splitWarehouseLocationCode,
  type WarehouseLocationCodeParts,
} from "./location-code-options";
import type { WarehouseLocation } from "./types";

const slotOptions = Array.from({ length: 100 }, (_, index) => String(index).padStart(2, "0"));

function location(locationGuid: string, locationCode: string): WarehouseLocation {
  return {
    locationGuid,
    locationCode,
    locationBarcode: null,
    locationType: 1,
    status: 1,
    productCount: 0,
  };
}

function parts(slot: string): WarehouseLocationCodeParts {
  return { letter: "A", section: "00", shelf: "00", slot };
}

assert.deepEqual(splitWarehouseLocationCode("a-00-00-01"), parts("01"));
assert.equal(buildWarehouseLocationCode(parts("09")), "A-00-00-09");

assert.deepEqual(
  getAvailableWarehouseLocationSlots(
    [
      location("slot-1", "A-00-00-01"),
      location("other-shelf", "A-00-01-01"),
      location("other-letter", "B-00-00-02"),
    ],
    parts("01"),
    slotOptions
  ).slice(0, 3),
  ["00", "02", "03"]
);

assert.equal(
  getAvailableWarehouseLocationSlots(
    [location("slot-1", "A-00-00-01")],
    parts("01"),
    slotOptions,
    "slot-1"
  ).includes("01"),
  true
);

assert.equal(
  getAvailableWarehouseLocationSlots(
    [location("invalid-code", "")],
    parts("01"),
    slotOptions
  ).includes("01"),
  true
);

assert.deepEqual(
  resolveAvailableWarehouseLocationParts(
    [location("slot-1", "A-00-00-01")],
    parts("01"),
    slotOptions
  ),
  parts("00")
);

assert.deepEqual(
  getAvailableWarehouseLocationSlots(
    slotOptions.map((slot) => location(`slot-${slot}`, buildWarehouseLocationCode(parts(slot)))),
    parts("01"),
    slotOptions
  ),
  []
);

console.log("location-code-options.test.ts: ok");
