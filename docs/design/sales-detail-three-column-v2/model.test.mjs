import test from "node:test";
import assert from "node:assert/strict";

import { branches, computeReport, products, sampleFacts, suppliers } from "./model.mjs";

const baseQuery = {
  kind: "australia",
  range: "today",
  compareMode: "week",
  autoCompare: true,
  selectedSupplier: null,
  selectedBranch: null,
  search: "",
  page: 1,
  pageSize: 10,
};

test("样例保留固定 id，名称编码使用截图上下文", () => {
  assert.equal(suppliers.length, 16);
  assert.deepEqual(suppliers.slice(0, 8).map((supplier) => [supplier.name, supplier.code]), [
    ["Hot Bargain", "AU200"], ["Dats", "AU201"], ["Art Wrap", "AU202"], ["Yatsal", "AU203"],
    ["Brazco", "AU204"], ["Bloomsdale", "AU205"], ["Malmar", "AU206"], ["PJ SAS", "AU207"],
  ]);
  assert.deepEqual(suppliers.slice(8).map((supplier) => [supplier.name, supplier.code]), [
    ["玛索文具", "HB215"], ["义乌宏宇工贸", "HB216"], ["汉成家居", "HB217"], ["东信织带", "HB218"],
    ["优佰", "HB219"], ["桐草工艺", "HB220"], ["炫风科技", "HB221"], ["尚美佳", "HB222"],
  ]);
  assert.deepEqual(branches.map((branch) => branch.name), [
    "Orion", "Glendale", "Charlestown Square", "Kotara", "Greenhills", "Kawana", "Lake Haven", "Waratah",
  ]);
  assert.equal(products.length, 64);
  assert.deepEqual([...new Set(products.map((product) => product.imageTone))], ["sage", "sand", "rose", "blue"]);
  for (const supplier of suppliers) assert.equal(products.filter((product) => product.supplierId === supplier.id).length, 4);
  assert.ok(products.every((product) => /^(AU20\d|HB2\d\d)-(GC|WB|PS|GWR)-\d{2}$/.test(product.code)));
  assert.equal(new Set(products.map((product) => product.barcode)).size, products.length);
  assert.ok(products.every((product) => /^\d{13}$/.test(product.barcode)));
  assert.ok(products.every((product) => product.name !== `${product.code}精选商品`));
  assert.equal(sampleFacts.length, products.length * branches.length);
  assert.deepEqual(Object.keys(products[0]).sort(), ["barcode", "code", "id", "imageTone", "name", "supplierId"]);
});

test("三栏筛选口径和分页独立", () => {
  const report = computeReport(baseQuery);
  assert.equal(report.supplierRows.length, 8);
  assert.equal(report.branchRows.length, 8);
  assert.equal(report.productTotal, 32);
  assert.equal(report.productRows.length, 10);
  assert.equal(report.pageCount, 4);

  const supplier = suppliers.find((item) => item.code === "AU200");
  const filtered = computeReport({ ...baseQuery, selectedSupplier: supplier.id, selectedBranch: branches[2].id, pageSize: 3, page: 2 });
  assert.equal(filtered.supplierRows.length, 8, "左栏不应被选中供应商坍缩");
  assert.equal(filtered.branchRows.length, 8, "中栏仍应展示该供应商的全部门店");
  assert.equal(filtered.productTotal, 4);
  assert.equal(filtered.productRows.length, 1);
  assert.ok(filtered.productRows.every((row) => row.supplierId === supplier.id));
});

