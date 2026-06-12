import type {
  PagedResult,
  SeasonalCardCatalogItem,
  SeasonalCardPriceOption,
  SeasonalCardSubmissionPayload,
  SeasonalCardSubmissionQuery,
  SeasonalCardSubmissionRecord,
  SeasonalCardType,
} from "@/modules/seasonal-cards/types";

const BASE_PATH = "/react/v1/seasonal-card-remaining";
const LIST_PAGE_SIZES = [20, 50, 100] as const;

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asString(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "string" && !value.trim()) {
    return null;
  }
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
}

function asNumber(value: unknown, fallback = 0): number {
  const parsed = asNullableNumber(value);
  return parsed == null ? fallback : parsed;
}

function asNullableInt(value: unknown): number | null {
  const parsed = asNullableNumber(value);
  return parsed == null ? null : Math.trunc(parsed);
}

function asBoolean(value: unknown) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    return normalized === "true" || normalized === "1" || normalized === "yes";
  }
  return false;
}

function trimText(value: unknown) {
  if (typeof value !== "string") {
    return undefined;
  }
  const trimmed = value.trim();
  return trimmed ? trimmed : undefined;
}

function normalizePage(value?: number) {
  return value && Number.isFinite(value) && value > 0 ? Math.trunc(value) : 1;
}

function normalizePageSize(value?: number) {
  const normalizedValue =
    value && Number.isFinite(value) ? Math.trunc(value) : undefined;
  return LIST_PAGE_SIZES.includes(
    normalizedValue as (typeof LIST_PAGE_SIZES)[number]
  )
    ? normalizedValue!
    : 20;
}

function asSeasonalCardType(value: unknown): SeasonalCardType | null {
  const parsed = asNullableInt(value);
  return parsed != null && parsed >= 1 && parsed <= 5
    ? (parsed as SeasonalCardType)
    : null;
}

function asSeasonalCardPriceOption(value: unknown): SeasonalCardPriceOption | null {
  const parsed = asNullableInt(value);
  return parsed != null && parsed >= 1 && parsed <= 4
    ? (parsed as SeasonalCardPriceOption)
    : null;
}

function unwrapPayload(payload: unknown) {
  if (Array.isArray(payload)) {
    return payload;
  }

  const root = asRecord(payload) ?? {};
  return pick(root, "data", "Data") ?? root;
}

function getArray(payload: unknown, ...keys: string[]) {
  if (Array.isArray(payload)) {
    return payload;
  }
  const root = asRecord(payload) ?? {};
  const value = pick(root, ...keys);
  return Array.isArray(value) ? value : [];
}

export function buildSeasonalCardSubmissionPayload(
  payload: SeasonalCardSubmissionPayload
) {
  const customUnitPrice = asNullableNumber(payload.customUnitPrice);
  const remark = trimText(payload.remark);

  return {
    storeCode: trimText(payload.storeCode) ?? "",
    catalogGuid: trimText(payload.catalogGuid) ?? "",
    seasonYear: asNullableInt(payload.seasonYear) ?? 0,
    remainingQuantity: asNullableInt(payload.remainingQuantity) ?? 0,
    ...(customUnitPrice == null ? {} : { customUnitPrice }),
    ...(remark ? { remark } : {}),
  };
}

export function buildSeasonalCardSubmissionQuery(
  query: SeasonalCardSubmissionQuery
) {
  return {
    storeCode: trimText(query.storeCode),
    cardType: asSeasonalCardType(query.cardType),
    seasonYear: asNullableInt(query.seasonYear),
    pageNumber: normalizePage(query.pageNumber),
    pageSize: normalizePageSize(query.pageSize),
  };
}

export function normalizeSeasonalCardCatalogItem(raw: unknown): SeasonalCardCatalogItem {
  const item = asRecord(raw) ?? {};
  return {
    catalogGuid: asString(pick(item, "catalogGuid", "CatalogGuid", "CatalogGUID")),
    cardType: asSeasonalCardType(pick(item, "cardType", "CardType")),
    cardTypeName: asString(pick(item, "cardTypeName", "CardTypeName")),
    priceOption: asSeasonalCardPriceOption(pick(item, "priceOption", "PriceOption")),
    priceOptionName: asString(pick(item, "priceOptionName", "PriceOptionName")),
    priceLabel: asString(pick(item, "priceLabel", "PriceLabel")),
    fixedUnitPrice: asNullableNumber(
      pick(item, "fixedUnitPrice", "FixedUnitPrice", "fixedPrice", "FixedPrice")
    ),
    allowsCustomUnitPrice: asBoolean(
      pick(item, "allowsCustomUnitPrice", "AllowsCustomUnitPrice")
    ),
    isEnabled: asBoolean(pick(item, "isEnabled", "IsEnabled", "isActive", "IsActive")),
    sortOrder: asNullableInt(pick(item, "sortOrder", "SortOrder")),
  };
}

