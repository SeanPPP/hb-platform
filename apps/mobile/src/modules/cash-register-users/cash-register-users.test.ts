import assert from "node:assert/strict";
import { resolveCashRegisterUserAccess, resolveEffectiveCashRegisterUserAccess } from "./access";
import {
  buildCashRegisterUserGridRequest,
  normalizeCashRegisterUser,
  normalizeCashRegisterUserScope,
} from "./normalize";
import {
  encodeCashRegisterBarcodeModules,
  encodeCode128Modules,
  encodeEan13Modules,
  generateCashRegisterBarcode,
  isValidEan13,
  validateCashRegisterUserForm,
} from "./barcode";
import type { CashRegisterUserFormValues } from "./types";

// 条码生成：13 位、校验位正确、首位不为 0。
for (let index = 0; index < 200; index++) {
  const barcode = generateCashRegisterBarcode();
  assert.match(barcode, /^[1-9]\d{12}$/, "生成的条码必须是首位非 0 的 13 位数字");
  assert.ok(isValidEan13(barcode), "生成的条码必须满足 EAN13 校验位");
}
assert.equal(generateCashRegisterBarcode(() => 0), "1000000000009", "随机源固定时校验位按 EAN13 规则计算");
assert.ok(isValidEan13("6755419997376"));
assert.equal(isValidEan13("6755419997372"), false, "生产里 HQ 同步来的历史条码可能不满足校验位");

// EAN13 模块编码：95 位，起止与中间护线固定；非法值返回 null。
const modules = encodeEan13Modules("4006381333931");
assert.ok(modules);
assert.equal(modules.length, 95);
assert.equal(modules.slice(0, 3), "101");
assert.equal(modules.slice(45, 50), "01010");
assert.equal(modules.slice(92), "101");
// 首位 4 对应奇偶组合 LGLLGG，第二位 0 使用 L 码 0001101。
assert.equal(modules.slice(3, 10), "0001101");
assert.equal(encodeEan13Modules("6755419997372"), null);
assert.equal(encodeEan13Modules("ABC"), null);

// Code128：黄金值取自 Web 端 JsBarcode 输出，保证手机屏幕与 Web 显示条纹一致（奇数位末位切 Code A）。
assert.equal(
  encodeCode128Modules("6755419997372"),
  "110100111001000010110011101000110110001000101011101111011110101000100011010001110101111011001110010101000111101100011101011"
);
assert.equal(
  encodeCode128Modules("123456"),
  "11010011100101100111001000101100011100010110100011011101100011101011"
);
assert.equal(encodeCode128Modules("中文"), null, "不可编码字符返回 null，界面只显示数字");
// 与 Web resolveBarcodeFormat 一致：合法 EAN13 走 EAN13，否则 Code128。
assert.equal(encodeCashRegisterBarcodeModules("4006381333931"), encodeEan13Modules("4006381333931"));
assert.equal(encodeCashRegisterBarcodeModules("6755419997372"), encodeCode128Modules("6755419997372"));

// 表单校验与 Web 规则一致。
const validForm: CashRegisterUserFormValues = {
  storeCode: "1004",
  userGuid: "user-1",
  operatorUser: "VALINDA",
  userBarcode: "6755419997376",
  loginRole: "2",
  remark: "",
  status: true,
};
assert.equal(validateCashRegisterUserForm(validForm), null);
assert.equal(validateCashRegisterUserForm({ ...validForm, storeCode: " " }), "storeRequired");
assert.equal(validateCashRegisterUserForm({ ...validForm, userGuid: "" }), "userRequired");
assert.equal(validateCashRegisterUserForm({ ...validForm, userBarcode: "" }), "barcodeRequired");
assert.equal(validateCashRegisterUserForm({ ...validForm, userBarcode: "123" }), "barcodeLength");
assert.equal(validateCashRegisterUserForm({ ...validForm, operatorUser: "x".repeat(101) }), "operatorTooLong");
assert.equal(validateCashRegisterUserForm({ ...validForm, remark: "x".repeat(501) }), "remarkTooLong");

