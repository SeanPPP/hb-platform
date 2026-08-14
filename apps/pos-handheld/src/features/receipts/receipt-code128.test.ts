import assert from "node:assert/strict";
import test from "node:test";

import {
  receiptCode128,
  receiptCode128ModuleWidth,
} from "./receipt-code128";

const guid = "10000000-0000-4000-8000-000000000042";

test("Code 128 自动在 B/C 集合间选择，载荷仍解码为完整 GUID", () => {
  const encoded = receiptCode128(guid);

  assert.equal(decodeEscPosCode128Payload(encoded.payload), guid);
  assert.ok(encoded.payload.includes("{C"));
  assert.ok(encoded.payload.includes("{B"));
  assert.ok(encoded.moduleCount + 20 <= 384);
  assert.equal(receiptCode128ModuleWidth(encoded, "58mm"), null);
  assert.equal(receiptCode128ModuleWidth(encoded, "80mm"), null);
});

test("完整 GUID 只有在标准双点模块及静区都装入纸宽时才允许输出", () => {
  const worstCase = receiptCode128("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
  const shortValue = receiptCode128("ORDER-42");

  assert.ok((worstCase.moduleCount + 20) * 2 > 576);
  assert.equal(receiptCode128ModuleWidth(worstCase, "58mm"), null);
  assert.equal(receiptCode128ModuleWidth(worstCase, "80mm"), null);
  assert.equal(receiptCode128ModuleWidth(shortValue, "58mm"), 2);
  assert.equal(receiptCode128ModuleWidth(shortValue, "80mm"), 2);
});

function decodeEscPosCode128Payload(payload: string): string {
  let set: "B" | "C" | null = null;
  let output = "";
  for (let index = 0; index < payload.length;) {
    if (payload[index] === "{" && payload[index + 1] === "{") {
      output += "{";
      index += 2;
      continue;
    }
    if (payload[index] === "{" && (payload[index + 1] === "B" || payload[index + 1] === "C")) {
      set = payload[index + 1] as "B" | "C";
      index += 2;
      continue;
    }
    if (set === "C") {
      output += payload.slice(index, index + 2);
      index += 2;
      continue;
    }
    output += payload[index];
    index += 1;
  }
  return output;
}