test("分店反查供应商会重算金额、排名和两个份额", () => {
  const selectedBranch = branches[2];
  const report = computeReport({ ...baseQuery, selectedBranch: selectedBranch.id, pageSize: 100 });
  const branchFacts = sampleFacts.filter((fact) => fact.branchId === selectedBranch.id);
  const current = (fact) => fact.periods.today.current;
  const compare = (fact) => fact.periods.today.compare;
  const sumFacts = (facts, getter) => Math.round(facts.reduce((sum, fact) => sum + getter(fact), 0) * 100) / 100;
  const roundPercent = (value) => Math.round(value * 100) / 100;
  const allCurrentRevenue = sumFacts(branchFacts, (fact) => current(fact).revenue);
  const allCompareRevenue = sumFacts(branchFacts, (fact) => compare(fact).revenue);
  const chinaFacts = branchFacts.filter((fact) => suppliers.find((supplier) => supplier.id === fact.supplierId).kind === "china");
  const chinaCurrentRevenue = sumFacts(chinaFacts, (fact) => current(fact).revenue);
  const chinaCompareRevenue = sumFacts(chinaFacts, (fact) => compare(fact).revenue);
  const expected = suppliers
    .filter((supplier) => supplier.kind === "australia")
    .map((supplier) => {
      const facts = branchFacts.filter((fact) => fact.supplierId === supplier.id);
      const revenue = sumFacts(facts, (fact) => current(fact).revenue);
      const compareRevenue = sumFacts(facts, (fact) => compare(fact).revenue);
      return {
        code: supplier.code,
        revenue,
        compareRevenue,
        share: roundPercent((revenue / allCurrentRevenue) * 100),
        compareShare: roundPercent((compareRevenue / allCompareRevenue) * 100),
        chinaShare: roundPercent((revenue / allCurrentRevenue) * 100),
        compareChinaShare: roundPercent((compareRevenue / allCompareRevenue) * 100),
      };
    })
    .sort((left, right) => right.revenue - left.revenue || left.code.localeCompare(right.code));
  assert.deepEqual(report.supplierRows.map((row) => row.code), expected.map((row) => row.code));
  report.supplierRows.forEach((row, index) => {
    const expectedRow = expected[index];
    for (const key of ["revenue", "compareRevenue", "share", "compareShare", "chinaShare", "compareChinaShare"]) {
      assert.equal(row[key], expectedRow[key], `${row.code}.${key}`);
    }
  });

  const chinaReport = computeReport({ ...baseQuery, kind: "china", selectedBranch: selectedBranch.id, pageSize: 100 });
  const expectedChina = suppliers
    .filter((supplier) => supplier.kind === "china")
    .map((supplier) => {
      const facts = branchFacts.filter((fact) => fact.supplierId === supplier.id);
      const revenue = sumFacts(facts, (fact) => current(fact).revenue);
      const compareRevenue = sumFacts(facts, (fact) => compare(fact).revenue);
      return {
        code: supplier.code,
        revenue,
        compareRevenue,
        // 国内 share 的分母是本分店国内小计，chinaShare 的分母是本分店全品类小计。
        share: roundPercent((revenue / chinaCurrentRevenue) * 100),
        compareShare: roundPercent((compareRevenue / chinaCompareRevenue) * 100),
        chinaShare: roundPercent((revenue / allCurrentRevenue) * 100),
        compareChinaShare: roundPercent((compareRevenue / allCompareRevenue) * 100),
      };
    })
    .sort((left, right) => right.revenue - left.revenue || left.code.localeCompare(right.code));
  assert.deepEqual(chinaReport.supplierRows.map((row) => row.code), expectedChina.map((row) => row.code));
  chinaReport.supplierRows.forEach((row, index) => {
    const expectedRow = expectedChina[index];
    for (const key of ["revenue", "compareRevenue", "share", "compareShare", "chinaShare", "compareChinaShare"]) {
      assert.equal(row[key], expectedRow[key], `中国 ${row.code}.${key}`);
    }
  });

  const noBranch = computeReport({ ...baseQuery, pageSize: 100 });
  const clearedBranch = computeReport({ ...baseQuery, selectedBranch: null, pageSize: 100 });
  assert.deepEqual(clearedBranch.supplierRows, noBranch.supplierRows, "清除分店应恢复全量供应商口径");
});

