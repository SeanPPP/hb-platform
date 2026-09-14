import type { LocalSupplierOption } from "./types";

export function searchCreateSuppliers(
  suppliers: LocalSupplierOption[],
  query: string,
  language: string,
) {
  const term = query.trim().toLocaleLowerCase();
  return suppliers
    .filter(
      (supplier) =>
        !term ||
        `${supplier.supplierName}\n${supplier.supplierCode}`
          .toLocaleLowerCase()
          .includes(term),
    )
    .sort(
      (a, b) =>
        (a.supplierName || a.supplierCode).localeCompare(
          b.supplierName || b.supplierCode,
          language === "zh" ? "zh-CN" : "en",
          { numeric: true, sensitivity: "base" },
        ) || a.supplierCode.localeCompare(b.supplierCode),
    );
}
