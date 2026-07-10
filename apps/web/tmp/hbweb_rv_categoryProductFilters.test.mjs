// src/pages/Warehouse/Products/categoryPath.ts
function getWarehouseCategoryDisplayLanguage(language) {
  return language?.toLowerCase().startsWith("en") ? "en" : "zh";
}
function formatWarehouseCategoryNodeName(node, language) {
  const displayLanguage = getWarehouseCategoryDisplayLanguage(language);
  const name = node.categoryName.trim();
  const chineseName = node.chineseName?.trim();
  return displayLanguage === "en" ? name || chineseName || "" : chineseName || name || "";
}

// src/pages/Warehouse/Categories/categoryProductFilters.ts
var ALL_PRODUCTS_FILTER_KEY = "__ALL_PRODUCTS__";
var UNCATEGORIZED_PRODUCTS_FILTER_KEY = "__UNCATEGORIZED_PRODUCTS__";
function resolveCategoryProductFilterMode(value) {
  if (value === ALL_PRODUCTS_FILTER_KEY) {
    return { type: "all" };
  }
  if (!value || value === UNCATEGORIZED_PRODUCTS_FILTER_KEY) {
    return { type: "uncategorized" };
  }
  return { type: "category", categoryGuid: value };
}
function hasExecutedCategoryProductQuery(state) {
  return state !== null;
}
function normalizeCategorySearchText(parts) {
  return Array.from(new Set(parts.map((part) => part?.trim()).filter(Boolean))).join(" ").toLowerCase();
}
function formatCategoryOptionName(node, language) {
  if (language) {
    return formatWarehouseCategoryNodeName(node, language);
  }
  return `${node.categoryName}${node.chineseName ? ` / ${node.chineseName}` : ""}`;
}
function buildCategoryOptions(nodes, level = 0, language) {
  return nodes.flatMap((node) => [
    {
      value: node.categoryGUID,
      label: `${level > 0 ? `${"--".repeat(level)} ` : ""}${formatCategoryOptionName(node, language)}`
    },
    ...buildCategoryOptions(node.children || [], level + 1, language)
  ]);
}
function buildFilterCategoryOptions(nodes, t, language) {
  return [
    { value: ALL_PRODUCTS_FILTER_KEY, label: t("warehouse.categories.allCategoryOption") },
    { value: UNCATEGORIZED_PRODUCTS_FILTER_KEY, label: t("warehouse.categories.uncategorizedOption", "\u672A\u5206\u7C7B\u5546\u54C1") },
    ...buildCategoryOptions(nodes, 0, language)
  ];
}
function buildCategoryTreeOptions(nodes, language, parentSearchParts = []) {
  return nodes.map((node) => {
    const title = formatCategoryOptionName(node, language);
    const currentSearchParts = [
      ...parentSearchParts,
      node.categoryName,
      node.chineseName,
      title
    ].filter((part) => Boolean(part?.trim()));
    const children = buildCategoryTreeOptions(node.children || [], language, currentSearchParts);
    return {
      title,
      value: node.categoryGUID,
      key: node.categoryGUID,
      // TreeSelect 搜索只看一个字段，这里把父级路径和中英文名称都压进去。
      searchText: normalizeCategorySearchText(currentSearchParts),
      children: children.length ? children : void 0
    };
  });
}
function buildFilterCategoryTreeOptions(nodes, t, language) {
  const allLabel = t("warehouse.categories.allCategoryOption");
  const uncategorizedLabel = t("warehouse.categories.uncategorizedOption", "\u672A\u5206\u7C7B\u5546\u54C1");
  return [
    {
      title: allLabel,
      value: ALL_PRODUCTS_FILTER_KEY,
      key: ALL_PRODUCTS_FILTER_KEY,
      searchText: normalizeCategorySearchText([allLabel, "all", "\u5168\u90E8\u5546\u54C1"])
    },
    {
      title: uncategorizedLabel,
      value: UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      key: UNCATEGORIZED_PRODUCTS_FILTER_KEY,
      searchText: normalizeCategorySearchText([uncategorizedLabel, "uncategorized", "\u672A\u5206\u7C7B\u5546\u54C1"])
    },
    ...buildCategoryTreeOptions(nodes, language)
  ];
}

