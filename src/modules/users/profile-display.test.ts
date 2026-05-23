import { calculateAge, maskTrailingFour } from "./profile-display";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const referenceDate = new Date("2026-05-23T00:00:00Z");

assertEqual(calculateAge("1990-05-22", referenceDate), 36, "age when birthday has passed");
assertEqual(calculateAge("1990-05-24", referenceDate), 35, "age before birthday this year");
assertEqual(calculateAge("not-a-date", referenceDate), null, "invalid birthday returns null");
assertEqual(calculateAge(undefined, referenceDate), null, "missing birthday returns null");

assertEqual(maskTrailingFour("12345678", "--"), "1234****", "masks trailing four digits");
assertEqual(maskTrailingFour("1234", "--"), "****", "masks short values fully");
assertEqual(maskTrailingFour("  SUPER-001  ", "--"), "SUPER****", "trims and masks text");
assertEqual(maskTrailingFour(undefined, "--"), "--", "empty value uses fallback");
