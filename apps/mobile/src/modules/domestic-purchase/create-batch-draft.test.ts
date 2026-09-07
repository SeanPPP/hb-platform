import assert from "node:assert/strict";
import { unwrapApiEnvelope } from "../../shared/api/api-envelope";
import { ProductCreationType } from "./types";
import type { DomesticSetTemplateDetail } from "./types";
import { applyTemplate, batchAddDrafts, buildBatchItems, buildTemplatePayload, createRequestScope, createSubmissionGate, draftFromTemplate, DraftValidationError, isExplicitCreateBusinessRejection, newProduct, newSubItem, summarizeBatch } from "./create-batch-draft";

const template: DomesticSetTemplateDetail = {
  templateId: "t1", supplierCode: "HB001", templateName: "杯组", setProductName: "茶杯套装",
  isEnabled: true, setQuantity: 2,
  subItems: [{ productName: "大杯", privateLabelPrice: 5, sortOrder: 2 }, { productName: "小杯", privateLabelPrice: 0, sortOrder: 1 }],
};

const set = { ...draftFromTemplate(template), createCount: "3", privateLabelPrice: "12.50", setPrice: "8" };
const products = [newProduct(), newProduct(ProductCreationType.Normal, "0"), set];
const payload = buildBatchItems(products);
assert.deepEqual(summarizeBatch(products), { normal: 2, sets: 3, subItems: 6, total: 11 });
assert.equal(payload[0].privateLabelPrice, null);
assert.equal(payload[1].privateLabelPrice, 0);
assert.equal(payload[2].createCount, 3);
assert.equal(payload[2].privateLabelPrice, 12.5);
assert.equal(payload[2].setPrice, 8);
assert.deepEqual(payload[2].subItems?.map((i) => [i.productName, i.privateLabelPrice, i.productType]), [["小杯", 0, 2], ["大杯", 5, 2]]);
assert.equal(buildBatchItems([{ ...set, subItems: [...set.subItems, newSubItem()] }])[0].subItems?.length, 2);
assert.equal(buildBatchItems([{ ...set, subItems: [{ ...newSubItem(), privateLabelPrice: "0" }] }])[0].subItems?.length, 1);

function rejects(code: string, action: () => unknown) {
  assert.throws(action, (error) => error instanceof DraftValidationError && error.code === code);
}
rejects("missingSubItems", () => buildBatchItems([newProduct(ProductCreationType.Set)]));
rejects("emptyProducts", () => buildBatchItems([]));
for (const value of ["0", "-1", "1.5", "", "NaN", "Infinity", "9007199254740992"]) {
  rejects("invalidSetCount", () => buildBatchItems([{ ...set, createCount: value }]));
}
for (const price of ["-1", "x", "NaN", "Infinity"]) {
  rejects("invalidPrice", () => buildBatchItems([newProduct(ProductCreationType.Normal, price)]));
  rejects("invalidPrice", () => buildBatchItems([{ ...set, subItems: [{ ...newSubItem(), privateLabelPrice: price }] }]));
}
rejects("invalidSetQuantity", () => buildBatchItems([{ ...set, setQuantity: "1.2" }]));
for (const count of ["0", "101", "2.5", ""]) rejects("invalidBatchCount", () => batchAddDrafts([], ProductCreationType.Normal, count, "", "append"));
assert.equal(batchAddDrafts(products, ProductCreationType.Normal, "5", "0", "append").length, 8);
assert.equal(batchAddDrafts(products, ProductCreationType.Normal, "95", "", "append").length, 98);
rejects("tooManyParentItems", () => batchAddDrafts(products, ProductCreationType.Normal, "96", "", "append"));
const replaced = batchAddDrafts(products, ProductCreationType.Set, "5", "", "overwrite");
assert.equal(replaced.length, 5);
assert.ok(replaced.every((p) => p.productType === ProductCreationType.Set && p.createCount === "1"));

