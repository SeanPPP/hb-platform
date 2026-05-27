import { shouldEnableSeasonalCardCatalog } from "./hooks";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  shouldEnableSeasonalCardCatalog(true),
  true,
  "submit-capable sessions should enable catalog query"
);

assertEqual(
  shouldEnableSeasonalCardCatalog(false),
  false,
  "view-only sessions should not enable catalog query"
);
