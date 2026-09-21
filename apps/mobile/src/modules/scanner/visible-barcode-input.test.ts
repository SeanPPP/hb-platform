import assert from "node:assert/strict";
import { extractVisibleBarcodeInput } from "./visible-barcode-input";

const barcode = "9528503822120";

assert.equal(
  extractVisibleBarcodeInput(barcode, `${barcode}${barcode}`),
  barcode,
  "同一条码追加到旧条码后面时，只返回新增段",
);
assert.equal(
  extractVisibleBarcodeInput("HB038-12", "HB038-129528503822005"),
  "9528503822005",
  "SKU 后追加整段条码时，不得把旧 SKU 带入查询",
);
assert.equal(extractVisibleBarcodeInput("", barcode), barcode, "空输入框应接受一次性完整条码");
assert.equal(
  extractVisibleBarcodeInput("HB038-12", barcode),
  barcode,
  "全选替换 SKU 时应识别完整条码",
);
assert.equal(
  extractVisibleBarcodeInput("9300000000001", "9528503822120"),
  "9528503822120",
  "完全替换旧条码时应返回新条码",
);
assert.equal(
  extractVisibleBarcodeInput("SKU-OLD", "9528503822120SKU-OLD"),
  barcode,
  "在旧值开头插入条码时应只返回插入段",
);
assert.equal(
  extractVisibleBarcodeInput("SKU-OLD", "SKU-9528503822120OLD"),
  barcode,
  "在旧值中间插入条码时应只返回插入段",
);

assert.equal(extractVisibleBarcodeInput("HB038-12", "HB038-123"), null, "单字符慢速手输不能当作扫码");
assert.equal(extractVisibleBarcodeInput(barcode, barcode.slice(0, -1)), null, "删除字符不能当作新条码");
assert.equal(extractVisibleBarcodeInput(barcode, barcode), null, "值未变化时不能重复查询");
assert.equal(extractVisibleBarcodeInput("SKU", "SKU95285A3822120"), null, "新增段含字母时不能当作条码");
assert.equal(extractVisibleBarcodeInput("SKU", "SKU1234567"), null, "少于八位的新增数字不能当作条码");
assert.equal(
  extractVisibleBarcodeInput("9528503822120", "9528503822121"),
  null,
  "同长度只改一个字符属于手工编辑，不能当作整码替换",
);
assert.equal(
  extractVisibleBarcodeInput("9528503822120", "95285038221"),
  null,
  "从旧值纯删除得到的合法长度数字也不能当作整码替换",
);
assert.equal(extractVisibleBarcodeInput("", "1234567890123456789"), null, "超过十八位不能当作条码");

console.log("visible-barcode-input.test.ts: ok");
