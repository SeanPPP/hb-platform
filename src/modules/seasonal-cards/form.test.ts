import {
  formatSeasonalCardPriceOptionLabel,
  getSeasonalCardHistoryCardTypeOptions,
  getSeasonalCardCatalogGroups,
  getSeasonalCardLocalizedTypeLabel,
  isSeasonalCardCustomPriceOption,
  validateSeasonalCardSubmissionDraft,
} from "./form";

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

const catalogGroups = getSeasonalCardCatalogGroups([
  {
    catalogGuid: "catalog-3",
    cardType: 1,
    cardTypeName: "Christmas",
    priceOption: 4,
    priceOptionName: "Other",
    priceLabel: "其他",
    fixedUnitPrice: null,
    allowsCustomUnitPrice: true,
    isEnabled: true,
    sortOrder: 9,
  },
  {
    catalogGuid: "catalog-1",
    cardType: 1,
    cardTypeName: "Christmas",
    priceOption: 1,
    priceOptionName: "$1",
    priceLabel: "$1",
    fixedUnitPrice: 1,
    allowsCustomUnitPrice: false,
    isEnabled: true,
    sortOrder: 1,
  },
  {
    catalogGuid: "catalog-2",
    cardType: 2,
    cardTypeName: "Easter",
    priceOption: 2,
    priceOptionName: "$2",
    priceLabel: "$2",
    fixedUnitPrice: 2,
    allowsCustomUnitPrice: false,
    isEnabled: true,
    sortOrder: 2,
  },
  {
    catalogGuid: "catalog-x",
    cardType: 5,
    cardTypeName: "Inactive",
    priceOption: 3,
    priceOptionName: "$3",
    priceLabel: "$3",
    fixedUnitPrice: 3,
    allowsCustomUnitPrice: false,
    isEnabled: false,
    sortOrder: 3,
  },
]);

assertDeepEqual(
  catalogGroups.map((group) => ({
    cardType: group.cardType,
    cardTypeName: group.cardTypeName,
    catalogGuids: group.options.map((option) => option.catalogGuid),
  })),
  [
    { cardType: 1, cardTypeName: "Christmas", catalogGuids: ["catalog-1", "catalog-3"] },
    { cardType: 2, cardTypeName: "Easter", catalogGuids: ["catalog-2"] },
  ],
  "catalog groups keep enabled options grouped by numeric card type and sorted by sort order"
);

assertEqual(
  isSeasonalCardCustomPriceOption(catalogGroups[0]?.options[1] ?? null),
  true,
  "other price option requires a custom unit price"
);

assertEqual(
  isSeasonalCardCustomPriceOption(catalogGroups[0]?.options[0] ?? null),
  false,
  "fixed price option does not require a custom unit price"
);

assertEqual(
  formatSeasonalCardPriceOptionLabel(catalogGroups[0]?.options[0]!, "其他"),
  "$1",
  "fixed price options display backend price label"
);

assertEqual(
  formatSeasonalCardPriceOptionLabel(catalogGroups[0]?.options[1]!, "其他"),
  "其他",
  "custom price options display backend or local other label"
);

assertDeepEqual(
  getSeasonalCardHistoryCardTypeOptions(),
  [
    { key: "1", label: "Christmas" },
    { key: "2", label: "Valentines Day" },
    { key: "3", label: "Mother's Day" },
    { key: "4", label: "Easter" },
    { key: "5", label: "Father's Day" },
  ],
  "history card type filter exposes the fixed backend enum order and fallback labels without relying on catalog"
);

assertEqual(
  getSeasonalCardLocalizedTypeLabel(2, "情人节", (key) =>
    ({
      "cardTypes.1": "Christmas",
      "cardTypes.2": "Valentines Day",
      "cardTypes.3": "Mother's Day",
      "cardTypes.4": "Easter",
      "cardTypes.5": "Father's Day",
    })[key] ?? key
  ),
  "Valentines Day",
  "recognized card types should prefer the current locale label over backend cardTypeName"
);

assertEqual(
  getSeasonalCardLocalizedTypeLabel(null, "后端中文名称", (key) => key),
  "后端中文名称",
  "unknown card types should fall back to backend cardTypeName when available"
);

const missingFieldErrors = validateSeasonalCardSubmissionDraft(
  {
    storeCode: "",
    seasonYear: "",
    cardType: "1",
    catalogGuid: "",
    remainingQuantity: "",
    customUnitPrice: "",
    remark: "",
  },
  null
);

assertDeepEqual(
  missingFieldErrors,
  {
    storeCode: "storeCode",
    seasonYear: "seasonYear",
    catalogGuid: "catalogGuid",
    remainingQuantity: "remainingQuantity",
  },
  "validation reports required fields before a catalog option is selected"
);

const customPriceErrors = validateSeasonalCardSubmissionDraft(
  {
    storeCode: "STO01",
    seasonYear: "2026",
    cardType: "1",
    catalogGuid: "catalog-3",
    remainingQuantity: "-1",
    customUnitPrice: "0",
    remark: "ok",
  },
  catalogGroups[0]?.options[1] ?? null
);

assertDeepEqual(
  customPriceErrors,
  {
    remainingQuantity: "remainingQuantity",
    customUnitPrice: "customUnitPrice",
  },
  "validation requires a non-negative integer quantity and positive custom price for other options"
);

const validDraftErrors = validateSeasonalCardSubmissionDraft(
  {
    storeCode: "STO01",
    seasonYear: "2026",
    cardType: "1",
    catalogGuid: "catalog-1",
    remainingQuantity: "0",
    customUnitPrice: "",
    remark: "memo",
  },
  catalogGroups[0]?.options[0] ?? null
);

assertDeepEqual(validDraftErrors, {}, "validation accepts zero quantity with a fixed-price catalog option");
