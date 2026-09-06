// 三栏销售明细原型的数据模型。数据是确定性脱敏样例，不连接生产接口。

const RANGE_SCALE = Object.freeze({
  today: 1,
  yesterday: 0.88,
  thisWeek: 5.8,
  lastWeek: 5.25,
  // 演示日期为 2026-09-06，本月只覆盖 09-01 至 09-06。
  thisMonth: 4.9,
  lastMonth: 20.1,
});

const VALID_RANGES = new Set(Object.keys(RANGE_SCALE));
const VALID_KINDS = new Set(["australia", "china"]);
const IMAGE_TONES = ["sage", "sand", "rose", "blue"];
const PRODUCT_TEMPLATES = [
  { name: "Greeting Cards", code: "GC", retailPrice: 3.2 },
  { name: "Woven Bag", code: "WB", retailPrice: 8.6 },
  { name: "Pencil Set", code: "PS", retailPrice: 4.8 },
  { name: "Gift Wrap Roll", code: "GWR", retailPrice: 6.9 },
];
const PRODUCT_NAME_SETS = [
  ["Woven Bag", "Pencil Set", "Greeting Cards", "Key Ring"],
  ["Storage Basket", "Lunch Bag", "Sticky Notes", "Socks"],
  ["Gift Wrap", "Tissue Paper", "Gift Bag", "Ribbon"],
  ["Hair Clips", "Travel Mug", "Canvas Tote", "Phone Charm"],
  ["Toy Car", "Puzzle Set", "Water Bottle", "Desk Organiser"],
  ["Greeting Card", "Birthday Card", "Thank You Card", "Christmas Card"],
  ["Candle Holder", "Photo Frame", "Vase", "Wall Hook"],
  ["Plush Toy", "Party Banner", "Table Cover", "Confetti Pack"],
  ["Color Pencil Set", "Notebook", "Highlighter", "Gel Pens"],
  ["Sunglasses", "Fashion Bag", "Hair Band", "Makeup Pouch"],
  ["Kitchen Towel", "Storage Box", "Laundry Basket", "Door Mat"],
  ["Ribbon", "Elastic Band", "Lace Trim", "Webbing Tape"],
  ["Phone Cable", "LED Light", "USB Adapter", "Earbuds Case"],
  ["Rattan Basket", "Bamboo Tray", "Jute Rope", "Craft Mat"],
  ["Mini Fan", "Calculator", "Desk Lamp", "Alarm Clock"],
  ["Artificial Flower", "Table Runner", "Cushion Cover", "Decorative Box"],
];

export const suppliers = Object.freeze([
  ["Hot Bargain", "AU200", "australia"],
  ["Dats", "AU201", "australia"],
  ["Art Wrap", "AU202", "australia"],
  ["Yatsal", "AU203", "australia"],
  ["Brazco", "AU204", "australia"],
  ["Bloomsdale", "AU205", "australia"],
  ["Malmar", "AU206", "australia"],
  ["PJ SAS", "AU207", "australia"],
  ["玛索文具", "HB215", "china"],
  ["义乌宏宇工贸", "HB216", "china"],
  ["汉成家居", "HB217", "china"],
  ["东信织带", "HB218", "china"],
  ["优佰", "HB219", "china"],
  ["桐草工艺", "HB220", "china"],
  ["炫风科技", "HB221", "china"],
  ["尚美佳", "HB222", "china"],
].map(([name, code, kind], index) => ({
  id: `supplier-${kind === "australia" ? "au" : "cn"}-${String(index % 8 + 1).padStart(2, "0")}`,
  code,
  name,
  kind,
})));

export const branches = Object.freeze(
  ["Orion", "Glendale", "Charlestown Square", "Kotara", "Greenhills", "Kawana", "Lake Haven", "Waratah"].map(
    (name, index) => ({ id: `branch-${String(index + 1).padStart(2, "0")}`, name })
  )
);