// 权限矩阵：管理与打印分开授予，Web 的 Store.ManageOperations 不带出移动端能力，设备会话一律不开放。
const accessWith = (...codes: string[]) => ({
  isAdmin: false,
  hasPermission: (code: string) => codes.includes(code),
});
assert.deepEqual(resolveCashRegisterUserAccess(accessWith("CashRegisterUsers.MobileManage"), false), {
  canView: true,
  canManage: true,
  canPrint: false,
});
assert.deepEqual(resolveCashRegisterUserAccess(accessWith("CashRegisterUsers.MobilePrint"), false), {
  canView: true,
  canManage: false,
  canPrint: true,
});
assert.deepEqual(resolveCashRegisterUserAccess(accessWith("Store.ManageOperations"), false), {
  canView: false,
  canManage: false,
  canPrint: false,
});
assert.deepEqual(resolveCashRegisterUserAccess({ isAdmin: true, hasPermission: () => false }, false), {
  canView: true,
  canManage: true,
  canPrint: true,
});
assert.deepEqual(resolveCashRegisterUserAccess({ isAdmin: true, hasPermission: () => true }, true), {
  canView: false,
  canManage: false,
  canPrint: false,
});

// 网格请求与 Web 同形：分店/状态走 filterModel，关键字走 globalSearch。
assert.deepEqual(
  buildCashRegisterUserGridRequest({ storeCode: " 1004 ", keyword: " val ", status: "active", startRow: 30, pageSize: 30 }),
  {
    startRow: 30,
    endRow: 59,
    pageSize: 30,
    globalSearch: "val",
    filterModel: {
      storeCode: { filterType: "text", type: "equals", filter: "1004" },
      status: { filterType: "text", type: "equals", filter: "true" },
    },
  }
);
assert.deepEqual(
  buildCashRegisterUserGridRequest({ storeCode: null, status: "all", startRow: 0, pageSize: 30 }).filterModel,
  {}
);

// 列表项兼容后端 HGUID / userGUID 大小写。
const normalized = normalizeCashRegisterUser({
  HGUID: "h-1",
  userGUID: "u-1",
  username: "valinda",
  operatorUser: "VALINDA",
  userBarcode: " 6755419997376 ",
  loginRole: "2",
  printCount: "3",
  status: true,
});
assert.equal(normalized.hGuid, "h-1");
assert.equal(normalized.userGuid, "u-1");
assert.equal(normalized.userBarcode, "6755419997376");
assert.equal(normalized.printCount, 3);
assert.equal(normalized.status, true);

// 服务端实时权限优先：登录时缓存「无权限」，后台授权后范围接口返回 canPrint 即可放行，无需重新登录。
const noCachedAccess = { canView: false, canManage: false, canPrint: false };
assert.deepEqual(resolveEffectiveCashRegisterUserAccess(noCachedAccess, undefined, false), noCachedAccess);
assert.deepEqual(
  resolveEffectiveCashRegisterUserAccess(noCachedAccess, { canManage: false, canPrint: true }, false),
  { canView: true, canManage: false, canPrint: true }
);
// 撤权同样以服务端为准，缓存里的旧权限不能继续放行。
assert.deepEqual(
  resolveEffectiveCashRegisterUserAccess({ canView: true, canManage: true, canPrint: true }, { canManage: false, canPrint: false }, false),
  noCachedAccess
);
assert.deepEqual(
  resolveEffectiveCashRegisterUserAccess(noCachedAccess, { canManage: true, canPrint: true }, true),
  noCachedAccess,
  "设备会话一律不开放"
);

const scope = normalizeCashRegisterUserScope({
  isAdmin: false,
  canManage: true,
  canPrint: false,
  manageableStores: [{ storeCode: "1002", storeName: "Robinson Road" }, { StoreCode: "1042" }, { storeName: "no-code" }],
});
assert.equal(scope.canManage, true);
assert.equal(scope.canPrint, false);
assert.deepEqual(scope.manageableStores, [
  { storeCode: "1002", storeName: "Robinson Road" },
  { storeCode: "1042", storeName: "1042" },
]);

console.log("cash-register-users.test.ts: ok");
