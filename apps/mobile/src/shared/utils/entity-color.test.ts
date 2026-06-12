import assert from "node:assert/strict";
import { getEntityTone } from "./entity-color";

function run() {
  const stableStoreTone = getEntityTone("ST001", "store");

  assert.deepEqual(
    stableStoreTone,
    getEntityTone("st001", "store"),
    "same key should map to the same store tone",
  );

  assert.deepEqual(
    getEntityTone("", "store"),
    {
      backgroundColor: "#F5F5F5",
      borderColor: "#D9D9D9",
      textColor: "#595959",
    },
    "empty key should map to the neutral tone",
  );

  assert.notDeepEqual(
    getEntityTone("ST001", "store"),
    getEntityTone("ST001", "supplier"),
    "store and supplier kinds should diverge for the same key",
  );

  const samplePalette = new Set(
    ["ST001", "ST002", "ST003", "ST004", "ST005", "ST006"].map((key) =>
      JSON.stringify(getEntityTone(key, "store")),
    ),
  );

  assert.ok(samplePalette.size > 1, "sample keys should span multiple tones");
  console.log("entity-color.test.ts: ok");
}

run();
