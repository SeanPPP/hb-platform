import type { CashRegisterUserFormValues } from "@/modules/cash-register-users/types";

export function computeEan13CheckDigit(first12: string) {
  const sum = first12
    .split("")
    .reduce((total, digit, index) => total + Number(digit) * (index % 2 === 0 ? 1 : 3), 0);
  return String((10 - (sum % 10)) % 10);
}

export function isValidEan13(value: string) {
  const barcode = value.trim();
  return /^\d{13}$/.test(barcode) && computeEan13CheckDigit(barcode.slice(0, 12)) === barcode[12];
}

/**
 * 与 Web 收银用户条码页一致：12 位随机数字 + EAN13 校验位；唯一性由后端条码占用表兜底。
 * 首位避开 0，避免部分扫码枪把 0 开头的 EAN13 当作 UPC-A 截掉。
 */
export function generateCashRegisterBarcode(random: () => number = Math.random) {
  const digits = Array.from({ length: 12 }, (_, index) =>
    String(index === 0 ? 1 + Math.floor(random() * 9) : Math.floor(random() * 10))
  ).join("");
  return digits + computeEan13CheckDigit(digits);
}

export type CashRegisterUserFormError =
  | "storeRequired"
  | "userRequired"
  | "barcodeRequired"
  | "barcodeLength"
  | "operatorTooLong"
  | "remarkTooLong";

/** 与 Web 表单规则一致：分店、关联用户、13 位条码必填，操作员 ≤100、备注 ≤500。 */
export function validateCashRegisterUserForm(values: CashRegisterUserFormValues): CashRegisterUserFormError | null {
  if (!values.storeCode.trim()) return "storeRequired";
  if (!values.userGuid.trim()) return "userRequired";
  const barcode = values.userBarcode.trim();
  if (!barcode) return "barcodeRequired";
  if (barcode.length !== 13) return "barcodeLength";
  if (values.operatorUser.trim().length > 100) return "operatorTooLong";
  if (values.remark.trim().length > 500) return "remarkTooLong";
  return null;
}

// EAN13 编码表：左半边按首位数字选择 L/G 奇偶组合，右半边统一用 R 码。
const EAN13_L = ["0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011"];
const EAN13_G = ["0100111", "0110011", "0011011", "0100001", "0011101", "0111001", "0000101", "0010001", "0001001", "0010111"];
const EAN13_R = ["1110010", "1100110", "1101100", "1000010", "1011100", "1001110", "1010000", "1000100", "1001000", "1110100"];
const EAN13_PARITY = ["LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"];

/** 返回 95 位模块串（1=黑条），非法 EAN13 返回 null，由界面降级为只显示数字。 */
export function encodeEan13Modules(value: string): string | null {
  const barcode = value.trim();
  if (!isValidEan13(barcode)) return null;
  const digits = barcode.split("").map(Number);
  const parity = EAN13_PARITY[digits[0]];
  const left = digits
    .slice(1, 7)
    .map((digit, index) => (parity[index] === "L" ? EAN13_L[digit] : EAN13_G[digit]))
    .join("");
  const right = digits.slice(7).map((digit) => EAN13_R[digit]).join("");
  return `101${left}01010${right}101`;
}

// Code128 条/空宽度表（值 0-105 的 6 段宽度，106 为 7 段终止符），与 JsBarcode 使用的标准表一致。
const CODE128_WIDTHS = [
  "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
  "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
  "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
  "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
  "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
  "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
  "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
  "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
  "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
  "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
  "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
];
const CODE128_START_B = 104;
const CODE128_START_C = 105;
const CODE128_CODE_A = 101;
const CODE128_STOP = 106;

function widthsToModules(widths: string) {
  return Array.from(widths)
    .map((width, index) => (index % 2 === 0 ? "1" : "0").repeat(Number(width)))
    .join("");
}

/**
 * 返回 Code128 模块串（1=黑条），用于非 EAN13 的历史收银条码屏幕预览；
 * 纯数字用 Code C 两位一组压缩；奇数位时与 JsBarcode 相同，末位切 Code A 编码，
 * 保证手机屏幕与 Web 显示的条纹一致；含不可编码字符返回 null。
 */
export function encodeCode128Modules(value: string): string | null {
  const text = value.trim();
  if (!text || !/^[\x20-\x7e]+$/.test(text)) return null;
  const codes: number[] = [];
  if (/^\d{2,}$/.test(text)) {
    codes.push(CODE128_START_C);
    const pairs = text.length - (text.length % 2);
    for (let index = 0; index < pairs; index += 2) codes.push(Number(text.slice(index, index + 2)));
    if (pairs < text.length) {
      // 数字在 Code A 中的值 = ASCII - 32，与 Code B 相同。
      codes.push(CODE128_CODE_A, text.charCodeAt(pairs) - 32);
    }
  } else {
    codes.push(CODE128_START_B, ...Array.from(text).map((char) => char.charCodeAt(0) - 32));
  }
  const checksum = codes.reduce((sum, code, index) => sum + code * (index === 0 ? 1 : index), 0) % 103;
  return [...codes, checksum, CODE128_STOP].map((code) => widthsToModules(CODE128_WIDTHS[code])).join("");
}

/** 与 Web resolveBarcodeFormat 一致：合法 EAN13 用 EAN13，否则 Code128。 */
export function encodeCashRegisterBarcodeModules(value: string) {
  return encodeEan13Modules(value) ?? encodeCode128Modules(value);
}
