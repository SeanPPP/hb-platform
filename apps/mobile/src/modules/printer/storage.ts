import { AppAsyncStorage } from "@/shared/storage/async-storage";
import type { SavedPrinter } from "@/modules/printer/types";

const LABEL_PRINTER_KEY = "hbweb_label_printer";
const RECEIPT_PRINTER_KEY = "hbweb_receipt_printer";

export const PrinterStorage = {
  async getPrinter() {
    return AppAsyncStorage.getObject<SavedPrinter>(LABEL_PRINTER_KEY);
  },

  async setPrinter(printer: SavedPrinter) {
    await AppAsyncStorage.setObject(LABEL_PRINTER_KEY, printer);
  },

  async clearPrinter() {
    await AppAsyncStorage.removeItem(LABEL_PRINTER_KEY);
  },

  // 标签打印机和小票打印机使用不同设备，必须分开保存，避免测试小票覆盖商品标签打印机。
  async getReceiptPrinter() {
    return AppAsyncStorage.getObject<SavedPrinter>(RECEIPT_PRINTER_KEY);
  },

  async setReceiptPrinter(printer: SavedPrinter) {
    await AppAsyncStorage.setObject(RECEIPT_PRINTER_KEY, printer);
  },

  async clearReceiptPrinter() {
    await AppAsyncStorage.removeItem(RECEIPT_PRINTER_KEY);
  },
};
