import assert from "node:assert/strict";
import {
  getWarehouseProductPdaLayout,
  getWarehouseProductSummaryRows,
  getWarehouseProductSummaryVisualRows,
  getWarehouseProductSections,
  getWarehouseLocationActionMode,
  getProductLocationCandidateAction,
  isProductLocationCandidateDisabled,
  getProductLocationLookupAction,
  getProductLocationScanBindDecision,
  isPickWarehouseLocation,
  canBindMoreProductsToWarehouseLocation,
} from "./pda-layout";

function run() {
  assert.equal(getWarehouseProductPdaLayout(360), "pda", "360dp 斑马 PDA 宽度应启用小屏布局");
  assert.equal(getWarehouseProductPdaLayout(402, { forcePda: true }), "pda", "iPhone 17 应强制启用 PDA 布局");
  assert.equal(getWarehouseProductPdaLayout(402, { forcePda: false }), "regular", "非强制时 402dp 仍保持常规布局");
  assert.equal(getWarehouseProductPdaLayout(430), "regular", "较宽屏幕保持常规布局");

  const pdaSections = getWarehouseProductSections("pda");
  assert.equal(pdaSections.showLocationAction, true, "PDA 主页面应保留货位操作弹窗入口");
  assert.equal(pdaSections.productSummaryColumns, 2, "PDA 商品摘要字段应继续按两栏显示");
  assert.equal(pdaSections.businessFieldsEditableInSummary, true, "PDA 商品业务字段应在商品信息卡片内点击编辑");
  assert.equal(pdaSections.showStandaloneEditorCard, false, "PDA 不应渲染独立商品编辑卡片");
  assert.deepEqual(
    getWarehouseProductSummaryRows("pda"),
    [
      ["itemNumber", "barcode"],
      ["stockQuantity", "location"],
      ["domesticPrice", "purchaseImportPrice", "retailOemPrice"],
      ["volume", "middlePackageQuantity", "packingQuantity"],
      ["grade", "warehouseStatus"],
    ],
    "PDA 商品字段必须按用户指定的业务行分组"
  );
  assert.deepEqual(
    getWarehouseProductSummaryVisualRows("pda"),
    [
      ["itemNumber", "barcode"],
      ["stockQuantity", "location"],
      ["domesticPrice", "purchaseImportPrice"],
      ["retailOemPrice"],
      ["volume", "middlePackageQuantity"],
      ["packingQuantity"],
      ["grade", "warehouseStatus"],
    ],
    "PDA 视觉行必须保持最多两栏"
  );
  assert.ok(
    getWarehouseProductSummaryVisualRows("pda").every((row) => row.length <= 2),
    "PDA 视觉行不能出现三栏"
  );

  const regularSections = getWarehouseProductSections("regular");
  assert.equal(regularSections.businessFieldsEditableInSummary, true, "常规布局也应在商品信息卡片内点击编辑业务字段");
  assert.equal(regularSections.showStandaloneEditorCard, false, "常规布局也不应渲染独立商品编辑卡片");
  assert.equal(regularSections.productSummaryColumns, 2, "常规布局保留双列摘要字段");

  assert.equal(getWarehouseLocationActionMode("bind"), "selection-modal", "绑定货位必须在可查询弹窗中完成");
  assert.equal(getWarehouseLocationActionMode("unbind"), "confirm-modal", "解绑货位必须走确认弹窗");
  assert.equal(isPickWarehouseLocation(1), true, "locationType=1 是配货位");
  assert.equal(isPickWarehouseLocation(null), true, "未知类型按配货位处理，避免误放开一对多");
  assert.equal(canBindMoreProductsToWarehouseLocation(1, 0), true, "空配货位允许绑定商品");
  assert.equal(canBindMoreProductsToWarehouseLocation(1, 1), false, "已有商品的配货位不允许继续绑定");
  assert.equal(canBindMoreProductsToWarehouseLocation(2, 0), true, "空存货位允许绑定商品");
  assert.equal(canBindMoreProductsToWarehouseLocation(2, 3), true, "存货位允许继续追加多个商品");
  assert.equal(getProductLocationScanBindDecision(1, 0), "bind", "扫码命中空配货位应直接绑定");
  assert.equal(getProductLocationScanBindDecision(1, 1), "block", "扫码命中已有商品的配货位应阻止绑定");
  assert.equal(getProductLocationScanBindDecision(2, 0), "bind", "扫码命中空存货位应直接绑定");
  assert.equal(getProductLocationScanBindDecision(2, 1), "confirm", "扫码命中已有商品的存货位应先确认");
  assert.equal(getProductLocationScanBindDecision(null, 1), "block", "未知类型已有商品按配货位阻止绑定");
  assert.equal(
    getProductLocationLookupAction({ source: "manual", matchCount: 1, locationType: 1, productCount: 0 }),
    "showResults",
    "手动回车唯一命中也只展示结果，不自动绑定"
  );
  assert.equal(
    getProductLocationCandidateAction({ locationType: 1, productCount: 0 }),
    "bind",
    "手动候选空配货位应显示绑定按钮"
  );
  assert.equal(
    getProductLocationCandidateAction({ locationType: 2, productCount: 1 }),
    "confirm",
    "手动候选已有商品的存货位应显示继续绑定按钮"
  );
  assert.equal(isProductLocationCandidateDisabled("bind", true), true, "busy 时候选卡片和按钮都应禁用");
  assert.equal(isProductLocationCandidateDisabled("block", false), true, "阻止绑定的候选卡片和按钮都应禁用");
  assert.equal(isProductLocationCandidateDisabled("bind", false), false, "可绑定候选在空闲时可操作");
  assert.equal(
    getProductLocationLookupAction({ source: "scan", matchCount: 0 }),
    "showResults",
    "扫码无结果不自动绑定"
  );
  assert.equal(
    getProductLocationLookupAction({ source: "scan", matchCount: 2 }),
    "showResults",
    "扫码多结果不自动绑定"
  );
  assert.equal(
    getProductLocationLookupAction({ source: "scan", matchCount: 1, locationType: 1, productCount: 0 }),
    "bind",
    "扫码唯一命中空配货位直接绑定"
  );
  assert.equal(
    getProductLocationLookupAction({ source: "scan", matchCount: 1, locationType: 1, productCount: 1 }),
    "block",
    "扫码唯一命中占用配货位阻止绑定"
  );
  assert.equal(
    getProductLocationLookupAction({ source: "scan", matchCount: 1, locationType: 2, productCount: 1 }),
    "confirm",
    "扫码唯一命中占用存货位先确认"
  );

  console.log("pda-layout.test.ts: ok");
}

run();