export const products = Object.freeze(
  suppliers.flatMap((supplier, supplierIndex) =>
    PRODUCT_TEMPLATES.map((template, productIndex) => ({
      id: `${supplier.id}-product-${productIndex + 1}`,
      code: `${supplier.code}-${template.code}-${String(productIndex + 1).padStart(2, "0")}`,
      barcode: String(9300000000000 + supplierIndex * 100 + productIndex + 1),
      name: PRODUCT_NAME_SETS[supplierIndex][productIndex],
      supplierId: supplier.id,
      imageTone: IMAGE_TONES[productIndex],
    }))
  )
);

// 该商品存在当前销售，但比较期在每个事实中都是零，故所有上层聚合都会自然排除同期。
const ZERO_COMPARE_PRODUCT_ID = products.find((product) => product.code === "AU200-GC-01").id;
const MISSING_COST_PRODUCT_ID = products.find((product) => product.code === "HB217-GWR-04").id;

const round = (value) => Math.round((value + Number.EPSILON) * 100) / 100;
const roundInt = (value) => Math.max(0, Math.round(value));
const supplierById = new Map(suppliers.map((supplier) => [supplier.id, supplier]));
const productById = new Map(products.map((product) => [product.id, product]));
const productMetaById = new Map(products.map((product) => [product.id, {
  supplierIndex: suppliers.findIndex((supplier) => supplier.id === product.supplierId),
  productIndex: PRODUCT_TEMPLATES.findIndex((template) => product.code.includes(`-${template.code}-`)),
  retailPrice: PRODUCT_TEMPLATES.find((template) => product.code.includes(`-${template.code}-`)).retailPrice,
}]));

function makeFact(product, branch, supplierIndex, branchIndex) {
  const { productIndex, retailPrice } = productMetaById.get(product.id);
  const seed = (supplierIndex + 3) * 29 + (productIndex + 1) * 17 + (branchIndex + 5) * 11;
  const baseQuantity = 2 + (seed % 7);
  const baseOrders = Math.max(1, Math.min(baseQuantity, 1 + (seed % 3)));
  const currentPrice = round(retailPrice * (0.93 + ((supplierIndex + branchIndex) % 5) * 0.02));
  const comparePrice = round(retailPrice * (0.9 + ((supplierIndex + productIndex + branchIndex * 2) % 7) * 0.03));
  const dateComparePrice = round(comparePrice * (0.94 + ((branchIndex + productIndex * 2) % 4) * 0.025));
  // 同期比较因商品和门店而异，确保增长率与均价不会整列相同。
  const compareDemandFactor = 0.72 + ((supplierIndex * 3 + productIndex * 5 + branchIndex) % 9) * 0.075;
  const hasCost = product.id !== MISSING_COST_PRODUCT_ID;
  const marginRate = 0.18 + ((supplierIndex + productIndex + branchIndex) % 8) * 0.025;
  const periods = {};

  for (const [range, scale] of Object.entries(RANGE_SCALE)) {
    const currentQuantity = roundInt(baseQuantity * scale);
    const currentOrders = Math.min(currentQuantity, roundInt(baseOrders * scale));
    const currentRevenue = round(currentQuantity * currentPrice);
    const makeCompareFact = (demandFactor, unitPrice) => {
      const compareQuantity = product.id === ZERO_COMPARE_PRODUCT_ID ? 0 : roundInt(baseQuantity * scale * demandFactor);
      const compareOrders = product.id === ZERO_COMPARE_PRODUCT_ID
        ? 0
        : Math.min(compareQuantity, roundInt(baseOrders * scale * demandFactor * (0.82 + (branchIndex % 4) * 0.06)));
      const compareRevenue = round(compareQuantity * unitPrice);
      return {
        revenue: compareRevenue,
        quantity: compareQuantity,
        orders: compareOrders,
        grossProfit: hasCost ? round(compareRevenue * marginRate) : null,
      };
    };
    const compare = makeCompareFact(compareDemandFactor, comparePrice);
    const compareDate = makeCompareFact(compareDemandFactor * (0.9 + ((supplierIndex + branchIndex) % 5) * 0.035), dateComparePrice);
    periods[range] = {
      current: {
        revenue: currentRevenue,
        quantity: currentQuantity,
        orders: currentOrders,
        grossProfit: hasCost ? round(currentRevenue * marginRate) : null,
      },
      compare,
      compareDate,
    };
  }

  return {
    id: `${product.id}-${branch.id}`,
    productId: product.id,
    supplierId: product.supplierId,
    branchId: branch.id,
    unitPrice: currentPrice,
    compareUnitPrice: comparePrice,
    periods,
    // 保留 today 平铺字段，便于调试样例事实而不改变 computeReport 接口。
    revenue: periods.today.current.revenue,
    quantity: periods.today.current.quantity,
    orders: periods.today.current.orders,
    grossProfit: periods.today.current.grossProfit,
    compareRevenue: periods.today.compare.revenue,
    compareQuantity: periods.today.compare.quantity,
    compareOrders: periods.today.compare.orders,
    compareGrossProfit: periods.today.compare.grossProfit,
  };
}