const placeholder = newProduct();
const manual = newProduct();
const applied = applyTemplate([placeholder, manual], template, placeholder.key);
assert.equal(applied[0].key, manual.key);
assert.equal(applied.length, 2);
assert.equal(applyTemplate([{ ...placeholder, privateLabelPrice: "0" }], template, placeholder.key).length, 2);
const second = draftFromTemplate(template);
assert.notEqual(second.key, applied[1].key);
assert.notEqual(second.subItems[0].key, applied[1].subItems[0].key);
second.subItems[0].productName = "修改";
assert.equal(applied[1].subItems[0].productName, "小杯");
assert.equal(second.privateLabelPrice, "");
assert.equal(second.setPrice, "");
assert.equal(second.createCount, "1");
assert.deepEqual(buildTemplatePayload("HB001", " 茶杯 ", set).subItems.map((s) => s.privateLabelPrice), [0, 5]);
rejects("templateNameRequired", () => buildTemplatePayload("HB001", " ", set));
rejects("setNameRequired", () => buildTemplatePayload("HB001", "杯", { ...set, productName: "" }));
rejects("subNameRequired", () => buildTemplatePayload("HB001", "杯", { ...set, subItems: [newSubItem()] }));
rejects("subPriceRequired", () => buildTemplatePayload("HB001", "杯", { ...set, subItems: [{ ...newSubItem(), productName: "杯" }] }));

rejects("tooManyParentItems", () => buildBatchItems([{ ...set, createCount: "2147483647" }]));
rejects("tooManyParentItems", () => buildBatchItems([
  { ...set, createCount: "60" },
  { ...set, key: newProduct().key, createCount: "41" },
]));
const manySubItems = Array.from({ length: 100 }, (_, index) => ({
  ...newSubItem(), productName: `子项 ${index + 1}`,
}));
rejects("tooManyExpandedItems", () => buildBatchItems([{ ...set, createCount: "100", subItems: manySubItems }]));
assert.equal(buildBatchItems([{ ...set, createCount: "100", subItems: manySubItems.slice(0, 99) }])[0]?.createCount, 100);

assert.equal(isExplicitCreateBusinessRejection({ response: { status: 400, data: { success: false, errorCode: "VALIDATION_ERROR" } } }), true);
assert.equal(isExplicitCreateBusinessRejection({ success: false, errorCode: "CREATE_BATCH_LIMIT_EXCEEDED" }), true);
assert.equal(isExplicitCreateBusinessRejection({ response: { status: 200, data: { success: false, ErrorCode: "CREATE_BATCH_LIMIT_EXCEEDED" } } }), true);
let unwrappedBusinessError: unknown;
try {
  unwrapApiEnvelope({ success: false, message: "数量超限", ErrorCode: "CREATE_BATCH_LIMIT_EXCEEDED" });
} catch (error) {
  unwrappedBusinessError = error;
}
assert.equal(isExplicitCreateBusinessRejection(unwrappedBusinessError), true);
assert.equal(isExplicitCreateBusinessRejection(Object.assign(new Error("服务异常"), { code: "CREATE_BATCH_ERROR" })), false);
assert.equal(isExplicitCreateBusinessRejection({ response: { status: 400, data: { success: false, errorCode: "CREATE_BATCH_ERROR" } } }), false);
assert.equal(isExplicitCreateBusinessRejection({ response: { status: 200, data: { success: false, ErrorCode: "CREATE_BATCH_ERROR" } } }), false);
assert.equal(isExplicitCreateBusinessRejection({ response: { status: 500, data: { success: false, errorCode: "CREATE_BATCH_ERROR" } } }), false);
assert.equal(isExplicitCreateBusinessRejection({ response: { status: 408 } }), false);
assert.equal(isExplicitCreateBusinessRejection(Object.assign(new Error("timeout"), { code: "ECONNABORTED" })), false);
assert.equal(isExplicitCreateBusinessRejection(Object.assign(new Error("cancelled"), { code: "ERR_CANCELED" })), false);
assert.equal(isExplicitCreateBusinessRejection(new Error("无法分类")), false);

async function testRequests() {
  const scope = createRequestScope();
  const supplierA = scope.begin();
  const supplierB = scope.begin();
  assert.equal(supplierA(), false);
  assert.equal(supplierB(), true);
  scope.invalidate();
  assert.equal(supplierB(), false);

  const gate = createSubmissionGate();
  let requests = 0;
  let finish!: () => void;
  const first = gate.run(async () => { requests++; await new Promise<void>((resolve) => { finish = resolve; }); return "batch-1"; });
  assert.equal(gate.busy, true);
  assert.equal(await gate.run(async () => { requests++; return "duplicate"; }), undefined);
  finish();
  assert.equal(await first, "batch-1");
  assert.equal(requests, 1);
  await assert.rejects(gate.run(async () => { throw new Error("business failure"); }));
  assert.equal(gate.busy, false);
  assert.equal(await gate.run(async () => "manual retry"), "manual retry");
}
void testRequests().then(() => console.log("create-batch-draft.test.ts: ok"));
