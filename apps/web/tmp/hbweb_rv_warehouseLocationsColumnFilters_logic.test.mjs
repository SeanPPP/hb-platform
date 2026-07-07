// src/pages/Warehouse/Locations/columnFilters.ts
var FILTER_TOKEN_NAMESPACE = "__filter";
var SERVER_FILTER_KEYS = [
  "locationCode",
  "locationBarcode",
  "updatedBy",
  "updatedAt",
  "status",
  "locationType",
  "productItemNumber",
  "productBarcode",
  "productName"
];
function setFilterValues(filters, key, values) {
  const normalizedValues = (values ?? []).map((value) => value === void 0 || value === null ? "" : String(value).trim()).filter(Boolean);
  if (!normalizedValues.length) {
    if (!(key in filters)) {
      return filters;
    }
    const nextFilters = { ...filters };
    delete nextFilters[key];
    return nextFilters;
  }
  return {
    ...filters,
    [key]: normalizedValues
  };
}
function buildModeToken(mode, value) {
  const normalizedValue = value === void 0 || value === null ? "" : String(value).trim();
  return normalizedValue ? `${FILTER_TOKEN_NAMESPACE}:${mode}:${normalizedValue}` : void 0;
}
function buildTextFilterTokens(mode, value) {
  const token = buildModeToken(mode, value);
  return token ? [token] : [];
}
function buildRangeFilterTokens(min, max) {
  const tokens = [];
  if (min !== void 0 && min !== null && String(min).trim()) {
    tokens.push(`gte:${String(min).trim()}`);
  }
  if (max !== void 0 && max !== null && String(max).trim()) {
    tokens.push(`lte:${String(max).trim()}`);
  }
  return tokens;
}
function buildComparableFilterTokens(mode, values) {
  if (mode === "range") {
    return buildRangeFilterTokens(values.min, values.max);
  }
  if (mode === "gte") {
    return buildRangeFilterTokens(values.value, void 0);
  }
  if (mode === "lte") {
    return buildRangeFilterTokens(void 0, values.value);
  }
  const token = buildModeToken(mode, values.value);
  return token ? [token] : [];
}
function normalizeLocationTableFilters(filters) {
  const filterKeyMap = {
    itemNumbers: "productItemNumber",
    productBarcodes: "productBarcode",
    productNames: "productName"
  };
  return Object.entries(filters).reduce((current, [key, value]) => {
    if (!value?.length) {
      return current;
    }
    const mappedFilterKey = filterKeyMap[key] ?? key;
    return setFilterValues(current, mappedFilterKey, value.map((item) => String(item).trim()));
  }, {});
}
function getSingleFilterValue(values) {
  return values?.length === 1 ? values[0] : void 0;
}
function parseBooleanFilter(values) {
  const value = getSingleFilterValue(values);
  if (value === "true") {
    return true;
  }
  if (value === "false") {
    return false;
  }
  return void 0;
}
function buildLocationFilterQuery(columnFilters) {
  const nestedFilters = SERVER_FILTER_KEYS.reduce((current, key) => {
    const values = columnFilters[key];
    if (values?.length) {
      current[key] = values;
    }
    return current;
  }, {});
  const query = {
    isUsed: parseBooleanFilter(columnFilters.usage),
    filters: Object.keys(nestedFilters).length ? nestedFilters : void 0
  };
  return query;
}

