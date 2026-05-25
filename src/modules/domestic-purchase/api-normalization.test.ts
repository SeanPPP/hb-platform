import {
  normalizeDomesticProduct,
  normalizeDomesticProductsListResponse,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const camelPayload = normalizeDomesticProductsListResponse({
  items: [
    {
      productCode: "CP-001",
      supplierCode: "SUP-1",
      supplierName: "Shanghai Supply",
      productName: "Green Tea",
      englishProductName: "Green Tea",
      hbProductNo: "HB-1001",
      barcode: "930000000001",
      productSpecification: "24 x 500ml",
      productType: "1",
      domesticPrice: "12.5",
      oemPrice: "13.75",
      importPrice: 14.25,
      packingQuantity: "24",
      unitVolume: "0.5",
      middlePackQuantity: "6",
      productImage: "https://example.com/a.png",
      isActive: true,
    },
  ],
  totalCount: "41",
  page: "3",
  pageSize: "15",
});

assertEqual(camelPayload.items.length, 1, "camel payload keeps items");
assertEqual(camelPayload.items[0]?.productCode, "CP-001", "camel product code");
assertEqual(camelPayload.items[0]?.productType, 1, "camel product type parses string");
assertEqual(camelPayload.items[0]?.domesticPrice, 12.5, "camel domestic price parses string");
assertEqual(camelPayload.items[0]?.oemPrice, 13.75, "camel oem price parses string");
assertEqual(camelPayload.items[0]?.importPrice, 14.25, "camel import price keeps number");
assertEqual(camelPayload.items[0]?.packingQuantity, 24, "camel packing quantity parses string");
assertEqual(camelPayload.items[0]?.unitVolume, 0.5, "camel unit volume parses string");
assertEqual(camelPayload.items[0]?.middlePackQuantity, 6, "camel middle pack quantity parses string");
assertEqual(camelPayload.items[0]?.isActive, true, "camel active flag keeps boolean");
assertEqual(camelPayload.total, 41, "camel total count parses");
assertEqual(camelPayload.page, 3, "camel page parses");
assertEqual(camelPayload.pageSize, 15, "camel page size parses");

const pascalPayload = normalizeDomesticProductsListResponse({
  Items: [
    {
      ProductCode: 2002,
      SupplierCode: "SUP-2",
      SupplierName: "Guangzhou Goods",
      ProductName: "Milk Candy",
      EnglishProductName: "Milk Candy",
      HBProductNo: "HB-2002",
      Barcode: "930000000002",
      ProductSpecification: "12 x 100g",
      ProductType: 2,
      DomesticPrice: "8.8",
      OEMPrice: "9.9",
      ImportPrice: "10.1",
      PackingQuantity: "12",
      UnitVolume: "1.25",
      MiddlePackQuantity: "4",
      ProductImage: "https://example.com/b.png",
      IsActive: 1,
    },
  ],
  Total: 88,
  Page: 2,
  PageSize: "25",
});

assertEqual(pascalPayload.items.length, 1, "pascal payload keeps items");
assertEqual(pascalPayload.items[0]?.productCode, "2002", "pascal numeric product code becomes string");
assertEqual(pascalPayload.items[0]?.productType, 2, "pascal product type keeps number");
assertEqual(pascalPayload.items[0]?.domesticPrice, 8.8, "pascal domestic price parses");
assertEqual(pascalPayload.items[0]?.oemPrice, 9.9, "pascal oem price parses");
assertEqual(pascalPayload.items[0]?.importPrice, 10.1, "pascal import price parses");
assertEqual(pascalPayload.items[0]?.isActive, true, "pascal active flag normalizes truthy number");
assertEqual(pascalPayload.total, 88, "pascal total parses");
assertEqual(pascalPayload.page, 2, "pascal page parses");
assertEqual(pascalPayload.pageSize, 25, "pascal page size parses");

const directProduct = normalizeDomesticProduct({
  ProductCode: "CP-404",
  SupplierCode: "SUP-4",
  ProductName: "Black Sesame",
  DomesticPrice: "",
  OEMPrice: null,
  ImportPrice: undefined,
  PackingQuantity: "0",
  UnitVolume: "0",
  MiddlePackQuantity: null,
  IsActive: "false",
});

assertEqual(directProduct.productCode, "CP-404", "single product normalizes code");
assertEqual(directProduct.supplierCode, "SUP-4", "single product normalizes supplier code");
assertEqual(directProduct.productName, "Black Sesame", "single product normalizes product name");
assertEqual(directProduct.domesticPrice, null, "empty domestic price becomes null");
assertEqual(directProduct.oemPrice, null, "null oem price stays null");
assertEqual(directProduct.importPrice, null, "missing import price becomes null");
assertEqual(directProduct.packingQuantity, 0, "zero packing quantity parses");
assertEqual(directProduct.unitVolume, 0, "zero unit volume parses");
assertEqual(directProduct.middlePackQuantity, null, "null middle pack quantity stays null");
assertEqual(directProduct.isActive, false, "false string becomes false");

const emptyPayload = normalizeDomesticProductsListResponse({});

assertEqual(emptyPayload.items.length, 0, "missing items falls back to empty list");
assertEqual(emptyPayload.total, 0, "missing total falls back to zero");
assertEqual(emptyPayload.page, 1, "missing page falls back to first page");
assertEqual(emptyPayload.pageSize, 20, "missing page size falls back to default page size");
