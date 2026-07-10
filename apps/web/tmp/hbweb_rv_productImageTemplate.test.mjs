// src/pages/PosAdmin/ProductManagement/productImageTemplate.ts
var COS_SUPPLIER_IMAGE_TEMPLATE = "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/{supplierCode}/{itemNumber}.jpg";
var SUPPLIER_IMAGE_TEMPLATE_PRESETS = {
  malmar: "https://www.malmar.com.au/img/products/thumb/{itemNumber}.jpg",
  yatsal: "https://www.yatsal.com.au/Images/ProductImages/500/{itemNumber}.jpg",
  dats: "https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg"
};
var PRODUCT_IMAGE_URL_MAX_LENGTH = 200;
function getDefaultSupplierImageTemplate(supplier, mode) {
  if (mode === "cos") {
    return COS_SUPPLIER_IMAGE_TEMPLATE;
  }
  const savedTemplate = supplier?.imageBaseUrl?.trim();
  if (savedTemplate) {
    return savedTemplate;
  }
  const supplierCode = supplier?.localSupplierCode?.trim().toLowerCase();
  return supplierCode ? SUPPLIER_IMAGE_TEMPLATE_PRESETS[supplierCode] ?? "" : "";
}
function buildSupplierImageUrl(template, values) {
  return template.split("{supplierCode}").join(encodeURIComponent(values.localSupplierCode)).split("{itemNumber}").join(encodeURIComponent(values.itemNumber));
}
function validateSupplierImageTemplate(template) {
  const value = template.trim();
  if (!value) {
    return "\u8BF7\u8F93\u5165\u56FE\u7247\u57FA\u7840 URL";
  }
  if (!value.includes("{itemNumber}")) {
    return "\u56FE\u7247\u57FA\u7840 URL \u5FC5\u987B\u5305\u542B {itemNumber}";
  }
  if (value.length > PRODUCT_IMAGE_URL_MAX_LENGTH) {
    return `\u56FE\u7247\u57FA\u7840 URL \u4E0D\u80FD\u8D85\u8FC7 ${PRODUCT_IMAGE_URL_MAX_LENGTH} \u4E2A\u5B57\u7B26`;
  }
  return void 0;
}

// src/pages/PosAdmin/ProductManagement/productImageTemplate.test.ts
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
var datsSupplier = {
  guid: "supplier-dats",
  localSupplierCode: "dats",
  name: "Dats",
  status: 1,
  imageBaseUrl: "https://saved.example.com/{itemNumber}.jpg"
};
assertEqual(
  getDefaultSupplierImageTemplate(datsSupplier, "supplier"),
  "https://saved.example.com/{itemNumber}.jpg",
  "\u4F9B\u5E94\u5546\u5DF2\u4FDD\u5B58\u56FE\u7247\u57FA\u7840 URL \u65F6\u5E94\u4F18\u5148\u4F7F\u7528\u4FDD\u5B58\u503C"
);
assertEqual(
  getDefaultSupplierImageTemplate({ ...datsSupplier, imageBaseUrl: "" }, "supplier"),
  "https://www.dats.com.au/images/ProductImages/500/{itemNumber}.jpg",
  "\u4F9B\u5E94\u5546\u56FE\u7247\u57FA\u7840 URL \u4E3A\u7A7A\u65F6\u5E94\u6309\u4F9B\u5E94\u5546\u4EE3\u7801\u4F7F\u7528\u9884\u8BBE\u6A21\u677F"
);
assertEqual(
  getDefaultSupplierImageTemplate({ ...datsSupplier, imageBaseUrl: "" }, "cos"),
  "https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/{supplierCode}/{itemNumber}.jpg",
  "COS \u6A21\u5F0F\u5E94\u4F7F\u7528\u4F9B\u5E94\u5546\u4EE3\u7801\u52A0\u8D27\u53F7\u6A21\u677F"
);
assertEqual(
  buildSupplierImageUrl(
    "https://cdn.example.com/{supplierCode}/{itemNumber}.jpg",
    { localSupplierCode: "dats", itemNumber: "72653" }
  ),
  "https://cdn.example.com/dats/72653.jpg",
  "\u56FE\u7247\u6A21\u677F\u5E94\u66FF\u6362\u4F9B\u5E94\u5546\u4EE3\u7801\u548C\u8D27\u53F7"
);
assertEqual(
  validateSupplierImageTemplate("https://cdn.example.com/{itemNumber}.jpg"),
  void 0,
  "\u5305\u542B\u8D27\u53F7\u5360\u4F4D\u7B26\u7684\u6A21\u677F\u5E94\u901A\u8FC7\u6821\u9A8C"
);
assert(
  validateSupplierImageTemplate("https://cdn.example.com/static.jpg")?.includes("{itemNumber}"),
  "\u7F3A\u5C11\u8D27\u53F7\u5360\u4F4D\u7B26\u65F6\u5E94\u8FD4\u56DE\u6821\u9A8C\u9519\u8BEF"
);
assert(
  validateSupplierImageTemplate(`https://cdn.example.com/${"a".repeat(180)}/{itemNumber}.jpg`)?.includes("200"),
  "\u56FE\u7247\u57FA\u7840 URL \u8D85\u8FC7\u5546\u54C1\u56FE\u7247\u5217\u957F\u5EA6\u65F6\u5E94\u8FD4\u56DE\u6821\u9A8C\u9519\u8BEF"
);
console.log("productImageTemplate.test: ok");