// src/pages/Warehouse/Categories/categoryProductFilters.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
var allMode = resolveCategoryProductFilterMode(ALL_PRODUCTS_FILTER_KEY);
assertEqual(allMode.type, "all", "\u9ED8\u8BA4 ALL \u9009\u9879\u5E94\u67E5\u8BE2\u5168\u90E8\u5546\u54C1");
assertEqual(allMode.categoryGuid, void 0, "ALL \u67E5\u8BE2\u4E0D\u5E94\u643A\u5E26\u5206\u7C7B GUID");
var emptyMode = resolveCategoryProductFilterMode(void 0);
assertEqual(emptyMode.type, "uncategorized", "\u6E05\u7A7A\u5206\u7C7B\u7B5B\u9009\u5E94\u67E5\u8BE2\u5206\u7C7B\u4E3A\u7A7A\u5546\u54C1");
assertEqual(emptyMode.categoryGuid, void 0, "\u7A7A\u5206\u7C7B\u67E5\u8BE2\u4E0D\u5E94\u643A\u5E26\u666E\u901A\u5206\u7C7B GUID");
var uncategorizedMode = resolveCategoryProductFilterMode(UNCATEGORIZED_PRODUCTS_FILTER_KEY);
assertEqual(uncategorizedMode.type, "uncategorized", "\u7A7A\u5206\u7C7B\u54E8\u5175\u503C\u5E94\u67E5\u8BE2\u5206\u7C7B\u4E3A\u7A7A\u5546\u54C1");
var categoryMode = resolveCategoryProductFilterMode("cat-guid-1");
assertEqual(categoryMode.type, "category", "\u666E\u901A\u5206\u7C7B GUID \u5E94\u67E5\u8BE2\u8BE5\u5206\u7C7B\u5546\u54C1");
assertEqual(categoryMode.categoryGuid, "cat-guid-1", "\u666E\u901A\u5206\u7C7B\u67E5\u8BE2\u5E94\u4FDD\u7559\u5206\u7C7B GUID");
var allKey = ALL_PRODUCTS_FILTER_KEY;
var uncategorizedKey = UNCATEGORIZED_PRODUCTS_FILTER_KEY;
assert(
  allKey !== uncategorizedKey,
  "ALL \u548C\u7A7A\u5206\u7C7B\u5FC5\u987B\u4F7F\u7528\u4E0D\u540C\u54E8\u5175\u503C\uFF0C\u907F\u514D\u6E05\u7A7A\u4E0B\u62C9\u65F6\u8BEF\u67E5\u5168\u90E8\u5546\u54C1"
);
assertEqual(hasExecutedCategoryProductQuery(null), false, "null \u5E94\u8868\u793A\u5C1A\u672A\u6267\u884C\u5546\u54C1\u67E5\u8BE2");
assertEqual(hasExecutedCategoryProductQuery(allMode), true, "\u663E\u5F0F ALL \u6A21\u5F0F\u5E94\u8868\u793A\u5DF2\u7ECF\u6267\u884C\u5546\u54C1\u67E5\u8BE2");
assertEqual(hasExecutedCategoryProductQuery(emptyMode), true, "\u663E\u5F0F\u7A7A\u5206\u7C7B\u6A21\u5F0F\u5E94\u8868\u793A\u5DF2\u7ECF\u6267\u884C\u5546\u54C1\u67E5\u8BE2");
var filterOptions = buildFilterCategoryOptions([], (key, fallback) => fallback ?? key);
assert(
  filterOptions.some((option) => option.value === UNCATEGORIZED_PRODUCTS_FILTER_KEY && option.label === "\u672A\u5206\u7C7B\u5546\u54C1"),
  "\u5206\u7C7B\u7B5B\u9009\u9009\u9879\u5E94\u5305\u542B\u672A\u5206\u7C7B\u5546\u54C1\u5FEB\u6377\u9009\u9879"
);
var categoryTree = [
  {
    categoryGUID: "cat-home",
    categoryName: "Home",
    chineseName: "\u5BB6\u5C45",
    isActive: true,
    children: [
      {
        categoryGUID: "cat-laundry",
        categoryName: "Laundry",
        chineseName: "\u6D17\u8863",
        isActive: true,
        children: []
      }
    ]
  }
];
var defaultLanguageOptions = buildFilterCategoryOptions(categoryTree, (key, fallback) => fallback ?? key);
var chineseOptions = buildFilterCategoryOptions(categoryTree, (key, fallback) => fallback ?? key, "zh");
var englishOptions = buildFilterCategoryOptions(categoryTree, (key, fallback) => fallback ?? key, "en");
assertEqual(defaultLanguageOptions[2]?.label, "Home / \u5BB6\u5C45", "\u4E0D\u4F20\u8BED\u8A00\u65F6\u5206\u7C7B\u7B5B\u9009\u5E94\u4FDD\u7559\u65E7\u7684\u4E2D\u82F1\u7EC4\u5408\u663E\u793A\uFF0C\u517C\u5BB9\u5206\u7C7B\u7BA1\u7406\u9875");
assertEqual(chineseOptions[2]?.label, "\u5BB6\u5C45", "\u4E2D\u6587\u8BED\u8A00\u4E0B\u5206\u7C7B\u7B5B\u9009\u5E94\u53EA\u663E\u793A\u4E2D\u6587\u540D\u79F0");
assertEqual(chineseOptions[3]?.label, "-- \u6D17\u8863", "\u4E2D\u6587\u8BED\u8A00\u4E0B\u5B50\u5206\u7C7B\u7B5B\u9009\u5E94\u53EA\u663E\u793A\u4E2D\u6587\u540D\u79F0\u5E76\u4FDD\u7559\u5C42\u7EA7\u524D\u7F00");
assertEqual(englishOptions[2]?.label, "Home", "\u82F1\u6587\u8BED\u8A00\u4E0B\u5206\u7C7B\u7B5B\u9009\u5E94\u53EA\u663E\u793A\u82F1\u6587\u540D\u79F0");
assertEqual(englishOptions[3]?.label, "-- Laundry", "\u82F1\u6587\u8BED\u8A00\u4E0B\u5B50\u5206\u7C7B\u7B5B\u9009\u5E94\u53EA\u663E\u793A\u82F1\u6587\u540D\u79F0\u5E76\u4FDD\u7559\u5C42\u7EA7\u524D\u7F00");
var categoryTreeOptions = buildFilterCategoryTreeOptions(
  categoryTree,
  (key, fallback) => fallback ?? key,
  "zh"
);
assertEqual(categoryTreeOptions[0]?.value, ALL_PRODUCTS_FILTER_KEY, "\u6811\u5F62\u5206\u7C7B\u7B5B\u9009\u9996\u9879\u5E94\u4FDD\u7559\u5168\u90E8\u5546\u54C1\u5FEB\u6377\u9009\u9879");
assertEqual(categoryTreeOptions[1]?.value, UNCATEGORIZED_PRODUCTS_FILTER_KEY, "\u6811\u5F62\u5206\u7C7B\u7B5B\u9009\u7B2C\u4E8C\u9879\u5E94\u4FDD\u7559\u672A\u5206\u7C7B\u5FEB\u6377\u9009\u9879");
assertEqual(categoryTreeOptions[2]?.title, "\u5BB6\u5C45", "\u6811\u5F62\u5206\u7C7B\u7236\u7EA7\u8282\u70B9\u5E94\u6309\u5F53\u524D\u8BED\u8A00\u663E\u793A\u5206\u7C7B\u540D");
assertEqual(categoryTreeOptions[2]?.children?.[0]?.title, "\u6D17\u8863", "\u6811\u5F62\u5206\u7C7B\u5B50\u8282\u70B9\u5E94\u4F7F\u7528\u771F\u5B9E children\uFF0C\u4E0D\u518D\u62FC\u63A5\u5C42\u7EA7\u524D\u7F00");
assert(
  !categoryTreeOptions[2]?.children?.[0]?.title.includes("--"),
  "\u6811\u5F62\u5206\u7C7B\u5B50\u8282\u70B9\u6807\u9898\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528 -- \u4F2A\u7F29\u8FDB"
);
assert(
  categoryTreeOptions[2]?.children?.[0]?.searchText.includes("home") && categoryTreeOptions[2]?.children?.[0]?.searchText.includes("\u5BB6\u5C45") && categoryTreeOptions[2]?.children?.[0]?.searchText.includes("laundry") && categoryTreeOptions[2]?.children?.[0]?.searchText.includes("\u6D17\u8863"),
  "\u6811\u5F62\u5206\u7C7B\u641C\u7D22\u5B57\u6BB5\u5E94\u5305\u542B\u7236\u7EA7\u8DEF\u5F84\u548C\u5F53\u524D\u8282\u70B9\u4E2D\u82F1\u6587\u540D\u79F0"
);
console.log("categoryProductFilters.test: ok");