// 每个门店都覆盖全部供应商，避免原型筛选时出现不真实的空列。
const FACTS = Object.freeze(
  products.flatMap((product) => {
    const supplierIndex = suppliers.findIndex((supplier) => supplier.id === product.supplierId);
    return branches.map((branch, branchIndex) => makeFact(product, branch, supplierIndex, branchIndex));
  })
);

function normalizeKind(kind) {
  return VALID_KINDS.has(kind) ? kind : "australia";
}

function normalizeRange(range) {
  return VALID_RANGES.has(range) ? range : "today";
}

function resolveSupplier(value) {
  if (!value) return null;
  return suppliers.find((supplier) => supplier.id === value || supplier.code === value) ?? null;
}

function resolveBranch(value) {
  if (!value) return null;
  return branches.find((branch) => branch.id === value) ?? null;
}

function resolveProduct(value) {
  if (!value) return null;
  return productById.get(value) ?? null;
}

function sum(values) {
  return round(values.reduce((total, value) => total + value, 0));
}

function metricFromFacts(facts, range, compareMode, autoCompare, averageByQuantity = false) {
  const currentFacts = facts.map((fact) => fact.periods[range].current);
  const compareFacts = facts.map((fact) => fact.periods[range][compareMode === "date" ? "compareDate" : "compare"]);
  const current = {
    revenue: sum(currentFacts.map((fact) => fact.revenue)),
    quantity: sum(currentFacts.map((fact) => fact.quantity)),
    orders: sum(currentFacts.map((fact) => fact.orders)),
    grossProfit: null,
  };
  const compare = {
    revenue: sum(compareFacts.map((fact) => fact.revenue)),
    quantity: sum(compareFacts.map((fact) => fact.quantity)),
    orders: sum(compareFacts.map((fact) => fact.orders)),
    grossProfit: null,
  };
  const hasMissingCost = currentFacts.some((fact) => fact.grossProfit === null && fact.revenue > 0)
    || compareFacts.some((fact) => fact.grossProfit === null && fact.revenue > 0);
  if (!hasMissingCost) {
    current.grossProfit = sum(currentFacts.map((fact) => fact.grossProfit));
    compare.grossProfit = sum(compareFacts.map((fact) => fact.grossProfit));
  }

  const currentAverageBase = averageByQuantity ? current.quantity : current.orders;
  const compareAverageBase = averageByQuantity ? compare.quantity : compare.orders;
  const aov = currentAverageBase > 0 ? round(current.revenue / currentAverageBase) : 0;
  const compareAov = compareAverageBase > 0 ? round(compare.revenue / compareAverageBase) : 0;
  const margin = current.grossProfit === null || current.revenue <= 0
    ? null
    : round((current.grossProfit / current.revenue) * 100);
  const compareMargin = compare.grossProfit === null || compare.revenue <= 0
    ? null
    : round((compare.grossProfit / compare.revenue) * 100);

  return {
    revenue: current.revenue,
    compareRevenue: autoCompare ? compare.revenue : null,
    quantity: current.quantity,
    compareQuantity: autoCompare ? compare.quantity : null,
    orders: current.orders,
    compareOrders: autoCompare ? compare.orders : null,
    aov,
    compareAov: autoCompare ? compareAov : null,
    grossProfit: current.grossProfit,
    compareGrossProfit: autoCompare ? compare.grossProfit : null,
    margin,
    compareMargin: autoCompare ? compareMargin : null,
    // 零基数、关闭对比或不可比时均不伪造增长率。
    growth: !autoCompare || compare.revenue === 0
      ? null
      : round(((current.revenue - compare.revenue) / compare.revenue) * 100),
  };
}