test("供应商和分店反查的金额与商品全集一致", () => {
  const supplier = suppliers.find((item) => item.code === "AU200");
  const selectedBranch = branches[1];
  const report = computeReport({
    ...baseQuery,
    selectedSupplier: supplier.id,
    selectedBranch: selectedBranch.id,
    pageSize: 100,
  });
  const left = report.supplierRows.find((row) => row.code === supplier.code);
  const middle = report.branchRows.find((row) => row.id === selectedBranch.id);
  assert.equal(report.branchRows.length, 8, "中栏仍保留该供应商的全部分店候选");
  assert.equal(report.productTotal, 4);
  for (const key of ["revenue", "compareRevenue", "quantity", "compareQuantity", "orders", "compareOrders", "grossProfit", "compareGrossProfit"]) {
    assert.equal(left[key], middle[key], `左栏/中栏 ${key}`);
    assert.equal(left[key], report.summary[key], `左栏/summary ${key}`);
    const productTotal = Math.round(report.productRows.reduce((sum, row) => sum + (row[key] ?? 0), 0) * 100) / 100;
    assert.equal(left[key], productTotal, `左栏/商品 total ${key}`);
  }
});

test("搜索影响商品全集，summary 是全集而 pageSummary 是当前页", () => {
  const searched = computeReport({ ...baseQuery, search: "AU200-GC-01", pageSize: 1, page: 1 });
  assert.equal(searched.productTotal, 1);
  assert.equal(searched.summary.productCount, 1);
  assert.equal(searched.pageSummary.productCount, 1);
  assert.equal(searched.summary.branchCount, 8);

  const pageOne = computeReport({ ...baseQuery, pageSize: 2, page: 1 });
  const pageTwo = computeReport({ ...baseQuery, pageSize: 2, page: 2 });
  assert.equal(pageOne.summary.productCount, 32);
  assert.equal(pageTwo.summary.productCount, 32);
  assert.equal(pageOne.summary.revenue, pageTwo.summary.revenue, "翻页不可改变 KPI summary");
  assert.notEqual(pageOne.pageSummary.revenue, pageTwo.pageSummary.revenue);

  const barcodeHit = computeReport({ ...baseQuery, search: products[0].barcode, pageSize: 10 });
  assert.equal(barcodeHit.productTotal, 1);
  assert.equal(barcodeHit.productRows[0].barcode, products[0].barcode);

  const multiWord = computeReport({ ...baseQuery, search: "Hot AU200 Woven", pageSize: 10 });
  assert.equal(multiWord.productTotal, 1, "搜索词需同时命中商品名、供应商名称和供应商编码");
});

