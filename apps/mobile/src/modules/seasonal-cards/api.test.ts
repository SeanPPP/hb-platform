import {
  buildSeasonalCardSubmissionPayload,
  buildSeasonalCardSubmissionQuery,
  normalizeSeasonalCardCatalogResponse,
  normalizeSeasonalCardSubmissionsResponse,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const submissionPayload = buildSeasonalCardSubmissionPayload({
  storeCode: " STO01 ",
  catalogGuid: " catalog-1 ",
  seasonYear: 2026.8,
  remainingQuantity: 12.9,
  customUnitPrice: 4.5,
  remark: "  urgent  ",
});

assertDeepEqual(
  submissionPayload,
  {
    storeCode: "STO01",
    catalogGuid: "catalog-1",
    seasonYear: 2026,
    remainingQuantity: 12,
    customUnitPrice: 4.5,
    remark: "urgent",
  },
  "submission payload trims text fields and coerces numeric values"
);

const queryPayload = buildSeasonalCardSubmissionQuery({
  storeCode: " STO01 ",
  cardType: 2.9,
  seasonYear: 2026.2,
  pageNumber: 2.9,
  pageSize: 50.1,
});

assertDeepEqual(
  queryPayload,
  {
    storeCode: "STO01",
    cardType: 2,
    seasonYear: 2026,
    pageNumber: 2,
    pageSize: 50,
  },
  "history query sends numeric card type enum and keeps supported pagination"
);

const defaultQueryPayload = buildSeasonalCardSubmissionQuery({
  storeCode: " ",
  pageNumber: 0,
  pageSize: 999,
});

assertEqual(defaultQueryPayload.storeCode, undefined, "blank store code is omitted from query");
assertEqual(defaultQueryPayload.pageNumber, 1, "invalid query page defaults to first page");
assertEqual(defaultQueryPayload.pageSize, 20, "invalid query page size defaults to 20");

const catalog = normalizeSeasonalCardCatalogResponse({
  success: true,
  data: [
    {
      catalogGuid: "catalog-1",
      cardType: 1,
      cardTypeName: "Christmas",
      priceOption: 1,
      priceOptionName: "$1",
      priceLabel: "$1",
      fixedUnitPrice: "1.00",
      allowsCustomUnitPrice: false,
      isEnabled: true,
      sortOrder: 1,
    },
    {
      catalogGuid: "catalog-2",
      cardType: 1,
      cardTypeName: "Christmas",
      priceOption: 4,
      priceOptionName: "Other",
      priceLabel: "其他",
      fixedUnitPrice: null,
      allowsCustomUnitPrice: 1,
      isEnabled: 0,
      sortOrder: 99,
    },
  ],
});

assertEqual(catalog.length, 2, "catalog response reads array directly from envelope data");
assertEqual(catalog[0]?.catalogGuid, "catalog-1", "catalog response normalizes catalog guid");
assertEqual(catalog[0]?.cardType, 1, "catalog response keeps numeric card type enum");
assertEqual(catalog[0]?.cardTypeName, "Christmas", "catalog response keeps card type name");
assertEqual(catalog[0]?.priceOption, 1, "catalog response keeps numeric price option enum");
assertEqual(catalog[0]?.priceLabel, "$1", "catalog response keeps display price label");
assertEqual(catalog[0]?.fixedUnitPrice, 1, "catalog response normalizes fixed unit price");
assertEqual(catalog[0]?.isEnabled, true, "catalog response normalizes enabled flag");
assertEqual(catalog[1]?.allowsCustomUnitPrice, true, "catalog response normalizes custom price flag");
assertEqual(catalog[1]?.isEnabled, false, "catalog response normalizes disabled catalog entries");

const unwrappedCatalog = normalizeSeasonalCardCatalogResponse([
  {
    catalogGuid: "catalog-unwrapped",
    cardType: 5,
    priceOption: 3,
    priceLabel: "$3",
    fixedUnitPrice: 3,
    isEnabled: true,
  },
]);

assertEqual(
  unwrappedCatalog.length,
  1,
  "catalog response keeps arrays already unwrapped by apiClient"
);
assertEqual(
  unwrappedCatalog[0]?.catalogGuid,
  "catalog-unwrapped",
  "catalog response normalizes already unwrapped catalog rows"
);

const submissions = normalizeSeasonalCardSubmissionsResponse({
  success: true,
  data: {
    items: [
      {
        submissionGuid: "submission-1",
        storeCode: "STO01",
        catalogGuid: "catalog-1",
        cardType: 3,
        cardTypeName: "Easter",
        seasonYear: "2026",
        unitPrice: "3.50",
        priceLabel: "$3",
        remainingQuantity: "8",
        remark: "legacy",
        submittedByName: "Alice",
        submittedAt: "2026-05-27T10:00:00Z",
      },
    ],
    totalCount: "3",
    page: "2",
    pageSize: "50",
  },
});

assertEqual(submissions.items.length, 1, "history response keeps items");
assertEqual(
  submissions.items[0]?.submissionGuid,
  "submission-1",
  "history response normalizes submission guid"
);
assertEqual(submissions.items[0]?.cardType, 3, "history response keeps numeric card type enum");
assertEqual(submissions.items[0]?.cardTypeName, "Easter", "history response keeps card type name");
assertEqual(submissions.items[0]?.priceLabel, "$3", "history response keeps price label");
assertEqual(submissions.items[0]?.seasonYear, 2026, "history response normalizes season year");
assertEqual(submissions.items[0]?.unitPrice, 3.5, "history response normalizes unit price");
assertEqual(submissions.items[0]?.remainingQuantity, 8, "history response normalizes quantity");
assertEqual(submissions.total, 3, "history response normalizes total count");
assertEqual(submissions.pageNumber, 2, "history response normalizes page from data.page");
assertEqual(submissions.pageSize, 50, "history response normalizes page size");