// src/pages/Warehouse/Locations/warehouseLocationsColumnFilters.logic.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
async function main() {
  const failures = [];
  const mappingFailure = await runTest("\u5217\u5934\u7B5B\u9009\u5E94\u6309\u63A5\u53E3\u5B57\u6BB5\u5206\u6D41", () => {
    const query = buildLocationFilterQuery({
      locationCode: buildTextFilterTokens("contains", "A-01"),
      locationType: ["1"],
      locationBarcode: buildTextFilterTokens("eq", "B-02"),
      status: ["0"],
      usage: ["true"],
      productItemNumber: buildTextFilterTokens("starts", "HB"),
      productBarcode: buildTextFilterTokens("contains", "9300"),
      productName: buildTextFilterTokens("ends", "Cream"),
      updatedAt: buildComparableFilterTokens("range", { min: "2026-07-01", max: "2026-07-02" }),
      updatedBy: buildTextFilterTokens("contains", "Sean")
    });
    assert(query.filters?.locationCode?.[0]?.includes("A-01"), "locationCode \u5E94\u4FDD\u7559 token \u8FDB\u5165 filters");
    assert(query.filters?.locationType?.[0] === "1", "locationType \u5E94\u8FDB\u5165 filters");
    assert(query.filters?.locationBarcode?.[0]?.includes("B-02"), "locationBarcode \u5E94\u4FDD\u7559 token \u8FDB\u5165 filters");
    assert(query.filters?.status?.[0] === "0", "status \u5E94\u8FDB\u5165 filters");
    assert(query.isUsed === true, "usage \u5E94\u6620\u5C04\u4E3A\u9876\u5C42 isUsed");
    assert(query.filters?.updatedBy?.[0]?.includes("Sean"), "updatedBy \u5E94\u4FDD\u7559 token \u8FDB\u5165 filters");
    assert(query.filters?.productItemNumber?.[0]?.includes("starts"), "\u5546\u54C1\u8D27\u53F7\u5E94\u4F7F\u7528 filters.productItemNumber");
    assert(query.filters?.productBarcode?.[0]?.includes("9300"), "\u5546\u54C1\u6761\u7801\u5E94\u4F7F\u7528 filters.productBarcode");
    assert(query.filters?.productName?.[0]?.includes("Cream"), "\u5546\u54C1\u540D\u79F0\u5E94\u4F7F\u7528 filters.productName");
    assert(query.filters?.updatedAt?.[0] === "gte:2026-07-01", "\u66F4\u65B0\u65F6\u95F4\u8D77\u59CB\u5E94\u4FDD\u7559\u8303\u56F4 token");
    assert(!query.filters?.usage, "usage \u4E0D\u80FD\u91CD\u590D\u8FDB\u5165 filters");
  });
  if (mappingFailure) failures.push(mappingFailure);
  const storageLocationFailure = await runTest("\u5B58\u8D27\u4F4D\u5217\u7B5B\u9009\u5E94\u6309\u540E\u7AEF\u7EA6\u5B9A\u53D1\u9001 2", () => {
    const query = buildLocationFilterQuery({
      locationType: ["2"]
    });
    assert(query.filters?.locationType?.[0] === "2", "\u5B58\u8D27\u4F4D\u7B5B\u9009\u5FC5\u987B\u53D1\u9001 LocationType=2");
  });
  if (storageLocationFailure) failures.push(storageLocationFailure);
  const normalizeFailure = await runTest("AntD \u8868\u683C filters \u5E94\u89C4\u8303\u6210\u5217\u8FC7\u6EE4\u72B6\u6001", () => {
    const filters = normalizeLocationTableFilters({
      locationCode: ["  A-01  "],
      itemNumbers: ["HB-1"],
      productName: null
    });
    assert(filters.locationCode?.[0] === "A-01", "\u6587\u672C\u503C\u5E94 trim \u540E\u4FDD\u7559");
    assert(filters.productItemNumber?.[0] === "HB-1", "\u5546\u54C1\u8D27\u53F7\u5217 key \u5E94\u6620\u5C04\u4E3A\u63A5\u53E3 filters.productItemNumber");
    assert(!filters.productName, "\u7A7A filters \u5E94\u88AB\u5FFD\u7565\uFF0C\u4FBF\u4E8E\u91CD\u7F6E\u6E05\u7A7A");
  });
  if (normalizeFailure) failures.push(normalizeFailure);
  const resetFailure = await runTest("\u7A7A\u5217\u8FC7\u6EE4\u4E0D\u5E94\u751F\u6210 filters \u5BF9\u8C61", () => {
    const query = buildLocationFilterQuery({});
    assert(query.filters === void 0, "\u91CD\u7F6E\u540E\u8BF7\u6C42\u4E0D\u5E94\u7EE7\u7EED\u643A\u5E26\u65E7 filters");
    assert(query.isUsed === void 0 && query.status === void 0, "\u91CD\u7F6E\u540E\u9876\u5C42\u7B5B\u9009\u5E94\u4E3A\u7A7A");
  });
  if (resetFailure) failures.push(resetFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("warehouseLocationsColumnFilters.logic.test: ok");
}
await main();
