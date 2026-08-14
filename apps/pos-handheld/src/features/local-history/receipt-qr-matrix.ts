import qrGenerator from "qrcode-generator";

export type ReceiptQrMatrix = readonly (readonly boolean[])[];

/**
 * 小票二维码只负责把已生成的完整载荷转成可绘制矩阵，不在 UI 层截断或重写值。
 */
export function receiptQrMatrix(value: string): ReceiptQrMatrix {
  const qrCode = qrGenerator(0, "M");
  qrCode.addData(value);
  qrCode.make();
  const size = qrCode.getModuleCount();
  return Array.from({ length: size }, (_row, row) =>
    Array.from({ length: size }, (_column, column) =>
      qrCode.isDark(row, column),
    ),
  );
}