test("选中商品可反查唯一供应商和多个分店，但商品候选不坍缩", () => {
  const product = products.find((item) => item.code === "AU200-GC-01");
  const report = computeReport({ ...baseQuery, selectedProduct: product.id, pageSize: 100 });
  assert.equal(report.supplierRows.length, 1);
  assert.equal(report.supplierRows[0].code, "AU200");
  assert.equal(report.branchRows.length, 8);
  assert.equal(report.productTotal, 32, "选中商品不应收窄商品候选面板");
  assert.equal(report.summary.productCount, 1);
  assert.equal(report.pageSummary.productCount, 32);

  const selectedSupplierReport = computeReport({
    ...baseQuery,
    selectedSupplier: product.supplierId,
    selectedProduct: product.id,
    pageSize: 100,
  });
  const left = selectedSupplierReport.supplierRows[0];
  const selectedProductRow = selectedSupplierReport.productRows.find((row) => row.id === product.id);
  const middleTotals = selectedSupplierReport.branchRows.reduce(
    (totals, row) => ({
      revenue: Math.round((totals.revenue + row.revenue) * 100) / 100,
      compareRevenue: Math.round((totals.compareRevenue + row.compareRevenue) * 100) / 100,
    }),
    { revenue: 0, compareRevenue: 0 }
  );
  assert.equal(selectedSupplierReport.branchRows.length, 8);
  assert.equal(left.revenue, selectedProductRow.revenue);
  assert.equal(left.compareRevenue, selectedProductRow.compareRevenue);
  assert.equal(left.revenue, selectedSupplierReport.summary.revenue);
  assert.equal(left.compareRevenue, selectedSupplierReport.summary.compareRevenue);
  assert.equal(left.revenue, middleTotals.revenue);
  assert.equal(left.compareRevenue, middleTotals.compareRevenue);

  const selectedBranch = branches[1];
  const branchFacts = sampleFacts.filter((fact) => fact.branchId === selectedBranch.id);
  const currentRevenue = (fact) => fact.periods.today.current.revenue;
  const compareRevenue = (fact) => fact.periods.today.compare.revenue;
  const roundPercent = (value) => Math.round(value * 100) / 100;
  const branchAllRevenue = Math.round(branchFacts.reduce((sum, fact) => sum + currentRevenue(fact), 0) * 100) / 100;
  const branchAllCompareRevenue = Math.round(branchFacts.reduce((sum, fact) => sum + compareRevenue(fact), 0) * 100) / 100;
  const selectedProductFacts = branchFacts.filter((fact) => fact.productId === product.id);
  const selectedProductRevenue = selectedProductFacts.reduce((sum, fact) => sum + currentRevenue(fact), 0);
  const selectedProductCompareRevenue = selectedProductFacts.reduce((sum, fact) => sum + compareRevenue(fact), 0);
  const selectedBranchProduct = computeReport({
    ...baseQuery,
    selectedBranch: selectedBranch.id,
    selectedProduct: product.id,
    pageSize: 100,
  });
  const selectedBranchProductSupplier = selectedBranchProduct.supplierRows[0];
  assert.equal(selectedBranchProductSupplier.revenue, Math.round(selectedProductRevenue * 100) / 100);
  assert.equal(selectedBranchProductSupplier.compareRevenue, Math.round(selectedProductCompareRevenue * 100) / 100);
  assert.equal(selectedBranchProductSupplier.share, roundPercent((selectedProductRevenue / branchAllRevenue) * 100));
  assert.equal(selectedBranchProductSupplier.compareShare, roundPercent((selectedProductCompareRevenue / branchAllCompareRevenue) * 100));
  assert.equal(selectedBranchProductSupplier.chinaShare, roundPercent((selectedProductRevenue / branchAllRevenue) * 100));
  assert.equal(selectedBranchProductSupplier.compareChinaShare, roundPercent((selectedProductCompareRevenue / branchAllCompareRevenue) * 100));

  const noCompare = computeReport({
    ...baseQuery,
    selectedBranch: selectedBranch.id,
    selectedProduct: product.id,
    autoCompare: false,
    pageSize: 100,
  });
  for (const row of [...noCompare.supplierRows, ...noCompare.branchRows, ...noCompare.productRows, noCompare.summary, noCompare.pageSummary]) {
    for (const key of ["compareRevenue", "compareQuantity", "compareOrders", "compareAov", "compareGrossProfit", "compareMargin", "compareShare", "compareChinaShare"]) {
      if (key in row) assert.equal(row[key], null, `${row.id}.${key}`);
    }
    assert.equal(row.growth, null, `${row.id}.growth`);
  }
});

test("商品均价按金额/数量，供应商和门店客单价按金额/客单数", () => {
  const report = computeReport(baseQuery);
  for (const row of report.productRows) {
    assert.equal(row.aov, row.quantity > 0 ? Math.round((row.revenue / row.quantity) * 100) / 100 : 0);
    assert.equal(row.compareAov, row.compareQuantity > 0 ? Math.round((row.compareRevenue / row.compareQuantity) * 100) / 100 : 0);
  }
  for (const row of [...report.supplierRows, ...report.branchRows]) {
    assert.equal(row.aov, row.orders > 0 ? Math.round((row.revenue / row.orders) * 100) / 100 : 0);
    assert.equal(row.compareAov, row.compareOrders > 0 ? Math.round((row.compareRevenue / row.compareOrders) * 100) / 100 : 0);
  }
});

