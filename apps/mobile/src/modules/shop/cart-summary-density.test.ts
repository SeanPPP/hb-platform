import {
  resolveCartSkuCount,
  resolveCartSummaryScale,
  resolveCheckoutBarMaxHeight,
} from "./cart-summary-density";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  resolveCartSummaryScale({ width: 430, height: 850 }),
  1,
  "regular screens keep the full cart summary scale"
);

assertEqual(
  resolveCartSummaryScale({ width: 360, height: 640 }),
  0.84,
  "small screens shrink cart summary proportionally"
);

assertEqual(
  resolveCartSummaryScale({ width: 320, height: 568 }),
  0.82,
  "very small screens clamp to a readable minimum scale"
);

assertEqual(
  resolveCartSkuCount({
    productCodes: ["P001", "P002", "P001"],
    reportedSkuCount: 0,
  }),
  2,
  "reported zero sku count falls back to distinct cart product codes"
);

assertEqual(
  resolveCartSkuCount({
    productCodes: ["P001", "P002"],
    reportedSkuCount: 5,
  }),
  5,
  "positive reported sku count remains authoritative"
);

assertEqual(
  resolveCheckoutBarMaxHeight({ width: 360, height: 640 }),
  96,
  "checkout bar max height is 15 percent of app height"
);
