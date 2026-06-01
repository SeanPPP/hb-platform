import { buildScanLookupPayload } from "./scan-lookup-payload";

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

assertDeepEqual(
  buildScanLookupPayload(" 930001 ", "1024"),
  {
    barcode: " 930001 ",
    storeCode: "1024",
  },
  "scan lookup payload keeps the scanned barcode and sends current store code"
);

assertDeepEqual(
  buildScanLookupPayload("930001", null),
  {
    barcode: "930001",
  },
  "scan lookup payload omits blank store code"
);
