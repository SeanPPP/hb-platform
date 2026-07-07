// src/pages/PosAdmin/Promotions/promotionStoreScope.test.ts
import assert from "node:assert/strict";

// src/pages/PosAdmin/Promotions/promotionStoreScope.ts
function getPromotionStoreCodes(stores) {
  return (stores ?? []).map((store) => store.storeCode?.trim()).filter((storeCode) => !!storeCode);
}
function isPromotionAllStoresScope(source) {
  return source.scopeType === "Headquarters" || getPromotionStoreCodes(source.stores).length === 0;
}
function getPromotionEditorStoreCodes(source) {
  return isPromotionAllStoresScope(source) ? [] : getPromotionStoreCodes(source.stores);
}

// src/pages/PosAdmin/Promotions/promotionStoreScope.test.ts
assert.equal(
  isPromotionAllStoresScope({ stores: [] }),
  true,
  "\u7A7A stores \u5E94\u663E\u793A\u4E3A\u603B\u90E8/\u5168\u90E8\u5206\u5E97\u8303\u56F4"
);
assert.equal(
  isPromotionAllStoresScope({ scopeType: "Headquarters", stores: [{ id: "1", storeCode: "" }] }),
  true,
  "Headquarters \u8303\u56F4\u5E94\u663E\u793A\u4E3A\u5168\u90E8\u5206\u5E97"
);
assert.deepEqual(
  getPromotionStoreCodes([{ id: "1", storeCode: "S01" }]),
  ["S01"],
  "\u6709\u5177\u4F53\u5206\u5E97\u65F6\u5E94\u56DE\u586B\u539F\u59CB\u5206\u5E97\u7F16\u7801"
);
assert.deepEqual(
  getPromotionEditorStoreCodes({ scopeType: "Headquarters", stores: [{ id: "1", storeCode: "S01" }] }),
  [],
  "Headquarters \u8303\u56F4\u5373\u4F7F\u5E26\u6709\u5206\u5E97\u6570\u636E\u4E5F\u5E94\u6309\u5168\u90E8\u5206\u5E97\u56DE\u586B"
);
assert.equal(
  isPromotionAllStoresScope({ stores: [{ id: "1", storeCode: "S01" }] }),
  false,
  "\u6709\u5177\u4F53\u5206\u5E97\u65F6\u4E0D\u5E94\u663E\u793A\u5168\u90E8\u5206\u5E97\u8303\u56F4\u63D0\u793A"
);
console.log("promotionStoreScope.test: ok");