test("日期范围和比较模式会改变事实层后的结果", () => {
  const week = computeReport({ ...baseQuery, range: "thisWeek", pageSize: 100 });
  const month = computeReport({ ...baseQuery, range: "thisMonth", pageSize: 100 });
  const byDate = computeReport({ ...baseQuery, compareMode: "date", pageSize: 100 });
  assert.ok(week.kindRevenue > month.kindRevenue, "演示本月 09-01 至 09-06 不应大于完整演示周");
  assert.notEqual(byDate.productRows[0].compareRevenue, week.productRows[0].compareRevenue);
});

test("事实层零售单价、数量和订单满足约束，比较事实有涨有跌", () => {
  assert.ok(sampleFacts.every((fact) => fact.unitPrice >= 1.5 && fact.unitPrice <= 15));
  assert.ok(sampleFacts.every((fact) => Number.isInteger(fact.quantity) && Number.isInteger(fact.orders)));
  assert.ok(sampleFacts.every((fact) => fact.quantity >= fact.orders));
  const zeroProductId = products.find((product) => product.code === "AU200-GC-01").id;
  const ratios = sampleFacts
    .filter((fact) => fact.productId !== zeroProductId)
    .map((fact) => fact.compareRevenue / fact.revenue);
  assert.ok(ratios.some((ratio) => ratio < 0.9));
  assert.ok(ratios.some((ratio) => ratio > 1.05));
  assert.ok(sampleFacts.every((fact) => Number.isInteger(fact.periods.thisMonth.current.quantity)));
});

test("零基数商品只剔除自身同期，父级仍有正常比较值", () => {
  const zeroProduct = computeReport({ ...baseQuery, selectedSupplier: "AU200", search: "AU200-GC-01" }).productRows[0];
  assert.equal(zeroProduct.compareRevenue, 0);
  assert.equal(zeroProduct.compareQuantity, 0);
  assert.equal(zeroProduct.growth, null);

  const supplierReport = computeReport({ ...baseQuery, selectedSupplier: "AU200", pageSize: 100 });
  const supplier = supplierReport.supplierRows.find((row) => row.code === "AU200");
  assert.ok(supplier.compareRevenue > 0);
  assert.ok(supplierReport.branchRows[0].compareRevenue > 0);
});

test("缺失成本会沿聚合链保留，关闭对比时 compare 字段均为 null", () => {
  const missingCost = computeReport({ ...baseQuery, kind: "china", search: "HB217-GWR-04" }).productRows[0];
  assert.equal(missingCost.grossProfit, null);
  assert.equal(missingCost.margin, null);
  assert.equal(missingCost.compareGrossProfit, null);
  const report = computeReport({ ...baseQuery, autoCompare: false });
  for (const row of [...report.supplierRows, ...report.branchRows, ...report.productRows, report.summary, report.pageSummary]) {
    for (const key of ["compareRevenue", "compareQuantity", "compareOrders", "compareAov", "compareGrossProfit", "compareMargin", "compareShare", "compareChinaShare"]) {
      if (key in row) assert.equal(row[key], null, `${row.id}.${key}`);
    }
    assert.equal(row.growth, null, `${row.id}.growth`);
  }
});

test("收入总计区分标签和全品类", () => {
  const china = computeReport({ ...baseQuery, kind: "china", pageSize: 100 });
  const australia = computeReport({ ...baseQuery, kind: "australia", pageSize: 100 });
  assert.equal(china.kindRevenue + australia.kindRevenue, china.totalRevenue);
  assert.ok(china.totalRevenue > china.kindRevenue);
});
