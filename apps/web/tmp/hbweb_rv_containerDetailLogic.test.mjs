// src/pages/Warehouse/ContainerDetail/containerDetailLogic.test.ts
import { readFileSync } from "node:fs";

// src/pages/Warehouse/Products/categoryPath.ts
function formatCategoryNodeName(node) {
  const name = node.categoryName.trim();
  const chineseName = node.chineseName?.trim();
  return [name, chineseName].filter(Boolean).join(" / ");
}
function getWarehouseCategoryDisplayLanguage(language) {
  return language?.toLowerCase().startsWith("en") ? "en" : "zh";
}
function formatWarehouseCategoryNodeName(node, language) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language);
  const name = node.categoryName.trim();
  const chineseName = node.chineseName?.trim();
  return displayLanguage === "en" ? name || chineseName || "" : chineseName || name || "";
}
function localizeCategoryPathText(path, language) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language);
  return path.split(">").map((part) => {
    const candidates = part.split("/").map((item) => item.trim()).filter(Boolean);
    if (!candidates.length) return "";
    const preferred = displayLanguage === "zh" ? candidates.find((item) => /[\u3400-\u9fff]/.test(item)) : candidates.find((item) => !/[\u3400-\u9fff]/.test(item));
    return preferred || candidates[0];
  }).filter(Boolean).join(" > ");
}
function addUniquePath(paths, path) {
  if (!path) {
    return [path].filter(Boolean);
  }
  if (!paths) {
    return [path];
  }
  return paths.includes(path) ? paths : [...paths, path];
}
function addLocalizedNamePath(lookup, name, fullPath, localizedFullPath) {
  if (!name || !fullPath) return;
  lookup.byName.set(name, addUniquePath(lookup.byName.get(name), fullPath));
  lookup.displayByName.set(name, {
    zh: addUniquePath(lookup.displayByName.get(name)?.zh, localizedFullPath.zh),
    en: addUniquePath(lookup.displayByName.get(name)?.en, localizedFullPath.en)
  });
}
function buildWarehouseCategoryLookup(nodes, parentPath = [], displayParentPath = { zh: [], en: [] }, lookup = {
  byGuid: /* @__PURE__ */ new Map(),
  byName: /* @__PURE__ */ new Map(),
  displayByGuid: /* @__PURE__ */ new Map(),
  displayByName: /* @__PURE__ */ new Map(),
  descendantGuidsByGuid: /* @__PURE__ */ new Map()
}) {
  for (const node of nodes) {
    const displayName = formatCategoryNodeName(node);
    const currentPath = [...parentPath, displayName].filter(Boolean);
    const fullPath = currentPath.join(" > ");
    const displayPath = {
      zh: [...displayParentPath.zh, formatWarehouseCategoryNodeName(node, "zh")].filter(Boolean),
      en: [...displayParentPath.en, formatWarehouseCategoryNodeName(node, "en")].filter(Boolean)
    };
    const localizedFullPath = {
      zh: displayPath.zh.join(" > "),
      en: displayPath.en.join(" > ")
    };
    const descendantGuids = /* @__PURE__ */ new Set();
    if (node.categoryGUID && fullPath) {
      lookup.byGuid.set(node.categoryGUID, fullPath);
      lookup.displayByGuid.set(node.categoryGUID, localizedFullPath);
      descendantGuids.add(node.categoryGUID);
    }
    addLocalizedNamePath(lookup, node.categoryName, fullPath, localizedFullPath);
    addLocalizedNamePath(lookup, node.chineseName, fullPath, localizedFullPath);
    buildWarehouseCategoryLookup(node.children || [], currentPath, displayPath, lookup);
    for (const child of node.children || []) {
      const childDescendantGuids = lookup.descendantGuidsByGuid.get(child.categoryGUID);
      childDescendantGuids?.forEach((guid) => descendantGuids.add(guid));
    }
    if (node.categoryGUID) {
      lookup.descendantGuidsByGuid.set(node.categoryGUID, descendantGuids);
    }
  }
  return lookup;
}
function getWarehouseProductCategoryTooltip(record, lookup, language) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language);
  if (record.warehouseCategoryGUID) {
    const pathByGuid = lookup.displayByGuid.get(record.warehouseCategoryGUID)?.[displayLanguage];
    if (pathByGuid) {
      return pathByGuid;
    }
  }
  const name = record.categoryName?.trim();
  if (!name) {
    return record.categoryPath ? localizeCategoryPathText(record.categoryPath, displayLanguage) : void 0;
  }
  const pathsByName = lookup.byName.get(name);
  const displayPathsByName = lookup.displayByName.get(name)?.[displayLanguage];
  if (displayPathsByName?.length) {
    return displayPathsByName.join("\uFF1B");
  }
  if (record.categoryPath) {
    return localizeCategoryPathText(record.categoryPath, displayLanguage);
  }
  if (pathsByName?.length) {
    return pathsByName.map((path) => localizeCategoryPathText(path, displayLanguage)).join("\uFF1B");
  }
  return name;
}

// src/pages/Warehouse/ContainerDetail/containerDetailLogic.ts
var CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY = "__ALL_CONTAINER_DETAIL_CATEGORIES__";
var CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY = "__UNCATEGORIZED_CONTAINER_DETAIL_CATEGORIES__";
var DEFAULT_CONTAINER_DETAIL_FLOAT_RATE = 1.3;
function calculateContainerDetailTableScrollY({
  viewportHeight,
  toolbarHeight,
  tableChromeHeight,
  isSmallLandscape,
  isSmallPortrait,
  maxScrollY
}) {
  const safeViewportHeight = Number.isFinite(viewportHeight) ? viewportHeight : maxScrollY;
  const stableContentTop = isSmallLandscape ? 88 : isSmallPortrait ? 72 : 150;
  const safeToolbarHeight = Math.max(0, Number.isFinite(toolbarHeight) ? toolbarHeight : 0);
  const safeTableChromeHeight = Math.max(0, Number.isFinite(tableChromeHeight) ? tableChromeHeight : 0);
  const bottomInset = isSmallLandscape ? 12 : isSmallPortrait ? 88 : 24;
  const contentGap = isSmallLandscape ? 8 : isSmallPortrait ? 10 : 12;
  const hardMinScrollY = isSmallLandscape ? 96 : isSmallPortrait ? 112 : 220;
  const availableHeight = safeViewportHeight - stableContentTop - bottomInset - safeToolbarHeight - contentGap - safeTableChromeHeight;
  return Math.max(hardMinScrollY, Math.min(maxScrollY, availableHeight));
}
function getUpdateFieldSelectionState(selectedFields, allFields) {
  const fieldSet = new Set(allFields);
  const selectedCount = selectedFields.filter((field) => fieldSet.has(field)).length;
  return {
    isAllSelected: allFields.length > 0 && selectedCount === allFields.length,
    isPartiallySelected: selectedCount > 0 && selectedCount < allFields.length
  };
}
function getNextUpdateFieldSelection(checked, allFields) {
  return checked ? [...allFields] : [];
}
var DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS = [
  "index",
  "itemNumber",
  "productName",
  "englishName",
  "containerPieces",
  "containerQuantity",
  "unitVolume",
  "totalVolume",
  "middlePackQuantity",
  "domesticPrice",
  "oemPrice"
];
var DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS = [
  "index",
  "productImage",
  "itemNumber",
  "barcodeImage",
  "englishName",
  "oemPrice"
];
var CONTAINER_DETAIL_EXPORT_COLUMNS = [
  { key: "index", labelKey: "containers.export.indexColumn", fallbackLabel: "\u5E8F\u53F7", width: 8, valueType: "integer" },
  { key: "itemNumber", labelKey: "containers.fields.itemNumber", fallbackLabel: "\u8D27\u53F7", width: 18, valueType: "text" },
  { key: "barcode", labelKey: "containers.fields.barcode", fallbackLabel: "\u6761\u7801", width: 20, valueType: "text" },
  { key: "barcodeImage", labelKey: "containers.export.barcodeImageColumn", fallbackLabel: "\u6761\u7801\u56FE\u7247", width: 24, valueType: "text" },
  { key: "productImage", labelKey: "containers.export.productImageColumn", fallbackLabel: "\u5546\u54C1\u56FE\u7247", width: 18, valueType: "text" },
  { key: "productName", labelKey: "containers.export.chineseNameColumn", fallbackLabel: "\u4E2D\u6587\u540D\u79F0", width: 36, valueType: "text" },
  { key: "englishName", labelKey: "containers.fields.englishName", fallbackLabel: "\u82F1\u6587\u540D\u79F0", width: 36, valueType: "text" },
  { key: "containerPieces", labelKey: "containers.export.piecesColumn", fallbackLabel: "\u4EF6\u6570", width: 12, valueType: "integer" },
  { key: "containerQuantity", labelKey: "containers.export.totalQuantityColumn", fallbackLabel: "\u603B\u88C5\u67DC\u6570", width: 12, valueType: "integer" },
  { key: "unitVolume", labelKey: "containers.export.unitVolumeColumn", fallbackLabel: "\u5355\u4EF6\u4F53\u79EF", width: 12, valueType: "volume" },
  { key: "totalVolume", labelKey: "containers.export.totalVolumeColumn", fallbackLabel: "\u603B\u4F53\u79EF", width: 12, valueType: "volume" },
  { key: "middlePackQuantity", labelKey: "containers.fields.middlePackQuantity", fallbackLabel: "\u4E2D\u5305\u6570", width: 12, valueType: "integer" },
  { key: "domesticPrice", labelKey: "containers.fields.domesticPrice", fallbackLabel: "\u56FD\u5185\u4EF7\u683C", width: 12, valueType: "money" },
  { key: "lastImportPrice", labelKey: "containers.fields.warehouseImportPrice", fallbackLabel: "\u5B9E\u65F6\u8FDB\u8D27\u4EF7", width: 14, valueType: "money" },
  { key: "lastOEMPrice", labelKey: "containers.fields.lastOEMPrice", fallbackLabel: "\u5B9E\u65F6\u96F6\u552E\u4EF7", width: 14, valueType: "money" },
  { key: "oemPrice", labelKey: "containers.fields.oemPrice", fallbackLabel: "\u96F6\u552E\u4EF7", width: 12, valueType: "money" }
];
var containerDetailSortFields = /* @__PURE__ */ new Set([
  "itemNumber",
  "barcode",
  "productName",
  "englishName",
  "productType",
  "newProduct",
  "matchType",
  "containerPieces",
  "middlePackQuantity",
  "containerQuantity",
  "packingQuantity",
  "unitVolume",
  "domesticPrice",
  "floatRate",
  "transportCost",
  "unitTransportCost",
  "warehouseImportPrice",
  "lastOEMPrice",
  "importPrice",
  "oemPrice",
  "warehouseStatus",
  "remark"
]);
var chineseTextPattern = /[\u4e00-\u9fff]/;
function containsChineseText(value) {
  return Boolean(value && chineseTextPattern.test(value));
}
function isValidContainerDetailEnglishTranslation(value) {
  return Boolean(value?.trim()) && !containsChineseText(value);
}
function isContainerDetailSortField(value) {
  return typeof value === "string" && containerDetailSortFields.has(value);
}
function mergeContainerDetailColumnOrder(savedOrder, availableOrder) {
  const availableSet = new Set(availableOrder);
  const seen = /* @__PURE__ */ new Set();
  const merged = [];
  for (const value of savedOrder ?? []) {
    if (typeof value !== "string" || !availableSet.has(value)) {
      continue;
    }
    const key = value;
    if (seen.has(key)) {
      continue;
    }
    seen.add(key);
    merged.push(key);
  }
  for (const key of availableOrder) {
    if (!seen.has(key)) {
      merged.push(key);
    }
  }
  return merged;
}
function moveContainerDetailColumnOrder(currentOrder, activeKey, overKey) {
  if (typeof activeKey !== "string" || typeof overKey !== "string" || activeKey === overKey) {
    return [...currentOrder];
  }
  const fromIndex = currentOrder.indexOf(activeKey);
  const toIndex = currentOrder.indexOf(overKey);
  if (fromIndex < 0 || toIndex < 0) {
    return [...currentOrder];
  }
  const nextOrder = [...currentOrder];
  const [moved] = nextOrder.splice(fromIndex, 1);
  nextOrder.splice(toIndex, 0, moved);
  return nextOrder;
}
function isContainerDetailColumnOrderCustomized(currentOrder, defaultOrder) {
  if (!currentOrder.length) {
    return false;
  }
  if (currentOrder.length !== defaultOrder.length) {
    return true;
  }
  return currentOrder.some((key, index) => key !== defaultOrder[index]);
}
function getContainerDetailEditableColumnKeysInOrder(currentColumnOrder, editableColumnKeys2) {
  const editableColumnKeySet = new Set(editableColumnKeys2);
  return currentColumnOrder.filter((key) => editableColumnKeySet.has(key));
}
function getNextContainerDetailEditableCell(currentRowKey, currentColumnKey, rowKeys, columnKeys, direction) {
  const rowIndex = rowKeys.indexOf(currentRowKey);
  const columnIndex = columnKeys.indexOf(currentColumnKey);
  if (rowIndex < 0 || columnIndex < 0) {
    return null;
  }
  const nextRowIndex = direction === "up" ? rowIndex - 1 : direction === "down" ? rowIndex + 1 : rowIndex;
  const nextColumnIndex = direction === "left" ? columnIndex - 1 : direction === "right" ? columnIndex + 1 : columnIndex;
  const nextRowKey = rowKeys[nextRowIndex];
  const nextColumnKey = columnKeys[nextColumnIndex];
  if (!nextRowKey || !nextColumnKey) {
    return null;
  }
  return {
    rowKey: nextRowKey,
    columnKey: nextColumnKey
  };
}
function getContainerDetailProductName(row) {
  return row.\u5546\u54C1\u540D\u79F0 ?? row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u540D\u79F0;
}
function getContainerDetailImageUrl(row) {
  return row.\u5546\u54C1\u56FE\u7247?.trim() || row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u56FE\u7247?.trim() || void 0;
}
function getContainerDetailCreateProductRowLabel(row) {
  return getContainerDetailItemNumber(row) ?? getContainerDetailProductCode(row) ?? row.hguid;
}
function findContainerDetailRowsMissingProductName(rows2) {
  return rows2.filter((row) => row.\u662F\u5426\u65B0\u5546\u54C1).map((row) => {
    const productName = getContainerDetailProductName(row)?.trim() ?? "";
    return {
      hguid: row.hguid,
      label: getContainerDetailCreateProductRowLabel(row),
      productName
    };
  }).filter((row) => !row.productName).map(({ hguid, label, productName }) => ({ hguid, label, productName }));
}
function findContainerDetailRowsMissingCreateProductRetailPrice(rows2) {
  return rows2.filter((row) => row.\u662F\u5426\u65B0\u5546\u54C1).map((row) => {
    const retailPrice = resolveContainerDetailOemPrice(row);
    return {
      hguid: row.hguid,
      label: getContainerDetailCreateProductRowLabel(row),
      retailPrice
    };
  }).filter((row) => !(typeof row.retailPrice === "number" && Number.isFinite(row.retailPrice) && row.retailPrice > 0));
}
function getContainerDetailEnglishName(row) {
  return row.\u82F1\u6587\u540D\u79F0 ?? row.\u5546\u54C1\u4FE1\u606F?.\u82F1\u6587\u540D\u79F0;
}
function getContainerDetailTranslationSource(row) {
  const englishName = getContainerDetailEnglishName(row);
  if (containsChineseText(englishName)) return englishName;
  return getContainerDetailProductName(row);
}
function getContainerDetailItemNumber(row) {
  return row.\u5546\u54C1\u4FE1\u606F?.\u8D27\u53F7?.trim() || void 0;
}
function getContainerDetailBarcode(row) {
  return row.\u5546\u54C1\u4FE1\u606F?.\u6761\u5F62\u7801?.trim() || void 0;
}
function getContainerDetailProductCode(row) {
  return row.\u5546\u54C1\u7F16\u7801?.trim() || row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u7F16\u7801?.trim() || void 0;
}
function firstTrimmedValue(...values) {
  return values.map((value) => value?.trim()).find((value) => Boolean(value));
}
function getContainerDetailLocalProductCode(row) {
  return firstTrimmedValue(row.localProductCode, row.LocalProductCode);
}
function getContainerDetailDomesticProductCode(row) {
  return firstTrimmedValue(row.domesticProductCode, row.DomesticProductCode, getContainerDetailProductCode(row));
}
function hasContainerDetailProductCodeConflict(row) {
  const explicit = row.hasProductCodeConflict ?? row.HasProductCodeConflict;
  if (explicit != null) return Boolean(explicit);
  const localProductCode = normalizeMatchKey(getContainerDetailLocalProductCode(row));
  const domesticProductCode = normalizeMatchKey(getContainerDetailDomesticProductCode(row));
  return Boolean(localProductCode && domesticProductCode && localProductCode !== domesticProductCode);
}
function getContainerDetailCategoryName(row) {
  return firstTrimmedValue(
    row.categoryName,
    row.CategoryName,
    row.productCategoryName,
    row.ProductCategoryName,
    row.\u5546\u54C1\u4FE1\u606F?.categoryName,
    row.\u5546\u54C1\u4FE1\u606F?.CategoryName,
    row.\u5546\u54C1\u4FE1\u606F?.productCategoryName,
    row.\u5546\u54C1\u4FE1\u606F?.ProductCategoryName
  );
}
function getContainerDetailCategoryPath(row) {
  return firstTrimmedValue(
    row.categoryPath,
    row.CategoryPath,
    row.categoryFullPath,
    row.CategoryFullPath,
    row.\u5546\u54C1\u4FE1\u606F?.categoryPath,
    row.\u5546\u54C1\u4FE1\u606F?.CategoryPath,
    row.\u5546\u54C1\u4FE1\u606F?.categoryFullPath,
    row.\u5546\u54C1\u4FE1\u606F?.CategoryFullPath
  );
}
function getContainerDetailCategoryGuid(row) {
  return firstTrimmedValue(
    row.warehouseCategoryGUID,
    row.WarehouseCategoryGUID,
    row.productCategoryGUID,
    row.ProductCategoryGUID,
    row.\u5546\u54C1\u4FE1\u606F?.warehouseCategoryGUID,
    row.\u5546\u54C1\u4FE1\u606F?.WarehouseCategoryGUID,
    row.\u5546\u54C1\u4FE1\u606F?.productCategoryGUID,
    row.\u5546\u54C1\u4FE1\u606F?.ProductCategoryGUID
  );
}
function getContainerDetailCategoryTooltipRecord(row) {
  return {
    categoryName: getContainerDetailCategoryName(row),
    categoryPath: getContainerDetailCategoryPath(row),
    warehouseCategoryGUID: getContainerDetailCategoryGuid(row)
  };
}
function isContainerDetailCategoryNameInSelectedSubtree(categoryName, selectedCategoryGuid, lookup) {
  if (!categoryName || !lookup) {
    return false;
  }
  const selectedPath = lookup.byGuid.get(selectedCategoryGuid);
  if (!selectedPath) {
    return false;
  }
  const pathsByName = lookup.byName.get(categoryName);
  return pathsByName?.some((path) => path === selectedPath || path.startsWith(`${selectedPath} > `)) ?? false;
}
function matchesContainerDetailCategoryFilter(row, categoryFilterValue, lookup) {
  if (!categoryFilterValue || categoryFilterValue === CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY) {
    return true;
  }
  const categoryGuid = getContainerDetailCategoryGuid(row);
  const categoryName = getContainerDetailCategoryName(row);
  if (categoryFilterValue === CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY) {
    return !categoryGuid && !categoryName;
  }
  const allowedCategoryGuids = lookup?.descendantGuidsByGuid.get(categoryFilterValue);
  if (categoryGuid) {
    return allowedCategoryGuids ? allowedCategoryGuids.has(categoryGuid) : categoryGuid === categoryFilterValue;
  }
  return isContainerDetailCategoryNameInSelectedSubtree(categoryName, categoryFilterValue, lookup);
}
function applyContainerDetailCategoryFilter(rows2, categoryFilterValue, lookup) {
  return rows2.filter((row) => matchesContainerDetailCategoryFilter(row, categoryFilterValue, lookup));
}
function getContainerDetailBatchCategoryProductCodes(rows2) {
  const productCodes = [];
  const seen = /* @__PURE__ */ new Set();
  let skippedMissingCodeCount = 0;
  for (const row of rows2) {
    const productCode = getContainerDetailProductCode(row);
    if (!productCode) {
      skippedMissingCodeCount += 1;
      continue;
    }
    if (seen.has(productCode)) {
      continue;
    }
    seen.add(productCode);
    productCodes.push(productCode);
  }
  return { productCodes, skippedMissingCodeCount };
}
function getContainerDetailMatchType(row) {
  if (hasContainerDetailProductCodeConflict(row)) {
    return "supplierItem";
  }
  const raw = row.matchType ?? row.MatchType;
  const normalized = raw?.trim().toLowerCase();
  if (normalized === "productcode" || normalized === "product_code" || normalized === "\u5546\u54C1\u7F16\u7801") {
    return "productCode";
  }
  if (normalized === "supplieritem" || normalized === "supplier_item" || normalized === "item_number" || normalized === "itemnumber" || normalized === "\u4F9B\u5E94\u5546\u7F16\u7801+\u8D27\u53F7" || normalized === "\u8D27\u53F7\u5339\u914D") return "supplierItem";
  if (normalized === "unmatched" || normalized === "\u672A\u5339\u914D") return "unmatched";
  return "unmatched";
}
function getContainerDetailProductType(row) {
  return row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u7C7B\u578B || row.\u5546\u54C1\u7C7B\u578B || "\u666E\u901A\u5546\u54C1";
}
function getContainerDetailProductTypeFilterKey(row) {
  const type = getContainerDetailProductType(row);
  if (type === "\u5957\u88C5\u5546\u54C1") return "set";
  if (type === "\u591A\u7801\u5546\u54C1") return "multi";
  if (type === "\u5957\u88C5\u5B50\u5546\u54C1") return "setChild";
  return "normal";
}
function getContainerDetailWarehouseStatusFilterKey(row) {
  return row.warehouseIsActive === true ? "active" : "inactive";
}
function resolveContainerDetailOemPrice(row) {
  return row.\u8D34\u724C\u4EF7\u683C;
}
function getContainerDetailReadonlyOemPrice(row) {
  return row.readonlyOemPrice ?? row.ReadonlyOemPrice;
}
function getContainerDetailOemPriceSource(row) {
  return row.\u8D34\u724C\u4EF7\u683C == null ? "none" : "detail";
}
function getContainerDetailRealtimeImportPrice(row) {
  return row.warehouseImportPrice ?? row.WarehouseImportPrice;
}
function getContainerDetailImportPriceTrend(row) {
  const realtimeImportPrice = getContainerDetailRealtimeImportPrice(row);
  const currentImportPrice = row.\u8FDB\u53E3\u4EF7\u683C;
  if (typeof realtimeImportPrice !== "number" || typeof currentImportPrice !== "number" || !Number.isFinite(realtimeImportPrice) || !Number.isFinite(currentImportPrice) || realtimeImportPrice === currentImportPrice) {
    return void 0;
  }
  return currentImportPrice > realtimeImportPrice ? "up" : "down";
}
function getContainerDetailRealtimeRetailPrice(row) {
  return row.warehouseOEMPrice ?? row.WarehouseOEMPrice;
}
function getContainerDetailVisibleOemPrice(row) {
  return row.\u662F\u5426\u65B0\u5546\u54C1 ? resolveContainerDetailOemPrice(row) : getContainerDetailRealtimeRetailPrice(row);
}
function calculateContainerDetailUnitTransportCost(row) {
  if (row.\u8FD0\u8F93\u6210\u672C == null || row.\u5355\u4EF6\u88C5\u7BB1\u6570 == null) return void 0;
  return roundToDigits(row.\u8FD0\u8F93\u6210\u672C * row.\u5355\u4EF6\u88C5\u7BB1\u6570, 2);
}
function getContainerDetailExportColumns(selectedKeys = DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS) {
  const columnMap = new Map(CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => [column.key, column]));
  const seen = /* @__PURE__ */ new Set();
  const columns = [];
  for (const key of selectedKeys) {
    if (seen.has(key)) continue;
    const column = columnMap.get(key);
    if (!column) continue;
    seen.add(key);
    columns.push(column);
  }
  return columns;
}
function getContainerDetailUnitVolume(row) {
  return row.\u5355\u4EF6\u4F53\u79EF ?? row.\u5546\u54C1\u4FE1\u606F?.\u5355\u4EF6\u4F53\u79EF ?? 0;
}
function getContainerDetailTotalVolume(row) {
  const unitVolume = getContainerDetailUnitVolume(row);
  return row.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF ?? (row.\u88C5\u67DC\u4EF6\u6570 ?? 0) * unitVolume;
}
function buildContainerDetailExportRow(row, index = 0) {
  return {
    index: index + 1,
    itemNumber: getContainerDetailItemNumber(row) ?? "",
    barcode: getContainerDetailBarcode(row) ?? "",
    barcodeImage: getContainerDetailBarcode(row) ?? "",
    productImage: getContainerDetailImageUrl(row) ?? "",
    productName: getContainerDetailProductName(row) ?? "",
    englishName: getContainerDetailEnglishName(row) ?? "",
    containerPieces: row.\u88C5\u67DC\u4EF6\u6570 ?? 0,
    containerQuantity: row.\u88C5\u67DC\u6570\u91CF ?? 0,
    unitVolume: getContainerDetailUnitVolume(row),
    totalVolume: getContainerDetailTotalVolume(row),
    middlePackQuantity: row.\u4E2D\u5305\u6570 ?? 0,
    domesticPrice: row.\u56FD\u5185\u4EF7\u683C ?? 0,
    lastImportPrice: getContainerDetailRealtimeImportPrice(row) ?? 0,
    lastOEMPrice: getContainerDetailRealtimeRetailPrice(row) ?? 0,
    oemPrice: getContainerDetailVisibleOemPrice(row) ?? 0
  };
}
function buildContainerDetailExportRows(rows2) {
  return rows2.map(buildContainerDetailExportRow);
}
function withContainerDetailEnglishName(row, englishName) {
  return {
    ...row,
    \u82F1\u6587\u540D\u79F0: englishName,
    \u5546\u54C1\u4FE1\u606F: row.\u5546\u54C1\u4FE1\u606F ? { ...row.\u5546\u54C1\u4FE1\u606F, \u82F1\u6587\u540D\u79F0: englishName } : row.\u5546\u54C1\u4FE1\u606F
  };
}
function mergeContainerDetailPatch(row, patch) {
  const next = { ...row, ...patch };
  const productInfoPatch = {};
  if ("\u82F1\u6587\u540D\u79F0" in patch) {
    productInfoPatch.\u82F1\u6587\u540D\u79F0 = patch.\u82F1\u6587\u540D\u79F0;
  }
  if ("\u5546\u54C1\u540D\u79F0" in patch) {
    productInfoPatch.\u5546\u54C1\u540D\u79F0 = patch.\u5546\u54C1\u540D\u79F0;
  }
  if ("\u5355\u4EF6\u88C5\u7BB1\u6570" in patch) {
    productInfoPatch.\u5355\u4EF6\u88C5\u7BB1\u6570 = patch.\u5355\u4EF6\u88C5\u7BB1\u6570;
  }
  if ("\u5355\u4EF6\u4F53\u79EF" in patch) {
    productInfoPatch.\u5355\u4EF6\u4F53\u79EF = patch.\u5355\u4EF6\u4F53\u79EF;
  }
  if (Object.keys(productInfoPatch).length > 0 && next.\u5546\u54C1\u4FE1\u606F) {
    return { ...next, \u5546\u54C1\u4FE1\u606F: { ...next.\u5546\u54C1\u4FE1\u606F, ...productInfoPatch } };
  }
  return next;
}
function buildContainerDetailSaveFailureKeys(rowKey, patch) {
  const fields = Object.keys(patch).filter((key) => key !== "hguid").sort();
  if (!fields.length) {
    return [`${rowKey}:__row__`];
  }
  return fields.map((field) => `${rowKey}:${field}`);
}
function matchesContainerDetailTagFilter(row, filter) {
  if (filter === "new") return Boolean(row.\u662F\u5426\u65B0\u5546\u54C1);
  if (filter === "existing") return !row.\u662F\u5426\u65B0\u5546\u54C1;
  if (isContainerDetailProductTypeTag(filter)) {
    return getContainerDetailProductTypeFilterKey(row) === filter;
  }
  if (filter === "noOemPrice") {
    const oemPrice = resolveContainerDetailOemPrice(row);
    return Boolean(row.\u662F\u5426\u65B0\u5546\u54C1) && (!oemPrice || oemPrice <= 0);
  }
  if (filter === "abnormalImport") return !row.\u8FDB\u53E3\u4EF7\u683C || row.\u8FDB\u53E3\u4EF7\u683C <= 0;
  if (filter === "active") return row.warehouseIsActive === true;
  if (filter === "inactive") return row.warehouseIsActive !== true;
  return true;
}
var containerDetailTagFilterGroups = [
  ["new", "existing"],
  ["normal", "set", "multi", "setChild"],
  ["noOemPrice", "abnormalImport"],
  ["active", "inactive"]
];
var containerDetailProductTypeTags = ["normal", "set", "multi", "setChild"];
function isContainerDetailProductTypeTag(tag) {
  return containerDetailProductTypeTags.includes(tag);
}
function matchesContainerDetailSelectedTags(row, selectedTags) {
  const selected = selectedTags.filter((tag) => tag !== "all");
  if (!selected.length) return true;
  return containerDetailTagFilterGroups.every((group) => {
    const selectedInGroup = group.filter((tag) => selected.includes(tag));
    if (!selectedInGroup.length) return true;
    return selectedInGroup.some((tag) => matchesContainerDetailTagFilter(row, tag));
  });
}
function canUseContainerDetailLocalTagFilters({
  loadedQueryKey,
  baseQueryKey,
  loadedRowsLength,
  itemsTotal,
  hasMore,
  loading,
  loadingMore
}) {
  return loadedQueryKey === baseQueryKey && !hasMore && !loading && !loadingMore && loadedRowsLength >= itemsTotal;
}
function buildContainerDetailTagStats(rows2) {
  const stats = {
    all: rows2.length,
    new: 0,
    existing: 0,
    noOemPrice: 0,
    abnormalImport: 0,
    active: 0,
    inactive: 0,
    normal: 0,
    set: 0,
    multi: 0,
    setChild: 0
  };
  rows2.forEach((row) => {
    if (matchesContainerDetailTagFilter(row, "new")) stats.new += 1;
    if (matchesContainerDetailTagFilter(row, "existing")) stats.existing += 1;
    if (matchesContainerDetailTagFilter(row, "noOemPrice")) stats.noOemPrice += 1;
    if (matchesContainerDetailTagFilter(row, "abnormalImport")) stats.abnormalImport += 1;
    if (matchesContainerDetailTagFilter(row, "active")) stats.active += 1;
    if (matchesContainerDetailTagFilter(row, "inactive")) stats.inactive += 1;
    const productType = getContainerDetailProductTypeFilterKey(row);
    stats[productType] += 1;
  });
  return stats;
}
function normalizeText(value) {
  return (value ?? "").trim().toLowerCase();
}
function matchesTextFilter(value, filter) {
  const normalizedFilter = normalizeText(filter);
  if (!normalizedFilter) return true;
  return normalizeText(value).includes(normalizedFilter);
}
function applyContainerDetailLoadedTextFilters(rows2, itemNumberFilter, filters) {
  return rows2.filter((row) => (
    // 前端文字筛选只作用于当前已加载行，避免输入关键字时触发货柜明细远程重载。
    matchesTextFilter(getContainerDetailItemNumber(row), itemNumberFilter) && matchesTextFilter(getContainerDetailItemNumber(row), filters.itemNumber) && matchesTextFilter(getContainerDetailBarcode(row), filters.barcode) && matchesTextFilter(getContainerDetailProductName(row), filters.productName) && matchesTextFilter(getContainerDetailEnglishName(row), filters.englishName) && matchesTextFilter(row.\u5907\u6CE8, filters.remark)
  ));
}
function isEmptyNumberRange(filter) {
  return filter?.min == null && filter?.max == null;
}
function matchesNumberRange(value, filter) {
  if (isEmptyNumberRange(filter)) return true;
  if (value == null) return false;
  if (filter?.min != null && value < filter.min) return false;
  if (filter?.max != null && value > filter.max) return false;
  return true;
}
function matchesOneOf(value, selected) {
  return !selected?.length || selected.includes(value);
}
function getColumnSortValue(row, field) {
  switch (field) {
    case "itemNumber":
      return getContainerDetailItemNumber(row);
    case "barcode":
      return getContainerDetailBarcode(row);
    case "productName":
      return getContainerDetailProductName(row);
    case "englishName":
      return getContainerDetailEnglishName(row);
    case "productType":
      return getContainerDetailProductTypeFilterKey(row);
    case "newProduct":
      return row.\u662F\u5426\u65B0\u5546\u54C1 ? 1 : 0;
    case "matchType":
      return getContainerDetailMatchType(row);
    case "containerPieces":
      return row.\u88C5\u67DC\u4EF6\u6570;
    case "middlePackQuantity":
      return row.\u4E2D\u5305\u6570;
    case "containerQuantity":
      return row.\u88C5\u67DC\u6570\u91CF;
    case "packingQuantity":
      return row.\u5355\u4EF6\u88C5\u7BB1\u6570;
    case "unitVolume":
      return row.\u5355\u4EF6\u4F53\u79EF;
    case "domesticPrice":
      return row.\u56FD\u5185\u4EF7\u683C;
    case "floatRate":
      return row.\u8C03\u6574\u6D6E\u7387;
    case "transportCost":
      return row.\u8FD0\u8F93\u6210\u672C;
    case "unitTransportCost":
      return calculateContainerDetailUnitTransportCost(row);
    case "warehouseImportPrice":
      return getContainerDetailRealtimeImportPrice(row);
    case "lastOEMPrice":
      return getContainerDetailRealtimeRetailPrice(row);
    case "importPrice":
      return row.\u8FDB\u53E3\u4EF7\u683C;
    case "oemPrice":
      return getContainerDetailVisibleOemPrice(row);
    case "warehouseStatus":
      return row.warehouseIsActive === true ? 1 : 0;
    case "remark":
      return row.\u5907\u6CE8;
    default:
      return void 0;
  }
}
function compareColumnValues(a, b) {
  const aEmpty = a == null || typeof a === "string" && !a.trim();
  const bEmpty = b == null || typeof b === "string" && !b.trim();
  if (aEmpty && bEmpty) return 0;
  if (aEmpty) return 1;
  if (bEmpty) return -1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), "zh-CN", { numeric: true, sensitivity: "base" });
}
function applyContainerDetailColumnState(rows2, filters, sortState) {
  const filtered = rows2.filter((row) => matchesTextFilter(getContainerDetailItemNumber(row), filters.itemNumber) && matchesTextFilter(getContainerDetailBarcode(row), filters.barcode) && matchesTextFilter(getContainerDetailProductName(row), filters.productName) && matchesTextFilter(getContainerDetailEnglishName(row), filters.englishName) && matchesTextFilter(row.\u5907\u6CE8, filters.remark) && matchesOneOf(getContainerDetailProductTypeFilterKey(row), filters.productTypes) && matchesOneOf(row.\u662F\u5426\u65B0\u5546\u54C1 ? "new" : "existing", filters.newProductStates) && matchesOneOf(getContainerDetailMatchType(row), filters.matchTypes) && matchesOneOf(getContainerDetailWarehouseStatusFilterKey(row), filters.warehouseStatus) && matchesNumberRange(row.\u88C5\u67DC\u4EF6\u6570, filters.containerPieces) && matchesNumberRange(row.\u4E2D\u5305\u6570, filters.middlePackQuantity) && matchesNumberRange(row.\u88C5\u67DC\u6570\u91CF, filters.containerQuantity) && matchesNumberRange(row.\u5355\u4EF6\u88C5\u7BB1\u6570, filters.packingQuantity) && matchesNumberRange(row.\u5355\u4EF6\u4F53\u79EF, filters.unitVolume) && matchesNumberRange(row.\u56FD\u5185\u4EF7\u683C, filters.domesticPrice) && matchesNumberRange(row.\u8C03\u6574\u6D6E\u7387, filters.floatRate) && matchesNumberRange(row.\u8FD0\u8F93\u6210\u672C, filters.transportCost) && matchesNumberRange(calculateContainerDetailUnitTransportCost(row), filters.unitTransportCost) && matchesNumberRange(getContainerDetailRealtimeImportPrice(row), filters.warehouseImportPrice) && matchesNumberRange(getContainerDetailRealtimeRetailPrice(row), filters.lastOEMPrice) && matchesNumberRange(row.\u8FDB\u53E3\u4EF7\u683C, filters.importPrice) && matchesNumberRange(getContainerDetailVisibleOemPrice(row), filters.oemPrice));
  if (!sortState) return filtered;
  return filtered.map((row, index) => ({ row, index })).sort((left, right) => {
    const result = compareColumnValues(
      getColumnSortValue(left.row, sortState.field),
      getColumnSortValue(right.row, sortState.field)
    );
    if (result === 0) return left.index - right.index;
    return sortState.order === "ascend" ? result : -result;
  }).map((item) => item.row);
}
function assignQueryValue(target, key, value) {
  target[key] = value;
}
function assignTrimmedText(target, key, value) {
  const normalized = value?.trim();
  if (normalized) {
    assignQueryValue(target, key, normalized);
  }
}
function assignNonEmptyArray(target, key, value) {
  if (value?.length) {
    assignQueryValue(target, key, [...value]);
  }
}
function assignNumberRange(target, minKey, maxKey, range) {
  if (range?.min != null) {
    assignQueryValue(target, minKey, range.min);
  }
  if (range?.max != null) {
    assignQueryValue(target, maxKey, range.max);
  }
}
function buildContainerDetailQuery({
  containerGuid,
  filters,
  sortState,
  pageNumber,
  pageSize,
  includeTotal,
  includeStats
}) {
  const query = {
    containerGuid,
    pageNumber,
    pageSize
  };
  if (includeTotal != null) {
    query.includeTotal = includeTotal;
  }
  if (includeStats != null) {
    query.includeStats = includeStats;
  }
  assignTrimmedText(query, "itemNumber", filters.itemNumber);
  assignTrimmedText(query, "barcode", filters.barcode);
  assignTrimmedText(query, "productName", filters.productName);
  assignTrimmedText(query, "englishName", filters.englishName);
  assignTrimmedText(query, "remark", filters.remark);
  assignNonEmptyArray(query, "productTypes", filters.productTypes);
  assignNonEmptyArray(query, "newProductStates", filters.newProductStates);
  assignNonEmptyArray(query, "matchTypes", filters.matchTypes);
  assignNonEmptyArray(query, "warehouseStatus", filters.warehouseStatus);
  assignNumberRange(query, "containerPiecesMin", "containerPiecesMax", filters.containerPieces);
  assignNumberRange(query, "middlePackQuantityMin", "middlePackQuantityMax", filters.middlePackQuantity);
  assignNumberRange(query, "containerQuantityMin", "containerQuantityMax", filters.containerQuantity);
  assignNumberRange(query, "packingQuantityMin", "packingQuantityMax", filters.packingQuantity);
  assignNumberRange(query, "unitVolumeMin", "unitVolumeMax", filters.unitVolume);
  assignNumberRange(query, "domesticPriceMin", "domesticPriceMax", filters.domesticPrice);
  assignNumberRange(query, "floatRateMin", "floatRateMax", filters.floatRate);
  assignNumberRange(query, "transportCostMin", "transportCostMax", filters.transportCost);
  assignNumberRange(query, "unitTransportCostMin", "unitTransportCostMax", filters.unitTransportCost);
  assignNumberRange(query, "warehouseImportPriceMin", "warehouseImportPriceMax", filters.warehouseImportPrice);
  assignNumberRange(query, "lastOEMPriceMin", "lastOEMPriceMax", filters.lastOEMPrice);
  assignNumberRange(query, "importPriceMin", "importPriceMax", filters.importPrice);
  assignNumberRange(query, "oemPriceMin", "oemPriceMax", filters.oemPrice);
  if (sortState) {
    query.sortBy = sortState.field;
    query.sortOrder = sortState.order;
  }
  return query;
}
function mergeContainerDetailLoadedItems(loadedItems, nextItems) {
  const merged = [...loadedItems];
  const indexByGuid = /* @__PURE__ */ new Map();
  merged.forEach((item, index) => {
    if (item.hguid) {
      indexByGuid.set(item.hguid, index);
    }
  });
  nextItems.forEach((item) => {
    const existingIndex = item.hguid ? indexByGuid.get(item.hguid) : void 0;
    if (existingIndex == null) {
      if (item.hguid) {
        indexByGuid.set(item.hguid, merged.length);
      }
      merged.push(item);
      return;
    }
    merged[existingIndex] = item;
  });
  return merged;
}
function getContainerDetailRemoteQueryResetState(_state) {
  return {
    selectedRowKeys: [],
    loadedItems: [],
    pageNumber: 1
  };
}
function applyContainerDetailWarehouseStatusByProductCodes(rows2, productCodes, isActive) {
  const productCodeSet = new Set(productCodes.map((value) => value.trim()).filter(Boolean));
  return rows2.map((row) => {
    const productCode = getContainerDetailProductCode(row);
    return productCode && productCodeSet.has(productCode) ? { ...row, warehouseIsActive: isActive } : row;
  });
}
function getContainerDetailWarehouseActionFailureMessage(result, fallback) {
  const failedCount = Number(result.failedCount ?? result.FailedCount ?? 0);
  const errors = result.errors ?? result.Errors ?? [];
  if (result.success === false || result.isSuccess === false || failedCount > 0) {
    return result.message ?? result.Message ?? errors.join("\uFF1B") ?? fallback;
  }
  return void 0;
}
function buildContainerDetailTranslationUpdates(rows2, translations) {
  const updates = [];
  rows2.forEach((row) => {
    const name = getContainerDetailTranslationSource(row);
    const englishName = name ? translations[name] : void 0;
    if (row.hguid && isValidContainerDetailEnglishTranslation(englishName)) {
      updates.push({ hguid: row.hguid, \u82F1\u6587\u540D\u79F0: englishName.trim() });
    }
  });
  return updates;
}
function countContainerDetailInvalidTranslationResults(rows2, translations) {
  return rows2.filter((row) => {
    const name = getContainerDetailTranslationSource(row);
    const englishName = name ? translations[name] : void 0;
    return Boolean(englishName) && !isValidContainerDetailEnglishTranslation(englishName);
  }).length;
}
function buildContainerDetailEnglishNameUpdates(rows2, englishName) {
  const normalizedEnglishName = englishName.trim();
  if (!isValidContainerDetailEnglishTranslation(normalizedEnglishName)) return [];
  return rows2.filter((row) => Boolean(row.hguid)).map((row) => ({ hguid: row.hguid, \u82F1\u6587\u540D\u79F0: normalizedEnglishName }));
}
function buildContainerDetailClearEnglishNameUpdates(rows2) {
  return rows2.filter((row) => Boolean(row.hguid)).map((row) => ({ hguid: row.hguid, ClearEnglishName: true }));
}
function applyContainerDetailEnglishNameUpdates(rows2, updates) {
  const updateMap = new Map(updates.map((item) => [item.hguid, item.\u82F1\u6587\u540D\u79F0]));
  return rows2.map((row) => updateMap.has(row.hguid) ? withContainerDetailEnglishName(row, updateMap.get(row.hguid)) : row);
}
function roundToDigits(value, digits) {
  const base = 10 ** digits;
  return Math.round((value + Number.EPSILON) * base) / base;
}
function isPlainRecord(value) {
  return typeof value === "object" && value !== null;
}
function normalizeContainerDetailPushToHqPayload(raw, fallbackMessage) {
  if (!isPlainRecord(raw)) return null;
  const errors = Array.isArray(raw.errors) ? raw.errors.map(String) : [];
  const successCount = Number(raw.successCount ?? raw.productsAdded ?? 0) + Number(raw.successCount === void 0 ? raw.productsUpdated ?? 0 : 0);
  const failedCount = Number(raw.failedCount ?? raw.errorCount ?? errors.length);
  const affectedRowCount = Number(raw.affectedRowCount ?? 0) || Number(raw.productsAdded ?? 0) + Number(raw.productsUpdated ?? 0) + Number(raw.warehouseInventoriesCreated ?? 0) + Number(raw.warehouseInventoriesUpdated ?? 0) + Number(raw.storeRetailPricesCreated ?? 0) + Number(raw.storeRetailPricesUpdated ?? 0) + Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0) + Number(raw.productSetCodesUpdated ?? 0) + Number(raw.storeMultiCodesCreated ?? 0) + Number(raw.storeMultiCodesUpdated ?? 0);
  return {
    ...raw,
    successCount,
    failedCount,
    totalCount: Number(raw.totalCount ?? successCount + failedCount),
    affectedRowCount,
    errors,
    message: typeof raw.message === "string" ? raw.message : fallbackMessage
  };
}
function extractPushToHqErrorResult(error) {
  if (!isPlainRecord(error) || !("payload" in error)) return null;
  const payload = error.payload;
  if (!isPlainRecord(payload)) return null;
  const fallbackMessage = typeof payload.message === "string" ? payload.message : error instanceof Error ? error.message : void 0;
  return normalizeContainerDetailPushToHqPayload(payload.data, fallbackMessage) ?? normalizeContainerDetailPushToHqPayload(payload.details, fallbackMessage) ?? normalizeContainerDetailPushToHqPayload(payload, fallbackMessage);
}
function calculateContainerDetailTransportCost(row, container) {
  const freight = container?.\u8FD0\u8D39;
  const totalVolume = container?.\u603B\u4F53\u79EF;
  const containerQuantity = row.\u88C5\u67DC\u6570\u91CF;
  const unitVolume = row.\u5355\u4EF6\u4F53\u79EF ?? row.\u5546\u54C1\u4FE1\u606F?.\u5355\u4EF6\u4F53\u79EF;
  const detailVolume = row.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF ?? (row.\u88C5\u67DC\u4EF6\u6570 != null && unitVolume != null ? row.\u88C5\u67DC\u4EF6\u6570 * unitVolume : void 0);
  if (freight == null || freight < 0 || !totalVolume || totalVolume <= 0 || containerQuantity == null || containerQuantity <= 0 || detailVolume == null || detailVolume < 0) {
    return row.\u8FD0\u8F93\u6210\u672C;
  }
  return roundToDigits(freight * detailVolume / containerQuantity / totalVolume, 2);
}
function calculateContainerDetailImportPrice(row, container, floatRate, transportCost) {
  const exchangeRate = container?.\u6C47\u7387;
  if (!exchangeRate || exchangeRate <= 0 || row.\u56FD\u5185\u4EF7\u683C == null) {
    return row.\u8FDB\u53E3\u4EF7\u683C;
  }
  return roundToDigits((row.\u56FD\u5185\u4EF7\u683C / exchangeRate + (transportCost ?? 0)) * floatRate * 10 / 11, 2);
}
function getContainerDetailCostMissingFields(container) {
  const fields = [];
  if (!container?.\u6C47\u7387 || container.\u6C47\u7387 <= 0) {
    fields.push("exchangeRate");
  }
  if (container?.\u8FD0\u8D39 == null) {
    fields.push("freight");
  }
  if (!container?.\u603B\u4F53\u79EF || container.\u603B\u4F53\u79EF <= 0) {
    fields.push("totalVolume");
  }
  return fields;
}
function calculateContainerSetCodePurchasePrice(mainPurchasePrice, itemRetailPrice, totalRetailPrice) {
  if (mainPurchasePrice == null || mainPurchasePrice <= 0 || itemRetailPrice == null || itemRetailPrice < 0 || totalRetailPrice == null || totalRetailPrice <= 0) {
    return void 0;
  }
  return roundToDigits(mainPurchasePrice * itemRetailPrice / totalRetailPrice, 2);
}
function buildContainerDetailFloatRateUpdates(rows2, container, floatRate) {
  return rows2.filter((row) => row.hguid).map((row) => {
    const nextFloatRate = floatRate ?? row.\u8C03\u6574\u6D6E\u7387 ?? DEFAULT_CONTAINER_DETAIL_FLOAT_RATE;
    const transportCost = calculateContainerDetailTransportCost(row, container);
    const importPrice = calculateContainerDetailImportPrice(row, container, nextFloatRate, transportCost);
    const hasChange = row.\u8C03\u6574\u6D6E\u7387 !== nextFloatRate || row.\u8FD0\u8F93\u6210\u672C !== transportCost || row.\u8FDB\u53E3\u4EF7\u683C !== importPrice;
    if (!hasChange) {
      return null;
    }
    return {
      hguid: row.hguid,
      \u8C03\u6574\u6D6E\u7387: nextFloatRate,
      \u8FD0\u8F93\u6210\u672C: transportCost,
      \u8FDB\u53E3\u4EF7\u683C: importPrice,
      // 浮率导致的系统重算只更新货柜明细，避免覆盖仓库表里人工维护的进货价。
      SkipRelatedProductSync: true
    };
  }).filter((update) => update !== null);
}
function isMissingPrice(value) {
  return value == null || value <= 0;
}
function normalizeMatchKey(value) {
  return value?.trim().toUpperCase();
}
function getDetectedLocalProductCode(item) {
  return item.LocalProductCode ?? item.localProductCode ?? item.ProductCode ?? item.productCode;
}
function getDetectedDomesticProductCode(item) {
  return item.DomesticProductCode ?? item.domesticProductCode;
}
function getDetectedConflictReason(item) {
  return item.ConflictReason ?? item.conflictReason;
}
function hasDetectedProductCodeConflict(item) {
  const explicit = item.HasProductCodeConflict ?? item.hasProductCodeConflict;
  if (explicit != null) return Boolean(explicit);
  const localProductCode = normalizeMatchKey(getDetectedLocalProductCode(item));
  const domesticProductCode = normalizeMatchKey(getDetectedDomesticProductCode(item));
  return Boolean(localProductCode && domesticProductCode && localProductCode !== domesticProductCode);
}
function getContainerDetailDetectionProductCode(row) {
  const productCode = getContainerDetailProductCode(row);
  return productCode;
}
function buildSupplierItemMatchKey(supplierCode, itemNumber) {
  const normalizedSupplierCode = normalizeMatchKey(supplierCode);
  const normalizedItemNumber = normalizeMatchKey(itemNumber);
  return normalizedSupplierCode && normalizedItemNumber ? `${normalizedSupplierCode}:${normalizedItemNumber}` : void 0;
}
function getContainerDetailSupplierCode(row) {
  return firstTrimmedValue(row.localSupplierCode, row.\u5546\u54C1\u4FE1\u606F?.localSupplierCode) ?? "200";
}
function buildContainerDetailDetectionItems(rows2) {
  return rows2.map((row) => ({
    // 检测同时携带商品编码和供应商+货号，由匹配结果决定最终展示方式。
    ProductCode: getContainerDetailDetectionProductCode(row),
    ItemNumber: getContainerDetailItemNumber(row),
    SupplierCode: getContainerDetailSupplierCode(row)
  })).filter((item) => item.ProductCode || item.ItemNumber);
}
function getDetectedDomesticPrice(item) {
  return item.DomesticPrice ?? item.domesticPrice ?? item.WarehouseDomesticPrice ?? item.warehouseDomesticPrice;
}
function getDetectedOemPrice(item) {
  return item.WarehouseOEMPrice ?? item.warehouseOEMPrice ?? item.DomesticOEMPrice ?? item.domesticOEMPrice ?? item.labelPrice ?? item.oemPrice ?? item.OEMPrice;
}
function getDetectedProductName(item) {
  return item.productName ?? item.ProductName ?? item.name;
}
function getDetectedEnglishName(item) {
  return item.englishName ?? item.EnglishName ?? item.nameEn;
}
function getDetectedPackingQuantity(item) {
  return item.PackingQuantity ?? item.packingQuantity ?? item.packingQty;
}
function getDetectedUnitVolume(item) {
  return item.WarehouseVolume ?? item.warehouseVolume ?? item.volume ?? item.Volume ?? item.unitVolume ?? item.UnitVolume;
}
function calculateContainerDetailTotalAmount(row) {
  if (row.\u88C5\u67DC\u6570\u91CF == null || row.\u56FD\u5185\u4EF7\u683C == null) return row.\u5408\u8BA1\u88C5\u67DC\u91D1\u989D;
  return roundToDigits(row.\u88C5\u67DC\u6570\u91CF * row.\u56FD\u5185\u4EF7\u683C * (row.\u8C03\u6574\u6D6E\u7387 ?? DEFAULT_CONTAINER_DETAIL_FLOAT_RATE), 2);
}
function calculateContainerDetailTotalVolume(row) {
  const unitVolume = row.\u5355\u4EF6\u4F53\u79EF ?? row.\u5546\u54C1\u4FE1\u606F?.\u5355\u4EF6\u4F53\u79EF;
  if (row.\u88C5\u67DC\u4EF6\u6570 == null || unitVolume == null) return row.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF;
  return roundToDigits(row.\u88C5\u67DC\u4EF6\u6570 * unitVolume, 3);
}
function buildDetectedPriceMaps(items) {
  const productCodeMap = /* @__PURE__ */ new Map();
  const supplierItemMap = /* @__PURE__ */ new Map();
  items.forEach((item) => {
    if ((item.Exists ?? item.exists) === false) return;
    const productCode = normalizeMatchKey(item.productCode ?? item.ProductCode);
    const hasConflict = hasDetectedProductCodeConflict(item);
    const supplierItemKey = buildSupplierItemMatchKey(item.supplierCode ?? item.SupplierCode ?? "200", item.itemNumber ?? item.ItemNumber);
    if (productCode && !hasConflict) productCodeMap.set(productCode, item);
    if (supplierItemKey) supplierItemMap.set(supplierItemKey, item);
  });
  return { productCodeMap, supplierItemMap };
}
function resolveContainerDetailDetectedMatch(row, detectedMaps) {
  const itemNumber = normalizeMatchKey(getContainerDetailItemNumber(row));
  const supplierItemKey = buildSupplierItemMatchKey(getContainerDetailSupplierCode(row), itemNumber);
  const detectionProductCode = normalizeMatchKey(getContainerDetailDetectionProductCode(row));
  const productCodeMatch = detectionProductCode ? detectedMaps.productCodeMap.get(detectionProductCode) : void 0;
  if (productCodeMatch) {
    return {
      item: productCodeMatch,
      matchType: "productCode"
    };
  }
  const supplierItemMatch = supplierItemKey ? detectedMaps.supplierItemMap.get(supplierItemKey) : void 0;
  if (supplierItemMatch) {
    return {
      item: supplierItemMatch,
      matchType: "supplierItem"
    };
  }
  return void 0;
}
function buildContainerDetailMatchStatusUpdates(rows2, detectedItems) {
  const detectedMaps = buildDetectedPriceMaps(detectedItems);
  return rows2.map((row) => {
    if (!row.hguid) return null;
    const match = resolveContainerDetailDetectedMatch(row, detectedMaps);
    if (!match) return null;
    const localProductCode = getDetectedLocalProductCode(match.item);
    const domesticProductCode = getDetectedDomesticProductCode(match.item) ?? getContainerDetailProductCode(row);
    const hasProductCodeConflict = Boolean(
      localProductCode && domesticProductCode && normalizeMatchKey(localProductCode) !== normalizeMatchKey(domesticProductCode)
    ) || hasDetectedProductCodeConflict(match.item);
    const isCandidate = match.matchType === "supplierItem" || hasProductCodeConflict;
    return {
      hguid: row.hguid,
      matchType: isCandidate ? "supplierItem" : match.matchType,
      ...isCandidate ? {
        hasProductCodeConflict,
        localProductCode,
        domesticProductCode,
        conflictReason: getDetectedConflictReason(match.item)
      } : { \u662F\u5426\u65B0\u5546\u54C1: false }
    };
  }).filter((update) => update !== null);
}
function buildContainerDetailMatchedDomesticDataUpdates(rows2, detectedItems, container) {
  const detectedMaps = buildDetectedPriceMaps(detectedItems);
  return rows2.map((row) => {
    if (!row.hguid) return null;
    const detectedMatch = resolveContainerDetailDetectedMatch(row, detectedMaps);
    if (!detectedMatch) return null;
    if (detectedMatch.matchType !== "productCode" || hasDetectedProductCodeConflict(detectedMatch.item)) return null;
    const update = { hguid: row.hguid };
    const match = detectedMatch.item;
    update.matchType = detectedMatch.matchType;
    update.\u662F\u5426\u65B0\u5546\u54C1 = false;
    const domesticPrice = getDetectedDomesticPrice(match);
    const oemPrice = getDetectedOemPrice(match);
    const productName = getDetectedProductName(match);
    const englishName = getDetectedEnglishName(match);
    const packingQuantity = getDetectedPackingQuantity(match);
    const unitVolume = getDetectedUnitVolume(match);
    if (isMissingPrice(row.\u56FD\u5185\u4EF7\u683C) && domesticPrice != null && domesticPrice > 0) {
      update.\u56FD\u5185\u4EF7\u683C = domesticPrice;
    }
    if (isMissingPrice(row.\u8D34\u724C\u4EF7\u683C) && oemPrice != null && oemPrice > 0) {
      update.\u8D34\u724C\u4EF7\u683C = oemPrice;
    }
    if (productName && productName !== getContainerDetailProductName(row)) {
      update.\u5546\u54C1\u540D\u79F0 = productName;
    }
    if (englishName && englishName !== getContainerDetailEnglishName(row)) {
      update.\u82F1\u6587\u540D\u79F0 = englishName;
    }
    if (packingQuantity != null && packingQuantity > 0 && packingQuantity !== row.\u5355\u4EF6\u88C5\u7BB1\u6570) {
      update.\u5355\u4EF6\u88C5\u7BB1\u6570 = packingQuantity;
      if (row.\u88C5\u67DC\u4EF6\u6570 != null) {
        update.\u88C5\u67DC\u6570\u91CF = roundToDigits(row.\u88C5\u67DC\u4EF6\u6570 * packingQuantity, 2);
      }
    }
    if (unitVolume != null && unitVolume >= 0 && unitVolume !== row.\u5355\u4EF6\u4F53\u79EF) {
      update.\u5355\u4EF6\u4F53\u79EF = unitVolume;
    }
    const nextRow = mergeContainerDetailPatch(row, update);
    const totalVolume = calculateContainerDetailTotalVolume(nextRow);
    if (totalVolume !== row.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF) update.\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF = totalVolume;
    const amountRow = mergeContainerDetailPatch(row, update);
    const totalAmount = calculateContainerDetailTotalAmount(amountRow);
    if (totalAmount !== row.\u5408\u8BA1\u88C5\u67DC\u91D1\u989D) update.\u5408\u8BA1\u88C5\u67DC\u91D1\u989D = totalAmount;
    const pricedRow = mergeContainerDetailPatch(row, update);
    const transportCost = calculateContainerDetailTransportCost(pricedRow, container);
    const importPrice = calculateContainerDetailImportPrice(
      { ...pricedRow, \u8FD0\u8F93\u6210\u672C: transportCost },
      container,
      pricedRow.\u8C03\u6574\u6D6E\u7387 ?? DEFAULT_CONTAINER_DETAIL_FLOAT_RATE,
      transportCost
    );
    if (transportCost !== row.\u8FD0\u8F93\u6210\u672C) update.\u8FD0\u8F93\u6210\u672C = transportCost;
    if (importPrice !== row.\u8FDB\u53E3\u4EF7\u683C) update.\u8FDB\u53E3\u4EF7\u683C = importPrice;
    return Object.keys(update).length > 1 ? update : null;
  }).filter((update) => update !== null);
}
function buildContainerDetailHqPushSelection(rows2) {
  const productCodes = [];
  const items = [];
  let skippedNewProductCount = 0;
  let missingProductCodeCount = 0;
  const candidateKeys = /* @__PURE__ */ new Set();
  rows2.forEach((row) => {
    const isNewProduct = Boolean(row.\u662F\u5426\u65B0\u5546\u54C1);
    const productCode = row.\u5546\u54C1\u7F16\u7801?.trim() || row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u7F16\u7801?.trim();
    const localSupplierCode = row.localSupplierCode?.trim() || row.\u5546\u54C1\u4FE1\u606F?.localSupplierCode?.trim();
    const itemNumber = row.\u5546\u54C1\u4FE1\u606F?.\u8D27\u53F7?.trim();
    const productName = getContainerDetailProductName(row)?.trim();
    const englishName = getContainerDetailEnglishName(row)?.trim();
    const barcode = row.\u5546\u54C1\u4FE1\u606F?.\u6761\u5F62\u7801?.trim();
    const imageUrl = row.\u5546\u54C1\u56FE\u7247?.trim() || row.\u5546\u54C1\u4FE1\u606F?.\u5546\u54C1\u56FE\u7247?.trim();
    const oemPrice = getContainerDetailVisibleOemPrice(row);
    if (!productCode && !(localSupplierCode && itemNumber)) {
      missingProductCodeCount += 1;
      return;
    }
    const candidateKey = productCode ? `code:${productCode.toUpperCase()}` : `supplier-item:${localSupplierCode.toUpperCase()}:${itemNumber.toUpperCase()}`;
    if (candidateKeys.has(candidateKey)) {
      return;
    }
    candidateKeys.add(candidateKey);
    if (productCode && !productCodes.includes(productCode)) {
      productCodes.push(productCode);
    }
    items.push({
      productCode: productCode || void 0,
      localSupplierCode,
      itemNumber,
      productName,
      englishName,
      barcode,
      imageUrl,
      domesticPrice: row.\u56FD\u5185\u4EF7\u683C == null ? void 0 : Number(row.\u56FD\u5185\u4EF7\u683C),
      importPrice: row.\u8FDB\u53E3\u4EF7\u683C == null ? void 0 : Number(row.\u8FDB\u53E3\u4EF7\u683C),
      oemPrice: oemPrice == null ? void 0 : Number(oemPrice),
      isNewProduct
    });
  });
  return {
    productCodes,
    items,
    skippedNewProductCount,
    missingProductCodeCount
  };
}

// src/pages/Warehouse/ContainerDetail/containerDetailLogic.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var rows = [
  {
    id: 1,
    hguid: "detail-1",
    \u5546\u54C1\u540D\u79F0: "\u660E\u7EC6\u5927\u8349\u8393",
    \u82F1\u6587\u540D\u79F0: "Detail Strawberry",
    \u5546\u54C1\u4FE1\u606F: {
      \u5546\u54C1\u540D\u79F0: "\u5546\u54C1\u4FE1\u606F\u5927\u8349\u8393",
      \u82F1\u6587\u540D\u79F0: "Product Strawberry"
    }
  },
  {
    id: 2,
    hguid: "detail-2",
    \u5546\u54C1\u4FE1\u606F: {
      \u5546\u54C1\u540D\u79F0: "TPR\u9CA8\u9C7C",
      \u82F1\u6587\u540D\u79F0: "TPR Shark"
    }
  }
];
assertDeepEqual(
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS,
  [
    "index",
    "itemNumber",
    "productName",
    "englishName",
    "containerPieces",
    "containerQuantity",
    "unitVolume",
    "totalVolume",
    "middlePackQuantity",
    "domesticPrice",
    "oemPrice"
  ],
  "\u8D27\u67DC\u660E\u7EC6\u9ED8\u8BA4\u5BFC\u51FA\u5217\u5E94\u4E3A\u7528\u6237\u6307\u5B9A\u7684 Excel \u6838\u5BF9\u6A21\u677F"
);
assertEqual(
  DEFAULT_CONTAINER_DETAIL_EXPORT_COLUMN_KEYS.some((key) => key === "barcodeImage" || key === "productImage"),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u9ED8\u8BA4\u5BFC\u51FA\u5217\u4E0D\u5E94\u5305\u542B\u56FE\u7247\u5217\uFF0C\u907F\u514D\u9ED8\u8BA4\u5BFC\u51FA\u53D8\u6162"
);
var updateFieldOptions = ["importPrice", "oemPrice", "storeRetailPrice"];
assertDeepEqual(
  getUpdateFieldSelectionState(["importPrice"], updateFieldOptions),
  { isAllSelected: false, isPartiallySelected: true },
  "\u5B57\u6BB5\u9009\u62E9\u5668\u90E8\u5206\u52FE\u9009\u65F6\u5E94\u663E\u793A\u534A\u9009\u6001"
);
assertDeepEqual(
  getUpdateFieldSelectionState([...updateFieldOptions], updateFieldOptions),
  { isAllSelected: true, isPartiallySelected: false },
  "\u5B57\u6BB5\u9009\u62E9\u5668\u5168\u90E8\u52FE\u9009\u65F6\u5E94\u663E\u793A\u5168\u9009\u6001"
);
assertDeepEqual(
  getUpdateFieldSelectionState([], updateFieldOptions),
  { isAllSelected: false, isPartiallySelected: false },
  "\u5B57\u6BB5\u9009\u62E9\u5668\u672A\u52FE\u9009\u65F6\u4E0D\u5E94\u663E\u793A\u5168\u9009\u6216\u534A\u9009\u6001"
);
assertDeepEqual(
  getNextUpdateFieldSelection(true, updateFieldOptions),
  [...updateFieldOptions],
  "\u5B57\u6BB5\u9009\u62E9\u5668\u70B9\u51FB\u5168\u9009\u65F6\u5E94\u9009\u4E2D\u5168\u90E8\u5B57\u6BB5"
);
assertDeepEqual(
  getNextUpdateFieldSelection(false, updateFieldOptions),
  [],
  "\u5B57\u6BB5\u9009\u62E9\u5668\u53D6\u6D88\u5168\u9009\u65F6\u5E94\u6E05\u7A7A\u5B57\u6BB5"
);
assertDeepEqual(
  DEFAULT_CONTAINER_DETAIL_PDF_EXPORT_COLUMN_KEYS,
  ["index", "productImage", "itemNumber", "barcodeImage", "englishName", "oemPrice"],
  "\u8D27\u67DC\u660E\u7EC6 PDF \u9ED8\u8BA4\u5BFC\u51FA\u5217\u5E94\u4E3A\u5E8F\u53F7\u3001\u5546\u54C1\u56FE\u7247\u3001\u8D27\u53F7\u3001\u6761\u7801\u56FE\u7247\u3001\u82F1\u6587\u548C\u96F6\u552E\u4EF7"
);
var customExportColumnKeys = ["oemPrice", "itemNumber", "containerQuantity"];
assertDeepEqual(
  getContainerDetailExportColumns(customExportColumnKeys).map((column) => column.key),
  ["oemPrice", "itemNumber", "containerQuantity"],
  "\u8D27\u67DC\u660E\u7EC6\u81EA\u5B9A\u4E49\u5BFC\u51FA\u5217\u5E94\u6309\u7528\u6237\u9009\u62E9\u987A\u5E8F\u8F93\u51FA"
);
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.filter((column) => column.key === "lastImportPrice" || column.key === "lastOEMPrice").map((column) => column.key),
  ["lastImportPrice", "lastOEMPrice"],
  "\u8D27\u67DC\u660E\u7EC6\u53EF\u9009\u5BFC\u51FA\u5217\u5E94\u5305\u542B\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u548C\u5B9E\u65F6\u96F6\u552E\u4EF7"
);
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.filter((column) => column.key === "barcode" || column.key === "barcodeImage" || column.key === "productImage").map((column) => column.key),
  ["barcode", "barcodeImage", "productImage"],
  "\u8D27\u67DC\u660E\u7EC6\u53EF\u9009\u5BFC\u51FA\u5217\u5E94\u5305\u542B\u6761\u7801\u3001\u6761\u7801\u56FE\u7247\u548C\u5546\u54C1\u56FE\u7247"
);
assertEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.find((column) => column.key === "lastImportPrice")?.labelKey,
  "containers.fields.warehouseImportPrice",
  "\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u5BFC\u51FA\u5217\u5E94\u590D\u7528\u8868\u683C\u91CC\u7684\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u7FFB\u8BD1 key"
);
assertEqual(
  getContainerDetailImportPriceTrend({ id: 120, hguid: "import-trend-up", warehouseImportPrice: 0.29, \u8FDB\u53E3\u4EF7\u683C: 0.38 }),
  "up",
  "\u672C\u6B21\u8FDB\u53E3\u4EF7\u683C\u9AD8\u4E8E\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u65F6\u5E94\u663E\u793A\u7EFF\u8272\u4E0A\u6DA8"
);
assertEqual(
  getContainerDetailImportPriceTrend({ id: 121, hguid: "import-trend-down", warehouseImportPrice: 0.38, \u8FDB\u53E3\u4EF7\u683C: 0.29 }),
  "down",
  "\u672C\u6B21\u8FDB\u53E3\u4EF7\u683C\u4F4E\u4E8E\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u65F6\u5E94\u663E\u793A\u7EA2\u8272\u4E0B\u964D"
);
assertEqual(
  getContainerDetailImportPriceTrend({ id: 122, hguid: "import-trend-same", warehouseImportPrice: 0.29, \u8FDB\u53E3\u4EF7\u683C: 0.29 }),
  void 0,
  "\u672C\u6B21\u8FDB\u53E3\u4EF7\u683C\u7B49\u4E8E\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u65F6\u4E0D\u5E94\u663E\u793A\u8D8B\u52BF\u7BAD\u5934"
);
assertEqual(
  getContainerDetailImportPriceTrend({ id: 123, hguid: "import-trend-missing-realtime", \u8FDB\u53E3\u4EF7\u683C: 0.38 }),
  void 0,
  "\u7F3A\u5C11\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u65F6\u4E0D\u5E94\u663E\u793A\u8D8B\u52BF\u7BAD\u5934"
);
assertEqual(
  getContainerDetailImportPriceTrend({ id: 124, hguid: "import-trend-missing-current", warehouseImportPrice: 0.38 }),
  void 0,
  "\u7F3A\u5C11\u672C\u6B21\u8FDB\u53E3\u4EF7\u683C\u65F6\u4E0D\u5E94\u663E\u793A\u8D8B\u52BF\u7BAD\u5934"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 768,
    toolbarHeight: 128,
    tableChromeHeight: 88,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620
  }),
  366,
  "\u684C\u9762\u8868\u683C\u9AD8\u5EA6\u5E94\u6263\u9664\u5DE5\u5177\u680F\u548C\u8868\u683C\u5934\u5C3E\uFF0C\u5E76\u5728\u9876\u90E8\u4F4E\u4E8E\u5DE5\u4F5C\u533A\u65F6\u4F7F\u7528\u4E0B\u9650"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 1200,
    toolbarHeight: 120,
    tableChromeHeight: 92,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620
  }),
  620,
  "\u684C\u9762\u7A7A\u95F4\u5145\u8DB3\u65F6\u8868\u683C\u9AD8\u5EA6\u5E94\u4FDD\u6301\u6700\u5927\u4E0A\u9650"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 165,
    tableChromeHeight: 130,
    isSmallLandscape: false,
    isSmallPortrait: false,
    maxScrollY: 620
  }),
  513,
  "\u684C\u9762\u6EDA\u52A8\u540E\u4ECD\u5E94\u4F7F\u7528\u7A33\u5B9A\u5DE5\u4F5C\u533A\u9876\u90E8\u8BA1\u7B97\uFF0C\u907F\u514D\u6EDA\u52A8\u4E2D\u6539\u53D8\u8868\u683C\u9AD8\u5EA6"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 226,
    tableChromeHeight: 130,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620
  }),
  468,
  "\u7A84\u5C4F\u6EDA\u52A8\u540E\u4ECD\u5E94\u4F7F\u7528\u7A33\u5B9A\u5DE5\u4F5C\u533A\u9876\u90E8\u8BA1\u7B97\uFF0C\u907F\u514D\u6EDA\u52A8\u4E2D\u6539\u53D8\u8868\u683C\u9AD8\u5EA6"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 994,
    toolbarHeight: 274,
    tableChromeHeight: 120,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620
  }),
  430,
  "678 \u5BBD\u7A84\u5C4F web \u89C6\u53E3\u5E94\u6309\u7A33\u5B9A\u5DE5\u4F5C\u533A top \u8BA1\u7B97\u8868\u683C body\uFF0C\u907F\u514D\u6EDA\u52A8\u4E2D\u91CD\u7B97\u9AD8\u5EA6"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 820,
    toolbarHeight: 220,
    tableChromeHeight: 124,
    isSmallLandscape: false,
    isSmallPortrait: true,
    maxScrollY: 620
  }),
  306,
  "\u7A84\u5C4F\u7A7A\u6570\u636E\u573A\u666F\u5E94\u6309\u7A33\u5B9A\u5DE5\u4F5C\u533A top \u8BA1\u7B97\u8868\u683C body\uFF0C\u4FDD\u7559\u987A\u6ED1\u6EDA\u52A8"
);
assertEqual(
  calculateContainerDetailTableScrollY({
    viewportHeight: 430,
    toolbarHeight: 168,
    tableChromeHeight: 84,
    isSmallLandscape: true,
    isSmallPortrait: false,
    maxScrollY: 620
  }),
  96,
  "\u5C0F\u5C4F\u6A2A\u5C4F\u8868\u683C\u9AD8\u5EA6\u4E0D\u8DB3\u65F6\u5E94\u53EA\u4FDD\u7559\u53EF\u64CD\u4F5C\u786C\u4E0B\u9650"
);
var exportRow = buildContainerDetailExportRow({
  id: 101,
  hguid: "export-101",
  \u5546\u54C1\u540D\u79F0: "\u660E\u7EC6\u540D\u79F0",
  \u82F1\u6587\u540D\u79F0: "Detail Name",
  \u5546\u54C1\u7C7B\u578B: "\u666E\u901A\u5546\u54C1",
  \u662F\u5426\u65B0\u5546\u54C1: true,
  matchType: "productCode",
  \u88C5\u67DC\u4EF6\u6570: 3,
  \u88C5\u67DC\u6570\u91CF: 720,
  \u56FD\u5185\u4EF7\u683C: 2.3,
  \u8C03\u6574\u6D6E\u7387: 1.2,
  \u8FD0\u8F93\u6210\u672C: 0.08,
  warehouseImportPrice: 0.52,
  \u8FDB\u53E3\u4EF7\u683C: 0.67,
  \u8D34\u724C\u4EF7\u683C: 3.5,
  warehouseOEMPrice: 4.8,
  lastImportPrice: 8.88,
  lastOEMPrice: 9.99,
  \u5355\u4EF6\u4F53\u79EF: 0.1188,
  \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0.3564,
  warehouseIsActive: true,
  \u5907\u6CE8: "\u4F18\u5148\u4E0A\u67B6",
  \u5546\u54C1\u4FE1\u606F: {
    \u8D27\u53F7: "HB291-005",
    \u6761\u5F62\u7801: "9525812910005",
    \u5546\u54C1\u540D\u79F0: "\u5546\u54C1\u4FE1\u606F\u540D\u79F0",
    \u82F1\u6587\u540D\u79F0: "Product Info Name",
    \u5546\u54C1\u56FE\u7247: "https://cdn.example.com/info-image.jpg",
    \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5546\u54C1",
    \u96F6\u552E\u4EF7\u683C: 9.99
  }
});
assertDeepEqual(
  {
    itemNumber: exportRow.itemNumber,
    barcode: exportRow.barcode,
    barcodeImage: exportRow.barcodeImage,
    productImage: exportRow.productImage,
    productName: exportRow.productName,
    englishName: exportRow.englishName,
    containerPieces: exportRow.containerPieces,
    containerQuantity: exportRow.containerQuantity,
    unitVolume: exportRow.unitVolume,
    totalVolume: exportRow.totalVolume,
    lastImportPrice: exportRow.lastImportPrice,
    lastOEMPrice: exportRow.lastOEMPrice,
    oemPrice: exportRow.oemPrice
  },
  {
    itemNumber: "HB291-005",
    barcode: "9525812910005",
    barcodeImage: "9525812910005",
    productImage: "https://cdn.example.com/info-image.jpg",
    productName: "\u660E\u7EC6\u540D\u79F0",
    englishName: "Detail Name",
    containerPieces: 3,
    containerQuantity: 720,
    unitVolume: 0.1188,
    totalVolume: 0.3564,
    lastImportPrice: 0.52,
    lastOEMPrice: 4.8,
    oemPrice: 3.5
  },
  "\u8D27\u67DC\u660E\u7EC6\u5BFC\u51FA\u884C\u5E94\u6309 Excel \u6A21\u677F\u8BFB\u53D6\u9875\u9762\u5C55\u793A\u5B57\u6BB5\u3001\u4F53\u79EF\u5B57\u6BB5\u548C\u5B9E\u65F6\u4ED3\u5E93\u4EF7\uFF0C\u4E14\u65B0\u5546\u54C1\u96F6\u552E\u4EF7\u5E94\u4F7F\u7528\u660E\u7EC6\u4E1A\u52A1\u4EF7"
);
assertEqual(
  resolveContainerDetailOemPrice({ id: 103, hguid: "oem-warehouse", \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6 }),
  2.2,
  "\u7EAF\u660E\u7EC6\u96F6\u552E\u4EF7 helper \u5E94\u53EA\u8BFB\u53D6\u8D27\u67DC\u660E\u7EC6\u4E1A\u52A1\u4EF7"
);
assertEqual(
  getContainerDetailVisibleOemPrice({ id: 104, hguid: "visible-oem-existing", \u662F\u5426\u65B0\u5546\u54C1: false, \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6 }),
  6.6,
  "\u5DF2\u6709\u5546\u54C1\u8868\u683C\u96F6\u552E\u4EF7\u5E94\u8BFB\u53D6\u4ED3\u5E93\u5B9E\u65F6\u96F6\u552E\u4EF7"
);
assertEqual(
  getContainerDetailVisibleOemPrice({ id: 105, hguid: "visible-oem-new", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6 }),
  2.2,
  "\u65B0\u5546\u54C1\u8868\u683C\u96F6\u552E\u4EF7\u5E94\u8BFB\u53D6\u8D27\u67DC\u660E\u7EC6\u96F6\u552E\u4EF7"
);
assertEqual(
  buildContainerDetailExportRow({ id: 106, hguid: "export-existing-oem", \u662F\u5426\u65B0\u5546\u54C1: false, \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6 }).oemPrice,
  6.6,
  "\u5DF2\u6709\u5546\u54C1\u5BFC\u51FA\u96F6\u552E\u4EF7\u5E94\u4F7F\u7528\u4ED3\u5E93\u5B9E\u65F6\u96F6\u552E\u4EF7"
);
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 107, hguid: "retail-warehouse-camel", \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6, LastOEMPrice: 7.7 }),
  6.6,
  "\u5B9E\u65F6\u96F6\u552E\u4EF7\u5E94\u8BFB\u53D6\u4ED3\u5E93\u5546\u54C1 camelCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 108, hguid: "retail-warehouse-pascal", \u8D34\u724C\u4EF7\u683C: 2.2, WarehouseOEMPrice: 7.7, LastOEMPrice: 8.8 }),
  7.7,
  "\u5B9E\u65F6\u96F6\u552E\u4EF7\u5E94\u517C\u5BB9\u540E\u7AEF PascalCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailRealtimeRetailPrice({ id: 109, hguid: "retail-warehouse-none", \u8D34\u724C\u4EF7\u683C: 2.2, LastOEMPrice: 8.8 }),
  void 0,
  "\u5B9E\u65F6\u96F6\u552E\u4EF7\u7F3A\u5B57\u6BB5\u65F6\u4E0D\u5E94\u56DE\u9000\u5386\u53F2\u5FEB\u7167\u6216\u660E\u7EC6\u4EF7"
);
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 110, hguid: "import-warehouse-camel", warehouseImportPrice: 5.5, LastImportPrice: 8.8 }),
  5.5,
  "\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u5E94\u8BFB\u53D6\u4ED3\u5E93\u5546\u54C1 camelCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 111, hguid: "import-warehouse-pascal", WarehouseImportPrice: 6.6, LastImportPrice: 8.8 }),
  6.6,
  "\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u5E94\u517C\u5BB9\u540E\u7AEF PascalCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailRealtimeImportPrice({ id: 112, hguid: "import-warehouse-none", LastImportPrice: 8.8 }),
  void 0,
  "\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u7F3A\u5B57\u6BB5\u65F6\u4E0D\u5E94\u56DE\u9000\u5386\u53F2\u5FEB\u7167"
);
assertEqual(
  getContainerDetailOemPriceSource({ id: 106, hguid: "oem-source-warehouse", \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 6.6 }),
  "detail",
  "\u96F6\u552E\u4EF7\u6765\u6E90\u5E94\u8BC6\u522B\u660E\u7EC6\u96F6\u552E\u4EF7"
);
assertEqual(
  getContainerDetailOemPriceSource({ id: 107, hguid: "oem-source-detail", \u8D34\u724C\u4EF7\u683C: 2.2, warehouseOEMPrice: 0 }),
  "detail",
  "\u96F6\u552E\u4EF7\u6765\u6E90\u5E94\u8BC6\u522B\u660E\u7EC6\u4E1A\u52A1\u4EF7"
);
assertEqual(
  getContainerDetailOemPriceSource({ id: 108, hguid: "oem-source-none" }),
  "none",
  "\u96F6\u552E\u4EF7\u6765\u6E90\u5E94\u8BC6\u522B\u65E0\u4EF7\u683C\u72B6\u6001"
);
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 109, hguid: "readonly-oem-camel", readonlyOemPrice: 6.6, \u8D34\u724C\u4EF7\u683C: 2.2 }),
  6.6,
  "\u53EA\u8BFB\u96F6\u552E\u4EF7\u5E94\u8BFB\u53D6\u540E\u7AEF camelCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 110, hguid: "readonly-oem-pascal", ReadonlyOemPrice: 7.7, \u8D34\u724C\u4EF7\u683C: 2.2 }),
  7.7,
  "\u53EA\u8BFB\u96F6\u552E\u4EF7\u5E94\u517C\u5BB9\u540E\u7AEF PascalCase \u5B57\u6BB5"
);
assertEqual(
  getContainerDetailReadonlyOemPrice({ id: 111, hguid: "readonly-oem-none", \u8D34\u724C\u4EF7\u683C: 2.2 }),
  void 0,
  "\u53EA\u8BFB\u96F6\u552E\u4EF7\u7F3A\u5B57\u6BB5\u65F6\u4E0D\u5E94\u56DE\u9000\u8D27\u67DC\u660E\u7EC6\u4E1A\u52A1\u4EF7"
);
assertDeepEqual(
  buildContainerDetailExportRows([
    {
      id: 102,
      hguid: "export-empty",
      warehouseIsActive: void 0
    }
  ]),
  [
    {
      index: 1,
      itemNumber: "",
      barcode: "",
      barcodeImage: "",
      productImage: "",
      productName: "",
      englishName: "",
      containerPieces: 0,
      containerQuantity: 0,
      unitVolume: 0,
      totalVolume: 0,
      middlePackQuantity: 0,
      domesticPrice: 0,
      lastImportPrice: 0,
      lastOEMPrice: 0,
      oemPrice: 0
    }
  ],
  "\u8D27\u67DC\u660E\u7EC6\u5BFC\u51FA\u884C\u7F3A\u5931\u5B57\u6BB5\u65F6\u5E94\u4F7F\u7528\u7A33\u5B9A\u7A7A\u503C\u6216 0\uFF0C\u907F\u514D Excel \u5BFC\u51FA\u62A5\u9519"
);
assertDeepEqual(
  buildContainerDetailExportRows([
    {
      id: 105,
      hguid: "export-volume-detail-first",
      \u88C5\u67DC\u4EF6\u6570: 2,
      \u5355\u4EF6\u4F53\u79EF: 0.25,
      \u5546\u54C1\u4FE1\u606F: { \u5355\u4EF6\u4F53\u79EF: 0.5 }
    },
    {
      id: 106,
      hguid: "export-volume-fallback",
      \u88C5\u67DC\u4EF6\u6570: 3,
      \u5546\u54C1\u4FE1\u606F: { \u5355\u4EF6\u4F53\u79EF: 0.4 }
    }
  ]).map((row) => ({
    index: row.index,
    unitVolume: row.unitVolume,
    totalVolume: row.totalVolume
  })),
  [
    { index: 1, unitVolume: 0.25, totalVolume: 0.5 },
    { index: 2, unitVolume: 0.4, totalVolume: 1.2000000000000002 }
  ],
  "\u8D27\u67DC\u660E\u7EC6\u5BFC\u51FA\u4F53\u79EF\u5E94\u4F18\u5148\u8BFB\u53D6\u660E\u7EC6\u5355\u4EF6\u4F53\u79EF\uFF0C\u5E76\u5728\u5408\u8BA1\u4F53\u79EF\u7F3A\u5931\u65F6\u7528\u4EF6\u6570\u4E58\u5355\u4EF6\u4F53\u79EF\u515C\u5E95"
);
assertEqual(
  getContainerDetailEnglishName(rows[0]),
  "Detail Strawberry",
  "\u82F1\u6587\u540D\u79F0\u5C55\u793A\u5E94\u4F18\u5148\u8BFB\u53D6\u8D27\u67DC\u660E\u7EC6\u5B57\u6BB5"
);
assertEqual(
  getContainerDetailProductType({
    id: 103,
    hguid: "type-domestic-first",
    \u5546\u54C1\u7C7B\u578B: "\u666E\u901A\u5546\u54C1",
    \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5546\u54C1" }
  }),
  "\u5957\u88C5\u5546\u54C1",
  "\u5546\u54C1\u7C7B\u578B\u5E94\u4F18\u5148\u8BFB\u53D6\u56FD\u5185\u5546\u54C1\u8868\u5173\u8054\u4FE1\u606F"
);
assertEqual(
  getContainerDetailProductType({
    id: 104,
    hguid: "type-detail-fallback",
    \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5B50\u5546\u54C1"
  }),
  "\u5957\u88C5\u5B50\u5546\u54C1",
  "\u56FD\u5185\u5546\u54C1\u8868\u7C7B\u578B\u7F3A\u5931\u65F6\u5E94\u56DE\u9000\u8D27\u67DC\u660E\u7EC6\u5546\u54C1\u7C7B\u578B"
);
var categoryRows = [
  {
    id: 151,
    hguid: "category-row-direct",
    \u5546\u54C1\u7F16\u7801: " P001 ",
    categoryName: "Bath",
    categoryPath: "Home / \u5BB6\u5C45 > Bath / \u6D74\u5BA4",
    warehouseCategoryGUID: "cat-bath"
  },
  {
    id: 152,
    hguid: "category-row-info",
    \u5546\u54C1\u4FE1\u606F: {
      \u5546\u54C1\u7F16\u7801: "P002",
      ProductCategoryName: "Kitchen",
      CategoryFullPath: "Home / \u5BB6\u5C45 > Kitchen / \u53A8\u623F",
      ProductCategoryGUID: "cat-kitchen"
    }
  },
  {
    id: 153,
    hguid: "category-row-empty",
    \u5546\u54C1\u7F16\u7801: "P003"
  },
  {
    id: 154,
    hguid: "category-row-duplicate-code",
    \u5546\u54C1\u7F16\u7801: "P001",
    WarehouseCategoryGUID: "cat-bath",
    CategoryName: "Bath"
  },
  {
    id: 157,
    hguid: "category-row-grandchild",
    \u5546\u54C1\u7F16\u7801: "P004",
    \u5546\u54C1\u540D\u79F0: "\u6D74\u5DFE\u5957\u88C5",
    warehouseCategoryGUID: "cat-towels",
    categoryName: "Towels",
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB-P004" }
  },
  {
    id: 158,
    hguid: "category-row-great-grandchild",
    \u5546\u54C1\u7F16\u7801: "P005",
    warehouseCategoryGUID: "cat-small-towels",
    categoryName: "Small Towels"
  },
  {
    id: 159,
    hguid: "category-row-sibling",
    \u5546\u54C1\u7F16\u7801: "P006",
    warehouseCategoryGUID: "cat-kitchen",
    categoryName: "Kitchen"
  },
  {
    id: 160,
    hguid: "category-row-name-only-grandchild",
    \u5546\u54C1\u7F16\u7801: "P007",
    categoryName: "Small Towels"
  }
];
var categoryTree = [
  {
    categoryGUID: "cat-home",
    categoryName: "Home",
    chineseName: "\u5BB6\u5C45",
    isActive: true,
    children: [
      {
        categoryGUID: "cat-bath",
        categoryName: "Bath",
        chineseName: "\u6D74\u5BA4",
        isActive: true,
        children: [
          {
            categoryGUID: "cat-towels",
            categoryName: "Towels",
            chineseName: "\u6BDB\u5DFE",
            isActive: true,
            children: [
              {
                categoryGUID: "cat-small-towels",
                categoryName: "Small Towels",
                chineseName: "\u5C0F\u6BDB\u5DFE",
                isActive: true,
                children: []
              }
            ]
          }
        ]
      },
      {
        categoryGUID: "cat-kitchen",
        categoryName: "Kitchen",
        chineseName: "\u53A8\u623F",
        isActive: true,
        children: []
      }
    ]
  }
];
var categoryLookup = buildWarehouseCategoryLookup(categoryTree);
assertEqual(getContainerDetailCategoryName(categoryRows[0]), "Bath", "\u5206\u7C7B\u540D\u79F0\u5E94\u4F18\u5148\u8BFB\u53D6\u660E\u7EC6\u884C\u5B57\u6BB5");
assertEqual(getContainerDetailCategoryName(categoryRows[1]), "Kitchen", "\u5206\u7C7B\u540D\u79F0\u5E94\u517C\u5BB9\u5546\u54C1\u4FE1\u606F PascalCase \u5B57\u6BB5");
assertEqual(getContainerDetailCategoryPath(categoryRows[1]), "Home / \u5BB6\u5C45 > Kitchen / \u53A8\u623F", "\u5B8C\u6574\u5206\u7C7B\u8DEF\u5F84\u5E94\u517C\u5BB9\u5546\u54C1\u4FE1\u606F CategoryFullPath \u5B57\u6BB5");
assertEqual(getContainerDetailCategoryGuid(categoryRows[1]), "cat-kitchen", "\u5206\u7C7B GUID \u5E94\u517C\u5BB9\u5546\u54C1\u4FE1\u606F ProductCategoryGUID \u5B57\u6BB5");
assertEqual(
  getWarehouseProductCategoryTooltip(getContainerDetailCategoryTooltipRecord({ id: 155, hguid: "category-name-only", categoryName: "Bath" }), buildWarehouseCategoryLookup(categoryTree), "zh"),
  "\u5BB6\u5C45 > \u6D74\u5BA4",
  "\u53EA\u6709\u5206\u7C7B\u540D\u79F0\u65F6\u5E94\u901A\u8FC7\u5206\u7C7B\u6811\u53CD\u67E5\u5F53\u524D\u8BED\u8A00 Tooltip \u5B8C\u6574\u8DEF\u5F84"
);
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY).map((row) => row.hguid),
  [
    "category-row-direct",
    "category-row-info",
    "category-row-empty",
    "category-row-duplicate-code",
    "category-row-grandchild",
    "category-row-great-grandchild",
    "category-row-sibling",
    "category-row-name-only-grandchild"
  ],
  "\u5168\u90E8\u5206\u7C7B\u8FC7\u6EE4\u5E94\u4FDD\u7559\u5F53\u524D\u5DF2\u52A0\u8F7D\u884C"
);
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, CONTAINER_DETAIL_UNCATEGORIZED_FILTER_KEY).map((row) => row.hguid),
  ["category-row-empty"],
  "\u672A\u5206\u7C7B\u8FC7\u6EE4\u5E94\u53EA\u4FDD\u7559\u7F3A\u5C11\u5206\u7C7B\u540D\u79F0\u548C\u5206\u7C7B GUID \u7684\u5F53\u524D\u5DF2\u52A0\u8F7D\u884C"
);
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, "cat-home", categoryLookup).map((row) => row.hguid),
  [
    "category-row-direct",
    "category-row-info",
    "category-row-duplicate-code",
    "category-row-grandchild",
    "category-row-great-grandchild",
    "category-row-sibling",
    "category-row-name-only-grandchild"
  ],
  "\u9009\u62E9\u6839\u5206\u7C7B\u65F6\u5E94\u547D\u4E2D\u81EA\u8EAB\u548C\u6240\u6709\u5C42\u7EA7\u7684\u5B50\u5B59\u5206\u7C7B\u5546\u54C1"
);
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, "cat-bath", categoryLookup).map((row) => row.hguid),
  ["category-row-direct", "category-row-duplicate-code", "category-row-grandchild", "category-row-great-grandchild", "category-row-name-only-grandchild"],
  "\u9009\u62E9\u7236\u5206\u7C7B\u65F6\u5E94\u547D\u4E2D\u81EA\u8EAB\u3001\u5B50\u7C7B\u3001\u5B59\u5B50\u7C7B\u548C\u53EA\u6709\u5206\u7C7B\u540D\u7684\u540E\u4EE3\u5546\u54C1"
);
assertDeepEqual(
  applyContainerDetailCategoryFilter(categoryRows, "cat-towels", categoryLookup).map((row) => row.hguid),
  ["category-row-grandchild", "category-row-great-grandchild", "category-row-name-only-grandchild"],
  "\u9009\u62E9\u5B50\u5206\u7C7B\u65F6\u5E94\u547D\u4E2D\u81EA\u8EAB\u548C\u6240\u6709\u66F4\u6DF1\u5C42\u540E\u4EE3\u5546\u54C1"
);
assertDeepEqual(
  applyContainerDetailLoadedTextFilters(categoryRows, " p004 ", {}).map((row) => row.hguid),
  ["category-row-grandchild"],
  "\u9876\u90E8\u8D27\u53F7\u5173\u952E\u5B57\u5E94\u53EA\u6309\u5F53\u524D\u5DF2\u52A0\u8F7D\u884C\u7684\u8D27\u53F7\u505A\u524D\u7AEF\u5305\u542B\u8FC7\u6EE4"
);
assertDeepEqual(
  applyContainerDetailLoadedTextFilters(categoryRows, "", { itemNumber: "p00", productName: "\u6D74\u5DFE" }).map((row) => row.hguid),
  ["category-row-grandchild"],
  "\u5217\u5934\u6587\u5B57\u641C\u7D22\u5E94\u5728\u524D\u7AEF\u540C\u65F6\u5339\u914D\u8D27\u53F7\u548C\u5546\u54C1\u540D\u79F0\u7B49\u6587\u672C\u5217"
);
assertDeepEqual(
  getContainerDetailBatchCategoryProductCodes(categoryRows),
  { productCodes: ["P001", "P002", "P003", "P004", "P005", "P006", "P007"], skippedMissingCodeCount: 0 },
  "\u6279\u91CF\u5206\u7C7B\u5E94\u63D0\u53D6 trim \u540E\u5546\u54C1\u7F16\u7801\u5E76\u53BB\u91CD"
);
assertDeepEqual(
  getContainerDetailBatchCategoryProductCodes([{ id: 156, hguid: "missing-code" }, ...categoryRows.slice(0, 1)]),
  { productCodes: ["P001"], skippedMissingCodeCount: 1 },
  "\u6279\u91CF\u5206\u7C7B\u5E94\u8DF3\u8FC7\u7F3A\u5546\u54C1\u7F16\u7801\u884C\u5E76\u7EDF\u8BA1\u6570\u91CF"
);
assertDeepEqual(
  buildContainerDetailSaveFailureKeys("row-1", { \u5546\u54C1\u540D\u79F0: "\u76AE\u5E26" }),
  ["row-1:\u5546\u54C1\u540D\u79F0"],
  "\u660E\u7EC6\u4FDD\u5B58\u5931\u8D25 key \u5E94\u533A\u5206\u5546\u54C1\u540D\u79F0\u5B57\u6BB5\uFF0C\u907F\u514D\u88AB\u540C\u4E00\u884C\u5176\u5B83\u4FDD\u5B58\u6E05\u9664"
);
assertDeepEqual(
  buildContainerDetailSaveFailureKeys("row-1", { \u5907\u6CE8: "\u5DF2\u786E\u8BA4" }),
  ["row-1:\u5907\u6CE8"],
  "\u540C\u4E00\u884C\u5907\u6CE8\u4FDD\u5B58\u5E94\u4F7F\u7528\u72EC\u7ACB\u5931\u8D25 key\uFF0C\u4E0D\u80FD\u6E05\u9664\u5546\u54C1\u540D\u79F0\u4FDD\u5B58\u5931\u8D25\u72B6\u6001"
);
assertDeepEqual(
  buildContainerDetailTranslationUpdates(rows, {
    \u660E\u7EC6\u5927\u8349\u8393: "Large Strawberry",
    TPR\u9CA8\u9C7C: "TPR Shark Toy"
  }),
  [
    { hguid: "detail-1", \u82F1\u6587\u540D\u79F0: "Large Strawberry" },
    { hguid: "detail-2", \u82F1\u6587\u540D\u79F0: "TPR Shark Toy" }
  ],
  "\u6279\u91CF\u7FFB\u8BD1\u5E94\u7528\u660E\u7EC6\u4E2D\u6587\u540D\u4F18\u5148\u751F\u6210\u53EF\u4FDD\u5B58\u7684\u82F1\u6587\u540D\u79F0\u66F4\u65B0"
);
var englishNameChineseRows = [
  {
    id: 3,
    hguid: "detail-3",
    \u5546\u54C1\u540D\u79F0: "\u5546\u54C1\u4E2D\u6587\u540D\u4E0D\u5E94\u4F18\u5148",
    \u82F1\u6587\u540D\u79F0: "\u8349\u8393\u73A9\u5177"
  }
];
assertEqual(
  getContainerDetailTranslationSource(englishNameChineseRows[0]),
  "\u8349\u8393\u73A9\u5177",
  "\u82F1\u6587\u540D\u79F0\u4ECD\u542B\u4E2D\u6587\u65F6\u5E94\u4F18\u5148\u4F5C\u4E3A\u7FFB\u8BD1\u6E90"
);
assertDeepEqual(
  buildContainerDetailTranslationUpdates(englishNameChineseRows, {
    \u8349\u8393\u73A9\u5177: "Strawberry Toy",
    \u5546\u54C1\u4E2D\u6587\u540D\u4E0D\u5E94\u4F18\u5148: "Wrong Source"
  }),
  [
    { hguid: "detail-3", \u82F1\u6587\u540D\u79F0: "Strawberry Toy" }
  ],
  "\u82F1\u6587\u540D\u79F0\u4E3A\u4E2D\u6587\u65F6\u6279\u91CF\u7FFB\u8BD1\u5E94\u7FFB\u8BD1\u82F1\u6587\u540D\u79F0\u5B57\u6BB5\u672C\u8EAB"
);
assertDeepEqual(
  buildContainerDetailTranslationUpdates(rows, {
    \u660E\u7EC6\u5927\u8349\u8393: "Large \u8349\u8393",
    TPR\u9CA8\u9C7C: "TPR Shark Toy"
  }),
  [
    { hguid: "detail-2", \u82F1\u6587\u540D\u79F0: "TPR Shark Toy" }
  ],
  "\u6279\u91CF\u7FFB\u8BD1\u5E94\u8DF3\u8FC7\u4ECD\u5305\u542B\u4E2D\u6587\u7684\u82F1\u6587\u540D\u79F0\u7ED3\u679C"
);
assertEqual(
  countContainerDetailInvalidTranslationResults(rows, {
    \u660E\u7EC6\u5927\u8349\u8393: "Large \u8349\u8393",
    TPR\u9CA8\u9C7C: "TPR Shark Toy"
  }),
  1,
  "\u6279\u91CF\u7FFB\u8BD1\u5E94\u7EDF\u8BA1\u4ECD\u5305\u542B\u4E2D\u6587\u7684\u8DF3\u8FC7\u7ED3\u679C"
);
var updatedRows = applyContainerDetailEnglishNameUpdates(rows, [
  { hguid: "detail-1", \u82F1\u6587\u540D\u79F0: "Large Strawberry" }
]);
assertEqual(updatedRows[0].\u82F1\u6587\u540D\u79F0, "Large Strawberry", "\u672C\u5730\u884C\u5E94\u5199\u5165\u660E\u7EC6\u7EA7\u82F1\u6587\u540D\u79F0");
assertEqual(updatedRows[0].\u5546\u54C1\u4FE1\u606F?.\u82F1\u6587\u540D\u79F0, "Large Strawberry", "\u672C\u5730\u884C\u5E94\u540C\u6B65\u5546\u54C1\u4FE1\u606F\u82F1\u6587\u540D\u79F0\u7528\u4E8E\u5C55\u793A\u515C\u5E95");
assertEqual(updatedRows[1].\u5546\u54C1\u4FE1\u606F?.\u82F1\u6587\u540D\u79F0, "TPR Shark", "\u672A\u547D\u4E2D\u7684\u884C\u5E94\u4FDD\u6301\u539F\u503C");
assertDeepEqual(
  buildContainerDetailEnglishNameUpdates(rows, "  Unified English Name  "),
  [
    { hguid: "detail-1", \u82F1\u6587\u540D\u79F0: "Unified English Name" },
    { hguid: "detail-2", \u82F1\u6587\u540D\u79F0: "Unified English Name" }
  ],
  "\u6279\u91CF\u4FEE\u6539\u82F1\u6587\u540D\u79F0\u5E94\u4E3A\u6240\u6709\u6709\u6548\u660E\u7EC6\u751F\u6210\u7EDF\u4E00\u4E14\u53BB\u7A7A\u683C\u7684\u82F1\u6587\u540D\u79F0"
);
assertDeepEqual(
  buildContainerDetailEnglishNameUpdates(rows, "Unified \u8349\u8393 Name"),
  [],
  "\u624B\u52A8\u6279\u91CF\u4FEE\u6539\u82F1\u6587\u540D\u79F0\u5E94\u62D2\u7EDD\u4ECD\u5305\u542B\u4E2D\u6587\u7684\u8F93\u5165"
);
assertDeepEqual(
  buildContainerDetailClearEnglishNameUpdates(rows),
  [
    { hguid: "detail-1", ClearEnglishName: true },
    { hguid: "detail-2", ClearEnglishName: true }
  ],
  "\u6E05\u9664\u82F1\u6587\u540D\u79F0\u5E94\u4E3A\u6240\u6709\u6709\u6548\u660E\u7EC6\u751F\u6210\u660E\u786E\u6E05\u7A7A\u6807\u8BB0"
);
var clearedRows = applyContainerDetailEnglishNameUpdates(rows, [
  { hguid: "detail-1", \u82F1\u6587\u540D\u79F0: void 0 }
]);
assertEqual(clearedRows[0].\u82F1\u6587\u540D\u79F0, void 0, "\u6E05\u9664\u540E\u672C\u5730\u884C\u660E\u7EC6\u7EA7\u82F1\u6587\u540D\u79F0\u5E94\u4E3A\u7A7A");
assertEqual(clearedRows[0].\u5546\u54C1\u4FE1\u606F?.\u82F1\u6587\u540D\u79F0, void 0, "\u6E05\u9664\u540E\u672C\u5730\u884C\u5546\u54C1\u4FE1\u606F\u82F1\u6587\u540D\u79F0\u5E94\u4E3A\u7A7A");
var editableRowKeys = ["row-1", "row-2", "row-3"];
var defaultPageColumnOrder = [
  "index",
  "image",
  "itemNumber",
  "englishName",
  "categoryName",
  "containerPieces",
  "packingQuantity",
  "containerQuantity",
  "unitVolume",
  "domesticPrice",
  "transportCost",
  "unitTransportCost",
  "floatRate",
  "middlePackQuantity",
  "warehouseImportPrice",
  "importPrice",
  "oemPrice",
  "lastOEMPrice",
  "newProduct",
  "productType",
  "matchType",
  "barcode",
  "productName",
  "warehouseStatus",
  "remark"
];
var editableColumnWhitelist = ["englishName", "packingQuantity", "unitVolume", "middlePackQuantity", "floatRate", "importPrice", "oemPrice", "remark"];
var editableColumnKeys = getContainerDetailEditableColumnKeysInOrder(defaultPageColumnOrder, editableColumnWhitelist);
assertDeepEqual(
  editableColumnKeys,
  ["englishName", "packingQuantity", "unitVolume", "floatRate", "middlePackQuantity", "importPrice", "oemPrice", "remark"],
  "\u9ED8\u8BA4\u9875\u9762\u5217\u987A\u5E8F\u5E94\u51B3\u5B9A\u65B9\u5411\u952E\u53EF\u7F16\u8F91\u5217\u987A\u5E8F"
);
assertDeepEqual(
  getContainerDetailEditableColumnKeysInOrder(
    ["remark", "warehouseStatus", "oemPrice", "floatRate", "englishName"],
    editableColumnWhitelist
  ),
  ["remark", "oemPrice", "floatRate", "englishName"],
  "\u81EA\u5B9A\u4E49\u5217\u987A\u5E8F\u5E94\u51B3\u5B9A\u65B9\u5411\u952E\u53EF\u7F16\u8F91\u5217\u987A\u5E8F"
);
assertDeepEqual(
  getNextContainerDetailEditableCell("row-2", "floatRate", editableRowKeys, editableColumnKeys, "up"),
  { rowKey: "row-1", columnKey: "floatRate" },
  "\u65B9\u5411\u952E\u4E0A\u5E94\u79FB\u52A8\u5230\u4E0A\u4E00\u884C\u540C\u4E00\u7F16\u8F91\u5217"
);
assertDeepEqual(
  getNextContainerDetailEditableCell("row-2", "floatRate", editableRowKeys, editableColumnKeys, "down"),
  { rowKey: "row-3", columnKey: "floatRate" },
  "\u65B9\u5411\u952E\u4E0B\u5E94\u79FB\u52A8\u5230\u4E0B\u4E00\u884C\u540C\u4E00\u7F16\u8F91\u5217"
);
assertDeepEqual(
  getNextContainerDetailEditableCell("row-2", "floatRate", editableRowKeys, editableColumnKeys, "left"),
  { rowKey: "row-2", columnKey: "unitVolume" },
  "\u65B9\u5411\u952E\u5DE6\u5E94\u79FB\u52A8\u5230\u540C\u4E00\u884C\u9875\u9762\u987A\u5E8F\u7684\u524D\u4E00\u4E2A\u7F16\u8F91\u5217"
);
assertDeepEqual(
  getNextContainerDetailEditableCell("row-2", "floatRate", editableRowKeys, editableColumnKeys, "right"),
  { rowKey: "row-2", columnKey: "middlePackQuantity" },
  "\u65B9\u5411\u952E\u53F3\u5E94\u79FB\u52A8\u5230\u540C\u4E00\u884C\u9875\u9762\u987A\u5E8F\u7684\u540E\u4E00\u4E2A\u7F16\u8F91\u5217"
);
assertEqual(
  getNextContainerDetailEditableCell("row-1", "englishName", editableRowKeys, editableColumnKeys, "up"),
  null,
  "\u7B2C\u4E00\u884C\u6309\u4E0A\u4E0D\u5E94\u79FB\u52A8"
);
assertEqual(
  getNextContainerDetailEditableCell("row-3", "remark", editableRowKeys, editableColumnKeys, "down"),
  null,
  "\u6700\u540E\u4E00\u884C\u6309\u4E0B\u4E0D\u5E94\u79FB\u52A8"
);
assertEqual(
  getNextContainerDetailEditableCell("row-2", "englishName", editableRowKeys, editableColumnKeys, "left"),
  null,
  "\u9996\u4E2A\u7F16\u8F91\u5217\u6309\u5DE6\u4E0D\u5E94\u79FB\u52A8"
);
assertEqual(
  getNextContainerDetailEditableCell("row-2", "remark", editableRowKeys, editableColumnKeys, "right"),
  null,
  "\u6700\u540E\u4E00\u4E2A\u7F16\u8F91\u5217\u6309\u53F3\u4E0D\u5E94\u79FB\u52A8"
);
assertEqual(
  getNextContainerDetailEditableCell("missing-row", "floatRate", editableRowKeys, editableColumnKeys, "down"),
  null,
  "\u5F53\u524D\u884C\u4E0D\u5B58\u5728\u65F6\u4E0D\u5E94\u79FB\u52A8"
);
assertEqual(
  getNextContainerDetailEditableCell("row-2", "missing-column", editableRowKeys, editableColumnKeys, "right"),
  null,
  "\u5F53\u524D\u5217\u4E0D\u5B58\u5728\u65F6\u4E0D\u5E94\u79FB\u52A8"
);
var tagRows = [
  { id: 31, hguid: "tag-31", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 0, \u8FDB\u53E3\u4EF7\u683C: 1, warehouseIsActive: true },
  { id: 32, hguid: "tag-32", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 0, warehouseOEMPrice: 2, \u8FDB\u53E3\u4EF7\u683C: 0, warehouseIsActive: false, \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5546\u54C1" } },
  { id: 33, hguid: "tag-33", \u662F\u5426\u65B0\u5546\u54C1: false, \u8D34\u724C\u4EF7\u683C: 3, \u8FDB\u53E3\u4EF7\u683C: 4, warehouseIsActive: true, \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7C7B\u578B: "\u591A\u7801\u5546\u54C1" } },
  { id: 34, hguid: "tag-34", \u662F\u5426\u65B0\u5546\u54C1: false, \u8D34\u724C\u4EF7\u683C: 0, \u8FDB\u53E3\u4EF7\u683C: void 0, warehouseIsActive: void 0, \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5B50\u5546\u54C1" }
];
assertDeepEqual(
  buildContainerDetailTagStats(tagRows),
  {
    all: 4,
    new: 2,
    existing: 2,
    noOemPrice: 2,
    abnormalImport: 2,
    active: 2,
    inactive: 2,
    normal: 1,
    set: 1,
    multi: 1,
    setChild: 1
  },
  "\u7EDF\u8BA1\u680F\u5E94\u6309\u5F53\u524D\u57FA\u7840\u7ED3\u679C\u7EDF\u8BA1\u5168\u90E8\u3001\u65B0\u5546\u54C1\u3001\u5DF2\u6709\u5546\u54C1\u3001\u7F3A\u96F6\u552E\u4EF7\u3001\u8FDB\u53E3\u4EF7\u5F02\u5E38\u3001\u4E0A\u4E0B\u67B6\u548C\u5546\u54C1\u7C7B\u578B\u6570\u91CF"
);
assertEqual(matchesContainerDetailTagFilter(tagRows[0], "new"), true, "\u65B0\u5546\u54C1 tag \u5E94\u5339\u914D\u662F\u5426\u65B0\u5546\u54C1\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[2], "new"), false, "\u65B0\u5546\u54C1 tag \u4E0D\u5E94\u5339\u914D\u5DF2\u6709\u5546\u54C1\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[0], "noOemPrice"), true, "\u7F3A\u96F6\u552E\u4EF7\u53EA\u7EDF\u8BA1\u65B0\u5546\u54C1\u4E14\u6709\u6548\u96F6\u552E\u4EF7\u4E3A\u7A7A\u6216\u4E0D\u5927\u4E8E 0 \u7684\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[1], "noOemPrice"), true, "\u660E\u7EC6\u96F6\u552E\u4EF7\u4E3A\u7A7A\u65F6\u5E94\u8FDB\u5165\u7F3A\u96F6\u552E\u4EF7 tag\uFF0C\u4ED3\u5E93\u5FEB\u7167\u4E0D\u518D\u515C\u5E95");
assertEqual(matchesContainerDetailTagFilter(tagRows[3], "noOemPrice"), false, "\u5DF2\u6709\u5546\u54C1\u7F3A\u96F6\u552E\u4EF7\u4E0D\u8FDB\u5165\u7F3A\u96F6\u552E\u4EF7 tag");
assertEqual(matchesContainerDetailTagFilter(tagRows[1], "abnormalImport"), true, "\u8FDB\u53E3\u4EF7\u4E3A 0 \u5E94\u8FDB\u5165\u8FDB\u53E3\u4EF7\u5F02\u5E38 tag");
assertEqual(matchesContainerDetailTagFilter(tagRows[2], "all"), true, "\u5168\u90E8 tag \u5E94\u5339\u914D\u6240\u6709\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[2], "active"), true, "\u4E0A\u67B6 tag \u5E94\u5339\u914D warehouseIsActive \u4E3A true \u7684\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[3], "inactive"), true, "\u4E0B\u67B6 tag \u5E94\u5339\u914D warehouseIsActive \u975E true \u7684\u884C");
assertEqual(matchesContainerDetailTagFilter(tagRows[1], "set"), true, "\u5957\u88C5\u5546\u54C1\u7EDF\u8BA1 tag \u5E94\u5339\u914D\u56FD\u5185\u5546\u54C1\u8868\u7C7B\u578B");
assertEqual(matchesContainerDetailTagFilter(tagRows[2], "multi"), true, "\u591A\u7801\u5546\u54C1\u7EDF\u8BA1 tag \u5E94\u5339\u914D\u591A\u7801\u7C7B\u578B");
assertEqual(matchesContainerDetailTagFilter(tagRows[3], "setChild"), true, "\u5957\u88C5\u5B50\u5546\u54C1\u7EDF\u8BA1 tag \u5E94\u5339\u914D\u660E\u7EC6\u515C\u5E95\u7C7B\u578B");
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], []), true, "\u672A\u9009\u62E9 tag \u65F6\u5E94\u663E\u793A\u5168\u90E8\u884C");
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ["new", "inactive"]), true, "\u65B0\u5546\u54C1\u4E0E\u4E0B\u67B6\u5C5E\u4E8E\u4E0D\u540C\u5206\u7EC4\uFF0C\u5E94\u540C\u65F6\u6EE1\u8DB3");
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], ["new", "inactive"]), false, "\u65B0\u5546\u54C1\u4F46\u5DF2\u4E0A\u67B6\u65F6\u4E0D\u5E94\u547D\u4E2D\u65B0\u5546\u54C1\u52A0\u4E0B\u67B6\u7EC4\u5408");
assertEqual(matchesContainerDetailSelectedTags(tagRows[2], ["new", "existing"]), true, "\u65B0\u5546\u54C1\u548C\u5DF2\u6709\u5546\u54C1\u540C\u7EC4\u591A\u9009\u5E94\u6309 OR \u5339\u914D");
assertEqual(matchesContainerDetailSelectedTags(tagRows[2], ["set", "multi"]), true, "\u5546\u54C1\u7C7B\u578B\u7EDF\u8BA1 tag \u540C\u7EC4\u591A\u9009\u5E94\u6309 OR \u5339\u914D");
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ["multi", "inactive"]), false, "\u5546\u54C1\u7C7B\u578B\u672A\u547D\u4E2D\u65F6\u5373\u4F7F\u4E0A\u4E0B\u67B6\u547D\u4E2D\u4E5F\u5E94\u88AB\u8FC7\u6EE4");
assertEqual(matchesContainerDetailSelectedTags(tagRows[3], ["noOemPrice", "abnormalImport"]), true, "\u5F02\u5E38\u7C7B tag \u540C\u7EC4\u591A\u9009\u5E94\u6309 OR \u5339\u914D");
assertEqual(matchesContainerDetailSelectedTags(tagRows[1], ["noOemPrice", "abnormalImport", "inactive"]), true, "\u5F02\u5E38\u7C7B OR \u540E\u5E94\u7EE7\u7EED\u4E0E\u4E0A\u4E0B\u67B6\u5206\u7EC4 AND");
assertEqual(matchesContainerDetailSelectedTags(tagRows[0], ["noOemPrice", "abnormalImport", "inactive"]), false, "\u547D\u4E2D\u5F02\u5E38\u7C7B\u4F46\u672A\u547D\u4E2D\u4E0B\u67B6\u65F6\u5E94\u88AB\u8FC7\u6EE4");
var columnStateRows = [
  {
    id: 201,
    hguid: "column-201",
    \u5546\u54C1\u540D\u79F0: "\u660E\u7EC6\u5851\u6599\u676F\u94A9",
    \u82F1\u6587\u540D\u79F0: "Plastic Cup Hook",
    \u5546\u54C1\u7C7B\u578B: "\u666E\u901A\u5546\u54C1",
    \u662F\u5426\u65B0\u5546\u54C1: false,
    matchType: "productCode",
    \u88C5\u67DC\u4EF6\u6570: 8,
    \u4E2D\u5305\u6570: 12,
    \u88C5\u67DC\u6570\u91CF: 1152,
    \u56FD\u5185\u4EF7\u683C: 12,
    \u8C03\u6574\u6D6E\u7387: 1.2,
    \u8FD0\u8F93\u6210\u672C: 0.35,
    \u8FDB\u53E3\u4EF7\u683C: 3.22,
    \u8D34\u724C\u4EF7\u683C: 3.88,
    warehouseOEMPrice: 4.5,
    \u5907\u6CE8: "\u7B2C\u4E00\u884C\u5907\u6CE8",
    warehouseIsActive: true,
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "8101733", \u6761\u5F62\u7801: "8052533117337", \u5546\u54C1\u540D\u79F0: "\u5546\u54C1\u5851\u6599\u676F\u94A9" }
  },
  {
    id: 202,
    hguid: "column-202",
    \u5546\u54C1\u540D\u79F0: "\u9B54\u65B9\u73E0\u5B50",
    \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5546\u54C1",
    \u662F\u5426\u65B0\u5546\u54C1: true,
    matchType: "unmatched",
    \u88C5\u67DC\u4EF6\u6570: 30,
    \u4E2D\u5305\u6570: 0,
    \u88C5\u67DC\u6570\u91CF: 4320,
    \u56FD\u5185\u4EF7\u683C: 0,
    \u8C03\u6574\u6D6E\u7387: 1.1,
    \u8FD0\u8F93\u6210\u672C: void 0,
    \u8FDB\u53E3\u4EF7\u683C: 0,
    \u8D34\u724C\u4EF7\u683C: 0,
    \u5907\u6CE8: "\u9700\u8981\u8865\u4EF7\u683C",
    warehouseIsActive: false,
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB386-013", \u6761\u5F62\u7801: "9527938600047", \u82F1\u6587\u540D\u79F0: "Cube Beads", \u5546\u54C1\u7C7B\u578B: "\u666E\u901A\u5546\u54C1" }
  },
  {
    id: 203,
    hguid: "column-203",
    \u5546\u54C1\u7C7B\u578B: "\u5957\u88C5\u5B50\u5546\u54C1",
    \u662F\u5426\u65B0\u5546\u54C1: false,
    matchType: "supplierItem",
    \u88C5\u67DC\u4EF6\u6570: 2,
    \u4E2D\u5305\u6570: void 0,
    \u88C5\u67DC\u6570\u91CF: 480,
    \u56FD\u5185\u4EF7\u683C: 5.5,
    \u8C03\u6574\u6D6E\u7387: void 0,
    \u8FD0\u8F93\u6210\u672C: 0.12,
    \u8FDB\u53E3\u4EF7\u683C: 1.45,
    \u8D34\u724C\u4EF7\u683C: 2.01,
    warehouseOEMPrice: 2.01,
    warehouseIsActive: void 0,
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "8104032", \u6761\u5F62\u7801: "8052533140328", \u5546\u54C1\u540D\u79F0: "\u4E09\u89D2\u652F\u67B6", \u82F1\u6587\u540D\u79F0: "Triangle Bracket" }
  }
];
function columnState(filters, sortState) {
  return applyContainerDetailColumnState(columnStateRows, filters, sortState).map((row) => row.hguid);
}
assertDeepEqual(columnState({ itemNumber: " hb386 " }), ["column-202"], "\u8D27\u53F7\u5217\u5934\u6587\u672C\u8FC7\u6EE4\u5E94\u5FFD\u7565\u5927\u5C0F\u5199\u5E76\u5305\u542B\u5339\u914D");
assertDeepEqual(columnState({ barcode: "3140328" }), ["column-203"], "\u6761\u7801\u5217\u5934\u6587\u672C\u8FC7\u6EE4\u5E94\u652F\u6301\u5C40\u90E8\u5339\u914D");
assertDeepEqual(columnState({ productName: "\u5851\u6599\u676F" }), ["column-201"], "\u5546\u54C1\u540D\u79F0\u5217\u5934\u8FC7\u6EE4\u5E94\u4F18\u5148\u8BFB\u53D6\u660E\u7EC6\u540D\u79F0\u5E76\u652F\u6301\u4E2D\u6587\u5305\u542B\u5339\u914D");
assertDeepEqual(columnState({ englishName: "triangle" }), ["column-203"], "\u82F1\u6587\u540D\u79F0\u5217\u5934\u8FC7\u6EE4\u5E94\u8BFB\u53D6\u5546\u54C1\u4FE1\u606F\u515C\u5E95\u5E76\u5FFD\u7565\u5927\u5C0F\u5199");
assertDeepEqual(columnState({ remark: "\u8865\u4EF7\u683C" }), ["column-202"], "\u5907\u6CE8\u5217\u5934\u8FC7\u6EE4\u5E94\u652F\u6301\u6587\u672C\u5305\u542B\u5339\u914D");
assertDeepEqual(columnState({ productTypes: ["set", "setChild"] }), ["column-203"], "\u5546\u54C1\u7C7B\u578B\u5217\u5934\u8FC7\u6EE4\u5E94\u4F18\u5148\u8BFB\u53D6\u56FD\u5185\u5546\u54C1\u8868\u7C7B\u578B\u5E76\u652F\u6301\u591A\u9009\u679A\u4E3E");
assertDeepEqual(columnState({ productTypes: ["normal"] }), ["column-201", "column-202"], "\u56FD\u5185\u5546\u54C1\u8868\u7C7B\u578B\u8986\u76D6\u660E\u7EC6\u65E7\u7C7B\u578B\u65F6\u5E94\u6309\u56FD\u5185\u5546\u54C1\u8868\u7C7B\u578B\u8FC7\u6EE4");
assertEqual(getContainerDetailProductTypeFilterKey({ id: 204, hguid: "column-204", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7C7B\u578B: "\u591A\u7801\u5546\u54C1" } }), "multi", "\u5546\u54C1\u7C7B\u578B\u8FC7\u6EE4\u952E\u5E94\u652F\u6301\u591A\u7801\u5546\u54C1");
assertDeepEqual(columnState({ newProductStates: ["new"] }), ["column-202"], "\u65B0\u5546\u54C1\u5217\u5934\u8FC7\u6EE4\u5E94\u652F\u6301\u7B5B\u51FA\u65B0\u5546\u54C1");
assertDeepEqual(columnState({ matchTypes: ["supplierItem"] }), ["column-203"], "\u5339\u914D\u65B9\u5F0F\u5217\u5934\u8FC7\u6EE4\u5E94\u652F\u6301\u4F9B\u5E94\u5546\u7F16\u7801\u52A0\u8D27\u53F7\u5339\u914D");
assertDeepEqual(columnState({ warehouseStatus: ["inactive"] }), ["column-202", "column-203"], "\u4ED3\u5E93\u72B6\u6001\u5217\u5934\u8FC7\u6EE4\u5E94\u628A\u975E true \u89C6\u4E3A\u4E0B\u67B6");
assertDeepEqual(columnState({ middlePackQuantity: { min: 1, max: 20 } }), ["column-201"], "\u4E2D\u5305\u6570\u5217\u5934\u8303\u56F4\u8FC7\u6EE4\u5E94\u8BFB\u53D6\u4ED3\u5E93\u5546\u54C1\u6700\u5C0F\u8BA2\u8D27\u91CF");
assertDeepEqual(columnState({ containerQuantity: { min: 500, max: 2e3 } }), ["column-201"], "\u88C5\u67DC\u6570\u91CF\u5217\u5934\u8303\u56F4\u8FC7\u6EE4\u5E94\u540C\u65F6\u652F\u6301\u6700\u5C0F\u503C\u548C\u6700\u5927\u503C");
assertDeepEqual(columnState({ domesticPrice: { min: 0, max: 0 } }), ["column-202"], "\u6570\u5B57\u5217\u5934\u8303\u56F4\u8FC7\u6EE4\u5E94\u6B63\u786E\u5339\u914D 0 \u503C");
assertDeepEqual(columnState({ transportCost: { min: 0 } }), ["column-201", "column-203"], "\u6570\u5B57\u5217\u5934\u8303\u56F4\u8FC7\u6EE4\u5E94\u6392\u9664\u7A7A\u503C");
assertDeepEqual(columnState({ oemPrice: { min: 4, max: 5 } }), ["column-201"], "\u96F6\u552E\u4EF7\u5217\u5934\u8303\u56F4\u8FC7\u6EE4\u5E94\u8BFB\u53D6\u8868\u683C\u53EF\u89C1\u96F6\u552E\u4EF7");
assertDeepEqual(columnState({ oemPrice: { min: 2 } }, { field: "containerPieces", order: "ascend" }), ["column-203", "column-201"], "\u5217\u5934\u8FC7\u6EE4\u540E\u6392\u5E8F\u5E94\u53EA\u4F5C\u7528\u4E8E\u8FC7\u6EE4\u540E\u7684\u53EF\u89C1\u884C");
assertDeepEqual(columnState({}, { field: "itemNumber", order: "ascend" }), ["column-201", "column-203", "column-202"], "\u8D27\u53F7\u6392\u5E8F\u5E94\u6309\u6587\u672C\u5347\u5E8F\u4E14\u4FDD\u6301\u7A33\u5B9A\u8F93\u51FA");
assertDeepEqual(
  applyContainerDetailColumnState([
    { id: 211, hguid: "sort-211", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB2" } },
    { id: 212, hguid: "sort-212", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "" } },
    { id: 213, hguid: "sort-213", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB10" } },
    { id: 214, hguid: "sort-214", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB2" } }
  ], {}, { field: "itemNumber", order: "ascend" }).map((row) => row.hguid),
  ["sort-211", "sort-214", "sort-213", "sort-212"],
  "\u8D27\u53F7\u5347\u5E8F\u5E94\u6309\u81EA\u7136\u6392\u5E8F\u3001\u7A7A\u8D27\u53F7\u6392\u6700\u540E\uFF0C\u5E76\u4FDD\u6301\u76F8\u540C\u8D27\u53F7\u539F\u59CB\u987A\u5E8F"
);
assertDeepEqual(columnState({}, { field: "transportCost", order: "ascend" }), ["column-203", "column-201", "column-202"], "\u6570\u5B57\u6392\u5E8F\u5E94\u628A\u7A7A\u503C\u6392\u5728\u6700\u540E");
assertDeepEqual(columnState({}, { field: "warehouseStatus", order: "descend" }), ["column-201", "column-202", "column-203"], "\u4ED3\u5E93\u72B6\u6001\u6392\u5E8F\u5E94\u652F\u6301\u4E0A\u67B6\u4F18\u5148\u4E14\u540C\u503C\u4FDD\u6301\u539F\u59CB\u987A\u5E8F");
assertDeepEqual(columnState({}, { field: "matchType", order: "ascend" }), ["column-201", "column-203", "column-202"], "\u5339\u914D\u65B9\u5F0F\u6392\u5E8F\u5E94\u6309\u5546\u54C1\u7F16\u7801\u3001\u4F9B\u5E94\u5546\u8D27\u53F7\u3001\u672A\u5339\u914D\u7A33\u5B9A\u6392\u5E8F");
assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: "CONTAINER-QUERY",
    filters: {
      itemNumber: " HB308 ",
      barcode: "",
      productName: " \u68B3\u5B50 ",
      englishName: " Grooming ",
      remark: " \u9700\u786E\u8BA4 ",
      productTypes: ["normal", "set"],
      newProductStates: ["new"],
      matchTypes: ["productCode", "supplierItem"],
      warehouseStatus: ["active"],
      containerPieces: { min: 1, max: 8 },
      middlePackQuantity: { min: 1, max: 24 },
      containerQuantity: { min: 0, max: 1200 },
      domesticPrice: { min: 2.5 },
      floatRate: { max: 1.3 },
      transportCost: { min: 0 },
      warehouseImportPrice: { max: 9.99 },
      importPrice: { min: 1.11, max: 3.33 },
      oemPrice: { min: 4.44, max: 5.55 }
    },
    sortState: { field: "itemNumber", order: "ascend" },
    pageNumber: 3,
    pageSize: 80
  }),
  {
    containerGuid: "CONTAINER-QUERY",
    pageNumber: 3,
    pageSize: 80,
    itemNumber: "HB308",
    productName: "\u68B3\u5B50",
    englishName: "Grooming",
    remark: "\u9700\u786E\u8BA4",
    productTypes: ["normal", "set"],
    newProductStates: ["new"],
    matchTypes: ["productCode", "supplierItem"],
    warehouseStatus: ["active"],
    containerPiecesMin: 1,
    containerPiecesMax: 8,
    middlePackQuantityMin: 1,
    middlePackQuantityMax: 24,
    containerQuantityMin: 0,
    containerQuantityMax: 1200,
    domesticPriceMin: 2.5,
    floatRateMax: 1.3,
    transportCostMin: 0,
    warehouseImportPriceMax: 9.99,
    importPriceMin: 1.11,
    importPriceMax: 3.33,
    oemPriceMin: 4.44,
    oemPriceMax: 5.55,
    sortBy: "itemNumber",
    sortOrder: "ascend"
  },
  "\u8FDC\u7A0B\u67E5\u8BE2\u53C2\u6570\u5E94\u7531\u5217\u7B5B\u9009\u3001\u6392\u5E8F\u548C\u5206\u9875\u72B6\u6001\u751F\u6210\uFF0C\u5E76\u4E14\u4E0D\u518D\u63D0\u4EA4\u6807\u7B7E\u5B57\u6BB5"
);
assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: "CONTAINER-NO-SORT",
    filters: { barcode: " 9300 " },
    pageNumber: 1,
    pageSize: 50
  }),
  {
    containerGuid: "CONTAINER-NO-SORT",
    pageNumber: 1,
    pageSize: 50,
    barcode: "9300"
  },
  "\u8FDC\u7A0B\u67E5\u8BE2\u53C2\u6570\u6CA1\u6709\u6392\u5E8F\u548C tag \u65F6\u4E0D\u5E94\u63D0\u4EA4\u7A7A\u5B57\u6BB5"
);
assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: "CONTAINER-APPEND",
    filters: {},
    pageNumber: 2,
    pageSize: 50,
    includeTotal: false,
    includeStats: false
  }),
  {
    containerGuid: "CONTAINER-APPEND",
    pageNumber: 2,
    pageSize: 50,
    includeTotal: false,
    includeStats: false
  },
  "\u8FFD\u52A0\u9875\u67E5\u8BE2\u5E94\u5141\u8BB8\u663E\u5F0F\u8DF3\u8FC7 total \u548C\u6807\u7B7E\u7EDF\u8BA1"
);
assertDeepEqual(
  buildContainerDetailQuery({
    containerGuid: "CONTAINER-TYPE-TAGS",
    filters: { productTypes: ["normal"] },
    pageNumber: 1,
    pageSize: 50
  }),
  {
    containerGuid: "CONTAINER-TYPE-TAGS",
    pageNumber: 1,
    pageSize: 50,
    productTypes: ["normal"]
  },
  "\u5546\u54C1\u7C7B\u578B\u7EDF\u8BA1 tag \u4E0D\u518D\u5408\u5E76\u5230\u8FDC\u7A0B productTypes\uFF0C\u53EA\u6709\u5217\u5934\u5546\u54C1\u7C7B\u578B\u7B5B\u9009\u8FDB\u5165\u540E\u7AEF"
);
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: "base-query",
    baseQueryKey: "base-query",
    loadedRowsLength: 83,
    itemsTotal: 83,
    hasMore: false,
    loading: false,
    loadingMore: false
  }),
  true,
  "\u5F53\u524D\u975E\u6807\u7B7E\u67E5\u8BE2\u5DF2\u5168\u91CF\u52A0\u8F7D\u65F6\u5E94\u5141\u8BB8\u524D\u7AEF\u6807\u7B7E\u8FC7\u6EE4"
);
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: "base-query",
    baseQueryKey: "base-query",
    loadedRowsLength: 50,
    itemsTotal: 83,
    hasMore: true,
    loading: false,
    loadingMore: false
  }),
  false,
  "\u4ECD\u6709\u4E0B\u4E00\u9875\u65F6\u4E0D\u80FD\u524D\u7AEF\u6807\u7B7E\u8FC7\u6EE4\uFF0C\u5FC5\u987B\u7531\u540E\u7AEF\u515C\u5E95"
);
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: "scoped-query",
    baseQueryKey: "base-query",
    loadedRowsLength: 12,
    itemsTotal: 12,
    hasMore: false,
    loading: false,
    loadingMore: false
  }),
  false,
  "\u6700\u8FD1\u52A0\u8F7D\u7684\u662F\u5E26\u6807\u7B7E\u67E5\u8BE2\u65F6\u4E0D\u80FD\u5F53\u4F5C base \u5168\u91CF\u7ED3\u679C\u505A\u672C\u5730\u6807\u7B7E\u5207\u6362"
);
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: "base-query",
    baseQueryKey: "base-query",
    loadedRowsLength: 82,
    itemsTotal: 83,
    hasMore: false,
    loading: false,
    loadingMore: false
  }),
  false,
  "\u5DF2\u52A0\u8F7D\u6570\u91CF\u5C0F\u4E8E\u603B\u6570\u65F6\u4E0D\u80FD\u524D\u7AEF\u6807\u7B7E\u8FC7\u6EE4"
);
assertEqual(
  canUseContainerDetailLocalTagFilters({
    loadedQueryKey: "base-query",
    baseQueryKey: "base-query",
    loadedRowsLength: 83,
    itemsTotal: 83,
    hasMore: false,
    loading: true,
    loadingMore: false
  }),
  false,
  "\u660E\u7EC6\u52A0\u8F7D\u4E2D\u4E0D\u80FD\u524D\u7AEF\u6807\u7B7E\u8FC7\u6EE4\uFF0C\u907F\u514D\u4F7F\u7528\u534A\u622A\u6570\u636E"
);
assertDeepEqual(
  mergeContainerDetailLoadedItems(
    [
      { id: 401, hguid: "merge-401", \u5546\u54C1\u540D\u79F0: "\u65E7 401" },
      { id: 402, hguid: "merge-402", \u5546\u54C1\u540D\u79F0: "\u65E7 402" }
    ],
    [
      { id: 402, hguid: "merge-402", \u5546\u54C1\u540D\u79F0: "\u65B0 402" },
      { id: 403, hguid: "merge-403", \u5546\u54C1\u540D\u79F0: "\u65B0 403" }
    ]
  ).map((row) => ({ hguid: row.hguid, name: row.\u5546\u54C1\u540D\u79F0 })),
  [
    { hguid: "merge-401", name: "\u65E7 401" },
    { hguid: "merge-402", name: "\u65B0 402" },
    { hguid: "merge-403", name: "\u65B0 403" }
  ],
  "\u61D2\u52A0\u8F7D\u8FFD\u52A0\u660E\u7EC6\u65F6\u5E94\u6309 hguid \u53BB\u91CD\u5E76\u7528\u65B0\u9875\u6570\u636E\u8986\u76D6\u91CD\u590D\u884C"
);
assertDeepEqual(
  mergeContainerDetailLoadedItems(
    [{ id: 501, hguid: "", \u5546\u54C1\u540D\u79F0: "\u65E0 GUID \u65E7\u884C" }],
    [{ id: 501, hguid: "", \u5546\u54C1\u540D\u79F0: "\u65E0 GUID \u65B0\u884C" }]
  ).map((row) => row.\u5546\u54C1\u540D\u79F0),
  ["\u65E0 GUID \u65E7\u884C", "\u65E0 GUID \u65B0\u884C"],
  "\u7F3A\u5C11 hguid \u7684\u660E\u7EC6\u4E0D\u80FD\u88AB\u8BEF\u5224\u4E3A\u540C\u4E00\u884C"
);
assertDeepEqual(
  getContainerDetailRemoteQueryResetState({ selectedRowKeys: ["a", "b"] }),
  {
    selectedRowKeys: [],
    loadedItems: [],
    pageNumber: 1
  },
  "\u8FDC\u7A0B query \u53D8\u5316\u65F6\u5E94\u91CD\u7F6E\u9009\u62E9\u3001\u5DF2\u52A0\u8F7D\u660E\u7EC6\u548C\u9875\u7801"
);
var sortableFields = [
  "itemNumber",
  "barcode",
  "productName",
  "englishName",
  "productType",
  "newProduct",
  "matchType",
  "containerPieces",
  "middlePackQuantity",
  "containerQuantity",
  "domesticPrice",
  "floatRate",
  "transportCost",
  "warehouseImportPrice",
  "importPrice",
  "oemPrice",
  "warehouseStatus",
  "remark"
];
assertDeepEqual(
  sortableFields.filter((field) => !isContainerDetailSortField(field)),
  [],
  "\u8D27\u67DC\u660E\u7EC6\u6240\u6709\u53EF\u6392\u5E8F\u5B57\u6BB5\u90FD\u5E94\u901A\u8FC7\u8FD0\u884C\u65F6\u767D\u540D\u5355\u6821\u9A8C"
);
assertEqual(isContainerDetailSortField("\u88C5\u67DC\u6570\u91CF"), false, "\u4E2D\u6587 dataIndex \u4E0D\u5E94\u88AB\u5F53\u4F5C\u8D27\u67DC\u660E\u7EC6\u6392\u5E8F\u5B57\u6BB5");
assertEqual(isContainerDetailSortField("unknownField"), false, "\u672A\u77E5\u5B57\u6BB5\u4E0D\u5E94\u88AB\u5F53\u4F5C\u8D27\u67DC\u660E\u7EC6\u6392\u5E8F\u5B57\u6BB5");
assertEqual(isContainerDetailSortField(void 0), false, "\u7A7A\u5B57\u6BB5\u4E0D\u5E94\u88AB\u5F53\u4F5C\u8D27\u67DC\u660E\u7EC6\u6392\u5E8F\u5B57\u6BB5");
assertEqual(
  getContainerDetailProductCode({ id: 301, hguid: "code-301", \u5546\u54C1\u7F16\u7801: "   ", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: " HB301 " } }),
  "HB301",
  "\u5546\u54C1\u7F16\u7801\u89E3\u6790\u5E94 trim \u660E\u7EC6\u7F16\u7801\u5E76\u5728\u7A7A\u767D\u65F6\u56DE\u9000\u5546\u54C1\u4FE1\u606F\u7F16\u7801"
);
assertEqual(
  getContainerDetailProductCode({ id: 302, hguid: "code-302", \u5546\u54C1\u7F16\u7801: "   ", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "   " } }),
  void 0,
  "\u5546\u54C1\u7F16\u7801\u89E3\u6790\u5E94\u628A\u7A7A\u767D\u7F16\u7801\u89C6\u4E3A\u7F3A\u5931"
);
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 311,
    hguid: "label-311",
    \u5546\u54C1\u7F16\u7801: "P-LABEL",
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB308-031" }
  }),
  "HB308-031",
  "\u521B\u5EFA\u65B0\u5546\u54C1\u4E2D\u6587\u540D\u63D0\u793A\u5E94\u4F18\u5148\u4F7F\u7528\u8D27\u53F7\u5B9A\u4F4D\u884C"
);
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 312,
    hguid: "label-312",
    \u5546\u54C1\u7F16\u7801: "P-LABEL"
  }),
  "P-LABEL",
  "\u521B\u5EFA\u65B0\u5546\u54C1\u4E2D\u6587\u540D\u63D0\u793A\u5728\u7F3A\u5C11\u8D27\u53F7\u65F6\u5E94\u4F7F\u7528\u5546\u54C1\u7F16\u7801\u5B9A\u4F4D\u884C"
);
assertEqual(
  getContainerDetailCreateProductRowLabel({
    id: 313,
    hguid: "label-313"
  }),
  "label-313",
  "\u521B\u5EFA\u65B0\u5546\u54C1\u4E2D\u6587\u540D\u63D0\u793A\u5728\u7F3A\u5C11\u8D27\u53F7\u548C\u5546\u54C1\u7F16\u7801\u65F6\u5E94\u4F7F\u7528\u660E\u7EC6 GUID \u5B9A\u4F4D\u884C"
);
assertDeepEqual(
  findContainerDetailRowsMissingProductName([
    { id: 314, hguid: "name-314", \u662F\u5426\u65B0\u5546\u54C1: true, \u5546\u54C1\u540D\u79F0: "\u76AE\u5E26", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB308-030" } },
    { id: 315, hguid: "name-315", \u662F\u5426\u65B0\u5546\u54C1: true, \u5546\u54C1\u540D\u79F0: "belt", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB308-031" } },
    { id: 318, hguid: "name-318", \u662F\u5426\u65B0\u5546\u54C1: true, \u5546\u54C1\u540D\u79F0: "22-36,3PCS", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-480" } },
    { id: 316, hguid: "name-316", \u662F\u5426\u65B0\u5546\u54C1: true, \u5546\u54C1\u540D\u79F0: "   ", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB308-032" } },
    { id: 317, hguid: "name-317", \u662F\u5426\u65B0\u5546\u54C1: false, \u5546\u54C1\u540D\u79F0: "belt", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB308-033" } }
  ]),
  [
    { hguid: "name-316", label: "HB308-032", productName: "" }
  ],
  "\u521B\u5EFA\u65B0\u5546\u54C1\u524D\u5E94\u53EA\u62E6\u622A\u65B0\u5546\u54C1\u4E2D\u5546\u54C1\u540D\u79F0\u4E3A\u7A7A\u7684\u660E\u7EC6\uFF0C\u975E\u4E2D\u6587\u540D\u79F0\u4E5F\u5E94\u901A\u8FC7"
);
assertDeepEqual(
  findContainerDetailRowsMissingCreateProductRetailPrice([
    { id: 319, hguid: "price-319", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 25, \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-480" } },
    { id: 320, hguid: "price-320", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 0, \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-481" } },
    { id: 321, hguid: "price-321", \u662F\u5426\u65B0\u5546\u54C1: true, \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-482" } },
    { id: 322, hguid: "price-322", \u662F\u5426\u65B0\u5546\u54C1: true, \u8D34\u724C\u4EF7\u683C: 0, warehouseOEMPrice: 26, \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-483" } },
    { id: 323, hguid: "price-323", \u662F\u5426\u65B0\u5546\u54C1: false, \u8D34\u724C\u4EF7\u683C: 0, \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB137-484" } }
  ]),
  [
    { hguid: "price-320", label: "HB137-481", retailPrice: 0 },
    { hguid: "price-321", label: "HB137-482", retailPrice: void 0 },
    { hguid: "price-322", label: "HB137-483", retailPrice: 0 }
  ],
  "\u521B\u5EFA\u65B0\u5546\u54C1\u524D\u5E94\u53EA\u62E6\u622A\u65B0\u5546\u54C1\u4E2D\u660E\u7EC6\u96F6\u552E\u4EF7\u4E0D\u5927\u4E8E 0 \u7684\u660E\u7EC6\uFF0C\u4E0A\u6B21\u96F6\u552E\u4EF7\u4E0D\u518D\u515C\u5E95"
);
assertEqual(getContainerDetailMatchType({ id: 306, hguid: "match-306", matchType: "productCode" }), "productCode", "\u5339\u914D\u65B9\u5F0F\u5E94\u4F18\u5148\u8BFB\u53D6\u524D\u7AEF\u5F52\u4E00\u5316\u5B57\u6BB5");
assertEqual(getContainerDetailMatchType({ id: 307, hguid: "match-307", MatchType: "SupplierItem" }), "supplierItem", "\u5339\u914D\u65B9\u5F0F\u5E94\u517C\u5BB9\u540E\u7AEF PascalCase \u5B57\u6BB5");
assertEqual(getContainerDetailMatchType({ id: 308, hguid: "match-308", \u662F\u5426\u65B0\u5546\u54C1: true }), "unmatched", "\u7F3A\u5C11\u5339\u914D\u65B9\u5F0F\u7684\u65B0\u5546\u54C1\u5E94\u663E\u793A\u672A\u5339\u914D");
assertEqual(getContainerDetailMatchType({ id: 309, hguid: "match-309", \u662F\u5426\u65B0\u5546\u54C1: false }), "unmatched", "\u7F3A\u5C11\u5339\u914D\u65B9\u5F0F\u7684\u5DF2\u6709\u5546\u54C1\u4E0D\u80FD\u9ED8\u8BA4\u663E\u793A\u5546\u54C1\u7F16\u7801\u5339\u914D");
assertEqual(
  getContainerDetailMatchType({
    id: 310,
    hguid: "match-310",
    MatchType: "ProductCode",
    \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "HB013-108", \u8D27\u53F7: "HB013-108" }
  }),
  "productCode",
  "\u540E\u7AEF\u660E\u786E\u8FD4\u56DE ProductCode \u65F6\u5E94\u5C55\u793A\u5546\u54C1\u7F16\u7801\u5339\u914D"
);
assertDeepEqual(
  applyContainerDetailWarehouseStatusByProductCodes([
    { id: 303, hguid: "code-303", \u5546\u54C1\u7F16\u7801: " HB303 ", warehouseIsActive: false },
    { id: 304, hguid: "code-304", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "HB303" }, warehouseIsActive: false },
    { id: 305, hguid: "code-305", \u5546\u54C1\u7F16\u7801: "HB305", warehouseIsActive: false }
  ], ["HB303"], true).map((row) => ({ hguid: row.hguid, active: row.warehouseIsActive })),
  [
    { hguid: "code-303", active: true },
    { hguid: "code-304", active: true },
    { hguid: "code-305", active: false }
  ],
  "\u4ED3\u5E93\u72B6\u6001\u672C\u5730\u66F4\u65B0\u5E94\u6309 trim \u540E\u5546\u54C1\u7F16\u7801\u540C\u6B65\u540C\u5546\u54C1\u884C"
);
var defaultColumnOrder = ["index", "image", "itemNumber", "categoryName", "barcode", "productName", "englishName"];
assertDeepEqual(
  mergeContainerDetailColumnOrder(["barcode", "unknown", "barcode", "image"], defaultColumnOrder),
  ["barcode", "image", "index", "itemNumber", "categoryName", "productName", "englishName"],
  "\u8D27\u67DC\u660E\u7EC6\u5217\u987A\u5E8F\u5E94\u8FC7\u6EE4\u672A\u77E5\u5217\u3001\u53BB\u91CD\u5E76\u8865\u9F50\u65B0\u589E\u5217"
);
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, "barcode", "image"),
  ["index", "barcode", "image", "itemNumber", "categoryName", "productName", "englishName"],
  "\u8D27\u67DC\u660E\u7EC6\u5217\u62D6\u62FD\u5E94\u628A active \u5217\u79FB\u52A8\u5230 over \u5217\u4F4D\u7F6E"
);
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, "missing", "image"),
  defaultColumnOrder,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u62D6\u62FD\u9047\u5230\u672A\u77E5\u5217\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertDeepEqual(
  moveContainerDetailColumnOrder(defaultColumnOrder, "image", "image"),
  defaultColumnOrder,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u62D6\u62FD active \u4E0E over \u76F8\u540C\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertEqual(
  isContainerDetailColumnOrderCustomized(defaultColumnOrder, defaultColumnOrder),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u987A\u5E8F\u4E0E\u9ED8\u8BA4\u987A\u5E8F\u4E00\u81F4\u65F6\u4E0D\u5E94\u663E\u793A\u91CD\u7F6E\u5165\u53E3"
);
assertEqual(
  isContainerDetailColumnOrderCustomized(moveContainerDetailColumnOrder(defaultColumnOrder, "barcode", "image"), defaultColumnOrder),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u987A\u5E8F\u88AB\u62D6\u62FD\u4FEE\u6539\u540E\u5E94\u663E\u793A\u91CD\u7F6E\u5165\u53E3"
);
assertEqual(
  isContainerDetailColumnOrderCustomized([], defaultColumnOrder),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u987A\u5E8F\u521D\u59CB\u5316\u4E3A\u7A7A\u65F6\u4E0D\u5E94\u8BEF\u5224\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: true, failedCount: 1, errors: ["HB303 \u66F4\u65B0\u5931\u8D25"] }, "\u6279\u91CF\u4E0A\u4E0B\u67B6\u5931\u8D25"),
  "HB303 \u66F4\u65B0\u5931\u8D25",
  "\u4ED3\u5E93\u4E0A\u4E0B\u67B6\u7ED3\u679C\u6709\u5931\u8D25\u660E\u7EC6\u65F6\u5E94\u89C6\u4E3A\u5931\u8D25"
);
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: false, message: "\u540E\u7AEF\u5931\u8D25" }, "\u6279\u91CF\u4E0A\u4E0B\u67B6\u5931\u8D25"),
  "\u540E\u7AEF\u5931\u8D25",
  "\u4ED3\u5E93\u4E0A\u4E0B\u67B6\u7ED3\u679C\u6839 success false \u65F6\u5E94\u8FD4\u56DE\u5931\u8D25\u4FE1\u606F"
);
assertEqual(
  getContainerDetailWarehouseActionFailureMessage({ success: true, failedCount: 0 }, "\u6279\u91CF\u4E0A\u4E0B\u67B6\u5931\u8D25"),
  void 0,
  "\u4ED3\u5E93\u4E0A\u4E0B\u67B6\u7ED3\u679C\u5168\u6210\u529F\u65F6\u4E0D\u5E94\u8FD4\u56DE\u5931\u8D25\u4FE1\u606F"
);
var pageSource = readFileSync("src/pages/Warehouse/ContainerDetail/index.tsx", "utf8");
var tagFiltersSource = readFileSync("src/pages/Warehouse/ContainerDetail/ContainerTagFilters.tsx", "utf8");
var columnsSource = readFileSync("src/pages/Warehouse/ContainerDetail/ContainerDetailColumns.tsx", "utf8");
var setCodeHookSource = readFileSync("src/pages/Warehouse/ContainerDetail/useContainerSetCode.tsx", "utf8");
var pageStyleSource = readFileSync("src/pages/Warehouse/ContainerDetail/index.css", "utf8");
var mobileLayoutSource = readFileSync("src/layout/MobileLayout.tsx", "utf8");
var containerDetailLogicSource = readFileSync("src/pages/Warehouse/ContainerDetail/containerDetailLogic.ts", "utf8");
var warehouseProductServiceSource = readFileSync("src/services/warehouseProductService.ts", "utf8");
var posProductTypeSource = readFileSync("src/types/posProduct.ts", "utf8");
var zhLocale = JSON.parse(readFileSync("src/i18n/locales/zh.json", "utf8"));
var enLocale = JSON.parse(readFileSync("src/i18n/locales/en.json", "utf8"));
assertEqual(
  pageSource.includes("PDF \u5BF9\u5916\u5206\u4EAB\u65F6\u4E0D\u5C55\u793A\u6C47\u7387\u548C\u8FD0\u8D39\u91D1\u989D\uFF1BExcel \u4ECD\u4FDD\u7559\u5B8C\u6574\u6838\u5BF9\u4FE1\u606F\u3002") && pageSource.includes("const summaryRows: NonNullable<ContainerExportOptions['summary']>['rows'] = format === 'pdf'") && pageSource.includes(": baseSummaryRows"),
  true,
  "\u8D27\u67DC\u660E\u7EC6 PDF \u57FA\u7840\u4FE1\u606F\u4E0D\u5E94\u5C55\u793A\u6C47\u7387\u548C\u8FD0\u8D39\u91D1\u989D\uFF0CExcel \u5E94\u4FDD\u7559\u5B8C\u6574\u6458\u8981"
);
assertEqual(
  pageSource.includes("const containerDetailTabKey = containerGuid ? `/warehouse/container/detail/${containerGuid}` : undefined") && pageSource.includes("if (!active || !containerDetailTabKey || !containerDetailTabTitle)") && pageSource.includes("updateTabTitle(containerDetailTabKey, containerDetailTabTitle)") && !pageSource.includes("useDynamicTabTitle(container?.\u8D27\u67DC\u7F16\u53F7"),
  true,
  "\u8D27\u67DC\u660E\u7EC6 Tab \u6807\u9898\u53EA\u5E94\u7531\u5F53\u524D active \u7684 KeepAlive \u5B9E\u4F8B\u66F4\u65B0\uFF0C\u907F\u514D\u65E7\u5B9E\u4F8B\u8DDF\u968F\u5168\u5C40 URL \u6539\u9519\u6807\u7B7E"
);
function getLocaleValue(source, key) {
  return key.split(".").reduce((current, part) => current && typeof current === "object" ? current[part] : void 0, source);
}
var containerDetailExportLabelKeys = CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => column.labelKey);
var requiredContainerI18nKeys = [
  "containers.actions.batchUpdateFloatRate",
  "containers.actions.batchUpdatePrices",
  "containers.actions.showReadonlyOemPrice",
  "containers.actions.pushToHq",
  "containers.actions.saveDetails",
  "containers.actions.matchDomesticData",
  "containers.actions.alignDomesticProductCode",
  "containers.actions.previewImage",
  "containers.text.loadedRows",
  "containers.text.warehouseInventoriesCreated",
  "containers.text.warehouseInventoriesUpdated",
  "containers.text.skippedNewProducts",
  "containers.text.missingProductCodeRows",
  "containers.text.pushToHqSelectedLocalProducts",
  "containers.text.moreCreateProductResultItems",
  "containers.text.createProductsJobSummary",
  "containers.text.skippedRows",
  "containers.text.failedRows",
  "containers.modals.savePendingPriceDetailsTitle",
  "containers.modals.savePendingPriceDetailsUpdateTitle",
  "containers.modals.savePendingPriceDetailsSummary",
  "containers.modals.savePendingPriceDetailsExistingRetailHint",
  "containers.modals.savePendingPriceDetailsNewRetailHint",
  "containers.modals.savePendingPriceDetailsRetryHint",
  "containers.messages.selectedRowsHidden",
  "containers.messages.savePendingPriceDetailsFirst",
  "containers.messages.detailSaveFailed",
  "containers.messages.noPendingPriceDetails",
  "containers.messages.detailPricesSaved",
  "containers.messages.selectBatchProducts",
  "containers.messages.noMatchableDetails",
  "containers.messages.missingMatchableProductIdentity",
  "containers.messages.noDomesticDataToUpdate",
  "containers.messages.domesticDataMatched",
  "containers.messages.columnOrderReset",
  "containers.messages.matchDomesticDataFailed",
  "containers.messages.missingAlignProductCode",
  "containers.messages.domesticProductCodeAligned",
  "containers.messages.alignDomesticProductCodeFailed",
  "containers.messages.alignSetChildNotSupported",
  "containers.messages.rowCategoryUpdated",
  "containers.messages.noExistingLocalProductsToPushHq",
  "containers.messages.createProductsJobSubmitted",
  "containers.messages.createProductsJobFailed",
  "containers.messages.createProductsJobPartialSucceeded",
  "containers.messages.createProductsJobSucceeded",
  "containers.messages.createProductFailed",
  "containers.messages.purchasePricesUpdateFailed",
  "containers.messages.newProductCannotToggleWarehouseStatus",
  "containers.messages.newProductsSkippedForWarehouseStatus",
  "containers.modals.batchUpdateFloatRateTitle",
  "containers.modals.batchUpdatePricesTitle",
  "containers.modals.batchActionContent",
  "containers.modals.batchActionAllHint",
  "containers.modals.alignDomesticProductCodeTitle",
  "containers.modals.alignDomesticProductCodeContent",
  "containers.modals.alignDomesticProductCodeConflictHint",
  "containers.modals.rowCategoryTitle",
  "containers.export.summaryTitle",
  ...containerDetailExportLabelKeys,
  "containers.setCode.pricesTitle",
  "containers.setCode.missingProductCode",
  "containers.setCode.loadFailed",
  "containers.setCode.saveSuccess",
  "containers.setCode.saveFailed",
  "containers.setCode.itemNumber",
  "containers.setCode.barcode",
  "containers.setCode.retailPrice",
  "containers.setCode.purchasePrice",
  "warehouse.categories.selectTargetCategory",
  "warehouse.categories.batchAssignFailed",
  "posAdmin.products.noManagePermission",
  "posAdmin.products.pushToHqAffectedRows",
  "posAdmin.products.productsAdded",
  "posAdmin.products.productsUpdated",
  "posAdmin.products.storeRetailPricesUpdated",
  "posAdmin.products.storeMultiCodesUpdated",
  "posAdmin.products.pushToHqResult",
  "posAdmin.products.pushToHqFailed",
  "posAdmin.products.pushToHqPartialSucceeded",
  "posAdmin.products.pushToHqSucceeded"
];
assertDeepEqual(
  requiredContainerI18nKeys.filter((key) => !getLocaleValue(zhLocale, key) || !getLocaleValue(enLocale, key)),
  [],
  "\u8D27\u67DC\u660E\u7EC6\u65B0\u589E\u53EF\u89C1\u6587\u6848\u5E94\u540C\u65F6\u8865\u9F50\u4E2D\u82F1\u6587 locale\uFF0C\u907F\u514D\u82F1\u6587\u6A21\u5F0F\u56DE\u9000\u4E2D\u6587\u515C\u5E95"
);
assertEqual(
  pageSource.includes("const newProductCount = scopedRows.filter((row) => row.\u662F\u5426\u65B0\u5546\u54C1).length") && pageSource.includes("const eligibleRows = scopedRows.filter((row) => !row.\u662F\u5426\u65B0\u5546\u54C1)") && pageSource.includes("message.warning(t('containers.messages.newProductsSkippedForWarehouseStatus'") && pageSource.includes("const productCodes = eligibleRows\n      .map(getContainerDetailProductCode)") && pageSource.includes("eligibleRows.some((row) => {"),
  true,
  "\u6279\u91CF\u4E0A\u4E0B\u67B6\u5E94\u8DF3\u8FC7\u65B0\u5546\u54C1\uFF0C\u53EA\u628A\u5DF2\u6709\u5546\u54C1\u7F16\u7801\u63D0\u4EA4\u5230\u4ED3\u5E93\u5546\u54C1\u4E0A\u4E0B\u67B6\u63A5\u53E3"
);
assertEqual(
  pageSource.includes("if (!eligibleRows.length)") && pageSource.includes("message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '\u65B0\u5546\u54C1\u8BF7\u5148\u521B\u5EFA\u540E\u518D\u4E0A\u4E0B\u67B6'))"),
  true,
  "\u6279\u91CF\u4E0A\u4E0B\u67B6\u76EE\u6807\u5168\u662F\u65B0\u5546\u54C1\u65F6\u5E94\u53EA\u63D0\u793A\uFF0C\u4E0D\u8BF7\u6C42\u540E\u7AEF\u63A5\u53E3"
);
assertEqual(
  pageSource.includes("const handleWarehouseStatusChange = async (row: ContainerDetail, isActive: boolean) => {") && pageSource.includes("if (row.\u662F\u5426\u65B0\u5546\u54C1) {\n      message.warning(t('containers.messages.newProductCannotToggleWarehouseStatus', '\u65B0\u5546\u54C1\u8BF7\u5148\u521B\u5EFA\u540E\u518D\u4E0A\u4E0B\u67B6'))\n      return\n    }\n    const productCode = getContainerDetailProductCode(row)"),
  true,
  "\u5355\u884C\u4ED3\u5E93\u72B6\u6001\u5207\u6362\u5E94\u5148\u62E6\u622A\u65B0\u5546\u54C1\uFF0C\u907F\u514D\u7528\u4E0D\u5B58\u5728\u7684\u4ED3\u5E93\u5546\u54C1\u7F16\u7801\u8C03\u7528\u4E0A\u4E0B\u67B6\u63A5\u53E3"
);
assertEqual(
  pageSource.includes("const warehouseStatusDisabledMessage = isWarehouseStatusPending") && pageSource.includes("? t('containers.messages.newProductCannotToggleWarehouseStatus', '\u65B0\u5546\u54C1\u8BF7\u5148\u521B\u5EFA\u540E\u518D\u4E0A\u4E0B\u67B6')") && pageSource.includes("<Tooltip title={warehouseStatusDisabledMessage}>") && pageSource.includes("disabled={row.\u662F\u5426\u65B0\u5546\u54C1 || !productCode || isWarehouseStatusPending}"),
  true,
  "\u65B0\u5546\u54C1\u884C\u7684\u4ED3\u5E93\u72B6\u6001\u5F00\u5173\u5E94\u7981\u7528\uFF0C\u5E76\u663E\u793A\u5148\u521B\u5EFA\u518D\u4E0A\u4E0B\u67B6\u63D0\u793A"
);
assertDeepEqual(
  CONTAINER_DETAIL_EXPORT_COLUMNS.map((column) => getLocaleValue(enLocale, column.labelKey)),
  [
    "No.",
    "Item No.",
    "Barcode",
    "Barcode Image",
    "Product Image",
    "Chinese Name",
    "English Name",
    "Pieces",
    "Total Qty",
    "Unit Volume",
    "Total Volume",
    "INNER",
    "RMB Cost",
    "Current Purchase Price",
    "Current Retail Price",
    "RRP"
  ],
  "\u82F1\u6587\u6A21\u5F0F\u5BFC\u51FA\u5217\u9009\u62E9\u5F39\u7A97\u548C Excel \u8868\u5934\u5E94\u5168\u90E8\u4F7F\u7528\u82F1\u6587 locale\uFF0C\u4E0D\u80FD\u56DE\u9000\u5230\u4E2D\u6587 fallback"
);
assertEqual(
  pageSource.includes("const DEFAULT_CONTAINER_DETAIL_SORT: ContainerDetailSortState = { field: 'itemNumber', order: 'ascend' }"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9875\u5E94\u58F0\u660E\u8D27\u53F7\u5347\u5E8F\u9ED8\u8BA4\u6392\u5E8F"
);
assertEqual(
  pageSource.includes("useState<ContainerDetailSortState>(DEFAULT_CONTAINER_DETAIL_SORT)"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9875\u521D\u59CB\u6392\u5E8F\u5E94\u9ED8\u8BA4\u4F7F\u7528\u8D27\u53F7\u5347\u5E8F"
);
assertEqual(
  pageSource.includes("setSortState(DEFAULT_CONTAINER_DETAIL_SORT)"),
  true,
  "\u6E05\u7A7A\u8868\u683C\u6392\u5E8F\u6216\u5217\u72B6\u6001\u65F6\u5E94\u6062\u590D\u8D27\u53F7\u5347\u5E8F\u9ED8\u8BA4\u6392\u5E8F"
);
assertEqual(
  pageSource.includes("const [showReadonlyOemPrice, setShowReadonlyOemPrice] = useState(false)"),
  true,
  "\u53EA\u8BFB\u96F6\u552E\u4EF7\u5FEB\u89C8\u5217\u5E94\u9ED8\u8BA4\u5173\u95ED"
);
assertEqual(
  pageSource.includes("showReadonlyOemPrice ? [readonlyOemPriceColumn] : []"),
  true,
  "\u53EA\u8BFB\u96F6\u552E\u4EF7\u5FEB\u89C8\u5217\u5E94\u53EA\u5728\u5F00\u5173\u6253\u5F00\u65F6\u63D2\u5165\u8868\u683C\u5217"
);
var matchedPriceContainer = { \u6C47\u7387: 4.5, \u8FD0\u8D39: 100, \u603B\u4F53\u79EF: 10 };
var matchedPriceRows = [
  {
    id: 701,
    hguid: "match-price-701",
    \u5546\u54C1\u7F16\u7801: "P-MATCH-1",
    \u56FD\u5185\u4EF7\u683C: void 0,
    \u8D34\u724C\u4EF7\u683C: 0,
    \u8C03\u6574\u6D6E\u7387: 1.2,
    \u88C5\u67DC\u4EF6\u6570: 2,
    \u88C5\u67DC\u6570\u91CF: 20,
    \u5355\u4EF6\u4F53\u79EF: 0.5,
    \u8FD0\u8F93\u6210\u672C: void 0,
    \u8FDB\u53E3\u4EF7\u683C: 0,
    \u5546\u54C1\u540D\u79F0: "\u65E7\u5546\u54C1\u540D",
    \u82F1\u6587\u540D\u79F0: "Old English"
  },
  {
    id: 702,
    hguid: "match-price-702",
    \u5546\u54C1\u7F16\u7801: "P-MATCH-2",
    \u56FD\u5185\u4EF7\u683C: 8.8,
    \u8D34\u724C\u4EF7\u683C: 3.3,
    \u8C03\u6574\u6D6E\u7387: 1.1,
    \u88C5\u67DC\u4EF6\u6570: 2,
    \u88C5\u67DC\u6570\u91CF: 24,
    \u5355\u4EF6\u88C5\u7BB1\u6570: 12,
    \u5355\u4EF6\u4F53\u79EF: 0.2,
    \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0.4,
    \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: 232.32,
    \u5546\u54C1\u540D\u79F0: "\u4FDD\u7559\u4EF7\u683C\u4F46\u66F4\u65B0\u89C4\u683C"
  },
  {
    id: 703,
    hguid: "match-price-703",
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "ITEM-703", \u6761\u5F62\u7801: "BAR-703" },
    \u56FD\u5185\u4EF7\u683C: 0,
    \u8D34\u724C\u4EF7\u683C: void 0,
    \u88C5\u67DC\u4EF6\u6570: 3
  },
  {
    id: 704,
    hguid: "match-price-704",
    \u56FD\u5185\u4EF7\u683C: void 0,
    \u8D34\u724C\u4EF7\u683C: void 0
  },
  {
    id: 705,
    hguid: "match-price-705",
    \u5546\u54C1\u7F16\u7801: "P-CODE-FIRST",
    localSupplierCode: "200",
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "ITEM-FALLBACK", \u6761\u5F62\u7801: "BAR-FALLBACK" },
    \u8D34\u724C\u4EF7\u683C: 0,
    \u88C5\u67DC\u4EF6\u6570: 4
  },
  {
    id: 706,
    hguid: "match-price-706",
    \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB138-066", \u6761\u5F62\u7801: "9527913800028" },
    \u8D34\u724C\u4EF7\u683C: 0,
    \u88C5\u67DC\u4EF6\u6570: 12
  },
  {
    id: 707,
    hguid: "match-price-707",
    \u5546\u54C1\u7F16\u7801: "P-STALE-TOTALS",
    \u56FD\u5185\u4EF7\u683C: 5,
    \u8C03\u6574\u6D6E\u7387: void 0,
    \u88C5\u67DC\u4EF6\u6570: 2,
    \u88C5\u67DC\u6570\u91CF: 10,
    \u5355\u4EF6\u88C5\u7BB1\u6570: 5,
    \u5355\u4EF6\u4F53\u79EF: 0.5,
    \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0,
    \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: 0,
    \u8FD0\u8F93\u6210\u672C: 0,
    \u8FDB\u53E3\u4EF7\u683C: 0,
    \u5546\u54C1\u540D\u79F0: "\u65E7\u5408\u8BA1\u5546\u54C1"
  },
  {
    id: 708,
    hguid: "match-price-708",
    \u5546\u54C1\u4FE1\u606F: { \u6761\u5F62\u7801: "BARCODE-ONLY" },
    \u56FD\u5185\u4EF7\u683C: void 0,
    \u8D34\u724C\u4EF7\u683C: void 0
  }
];
var matchedPriceUpdates = buildContainerDetailMatchedDomesticDataUpdates(
  matchedPriceRows,
  [
    { ProductCode: "P-MATCH-1", ProductName: "\u65B0\u5546\u54C1\u540D", EnglishName: "New English", WarehouseDomesticPrice: 11.6, WarehouseOEMPrice: 6.99, PackingQuantity: 48, WarehouseVolume: 0.118 },
    { ProductCode: "P-MATCH-2", ProductName: "\u8986\u76D6\u540D\u79F0", EnglishName: "Override English", WarehouseDomesticPrice: 22.2, WarehouseOEMPrice: 9.9, PackingQuantity: 24, WarehouseVolume: 0.33 },
    { ItemNumber: "ITEM-703", ProductName: "\u8D27\u53F7\u5339\u914D\u5546\u54C1", EnglishName: "Item Matched", WarehouseDomesticPrice: 5.5, WarehouseOEMPrice: 2.2, PackingQuantity: 10, WarehouseVolume: 0.25 },
    { ProductCode: "P-CODE-FIRST", ItemNumber: "OTHER-ITEM", ProductName: "\u5546\u54C1\u7F16\u7801\u4F18\u5148\u5546\u54C1", WarehouseOEMPrice: 7.7, PackingQuantity: 8 },
    { ItemNumber: "ITEM-FALLBACK", ProductName: "\u4E0D\u5E94\u4F7F\u7528\u7684\u8D27\u53F7\u5339\u914D", WarehouseOEMPrice: 1.1, PackingQuantity: 99 },
    { ItemNumber: "HB138-066", ProductName: "\u91D1/\u9ED1\u6846\u6DF730X40", DomesticOEMPrice: 15.5, PackingQuantity: 24 },
    { ProductCode: "P-STALE-TOTALS", ProductName: "\u65E7\u5408\u8BA1\u5546\u54C1", WarehouseDomesticPrice: 5, PackingQuantity: 5, WarehouseVolume: 0.5 },
    { Barcode: "BARCODE-ONLY", ProductName: "\u6761\u7801\u4E0D\u5E94\u515C\u5E95", WarehouseDomesticPrice: 9.9, WarehouseOEMPrice: 8.8 }
  ],
  matchedPriceContainer
);
assertDeepEqual(
  buildContainerDetailDetectionItems([
    { id: 901, hguid: "detect-901", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B", \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" } },
    { id: 902, hguid: "detect-902", \u5546\u54C1\u7F16\u7801: "P-CODE", localSupplierCode: "SUP01", \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "ITEM-902" } },
    { id: 903, hguid: "detect-903", \u5546\u54C1\u4FE1\u606F: { \u6761\u5F62\u7801: "BARCODE-ONLY" } },
    { id: 904, hguid: "detect-904", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "HB013-108", \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" } }
  ]),
  [
    { ProductCode: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B", ItemNumber: "HB013-108", SupplierCode: "200" },
    { ProductCode: "P-CODE", ItemNumber: "ITEM-902", SupplierCode: "SUP01" },
    { ProductCode: "HB013-108", ItemNumber: "HB013-108", SupplierCode: "200" }
  ],
  "\u5339\u914D\u68C0\u6D4B\u9879\u5E94\u540C\u65F6\u63D0\u4EA4\u5546\u54C1\u7F16\u7801\u548C\u884C\u4F9B\u5E94\u5546+\u8D27\u53F7\uFF0C\u4E14\u4E0D\u63D0\u4EA4\u6761\u7801\u515C\u5E95"
);
assertDeepEqual(
  buildContainerDetailMatchedDomesticDataUpdates(
    [
      {
        id: 904,
        hguid: "match-price-904",
        \u5546\u54C1\u7F16\u7801: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      { ProductCode: "G091539", ItemNumber: "HB013-108", SupplierCode: "200", ProductName: "FOLDABLE BROOM SET" },
      { Barcode: "9528501322108", ProductName: "\u6761\u7801\u4E0D\u5E94\u515C\u5E95" }
    ],
    matchedPriceContainer
  ),
  [],
  "\u5546\u54C1\u7F16\u7801\u4E0D\u4E00\u81F4\u4F46\u4F9B\u5E94\u5546 200 + HB \u8D27\u53F7\u547D\u4E2D\u65F6\uFF0C\u53EA\u80FD\u4F5C\u4E3A\u5019\u9009\uFF0C\u4E0D\u80FD\u81EA\u52A8\u5199\u5165\u660E\u7EC6\u6570\u636E"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 905,
        hguid: "match-status-905",
        \u5546\u54C1\u7F16\u7801: "P-SAME",
        localSupplierCode: "200",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      { ProductCode: "P-SAME", ProductName: "FOLDABLE BROOM SET", WarehouseDomesticPrice: 13, WarehouseOEMPrice: 11.99 },
      { Barcode: "9528501322108", ProductName: "\u6761\u7801\u4E0D\u5E94\u515C\u5E95", WarehouseDomesticPrice: 99 }
    ]
  ),
  [
    {
      hguid: "match-status-905",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false
    }
  ],
  "\u771F\u5B9E\u5546\u54C1\u7F16\u7801\u4E00\u81F4\u65F6\uFF0C\u52A0\u8F7D\u6001\u53EA\u8BFB\u5339\u914D\u6821\u6B63\u5E94\u6807\u8BB0\u5546\u54C1\u7F16\u7801\u5339\u914D\u4E14\u4E0D\u751F\u6210\u4EF7\u683C\u5199\u5E93\u5B57\u6BB5"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9051,
        hguid: "match-status-9051",
        \u5546\u54C1\u7F16\u7801: "P-SAME",
        localSupplierCode: "200",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        MatchType: "ProductCode",
        \u662F\u5426\u65B0\u5546\u54C1: false
      }
    ],
    [
      {
        ProductCode: "P-SAME",
        ItemNumber: "HB013-108",
        matchType: "both",
        ProductName: "\u626B\u628A"
      }
    ]
  ),
  [
    {
      hguid: "match-status-9051",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false
    }
  ],
  "\u540E\u7AEF\u8FD4\u56DE both \u4E14\u5546\u54C1\u7F16\u7801\u4E5F\u547D\u4E2D\u65F6\uFF0C\u5E94\u4F18\u5148\u5C55\u793A\u5546\u54C1\u7F16\u7801\u5339\u914D"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9052,
        hguid: "match-status-9052",
        \u5546\u54C1\u7F16\u7801: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      {
        productCode: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B",
        itemNumber: "HB013-108",
        exists: true,
        matchType: "item_number",
        productName: "\u5957\u626B"
      }
    ]
  ),
  [
    {
      hguid: "match-status-9052",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false
    }
  ],
  "\u5546\u54C1\u7F16\u7801\u771F\u5B9E\u4E00\u81F4\u65F6\uFF0C\u5373\u4F7F\u540E\u7AEF\u8FD4\u56DE item_number\uFF0C\u4E5F\u5E94\u6309\u5546\u54C1\u7F16\u7801\u5339\u914D\u5C55\u793A"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 906,
        hguid: "match-status-906",
        \u5546\u54C1\u7F16\u7801: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      {
        ProductCode: "G091539",
        ItemNumber: "HB013-108",
        SupplierCode: "200",
        MatchType: "ProductCode",
        ProductName: "FOLDABLE BROOM SET"
      },
      { Barcode: "9528501322108", ProductName: "\u6761\u7801\u4E0D\u5E94\u515C\u5E95", WarehouseDomesticPrice: 99 }
    ]
  ),
  [
    {
      hguid: "match-status-906",
      matchType: "supplierItem",
      hasProductCodeConflict: true,
      localProductCode: "G091539",
      domesticProductCode: "59FBE37D-A8B1-49E5-84A8-DB1C39AFE56B"
    }
  ],
  "\u5546\u54C1\u7F16\u7801\u4E0D\u540C\u4F46 200 + \u8D27\u53F7\u547D\u4E2D\u65F6\uFF0C\u5E94\u53EA\u6807\u8BB0\u5019\u9009\u5E76\u5E26\u51FA\u672C\u5730\u4E3B\u6863\u7F16\u7801"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9061,
        hguid: "match-status-9061",
        \u5546\u54C1\u7F16\u7801: "DOM-SUP01",
        localSupplierCode: "SUP01",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "ITEM-SUP01" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      {
        ProductCode: "LOCAL-SUP01",
        ItemNumber: "ITEM-SUP01",
        SupplierCode: "SUP01",
        ProductName: "Supplier scoped candidate"
      }
    ]
  ),
  [
    {
      hguid: "match-status-9061",
      matchType: "supplierItem",
      hasProductCodeConflict: true,
      localProductCode: "LOCAL-SUP01",
      domesticProductCode: "DOM-SUP01"
    }
  ],
  "\u5546\u54C1\u7F16\u7801\u4E0D\u540C\u4F46\u884C\u4F9B\u5E94\u5546+\u8D27\u53F7\u547D\u4E2D\u65F6\uFF0C\u5E94\u6309\u771F\u5B9E\u4F9B\u5E94\u5546\u6807\u8BB0\u5019\u9009"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 9062,
        hguid: "match-status-9062",
        \u5546\u54C1\u7F16\u7801: "DOM-SUP02",
        localSupplierCode: "SUP02",
        \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "ITEM-SUP02" },
        \u662F\u5426\u65B0\u5546\u54C1: true
      }
    ],
    [
      {
        ProductCode: "LOCAL-WRONG-SUPPLIER",
        ItemNumber: "ITEM-SUP02",
        SupplierCode: "SUP01",
        ProductName: "Wrong supplier candidate"
      }
    ]
  ),
  [],
  "\u8D27\u53F7\u76F8\u540C\u4F46\u4F9B\u5E94\u5546\u4E0D\u540C\uFF0C\u4E0D\u5E94\u5C55\u793A\u5019\u9009"
);
assertDeepEqual(
  buildContainerDetailMatchStatusUpdates(
    [
      {
        id: 907,
        hguid: "match-status-907",
        \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "HB013-108", \u8D27\u53F7: "HB013-108", \u6761\u5F62\u7801: "9528501322108" },
        MatchType: "ProductCode",
        \u662F\u5426\u65B0\u5546\u54C1: false
      }
    ],
    [
      {
        ProductCode: "HB013-108",
        ItemNumber: "HB013-108",
        SupplierCode: "200",
        MatchType: "ProductCode",
        ProductName: "FOLDABLE BROOM SET"
      }
    ]
  ),
  [
    {
      hguid: "match-status-907",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false
    }
  ],
  "\u5546\u54C1\u7F16\u7801\u547D\u4E2D\u65F6\uFF0C\u5373\u4F7F\u540C\u65F6\u5E26\u6709\u4F9B\u5E94\u5546 200 + \u8D27\u53F7\uFF0C\u4E5F\u5E94\u5C55\u793A\u5546\u54C1\u7F16\u7801\u5339\u914D"
);
assertDeepEqual(
  matchedPriceUpdates,
  [
    {
      hguid: "match-price-701",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      \u56FD\u5185\u4EF7\u683C: 11.6,
      \u8D34\u724C\u4EF7\u683C: 6.99,
      \u5546\u54C1\u540D\u79F0: "\u65B0\u5546\u54C1\u540D",
      \u82F1\u6587\u540D\u79F0: "New English",
      \u5355\u4EF6\u88C5\u7BB1\u6570: 48,
      \u88C5\u67DC\u6570\u91CF: 96,
      \u5355\u4EF6\u4F53\u79EF: 0.118,
      \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0.236,
      \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: 1336.32,
      \u8FD0\u8F93\u6210\u672C: 0.02,
      \u8FDB\u53E3\u4EF7\u683C: 2.83
    },
    {
      hguid: "match-price-702",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      \u5546\u54C1\u540D\u79F0: "\u8986\u76D6\u540D\u79F0",
      \u82F1\u6587\u540D\u79F0: "Override English",
      \u5355\u4EF6\u88C5\u7BB1\u6570: 24,
      \u88C5\u67DC\u6570\u91CF: 48,
      \u5355\u4EF6\u4F53\u79EF: 0.33,
      \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0.66,
      \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: 464.64,
      \u8FD0\u8F93\u6210\u672C: 0.14,
      \u8FDB\u53E3\u4EF7\u683C: 2.1
    },
    {
      hguid: "match-price-705",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      \u8D34\u724C\u4EF7\u683C: 7.7,
      \u5546\u54C1\u540D\u79F0: "\u5546\u54C1\u7F16\u7801\u4F18\u5148\u5546\u54C1",
      \u5355\u4EF6\u88C5\u7BB1\u6570: 8,
      \u88C5\u67DC\u6570\u91CF: 32
    },
    {
      hguid: "match-price-707",
      matchType: "productCode",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 1,
      \u5408\u8BA1\u88C5\u67DC\u91D1\u989D: 65,
      \u8FD0\u8F93\u6210\u672C: 1,
      \u8FDB\u53E3\u4EF7\u683C: 2.49
    }
  ],
  "\u5339\u914D\u56FD\u5185\u6570\u636E\u53EA\u5141\u8BB8\u5546\u54C1\u7F16\u7801\u7CBE\u786E\u547D\u4E2D\u5199\u5165\uFF1B\u8D27\u53F7\u547D\u4E2D\u4EC5\u4F5C\u4E3A\u5019\u9009\uFF0C\u4E0D\u81EA\u52A8\u8865\u4EF7\u683C\u6216\u540D\u79F0"
);
assertEqual(pageSource.includes("t('containers.actions.matchDomesticData')"), true, "\u9875\u9762\u6309\u94AE\u6587\u6848\u5E94\u4F7F\u7528\u5339\u914D\u56FD\u5185\u6570\u636E i18n key");
assertEqual(
  pageSource.includes("alignDomesticProductCode({") && pageSource.includes("expectedDomesticProductCode: domesticProductCode") && pageSource.includes("targetProductCode: localProductCode") && pageSource.includes("supplierCode,"),
  true,
  "\u5019\u9009\u5546\u54C1\u7F16\u7801\u5BF9\u9F50\u5FC5\u987B\u8D70\u4EBA\u5DE5\u786E\u8BA4\u63A5\u53E3\uFF0C\u4E0D\u80FD\u901A\u8FC7\u5339\u914D\u56FD\u5185\u6570\u636E\u6216\u4FDD\u5B58\u660E\u7EC6\u81EA\u52A8\u6539\u7801"
);
assertEqual(
  pageSource.includes("const canAlignDomesticProductCode = access.canEditContainer && (access.isAdmin || access.hasPermission(P.Products.Edit))") && pageSource.includes("getContainerDetailProductType(row) !== '\u5957\u88C5\u5B50\u5546\u54C1'") && pageSource.includes("const canAlignCandidate ="),
  true,
  "\u5BF9\u9F50\u56FD\u5185\u7F16\u7801\u5165\u53E3\u5E94\u8981\u6C42\u5546\u54C1\u7F16\u8F91\u6743\u9650\uFF0C\u4E14\u5957\u88C5\u5B50\u5546\u54C1\u4E0D\u80FD\u663E\u793A\u5355\u72EC\u5BF9\u9F50\u6309\u94AE"
);
assertEqual(
  pageSource.includes("renderColumnTitle('warehouseImportPrice'") && pageSource.includes("t('containers.fields.warehouseImportPrice'"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5E94\u72EC\u7ACB\u663E\u793A\u5B9E\u65F6\u8FDB\u8D27\u4EF7\u5217"
);
assertEqual(
  pageSource.includes("buildContainerDetailMatchedDomesticDataUpdates(scopedRows, detected, container)") && pageSource.includes("const scopedRows = await confirmBatchRows(t('containers.actions.matchDomesticData'))") && pageSource.includes("return await fetchAllRowsForCurrentQuery()"),
  true,
  "\u9875\u9762\u5E94\u8C03\u7528\u5339\u914D\u56FD\u5185\u6570\u636E helper\uFF0C\u672A\u52FE\u9009\u65F6\u6309\u5F53\u524D\u7B5B\u9009\u7ED3\u679C\u5168\u91CF\u5904\u7406"
);
assertEqual(
  pageSource.includes("findContainerDetailRowsMissingProductName(scopedRows)") && pageSource.includes("'containers.messages.createProductsMissingProductName'") && pageSource.includes("missingProductNameRows.map((row) => row.label).join"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u524D\u5E94\u62E6\u622A\u5546\u54C1\u540D\u79F0\u4E3A\u7A7A\u7684\u65B0\u5546\u54C1\uFF0C\u5E76\u5728\u63D0\u793A\u4E2D\u5E26\u51FA\u53EF\u5B9A\u4F4D\u7684\u8D27\u53F7\u6216\u7F16\u7801"
);
assertEqual(
  pageSource.includes("editingProductNameRowKey") && pageSource.includes("startEditingProductName(row)") && pageSource.includes("commitProductNameEdit(row)") && pageSource.includes("saveRowPatch(row, { \u5546\u54C1\u540D\u79F0: productName })"),
  true,
  "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u652F\u6301\u53CC\u51FB\u8FDB\u5165\u7F16\u8F91\uFF0C\u5E76\u590D\u7528\u660E\u7EC6\u4FDD\u5B58\u63A5\u53E3\u5199\u56DE\u4E2D\u6587\u5546\u54C1\u540D"
);
assertEqual(
  pageStyleSource.includes(".container-detail-product-name-editable") && pageStyleSource.includes(".container-detail-product-name-input"),
  true,
  "\u5546\u54C1\u540D\u79F0\u53CC\u51FB\u7F16\u8F91\u5E94\u6709\u7A33\u5B9A\u6837\u5F0F\u7C7B\uFF0C\u907F\u514D\u8F93\u5165\u6001\u6539\u53D8\u8868\u683C\u5E03\u5C40"
);
assertEqual(
  pageSource.includes("const detectionItems = buildContainerDetailDetectionItems(scopedRows)"),
  true,
  "\u9875\u9762\u5339\u914D\u56FD\u5185\u6570\u636E\u68C0\u6D4B\u8BF7\u6C42\u5E94\u590D\u7528\u7EDF\u4E00\u68C0\u6D4B\u9879 helper"
);
assertEqual(
  containerDetailLogicSource.includes("SupplierCode: getContainerDetailSupplierCode(row)") && !containerDetailLogicSource.includes("Barcode: getContainerDetailBarcode(row)"),
  true,
  "\u5339\u914D\u56FD\u5185\u6570\u636E\u68C0\u6D4B\u8BF7\u6C42\u5E94\u4F7F\u7528\u884C\u4F9B\u5E94\u5546\u7F16\u7801\u4E14\u4E0D\u518D\u63D0\u4EA4\u6761\u7801\u515C\u5E95"
);
assertEqual(
  pageSource.includes("void reconcileLoadedMatchStatus(result.items, currentRequestId)") && pageSource.includes("products.filter((row) => getContainerDetailProductCode(row) || getContainerDetailItemNumber(row))") && pageSource.includes("buildContainerDetailMatchStatusUpdates(rowsNeedingMatchStatus, detected)") && pageSource.includes("\u52A0\u8F7D\u6001\u53EA\u6821\u6B63\u8868\u683C\u5C55\u793A\u72B6\u6001\uFF0C\u4E0D\u5199\u5E93"),
  true,
  "\u9875\u9762\u52A0\u8F7D\u540E\u5E94\u5BF9\u5F53\u524D\u61D2\u52A0\u8F7D\u5757\u53EA\u8BFB\u6821\u6B63\u5339\u914D\u72B6\u6001\uFF0C\u907F\u514D\u65E7\u9519\u8BEF MatchType \u7559\u5728\u8868\u683C\u4E2D\u4E14\u907F\u514D\u5199\u5E93"
);
assertEqual(
  pageSource.includes("SkipRelatedProductSync: true"),
  true,
  "\u5339\u914D\u56FD\u5185\u6570\u636E\u5E94\u53EA\u66F4\u65B0\u8D27\u67DC\u660E\u7EC6\uFF0C\u8DF3\u8FC7\u5173\u8054\u5546\u54C1\u540C\u6B65"
);
assertEqual(
  pageSource.includes("value={getContainerDetailEnglishName(row) ?? ''}"),
  true,
  "\u82F1\u6587\u540D\u79F0\u8F93\u5165\u6846\u5E94\u53D7\u63A7\u7ED1\u5B9A\u884C\u72B6\u6001\uFF0C\u6279\u91CF\u7FFB\u8BD1\u540E\u624D\u80FD\u7ACB\u5373\u5237\u65B0\u663E\u793A"
);
assertEqual(
  pageSource.includes("defaultValue={getContainerDetailEnglishName(row)}"),
  false,
  "\u82F1\u6587\u540D\u79F0\u8F93\u5165\u6846\u4E0D\u80FD\u4F7F\u7528 defaultValue\uFF0C\u5426\u5219\u6279\u91CF\u7FFB\u8BD1\u540E\u7684\u72B6\u6001\u53D8\u5316\u4E0D\u4F1A\u5237\u65B0\u5DF2\u6302\u8F7D\u8F93\u5165\u6846"
);
assertEqual(
  pageSource.includes("<Input.TextArea"),
  true,
  "\u82F1\u6587\u540D\u79F0\u8F93\u5165\u6846\u5E94\u4F7F\u7528 TextArea \u652F\u6301\u957F\u82F1\u6587\u81EA\u52A8\u6362\u884C"
);
assertEqual(
  pageSource.includes("autoSize={{ minRows: 1, maxRows: 2 }}"),
  true,
  "\u82F1\u6587\u540D\u79F0\u8F93\u5165\u6846\u81EA\u52A8\u6362\u884C\u6700\u591A\u663E\u793A 2 \u884C"
);
assertEqual(
  pageSource.includes("setSelectedRowKeys([])"),
  true,
  "\u7B5B\u9009\u6761\u4EF6\u53D8\u5316\u65F6\u5E94\u6E05\u7A7A\u5DF2\u9009\u660E\u7EC6\uFF0C\u907F\u514D\u9690\u85CF\u9009\u4E2D\u884C\u540E\u6279\u91CF\u64CD\u4F5C\u9000\u56DE\u4F5C\u7528\u4E8E\u5F53\u524D\u5168\u90E8\u53EF\u89C1\u884C"
);
assertEqual(
  pageSource.includes("[active, activeLoadQueryKey]") && pageSource.includes("\u6807\u7B7E\u4E0D\u8FDB\u5165 detailQueryKey\uFF1B\u53EA\u6709\u975E\u6807\u7B7E\u8FDC\u7A0B\u7B5B\u9009\u53D8\u5316\u624D\u91CD\u7F6E\u61D2\u52A0\u8F7D\u7ED3\u679C\u3002"),
  true,
  "\u8FDC\u7A0B\u91CD\u8F7D effect \u5E94\u76D1\u542C active \u548C base \u67E5\u8BE2 key\uFF0C\u6807\u7B7E\u5207\u6362\u4E0D\u5E94\u89E6\u53D1 reset reload"
);
assertEqual(
  pageSource.includes("{ value: 'all', label: t('containers.filters.allTags'), color: 'blue' }"),
  true,
  "\u5168\u90E8\u6807\u7B7E\u7EDF\u8BA1\u9879\u5E94\u4FDD\u6301\u84DD\u8272\uFF0C\u4F5C\u4E3A\u603B\u89C8\u5165\u53E3"
);
assertEqual(
  pageSource.includes("{ value: 'new', label: t('containers.tags.newProduct'), color: 'cyan' }"),
  true,
  "\u65B0\u5546\u54C1\u7EDF\u8BA1\u9879\u5E94\u4F7F\u7528\u4E0D\u540C\u4E8E\u5168\u90E8\u6807\u7B7E\u7684\u989C\u8272"
);
assertEqual(
  pageSource.includes("{ value: 'multi', label: t('containers.productTypes.multiCode'), color: 'purple' }"),
  true,
  "\u7EDF\u8BA1 tag \u5E94\u5305\u542B\u591A\u7801\u5546\u54C1\u7C7B\u578B\u5165\u53E3"
);
assertEqual(
  pageSource.includes("productTypeFilter"),
  false,
  "\u9876\u90E8\u72EC\u7ACB\u5546\u54C1\u7C7B\u578B\u4E0B\u62C9\u5DF2\u53D6\u6D88\uFF0C\u5546\u54C1\u7C7B\u578B\u8FC7\u6EE4\u5E94\u901A\u8FC7\u7EDF\u8BA1 tag \u548C\u5217\u5934\u7B5B\u9009\u5B8C\u6210"
);
assertEqual(
  tagFiltersSource.includes("color={option.color}"),
  true,
  "\u7EDF\u8BA1\u6807\u7B7E\u5E94\u59CB\u7EC8\u6309\u5404\u81EA\u8BED\u4E49\u8272\u663E\u793A\uFF0C\u4E0D\u53EA\u5728\u9009\u4E2D\u65F6\u663E\u793A\u84DD\u8272"
);
assertEqual(
  pageSource.includes("const targetRows = selectedRowKeys.length ? selectedRows : displayRows"),
  true,
  "\u6279\u91CF\u76EE\u6807\u884C\u5E94\u6309\u662F\u5426\u5B58\u5728\u9009\u62E9\u610F\u56FE\u5224\u65AD\uFF0C\u9690\u85CF\u9009\u4E2D\u884C\u65F6\u4E0D\u80FD\u9000\u56DE\u5F53\u524D\u5168\u90E8\u53EF\u89C1\u884C\u4E14\u672A\u9009\u4E2D\u65F6\u4F7F\u7528\u6700\u7EC8\u53EF\u89C1\u884C"
);
assertEqual(
  pageSource.includes("const ensureTargetRowsVisible = () => {"),
  true,
  "\u6279\u91CF\u64CD\u4F5C\u5E94\u5728\u5DF2\u9009\u884C\u88AB\u5F53\u524D\u7B5B\u9009\u9690\u85CF\u65F6\u7EDF\u4E00\u62E6\u622A"
);
assertEqual(
  pageSource.includes("t('containers.messages.selectedRowsHidden'"),
  true,
  "\u9690\u85CF\u9009\u4E2D\u884C\u89E6\u53D1\u6279\u91CF\u64CD\u4F5C\u65F6\u5E94\u4F7F\u7528 i18n \u63D0\u793A\u7528\u6237\u91CD\u65B0\u9009\u62E9"
);
assertEqual(tagFiltersSource.includes('role="button"'), true, "\u7EDF\u8BA1 tag \u5E94\u63D0\u4F9B\u6309\u94AE\u8BED\u4E49");
assertEqual(tagFiltersSource.includes("tabIndex={0}"), true, "\u7EDF\u8BA1 tag \u5E94\u53EF\u901A\u8FC7\u952E\u76D8\u805A\u7126");
assertEqual(tagFiltersSource.includes("aria-pressed={active}"), true, "\u7EDF\u8BA1 tag \u5E94\u66B4\u9732\u5F53\u524D\u9009\u4E2D\u72B6\u6001");
assertEqual(
  tagFiltersSource.includes("event.key === 'Enter' || event.key === ' '"),
  true,
  "\u7EDF\u8BA1 tag \u5E94\u652F\u6301 Enter \u548C\u7A7A\u683C\u952E\u89E6\u53D1\u8FC7\u6EE4"
);
var hqSelectionRows = [
  { id: 10, hguid: "detail-10", \u5546\u54C1\u7F16\u7801: "HB001", \u662F\u5426\u65B0\u5546\u54C1: false },
  { id: 11, hguid: "detail-11", \u5546\u54C1\u7F16\u7801: "HB002", \u662F\u5426\u65B0\u5546\u54C1: true },
  { id: 12, hguid: "detail-12", \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: "HB003" }, \u662F\u5426\u65B0\u5546\u54C1: false },
  { id: 13, hguid: "detail-13", \u5546\u54C1\u7F16\u7801: "HB001", \u662F\u5426\u65B0\u5546\u54C1: false },
  { id: 14, hguid: "detail-14", \u662F\u5426\u65B0\u5546\u54C1: false }
];
assertDeepEqual(
  buildContainerDetailHqPushSelection(hqSelectionRows),
  {
    productCodes: ["HB001", "HB002", "HB003"],
    items: [
      {
        productCode: "HB001",
        localSupplierCode: void 0,
        itemNumber: void 0,
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: void 0,
        importPrice: void 0,
        oemPrice: void 0,
        isNewProduct: false
      },
      {
        productCode: "HB002",
        localSupplierCode: void 0,
        itemNumber: void 0,
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: void 0,
        importPrice: void 0,
        oemPrice: void 0,
        isNewProduct: true
      },
      {
        productCode: "HB003",
        localSupplierCode: void 0,
        itemNumber: void 0,
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: void 0,
        importPrice: void 0,
        oemPrice: void 0,
        isNewProduct: false
      }
    ],
    skippedNewProductCount: 0,
    missingProductCodeCount: 1
  },
  "\u53D1\u9001\u5230 HQ \u5E94\u6536\u96C6\u6709\u6709\u6548\u5339\u914D\u4FE1\u606F\u7684\u9009\u4E2D\u660E\u7EC6\uFF0C\u5E76\u5BF9\u5546\u54C1\u7F16\u7801\u53BB\u91CD"
);
assertDeepEqual(
  buildContainerDetailHqPushSelection([
    {
      id: 15,
      hguid: "detail-15",
      \u5546\u54C1\u7F16\u7801: " HB015 ",
      \u662F\u5426\u65B0\u5546\u54C1: 1,
      \u56FD\u5185\u4EF7\u683C: 4.2,
      \u8FDB\u53E3\u4EF7\u683C: 1.55,
      \u8D34\u724C\u4EF7\u683C: 1.72,
      localSupplierCode: "DATS",
      \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "72653" }
    },
    {
      id: 16,
      hguid: "detail-16",
      \u5546\u54C1\u7F16\u7801: "   ",
      \u5546\u54C1\u540D\u79F0: "\u8D27\u67DC\u5546\u54C1\u540D",
      \u82F1\u6587\u540D\u79F0: "Container Product",
      \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u7F16\u7801: " HB016 ", \u8D27\u53F7: "72654", localSupplierCode: "COS", \u6761\u5F62\u7801: "9527000016", \u5546\u54C1\u56FE\u7247: "local-product-image.jpg" },
      \u662F\u5426\u65B0\u5546\u54C1: false,
      \u56FD\u5185\u4EF7\u683C: 5.1,
      \u8FDB\u53E3\u4EF7\u683C: 1.88,
      \u8D34\u724C\u4EF7\u683C: 2.01,
      warehouseOEMPrice: 3.21,
      warehouseIsActive: false
    },
    { id: 17, hguid: "detail-17", \u5546\u54C1\u7F16\u7801: "HB017", \u662F\u5426\u65B0\u5546\u54C1: true, warehouseIsActive: true }
  ]),
  {
    productCodes: ["HB015", "HB016", "HB017"],
    items: [
      {
        productCode: "HB015",
        localSupplierCode: "DATS",
        itemNumber: "72653",
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: 4.2,
        importPrice: 1.55,
        oemPrice: 1.72,
        isNewProduct: true
      },
      {
        productCode: "HB016",
        localSupplierCode: "COS",
        itemNumber: "72654",
        productName: "\u8D27\u67DC\u5546\u54C1\u540D",
        englishName: "Container Product",
        barcode: "9527000016",
        imageUrl: "local-product-image.jpg",
        domesticPrice: 5.1,
        importPrice: 1.88,
        oemPrice: 3.21,
        isNewProduct: false
      },
      {
        productCode: "HB017",
        localSupplierCode: void 0,
        itemNumber: void 0,
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: void 0,
        importPrice: void 0,
        oemPrice: void 0,
        isNewProduct: true
      }
    ],
    skippedNewProductCount: 0,
    missingProductCodeCount: 0
  },
  "\u53D1\u9001\u5230 HQ \u4E0D\u5E94\u56E0\u65B0\u5546\u54C1\u9875\u9762\u72B6\u6001\u8DF3\u8FC7\u5019\u9009\uFF0C\u5E76\u5728\u660E\u7EC6\u7F16\u7801\u4E3A\u7A7A\u767D\u65F6\u56DE\u9000\u5546\u54C1\u4FE1\u606F\u7F16\u7801"
);
assertDeepEqual(
  buildContainerDetailHqPushSelection([
    { id: 20, hguid: "detail-20", \u5546\u54C1\u7F16\u7801: "HB020", \u662F\u5426\u65B0\u5546\u54C1: true },
    {
      id: 21,
      hguid: "detail-21",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      localSupplierCode: "DATS",
      \u5546\u54C1\u4FE1\u606F: { \u8D27\u53F7: "72655" },
      \u56FD\u5185\u4EF7\u683C: 6.2,
      \u8FDB\u53E3\u4EF7\u683C: 2.11,
      \u8D34\u724C\u4EF7\u683C: 2.34,
      WarehouseOEMPrice: 4.56,
      warehouseIsActive: true
    }
  ]),
  {
    productCodes: ["HB020"],
    items: [
      {
        productCode: "HB020",
        localSupplierCode: void 0,
        itemNumber: void 0,
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: void 0,
        importPrice: void 0,
        oemPrice: void 0,
        isNewProduct: true
      },
      {
        productCode: void 0,
        localSupplierCode: "DATS",
        itemNumber: "72655",
        productName: void 0,
        englishName: void 0,
        barcode: void 0,
        imageUrl: void 0,
        domesticPrice: 6.2,
        importPrice: 2.11,
        oemPrice: 4.56,
        isNewProduct: false
      }
    ],
    skippedNewProductCount: 0,
    missingProductCodeCount: 0
  },
  "\u65B0\u5546\u54C1\u9875\u9762\u72B6\u6001\u4E0D\u62E6\u622A\u53D1\u9001\uFF0C\u7F3A\u5546\u54C1\u7F16\u7801\u4F46\u6709\u4F9B\u5E94\u5546\u548C\u8D27\u53F7\u65F6\u4E5F\u5E94\u643A\u5E26\u5019\u9009\u9879"
);
assertDeepEqual(
  buildContainerDetailHqPushSelection([
    {
      id: 22,
      hguid: "detail-22",
      \u5546\u54C1\u7F16\u7801: "HB022",
      \u662F\u5426\u65B0\u5546\u54C1: false,
      warehouseIsActive: true,
      \u56FD\u5185\u4EF7\u683C: 3.2,
      \u8FDB\u53E3\u4EF7\u683C: 1.08,
      \u8D34\u724C\u4EF7\u683C: 1.2,
      warehouseOEMPrice: 2.4,
      \u5546\u54C1\u56FE\u7247: "container-hb022.jpg"
    },
    {
      id: 23,
      hguid: "detail-23",
      \u5546\u54C1\u7F16\u7801: "HB023",
      \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u56FE\u7247: "product-hb023.jpg" },
      \u662F\u5426\u65B0\u5546\u54C1: false,
      warehouseIsActive: false,
      \u56FD\u5185\u4EF7\u683C: 3.5,
      \u8FDB\u53E3\u4EF7\u683C: 1.18,
      \u8D34\u724C\u4EF7\u683C: 1.3
    }
  ]).items,
  [
    {
      productCode: "HB022",
      localSupplierCode: void 0,
      itemNumber: void 0,
      productName: void 0,
      englishName: void 0,
      barcode: void 0,
      imageUrl: "container-hb022.jpg",
      domesticPrice: 3.2,
      importPrice: 1.08,
      oemPrice: 2.4,
      isNewProduct: false
    },
    {
      productCode: "HB023",
      localSupplierCode: void 0,
      itemNumber: void 0,
      productName: void 0,
      englishName: void 0,
      barcode: void 0,
      imageUrl: "product-hb023.jpg",
      domesticPrice: 3.5,
      importPrice: 1.18,
      oemPrice: void 0,
      isNewProduct: false
    }
  ],
  "\u53D1\u9001\u5230 HQ \u7684\u5019\u9009\u9879\u5E94\u4FDD\u7559\u56FE\u7247\u548C\u4EF7\u683C\uFF0C\u4F46\u4E0D\u5F97\u643A\u5E26\u4ED3\u5E93\u4E0A\u4E0B\u67B6\u72B6\u6001"
);
assertEqual(
  getContainerDetailImageUrl({
    id: 24,
    hguid: "detail-24",
    \u5546\u54C1\u56FE\u7247: "  container-detail-image.jpg  ",
    \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u56FE\u7247: "product-info-image.jpg" }
  }),
  "container-detail-image.jpg",
  "\u8D27\u67DC\u660E\u7EC6\u56FE\u7247\u5C55\u793A\u5E94\u4F18\u5148\u4F7F\u7528\u884C\u7EA7\u5546\u54C1\u56FE\u7247\u5E76\u6E05\u7406\u7A7A\u767D"
);
assertEqual(
  getContainerDetailImageUrl({
    id: 25,
    hguid: "detail-25",
    \u5546\u54C1\u56FE\u7247: "   ",
    \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u56FE\u7247: "  product-info-image.jpg  " }
  }),
  "product-info-image.jpg",
  "\u8D27\u67DC\u660E\u7EC6\u56FE\u7247\u5C55\u793A\u5E94\u5728\u884C\u7EA7\u56FE\u7247\u4E3A\u7A7A\u65F6\u56DE\u9000\u5546\u54C1\u4FE1\u606F\u56FE\u7247"
);
assertEqual(
  getContainerDetailImageUrl({
    id: 26,
    hguid: "detail-26",
    \u5546\u54C1\u56FE\u7247: "   ",
    \u5546\u54C1\u4FE1\u606F: { \u5546\u54C1\u56FE\u7247: "   " }
  }),
  void 0,
  "\u8D27\u67DC\u660E\u7EC6\u56FE\u7247\u5C55\u793A\u5728\u6240\u6709\u56FE\u7247\u5B57\u6BB5\u4E3A\u7A7A\u65F6\u5E94\u8FD4\u56DE\u7A7A\u503C"
);
var normalizedPushFailure = normalizeContainerDetailPushToHqPayload({
  failedCount: 2,
  errors: ["\u5546\u54C1\u4E0D\u5B58\u5728", "\u4EF7\u683C\u5F02\u5E38"]
});
assertEqual(normalizedPushFailure?.successCount, 0, "\u53D1\u9001\u5230 HQ \u5931\u8D25 payload \u5E94\u5F52\u4E00\u6210\u529F\u6570");
assertEqual(normalizedPushFailure?.failedCount, 2, "\u53D1\u9001\u5230 HQ \u5931\u8D25 payload \u5E94\u5F52\u4E00\u5931\u8D25\u6570");
assertEqual(normalizedPushFailure?.totalCount, 2, "\u53D1\u9001\u5230 HQ \u5931\u8D25 payload \u7F3A\u5C11 totalCount \u65F6\u5E94\u4F7F\u7528\u6210\u529F\u6570\u52A0\u5931\u8D25\u6570\u515C\u5E95");
assertEqual(normalizedPushFailure?.affectedRowCount, 0, "\u53D1\u9001\u5230 HQ \u5931\u8D25 payload \u5E94\u5F52\u4E00 HQ \u5F71\u54CD\u8BB0\u5F55\u6570");
assertDeepEqual(normalizedPushFailure?.errors, ["\u5546\u54C1\u4E0D\u5B58\u5728", "\u4EF7\u683C\u5F02\u5E38"], "\u53D1\u9001\u5230 HQ \u5931\u8D25 payload \u5E94\u4FDD\u7559\u9519\u8BEF\u660E\u7EC6");
var rootPayloadPushFailure = extractPushToHqErrorResult({
  payload: {
    success: false,
    message: "\u540E\u7AEF\u660E\u786E\u8FD4\u56DE\u5931\u8D25"
  }
});
assertEqual(rootPayloadPushFailure?.message, "\u540E\u7AEF\u660E\u786E\u8FD4\u56DE\u5931\u8D25", "\u53D1\u9001\u5230 HQ \u5931\u8D25\u89E3\u6790\u5E94\u652F\u6301 payload \u6839\u5BF9\u8C61 message");
assertEqual(pageSource.includes("createPushProductsToHqJob({"), true, "\u9875\u9762\u5E94\u5148\u521B\u5EFA\u53D1\u9001\u5230 HQ \u540E\u53F0 job");
assertEqual(pageSource.includes("getPushProductsToHqJob"), true, "\u9875\u9762\u5E94\u901A\u8FC7\u67E5\u8BE2\u63A5\u53E3\u8F6E\u8BE2\u53D1\u9001\u5230 HQ job");
assertEqual(pageSource.includes("createHqSyncJobPoller({"), true, "\u9875\u9762\u5E94\u590D\u7528\u540E\u53F0 job \u8F6E\u8BE2\u5668\u7B49\u5F85\u53D1\u9001\u5230 HQ \u7EC8\u6001");
assertEqual(pageSource.includes("buildContainerDetailHqPushSelection(selectedRows)"), true, "\u9875\u9762\u5E94\u53EA\u57FA\u4E8E\u624B\u52A8\u9009\u4E2D\u7684\u660E\u7EC6\u6784\u5EFA\u53D1\u9001\u8303\u56F4");
assertEqual(pageSource.includes("items: selection.items"), true, "\u9875\u9762\u53D1\u9001\u5230 HQ \u65F6\u5E94\u628A\u5019\u9009 items \u4E00\u5E76\u53D1\u9001\u7ED9\u540E\u7AEF");
assertEqual(pageSource.includes("const pushToHqLoadingRef = useRef(false)"), true, "\u9875\u9762\u5E94\u4F7F\u7528 ref \u9501\u9632\u6B62\u8FDE\u7EED\u70B9\u51FB\u91CD\u590D\u53D1\u9001");
assertEqual(pageSource.includes("releasePushToHqLoading()"), true, "\u53D1\u9001\u5230 HQ job \u63D0\u4EA4\u6210\u529F\u540E\u5E94\u7ACB\u5373\u89E3\u9664\u6309\u94AE loading");
assertEqual(pageSource.includes("notification.info({") && pageSource.includes("key: pushToHqNotificationKey"), true, "\u53D1\u9001\u5230 HQ job \u63D0\u4EA4\u540E\u5E94\u5C55\u793A\u540E\u53F0\u6267\u884C\u901A\u77E5");
assertEqual(pageSource.includes("notification.success({") && pageSource.includes("notification.warning({") && pageSource.includes("notification.error({"), true, "\u53D1\u9001\u5230 HQ job \u7EC8\u6001\u5E94\u6309\u6210\u529F\u3001\u90E8\u5206\u6210\u529F\u548C\u5931\u8D25\u5C55\u793A\u901A\u77E5");
var pushToHqPollingSource = pageSource.slice(
  pageSource.indexOf("const pollPushToHqJob = ("),
  pageSource.indexOf("const handlePushSelectedProductsToHq = async () => {")
);
assertEqual(pushToHqPollingSource.includes("loadData()"), false, "\u53D1\u9001\u5230 HQ job \u7EC8\u6001\u4E0D\u5E94\u91CD\u65B0\u52A0\u8F7D\u8D27\u67DC\u660E\u7EC6\u8868\u683C");
assertEqual(pushToHqPollingSource.includes("showPushToHqResult"), false, "\u53D1\u9001\u5230 HQ job \u7EC8\u6001\u53EA\u4F7F\u7528\u53F3\u4E0A\u89D2\u901A\u77E5\uFF0C\u4E0D\u5E94\u518D\u5F39\u7ED3\u679C Modal");
assertEqual(pageSource.includes("message.warning(t('containers.messages.pushToHqSkippedNewProducts'"), false, "\u53D1\u9001\u5230 HQ \u4E0D\u5E94\u518D\u56E0\u9875\u9762\u65B0\u5546\u54C1\u72B6\u6001\u7ED9\u51FA\u8DF3\u8FC7 warning");
assertEqual(pageSource.includes("\u53D1\u9001 HQ \u7684\u7ED3\u679C\u7EDF\u4E00\u6536\u655B\u5230\u53F3\u4E0A\u89D2\u901A\u77E5"), true, "\u53D1\u9001\u5230 HQ \u63D0\u4EA4\u5931\u8D25\u4E5F\u5E94\u4F7F\u7528\u53F3\u4E0A\u89D2\u901A\u77E5\u627F\u8F7D\u7ED3\u679C");
assertEqual(pageSource.includes("message: t('posAdmin.products.pushToHqFailed', '\u53D1\u9001\u5230 HQ \u5931\u8D25')"), true, "\u540E\u7AEF\u660E\u786E\u5931\u8D25\u65F6\u5E94\u5C55\u793A\u5931\u8D25\u901A\u77E5\u800C\u4E0D\u662F\u90E8\u5206\u6210\u529F");
assertEqual(pageSource.includes("result.warehouseInventoriesCreated"), true, "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A\u4ED3\u5E93\u5E93\u5B58\u65B0\u589E\u7EDF\u8BA1");
assertEqual(pageSource.includes("result.warehouseInventoriesUpdated"), true, "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A\u4ED3\u5E93\u5E93\u5B58\u66F4\u65B0\u7EDF\u8BA1");
assertEqual(pageSource.includes("result.storeRetailPricesCreated"), true, "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A\u5206\u5E97\u4EF7\u683C\u65B0\u589E\u7EDF\u8BA1");
assertEqual(pageSource.includes("result.productSetCodesCreated"), true, "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A\u5957\u88C5\u591A\u7801\u65B0\u589E\u7EDF\u8BA1");
assertEqual(pageSource.includes("result.storeMultiCodesCreated"), true, "\u7ED3\u679C\u5F39\u7A97\u5E94\u5C55\u793A\u5206\u5E97\u591A\u7801\u65B0\u589E\u7EDF\u8BA1");
assertEqual(pageSource.includes("disabled={!selectedRowKeys.length || pushToHqLoading}"), true, "\u53D1\u9001\u5230 HQ \u6309\u94AE\u5FC5\u987B\u8981\u6C42\u624B\u52A8\u9009\u4E2D\u660E\u7EC6");
var pushToHqHandlerSource = pageSource.slice(
  pageSource.indexOf("const handlePushSelectedProductsToHq = async () => {"),
  pageSource.indexOf("const renderCreateProductResultItems = (items: ContainerProductCreationResultItem[]) => {")
);
assertEqual(
  pushToHqHandlerSource.includes("if (!ensureNoPendingPriceDetails()) return") && pushToHqHandlerSource.indexOf("if (!ensureNoPendingPriceDetails()) return") < pushToHqHandlerSource.indexOf("const selection = buildContainerDetailHqPushSelection(selectedRows)"),
  true,
  "\u53D1\u9001\u5230 HQ \u524D\u5E94\u963B\u6B62\u672A\u4FDD\u5B58\u7684\u8FDB\u53E3\u4EF7\u683C\u548C\u96F6\u552E\u4EF7\u7EE7\u7EED\u6D41\u8F6C"
);
assertEqual(
  pageSource.includes("createContainerProductCreationJob({"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5E94\u63D0\u4EA4\u540E\u7AEF\u540E\u53F0 job\uFF0C\u800C\u4E0D\u662F\u7531\u9875\u9762\u4E32\u884C\u5199\u591A\u4E2A\u670D\u52A1"
);
assertEqual(
  pageSource.includes("buildContainerCreateProductsOperationId(containerGuid, detailHguids)"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5E94\u4F7F\u7528\u8D27\u67DC\u548C\u660E\u7EC6\u751F\u6210\u7A33\u5B9A operationId"
);
assertEqual(
  pageSource.includes("waitForContainerProductCreationJob(job.jobId)"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5E94\u8F6E\u8BE2\u540E\u53F0 job \u76F4\u5230\u7EC8\u6001"
);
var createNewProductsHandlerSource = pageSource.slice(
  pageSource.indexOf("const createNewProducts = async () => {"),
  pageSource.indexOf("const updateExistingPurchase = async () => {")
);
assertEqual(
  createNewProductsHandlerSource.includes("if (!ensureNoPendingPriceDetails()) return") && createNewProductsHandlerSource.indexOf("if (!ensureNoPendingPriceDetails()) return") < createNewProductsHandlerSource.indexOf("const scopedRows = await confirmBatchRows"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u524D\u5E94\u63D0\u793A\u5148\u4FDD\u5B58\u660E\u7EC6\u4EF7\u683C\uFF0C\u907F\u514D\u540E\u53F0 job \u8BFB\u53D6\u65E7\u4EF7\u683C"
);
var createProductsJobSource = pageSource.slice(
  pageSource.indexOf("const showCreateProductsJobResult = (job: ContainerProductCreationJob) => {"),
  pageSource.indexOf("const updateExistingPurchase = async () => {")
);
assertEqual(createProductsJobSource.includes("loadData()"), false, "\u6279\u91CF\u521B\u5EFA\u65B0\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u7EC8\u6001\u4E0D\u5E94\u81EA\u52A8\u5237\u65B0\u8D27\u67DC\u660E\u7EC6\u8868\u683C");
assertEqual(createProductsJobSource.includes("Modal."), false, "\u6279\u91CF\u521B\u5EFA\u65B0\u5546\u54C1\u540E\u53F0\u4EFB\u52A1\u7EC8\u6001\u53EA\u4F7F\u7528\u53F3\u4E0A\u89D2\u901A\u77E5\uFF0C\u4E0D\u5E94\u518D\u5F39\u7ED3\u679C Modal");
assertEqual(
  pageSource.includes("createPushProductsToHqJob") && pageSource.includes("getPushProductsToHqJob"),
  true,
  "\u53D1\u9001\u5230 HQ \u5E94\u5BFC\u5165\u540E\u53F0 job \u521B\u5EFA\u548C\u67E5\u8BE2\u63A5\u53E3"
);
assertEqual(
  pageSource.includes("const updateFields = await confirmPushToHqUpdateFields(selection.items.length)") && pageSource.includes("buildPushProductsToHqOperationId(containerGuid, selection.productCodes, selection.items.length, updateFields)") && pageSource.includes("updateFields,"),
  true,
  "\u8D27\u67DC\u53D1\u9001\u5230 HQ \u5E94\u5148\u52FE\u9009\u66F4\u65B0\u5B57\u6BB5\uFF0C\u5E76\u628A\u5B57\u6BB5\u9009\u62E9\u4F20\u5165 job \u4E0E operationId"
);
assertEqual(
  pageSource.includes("function UpdateFieldSelector") && pageSource.includes("indeterminate={isPartiallySelected}") && pageSource.includes("t('common.selectAll', '\u5168\u9009')") && pageSource.includes("getNextUpdateFieldSelection(event.target.checked, allValues)") && pageSource.includes("getUpdateFieldSelectionState(selectedFields, allValues)") && pageSource.includes("value={selectedFields}"),
  true,
  "\u5B57\u6BB5\u9009\u62E9\u5F39\u7A97\u5E94\u63D0\u4F9B\u5168\u9009\u590D\u9009\u6846\uFF0C\u5E76\u7528\u53D7\u63A7\u52FE\u9009\u72B6\u6001\u540C\u6B65\u5B57\u6BB5\u5217\u8868"
);
assertEqual(
  posProductTypeSource.includes("type MissingPushProductsToHqUpdateFieldOption = Exclude<PushProductsToHqUpdateField, PushProductsToHqUpdateFieldOptionValue>") && posProductTypeSource.includes("const assertAllPushProductsToHqUpdateFieldsCovered: Record<MissingPushProductsToHqUpdateFieldOption, never> = {}") && pageSource.includes("pushProductsToHqUpdateFieldOptions") && pageSource.includes("defaultPushProductsToHqUpdateFields"),
  true,
  "\u53D1\u9001 HQ \u5B57\u6BB5\u6E05\u5355\u5E94\u590D\u7528\u5171\u4EAB\u5B9A\u4E49\u5E76\u6709\u7F16\u8BD1\u671F\u8986\u76D6\u68C0\u67E5\uFF0C\u907F\u514D\u7C7B\u578B\u65B0\u589E\u5B57\u6BB5\u4F46\u5F39\u7A97\u6F0F\u5217"
);
assertEqual(
  pageSource.includes("updateProduct(code, { purchasePrice: row.\u8FDB\u53E3\u4EF7\u683C ?? 0 })"),
  false,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u4E0D\u5E94\u8C03\u7528\u666E\u901A POS \u5546\u54C1\u6574\u5BF9\u8C61\u66F4\u65B0\u63A5\u53E3\uFF0C\u907F\u514D\u6E05\u7A7A\u540D\u79F0\u3001\u6761\u7801\u548C\u4E0A\u4E0B\u67B6\u72B6\u6001"
);
assertEqual(
  pageSource.indexOf("await batchUpdateWarehouseProducts(warehouseUpdates") < pageSource.indexOf("await upsertRetailForActiveStores(retailUpdates)"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5E94\u5148\u786E\u8BA4\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u6210\u529F\uFF0C\u518D\u7EE7\u7EED\u5206\u5E97\u4EF7\u683C upsert"
);
assertEqual(
  pageSource.includes("item.OEMPrice = oemPrice") && pageSource.includes("item.StoreRetailPriceValue = oemPrice") && pageSource.includes("item.MultiCodeRetailPrice = oemPrice"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5E94\u540C\u65F6\u63D0\u4EA4\u6709\u6548\u96F6\u552E\u4EF7\uFF0C\u8865\u9F50\u5546\u54C1\u4E3B\u8868\u548C\u5206\u5E97\u96F6\u552E\u4EF7"
);
assertEqual(
  pageSource.includes("const oemPrice = getContainerDetailVisibleOemPrice(row)") && pageSource.includes("const hasPositiveOemPrice = (row: ContainerDetail) => (getContainerDetailVisibleOemPrice(row) ?? 0) > 0") && pageSource.includes("shouldUpdate('oemPrice') && hasPositiveOemPrice(row)") && !pageSource.includes("hasImportDiff || hasOemDiff"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5E94\u4EE5\u8868\u683C\u53EF\u89C1\u96F6\u552E\u4EF7\u4E3A\u51C6\u63D0\u4EA4\u4EF7\u683C\uFF0C\u4E0D\u5E94\u56E0\u68C0\u6D4B\u5230\u7684\u4ED3\u5E93\u4EF7\u683C\u76F8\u540C\u800C\u8DF3\u8FC7"
);
assertEqual(
  pageSource.includes("message.error(error instanceof Error ? error.message : t('containers.messages.purchasePricesUpdateFailed', '\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5931\u8D25'))"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5931\u8D25\u65F6\u5E94\u7ED9\u7528\u6237\u53EF\u89C1\u9519\u8BEF\u63D0\u793A"
);
var updateExistingPurchaseHandlerSource = pageSource.slice(
  pageSource.indexOf("const updateExistingPurchase = async () => {"),
  pageSource.indexOf("const deleteSelected = () => {")
);
assertEqual(
  updateExistingPurchaseHandlerSource.includes("if (!ensureNoPendingPriceDetails()) return") && updateExistingPurchaseHandlerSource.indexOf("if (!ensureNoPendingPriceDetails()) return") < updateExistingPurchaseHandlerSource.indexOf("const confirmed = await confirmBatchRowsWithUpdateFields"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u524D\u5E94\u963B\u6B62\u672A\u4FDD\u5B58\u7684\u624B\u52A8\u4EF7\u683C\u76F4\u63A5\u5199\u5165\u5546\u54C1\u548C\u5206\u5E97\u4EF7\u683C"
);
assertEqual(
  pageSource.indexOf("await batchUpdateWarehouseProducts(warehouseUpdates") < pageSource.indexOf("await upsertMultiCodeForActiveStores(multiCodeUpdates)"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u5E94\u5148\u786E\u8BA4\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0\u6210\u529F\uFF0C\u518D\u7EE7\u7EED\u591A\u7801\u4EF7\u683C upsert"
);
assertEqual(
  pageSource.includes("syncStorePurchasePrice: shouldUpdate") && !updateExistingPurchaseHandlerSource.includes("IsActive: true"),
  true,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u5E94\u6309\u5B57\u6BB5\u52FE\u9009\u63A7\u5236\u5206\u5E97\u8FDB\u8D27\u4EF7\u540C\u6B65\u4E14\u4E0D\u518D\u5F3A\u5236\u6539\u4E0A\u4E0B\u67B6\u72B6\u6001"
);
assertEqual(
  warehouseProductServiceSource.includes("ensureApiSuccess(raw?.success ?? raw?.isSuccess, raw?.message, '\u4ED3\u5E93\u6279\u91CF\u66F4\u65B0\u5931\u8D25')"),
  true,
  "\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0 service \u5E94\u6821\u9A8C\u6839\u54CD\u5E94\u5931\u8D25\uFF0C\u5931\u8D25\u65F6\u963B\u65AD\u540E\u7EED\u5199\u8868"
);
assertEqual(
  warehouseProductServiceSource.includes("throw new Error(result.message || errors.join('\uFF1B') || '\u4ED3\u5E93\u6279\u91CF\u66F4\u65B0\u90E8\u5206\u5931\u8D25')"),
  true,
  "\u4ED3\u5E93\u5546\u54C1\u6279\u91CF\u66F4\u65B0 service \u5E94\u5728 failedCount/errors \u8868\u793A\u90E8\u5206\u5931\u8D25\u65F6\u629B\u9519"
);
assertEqual(
  pageSource.includes("!access.canEditContainer || !access.canManagePosProducts"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u51FD\u6570\u5185\u5E94\u540C\u65F6\u6821\u9A8C\u8D27\u67DC\u7F16\u8F91\u548C POS \u5546\u54C1\u7BA1\u7406\u6743\u9650"
);
assertEqual(
  pageSource.includes("access.canEditContainer && access.canManagePosProducts"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5165\u53E3\u5E94\u540C\u65F6\u8981\u6C42\u8D27\u67DC\u7F16\u8F91\u548C POS \u5546\u54C1\u7BA1\u7406\u6743\u9650"
);
assertEqual(
  pageSource.includes("createProductsLoadingRef.current"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5E94\u4F7F\u7528 ref \u9501\u9632\u6B62\u8FDE\u7EED\u70B9\u51FB\u91CD\u590D\u63D0\u4EA4"
);
assertEqual(
  pageSource.includes("pendingDetailSavePromisesRef") && pageSource.includes("failedDetailSaveKeysRef") && pageSource.includes("buildContainerDetailSaveFailureKeys(saveKey, patch)") && pageSource.includes("blurActiveContainerDetailEditableCell()") && pageSource.includes("flushPendingDetailSaves") && pageSource.includes("failedDetailSaveKeysRef.current.size > 0") && pageSource.indexOf("blurActiveContainerDetailEditableCell()") < pageSource.indexOf("await flushPendingDetailSaves()") && pageSource.indexOf("await flushPendingDetailSaves()") < pageSource.indexOf("const missingProductNameRows = findContainerDetailRowsMissingProductName(scopedRows)") && pageSource.indexOf("await flushPendingDetailSaves()") < pageSource.indexOf("const missingRetailPriceRows = findContainerDetailRowsMissingCreateProductRetailPrice(scopedRows)") && pageSource.indexOf("await flushPendingDetailSaves()") < pageSource.indexOf("const job = await createContainerProductCreationJob({"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u524D\u5FC5\u987B\u5148\u89E6\u53D1\u7F16\u8F91\u5355\u5143\u683C blur \u5E76\u7B49\u5F85\u8D27\u67DC\u660E\u7EC6\u4FDD\u5B58\u5B8C\u6210\uFF0C\u907F\u514D\u540E\u53F0 job \u8BFB\u53D6\u65E7\u503C"
);
assertEqual(
  pageSource.includes("pendingDetailSaveCount > 0") && pageSource.includes("disabled: createProductsLoading || pendingDetailSaveCount > 0"),
  true,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u5165\u53E3\u5E94\u5728\u660E\u7EC6\u4FDD\u5B58\u4E2D\u7981\u7528\uFF0C\u907F\u514D\u4FDD\u5B58\u7ADE\u6001"
);
assertEqual(
  pageSource.includes("handleDetailSaveError") && pageSource.includes(".catch(handleDetailSaveError)"),
  true,
  "\u884C\u5185\u4FDD\u5B58\u7684 fire-and-forget \u8C03\u7528\u5E94\u6355\u83B7\u5931\u8D25\uFF0C\u907F\u514D\u672A\u5904\u7406 Promise \u62D2\u7EDD"
);
assertEqual(
  pageSource.includes("await createProduct({"),
  false,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u9875\u9762\u4E0D\u5E94\u518D\u9010\u884C\u8C03\u7528 POS createProduct"
);
assertEqual(
  pageSource.includes("batchCreateProducts(created.map"),
  false,
  "\u521B\u5EFA\u65B0\u5546\u54C1\u9875\u9762\u4E0D\u5E94\u518D\u76F4\u63A5\u6279\u91CF\u5199\u4ED3\u5E93\u5546\u54C1"
);
assertEqual(
  pageSource.includes("catch(() => null)"),
  false,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u4E0D\u5E94\u541E\u6389 POS \u5546\u54C1\u66F4\u65B0\u5931\u8D25"
);
assertEqual(
  pageSource.includes("posUpdateFailures"),
  false,
  "\u66F4\u65B0\u5DF2\u6709\u5546\u54C1\u4EF7\u683C\u4E0D\u5E94\u518D\u4FDD\u7559 POS \u5546\u54C1\u6574\u5BF9\u8C61\u66F4\u65B0\u5931\u8D25\u5206\u652F"
);
var priceContainer = {
  id: 1,
  hguid: "container-1",
  \u6C47\u7387: 4.5,
  \u8FD0\u8D39: 12e3,
  \u603B\u4F53\u79EF: 67.44
};
var priceRows = [
  {
    id: 101,
    hguid: "price-101",
    \u56FD\u5185\u4EF7\u683C: 3.9,
    \u88C5\u67DC\u6570\u91CF: 1200,
    \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 6.744,
    \u8FD0\u8F93\u6210\u672C: 0,
    \u8FDB\u53E3\u4EF7\u683C: 1,
    \u8C03\u6574\u6D6E\u7387: 1
  },
  {
    id: 102,
    hguid: "price-102",
    \u56FD\u5185\u4EF7\u683C: 5.2,
    \u88C5\u67DC\u6570\u91CF: 600,
    \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 3.372,
    \u8FD0\u8F93\u6210\u672C: 0,
    \u8FDB\u53E3\u4EF7\u683C: 2,
    \u8C03\u6574\u6D6E\u7387: 1.1
  }
];
assertEqual(
  calculateContainerDetailTransportCost(priceRows[0], priceContainer),
  1,
  "\u8FD0\u8F93\u6210\u672C\u5E94\u6309\u8FD0\u8D39\u3001\u660E\u7EC6\u4F53\u79EF\u3001\u88C5\u67DC\u6570\u91CF\u3001\u603B\u4F53\u79EF\u5206\u644A\u4E3A\u5355\u4F4D\u6210\u672C\u5E76\u4FDD\u7559 2 \u4F4D"
);
assertEqual(
  calculateContainerDetailUnitTransportCost({ id: 109, hguid: "price-109", \u8FD0\u8F93\u6210\u672C: 0.05, \u5355\u4EF6\u88C5\u7BB1\u6570: 12 }),
  0.6,
  "\u5355\u4EF6\u8FD0\u8F93\u6210\u672C\u5E94\u7B49\u4E8E\u5355\u54C1\u8FD0\u8F93\u6210\u672C\u4E58\u5355\u4EF6\u88C5\u7BB1\u6570\u5E76\u4FDD\u7559 2 \u4F4D"
);
assertEqual(
  calculateContainerDetailImportPrice(priceRows[0], priceContainer, 1.2, 1),
  2.04,
  "\u8FDB\u53E3\u4EF7\u683C\u5E94\u6CBF\u7528\u5F53\u524D\u516C\u5F0F\u5E76\u4FDD\u7559 2 \u4F4D"
);
assertEqual(DEFAULT_CONTAINER_DETAIL_FLOAT_RATE, 1.3, "\u8D27\u67DC\u660E\u7EC6\u7A7A\u6D6E\u7387\u9ED8\u8BA4\u503C\u5E94\u4E3A 1.30");
assertDeepEqual(
  getContainerDetailCostMissingFields({ \u6C47\u7387: void 0, \u8FD0\u8D39: 0, \u603B\u4F53\u79EF: 10 }),
  ["exchangeRate"],
  "\u7F3A\u5C11\u6C47\u7387\u65F6\u5E94\u963B\u6B62\u6210\u672C\u91CD\u7B97\uFF0C\u4F46\u8FD0\u8D39 0 \u662F\u5408\u6CD5\u8F93\u5165"
);
assertDeepEqual(
  getContainerDetailCostMissingFields({ \u6C47\u7387: 4.5, \u8FD0\u8D39: void 0, \u603B\u4F53\u79EF: 0 }),
  ["freight", "totalVolume"],
  "\u7F3A\u5C11\u8FD0\u8D39\u6216\u603B\u4F53\u79EF\u65F6\u5E94\u963B\u6B62\u6210\u672C\u91CD\u7B97"
);
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 105, hguid: "price-105", \u88C5\u67DC\u4EF6\u6570: 2, \u88C5\u67DC\u6570\u91CF: 5, \u5546\u54C1\u4FE1\u606F: { \u5355\u4EF6\u4F53\u79EF: 0.5 } },
    { ...priceContainer, \u8FD0\u8D39: 100, \u603B\u4F53\u79EF: 10 }
  ),
  2,
  "\u660E\u7EC6\u7EA7\u5408\u8BA1\u4F53\u79EF\u7F3A\u5931\u65F6\u5E94\u4F7F\u7528\u5546\u54C1\u4FE1\u606F\u5355\u4EF6\u4F53\u79EF\u8BA1\u7B97\u5355\u4F4D\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 107, hguid: "price-107", \u88C5\u67DC\u4EF6\u6570: 2, \u88C5\u67DC\u6570\u91CF: 5, \u5355\u4EF6\u4F53\u79EF: 0.25, \u5546\u54C1\u4FE1\u606F: { \u5355\u4EF6\u4F53\u79EF: 0.5 } },
    { ...priceContainer, \u8FD0\u8D39: 100, \u603B\u4F53\u79EF: 10 }
  ),
  1,
  "\u660E\u7EC6\u5355\u4EF6\u4F53\u79EF\u5E94\u4F18\u5148\u4E8E\u5546\u54C1\u4FE1\u606F\u5355\u4EF6\u4F53\u79EF\u8BA1\u7B97\u5355\u4F4D\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 108, hguid: "price-108", \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 2, \u88C5\u67DC\u4EF6\u6570: 2, \u88C5\u67DC\u6570\u91CF: 5, \u5355\u4EF6\u4F53\u79EF: 0.25, \u5546\u54C1\u4FE1\u606F: { \u5355\u4EF6\u4F53\u79EF: 0.5 } },
    { ...priceContainer, \u8FD0\u8D39: 100, \u603B\u4F53\u79EF: 10 }
  ),
  4,
  "\u5408\u8BA1\u88C5\u67DC\u4F53\u79EF\u5E94\u4F18\u5148\u4E8E\u88C5\u67DC\u4EF6\u6570\u4E58\u5355\u4EF6\u4F53\u79EF"
);
assertEqual(
  calculateContainerDetailTransportCost(priceRows[0], { ...priceContainer, \u8FD0\u8D39: 0 }),
  0,
  "\u8FD0\u8D39\u4E3A 0 \u662F\u5408\u6CD5\u516C\u5F0F\u8F93\u5165\uFF0C\u5E94\u91CD\u7B97\u4E3A 0 \u800C\u4E0D\u662F\u4FDD\u7559\u65E7\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 0, \u8FD0\u8F93\u6210\u672C: 9 }, priceContainer),
  0,
  "\u660E\u7EC6\u4F53\u79EF\u4E3A 0 \u662F\u5408\u6CD5\u516C\u5F0F\u8F93\u5165\uFF0C\u5E94\u91CD\u7B97\u4E3A 0 \u800C\u4E0D\u662F\u4FDD\u7559\u65E7\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailImportPrice({ ...priceRows[0], \u56FD\u5185\u4EF7\u683C: 0, \u8FDB\u53E3\u4EF7\u683C: 9 }, priceContainer, 1.2, 10),
  10.91,
  "\u56FD\u5185\u4EF7\u683C\u4E3A 0 \u662F\u5408\u6CD5\u516C\u5F0F\u8F93\u5165\uFF0C\u5E94\u7EE7\u7EED\u53E0\u52A0\u8FD0\u8F93\u6210\u672C\u8BA1\u7B97\u8FDB\u53E3\u4EF7\u683C"
);
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(priceRows, priceContainer, 1.2),
  [
    { hguid: "price-101", \u8C03\u6574\u6D6E\u7387: 1.2, \u8FD0\u8F93\u6210\u672C: 1, \u8FDB\u53E3\u4EF7\u683C: 2.04, SkipRelatedProductSync: true },
    { hguid: "price-102", \u8C03\u6574\u6D6E\u7387: 1.2, \u8FD0\u8F93\u6210\u672C: 1, \u8FDB\u53E3\u4EF7\u683C: 2.35, SkipRelatedProductSync: true }
  ],
  "\u6279\u91CF\u5E94\u7528\u6D6E\u7387\u5E94\u540C\u65F6\u751F\u6210\u8C03\u6574\u6D6E\u7387\u3001\u8FD0\u8F93\u6210\u672C\u548C\u8FDB\u53E3\u4EF7\u683C\u66F4\u65B0"
);
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(priceRows, { ...priceContainer, \u6C47\u7387: 5, \u8FD0\u8D39: 9e3 }, void 0),
  [
    { hguid: "price-101", \u8C03\u6574\u6D6E\u7387: 1, \u8FD0\u8F93\u6210\u672C: 0.75, \u8FDB\u53E3\u4EF7\u683C: 1.39, SkipRelatedProductSync: true },
    { hguid: "price-102", \u8C03\u6574\u6D6E\u7387: 1.1, \u8FD0\u8F93\u6210\u672C: 0.75, \u8FDB\u53E3\u4EF7\u683C: 1.79, SkipRelatedProductSync: true }
  ],
  "\u6C47\u7387\u6216\u8FD0\u8D39\u53D8\u5316\u540E\u5E94\u6309\u6BCF\u884C\u73B0\u6709\u8C03\u6574\u6D6E\u7387\u91CD\u7B97\u4EF7\u683C"
);
assertDeepEqual(
  buildContainerDetailFloatRateUpdates([{ ...priceRows[0], \u8C03\u6574\u6D6E\u7387: void 0 }], priceContainer, void 0),
  [{ hguid: "price-101", \u8C03\u6574\u6D6E\u7387: 1.3, \u8FD0\u8F93\u6210\u672C: 1, \u8FDB\u53E3\u4EF7\u683C: 2.21, SkipRelatedProductSync: true }],
  "\u884C\u5185\u6D6E\u7387\u4E3A\u7A7A\u65F6\u5E94\u6309\u9ED8\u8BA4 1.30 \u91CD\u7B97\u8FD0\u8F93\u6210\u672C\u548C\u8FDB\u53E3\u4EF7\u683C\u5E76\u5199\u56DE\u6D6E\u7387"
);
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(
    [
      { ...priceRows[0], \u8FD0\u8F93\u6210\u672C: 1, \u8FDB\u53E3\u4EF7\u683C: 1.7 },
      { ...priceRows[1], \u8FD0\u8F93\u6210\u672C: 1, \u8FDB\u53E3\u4EF7\u683C: 2.16 }
    ],
    priceContainer,
    void 0
  ),
  [],
  "\u6210\u672C\u548C\u8FDB\u53E3\u4EF7\u6CA1\u6709\u53D8\u5316\u65F6\u4E0D\u5E94\u751F\u6210\u65E0\u5DEE\u5F02\u5199\u5E93\u66F4\u65B0"
);
assertDeepEqual(
  buildContainerDetailFloatRateUpdates(
    [{ id: 106, hguid: "price-106", \u8C03\u6574\u6D6E\u7387: 1, \u8FDB\u53E3\u4EF7\u683C: 8 }],
    { ...priceContainer, \u6C47\u7387: 0 },
    1.2
  ),
  [{ hguid: "price-106", \u8C03\u6574\u6D6E\u7387: 1.2, \u8FD0\u8F93\u6210\u672C: void 0, \u8FDB\u53E3\u4EF7\u683C: 8, SkipRelatedProductSync: true }],
  "\u5355\u884C\u4FEE\u6539\u6D6E\u7387\u65F6\u5373\u4F7F\u4EF7\u683C\u65E0\u6CD5\u91CD\u7B97\uFF0C\u4E5F\u5E94\u751F\u6210\u6D6E\u7387\u5199\u5E93\u66F4\u65B0"
);
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], \u88C5\u67DC\u6570\u91CF: 0, \u8FD0\u8F93\u6210\u672C: 9 }, priceContainer),
  9,
  "\u88C5\u67DC\u6570\u91CF\u4E3A 0 \u65F6\u5E94\u4FDD\u7559\u539F\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], \u88C5\u67DC\u6570\u91CF: -1, \u8FD0\u8F93\u6210\u672C: 9 }, priceContainer),
  9,
  "\u88C5\u67DC\u6570\u91CF\u4E3A\u8D1F\u6570\u65F6\u5E94\u4FDD\u7559\u539F\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost({ ...priceRows[0], \u88C5\u67DC\u6570\u91CF: void 0, \u8FD0\u8F93\u6210\u672C: 9 }, priceContainer),
  9,
  "\u7F3A\u5C11\u88C5\u67DC\u6570\u91CF\u65F6\u5E94\u4FDD\u7559\u539F\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailTransportCost(
    { id: 103, hguid: "price-103", \u5408\u8BA1\u88C5\u67DC\u4F53\u79EF: 1, \u8FD0\u8F93\u6210\u672C: 7 },
    { ...priceContainer, \u603B\u4F53\u79EF: 0 }
  ),
  7,
  "\u7F3A\u5C11\u53EF\u7528\u603B\u4F53\u79EF\u65F6\u5E94\u4FDD\u7559\u539F\u8FD0\u8F93\u6210\u672C"
);
assertEqual(
  calculateContainerDetailImportPrice(
    { id: 104, hguid: "price-104", \u56FD\u5185\u4EF7\u683C: void 0, \u8FDB\u53E3\u4EF7\u683C: 8 },
    priceContainer,
    1.2,
    10
  ),
  8,
  "\u7F3A\u5C11\u56FD\u5185\u4EF7\u683C\u65F6\u5E94\u4FDD\u7559\u539F\u8FDB\u53E3\u4EF7\u683C"
);
assertDeepEqual(
  [4.99, 6.99, 8.99].map((itemRetailPrice) => calculateContainerSetCodePurchasePrice(6.1, itemRetailPrice, 20.97)),
  [1.45, 2.03, 2.62],
  "\u5957\u88C5\u5B50\u9879\u8FDB\u8D27\u4EF7\u5E94\u6309\u5B50\u9879\u4EF7\u683C\u5360\u6BD4\u4ECE\u4E3B\u5546\u54C1\u8FDB\u53E3\u4EF7\u683C\u5206\u644A"
);
assertEqual(calculateContainerSetCodePurchasePrice(void 0, 4.99, 20.97), void 0, "\u7F3A\u5C11\u4E3B\u5546\u54C1\u8FDB\u53E3\u4EF7\u683C\u65F6\u4E0D\u81EA\u52A8\u5206\u644A\u5957\u88C5\u5B50\u9879\u8FDB\u8D27\u4EF7");
assertEqual(calculateContainerSetCodePurchasePrice(0, 4.99, 20.97), void 0, "\u4E3B\u5546\u54C1\u8FDB\u53E3\u4EF7\u683C\u4E3A 0 \u65F6\u4E0D\u81EA\u52A8\u5206\u644A\u5957\u88C5\u5B50\u9879\u8FDB\u8D27\u4EF7");
assertEqual(calculateContainerSetCodePurchasePrice(6.1, 4.99, 0), void 0, "\u5B50\u9879\u4EF7\u683C\u5408\u8BA1\u4E3A 0 \u65F6\u4E0D\u81EA\u52A8\u5206\u644A\u5957\u88C5\u5B50\u9879\u8FDB\u8D27\u4EF7");
assertEqual(pageSource.includes("value={row.\u8C03\u6574\u6D6E\u7387}"), true, "\u8C03\u6574\u6D6E\u7387\u8F93\u5165\u6846\u5E94\u53D7\u63A7\uFF0C\u6279\u91CF\u5E94\u7528\u540E\u7ACB\u5373\u5237\u65B0\u663E\u793A");
assertEqual(pageSource.includes("defaultValue={row.\u8C03\u6574\u6D6E\u7387}"), false, "\u8C03\u6574\u6D6E\u7387\u8F93\u5165\u6846\u4E0D\u80FD\u4F7F\u7528 defaultValue");
assertEqual(
  pageSource.includes("value={row.\u8C03\u6574\u6D6E\u7387}\n            keyboard={false}\n            precision={2}"),
  true,
  "\u8C03\u6574\u6D6E\u7387\u884C\u5185\u8F93\u5165\u5E94\u4FDD\u6301 2 \u4F4D\u5C0F\u6570"
);
assertEqual(pageSource.includes("renderNumericCell(formatNumber(row.\u8C03\u6574\u6D6E\u7387, 2))"), true, "\u8C03\u6574\u6D6E\u7387\u53EA\u8BFB\u663E\u793A\u5E94\u4FDD\u6301 2 \u4F4D\u5C0F\u6570");
assertEqual(
  pageSource.includes("value={batchFloatRate}") && pageSource.includes("placeholder={t('containers.fields.floatRate')}") && pageSource.includes("precision={2}\n            controls={false}") && pageSource.includes("onChange={setBatchFloatRate}"),
  true,
  "\u6279\u91CF\u4FEE\u6539\u6D6E\u7387\u5F39\u7A97\u8F93\u5165\u5E94\u4FDD\u6301 2 \u4F4D\u5C0F\u6570"
);
assertDeepEqual(
  [
    "value={row.\u5355\u4EF6\u88C5\u7BB1\u6570}\n            keyboard={false}\n            min={0}\n            precision={0}\n            step={1}\n            controls={false}",
    "value={row.\u5355\u4EF6\u4F53\u79EF}\n            keyboard={false}\n            min={0}\n            precision={3}\n            controls={false}",
    "value={row.\u8C03\u6574\u6D6E\u7387}\n            keyboard={false}\n            precision={2}\n            controls={false}",
    "value={row.\u4E2D\u5305\u6570}\n            keyboard={false}\n            min={0}\n            precision={0}\n            controls={false}",
    'value={row.\u8FDB\u53E3\u4EF7\u683C}\n              keyboard={false}\n              min={0}\n              prefix="$"\n              precision={2}\n              controls={false}',
    'value={getContainerDetailVisibleOemPrice(row)}\n            keyboard={false}\n            min={0}\n            prefix="$"\n            precision={2}\n            controls={false}',
    "value={batchFloatRate}\n            placeholder={t('containers.fields.floatRate')}\n            precision={2}\n            controls={false}",
    `value={batchImportPrice}
            placeholder={t('containers.fields.importPrice')}
            min={0}
            prefix="$"
            precision={2}
            controls={false}`,
    `value={batchOemPrice}
            placeholder={t('containers.fields.oemPrice')}
            min={0}
            prefix="$"
            precision={2}
            controls={false}`,
    "<InputNumber value={headerForm.\u6C47\u7387} precision={4} controls={false}",
    "<InputNumber value={headerForm.\u8FD0\u8D39} precision={2} controls={false}"
  ].filter((snippet) => !pageSource.includes(snippet)),
  [],
  "\u8D27\u67DC\u660E\u7EC6\u9875\u6240\u6709\u53EF\u7F16\u8F91\u6570\u5B57\u8F93\u5165\u90FD\u5E94\u5173\u95ED\u52A0\u51CF\u6309\u94AE"
);
assertEqual(pageSource.includes("value={row.\u8FDB\u53E3\u4EF7\u683C}"), true, "\u8FDB\u53E3\u4EF7\u683C\u8F93\u5165\u6846\u5E94\u53D7\u63A7\uFF0C\u6279\u91CF\u5E94\u7528\u540E\u7ACB\u5373\u5237\u65B0\u663E\u793A");
assertEqual(pageSource.includes("defaultValue={row.\u8FDB\u53E3\u4EF7\u683C}"), false, "\u8FDB\u53E3\u4EF7\u683C\u8F93\u5165\u6846\u4E0D\u80FD\u4F7F\u7528 defaultValue");
assertEqual(pageSource.includes("const updatePayload: UpdateContainerRequest"), true, "\u4FDD\u5B58\u8D27\u67DC\u5934\u90E8\u5E94\u4F7F\u7528\u7A84\u66F4\u65B0 payload");
assertEqual(pageSource.includes("await updateContainer(containerGuid, nextContainer)"), false, "\u4FDD\u5B58\u8D27\u67DC\u5934\u90E8\u4E0D\u80FD\u628A\u5B8C\u6574\u8D27\u67DC\u5BF9\u8C61\u53D1\u9001\u5230\u540E\u7AEF");
assertEqual(
  pageSource.includes("t('containers.fields.domesticPriceTotal')") && pageSource.includes("formatCurrency(container?.\u5408\u8BA1\u91D1\u989D, '\xA5')"),
  true,
  "\u8D27\u67DC\u5934\u90E8\u57FA\u7840\u4FE1\u606F\u5E94\u53EA\u8BFB\u5C55\u793A\u56FD\u5185\u4EF7\u683C\u5408\u8BA1\u5E76\u4F7F\u7528\u4EBA\u6C11\u5E01\u683C\u5F0F"
);
{
  const headerFormInitStart = pageSource.indexOf("setHeaderForm({");
  const headerFormInitEnd = pageSource.indexOf("    } catch (error) {", headerFormInitStart);
  const headerUpdatePayloadStart = pageSource.indexOf("const updatePayload: UpdateContainerRequest = {");
  const headerUpdatePayloadEnd = pageSource.indexOf("    setSavingHeader(true)", headerUpdatePayloadStart);
  assertEqual(
    headerFormInitStart >= 0 && headerFormInitEnd > headerFormInitStart && headerUpdatePayloadStart >= 0 && headerUpdatePayloadEnd > headerUpdatePayloadStart,
    true,
    "\u8D27\u67DC\u5934\u90E8\u8868\u5355\u548C\u4FDD\u5B58 payload \u65AD\u8A00\u5E94\u5148\u5B9A\u4F4D\u5230\u6709\u6548\u6E90\u7801\u7247\u6BB5"
  );
  const headerFormInitSource = pageSource.slice(
    headerFormInitStart,
    headerFormInitEnd
  );
  const headerUpdatePayloadSource = pageSource.slice(
    headerUpdatePayloadStart,
    headerUpdatePayloadEnd
  );
  assertEqual(
    !headerFormInitSource.includes("\u5408\u8BA1\u91D1\u989D") && !headerUpdatePayloadSource.includes("\u5408\u8BA1\u91D1\u989D") && !headerUpdatePayloadSource.includes("...headerForm"),
    true,
    "\u56FD\u5185\u4EF7\u683C\u5408\u8BA1\u6765\u81EA\u4E3B\u8868\u6C47\u603B\uFF0C\u53EA\u8BFB\u5B57\u6BB5\u4E0D\u80FD\u8FDB\u5165 headerForm \u521D\u59CB\u5316\u3001\u4FDD\u5B58 payload \u6216\u88AB\u6574\u5305\u5C55\u5F00"
  );
}
assertEqual(
  pageSource.includes("\u8D27\u67DC\u7F16\u53F7: nextContainerNumber") && pageSource.includes("\u88C5\u67DC\u65E5\u671F: headerForm.\u88C5\u67DC\u65E5\u671F ? headerForm.\u88C5\u67DC\u65E5\u671F.format('YYYY-MM-DD') : undefined") && pageSource.includes("\u9884\u8BA1\u5230\u5CB8\u65E5\u671F: headerForm.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F ? headerForm.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F.format('YYYY-MM-DD') : undefined"),
  true,
  "\u4FDD\u5B58\u8D27\u67DC\u5934\u90E8\u5E94\u643A\u5E26\u53EF\u7F16\u8F91\u7684\u8D27\u67DC\u7F16\u53F7\u3001\u88C5\u67DC\u65E5\u671F\u548C\u9884\u8BA1\u5230\u5CB8\u65E5\u671F"
);
assertEqual(
  pageSource.includes("value={headerForm.\u8D27\u67DC\u7F16\u53F7}") && pageSource.includes("value={headerForm.\u88C5\u67DC\u65E5\u671F}") && pageSource.includes("value={headerForm.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F}") && pageSource.includes("<DatePicker allowClear={false} value={headerForm.\u88C5\u67DC\u65E5\u671F}") && pageSource.includes("<DatePicker allowClear={false} value={headerForm.\u9884\u8BA1\u5230\u5CB8\u65E5\u671F}") && pageSource.includes("message.error(t('containers.placeholders.enterContainerNumber'"),
  true,
  "\u8D27\u67DC\u5934\u90E8\u57FA\u7840\u4FE1\u606F\u7F16\u8F91\u6001\u5E94\u8986\u76D6\u7F16\u53F7\u548C\u65E5\u671F\u5B57\u6BB5\uFF0C\u7981\u6B62\u6E05\u7A7A\u5173\u952E\u65E5\u671F\uFF0C\u5E76\u6821\u9A8C\u8D27\u67DC\u7F16\u53F7\u4E0D\u80FD\u4E3A\u7A7A"
);
assertEqual(
  pageSource.includes("getContainerDetailCostMissingFields(nextCostContainer).filter((field) => field !== 'totalVolume')"),
  false,
  "\u4FDD\u5B58\u975E\u6210\u672C\u57FA\u7840\u4FE1\u606F\u4E0D\u5E94\u88AB\u6C47\u7387\u6216\u8FD0\u8D39\u7F3A\u5931\u62E6\u622A"
);
assertEqual(
  pageSource.includes("const shouldRecalculateCosts =") && pageSource.includes("recalculateContainerCostsByScope(containerGuid, buildWholeContainerDetailBatchScope())"),
  true,
  "\u4FDD\u5B58\u8D27\u67DC\u5934\u90E8\u6C47\u7387\u6216\u8FD0\u8D39\u53D8\u5316\u540E\u5E94\u81EA\u52A8\u6309\u6574\u67DC\u8303\u56F4\u91CD\u7B97\u6210\u672C"
);
assertEqual(
  pageSource.includes("const buildWholeContainerDetailBatchScope = (): ContainerDetailBatchScope => ({") && pageSource.includes("filters: {},") && !pageSource.includes("selectedTags: [],"),
  true,
  "\u8D27\u67DC\u5934\u90E8\u81EA\u52A8\u91CD\u7B97\u5E94\u4F7F\u7528\u4E0D\u5E26\u7B5B\u9009\u6761\u4EF6\u548C\u6807\u7B7E\u6761\u4EF6\u7684\u6574\u67DC scope"
);
assertEqual(
  pageSource.includes("Modal.warning") && pageSource.includes('<Space direction="vertical"') && pageSource.includes("showCostRecalculateWarning") && pageSource.includes("missingExchangeRateForCost") && pageSource.includes("missingFreightForCost") && pageSource.includes("missingTotalVolumeForCost"),
  true,
  "\u6210\u672C\u91CD\u7B97\u7F3A\u5C11\u6C47\u7387\u3001\u8FD0\u8D39\u6216\u603B\u4F53\u79EF\u65F6\u5E94\u901A\u8FC7\u5F39\u7A97\u63D0\u793A\u5E76\u505C\u6B62\u5199\u5E93"
);
assertEqual(
  pageSource.includes("headerSavedCostsRecalculateFailed") && pageSource.includes("message.warning(t('containers.messages.headerSavedCostsRecalculateFailed'"),
  true,
  "\u4FDD\u5B58\u8D27\u67DC\u5934\u90E8\u6210\u529F\u4F46\u6210\u672C\u91CD\u7B97\u5931\u8D25\u65F6\u5E94\u72EC\u7ACB\u63D0\u793A\uFF0C\u4E0D\u80FD\u4F2A\u88C5\u6210\u4FDD\u5B58\u5931\u8D25"
);
assertEqual(
  pageSource.includes("setBatchFloatRate(DEFAULT_CONTAINER_DETAIL_FLOAT_RATE)") && pageSource.includes("setBatchModalScopeRows(scopedRows)") && pageSource.includes("applyContainerFloatRateByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows), batchFloatRate)"),
  true,
  "\u6279\u91CF\u4FEE\u6539\u6D6E\u7387\u5F39\u7A97\u6253\u5F00\u65F6\u5E94\u9ED8\u8BA4\u586B\u5165 1.30\uFF0C\u786E\u8BA4\u540E\u6309\u5F39\u7A97\u89E3\u6790\u51FA\u7684\u6279\u91CF scope \u91CD\u7B97\u6210\u672C"
);
assertEqual(pageSource.includes("t('containers.formulas.transportCost'"), true, "\u8868\u683C\u9875\u811A\u8FD0\u8F93\u6210\u672C\u516C\u5F0F\u5E94\u4F7F\u7528 i18n key");
assertEqual(pageSource.includes("t('containers.formulas.importPrice'"), true, "\u8868\u683C\u9875\u811A\u8FDB\u53E3\u4EF7\u683C\u516C\u5F0F\u5E94\u4F7F\u7528 i18n key");
assertEqual(pageSource.includes("\u8FD0\u8F93\u6210\u672C = \u8FD0\u8D39 \xD7 \u660E\u7EC6\u4F53\u79EF \xF7 \u88C5\u67DC\u6570\u91CF \xF7 \u603B\u4F53\u79EF"), true, "\u8868\u683C\u9875\u811A\u5E94\u5C55\u793A\u8FD0\u8F93\u6210\u672C\u516C\u5F0F");
assertEqual(pageSource.includes("\u8FDB\u53E3\u4EF7\u683C = ((\u56FD\u5185\u4EF7\u683C \xF7 \u6C47\u7387 + \u8FD0\u8F93\u6210\u672C) \xD7 \u8C03\u6574\u6D6E\u7387 \xD7 10) \xF7 11"), true, "\u8868\u683C\u9875\u811A\u5E94\u5C55\u793A\u8FDB\u53E3\u4EF7\u683C\u516C\u5F0F");
assertEqual(
  pageSource.includes("const [recalculateCostsLoading, setRecalculateCostsLoading] = useState(false)"),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u5DE5\u5177\u680F\u4E0D\u518D\u7EF4\u62A4\u72EC\u7ACB\u91CD\u7B97\u6210\u672C\u6309\u94AE loading \u72B6\u6001"
);
assertEqual(
  pageSource.includes("const handleRecalculateCosts = async () => {"),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u5DE5\u5177\u680F\u4E0D\u518D\u63D0\u4F9B\u72EC\u7ACB\u91CD\u7B97\u6210\u672C\u5165\u53E3"
);
assertEqual(
  pageSource.includes("recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())"),
  false,
  "\u72EC\u7ACB\u91CD\u7B97\u6210\u672C\u4E0D\u518D\u6309\u5F53\u524D\u7B5B\u9009 scope \u66B4\u9732\uFF0C\u6210\u672C\u91CD\u7B97\u7531\u5934\u90E8\u4FDD\u5B58\u548C\u6279\u91CF\u6D6E\u7387\u81EA\u52A8\u89E6\u53D1"
);
assertEqual(
  pageSource.includes("dataSource={displayRows}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u5E94\u4F7F\u7528\u5217\u5934\u8FC7\u6EE4\u548C\u6392\u5E8F\u540E\u7684 displayRows"
);
assertEqual(
  pageSource.includes("applyContainerDetailColumnState(filteredRows, {}, sortState)"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u5934\u6392\u5E8F\u5E94\u5728\u524D\u7AEF\u5BF9\u5F53\u524D\u5DF2\u52A0\u8F7D\u53EF\u89C1\u884C\u6392\u5E8F"
);
assertEqual(
  pageSource.includes("getCategoryTree") && pageSource.includes("batchAssignProducts") && pageSource.includes("buildWarehouseCategoryLookup") && pageSource.includes("getWarehouseProductCategoryTooltip") && pageSource.includes("formatWarehouseCategoryNodeName"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5E94\u52A0\u8F7D\u5206\u7C7B\u6811\u3001\u590D\u7528\u5206\u7C7B\u8DEF\u5F84 Tooltip helper \u548C\u56FD\u9645\u5316\u540D\u79F0 helper\uFF0C\u5E76\u8C03\u7528\u6279\u91CF\u5206\u7C7B\u670D\u52A1"
);
assertEqual(
  !pageSource.includes("const [itemNumberFilter, setItemNumberFilter]") && !pageSource.includes("const [categoryFilterValue, setCategoryFilterValue]") && pageSource.includes("baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))") && pageSource.includes("applyContainerDetailLoadedTextFilters(tagFilteredRows, '', columnFilters)") && !pageSource.includes("applyContainerDetailCategoryFilter(textFilteredRows, categoryFilterValue, categoryLookup)") && !pageSource.includes("categoryFilterValue, sortState"),
  true,
  "\u9876\u90E8\u8FC7\u6EE4\u6761\u79FB\u9664\u540E\uFF0C\u6807\u7B7E\u5FEB\u89C8\u548C\u5217\u5934\u6587\u5B57\u8FC7\u6EE4\u4ECD\u5E94\u5728\u524D\u7AEF\u8FC7\u6EE4\u5DF2\u52A0\u8F7D\u884C\uFF0C\u5206\u7C7B\u548C\u6392\u5E8F\u4E0D\u5E94\u8FDB\u5165\u8FDC\u7A0B\u52A0\u8F7D\u67E5\u8BE2\u4F9D\u8D56"
);
assertEqual(
  !pageSource.includes("placeholder={t('containers.filters.allCategories'") && !pageSource.includes("options={categoryFilterOptions}") && !pageSource.includes("setCategoryFilterValue(value || CONTAINER_DETAIL_ALL_CATEGORY_FILTER_KEY)") && !pageSource.includes("buildContainerDetailCategoryOptions(categories, t, i18n.language)") && pageSource.includes("textFilterProps('itemNumber', t('containers.placeholders.filterItemNumber'))"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9876\u90E8\u5206\u7C7B Select \u5DF2\u79FB\u9664\uFF0C\u8D27\u53F7\u8FC7\u6EE4\u5E94\u53EA\u4FDD\u7559\u5217\u5934\u5165\u53E3"
);
assertEqual(
  pageSource.includes("filteredRows.length !== rows.length") && pageSource.includes("t('containers.text.visibleRows'"),
  true,
  "\u5206\u7C7B\u8FC7\u6EE4\u540E\u5E94\u663E\u793A\u5F53\u524D\u53EF\u89C1\u884C\u6570\u91CF\uFF0C\u907F\u514D\u8BEF\u89E3\u4E3A\u540E\u7AEF\u603B\u6570\u53D8\u5316"
);
assertEqual(
  (() => {
    const baseQueryStart = pageSource.indexOf("const baseDetailQuery = useMemo(() => buildContainerDetailQuery({");
    const queryEnd = pageSource.indexOf("const baseDetailQueryKey", baseQueryStart);
    const baseQuerySource = pageSource.slice(baseQueryStart, queryEnd);
    return baseQueryStart >= 0 && queryEnd > baseQueryStart && !pageSource.includes("const scopedDetailQuery = useMemo(() => buildContainerDetailQuery({") && !pageSource.includes("const scopedFullDetailQuery = useMemo(() => buildContainerDetailQuery({") && !baseQuerySource.includes("selectedTags") && baseQuerySource.includes("filters: remoteColumnFilters") && pageSource.includes("const detailQuery = baseDetailQuery") && pageSource.includes("const detailQueryKey = baseDetailQueryKey") && pageSource.includes("const activeLoadQueryKey = detailQueryKey");
  })(),
  true,
  "\u660E\u7EC6\u52A0\u8F7D\u67E5\u8BE2\u5E94\u53EA\u4FDD\u7559\u65E0\u6807\u7B7E base \u67E5\u8BE2\uFF0C\u6807\u7B7E\u5207\u6362\u4E0D\u5E94\u8FDB\u5165\u8FDC\u7A0B\u67E5\u8BE2 key"
);
assertEqual(
  pageSource.includes("const shouldComputeDetailMeta = mode === 'reset'") && pageSource.includes("includeTotal: shouldComputeDetailMeta") && pageSource.includes("includeStats: shouldComputeDetailMeta") && pageSource.includes("if (result.totalComputed !== false) {") && pageSource.includes("if (result.statsComputed !== false) {"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9996\u5C4F\u624D\u5E94\u8BF7\u6C42 total/tagStats\uFF0C\u8FFD\u52A0\u9875\u4E0D\u5E94\u8986\u76D6\u9996\u5C4F\u7EDF\u8BA1"
);
assertEqual(
  (() => {
    const currentQueryStart = pageSource.indexOf("const fetchAllRowsForCurrentQuery = async () => {");
    const wholeQueryStart = pageSource.indexOf("const fetchAllRowsForWholeContainer = async () => {");
    const queryBlockEnd = pageSource.indexOf("const confirmSubmitContainer", wholeQueryStart);
    const allRowsSource = pageSource.slice(currentQueryStart, queryBlockEnd);
    return currentQueryStart >= 0 && wholeQueryStart > currentQueryStart && queryBlockEnd > wholeQueryStart && allRowsSource.includes("includeTotal: false") && allRowsSource.includes("includeStats: false");
  })(),
  true,
  "\u540E\u53F0\u6279\u91CF\u62C9\u5168\u91CF\u660E\u7EC6\u65F6\u5E94\u8DF3\u8FC7 total/tagStats \u5E76\u4F9D\u8D56 hasMore"
);
assertEqual(
  !pageSource.includes("itemNumber: itemNumberFilter.trim() || columnFilters.itemNumber") && pageSource.includes("const remoteColumnFilters = useMemo<ContainerDetailColumnFilters>(() => omitContainerDetailTextFilters(columnFilters), [columnFilters])"),
  true,
  "\u9876\u90E8\u8D27\u53F7\u548C\u5217\u5934\u6587\u5B57\u7B5B\u9009\u4E0D\u5E94\u5408\u5E76\u8FDB\u8FDC\u7A0B\u67E5\u8BE2\u6761\u4EF6"
);
assertEqual(
  pageSource.includes("baseFilteredRows.filter((row) => matchesContainerDetailSelectedTags(row, selectedTagFilters))") && pageSource.includes("applyContainerDetailLoadedTextFilters(tagFilteredRows, '', columnFilters)") && pageSource.includes("hasLoadedFullBaseDetailQuery ? localBaseTagStats : remoteTagStats") && pageSource.includes("return await fetchAllRowsForCurrentQuery()") && pageSource.includes("setBatchModalScopeRows(scopedRows)") && pageSource.includes("buildDetailBatchScope(batchModalScopeRows)") && pageSource.includes("selectedHguids: getRowsHguids(scopeRows)") && pageSource.includes("...baseDetailQuery,"),
  true,
  "\u6807\u7B7E\u5E94\u59CB\u7EC8\u8FDB\u5165\u524D\u7AEF\u8FC7\u6EE4\u94FE\u8DEF\uFF0C\u6279\u91CF\u4F5C\u7528\u57DF\u548C\u5168\u91CF\u62C9\u53D6\u5E94\u4F7F\u7528 base \u67E5\u8BE2\u540E\u5728\u524D\u7AEF\u6536\u655B HGUID"
);
assertEqual(
  pageSource.includes("columnFilters") && pageSource.includes("sortState"),
  true,
  "\u9875\u9762\u5E94\u7EF4\u62A4\u53D7\u63A7\u5217\u5934\u8FC7\u6EE4\u548C\u6392\u5E8F\u72B6\u6001"
);
assertEqual(
  pageSource.includes("makeTextFilterDropdown") && pageSource.includes("makeNumberRangeFilterDropdown") && pageSource.includes("makeEnumFilterDropdown"),
  true,
  "\u5217\u5934\u5E94\u63D0\u4F9B\u6587\u672C\u3001\u6570\u5B57\u8303\u56F4\u548C\u679A\u4E3E\u4E09\u7C7B\u8FC7\u6EE4\u9762\u677F"
);
assertEqual(
  pageSource.includes("renderColumnTitle('itemNumber'") && pageSource.includes("renderColumnTitle('barcode'") && pageSource.includes("renderColumnTitle('productName'") && pageSource.includes("renderColumnTitle('middlePackQuantity'") && pageSource.includes("renderColumnTitle('containerQuantity'") && pageSource.includes("renderColumnTitle('importPrice'") && pageSource.includes("renderColumnTitle('warehouseStatus'"),
  true,
  "\u5173\u952E\u4E1A\u52A1\u5217\u5E94\u6302\u8F7D\u5217\u5934\u6392\u5E8F\u6216\u8FC7\u6EE4\u914D\u7F6E"
);
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_EDITABLE_COLUMN_KEYS = ['englishName', 'packingQuantity', 'unitVolume', 'middlePackQuantity', 'floatRate', 'importPrice', 'oemPrice', 'remark'] as const") && pageSource.includes("patchRow(rowKey(row), { \u4E2D\u5305\u6570: value == null ? undefined : Number(value) })") && pageSource.includes("saveRowPatch(row, { \u4E2D\u5305\u6570: event.target.value ? Number(event.target.value) : undefined })"),
  true,
  "\u4E2D\u5305\u6570\u5217\u5E94\u4F5C\u4E3A\u53EF\u7F16\u8F91\u6570\u5B57\u5217\u4FDD\u5B58\u5230\u8D27\u67DC\u660E\u7EC6\u66F4\u65B0\u63A5\u53E3"
);
assertEqual(
  pageSource.includes("const orderedEditableColumnKeys = useMemo(") && pageSource.includes("getContainerDetailEditableColumnKeysInOrder(") && pageSource.includes("orderedBaseColumns.map((column) => String(column.key))") && pageSource.includes("orderedEditableColumnKeys,\n      direction,"),
  true,
  "\u65B9\u5411\u952E\u5BFC\u822A\u5E94\u6309\u5F53\u524D\u9875\u9762\u5217\u987A\u5E8F\u8FC7\u6EE4\u53EF\u7F16\u8F91\u5217"
);
assertEqual(
  pageSource.includes("value={row.\u5355\u4EF6\u88C5\u7BB1\u6570}\n            keyboard={false}\n            min={0}\n            precision={0}\n            step={1}") && pageSource.includes("renderNumericCell(formatNumber(row.\u5355\u4EF6\u88C5\u7BB1\u6570, 0))"),
  true,
  "\u5355\u4EF6\u88C5\u7BB1\u6570\u884C\u5185\u8F93\u5165\u548C\u53EA\u8BFB\u663E\u793A\u5E94\u6309\u6574\u6570\u5904\u7406"
);
assertEqual(
  pageSource.includes("value={row.\u5355\u4EF6\u4F53\u79EF}\n            keyboard={false}\n            min={0}\n            precision={3}") && pageSource.includes("renderNumericCell(formatNumber(row.\u5355\u4EF6\u4F53\u79EF, 3))"),
  true,
  "\u5355\u4EF6\u4F53\u79EF\u884C\u5185\u8F93\u5165\u548C\u53EA\u8BFB\u663E\u793A\u5E94\u4FDD\u7559 3 \u4F4D\u5C0F\u6570"
);
assertEqual(
  pageSource.includes("patchRow(rowKey(row), { \u5355\u4EF6\u88C5\u7BB1\u6570: row.\u5355\u4EF6\u88C5\u7BB1\u6570 })") && pageSource.includes("patchRow(rowKey(row), { \u5355\u4EF6\u4F53\u79EF: row.\u5355\u4EF6\u4F53\u79EF })") && pageSource.includes("const savePackageMetricPatch = async (row: ContainerDetail, patch: Partial<ContainerDetail>) => {") && pageSource.includes("showCostRecalculateWarning(getContainerDetailCostMissingFields(container))") && pageSource.includes("update.SkipRelatedProductSync = true") && pageSource.includes("savePackageMetricPatch(row, { \u5355\u4EF6\u88C5\u7BB1\u6570: Number(event.target.value) })") && pageSource.includes("savePackageMetricPatch(row, { \u5355\u4EF6\u4F53\u79EF: Number(event.target.value) })"),
  true,
  "\u5355\u4EF6\u88C5\u7BB1\u6570\u548C\u5355\u4EF6\u4F53\u79EF\u6E05\u7A7A\u65F6\u5E94\u56DE\u6EDA\u5F53\u524D\u503C\uFF0C\u7CFB\u7EDF\u91CD\u7B97\u8FDB\u8D27\u4EF7\u4E0D\u80FD\u540C\u6B65\u4ED3\u5E93\u8868"
);
assertEqual(
  pageSource.includes("type PendingContainerDetailPricePatch =") && pageSource.includes("const [pendingPricePatches, setPendingPricePatches] = useState<PendingContainerDetailPricePatchMap>({})") && pageSource.includes("const [priceDetailsSaving, setPriceDetailsSaving] = useState(false)") && pageSource.includes("const markPendingPricePatch = (row: ContainerDetail") && pageSource.includes("const buildPendingPriceSavePlan = (): PendingContainerDetailPriceSavePlan | null => {") && pageSource.includes("const confirmSavePendingPriceDetails = (plan: PendingContainerDetailPriceSavePlan) => new Promise<boolean>") && pageSource.includes("const executePendingPriceSavePlan = async (plan: PendingContainerDetailPriceSavePlan) => {") && pageSource.includes("const savePendingPriceDetails = async () => {"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9875\u5E94\u7EF4\u62A4\u8FDB\u53E3\u4EF7\u683C\u548C\u96F6\u552E\u4EF7\u7684\u624B\u52A8\u5F85\u4FDD\u5B58\u72B6\u6001"
);
assertEqual(
  pageSource.includes("const update: UpdateContainerDetailRequest = { hguid: patch.hguid }") && pageSource.includes("if (patch.\u8FDB\u53E3\u4EF7\u683C != null) update.\u8FDB\u53E3\u4EF7\u683C = patch.\u8FDB\u53E3\u4EF7\u683C") && pageSource.includes("if (patch.\u8D34\u724C\u4EF7\u683C != null) update.\u8D34\u724C\u4EF7\u683C = patch.\u8D34\u724C\u4EF7\u683C") && pageSource.includes("await trackDetailSavePromise(plan.saveKeys, batchUpdateDetails(plan.detailUpdates))") && !pageSource.includes("await batchUpdateWarehouseProducts(plan.warehouseUpdates)") && !pageSource.includes("t('containers.messages.missingWarehouseProductCodeForRetailPrice'") && pageSource.includes("const confirmed = await confirmSavePendingPriceDetails(savePlan)") && pageSource.includes("await executePendingPriceSavePlan(savePlan)") && pageSource.includes("setPendingPricePatches((current) => {") && pageSource.includes("t('containers.messages.detailPricesSaved'"),
  true,
  "\u4FDD\u5B58\u660E\u7EC6\u5E94\u53EA\u53D1\u660E\u7EC6\u4E8B\u52A1\u63A5\u53E3\uFF0C\u7531\u540E\u7AEF\u7EDF\u4E00\u540C\u6B65\u5DF2\u6709\u5546\u54C1\u5173\u8054\u4EF7\u683C"
);
assertEqual(
  pageSource.includes("Modal.confirm({") && pageSource.includes("t('containers.modals.savePendingPriceDetailsTitle', '\u786E\u8BA4\u4FDD\u5B58\u660E\u7EC6\u4EF7\u683C')") && pageSource.includes("t('containers.modals.savePendingPriceDetailsUpdateTitle', '\u66F4\u65B0\u8BF4\u660E')") && pageSource.includes("'containers.modals.savePendingPriceDetailsSummary'") && pageSource.includes("t('containers.modals.savePendingPriceDetailsExistingRetailHint'") && pageSource.includes("t('containers.modals.savePendingPriceDetailsNewRetailHint'") && pageSource.includes("t('containers.modals.savePendingPriceDetailsRetryHint'"),
  true,
  "\u4FDD\u5B58\u660E\u7EC6\u5E94\u5728\u843D\u5E93\u524D\u5F39\u51FA\u4E8C\u6B21\u786E\u8BA4\uFF0C\u5E76\u5C55\u793A\u66F4\u65B0\u8BF4\u660E"
);
assertEqual(
  pageSource.includes("icon={<SaveOutlined />}") && pageSource.includes("loading={priceDetailsSaving}") && pageSource.includes("disabled={!pendingPricePatchCount || priceDetailsSaving}") && pageSource.includes("onClick={() => void savePendingPriceDetails()}") && pageSource.includes("t('containers.actions.saveDetails', '\u4FDD\u5B58\u660E\u7EC6')"),
  true,
  "\u6279\u91CF\u4EF7\u683C\u64CD\u4F5C\u533A\u5E94\u63D0\u4F9B\u4FDD\u5B58\u660E\u7EC6\u6309\u94AE\uFF0C\u4E14\u65E0\u5F85\u4FDD\u5B58\u4EF7\u683C\u65F6\u7981\u7528"
);
var pendingPriceGuardSource = pageSource.slice(
  pageSource.indexOf("const ensureNoPendingPriceDetails = () => {"),
  pageSource.indexOf("const patchRow = (key: string, patch: Partial<ContainerDetail>) => {")
);
assertEqual(
  pendingPriceGuardSource.includes("if (!pendingPricePatchCount) return true") && pendingPriceGuardSource.includes("t('containers.messages.savePendingPriceDetailsFirst', '\u8BF7\u5148\u70B9\u51FB\u201C\u4FDD\u5B58\u660E\u7EC6\u201D\u4FDD\u5B58\u8FDB\u53E3\u4EF7\u683C/\u96F6\u552E\u4EF7')") && pendingPriceGuardSource.includes("return false"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9875\u5E94\u63D0\u4F9B\u672A\u4FDD\u5B58\u4EF7\u683C\u62E6\u622A\u63D0\u793A\uFF0C\u8981\u6C42\u7528\u6237\u5148\u70B9\u4FDD\u5B58\u660E\u7EC6"
);
assertEqual(
  columnsSource.includes("function renderOemPriceCell(row: ContainerDetail)") && columnsSource.includes("formatCurrency(getContainerDetailVisibleOemPrice(row), '$')") && columnsSource.includes("function renderImportPriceCell(row: ContainerDetail, input?: ReactNode)") && pageSource.includes("renderImportPriceCell(row, (") && pageSource.includes(": renderImportPriceCell(row)") && pageSource.includes("formatCurrency(v, '\xA5')") && pageSource.includes('prefix="$"'),
  true,
  "\u4EF7\u683C\u5217\u5E94\u6309\u56FD\u5185\u4EF7\u683C\u4EBA\u6C11\u5E01\u3001\u5176\u5B83\u4EF7\u683C\u7F8E\u5143\u663E\u793A\u8D27\u5E01\u7B26\u53F7"
);
assertEqual(
  containerDetailLogicSource.includes("return currentImportPrice > realtimeImportPrice ? 'up' : 'down'") && columnsSource.includes("getContainerDetailImportPriceTrend(row)") && columnsSource.includes("const Icon = trend === 'up' ? ArrowUpOutlined : ArrowDownOutlined") && columnsSource.includes("return <Icon className={className} />") && pageSource.includes("onChange={(value) => markPendingPricePatch(row, { \u8FDB\u53E3\u4EF7\u683C: value == null ? undefined : Number(value) })") && pageSource.includes("onChange={(value) => markPendingPricePatch(row, { \u8D34\u724C\u4EF7\u683C: value == null ? undefined : Number(value) })") && !pageSource.includes("saveRowPatch(row, { \u8FDB\u53E3\u4EF7\u683C: event.target.value ? Number(event.target.value) : undefined })") && !pageSource.includes("saveRowPatch(row, { \u8D34\u724C\u4EF7\u683C: event.target.value ? Number(event.target.value) : undefined })") && pageStyleSource.includes(".container-detail-import-price-trend-up") && pageStyleSource.includes("color: #52c41a") && pageStyleSource.includes(".container-detail-import-price-trend-down") && pageStyleSource.includes("color: #ff4d4f"),
  true,
  "\u8FDB\u53E3\u4EF7\u683C\u548C\u96F6\u552E\u4EF7\u5E94\u6539\u4E3A\u624B\u52A8\u4FDD\u5B58\u660E\u7EC6\uFF0C\u4E0D\u80FD\u518D\u5931\u7126\u81EA\u52A8\u843D\u5E93"
);
assertEqual(
  columnsSource.includes("\u96F6\u552E\u4EF7\u53EA\u8BFB\u5355\u5143\u683C\uFF1A\u65B0\u5546\u54C1\u663E\u793A\u660E\u7EC6\u4EF7\uFF0C\u5DF2\u6709\u5546\u54C1\u663E\u793A\u4ED3\u5E93\u5B9E\u65F6\u4EF7") && pageSource.includes("value={getContainerDetailVisibleOemPrice(row)}") && pageSource.includes("warehouseOEMPrice: patch.\u8D34\u724C\u4EF7\u683C") && !columnsSource.includes("source === 'warehouse'") && !pageSource.includes("className={getOemPriceSourceClassName(row)}") && !pageStyleSource.includes(".container-detail-oem-price-cell-warehouse") && !pageStyleSource.includes(".container-detail-oem-price-cell-fallback"),
  true,
  "\u96F6\u552E\u4EF7\u5E94\u6309\u65B0\u5546\u54C1/\u5DF2\u6709\u5546\u54C1\u5206\u6D41\u663E\u793A\u548C\u7F16\u8F91\uFF0C\u4E0D\u518D\u6CBF\u7528\u4ED3\u5E93\u5FEB\u7167\u6765\u6E90\u5E95\u8272"
);
assertEqual(
  pageSource.includes("handleWarehouseStatusChange"),
  true,
  "\u4ED3\u5E93\u72B6\u6001\u5217\u5E94\u652F\u6301\u884C\u5185\u7F16\u8F91"
);
assertEqual(
  pageSource.includes("getContainerDetailProductCode(row)"),
  true,
  "\u9875\u9762\u5E94\u901A\u8FC7\u7EDF\u4E00 helper \u89E3\u6790\u5546\u54C1\u7F16\u7801\uFF0C\u907F\u514D\u7A7A\u767D\u7F16\u7801\u7ED5\u8FC7\u515C\u5E95"
);
assertEqual(
  pageSource.includes("getContainerDetailWarehouseActionFailureMessage(result"),
  true,
  "\u4ED3\u5E93\u72B6\u6001\u66F4\u65B0\u5E94\u7EDF\u4E00\u68C0\u67E5\u6839\u5931\u8D25\u548C\u90E8\u5206\u5931\u8D25\u7ED3\u679C"
);
assertEqual(
  pageSource.includes("pendingWarehouseStatusCodes") && pageSource.includes("loading={isWarehouseStatusPending}") && pageSource.includes("disabled={row.\u662F\u5426\u65B0\u5546\u54C1 || !productCode || isWarehouseStatusPending}"),
  true,
  "\u884C\u5185\u4ED3\u5E93\u72B6\u6001\u66F4\u65B0\u5E94\u663E\u793A\u63D0\u4EA4\u4E2D\u72B6\u6001\uFF0C\u5E76\u963B\u6B62\u65B0\u5546\u54C1\u6216\u91CD\u590D\u70B9\u51FB"
);
assertEqual(
  pageSource.includes("const previousStatuses = rows") && pageSource.includes("setRows((items) => applyContainerDetailWarehouseStatusByProductCodes(items, [productCode], isActive))") && pageSource.includes("rollbackContainerDetailWarehouseStatuses"),
  true,
  "\u884C\u5185\u4ED3\u5E93\u72B6\u6001\u5E94\u5148\u4E50\u89C2\u66F4\u65B0\uFF0C\u5931\u8D25\u65F6\u56DE\u6EDA\u540C\u5546\u54C1\u7F16\u7801\u884C"
);
assertEqual(
  pageSource.includes(".filter((value) => !pendingWarehouseStatusCodes.has(value))") && pageSource.includes("t('containers.messages.warehouseStatusUpdating'"),
  true,
  "\u6279\u91CF\u4E0A\u4E0B\u67B6\u5E94\u8DF3\u8FC7\u6B63\u5728\u884C\u5185\u63D0\u4EA4\u7684\u5546\u54C1\u7F16\u7801\uFF0C\u907F\u514D\u5931\u8D25\u56DE\u6EDA\u8986\u76D6\u6279\u91CF\u6210\u529F\u72B6\u6001"
);
assertEqual(
  pageSource.includes("applyContainerDetailWarehouseStatusByProductCodes(items, productCodes, isActive)") && pageSource.includes("applyContainerDetailWarehouseStatusByProductCodes(items, [productCode], isActive)"),
  true,
  "\u6279\u91CF\u548C\u884C\u5185\u4ED3\u5E93\u72B6\u6001\u66F4\u65B0\u90FD\u5E94\u590D\u7528\u540C\u5546\u54C1\u7F16\u7801\u672C\u5730\u66F4\u65B0 helper"
);
assertEqual(
  pageSource.includes("applyContainerPricesByScope(containerGuid, buildDetailBatchScope(batchModalScopeRows)") && pageSource.includes("const scopedRows = await confirmBatchRows(t(isActive ? 'containers.actions.batchActivate' : 'containers.actions.batchDeactivate'))") && pageSource.includes("return await fetchAllRowsForCurrentQuery()") && pageSource.includes("const productCodes = eligibleRows"),
  true,
  "\u6279\u91CF\u4FEE\u6539\u4EF7\u683C\u5E94\u4F7F\u7528 HGUID scope\uFF0C\u6279\u91CF\u4E0A\u4E0B\u67B6\u672A\u9009\u62E9\u65F6\u5E94\u63D0\u793A\u540E\u4F5C\u7528\u4E8E\u5F53\u524D\u7B5B\u9009\u7ED3\u679C\u5168\u91CF\u4E2D\u7684\u5DF2\u6709\u5546\u54C1"
);
assertEqual(
  pageSource.includes('className="container-detail-bulk-input"') || pageSource.includes("t('containers.actions.applyFloatRate')") || pageSource.includes("t('containers.actions.applyPrices')") || pageSource.includes("t('containers.actions.recalculateCosts')") || pageSource.includes("onClick={() => void handleRecalculateCosts()}"),
  false,
  "\u5DE5\u5177\u680F\u4E0D\u5E94\u518D\u4FDD\u7559\u6279\u91CF\u6D6E\u7387\u3001\u6279\u91CF\u4EF7\u683C\u8F93\u5165\u6846\u6216\u72EC\u7ACB\u91CD\u7B97/\u5E94\u7528\u6309\u94AE"
);
assertEqual(
  pageSource.includes("key: 'batchFloatRate'") && pageSource.includes("key: 'batchPrices'") && pageSource.includes("key: 'matchDomesticData'") && pageSource.includes("t('containers.actions.batchUpdateFloatRate'") && pageSource.includes("t('containers.actions.batchUpdatePrices'") && !pageSource.includes("key: 'backfillLastPrices'"),
  true,
  "\u6279\u91CF\u64CD\u4F5C\u83DC\u5355\u5E94\u5305\u542B\u6279\u91CF\u4FEE\u6539\u6D6E\u7387\u3001\u6279\u91CF\u4FEE\u6539\u4EF7\u683C\u548C\u5339\u914D\u56FD\u5185\u6570\u636E\uFF0C\u5E76\u79FB\u9664\u56DE\u586B\u4E0A\u6B21\u4EF7\u683C\u5165\u53E3"
);
assertDeepEqual(
  [
    "const scopedRows = await confirmBatchRows(t('containers.actions.matchDomesticData'))",
    "const scopedRows = await confirmBatchRows(t(isActive ? 'containers.actions.batchActivate' : 'containers.actions.batchDeactivate'))",
    "const scopedRows = await confirmBatchRows(t('containers.actions.batchTranslate'))",
    "const scopedRows = await confirmBatchRows(t('containers.actions.clearEnglishNames'), { danger: true })",
    "const scopedRows = await confirmBatchRows(t('containers.actions.createNewProducts'))",
    "const confirmed = await confirmBatchRowsWithUpdateFields(",
    "title={t('containers.modals.batchUpdateFloatRateTitle'",
    "title={t('containers.modals.batchUpdatePricesTitle'"
  ].filter((snippet) => !pageSource.includes(snippet)),
  [],
  "\u5199\u5165\u7C7B\u6279\u91CF\u64CD\u4F5C\u5E94\u7EDF\u4E00\u7ECF\u8FC7\u786E\u8BA4\u5F39\u7A97\u6216\u8F93\u5165\u786E\u8BA4\u5F39\u7A97"
);
assertEqual(
  pageSource.includes("const resolveBatchActionTargetRows = async () => {") && pageSource.includes("return await fetchAllRowsForCurrentQuery()") && pageSource.includes("const scopedRows = await resolveBatchActionTargetRows()") && pageSource.includes("renderBatchActionContent(batchModalTargetCount)") && pageSource.includes("const [batchModalScopeRows, setBatchModalScopeRows] = useState<ContainerDetail[]>([])") && pageSource.includes("setBatchModalScopeRows(scopedRows)") && pageSource.includes("buildDetailBatchScope(batchModalScopeRows)") && pageSource.includes("selectedHguids: getRowsHguids(scopeRows)") && pageSource.includes("t('containers.modals.batchActionAllHint'"),
  true,
  "\u672A\u9009\u62E9\u5546\u54C1\u65F6\u5E94\u5148\u89E3\u6790\u524D\u7AEF\u5B8C\u6574\u53EF\u89C1\u884C\uFF0C\u5F39\u7A97\u548C\u63D0\u4EA4\u90FD\u4F7F\u7528\u540C\u4E00\u6279 HGUID \u8303\u56F4"
);
assertEqual(
  pageSource.includes('data-column-key="image"') || pageSource.includes('data-column-key="index"'),
  false,
  "\u56FE\u7247\u548C\u7F16\u53F7\u5217\u4E0D\u5E94\u6DFB\u52A0\u65E0\u610F\u4E49\u5217\u5934\u8FC7\u6EE4\u914D\u7F6E"
);
assertEqual(
  pageSource.includes("buildContainerDetailFloatRateUpdates([row], container, value)"),
  true,
  "\u5355\u884C\u4FDD\u5B58\u6D6E\u7387\u5E94\u4F7F\u7528\u539F\u59CB\u884C\u8BA1\u7B97\u53D8\u5316\uFF0C\u4E0D\u80FD\u63D0\u524D\u8986\u76D6\u6D6E\u7387\u5BFC\u81F4\u4E0D\u5199\u5E93"
);
assertEqual(
  pageSource.includes("recalculateContainerCostsByScope(containerGuid, buildDetailBatchScope())"),
  false,
  "\u8D27\u67DC\u660E\u7EC6\u9875\u4E0D\u5E94\u518D\u66B4\u9732\u72EC\u7ACB\u91CD\u7B97\u6210\u672C scope \u5199\u56DE\u5165\u53E3"
);
assertEqual(
  pageSource.includes("await loadDetailChunk(1, 'reset')"),
  true,
  "\u91CD\u7B97\u6210\u672C\u5199\u56DE\u6210\u529F\u540E\u5E94\u91CD\u8F7D\u5F53\u524D\u67E5\u8BE2\u9996\u5757"
);
assertEqual(pageSource.includes("t('containers.messages.missingFreightForCost'"), true, "\u7F3A\u5C11\u8FD0\u8D39\u65F6\u5E94\u901A\u8FC7 i18n \u63D0\u793A\u4E14\u4E0D\u5199\u5E93");
assertEqual(pageSource.includes("t('containers.messages.missingTotalVolumeForCost'"), true, "\u7F3A\u5C11\u603B\u4F53\u79EF\u65F6\u5E94\u901A\u8FC7 i18n \u63D0\u793A\u4E14\u4E0D\u5199\u5E93");
assertEqual(
  pageSource.includes("message.success(t('containers.messages.detailsUpdated', { count: result.totalUpdated }))"),
  true,
  "\u91CD\u7B97\u6210\u672C\u6210\u529F\u540E\u5E94\u63D0\u793A\u66F4\u65B0\u6761\u6570"
);
assertEqual(pageSource.includes("loading={recalculateCostsLoading}"), false, "\u5DE5\u5177\u680F\u4E0D\u5E94\u518D\u6E32\u67D3\u72EC\u7ACB\u91CD\u7B97\u6210\u672C\u6309\u94AE loading");
assertEqual(pageSource.includes("onClick={() => void handleRecalculateCosts()}"), false, "\u5DE5\u5177\u680F\u4E0D\u5E94\u518D\u8C03\u7528\u72EC\u7ACB\u91CD\u7B97\u6210\u672C\u5165\u53E3");
assertEqual(pageSource.includes("t('containers.actions.recalculateCosts')"), false, "\u5DE5\u5177\u680F\u4E0D\u5E94\u518D\u6E32\u67D3\u91CD\u7B97\u6210\u672C\u6309\u94AE\u6587\u6848");
assertEqual(
  pageSource.includes("renderColumnTitle('itemNumber', t('containers.fields.itemNumber'))") && pageSource.includes("fixed: 'left'"),
  true,
  "\u8D27\u53F7\u5217\u5E94\u56FA\u5B9A\u5728\u5DE6\u4FA7\uFF0C\u6A2A\u5411\u6EDA\u52A8\u65F6\u4FDD\u6301\u53EF\u89C1"
);
var defaultColumnOrderMarkers = [
  "key: 'index'",
  "key: 'image'",
  "renderColumnTitle('itemNumber'",
  "renderColumnTitle('englishName'",
  "key: 'categoryName'",
  "renderColumnTitle('containerPieces'",
  "renderColumnTitle('packingQuantity'",
  "renderColumnTitle('containerQuantity'",
  "renderColumnTitle('unitVolume'",
  "renderColumnTitle('domesticPrice'",
  "renderColumnTitle('transportCost'",
  "renderColumnTitle('unitTransportCost'",
  "renderColumnTitle('floatRate'",
  "renderColumnTitle('middlePackQuantity'",
  "renderColumnTitle('warehouseImportPrice'",
  "renderColumnTitle('importPrice'",
  "renderColumnTitle('oemPrice'",
  "renderColumnTitle('lastOEMPrice'",
  "renderColumnTitle('newProduct'",
  "renderColumnTitle('productType'",
  "renderColumnTitle('matchType'",
  "renderColumnTitle('barcode'",
  "renderColumnTitle('productName'",
  "renderColumnTitle('warehouseStatus'",
  "renderColumnTitle('remark'"
];
var defaultColumnOrderIndexes = defaultColumnOrderMarkers.map((marker) => pageSource.indexOf(marker));
assertEqual(
  defaultColumnOrderIndexes.every((index) => index >= 0) && defaultColumnOrderIndexes.every((index, markerIndex) => markerIndex === 0 || index > defaultColumnOrderIndexes[markerIndex - 1]),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9ED8\u8BA4\u5217\u987A\u5E8F\u5E94\u6309\u622A\u56FE\u6392\u5217"
);
var categoryColumnSource = pageSource.slice(
  pageSource.indexOf("key: 'categoryName'"),
  pageSource.indexOf("title: renderColumnTitle('containerPieces'")
);
assertEqual(
  categoryColumnSource.includes("title: renderCompactHeader(t('containers.fields.category'") && categoryColumnSource.includes("openRowCategoryModal(row)") && categoryColumnSource.includes("renderContainerDetailCategoryCell(row, categoryLookup, i18n.language") && columnsSource.includes("const displayName = getContainerDetailCategoryName(record) || '--'"),
  true,
  "\u5206\u7C7B\u5217\u5E94\u663E\u793A\u5206\u7C7B\u540D\u79F0\uFF0CTooltip \u4F7F\u7528\u5B8C\u6574\u8DEF\u5F84 helper\uFF0C\u7F3A\u5931\u65F6\u663E\u793A --\uFF0C\u4E14\u6709\u6743\u9650\u65F6\u53EF\u6253\u5F00\u5355\u884C\u76EE\u6807\u5206\u7C7B\u4FEE\u6539\u5F39\u7A97"
);
var barcodeColumnSource = pageSource.slice(
  pageSource.indexOf("renderColumnTitle('barcode', t('containers.fields.barcode'))"),
  pageSource.indexOf("title: renderColumnTitle('productName'")
);
assertEqual(
  barcodeColumnSource.includes("fixed: 'left'"),
  false,
  "\u6761\u7801\u5217\u5E94\u6309\u622A\u56FE\u79FB\u52A8\u5230\u540E\u6BB5\uFF0C\u4E0D\u518D\u56FA\u5B9A\u5728\u5DE6\u4FA7"
);
assertEqual(
  barcodeColumnSource.includes("showReadonlyOemPrice ? [readonlyOemPriceColumn] : []") && pageSource.includes("const readonlyOemPriceColumn: ColumnsType<ContainerDetail>[number]") && pageSource.includes("render: (_, row) => renderReadonlyOemPriceCell(row)") && columnsSource.includes("function renderReadonlyOemPriceCell(row: ContainerDetail)") && !pageSource.slice(pageSource.indexOf("const readonlyOemPriceColumn"), pageSource.indexOf("const baseColumns")).includes("fixed: 'left'") && !pageSource.slice(pageSource.indexOf("const readonlyOemPriceColumn"), pageSource.indexOf("const baseColumns")).includes("<InputNumber"),
  true,
  "\u6761\u7801\u5217\u540E\u5E94\u6309\u5F00\u5173\u63D2\u5165\u53EA\u8BFB\u96F6\u552E\u4EF7\u5217\uFF0C\u4FBF\u4E8E\u6A2A\u5411\u6EDA\u52A8\u524D\u5FEB\u901F\u6838\u4EF7"
);
assertEqual(
  pageSource.includes("rowSelection={{") && pageSource.includes("selectedRowKeys,") && pageSource.includes("onChange: setSelectedRowKeys,") && pageSource.includes("fixed: !viewport.isSmallPortrait,") && pageSource.includes("orderedBaseColumns.map((column) => ({ ...column, fixed: undefined }))"),
  true,
  "\u9009\u62E9\u6846\u5217\u9ED8\u8BA4\u968F\u5DE6\u4FA7\u5217\u56FA\u5B9A\uFF0C\u5C0F\u5C4F\u7AD6\u5C4F\u65F6\u5E94\u968F\u8868\u683C\u5217\u4E00\u8D77\u53D6\u6D88\u56FA\u5B9A"
);
assertEqual(
  pageSource.includes("key: field") && pageSource.includes("sorter: true") && pageSource.includes("sortOrder: sortState?.field === field ? sortState.order : null"),
  true,
  "\u53EF\u6392\u5E8F\u5217\u5E94\u4F7F\u7528 AntD \u7A33\u5B9A key \u4F5C\u4E3A\u6392\u5E8F\u5B57\u6BB5\u6807\u8BC6\uFF0C\u5E76\u4FDD\u7559 sorter \u4E0E sortOrder"
);
assertEqual(
  pageSource.includes("isContainerDetailSortField(nextSortField)"),
  true,
  "\u8868\u683C\u6392\u5E8F\u56DE\u8C03\u5E94\u901A\u8FC7\u8FD0\u884C\u65F6\u767D\u540D\u5355\u8FC7\u6EE4 sorter \u5B57\u6BB5"
);
assertEqual(
  pageSource.includes("DndContext") && pageSource.includes("SortableContext") && pageSource.includes("useSortable") && pageSource.includes("horizontalListSortingStrategy"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u5934\u5217\u62D6\u62FD\u5E94\u590D\u7528 @dnd-kit \u6A2A\u5411\u6392\u5E8F\u80FD\u529B"
);
assertEqual(
  pageSource.includes("components={{ header: { cell: DraggableHeaderCell } }}") && pageSource.includes("<SortableContext items={columnOrder} strategy={horizontalListSortingStrategy}>") && pageSource.includes("<DndContext sensors={columnDragSensors} collisionDetection={closestCenter} onDragEnd={handleColumnDragEnd}>"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u5E94\u63A5\u5165\u53EF\u62D6\u62FD\u8868\u5934 cell \u4E0E\u6A2A\u5411 SortableContext"
);
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY = 'hbweb_rv.containerDetail.columnOrder.v3'") && pageSource.includes("localStorage.setItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY") && pageSource.includes("mergeContainerDetailColumnOrder("),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u987A\u5E8F\u5E94\u4FDD\u5B58\u5230 v3 localStorage key\uFF0C\u8986\u76D6\u65E7\u9ED8\u8BA4\u987A\u5E8F\u5E76\u517C\u5BB9\u65B0\u589E\u5217"
);
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY = 'hbweb_rv.containerDetail.columnWidths.v1'") && pageSource.includes("const [columnWidths, setColumnWidths]") && pageSource.includes("normalizeContainerDetailColumnWidths(") && pageSource.includes("localStorage.setItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u5BBD\u5E94\u72EC\u7ACB\u4FDD\u5B58\u5230 v1 localStorage key\uFF0C\u5E76\u6309\u5F53\u524D\u4E1A\u52A1\u5217\u8FC7\u6EE4\u65E7\u5BBD\u5EA6"
);
assertEqual(
  pageSource.includes("data-column-width") && pageSource.includes("onColumnResizeStart: handleColumnResizeStart") && pageSource.includes('className="container-detail-column-resize-handle"') && pageSource.includes("event.stopPropagation()") && pageSource.includes('<div className="container-detail-draggable-header" {...attributes} {...listeners}>') && !pageSource.includes("<th ref={setNodeRef} style={headerStyle} {...props} {...attributes} {...listeners}>"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u5BBD\u62D6\u62FD\u5E94\u4F7F\u7528\u72EC\u7ACB\u8868\u5934\u624B\u67C4\uFF0C\u5E76\u907F\u514D\u89E6\u53D1\u8868\u5934\u6392\u5E8F\u62D6\u62FD"
);
assertEqual(
  pageSource.includes("const tableScrollX = Math.max(") && pageSource.includes("CONTAINER_DETAIL_SELECTION_COLUMN_WIDTH + columns.reduce") && pageSource.includes("scroll={{ x: tableScrollX, y: tableScrollY }}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u6A2A\u5411\u6EDA\u52A8\u5BBD\u5EA6\u5E94\u8DDF\u968F\u5F53\u524D\u4E1A\u52A1\u5217\u5BBD\u603B\u548C\u66F4\u65B0"
);
assertEqual(
  pageStyleSource.includes(".container-detail-column-resize-handle") && pageStyleSource.includes("cursor: col-resize") && pageStyleSource.includes("touch-action: none"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5217\u5BBD\u62D6\u62FD\u624B\u67C4\u5E94\u6709\u72EC\u7ACB\u547D\u4E2D\u533A\u57DF\u548C col-resize \u5149\u6807"
);
assertEqual(
  pageSource.includes("containers.actions.resetColumns") && pageSource.includes("const isColumnSettingsCustomized = isColumnOrderCustomized || isColumnWidthCustomized") && pageSource.includes("setColumnOrder(draggableColumnKeys)") && pageSource.includes("setColumnWidths({})") && pageSource.includes("localStorage.removeItem(CONTAINER_DETAIL_COLUMN_ORDER_STORAGE_KEY)") && pageSource.includes("localStorage.removeItem(CONTAINER_DETAIL_COLUMN_WIDTH_STORAGE_KEY)"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u624B\u52A8\u62D6\u62FD\u5217\u6216\u5217\u5BBD\u540E\u5E94\u63D0\u4F9B\u91CD\u7F6E\u5217\u6309\u94AE\u5E76\u6E05\u9664\u672C\u5730\u5217\u8BBE\u7F6E"
);
assertEqual(
  pageSource.includes("const draggableColumnKeys = baseColumns.map((column) => String(column.key) as ContainerDetailTableColumnKey)") && pageSource.includes("rowSelection={{") && !pageSource.includes("columnOrder.includes('selection')"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9009\u62E9\u5217\u4ECD\u5E94\u7531 rowSelection \u7BA1\u7406\uFF0C\u4E0D\u80FD\u8FDB\u5165\u4E1A\u52A1\u5217\u62D6\u62FD\u987A\u5E8F"
);
assertEqual(
  pageSource.includes("key: 'batchCategory'") && pageSource.includes("const canBatchSetCategory = access.canEditContainer && access.canManagePosProducts") && pageSource.includes("if (!canBatchSetCategory)") && pageSource.includes("void openBatchCategory()") && pageSource.includes("handleBatchCategorySave") && pageSource.includes("getContainerDetailBatchCategoryProductCodes(batchCategoryTargetRows)") && pageSource.includes("batchAssignProducts(targetCategoryGuid, productCodes)"),
  true,
  "\u6279\u91CF\u64CD\u4F5C\u83DC\u5355\u5E94\u5305\u542B\u6279\u91CF\u5206\u7C7B\uFF0C\u5E76\u63D0\u4EA4\u5F53\u524D\u76EE\u6807\u884C\u7684\u53BB\u91CD\u5546\u54C1\u7F16\u7801"
);
assertEqual(
  pageSource.includes("const canBackfillLastPrices = access.isAdmin || access.isWarehouseManager") || pageSource.includes("if (!canBackfillLastPrices) return") || pageSource.includes("key: 'backfillLastPrices'") || pageSource.includes("backfillContainerLastPricesByScope(containerGuid, scope)"),
  false,
  "web \u8D27\u67DC\u660E\u7EC6\u6279\u91CF\u64CD\u4F5C\u83DC\u5355\u4E0D\u5E94\u518D\u66B4\u9732\u56DE\u586B\u4E0A\u6B21\u4EF7\u683C\u5165\u53E3"
);
var batchCategorySaveSource = pageSource.slice(
  pageSource.indexOf("const handleBatchCategorySave = async () => {"),
  pageSource.indexOf("const submitBatchEditEnglishName = async () => {")
);
assertEqual(
  batchCategorySaveSource.includes("await batchAssignProducts(targetCategoryGuid, productCodes)") && !batchCategorySaveSource.includes("await loadDetailChunk(1, 'reset')") && batchCategorySaveSource.includes("setRows((items) =>") && batchCategorySaveSource.includes("const productCode = getContainerDetailProductCode(item)") && batchCategorySaveSource.includes("productCodeSet.has(productCode)") && batchCategorySaveSource.includes("buildContainerDetailCategoryPatch(item, targetCategoryGuid, selectedTargetCategory, selectedTargetCategoryPath)") && pageSource.includes("WarehouseCategoryGUID: categoryGuid") && pageSource.includes("ProductCategoryGUID: categoryGuid"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u6279\u91CF\u5206\u7C7B\u4FDD\u5B58\u6210\u529F\u540E\u5E94\u672C\u5730\u66F4\u65B0\u5F53\u524D\u884C\u5206\u7C7B\uFF0C\u4E0D\u5E94\u91CD\u65B0\u67E5\u8BE2\u660E\u7EC6\u8868\u683C"
);
assertEqual(
  pageSource.includes("title={t('containers.modals.batchCategoryTitle'") && pageSource.includes("t('warehouse.categories.targetCategory'") && pageSource.includes("selectedTargetCategoryPath || formatWarehouseCategoryNodeName(selectedTargetCategory, i18n.language)") && pageSource.includes("import CategoryTreePicker") && pageSource.includes("setCategoryExpandedKeys(collectCategoryExpandedKeys(categories, 1))") && pageSource.includes("<CategoryTreePicker") && pageSource.includes("selectedKey={targetCategoryGuid}") && pageSource.includes("maxHeight={360}"),
  true,
  "\u6279\u91CF\u5206\u7C7B\u5F39\u7A97\u5E94\u4F7F\u7528\u5E26\u67E5\u8BE2\u7684\u5F53\u524D\u8BED\u8A00\u5206\u7C7B\u6811\uFF0C\u5E76\u5728\u6BCF\u6B21\u6253\u5F00\u65F6\u9ED8\u8BA4\u5C55\u5F00\u5230\u4E00\u7EA7\u5206\u7C7B"
);
var rowCategoryModalSource = pageSource.slice(
  pageSource.indexOf("title={t('containers.modals.rowCategoryTitle'"),
  pageSource.indexOf("<div className={pageClassName}>")
);
assertEqual(
  rowCategoryModalSource.includes("'\u76EE\u6807\u5206\u7C7B\u4FEE\u6539'") && rowCategoryModalSource.includes("open={rowCategoryOpen}") && rowCategoryModalSource.includes("selectedKey={rowTargetCategoryGuid}") && rowCategoryModalSource.includes("onOk={() => void handleRowCategorySave()}") && pageSource.includes("await batchUpdateDetails([{ hguid: rowCategoryEditingRow.hguid, ProductCategoryGUID: rowTargetCategoryGuid }])") && pageSource.includes("rowKey(item) !== rowKey(rowCategoryEditingRow)") && pageSource.includes("setRowCategoryOpen(false)"),
  true,
  "\u5355\u884C\u76EE\u6807\u5206\u7C7B\u4FEE\u6539\u5F39\u7A97\u5E94\u53EA\u63D0\u4EA4\u5F53\u524D\u884C ProductCategoryGUID\uFF0C\u5E76\u5728\u4FDD\u5B58\u540E\u53EA\u66F4\u65B0\u5F53\u524D\u5DF2\u52A0\u8F7D\u884C"
);
assertEqual(
  pageSource.includes("t('common.copyValue'") && pageSource.includes("t('containers.setCode.pricesTitle'") && pageSource.includes("okText={t('common.save')}") && !pageSource.includes(">\u91CD\u7B97\u6210\u672C</Button>") && !pageSource.includes(">\u5339\u914D\u56FD\u5185\u6570\u636E</Button>"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u590D\u5236\u3001\u91CD\u7B97\u3001\u5339\u914D\u548C\u5957\u88C5\u591A\u7801\u5F39\u7A97\u5E94\u4F7F\u7528 i18n \u6587\u6848"
);
assertEqual(pageSource.includes('className="container-detail-table"'), true, "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u5E94\u6302\u8F7D\u4E13\u5C5E class \u4EE5\u9694\u79BB\u5782\u76F4\u5BF9\u9F50\u6837\u5F0F");
assertEqual(
  pageSource.includes("rowClassName={(_, index) => index % 2 === 1 ? 'container-detail-row-striped' : ''}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u5E94\u6309\u5F53\u524D\u663E\u793A\u987A\u5E8F\u7ED9\u5076\u6570\u89C6\u89C9\u884C\u6DFB\u52A0\u9694\u884C\u8272 class"
);
assertEqual(
  pageSource.includes("const CONTAINER_DETAIL_PAGE_SIZE = 50") && pageSource.includes("pagination={false}") && pageSource.includes("virtual") && pageSource.includes("onScroll={handleDetailTableScroll}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5E94\u5173\u95ED\u53EF\u89C1\u5206\u9875\u5668\uFF0C\u4F7F\u7528 50 \u6761\u5185\u90E8\u61D2\u52A0\u8F7D\u5757\u548C\u865A\u62DF\u6EDA\u52A8"
);
var stickyControlsStart = pageSource.indexOf('className="container-detail-sticky-controls"');
var detailTableStart = pageSource.indexOf('className="container-detail-table"', stickyControlsStart);
var stickyControlsSource = pageSource.slice(stickyControlsStart, detailTableStart);
assertEqual(
  stickyControlsStart >= 0 && detailTableStart > stickyControlsStart && pageSource.includes('<Card className="container-detail-grid-card">') && pageSource.includes('className="container-detail-table-region"') && !pageSource.includes('className="container-detail-scroll-spacer"') && stickyControlsSource.includes('className="container-detail-toolbar"') && stickyControlsSource.includes('className="container-detail-action-row"') && stickyControlsSource.includes('className="container-detail-action-meta"') && stickyControlsSource.includes('className="container-detail-bulk-row"') && stickyControlsSource.includes("<ContainerTagFilters") && stickyControlsSource.includes('{exporting ? <Progress percent={exportProgress} size="small" /> : null}'),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u64CD\u4F5C\u6309\u94AE\u3001\u72B6\u6001\u4FE1\u606F\u3001\u6279\u91CF\u64CD\u4F5C\u3001\u7EDF\u8BA1\u6807\u7B7E\u548C\u5BFC\u51FA\u8FDB\u5EA6\u5E94\u5728\u8868\u683C\u524D\u7684\u7D27\u51D1 sticky \u63A7\u5236\u533A\u5185"
);
assertEqual(
  pageSource.includes("ref={setGridContentElement}") && pageSource.includes("ref={setToolbarElement}") && pageSource.includes("ref={setTableRegionElement}") && pageSource.includes("ResizeObserver") && pageSource.includes("const [detailLayoutMetrics, setDetailLayoutMetrics] = useState") && pageSource.includes("querySelector('.ant-table-thead')") && pageSource.includes("querySelector('.ant-table-footer')") && pageSource.includes("horizontalScrollbarHeight") && !pageSource.includes("window.addEventListener('scroll', scheduleMeasure") && !pageSource.includes("window.removeEventListener('scroll', scheduleMeasure") && !pageSource.includes("window.addEventListener('scroll', scheduleMeasure, true)") && pageSource.includes("calculateContainerDetailTableScrollY({") && !pageSource.includes("contentTop: detailLayoutMetrics.contentTop,") && pageSource.includes("toolbarHeight: detailLayoutMetrics.toolbarHeight,") && pageSource.includes("tableChromeHeight: detailLayoutMetrics.tableChromeHeight,"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u9AD8\u5EA6\u5E94\u53EA\u6839\u636E\u5DE5\u5177\u680F\u548C\u8868\u683C\u5934\u5C3E\u5B9E\u6D4B\u9AD8\u5EA6\u52A8\u6001\u8BA1\u7B97\uFF0C\u4E0D\u76D1\u542C\u6EDA\u52A8"
);
assertEqual(
  pageSource.includes("const [detailTableRenderKey, setDetailTableRenderKey] = useState(0)") && pageSource.includes("const lastDetailTableScrollTopRef = useRef(0)") && pageSource.includes("const wasContainerDetailTabActiveRef = useRef(active)") && pageSource.includes("if (!active || wasActive || rows.length === 0)") && pageSource.includes("window.requestAnimationFrame(() => {") && pageSource.includes("setDetailTableRenderKey((value) => value + 1)") && pageSource.includes("detailTableRef.current?.scrollTo?.({ top: scrollTop })") && pageSource.includes("key={`${containerGuid}-${detailTableRenderKey}`}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6 Tab \u5207\u56DE\u65F6\u5E94\u91CD\u6302\u8F7D AntD \u865A\u62DF\u8868\u683C\u5E76\u6062\u590D\u6EDA\u52A8\u4F4D\u7F6E\uFF0C\u907F\u514D KeepAlive \u9690\u85CF\u540E body \u7A7A\u767D"
);
assertEqual(
  pageSource.includes("lastDetailTableScrollTopRef.current = target.scrollTop") && pageSource.includes("target.scrollTop + target.clientHeight >= target.scrollHeight - 96") && pageSource.includes("void loadNextDetailChunk()"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u6EDA\u52A8\u5904\u7406\u5E94\u540C\u65F6\u4FDD\u5B58\u6EDA\u52A8\u4F4D\u7F6E\u5E76\u4FDD\u7559\u89E6\u5E95\u52A0\u8F7D\u4E0B\u4E00\u5757"
);
assertEqual(
  pageStyleSource.includes(".container-detail-table .ant-table-thead > tr > th"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u5934\u5E94\u901A\u8FC7\u4E13\u5C5E\u6837\u5F0F\u4FDD\u6301\u5782\u76F4\u5C45\u4E2D"
);
assertEqual(
  pageStyleSource.includes(".container-detail-sticky-controls") && pageStyleSource.includes("position: relative") && !pageStyleSource.includes("top: 138px") && !pageStyleSource.includes("top: 104px") && pageStyleSource.includes(".container-detail-grid-card") && pageStyleSource.includes("position: sticky") && pageStyleSource.includes("top: 150px") && pageStyleSource.includes(".container-detail-grid-card > .ant-card-body") && pageStyleSource.includes(".container-detail-table-region") && pageStyleSource.includes("min-height: 0") && pageStyleSource.includes("overflow: hidden") && pageStyleSource.includes("flex: 1 1 auto") && !pageStyleSource.includes(".container-detail-scroll-spacer") && pageStyleSource.includes(".container-detail-page-small-portrait .container-detail-grid-card") && pageStyleSource.includes(".container-detail-page-small-landscape .container-detail-grid-card") && pageStyleSource.includes(".container-detail-toolbar") && pageStyleSource.includes(".container-detail-action-row") && pageStyleSource.includes(".container-detail-action-meta") && pageStyleSource.includes(".container-detail-bulk-row") && pageStyleSource.includes("overflow-x: auto") && pageStyleSource.includes("flex-wrap: nowrap !important") && pageStyleSource.includes(".container-detail-page-small .container-detail-table .ant-table-footer") && pageStyleSource.includes(".container-detail-page-small-landscape .container-detail-sticky-controls"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u63A7\u5236\u533A\u5E94\u4F7F\u7528\u5C40\u90E8\u7D27\u51D1\u5E03\u5C40\uFF0C\u4E0D\u518D\u4F9D\u8D56\u4F1A\u88AB\u5168\u5C40\u6807\u7B7E\u680F\u906E\u6321\u7684\u56FA\u5B9A sticky top"
);
assertEqual(
  pageStyleSource.includes(".container-detail-table .ant-table-tbody > tr > td"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u6B63\u6587\u5355\u5143\u683C\u5E94\u901A\u8FC7\u4E13\u5C5E\u6837\u5F0F\u4FDD\u6301\u5782\u76F4\u5C45\u4E2D"
);
assertEqual(
  pageStyleSource.includes(".container-detail-table .container-detail-row-striped:not(.ant-table-row-selected) > td"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9694\u884C\u8272\u5E94\u9650\u5B9A\u5728\u4E13\u5C5E\u8868\u683C\u5185\u4E14\u4E0D\u8986\u76D6\u9009\u4E2D\u884C"
);
assertEqual(
  pageStyleSource.includes(".container-detail-table .container-detail-row-striped:not(.ant-table-row-selected):hover > td"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u9694\u884C\u8272\u5E94\u4FDD\u7559 hover \u53CD\u9988"
);
assertEqual(
  pageSource.includes('className="container-detail-nowrap container-detail-copyable"'),
  true,
  "\u8D27\u53F7\u590D\u5236\u533A\u57DF\u5E94\u4F7F\u7528\u4E13\u5C5E nowrap \u6837\u5F0F\u4FDD\u6301\u5355\u884C\u7D27\u51D1\u663E\u793A"
);
assertEqual(
  pageSource.includes('className="container-detail-copy-button"'),
  true,
  "\u8D27\u53F7\u590D\u5236\u6309\u94AE\u5E94\u4F7F\u7528\u56FE\u6807\u6309\u94AE\u6837\u5F0F\uFF0C\u4E0D\u663E\u793A\u590D\u5236\u6587\u5B57"
);
assertEqual(
  pageSource.includes('className="container-detail-barcode-cell"'),
  true,
  "\u6761\u7801\u5217\u5E94\u4F7F\u7528\u4E13\u5C5E\u7D27\u51D1\u5BB9\u5668\u907F\u514D\u6761\u7801\u548C\u590D\u5236\u6309\u94AE\u6362\u884C"
);
assertEqual(
  pageSource.includes("<BarcodePreview value={barcode} showText showCopy={false} options={{ height: 24 }} />"),
  true,
  "\u6761\u7801\u5217\u5E94\u663E\u793A\u6761\u7801\u6587\u672C\uFF0C\u4E0D\u80FD\u9690\u85CF\u6761\u7801\u6587\u672C"
);
assertEqual(
  pageSource.includes("Image,") && pageSource.includes("<Image") && pageSource.includes("const imageUrl = getContainerDetailImageUrl(row)") && pageSource.includes("src={imageUrl}") && pageSource.includes('className="container-detail-product-image"') && pageSource.includes("preview={{ mask: t('containers.actions.previewImage', '\u67E5\u770B\u5927\u56FE') }}"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5546\u54C1\u56FE\u7247\u5E94\u4F7F\u7528\u7EDF\u4E00\u56FE\u7247\u515C\u5E95\u5E76\u53EF\u70B9\u51FB\u653E\u5927\u9884\u89C8"
);
assertEqual(
  setCodeHookSource.includes("useTranslation()") && setCodeHookSource.includes("t('containers.setCode.missingProductCode')") && setCodeHookSource.includes("t('containers.setCode.itemNumber')") && setCodeHookSource.includes("t('containers.setCode.purchasePrice')") && !setCodeHookSource.includes("title: '\u5957\u88C5\u8D27\u53F7'") && !setCodeHookSource.includes("message.success('\u4FDD\u5B58\u6210\u529F')"),
  true,
  "\u5957\u88C5\u591A\u7801\u5F39\u7A97\u6D88\u606F\u548C\u5217\u6807\u9898\u5E94\u8D70 i18n\uFF0C\u4E0D\u80FD\u5728\u82F1\u6587\u6A21\u5F0F\u663E\u793A\u4E2D\u6587"
);
assertEqual(
  pageStyleSource.includes(".container-detail-barcode-cell .ant-typography"),
  true,
  "\u6761\u7801\u6587\u672C\u5E94\u4F7F\u7528\u4E13\u5C5E\u6837\u5F0F\u4FDD\u6301\u5B8C\u6574\u663E\u793A"
);
assertEqual(
  pageStyleSource.includes("overflow: visible"),
  true,
  "\u6761\u7801\u6587\u672C\u4E0D\u5E94\u88AB\u7701\u7565\u6216\u6298\u53E0\u9690\u85CF"
);
assertEqual(
  pageSource.includes('className="container-detail-two-line-text"'),
  true,
  "\u5546\u54C1\u540D\u79F0\u5E94\u4F7F\u7528\u4E24\u884C\u622A\u65AD\u6837\u5F0F\u907F\u514D\u957F\u540D\u79F0\u6491\u9AD8\u6574\u884C"
);
assertEqual(
  pageSource.includes('className="container-detail-english-name-input"'),
  true,
  "\u82F1\u6587\u540D\u79F0\u8F93\u5165\u6846\u5E94\u4F7F\u7528\u4E13\u5C5E\u4E24\u884C\u7D27\u51D1\u6837\u5F0F"
);
assertEqual(
  pageSource.includes("renderNumericCell("),
  true,
  "\u6570\u5B57\u5217\u5E94\u901A\u8FC7\u7EDF\u4E00 helper \u4FDD\u6301\u5355\u884C\u548C\u7B49\u5BBD\u6570\u5B57\u663E\u793A"
);
assertEqual(
  pageStyleSource.includes("-webkit-line-clamp: 2"),
  true,
  "\u540D\u79F0\u548C\u8868\u5934\u5E94\u901A\u8FC7 CSS \u9650\u5236\u6700\u591A\u4E24\u884C\u663E\u793A"
);
assertEqual(
  pageStyleSource.includes("font-variant-numeric: tabular-nums"),
  true,
  "\u6570\u5B57\u5217\u5E94\u4F7F\u7528\u7B49\u5BBD\u6570\u5B57\u89C6\u89C9\u4EE5\u4FBF\u626B\u8BFB"
);
assertEqual(
  pageStyleSource.includes(".container-detail-table .ant-table-cell"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u8868\u683C\u5E94\u538B\u7F29\u5355\u5143\u683C padding \u63D0\u5347\u5BC6\u5EA6"
);
assertEqual(
  pageStyleSource.includes(".container-detail-stat-tag-muted"),
  true,
  "\u672A\u9009\u4E2D\u7684\u7EDF\u8BA1\u6807\u7B7E\u5E94\u6709\u5F31\u5316\u6837\u5F0F\uFF0C\u4FDD\u7559\u989C\u8272\u540C\u65F6\u907F\u514D\u548C\u9009\u4E2D\u6001\u6DF7\u6DC6"
);
assertEqual(
  pageSource.includes("const headerLoadRequestIdRef = useRef(0)") && pageSource.includes("const containerDetailLoadRequestIdRef = useRef(0)") && pageSource.includes("detailAbortControllerRef.current?.abort()"),
  true,
  "\u8D27\u67DC\u8BE6\u60C5\u5934\u90E8\u548C\u660E\u7EC6\u52A0\u8F7D\u5E94\u5206\u522B\u4F7F\u7528 request id \u4E0E AbortController \u9632\u6B62\u65E7\u8BF7\u6C42\u8986\u76D6\u65B0\u9875\u9762"
);
assertEqual(
  pageSource.includes("if (headerLoadRequestIdRef.current !== currentRequestId)") && pageSource.includes("if (controller.signal.aborted || containerDetailLoadRequestIdRef.current !== currentRequestId)") && pageSource.includes("return"),
  true,
  "\u8D27\u67DC\u8BE6\u60C5\u8FC7\u671F\u8BF7\u6C42\u5B8C\u6210\u6216\u5931\u8D25\u540E\u5E94\u76F4\u63A5\u5FFD\u7565\uFF0C\u4E0D\u80FD\u5199\u5165 state \u6216\u5F39\u5931\u8D25\u63D0\u793A"
);
assertEqual(
  pageSource.includes("const errorMessage = error instanceof Error ? error.message : t('containers.messages.loadDetailFailed')") && pageSource.includes("if (showLoading) {") && pageSource.includes("message.error(errorMessage)") && pageSource.includes("} else {") && pageSource.includes("console.error('\u8D27\u67DC\u8BE6\u60C5\u9759\u9ED8\u5237\u65B0\u5931\u8D25', error)"),
  true,
  "\u8D27\u67DC\u8BE6\u60C5\u9759\u9ED8\u5237\u65B0\u5931\u8D25\u5E94\u4FDD\u7559\u5F53\u524D\u5185\u5BB9\uFF0C\u4E0D\u5F39\u660E\u663E\u5931\u8D25\u63D0\u793A\uFF1B\u9996\u6B21\u52A0\u8F7D\u5931\u8D25\u624D\u5C55\u793A\u9519\u8BEF"
);
assertEqual(
  pageSource.includes("const [containerGuid] = useState(() => route?.params.containerGuid || '')"),
  false,
  "\u8D27\u67DC GUID \u4E0D\u80FD\u7528 useState \u56FA\u5B9A\u9996\u6B21\u8DEF\u7531\u53C2\u6570\uFF0C\u79FB\u52A8\u7AEF\u5207\u6362\u8BE6\u60C5\u65F6\u5FC5\u987B\u8DDF\u968F\u5F53\u524D URL"
);
assertEqual(
  pageSource.includes("\u79FB\u52A8\u7AEF\u5E03\u5C40\u53EF\u80FD\u590D\u7528 route element\uFF0C\u8D27\u67DC GUID \u5FC5\u987B\u6BCF\u6B21\u8DDF\u968F\u5F53\u524D URL"),
  true,
  "\u8D27\u67DC\u8BE6\u60C5\u5E94\u6709\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u79FB\u52A8\u7AEF route element \u590D\u7528\u65F6 GUID \u5FC5\u987B\u8DDF\u968F\u5F53\u524D URL"
);
assertEqual(
  pageSource.includes("const lastLoadedContainerDetailSuccessRef = useRef<{ containerGuid: string; queryKey: string } | null>(null)") && pageSource.includes("lastLoadedContainerDetailSuccessRef.current = { containerGuid, queryKey: detailQueryKey }") && pageSource.includes("const loadedDetailQueryKey = lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid") && pageSource.includes("loadedDetailQueryKey: lastLoadedContainerDetailSuccessRef.current?.containerGuid === containerGuid") && !pageSource.includes("loadedDetailQueryKey: lastLoadedContainerDetailQueryKeyRef.current"),
  true,
  "\u660E\u7EC6\u81EA\u52A8\u8DF3\u8FC7\u5224\u65AD\u53EA\u80FD\u4F7F\u7528\u660E\u7EC6\u6210\u529F\u52A0\u8F7D\u8BB0\u5F55\uFF0C\u4E0D\u80FD\u6CBF\u7528\u5934\u90E8\u52A0\u8F7D\u72B6\u6001\u6216\u65E7\u67E5\u8BE2 key"
);
assertEqual(
  mobileLayoutSource.includes('<div className="mobile-content" key={location.pathname}>'),
  true,
  "\u79FB\u52A8\u7AEF\u5E03\u5C40\u5E94\u6309 pathname \u91CD\u5EFA\u5F53\u524D\u9875\u9762\uFF0C\u907F\u514D\u4E0D\u540C\u8D27\u67DC\u8BE6\u60C5\u590D\u7528\u540C\u4E00\u4E2A\u7EC4\u4EF6\u5B9E\u4F8B"
);
assertEqual(
  pageSource.includes("renderProductTypeTag(row)") && pageSource.includes("container-detail-product-type-tag-clickable") && pageSource.includes("productType === '\u5957\u88C5\u5546\u54C1'") && pageSource.includes("openSetCodeModal(row)"),
  true,
  "\u8D27\u67DC\u660E\u7EC6\u5546\u54C1\u7C7B\u578B\u5217\u5E94\u4F7F\u7528\u5F69\u8272 Tag\uFF0C\u5957\u88C5\u5546\u54C1 Tag \u5E94\u53EF\u70B9\u51FB\u6253\u5F00\u5957\u88C5\u591A\u7801\u5F39\u7A97"
);
assertEqual(
  setCodeHookSource.includes("getContainerDomesticSetCodes(productCode, abortController.signal)") && setCodeHookSource.includes("setCodeAbortControllerRef.current?.abort()") && setCodeHookSource.includes("updateContainerDomesticSetCodePrices(productCode, changedSetCodePriceItems)"),
  true,
  "\u5957\u88C5\u591A\u7801\u5F39\u7A97\u5E94\u652F\u6301\u4E2D\u6B62\u65E7\u8BF7\u6C42\uFF0C\u5E76\u901A\u8FC7\u56FD\u5185\u5957\u88C5\u4EF7\u683C\u63A5\u53E3\u4FDD\u5B58\u53D8\u66F4"
);
assertEqual(
  setCodeHookSource.includes("const mainPurchasePrice = setCodeModalRow?.\u8FDB\u53E3\u4EF7\u683C") && setCodeHookSource.includes("calculateContainerSetCodePurchasePrice(mainPurchasePrice, nextRetailPrice, totalRetailPrice)") && !setCodeHookSource.includes("setCodeModalRow?.warehouseImportPrice"),
  true,
  "\u5957\u88C5\u5B50\u9879\u8FDB\u8D27\u4EF7\u5E94\u6309\u8D27\u67DC\u660E\u7EC6\u5F53\u524D\u884C\u8FDB\u53E3\u4EF7\u683C\u5206\u644A\uFF0C\u4E0D\u80FD\u4F7F\u7528\u4ED3\u5E93\u5F53\u524D\u8FDB\u8D27\u4EF7"
);
assertEqual(
  pageStyleSource.includes(".container-detail-product-type-tag-clickable"),
  true,
  "\u53EF\u70B9\u51FB\u5546\u54C1\u7C7B\u578B Tag \u5E94\u6709\u4E13\u5C5E\u6837\u5F0F\u63D0\u793A\u53EF\u64CD\u4F5C"
);
console.log("containerDetailLogic.test: ok");