function withRowIdentity(identity, facts, options, averageByQuantity = false) {
  return {
    ...identity,
    ...metricFromFacts(facts, options.range, options.compareMode, options.autoCompare, averageByQuantity),
  };
}

function aggregateFactsBy(facts, key) {
  const groups = new Map();
  for (const fact of facts) {
    const groupValue = fact[key];
    const group = groups.get(groupValue) ?? [];
    group.push(fact);
    groups.set(groupValue, group);
  }
  return groups;
}

function currentTotal(kind, options) {
  const kindFacts = FACTS.filter((fact) => supplierById.get(fact.supplierId).kind === kind);
  return metricFromFacts(kindFacts, options.range, options.compareMode, options.autoCompare);
}

function totalsForAllKinds(options) {
  return metricFromFacts(FACTS, options.range, options.compareMode, options.autoCompare);
}

function ratio(value, denominator) {
  return denominator > 0 ? round((value / denominator) * 100) : null;
}

function summaryForProducts(rows, options, name) {
  const facts = rows.flatMap((row) => row.__facts ?? []);
  const summary = withRowIdentity(
    {
      id: "summary",
      code: "SUMMARY",
      name,
      supplierCount: new Set(facts.map((fact) => fact.supplierId)).size,
      branchCount: new Set(facts.map((fact) => fact.branchId)).size,
      productCount: rows.length,
    },
    facts,
    options
  );
  delete summary.__facts;
  return summary;
}

function sortByRevenue(rows) {
  return rows.sort((left, right) => right.revenue - left.revenue || left.code.localeCompare(right.code));
}