export function normalizeSeasonalCardCatalogResponse(
  payload: unknown
): SeasonalCardCatalogItem[] {
  const data = unwrapPayload(payload);
  return getArray(data, "items", "Items", "catalog", "Catalog").map(
    normalizeSeasonalCardCatalogItem
  );
}

export function normalizeSeasonalCardSubmission(
  raw: unknown
): SeasonalCardSubmissionRecord {
  const item = asRecord(raw) ?? {};
  return {
    submissionGuid: asString(
      pick(item, "submissionGuid", "SubmissionGuid", "SubmissionGUID")
    ),
    storeCode: asString(pick(item, "storeCode", "StoreCode")),
    catalogGuid: asString(pick(item, "catalogGuid", "CatalogGuid", "CatalogGUID")),
    cardType: asSeasonalCardType(pick(item, "cardType", "CardType")),
    cardTypeName: asString(pick(item, "cardTypeName", "CardTypeName")),
    seasonYear: asNullableInt(pick(item, "seasonYear", "SeasonYear")),
    unitPrice: asNullableNumber(pick(item, "unitPrice", "UnitPrice")),
    priceLabel: asString(pick(item, "priceLabel", "PriceLabel")),
    remainingQuantity: asNullableInt(
      pick(item, "remainingQuantity", "RemainingQuantity")
    ),
    remark: asString(pick(item, "remark", "Remark", "remarks", "Remarks")),
    submittedByName: asString(
      pick(item, "submittedByName", "SubmittedByName", "createUser", "CreateUser")
    ),
    submittedAt: asString(
      pick(item, "submittedAt", "SubmittedAt", "createTime", "CreateTime")
    ),
  };
}

export function normalizeSeasonalCardSubmissionsResponse(
  payload: unknown
): PagedResult<SeasonalCardSubmissionRecord> {
  const data = unwrapPayload(payload);
  return {
    items: getArray(data, "items", "Items", "submissions", "Submissions").map(
      normalizeSeasonalCardSubmission
    ),
    total: asNumber(pick(data as Record<string, unknown>, "total", "Total", "totalCount", "TotalCount"), 0),
    pageNumber: asNumber(pick(data as Record<string, unknown>, "pageNumber", "PageNumber", "page", "Page"), 1),
    pageSize: asNumber(pick(data as Record<string, unknown>, "pageSize", "PageSize", "limit", "Limit"), 20),
  };
}

export function normalizeSeasonalCardSubmissionDetail(
  payload: unknown
): SeasonalCardSubmissionRecord | null {
  const data = unwrapPayload(payload);
  const detail = asRecord(
    pick(asRecord(data) ?? {}, "item", "Item", "submission", "Submission")
  );
  const record = detail ?? asRecord(data);
  if (!record) {
    return null;
  }
  return normalizeSeasonalCardSubmission(record);
}

export async function fetchSeasonalCardCatalog() {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/catalog`);
  return normalizeSeasonalCardCatalogResponse(response.data);
}

export async function submitSeasonalCardSubmission(
  payload: SeasonalCardSubmissionPayload
) {
  const client = await getApiClient();
  const response = await client.post(
    `${BASE_PATH}/submissions`,
    buildSeasonalCardSubmissionPayload(payload)
  );
  return normalizeSeasonalCardSubmissionDetail(response.data);
}

export async function fetchSeasonalCardSubmissions(
  query: SeasonalCardSubmissionQuery
) {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/submissions`, {
    params: buildSeasonalCardSubmissionQuery(query),
  });
  return normalizeSeasonalCardSubmissionsResponse(response.data);
}

export async function fetchSeasonalCardSubmissionDetail(submissionGuid: string) {
  const client = await getApiClient();
  const response = await client.get(
    `${BASE_PATH}/submissions/${encodeURIComponent(submissionGuid)}`
  );
  return normalizeSeasonalCardSubmissionDetail(response.data);
}
