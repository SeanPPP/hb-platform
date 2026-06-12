import { AppAsyncStorage } from "@/shared/storage/async-storage";
import type { SavedPrinter } from "@/modules/printer/types";

const LABEL_PRINTER_KEY = "hbweb_label_printer";

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
};