export function computeReport(input = {}) {
  const kind = normalizeKind(input.kind);
  const range = normalizeRange(input.range);
  const compareMode = input.compareMode === "date" ? "date" : "week";
  const autoCompare = input.autoCompare !== false;
  const selectedSupplier = resolveSupplier(input.selectedSupplier);
  const selectedBranch = resolveBranch(input.selectedBranch);
  const selectedProduct = resolveProduct(input.selectedProduct);
  const searchTokens = typeof input.search === "string"
    ? input.search.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean)
    : [];
  const pageSize = Number.isFinite(input.pageSize) && input.pageSize > 0 ? Math.floor(input.pageSize) : 10;
  const options = { range, compareMode, autoCompare };
  const kindFacts = FACTS.filter((fact) => supplierById.get(fact.supplierId).kind === kind);

  // 选中门店时，左栏改为该门店的供应商反查；selectedSupplier 只影响中、右栏。
  let supplierScopeFacts = selectedBranch
    ? kindFacts.filter((fact) => fact.branchId === selectedBranch.id)
    : kindFacts;
  if (selectedProduct) supplierScopeFacts = supplierScopeFacts.filter((fact) => fact.productId === selectedProduct.id);
  const supplierGroups = aggregateFactsBy(supplierScopeFacts, "supplierId");
  const allTotals = totalsForAllKinds(options);
  const chinaTotals = currentTotal("china", options);
  const supplierScopeAllFacts = selectedBranch
    ? FACTS.filter((fact) => fact.branchId === selectedBranch.id)
    : FACTS;
  const supplierScopeChinaFacts = supplierScopeAllFacts.filter(
    (fact) => supplierById.get(fact.supplierId).kind === "china"
  );
  const supplierScopeTotals = selectedBranch
    ? metricFromFacts(supplierScopeAllFacts, options.range, options.compareMode, options.autoCompare)
    : allTotals;
  const supplierScopeChinaTotals = selectedBranch
    ? metricFromFacts(supplierScopeChinaFacts, options.range, options.compareMode, options.autoCompare)
    : chinaTotals;
  const supplierRows = sortByRevenue(
    [...supplierGroups.entries()].map(([supplierId, facts]) => {
      const supplier = supplierById.get(supplierId);
      const row = withRowIdentity({ id: supplier.id, code: supplier.code, name: supplier.name, supplierId }, facts, options);
      return {
        ...row,
        // 澳洲占全部收入；中国的 share 占国内收入，chinaShare 始终占全部收入。
        share: supplier.kind === "china"
          ? ratio(row.revenue, supplierScopeChinaTotals.revenue)
          : ratio(row.revenue, supplierScopeTotals.revenue),
        compareShare: !autoCompare
          ? null
          : supplier.kind === "china"
            ? ratio(row.compareRevenue, supplierScopeChinaTotals.compareRevenue)
            : ratio(row.compareRevenue, supplierScopeTotals.compareRevenue),
        chinaShare: ratio(row.revenue, supplierScopeTotals.revenue),
        compareChinaShare: !autoCompare ? null : ratio(row.compareRevenue, supplierScopeTotals.compareRevenue),
      };
    })
  );

  let branchFacts = selectedSupplier
    ? kindFacts.filter((fact) => fact.supplierId === selectedSupplier.id)
    : kindFacts;
  if (selectedProduct) branchFacts = branchFacts.filter((fact) => fact.productId === selectedProduct.id);
  const branchGroups = aggregateFactsBy(branchFacts, "branchId");
  const branchRows = sortByRevenue(
    [...branchGroups.entries()].map(([branchId, facts]) => {
      const branch = branches.find((candidate) => candidate.id === branchId);
      return withRowIdentity({ id: branch.id, code: `B${branchId.slice(-2)}`, name: branch.name, branchId }, facts, options);
    })
  );

  let productFacts = kindFacts;
  if (selectedSupplier) productFacts = productFacts.filter((fact) => fact.supplierId === selectedSupplier.id);
  if (selectedBranch) productFacts = productFacts.filter((fact) => fact.branchId === selectedBranch.id);
  const productGroups = aggregateFactsBy(productFacts, "productId");
  let productRows = [...productGroups.entries()].map(([productId, facts]) => {
    const product = productById.get(productId);
    const row = withRowIdentity(
      {
        id: product.id,
        code: product.code,
        barcode: product.barcode,
        name: product.name,
        supplierId: product.supplierId,
        imageTone: product.imageTone,
      },
      facts,
      options,
      true
    );
    row.__facts = facts;
    return row;
  });
  if (searchTokens.length > 0) {
    productRows = productRows.filter((row) => {
      const supplier = supplierById.get(row.supplierId);
      const searchable = `${row.name} ${row.code} ${row.barcode} ${supplier.name} ${supplier.code}`.toLocaleLowerCase();
      return searchTokens.every((token) => searchable.includes(token));
    });
  }
  sortByRevenue(productRows);
  const productTotal = productRows.length;
  const pageCount = Math.max(1, Math.ceil(productTotal / pageSize));
  const requestedPage = Number.isFinite(input.page) && input.page > 0 ? Math.floor(input.page) : 1;
  const page = Math.min(requestedPage, pageCount);
  const pageRows = productRows.slice((page - 1) * pageSize, page * pageSize);
  const summaryRows = selectedProduct
    ? productRows.filter((row) => row.id === selectedProduct.id)
    : productRows;
  const summary = summaryForProducts(summaryRows, options, "筛选汇总");
  const pageSummary = summaryForProducts(pageRows, options, "当前页汇总");

  return {
    supplierRows,
    branchRows,
    productRows: pageRows.map(({ __facts, ...row }) => row),
    productTotal,
    page,
    pageCount,
    summary,
    pageSummary,
    totalRevenue: allTotals.revenue,
    kindRevenue: currentTotal(kind, options).revenue,
  };
}

export const sampleFacts = FACTS;
