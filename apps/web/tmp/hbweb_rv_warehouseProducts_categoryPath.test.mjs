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
function addLocalizedNamePath(lookup2, name, fullPath, localizedFullPath) {
  if (!name || !fullPath) return;
  lookup2.byName.set(name, addUniquePath(lookup2.byName.get(name), fullPath));
  lookup2.displayByName.set(name, {
    zh: addUniquePath(lookup2.displayByName.get(name)?.zh, localizedFullPath.zh),
    en: addUniquePath(lookup2.displayByName.get(name)?.en, localizedFullPath.en)
  });
}
function buildWarehouseCategoryLookup(nodes, parentPath = [], displayParentPath = { zh: [], en: [] }, lookup2 = {
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
      lookup2.byGuid.set(node.categoryGUID, fullPath);
      lookup2.displayByGuid.set(node.categoryGUID, localizedFullPath);
      descendantGuids.add(node.categoryGUID);
    }
    addLocalizedNamePath(lookup2, node.categoryName, fullPath, localizedFullPath);
    addLocalizedNamePath(lookup2, node.chineseName, fullPath, localizedFullPath);
    buildWarehouseCategoryLookup(node.children || [], currentPath, displayPath, lookup2);
    for (const child of node.children || []) {
      const childDescendantGuids = lookup2.descendantGuidsByGuid.get(child.categoryGUID);
      childDescendantGuids?.forEach((guid) => descendantGuids.add(guid));
    }
    if (node.categoryGUID) {
      lookup2.descendantGuidsByGuid.set(node.categoryGUID, descendantGuids);
    }
  }
  return lookup2;
}
function getWarehouseProductCategoryTooltip(record, lookup2, language) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language);
  if (record.warehouseCategoryGUID) {
    const pathByGuid = lookup2.displayByGuid.get(record.warehouseCategoryGUID)?.[displayLanguage];
    if (pathByGuid) {
      return pathByGuid;
    }
  }
  const name = record.categoryName?.trim();
  if (!name) {
    return record.categoryPath ? localizeCategoryPathText(record.categoryPath, displayLanguage) : void 0;
  }
  const pathsByName = lookup2.byName.get(name);
  const displayPathsByName = lookup2.displayByName.get(name)?.[displayLanguage];
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

// src/pages/Warehouse/Products/categoryPath.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var categories = [
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
        children: []
      },
      {
        categoryGUID: "cat-laundry",
        categoryName: "Laundry",
        chineseName: "\u6D17\u8863",
        isActive: true,
        children: []
      },
      {
        categoryGUID: "cat-tools",
        categoryName: "Tools",
        isActive: true,
        children: []
      },
      {
        categoryGUID: "cat-cn-only",
        categoryName: "",
        chineseName: "\u4E2D\u6587\u5206\u7C7B",
        isActive: true,
        children: []
      }
    ]
  },
  {
    categoryGUID: "cat-promo",
    categoryName: "Promotion",
    chineseName: "\u4FC3\u9500",
    isActive: true,
    children: [
      {
        categoryGUID: "cat-bath-promo",
        categoryName: "Bath",
        chineseName: "\u6D74\u5BA4",
        isActive: true,
        children: []
      }
    ]
  }
];
var lookup = buildWarehouseCategoryLookup(categories);
assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: "Bath" },
    lookup
  ),
  "\u5BB6\u5C45 > \u6D74\u5BA4\uFF1B\u4FC3\u9500 > \u6D74\u5BA4",
  "\u53EA\u6709\u5206\u7C7B\u540D\u79F0\u65F6\u5E94\u4ECE\u5206\u7C7B\u6811\u53CD\u67E5\u9ED8\u8BA4\u4E2D\u6587\u5B8C\u6574\u8DEF\u5F84"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: "Bath" },
    lookup,
    "en"
  ),
  "Home > Bath\uFF1BPromotion > Bath",
  "\u53EA\u6709\u5206\u7C7B\u540D\u79F0\u65F6\u5E94\u652F\u6301\u53CD\u67E5\u82F1\u6587\u5B8C\u6574\u8DEF\u5F84"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: "Bath",
      warehouseCategoryGUID: "cat-bath"
    },
    lookup
  ),
  "\u5BB6\u5C45 > \u6D74\u5BA4",
  "\u5546\u54C1\u5E26\u5206\u7C7B GUID \u65F6\u5E94\u4F18\u5148\u4F7F\u7528 GUID \u5BF9\u5E94\u4E2D\u6587\u5B8C\u6574\u8DEF\u5F84"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryName: "Bath",
      warehouseCategoryGUID: "cat-bath",
      categoryPath: "Backend / \u540E\u7AEF > Full / \u5B8C\u6574 > Path / \u8DEF\u5F84"
    },
    lookup,
    "en"
  ),
  "Home > Bath",
  "\u5546\u54C1\u5E26\u5206\u7C7B GUID \u65F6\u5E94\u4F18\u5148\u4F7F\u7528\u5206\u7C7B\u6811\u8DEF\u5F84\u4FDD\u6301\u8BED\u8A00\u4E00\u81F4"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryPath: "Backend / \u540E\u7AEF > Full / \u5B8C\u6574 > Path / \u8DEF\u5F84"
    },
    lookup
  ),
  "\u540E\u7AEF > \u5B8C\u6574 > \u8DEF\u5F84",
  "\u6CA1\u6709\u5206\u7C7B GUID \u548C\u540D\u79F0\u65F6\u4E2D\u6587\u754C\u9762\u5E94\u672C\u5730\u5316\u540E\u7AEF\u5B8C\u6574\u8DEF\u5F84"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    {
      categoryPath: "Home / \u5BB6\u5C45 > Laundry / \u6D17\u8863"
    },
    lookup,
    "en"
  ),
  "Home > Laundry",
  "\u6CA1\u6709\u5206\u7C7B GUID \u548C\u540D\u79F0\u65F6\u82F1\u6587\u754C\u9762\u5E94\u672C\u5730\u5316\u540E\u7AEF\u5B8C\u6574\u8DEF\u5F84"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: "Tools", warehouseCategoryGUID: "cat-tools" },
    lookup,
    "zh"
  ),
  "\u5BB6\u5C45 > Tools",
  "\u4E2D\u6587\u754C\u9762\u9047\u5230\u7F3A\u4E2D\u6587\u540D\u8282\u70B9\u65F6\u5E94\u56DE\u9000\u82F1\u6587\u540D"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: "\u4E2D\u6587\u5206\u7C7B", warehouseCategoryGUID: "cat-cn-only" },
    lookup,
    "en"
  ),
  "Home > \u4E2D\u6587\u5206\u7C7B",
  "\u82F1\u6587\u754C\u9762\u9047\u5230\u7F3A\u82F1\u6587\u540D\u8282\u70B9\u65F6\u5E94\u56DE\u9000\u4E2D\u6587\u540D"
);
assertEqual(
  getWarehouseProductCategoryTooltip(
    { categoryName: "Unknown" },
    lookup
  ),
  "Unknown",
  "\u5206\u7C7B\u6811\u65E0\u6CD5\u5339\u914D\u65F6\u5E94\u9000\u56DE\u5206\u7C7B\u540D\u79F0"
);
console.log("warehouseProducts.categoryPath.test: ok");
