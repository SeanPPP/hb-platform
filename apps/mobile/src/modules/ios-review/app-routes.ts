import {
  IOS_REVIEW_PERMISSION_CODES,
  IOS_REVIEW_STORES,
  createIosReviewUser,
} from "./identity";
import { IOS_REVIEW_SAMPLE_BARCODE } from "./helpers";
import { IOS_REVIEW_MENU_ITEMS } from "./menu";
import type { IosReviewDomainName, ReviewDataStore } from "./data-store";
import {
  type ReviewTransport,
  type ReviewTransportRequest,
  type ReviewTransportResult,
} from "./transport";

type ReviewMethod = ReviewTransportRequest["method"];
type JsonRecord = Record<string, any>;
type RouteHandler = (
  request: ReviewTransportRequest,
) => ReviewTransportResult | Promise<ReviewTransportResult>;

interface AppRouteState {
  now: string;
  today: string;
  products: JsonRecord[];
  productDetails: Map<string, JsonRecord>;
  carts: Map<string, JsonRecord>;
  orders: JsonRecord[];
  locations: JsonRecord[];
  containers: JsonRecord[];
  containerDetails: JsonRecord[];
  domesticBatches: JsonRecord[];
  domesticProducts: JsonRecord[];
  invoices: JsonRecord[];
  invoiceDetails: JsonRecord[];
  advertisements: JsonRecord[];
  promotions: JsonRecord[];
  installmentOrders: JsonRecord[];
  vouchers: JsonRecord[];
  seasonalCatalog: JsonRecord[];
  seasonalSubmissions: JsonRecord[];
  punches: JsonRecord[];
  availability: JsonRecord[];
  leaveRequests: JsonRecord[];
  approvals: JsonRecord[];
  schedules: JsonRecord[];
  holidays: JsonRecord[];
  users: JsonRecord[];
  posPermissionCodes: string[];
  employeeProfile: JsonRecord;
  cashierBarcode: JsonRecord;
  devices: JsonRecord[];
  appDevices: JsonRecord[];
  mirrorIds: Map<string, string>;
  sequence: number;
}

interface AppRouteStateHolder {
  current: AppRouteState;
}

const stateHolders = new WeakMap<ReviewDataStore, AppRouteStateHolder>();

function clone<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => clone(item)) as T;
  }
  if (value && typeof value === "object" && !(value instanceof Map)) {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>).map(([key, item]) => [
        key,
        clone(item),
      ]),
    ) as T;
  }
  return value;
}

function toDateOnly(date: Date) {
  return date.toISOString().slice(0, 10);
}

function resolveReviewStoreTimeZone(storeCode: unknown) {
  const normalizedStoreCode = String(storeCode ?? "")
    .trim()
    .toUpperCase();
  const stateCode = String(
    IOS_REVIEW_STORES.find(
      (store) => store.storeCode.toUpperCase() === normalizedStoreCode,
    )?.stateCode ?? "",
  ).toUpperCase();
  if (stateCode === "QLD") return "Australia/Brisbane";
  if (stateCode === "VIC") return "Australia/Melbourne";
  // NSW 及无法识别的门店都沿用 Central Backend 的 Sydney 回退规则。
  return "Australia/Sydney";
}

function toStoreLocalDateOnly(value: unknown, storeCode: unknown) {
  const instant = new Date(String(value ?? ""));
  if (!Number.isFinite(instant.getTime())) return "";
  const parts = new Intl.DateTimeFormat("en-AU", {
    timeZone: resolveReviewStoreTimeZone(storeCode),
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(instant);
  const byType = Object.fromEntries(
    parts.map((part) => [part.type, part.value]),
  );
  return `${byType.year}-${byType.month}-${byType.day}`;
}

function createInitialState(now = new Date()): AppRouteState {
  const timestamp = now.toISOString();
  const today = toDateOnly(now);
  const products: JsonRecord[] = [
    {
      productCode: "REV-PROD-001",
      itemNumber: "REV-MUG-001",
      barcode: IOS_REVIEW_SAMPLE_BARCODE,
      productName: "Demo Ceramic Mug",
      productImage:
        "data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%3E%3C/svg%3E",
      categoryName: "Homeware",
      warehouseCategoryGUID: "review-category-homeware",
      grade: "A",
      oemPrice: 12.95,
      retailPrice: 12.95,
      purchasePrice: 5.2,
      domesticPrice: 5.2,
      importPrice: 5.2,
      stockQuantity: 48,
      minOrderQuantity: 1,
      middlePackageQuantity: 6,
      packingQuantity: 24,
      packQty: 24,
      volume: 0.002,
      isInStock: true,
      isActive: true,
      warehouseIsActive: true,
      localSupplierCode: "REV-SUP-001",
      supplierCode: "REV-SUP-001",
      supplierName: "Demo Local Supplier",
      locationGuid: "review-location-001",
      locationCode: "A-01-01",
      locationBarcode: "REVLOC000001",
      updatedAt: timestamp,
    },
    {
      productCode: "REV-PROD-002",
      itemNumber: "REV-TOWEL-002",
      barcode: "9330000000024",
      productName: "Demo Cotton Towel",
      categoryName: "Homeware",
      warehouseCategoryGUID: "review-category-homeware",
      grade: "A",
      oemPrice: 8.5,
      retailPrice: 8.5,
      purchasePrice: 3.4,
      importPrice: 3.4,
      stockQuantity: 72,
      minOrderQuantity: 1,
      middlePackageQuantity: 12,
      packingQuantity: 48,
      packQty: 48,
      volume: 0.001,
      isInStock: true,
      isActive: true,
      warehouseIsActive: true,
      localSupplierCode: "REV-SUP-001",
      supplierCode: "REV-SUP-001",
      supplierName: "Demo Local Supplier",
      locationGuid: "review-location-001",
      locationCode: "A-01-01",
      locationBarcode: "REVLOC000001",
      updatedAt: timestamp,
    },
  ];
  const firstCartLine = createCartLine(products[0], 2, timestamp);
  const carts = new Map<string, JsonRecord>([
    ["REV001", createCart("REV001", [firstCartLine], timestamp)],
    ["REV002", createCart("REV002", [], timestamp)],
    ["REVWH", createCart("REVWH", [], timestamp)],
  ]);
  const orders = [
    {
      orderGUID: "review-order-001",
      orderNo: "REV-ORDER-0001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      orderDate: timestamp,
      flowStatus: 2,
      totalAmount: firstCartLine.amount,
      oemTotalAmount: firstCartLine.amount,
      importTotalAmount: firstCartLine.importAmount,
      totalOrderAmount: firstCartLine.amount,
      totalQuantity: firstCartLine.quantity,
      totalAllocQuantity: firstCartLine.quantity,
      totalOrderVolume: firstCartLine.totalVolume,
      totalAllocVolume: firstCartLine.totalVolume,
      remarks: "Completed offline demo order",
      items: [clone(firstCartLine)],
      createdAt: timestamp,
      updatedAt: timestamp,
    },
  ];
  const locations = [
    {
      locationGuid: "review-location-001",
      locationCode: "A-01-01",
      locationBarcode: "REVLOC000001",
      status: 1,
      locationType: 1,
      productCount: 1,
      updatedAt: timestamp,
      updatedBy: "App Review Demo",
      products: [
        {
          productCode: products[0].productCode,
          itemNumber: products[0].itemNumber,
          productName: products[0].productName,
          productImage: products[0].productImage,
          middlePackageQuantity: products[0].middlePackageQuantity,
        },
      ],
    },
  ];
  const containers = [
    {
      hguid: "review-container",
      HGUID: "review-container",
      柜号: "REV-CN-001",
      containerNumber: "REV-CN-001",
      预计到岸日期: today,
      状态: "Draft",
      totalPieces: 120,
      totalAmount: 624,
      totalVolume: 18.5,
    },
  ];
  const containerDetails = [
    {
      hguid: "review-container-detail-001",
      HGUID: "review-container-detail-001",
      商品编码: products[0].productCode,
      商品名称: products[0].productName,
      英文名称: "Demo Ceramic Mug",
      商品图片: products[0].productImage,
      贴牌价格: products[0].oemPrice,
      国内价格: products[0].domesticPrice,
      进口价格: products[0].importPrice,
      装柜数量: 120,
      合计装柜体积: 18.5,
      是否新商品: false,
      商品信息: {
        商品编码: products[0].productCode,
        货号: products[0].itemNumber,
        条形码: products[0].barcode,
        商品名称: products[0].productName,
        英文名称: "Demo Ceramic Mug",
        商品图片: products[0].productImage,
        localSupplierCode: "REV-SUP-001",
      },
      matchType: "productCode",
      localProductCode: products[0].productCode,
      domesticProductCode: products[0].productCode,
    },
  ];
  const domesticProducts = products.map((product) => ({
    productCode: product.productCode,
    supplierCode: "REV-CN-SUP",
    supplierName: "Demo China Supplier",
    productName: product.productName,
    englishProductName: product.productName,
    hbProductNo: product.itemNumber,
    barcode: product.barcode,
    productSpecification: "Demo specification",
    productType: 1,
    domesticPrice: product.domesticPrice,
    oemPrice: product.oemPrice,
    importPrice: product.importPrice,
    packingQuantity: product.packingQuantity,
    unitVolume: product.volume,
    middlePackQuantity: product.middlePackageQuantity,
    productImage: product.productImage,
    isActive: true,
  }));
  const domesticBatches = [
    {
      batchNumber: "REV-BATCH-001",
      supplierCode: "REV-CN-SUP",
      supplierName: "Demo China Supplier",
      prefixCode: "REV",
      normalProductCount: 2,
      setProductCount: 0,
      totalCount: 2,
      createdTime: timestamp,
      createdBy: "App Review Demo",
      items: domesticProducts.map((item) => ({
        ...item,
        itemNumber: item.hbProductNo,
        privateLabelPrice: item.oemPrice,
      })),
    },
  ];
  const invoices = [
    {
      invoiceGuid: "review-invoice-001",
      invoiceNo: "REV-INV-0001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      supplierCode: "REV-SUP-001",
      supplierName: "Demo Local Supplier",
      orderDate: today,
      inboundDate: today,
      flowStatus: 2,
      inboundStatus: 1,
      totalAmount: 320.5,
      receivedTotalAmount: 320.5,
      priceIncreaseItemCount: 1,
      priceDecreaseItemCount: 0,
      remarks: "Synthetic local supplier invoice",
      createdAt: timestamp,
      updatedAt: timestamp,
    },
  ];
  const invoiceDetails = products.map((product, index) => ({
    detailGuid: `review-invoice-line-${index + 1}`,
    invoiceGuid: "review-invoice-001",
    storeCode: "REV001",
    supplierCode: "REV-SUP-001",
    productCode: product.productCode,
    storeProductCode: product.productCode,
    itemNumber: product.itemNumber,
    barcode: product.barcode,
    productName: product.productName,
    quantity: index + 2,
    purchasePrice: product.purchasePrice,
    lastPurchasePrice: product.purchasePrice,
    retailPrice: product.retailPrice,
    amount: product.purchasePrice * (index + 2),
    productImage: product.productImage,
  }));
  const advertisements = [
    {
      id: "review-ad-001",
      title: "Demo Weekend Specials",
      description: "Synthetic banner for App Review",
      mediaType: "Image",
      mediaUrl: products[0].productImage,
      thumbnailUrl: products[0].productImage,
      objectKey: "ios-review/advertisements/review-ad-001.svg",
      originalFileName: "demo-weekend-specials.svg",
      contentType: "image/svg+xml",
      fileSize: 256,
      effectiveStart: timestamp,
      effectiveEnd: new Date(now.getTime() + 7 * 86400000).toISOString(),
      isEnabled: true,
      sortOrder: 1,
      stores: [{ storeCode: "REV001" }],
    },
  ];
  const promotions = [
    {
      id: "review-promotion-001",
      name: "Demo Mug Bundle",
      description: "Buy two demo mugs for a fixed price",
      effectiveStart: timestamp,
      effectiveEnd: new Date(now.getTime() + 7 * 86400000).toISOString(),
      isEnabled: true,
      isExclusive: true,
      priority: 10,
      applyQuantity: 2,
      fixedPrice: 20,
      maxApplicationsPerOrder: 2,
      products: [{ productCode: products[0].productCode, unitWeight: 1 }],
      stores: [{ storeCode: "REV001" }],
      productsCount: 1,
      storesCount: 1,
      scopeType: "StoreOnly",
      canEditInStoreScope: true,
      canCopyToStore: true,
    },
  ];
  const installmentOrders = [
    {
      installmentGuid: "review-installment-001",
      installmentNumber: "REV-INS-0001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      deviceCode: "REV-IOS-01",
      cashierName: "App Review Demo",
      customerPhone: "0400000001",
      customerName: "Demo Customer",
      createdAt: timestamp,
      totalAmount: 120,
      minimumDownPayment: 24,
      downPaymentAmount: 40,
      paidAmount: 120,
      balanceAmount: 0,
      status: 3,
      updatedAt: timestamp,
      note: "Review customer pickup completed",
      pickupInfo: {
        pickedUpAt: timestamp,
        pickedUpBy: "App Review Demo",
        pickupNote: "Identity confirmed at pickup",
      },
      cancellationInfo: null,
      lines: [
        {
          installmentLineGuid: "review-installment-line-001",
          productCode: products[0].productCode,
          referenceCode: products[0].barcode,
          displayName: products[0].productName,
          lookupCode: products[0].barcode,
          quantity: 1.25,
          unitPrice: 100,
          discountAmount: 5,
          actualAmount: 120,
          itemNumber: "REV-ITEM-001",
        },
      ],
      payments: [
        {
          paymentGuid: "review-payment-001",
          method: 1,
          amount: 40,
          reference: "REV-PAY-001",
          status: 1,
          recordedAt: timestamp,
          cashierId: "review-user",
          deviceCode: "REV-IOS-01",
        },
        {
          paymentGuid: "review-payment-voided-001",
          method: 2,
          amount: 10,
          reference: "REV-PAY-VOIDED-001",
          status: 2,
          recordedAt: timestamp,
          cashierId: "review-user",
          deviceCode: "REV-IOS-01",
        },
        {
          paymentGuid: "review-payment-002",
          method: 1,
          amount: 80,
          reference: "REV-PAY-002",
          status: 1,
          recordedAt: timestamp,
          cashierId: "review-user",
          deviceCode: "REV-IOS-01",
        },
      ],
    },
    {
      installmentGuid: "review-installment-002",
      installmentNumber: "REV-INS-0002",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      deviceCode: "REV-IOS-02",
      cashierName: "App Review Demo",
      customerPhone: "0400000002",
      customerName: "Demo Customer Two",
      // UTC 仍在前一天，但 Brisbane 已进入 7 月 16 日，用于锁定门店本地日期边界。
      createdAt: "2026-07-15T23:30:00.000Z",
      totalAmount: 60,
      minimumDownPayment: 12,
      downPaymentAmount: 20,
      paidAmount: 60,
      balanceAmount: 0,
      status: 3,
      updatedAt: "2026-07-16T00:00:00.000Z",
      note: "Review timezone boundary fixture",
      pickupInfo: null,
      cancellationInfo: null,
      lines: [],
      payments: [],
    },
  ];
  const vouchers = [
    {
      id: "review-voucher-001",
      voucherCode: "REV-VOUCHER-0001",
      voucherType: 1,
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      customerCode: "REV-CUSTOMER-001",
      customerName: "Demo Customer",
      amount: 50,
      remainingAmount: 35,
      status: "Active",
      createTime: timestamp,
      updateTime: timestamp,
      expiredDate: new Date(now.getTime() + 30 * 86400000).toISOString(),
      createUser: "App Review Demo",
      remark: "Synthetic voucher",
      ledger: [
        {
          id: "review-ledger-001",
          voucherCode: "REV-VOUCHER-0001",
          action: "issued",
          amount: 50,
          remainingAmount: 50,
          actionTime: timestamp,
          operatorName: "App Review Demo",
        },
      ],
      relatedOrders: [
        {
          orderGuid: orders[0].orderGUID,
          orderNo: orders[0].orderNo,
          storeCode: "REV001",
          amount: 15,
          orderTime: timestamp,
        },
      ],
    },
  ];
  const seasonalCatalog = [
    {
      catalogGuid: "review-seasonal-catalog-001",
      cardType: 1,
      cardTypeName: "Christmas Card",
      priceOption: 1,
      priceOptionName: "Fixed price",
      priceLabel: "$2.00",
      fixedUnitPrice: 2,
      allowsCustomUnitPrice: false,
      isEnabled: true,
      sortOrder: 1,
    },
  ];
  const seasonalSubmissions = [
    {
      submissionGuid: "review-seasonal-submission-001",
      storeCode: "REV001",
      catalogGuid: "review-seasonal-catalog-001",
      cardType: 1,
      cardTypeName: "Christmas Card",
      seasonYear: now.getUTCFullYear(),
      unitPrice: 2,
      priceLabel: "$2.00",
      remainingQuantity: 36,
      remark: "Synthetic stock count",
      submittedByName: "App Review Demo",
      submittedAt: timestamp,
    },
  ];
  const schedules = [
    {
      scheduleGuid: "review-schedule-001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      userGuid: "review-user",
      employeeName: "App Review Demo",
      workDate: today,
      startTime: "09:00",
      endTime: "17:00",
      status: "Active",
      remark: "Offline demo shift",
      isMine: true,
    },
  ];
  const availability = [
    {
      availabilityGuid: "review-availability-001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      workDate: today,
      startTime: "08:00",
      endTime: "18:00",
      note: "Available",
      status: "Submitted",
    },
  ];
  const leaveRequests = [
    {
      leaveGuid: "review-leave-001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      leaveType: "AnnualLeave",
      startDate: today,
      endDate: today,
      reason: "Synthetic leave request",
      status: "Pending",
      submittedAt: timestamp,
    },
  ];
  const approvals = [
    {
      approvalGuid: "review-approval-001",
      sourceGuid: "review-leave-001",
      sourceType: "Leave",
      employeeName: "Demo Staff",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      workDate: today,
      title: "Annual leave request",
      detail: "Synthetic approval",
      status: "Pending",
      submittedAt: timestamp,
    },
  ];
  const holidays = [
    {
      holidayGuid: "review-holiday-001",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      holidayDate: today,
      holidayName: "Demo Public Holiday",
      businessStatus: "Open",
      openTime: "10:00",
      closeTime: "16:00",
      isPaidHoliday: true,
      remark: "Synthetic holiday",
    },
  ];
  const users = [
    {
      userGuid: "review-staff-001",
      userGUID: "review-staff-001",
      username: "demo_staff",
      fullName: "Demo Staff",
      displayName: "Demo Staff",
      email: "demo-staff@example.invalid",
      phone: "0400000002",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      roleNames: ["StoreStaff"],
      accessStoreAssignments: [
        {
          storeGUID: IOS_REVIEW_STORES[0].storeGUID,
          isPrimary: false,
        },
      ],
      accessRoleGuids: ["review-role-store-staff"],
      directPermissionCodes: ["PosTerminal.Sales.AddItem"],
      status: "Active",
      isActive: true,
      createdAt: timestamp,
      updatedAt: timestamp,
    },
  ];
  const employeeProfile = {
    username: "ios_app_review",
    displayName: "App Review Demo",
    bankBsb: "000-000",
    bankAccountNumber: "00000000",
    superannuationCompanyName: "Demo Super",
    superannuationCompanyCode: "DEMO",
    superannuationAccountNumber: "DEMO-0001",
    birthday: "1990-01-01",
    gender: "Not specified",
    employmentType: "Demo",
    avatarUrl: products[0].productImage,
    identityId: "DEMO-ID-0001",
    identityPhotoUrl: products[0].productImage,
    identityPhotoUrlExpiresAt: new Date(now.getTime() + 86400000).toISOString(),
    address: "123 Demo Street, Brisbane QLD 4000",
    createdAt: timestamp,
    updatedAt: timestamp,
  };
  const cashierBarcode = {
    exists: true,
    barcode: IOS_REVIEW_SAMPLE_BARCODE,
    format: "EAN13",
    printCount: 0,
    createdAt: timestamp,
    updatedAt: timestamp,
  };
  const devices = [
    {
      id: "review-device-001",
      hardwareId: "REV-IOS-HARDWARE-001",
      systemDeviceNumber: "REV-SYS-001",
      deviceNumber: "REV-DEVICE-001",
      deviceType: "Mobile",
      deviceSystem: "iOS",
      deviceName: "App Review iPhone",
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      status: 1,
      appVersion: "1.0.2",
      platform: "ios",
      lastSeenAt: timestamp,
      createdAt: timestamp,
      updatedAt: timestamp,
    },
  ];
  const appDevices = [
    {
      id: "review-app-device-001",
      hardwareId: "REV-IOS-HARDWARE-001",
      systemDeviceNumber: "REV-SYS-001",
      deviceSystem: "iOS",
      platform: "ios",
      storeCode: "REV001",
      appVersion: "1.0.2",
      appBuildVersion: "review",
      runtimeVersion: "1.0.2",
      channel: "production",
      updateSource: "embedded",
      lastSeenAtUtc: timestamp,
      isOnline: true,
      lastAuthMode: "iosReview",
      lastSeenUserGuid: "review-user",
      lastSeenUsername: "ios_app_review",
      lastSeenUserFullName: "App Review Demo",
    },
  ];
  const productDetails = new Map(
    products.map((product) => [
      String(product.productCode),
      createProductDetailFixture(product),
    ]),
  );

  return {
    now: timestamp,
    today,
    products,
    productDetails,
    carts,
    orders,
    locations,
    containers,
    containerDetails,
    domesticBatches,
    domesticProducts,
    invoices,
    invoiceDetails,
    advertisements,
    promotions,
    installmentOrders,
    vouchers,
    seasonalCatalog,
    seasonalSubmissions,
    punches: [],
    availability,
    leaveRequests,
    approvals,
    schedules,
    holidays,
    users,
    posPermissionCodes: [...IOS_REVIEW_PERMISSION_CODES],
    employeeProfile,
    cashierBarcode,
    devices,
    appDevices,
    mirrorIds: new Map(),
    sequence: 100,
  };
}

function createCartLine(
  product: JsonRecord,
  quantity: number,
  timestamp: string,
) {
  return {
    detailGUID: `review-cart-line-${product.productCode}`,
    productCode: product.productCode,
    itemNumber: product.itemNumber,
    barcode: product.barcode,
    grade: product.grade,
    productName: product.productName,
    productImage: product.productImage,
    price: product.oemPrice ?? product.retailPrice ?? 0,
    quantity,
    allocQuantity: quantity,
    amount: (product.oemPrice ?? product.retailPrice ?? 0) * quantity,
    importPrice: product.importPrice ?? product.purchasePrice ?? 0,
    importAmount:
      (product.importPrice ?? product.purchasePrice ?? 0) * quantity,
    volume: product.volume ?? 0,
    totalVolume: (product.volume ?? 0) * quantity,
    minOrderQuantity: product.minOrderQuantity ?? 1,
    isActive: true,
    locationCode: product.locationCode,
    rrp: product.oemPrice ?? product.retailPrice,
    updatedAt: timestamp,
  };
}

function createProductDetailFixture(product: JsonRecord): JsonRecord {
  const productCode = String(product.productCode);
  return {
    ...clone(product),
    productType: 1,
    productTypeLabel: "Standard",
    localSupplierCode: product.localSupplierCode,
    localSupplierName: product.supplierName,
    storePrice: {
      uuid: `review-store-price-${productCode}`,
      storeCode: "REV001",
      storeName: "Demo Brisbane",
      productCode,
      storeProductCode: productCode,
      supplierCode: product.supplierCode,
      purchasePrice: product.purchasePrice,
      retailPrice: product.retailPrice,
      discountRate: 0,
      isAutoPricing: false,
      isSpecialProduct: false,
      isActive: true,
      rate: 1,
    },
    clearancePrice: null,
    setCodes: [
      {
        setCodeId: `review-set-${productCode}`,
        productCode,
        setProductCode: productCode,
        setItemNumber: product.itemNumber,
        setBarcode: product.barcode,
        setPurchasePrice: product.purchasePrice,
        setRetailPrice: product.retailPrice,
        setQuantity: 1,
        setType: 1,
        setTypeDescription: "Standard",
        isActive: true,
      },
    ],
    multiCodes: [
      {
        uuid:
          productCode === "REV-PROD-001"
            ? "review-multi-001"
            : `review-multi-${productCode}`,
        setCodeId: "",
        storeCode: "REV001",
        productCode,
        multiCodeProductCode: productCode,
        storeMultiCodeProductCode: productCode,
        barcode: product.barcode,
        purchasePrice: product.purchasePrice,
        retailPrice: product.retailPrice,
        discountRate: 0,
        isAutoPricing: false,
        isSpecialProduct: false,
        isActive: true,
        rate: 1,
      },
    ],
    setCodeCount: 1,
    multiCodeCount: 1,
    codesIncluded: true,
  };
}

function createCart(
  storeCode: string,
  items: JsonRecord[],
  timestamp: string,
): JsonRecord {
  return summarizeCart({
    orderGUID: `review-cart-${storeCode}`,
    orderNo: `REV-CART-${storeCode}`,
    storeCode,
    storeName:
      IOS_REVIEW_STORES.find((store) => store.storeCode === storeCode)
        ?.storeName ?? storeCode,
    remarks: "Offline review cart",
    orderDate: timestamp,
    flowStatus: 0,
    items,
  });
}

function summarizeCart(cart: JsonRecord): JsonRecord {
  const items = Array.isArray(cart.items) ? cart.items : [];
  return {
    ...cart,
    totalAmount: items.reduce(
      (sum: number, item: JsonRecord) => sum + Number(item.amount ?? 0),
      0,
    ),
    totalQuantity: items.reduce(
      (sum: number, item: JsonRecord) => sum + Number(item.quantity ?? 0),
      0,
    ),
    totalImportAmount: items.reduce(
      (sum: number, item: JsonRecord) => sum + Number(item.importAmount ?? 0),
      0,
    ),
    totalSku: new Set(items.map((item: JsonRecord) => item.productCode)).size,
    totalVolume: items.reduce(
      (sum: number, item: JsonRecord) => sum + Number(item.totalVolume ?? 0),
      0,
    ),
  };
}

function asRecord(value: unknown): JsonRecord {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as JsonRecord)
    : {};
}

function nextId(state: AppRouteState, prefix: string) {
  state.sequence += 1;
  return `${prefix}-${state.sequence}`;
}

function page(items: JsonRecord[], pageNumber = 1, pageSize = 20) {
  return {
    items: clone(items),
    total: items.length,
    totalCount: items.length,
    page: pageNumber,
    pageNumber,
    pageSize,
  };
}

function pagedSlice(items: JsonRecord[], pageNumber: number, pageSize: number) {
  const safePageNumber =
    Number.isFinite(pageNumber) && pageNumber > 0 ? Math.trunc(pageNumber) : 1;
  const safePageSize =
    Number.isFinite(pageSize) && pageSize > 0 ? Math.trunc(pageSize) : 20;
  const start = (safePageNumber - 1) * safePageSize;
  const result = page(
    items.slice(start, start + safePageSize),
    safePageNumber,
    safePageSize,
  );
  return {
    ...result,
    total: items.length,
    totalCount: items.length,
  };
}

function findByAnyId(items: JsonRecord[], id: string) {
  return items.find((item) =>
    [
      item.id,
      item.orderGuid,
      item.orderGUID,
      item.voucherCode,
      item.submissionGuid,
      item.invoiceGuid,
      item.batchNumber,
      item.locationGuid,
      item.userGuid,
      item.userGUID,
      item.scheduleGuid,
      item.holidayGuid,
    ].some((value) => String(value ?? "") === id),
  );
}

function register(
  transport: ReviewTransport,
  methods: readonly ReviewMethod[],
  path: string | RegExp,
  handle: RouteHandler,
) {
  methods.forEach((method) => transport.register({ method, path, handle }));
}

function getHolder(dataStore: ReviewDataStore) {
  let holder = stateHolders.get(dataStore);
  if (!holder) {
    holder = { current: createInitialState(dataStore.getNow()) };
    stateHolders.set(dataStore, holder);
  }
  return holder;
}

function mirrorCreate(
  state: AppRouteState,
  dataStore: ReviewDataStore,
  domain: IosReviewDomainName,
  key: string,
  label: string,
  status = "active",
) {
  const entity = dataStore.create(domain, { label, status });
  state.mirrorIds.set(key, entity.id);
}

function mirrorUpdate(
  state: AppRouteState,
  dataStore: ReviewDataStore,
  domain: IosReviewDomainName,
  key: string,
  label: string,
  status = "updated",
) {
  const mirrorId = state.mirrorIds.get(key) ?? dataStore.list(domain)[0]?.id;
  if (!mirrorId) {
    mirrorCreate(state, dataStore, domain, key, label, status);
    return;
  }
  dataStore.update(domain, mirrorId, { label, status });
  state.mirrorIds.set(key, mirrorId);
}

function mirrorRemove(
  state: AppRouteState,
  dataStore: ReviewDataStore,
  domain: IosReviewDomainName,
  key: string,
) {
  const mirrorId = state.mirrorIds.get(key) ?? dataStore.list(domain)[0]?.id;
  if (mirrorId) dataStore.remove(domain, mirrorId);
  state.mirrorIds.delete(key);
}

function binaryExport() {
  // 以最小 ZIP 文件头模拟 xlsx/pdf 二进制，避免审核模式触发任何真实导出请求。
  return new Uint8Array([0x50, 0x4b, 0x03, 0x04, 0x52, 0x45, 0x56]).buffer;
}

/**
 * 重置所有实际 App endpoint 使用的内存 fixture。
 * 登出时应与审核会话标记和 React Query 缓存一并清理。
 */
export function resetIosReviewAppRouteState(dataStore: ReviewDataStore) {
  dataStore.reset();
  const holder = getHolder(dataStore);
  holder.current = createInitialState(dataStore.getNow());
}

/**
 * 注册 App 实际使用的审核模式端点。
 * 仅登记已审计路径；未知请求继续由 transport 统一 fail-closed。
 */
export function registerIosReviewAppRoutes(
  transport: ReviewTransport,
  dataStore: ReviewDataStore,
) {
  const holder = getHolder(dataStore);
  const state = () => holder.current;

  register(transport, ["GET"], "/navigation/app-menu", () => ({
    data: IOS_REVIEW_MENU_ITEMS.map((item) => ({ ...item })),
  }));
  register(transport, ["GET"], "/auth/current", () => ({
    data: createIosReviewUser(),
  }));
  register(transport, ["POST"], "/auth/logout", () => ({
    data: { success: true },
  }));
  register(
    transport,
    ["GET", "POST"],
    /^\/Users\/guid\/([^/]+)\/stores$/i,
    ({ method, match, body }) => {
      const userGuid = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, userGuid);
      if (!user) {
        return { data: IOS_REVIEW_STORES.map((store) => ({ ...store })) };
      }

      if (method === "POST") {
        user.accessStoreAssignments = Array.isArray(body)
          ? body.map((item) => {
              const assignment = asRecord(item);
              return {
                storeGUID: String(
                  assignment.StoreGUID ?? assignment.storeGUID ?? "",
                ),
                isPrimary: Boolean(
                  assignment.IsPrimary ?? assignment.isPrimary,
                ),
              };
            })
          : [];
        mirrorUpdate(
          state(),
          dataStore,
          "users",
          userGuid,
          "User store access",
        );
        return { data: true };
      }

      const assignments = Array.isArray(user.accessStoreAssignments)
        ? user.accessStoreAssignments.map(asRecord)
        : [];
      return {
        data: assignments.flatMap((assignment) => {
          const store = IOS_REVIEW_STORES.find(
            (item) => item.storeGUID === assignment.storeGUID,
          );
          return store
            ? [{ ...store, isPrimary: Boolean(assignment.isPrimary) }]
            : [];
        }),
      };
    },
  );
  register(transport, ["GET"], "/stores/all-by-name", () => ({
    data: IOS_REVIEW_STORES.map((store) => ({ ...store })),
  }));
  register(transport, ["GET"], "/Roles/active", () => ({
    data: [
      {
        roleGUID: "review-role-store-staff",
        roleName: "StoreStaff",
        description: "Standard review store account",
        isActive: true,
      },
      {
        roleGUID: "review-role-store-manager",
        roleName: "StoreManager",
        description: "Derived from managed stores",
        isActive: true,
      },
    ],
  }));
  register(transport, ["GET"], "/Roles/permissions", () => ({
    data: [
      {
        category: "Users",
        displayName: "User management",
        description: "Synthetic account access permissions",
        permissions: [
          {
            name: "Users.View",
            displayName: "View users",
            description: "View staff accounts",
            category: "Users",
            isSystemPermission: true,
          },
        ],
      },
      {
        category: "PosTerminal",
        displayName: "POS terminal",
        description: "Synthetic account default POS permissions",
        permissions: [
          {
            name: "PosTerminal.Sales.AddItem",
            displayName: "Add sale item",
            description: "Account default before store override",
            category: "PosTerminal",
            isSystemPermission: true,
          },
        ],
      },
    ],
  }));
  register(transport, ["GET"], "/react/v1/warehouse-categories/tree", () => ({
    data: [
      {
        categoryGUID: "review-category-homeware",
        categoryName: "Homeware",
        children: [],
      },
    ],
  }));
  register(transport, ["GET"], "/react/v1/product-grades/options", () => ({
    data: { items: [{ grade: "A" }, { grade: "B" }] },
  }));
  register(transport, ["GET"], "/react/v1/local-suppliers/active", () => ({
    data: [
      {
        supplierCode: "REV-SUP-001",
        supplierName: "Demo Local Supplier",
      },
    ],
  }));

  register(transport, ["POST"], "/react/v1/store-order/products", () => ({
    data: page(state().products, 1, 24),
  }));
  register(
    transport,
    ["POST"],
    "/react/v1/store-order/dynamic-data",
    ({ body }) => {
      const payload = asRecord(body);
      const storeCode = String(payload.storeCode ?? "REV001");
      const cart = state().carts.get(storeCode);
      const codes = Array.isArray(payload.productCodes)
        ? payload.productCodes
        : state().products.map((product) => product.productCode);
      return {
        data: codes.map((productCode: string) => ({
          productCode,
          lastOrderDate: state().today,
          lastQuantity: 2,
          lastAllocQuantity: 2,
          cartQuantity:
            cart?.items?.find(
              (item: JsonRecord) => item.productCode === productCode,
            )?.quantity ?? 0,
        })),
      };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/store-order/products/scan-lookup",
    ({ body }) => {
      const payload = asRecord(body);
      const barcode = String(payload.barcode ?? payload.Barcode ?? "");
      return {
        data: {
          barcode,
          items: state().products.filter(
            (product) => product.barcode === barcode,
          ),
        },
      };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-order\/cart\/([^/]+)$/i,
    ({ match }) => {
      const storeCode = decodeURIComponent(match?.[1] ?? "REV001");
      const current = state();
      const cart =
        current.carts.get(storeCode) ?? createCart(storeCode, [], current.now);
      current.carts.set(storeCode, cart);
      return { data: clone(summarizeCart(cart)) };
    },
  );

  const mutateCart: RouteHandler = ({ path, body }) => {
    const current = state();
    const payload = asRecord(body);
    const storeCode = String(payload.storeCode ?? "REV001");
    const cart =
      current.carts.get(storeCode) ?? createCart(storeCode, [], current.now);
    current.carts.set(storeCode, cart);
    // scan-lookup-add 只传条码；在本地先解析为商品编码，再复用购物车写入逻辑。
    const scannedProduct = current.products.find(
      (item) =>
        item.barcode === payload.barcode || item.barcode === payload.Barcode,
    );
    const productCode = String(
      payload.productCode ?? scannedProduct?.productCode ?? "",
    );
    const existingIndex = cart.items.findIndex(
      (item: JsonRecord) => item.productCode === productCode,
    );
    let changedItem: JsonRecord | null = null;
    let removed = false;

    if (path.endsWith("/clear")) {
      cart.items = [];
      mirrorCreate(
        current,
        dataStore,
        "carts",
        `cart-clear:${storeCode}`,
        `Cleared ${storeCode}`,
        "cleared",
      );
    } else if (path.endsWith("/remove")) {
      const detailGuid = String(payload.detailGUID ?? payload.detailGuid ?? "");
      const removeIndex = cart.items.findIndex(
        (item: JsonRecord) =>
          item.detailGUID === detailGuid || item.productCode === productCode,
      );
      if (removeIndex >= 0) {
        const [removedItem] = cart.items.splice(removeIndex, 1);
        removed = true;
        mirrorRemove(
          current,
          dataStore,
          "carts",
          `cart:${storeCode}:${removedItem.productCode}`,
        );
      }
    } else {
      const product = current.products.find(
        (item) => item.productCode === productCode,
      );
      if (!product) {
        throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${productCode}`);
      }
      const quantity = Math.max(0, Number(payload.quantity ?? 1));
      if (existingIndex >= 0) {
        changedItem = {
          ...cart.items[existingIndex],
          quantity,
          amount: Number(cart.items[existingIndex].price) * quantity,
          importAmount:
            Number(cart.items[existingIndex].importPrice) * quantity,
          totalVolume: Number(cart.items[existingIndex].volume ?? 0) * quantity,
          updatedAt: current.now,
        };
        cart.items[existingIndex] = changedItem;
        mirrorUpdate(
          current,
          dataStore,
          "carts",
          `cart:${storeCode}:${productCode}`,
          `Cart ${productCode}`,
          "updated",
        );
      } else {
        changedItem = createCartLine(product, quantity, current.now);
        cart.items.push(changedItem);
        mirrorCreate(
          current,
          dataStore,
          "carts",
          `cart:${storeCode}:${productCode}`,
          `Cart ${productCode}`,
        );
      }
    }

    const summary = summarizeCart(cart);
    Object.assign(cart, summary);
    if (path.includes("/scan-")) {
      const mutation = {
        productCode,
        removed,
        changedItem: clone(changedItem),
        summary: {
          orderGUID: cart.orderGUID,
          storeCode,
          cartRevision: current.sequence,
          totalAmount: cart.totalAmount,
          totalImportAmount: cart.totalImportAmount,
          totalQuantity: cart.totalQuantity,
          totalSku: cart.totalSku,
        },
      };
      if (path.endsWith("/scan-lookup-add")) {
        return {
          data: {
            barcode: String(payload.barcode ?? payload.Barcode ?? ""),
            matchType: "Barcode",
            items: changedItem ? [clone(changedItem)] : [],
            added: Boolean(changedItem),
            cart: mutation,
          },
        };
      }
      return { data: mutation };
    }
    return { data: clone(cart) };
  };
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/store-order\/cart\/(?:add|update|remove|clear|scan-add|scan-update|scan-lookup-add)$/i,
    mutateCart,
  );
  register(transport, ["POST"], "/react/v1/store-order/submit", ({ body }) => {
    const current = state();
    const payload = asRecord(body);
    const storeCode = String(payload.storeCode ?? "REV001");
    const cart = summarizeCart(
      current.carts.get(storeCode) ?? createCart(storeCode, [], current.now),
    );
    const orderGUID = nextId(current, "review-order");
    const order = {
      ...clone(cart),
      orderGUID,
      orderNo: `REV-ORDER-${String(current.sequence).padStart(4, "0")}`,
      flowStatus: 1,
      remarks: String(payload.remarks ?? cart.remarks ?? ""),
      totalOrderAmount: cart.totalAmount,
      importTotalAmount: cart.totalImportAmount,
      oemTotalAmount: cart.totalAmount,
      totalAllocQuantity: cart.totalQuantity,
      totalOrderVolume: cart.totalVolume,
      totalAllocVolume: cart.totalVolume,
      createdAt: current.now,
      updatedAt: current.now,
    };
    current.orders.unshift(order);
    current.carts.set(storeCode, createCart(storeCode, [], current.now));
    mirrorCreate(
      current,
      dataStore,
      "orders",
      orderGUID,
      order.orderNo,
      "submitted",
    );
    return { data: clone(order), status: 201 };
  });
  register(transport, ["POST"], "/react/v1/store-order/list", () => ({
    data: page(state().orders),
  }));
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-order\/detail\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const order = findByAnyId(state().orders, id);
      if (!order) throw new Error(`IOS_REVIEW_ORDER_NOT_FOUND: ${id}`);
      return { data: clone(order) };
    },
  );

  register(
    transport,
    ["GET"],
    "/react/v1/product-warehouse/mobile/lookup",
    ({ query }) => {
      const keyword = (query.get("keyword") ?? "").toLowerCase();
      return {
        data: state().products.filter((product) =>
          [
            product.productCode,
            product.itemNumber,
            product.barcode,
            product.productName,
          ]
            .join(" ")
            .toLowerCase()
            .includes(keyword),
        ),
      };
    },
  );
  register(
    transport,
    ["GET", "PATCH"],
    /^\/react\/v1\/product-warehouse\/mobile\/([^/]+)$/i,
    ({ method, match, body }) => {
      const productCode = decodeURIComponent(match?.[1] ?? "");
      const product = state().products.find(
        (item) => item.productCode === productCode,
      );
      if (!product)
        throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${productCode}`);
      if (method === "PATCH") {
        Object.assign(product, asRecord(body), { updatedAt: state().now });
        mirrorUpdate(
          state(),
          dataStore,
          "warehouse",
          `warehouse:${productCode}`,
          product.productName,
        );
      }
      return { data: clone(product) };
    },
  );
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/product-warehouse\/mobile\/([^/]+)\/location$/i,
    ({ match, body }) => {
      const productCode = decodeURIComponent(match?.[1] ?? "");
      const product = state().products.find(
        (item) => item.productCode === productCode,
      );
      if (!product)
        throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${productCode}`);
      const location = state().locations.find(
        (item) => item.locationGuid === asRecord(body).locationGuid,
      );
      Object.assign(product, {
        locationGuid: location?.locationGuid ?? null,
        locationCode: location?.locationCode ?? null,
        locationBarcode: location?.locationBarcode ?? null,
        updatedAt: state().now,
      });
      mirrorUpdate(
        state(),
        dataStore,
        "warehouse",
        `warehouse:${productCode}`,
        product.productName,
      );
      return { data: clone(product) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/product-warehouse\/mobile\/([^/]+)\/print-payload$/i,
    ({ match, query }) => {
      const productCode = decodeURIComponent(match?.[1] ?? "");
      const product = state().products.find(
        (item) => item.productCode === productCode,
      );
      if (!product)
        throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${productCode}`);
      if (query.get("type") === "location") {
        const location = state().locations.find(
          (item) => item.locationGuid === product.locationGuid,
        );
        return {
          data: clone({
            ...location,
            itemNumber: product.itemNumber,
            productName: product.productName,
          }),
        };
      }
      return { data: clone(product) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/product-warehouse\/mobile\/[^/]+\/image-upload-signature$/i,
    () => ({
      data: {
        url: "data:application/octet-stream;base64,UkVWSUVX",
        objectKey: "ios-review/warehouse/demo-image.svg",
        headers: {},
      },
    }),
  );
  register(
    transport,
    ["POST"],
    "/react/v1/product-warehouse/detect",
    ({ body }) => ({
      data: (Array.isArray(asRecord(body).Items)
        ? asRecord(body).Items
        : []
      ).map((item: JsonRecord) => ({
        localProductCode: item.ProductCode ?? "REV-PROD-001",
        domesticProductCode: item.ProductCode ?? "REV-PROD-001",
        matchType: "productCode",
        hasProductCodeConflict: false,
      })),
    }),
  );

  register(transport, ["GET"], "/react/v1/locations/lookup", ({ query }) => {
    const keyword = (query.get("keyword") ?? "").toLowerCase();
    return {
      data: state().locations.filter((location) =>
        [location.locationCode, location.locationBarcode]
          .join(" ")
          .toLowerCase()
          .includes(keyword),
      ),
    };
  });
  register(transport, ["GET"], "/react/v1/locations/mobile/unused", () => ({
    data: {
      items: clone(state().locations.filter((item) => item.productCount === 0)),
    },
  }));
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/locations\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const location = findByAnyId(state().locations, id);
      if (!location) throw new Error(`IOS_REVIEW_LOCATION_NOT_FOUND: ${id}`);
      return { data: clone(location) };
    },
  );
  register(transport, ["POST"], "/react/v1/locations", ({ body }) => {
    const current = state();
    const payload = asRecord(body);
    const location: JsonRecord = {
      ...payload,
      locationGuid: nextId(current, "review-location"),
      productCount: 0,
      products: [],
      updatedAt: current.now,
      updatedBy: "App Review Demo",
    };
    current.locations.push(location);
    mirrorCreate(
      current,
      dataStore,
      "warehouse",
      location.locationGuid,
      String(location.locationCode ?? "Demo location"),
    );
    return { data: clone(location), status: 201 };
  });
  register(
    transport,
    ["PUT", "DELETE"],
    /^\/react\/v1\/locations\/([^/]+)$/i,
    ({ method, match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const index = current.locations.findIndex(
        (item) => item.locationGuid === id,
      );
      if (index < 0) throw new Error(`IOS_REVIEW_LOCATION_NOT_FOUND: ${id}`);
      if (method === "DELETE") {
        current.locations.splice(index, 1);
        mirrorRemove(current, dataStore, "warehouse", id);
        return { data: { success: true } };
      }
      current.locations[index] = {
        ...current.locations[index],
        ...asRecord(body),
        locationGuid: id,
        updatedAt: current.now,
      };
      mirrorUpdate(
        current,
        dataStore,
        "warehouse",
        id,
        String(current.locations[index].locationCode ?? id),
      );
      return { data: clone(current.locations[index]) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/locations\/([^/]+)\/products\/bind$/i,
    ({ match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const location = findByAnyId(current.locations, id);
      if (!location) throw new Error(`IOS_REVIEW_LOCATION_NOT_FOUND: ${id}`);
      const identifier = String(asRecord(body).productIdentifier ?? "");
      const product = current.products.find((item) =>
        [item.productCode, item.itemNumber, item.barcode].includes(identifier),
      );
      if (
        product &&
        !location.products.some(
          (item: JsonRecord) => item.productCode === product.productCode,
        )
      ) {
        location.products.push(clone(product));
        location.productCount = location.products.length;
      }
      mirrorUpdate(
        current,
        dataStore,
        "warehouse",
        id,
        String(location.locationCode ?? id),
      );
      return { data: clone(location) };
    },
  );
  register(
    transport,
    ["DELETE"],
    /^\/react\/v1\/locations\/([^/]+)\/products\/([^/]+)$/i,
    ({ match }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const productCode = decodeURIComponent(match?.[2] ?? "");
      const location = findByAnyId(current.locations, id);
      if (!location) throw new Error(`IOS_REVIEW_LOCATION_NOT_FOUND: ${id}`);
      location.products = location.products.filter(
        (item: JsonRecord) => item.productCode !== productCode,
      );
      location.productCount = location.products.length;
      mirrorUpdate(
        current,
        dataStore,
        "warehouse",
        id,
        String(location.locationCode ?? id),
      );
      return { data: clone(location) };
    },
  );

  register(transport, ["POST"], "/react/v1/containers/list", () => ({
    data: page(state().containers),
  }));
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/containers\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const container = state().containers.find(
        (item) => item.hguid === id || item.HGUID === id,
      );
      if (!container) throw new Error(`IOS_REVIEW_CONTAINER_NOT_FOUND: ${id}`);
      return { data: clone(container) };
    },
  );
  register(transport, ["POST"], "/react/v1/containers", ({ body }) => {
    const current = state();
    const id = nextId(current, "review-container");
    current.containers.push({ ...asRecord(body), hguid: id, HGUID: id });
    mirrorCreate(current, dataStore, "warehouse", id, "Created demo container");
    return { data: { containerGuid: id }, status: 201 };
  });
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/containers\/([^/]+)$/i,
    ({ match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const container = state().containers.find(
        (item) => item.hguid === id || item.HGUID === id,
      );
      if (!container) throw new Error(`IOS_REVIEW_CONTAINER_NOT_FOUND: ${id}`);
      Object.assign(container, asRecord(body));
      mirrorUpdate(
        state(),
        dataStore,
        "warehouse",
        id,
        "Updated demo container",
      );
      return { data: { success: true } };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/containers\/([^/]+)\/products\/query$/i,
    () => ({
      data: {
        items: clone(state().containerDetails),
        itemsTotal: state().containerDetails.length,
        pageNumber: 1,
        pageSize: 30,
        hasMore: false,
        totalComputed: true,
        statsComputed: true,
        tagStats: {
          all: state().containerDetails.length,
          existing: state().containerDetails.length,
        },
      },
    }),
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/containers\/[^/]+\/products\/export$/i,
    () => ({
      data: binaryExport(),
      headers: {
        "content-type":
          "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "content-disposition": "attachment; filename=review-container.xlsx",
      },
    }),
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/containers\/(?:batch-update-details|batch-delete-details|sync-from-hq|push-to-hbsales|details\/align-domestic-product-code)$/i,
    ({ path, body }) => {
      const current = state();
      const requested = Array.isArray(body)
        ? body.length
        : Array.isArray(asRecord(body).hguids)
          ? asRecord(body).hguids.length
          : 1;
      mirrorUpdate(current, dataStore, "warehouse", path, path, "completed");
      return {
        data: {
          success: true,
          totalRequested: requested,
          totalUpdated: requested,
          totalDeleted: path.includes("delete") ? requested : 0,
          oldProductCode: "REV-PROD-OLD",
          newProductCode: "REV-PROD-001",
          updatedDomesticProducts: 1,
          updatedContainerDetails: 1,
          syncedCount: 1,
        },
      };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/containers\/[^/]+\/actions\/(?:apply-float-rate|apply-prices|recalculate-costs|backfill-last-prices)$/i,
    () => ({
      data: { success: true, totalRequested: 1, totalUpdated: 1 },
    }),
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/(?:container-products\/create-new-products|container-products\/submit-container|products\/push-to-hq)\/jobs$/i,
    ({ path, body }) => {
      const current = state();
      const jobId = nextId(current, "review-job");
      mirrorCreate(current, dataStore, "warehouse", jobId, path, "completed");
      return {
        data: {
          jobId,
          operationId: asRecord(body).operationId,
          status: "Succeeded",
          result: {
            createdCount: 1,
            successCount: 1,
            totalCount: 1,
            failedCount: 0,
            containerCompleted: true,
            errors: [],
          },
        },
        status: 201,
      };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/(?:container-products\/create-new-products|products\/push-to-hq)\/jobs\/([^/]+)$/i,
    ({ match }) => ({
      data: {
        jobId: decodeURIComponent(match?.[1] ?? "review-job"),
        status: "Succeeded",
        result: {
          createdCount: 1,
          successCount: 1,
          totalCount: 1,
          failedCount: 0,
          containerCompleted: true,
          errors: [],
        },
      },
    }),
  );

  register(transport, ["GET"], "/v1/domestic-product-creation/batches", () => ({
    data: page(state().domesticBatches),
  }));
  register(
    transport,
    ["GET"],
    /^\/v1\/domestic-product-creation\/batch\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const batch = findByAnyId(state().domesticBatches, id);
      if (!batch) throw new Error(`IOS_REVIEW_BATCH_NOT_FOUND: ${id}`);
      return { data: clone(batch) };
    },
  );
  register(
    transport,
    ["POST"],
    "/v1/domestic-product-creation/batch",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const batch = {
        ...payload,
        batchNumber: String(
          payload.batchNumber ?? nextId(current, "REV-BATCH"),
        ),
        createdTime: current.now,
        createdBy: "App Review Demo",
        items: Array.isArray(payload.items) ? payload.items : [],
      };
      current.domesticBatches.unshift(batch);
      mirrorCreate(
        current,
        dataStore,
        "domesticPurchase",
        batch.batchNumber,
        batch.batchNumber,
      );
      return { data: clone(batch), status: 201 };
    },
  );
  register(
    transport,
    ["PUT"],
    /^\/v1\/domestic-product-creation\/batch\/([^/]+)\/items$/i,
    ({ match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const batch = findByAnyId(current.domesticBatches, id);
      if (!batch) throw new Error(`IOS_REVIEW_BATCH_NOT_FOUND: ${id}`);
      batch.items = Array.isArray(asRecord(body).items)
        ? asRecord(body).items
        : body;
      mirrorUpdate(current, dataStore, "domesticPurchase", id, id);
      return { data: clone(batch) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/v1\/domestic-product-creation\/batch\/[^/]+\/export$/i,
    () => ({
      data: binaryExport(),
      headers: {
        "content-type":
          "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      },
    }),
  );
  register(transport, ["GET"], "/v1/DomesticProducts", () => ({
    data: page(state().domesticProducts),
  }));
  register(
    transport,
    ["PUT"],
    /^\/v1\/DomesticProducts\/([^/]+)$/i,
    ({ match, body }) => {
      const current = state();
      const code = decodeURIComponent(match?.[1] ?? "");
      const product = current.domesticProducts.find(
        (item) => item.productCode === code,
      );
      if (!product) throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${code}`);
      Object.assign(product, asRecord(body), { productCode: code });
      mirrorUpdate(
        current,
        dataStore,
        "domesticPurchase",
        code,
        product.productName,
      );
      return { data: clone(product) };
    },
  );
  register(transport, ["GET"], "/v1/ChinaSuppliers/active", () => ({
    data: [{ supplierCode: "REV-CN-SUP", supplierName: "Demo China Supplier" }],
  }));
  register(transport, ["GET"], "/v1/ProductPrefixCodes", () => ({
    data: {
      items: [
        {
          prefixCode: "REV",
          prefixName: "Review Demo",
          prefixDescription: "Synthetic prefix",
        },
      ],
    },
  }));

  register(
    transport,
    ["POST"],
    "/react/v1/local-supplier-invoices/grid",
    () => ({
      data: page(state().invoices),
    }),
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/local-supplier-invoices\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const invoice = findByAnyId(state().invoices, id);
      if (!invoice) throw new Error(`IOS_REVIEW_INVOICE_NOT_FOUND: ${id}`);
      return { data: clone(invoice) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/local-supplier-invoices\/([^/]+)\/details\/grid$/i,
    () => ({
      data: page(state().invoiceDetails, 1, 50),
    }),
  );

  register(transport, ["POST"], "/react/v1/advertisements/grid", () => ({
    data: page(state().advertisements),
  }));
  register(
    transport,
    ["POST"],
    "/react/v1/advertisements/upload-signature",
    () => ({
      data: {
        url: "data:application/octet-stream;base64,UkVWSUVX",
        uploadUrl: "data:application/octet-stream;base64,UkVWSUVX",
        mediaUrl: "data:image/svg+xml,%3Csvg%3E%3C/svg%3E",
        objectKey: "ios-review/advertisements/upload.svg",
        headers: {},
      },
    }),
  );
  register(transport, ["POST"], "/react/v1/advertisements", ({ body }) => {
    const current = state();
    const advertisement: JsonRecord = {
      ...asRecord(body),
      id: nextId(current, "review-ad"),
    };
    current.advertisements.unshift(advertisement);
    mirrorCreate(
      current,
      dataStore,
      "advertisements",
      advertisement.id,
      String(advertisement.title ?? "Demo ad"),
    );
    return { data: clone(advertisement), status: 201 };
  });
  register(
    transport,
    ["GET", "PUT", "DELETE"],
    /^\/react\/v1\/advertisements\/([^/]+)$/i,
    ({ method, match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const index = current.advertisements.findIndex((item) => item.id === id);
      if (index < 0)
        throw new Error(`IOS_REVIEW_ADVERTISEMENT_NOT_FOUND: ${id}`);
      if (method === "DELETE") {
        current.advertisements.splice(index, 1);
        mirrorRemove(current, dataStore, "advertisements", id);
        return { data: { success: true } };
      }
      if (method === "PUT") {
        current.advertisements[index] = {
          ...current.advertisements[index],
          ...asRecord(body),
          id,
        };
        mirrorUpdate(
          current,
          dataStore,
          "advertisements",
          id,
          String(current.advertisements[index].title ?? id),
        );
      }
      return { data: clone(current.advertisements[index]) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/advertisements\/([^/]+)\/enable$/i,
    ({ match, query }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const item = state().advertisements.find(
        (candidate) => candidate.id === id,
      );
      if (!item) throw new Error(`IOS_REVIEW_ADVERTISEMENT_NOT_FOUND: ${id}`);
      item.isEnabled = query.get("enable") !== "false";
      mirrorUpdate(
        state(),
        dataStore,
        "advertisements",
        id,
        item.title,
        item.isEnabled ? "active" : "disabled",
      );
      return { data: true };
    },
  );

  register(transport, ["POST"], "/react/v1/promotions/store/grid", () => ({
    data: page(state().promotions),
  }));
  register(transport, ["POST"], "/react/v1/promotions/store", ({ body }) => {
    const current = state();
    const promotion: JsonRecord = {
      ...asRecord(body),
      id: nextId(current, "review-promotion"),
      scopeType: "StoreOnly",
      canEditInStoreScope: true,
      canCopyToStore: true,
    };
    current.promotions.unshift(promotion);
    mirrorCreate(
      current,
      dataStore,
      "promotions",
      promotion.id,
      String(promotion.name ?? "Demo promotion"),
    );
    return { data: clone(promotion), status: 201 };
  });
  register(
    transport,
    ["POST"],
    "/react/v1/promotions/store/copy",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const source =
        current.promotions.find(
          (item) => item.id === payload.sourcePromotionId,
        ) ?? current.promotions[0];
      const promotion = {
        ...clone(source),
        id: nextId(current, "review-promotion"),
        name: payload.name ?? `${source?.name ?? "Demo promotion"} Copy`,
        stores: [{ storeCode: payload.storeCode ?? "REV002" }],
        scopeType: "StoreOnly",
      };
      current.promotions.unshift(promotion);
      mirrorCreate(
        current,
        dataStore,
        "promotions",
        promotion.id,
        promotion.name,
      );
      return { data: clone(promotion), status: 201 };
    },
  );
  register(
    transport,
    ["GET", "PUT"],
    /^\/react\/v1\/promotions\/store\/([^/]+)$/i,
    ({ method, match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const promotion = state().promotions.find((item) => item.id === id);
      if (!promotion) throw new Error(`IOS_REVIEW_PROMOTION_NOT_FOUND: ${id}`);
      if (method === "PUT") {
        Object.assign(promotion, asRecord(body), { id });
        mirrorUpdate(state(), dataStore, "promotions", id, promotion.name);
      }
      return { data: clone(promotion) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/promotions\/store\/([^/]+)\/enable$/i,
    ({ match, query }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const promotion = state().promotions.find((item) => item.id === id);
      if (!promotion) throw new Error(`IOS_REVIEW_PROMOTION_NOT_FOUND: ${id}`);
      promotion.isEnabled = query.get("enable") !== "false";
      mirrorUpdate(
        state(),
        dataStore,
        "promotions",
        id,
        promotion.name,
        promotion.isEnabled ? "active" : "disabled",
      );
      return { data: true };
    },
  );

  register(
    transport,
    ["POST"],
    "/react/v1/store-product-maintenance/lookup",
    ({ body }) => {
      const keyword = String(asRecord(body).keyword ?? "").toLowerCase();
      return {
        data: state().products.filter((product) =>
          [
            product.productCode,
            product.itemNumber,
            product.barcode,
            product.productName,
          ]
            .join(" ")
            .toLowerCase()
            .includes(keyword),
        ),
      };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-product-maintenance\/([^/]+)(?:\/fast-detail)?$/i,
    ({ match }) => {
      const code = decodeURIComponent(match?.[1] ?? "");
      const detail = state().productDetails.get(code);
      if (!detail) throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${code}`);
      return { data: clone(detail) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-product-maintenance\/([^/]+)\/codes$/i,
    ({ match, query }) => {
      const code = decodeURIComponent(match?.[1] ?? "");
      const detail = state().productDetails.get(code);
      if (!detail) throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${code}`);
      const requestedType = query.get("type") ?? query.get("productType");
      const items = requestedType === "2" ? detail.multiCodes : detail.setCodes;
      return {
        data: {
          items: clone(items),
          totalCount: items.length,
          page: Number(query.get("page") ?? 1),
          pageSize: Number(query.get("pageSize") ?? 50),
          hasMore: false,
        },
      };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/products/create-with-prices",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const productCode = String(
        payload.productCode ?? nextId(current, "REV-PROD"),
      );
      const product: JsonRecord = {
        ...payload,
        productCode,
        barcode: payload.barcode ?? `9330000${current.sequence}`,
        isActive: true,
        warehouseIsActive: true,
        updatedAt: current.now,
      };
      current.products.push(product);
      current.productDetails.set(
        productCode,
        createProductDetailFixture(product),
      );
      mirrorCreate(
        current,
        dataStore,
        "products",
        productCode,
        String(product.productName ?? productCode),
      );
      return {
        data: { productCode, product: clone(product), created: true },
        status: 201,
      };
    },
  );
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/store-product-maintenance\/store-prices\/([^/]+)$/i,
    ({ match, body }) => {
      const current = state();
      const uuid = decodeURIComponent(match?.[1] ?? "review-store-price");
      const detail = Array.from(current.productDetails.values()).find(
        (item) => item.storePrice?.uuid === uuid,
      );
      if (!detail) throw new Error(`IOS_REVIEW_STORE_PRICE_NOT_FOUND: ${uuid}`);
      Object.assign(detail.storePrice, asRecord(body), { uuid });
      mirrorUpdate(current, dataStore, "products", uuid, "Store price");
      return { data: clone(detail.storePrice) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/store-product-maintenance\/store-prices\/([^/]+)\/sync-warehouse$/i,
    ({ match, body }) => {
      const current = state();
      const uuid = decodeURIComponent(match?.[1] ?? "");
      const detail = Array.from(current.productDetails.values()).find(
        (item) => item.storePrice?.uuid === uuid,
      );
      if (!detail) throw new Error(`IOS_REVIEW_STORE_PRICE_NOT_FOUND: ${uuid}`);
      const payload = asRecord(body);
      const previousPurchasePrice = detail.storePrice.purchasePrice ?? null;
      const previousRetailPrice = detail.storePrice.retailPrice ?? null;
      const warehousePurchasePrice =
        payload.expectedWarehousePurchasePrice ?? previousPurchasePrice;
      const warehouseRetailPrice =
        payload.expectedWarehouseRetailPrice ?? previousRetailPrice;
      detail.storePrice.purchasePrice = warehousePurchasePrice;
      const retailUpdated = Boolean(payload.confirmRetailPrice);
      if (retailUpdated) detail.storePrice.retailPrice = warehouseRetailPrice;
      mirrorUpdate(
        current,
        dataStore,
        "products",
        uuid,
        "Warehouse price sync",
        "synced",
      );
      return {
        data: {
          status: retailUpdated ? "synced" : "confirmation_required",
          purchaseUpdated: warehousePurchasePrice !== previousPurchasePrice,
          retailUpdated,
          retailConfirmationRequired: !retailUpdated,
          storePrice: clone(detail.storePrice),
          warehousePurchasePrice,
          warehouseRetailPrice,
          previousStorePurchasePrice: previousPurchasePrice,
          previousStoreRetailPrice: previousRetailPrice,
          discountRate: detail.storePrice.discountRate ?? 0,
          previousDiscountedRetailPrice: previousRetailPrice,
          newDiscountedRetailPrice: detail.storePrice.retailPrice,
        },
      };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/store-product-maintenance/evaluate-auto-pricing",
    ({ body }) => ({
      data: {
        ...asRecord(body),
        currentRetailPrice: 12.95,
        recalculatedRetailPrice: 12.95,
        currentRetailPriceFormatted: "$12.95",
        recalculatedRetailPriceFormatted: "$12.95",
        hasValidPurchasePrice: true,
        shouldUpdate: false,
      },
    }),
  );
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/store-product-maintenance\/products\/([^/]+)\/(?:type|clearance-price)$/i,
    ({ path, match, body }) => {
      const code = decodeURIComponent(match?.[1] ?? "");
      const product = state().products.find(
        (item) => item.productCode === code,
      );
      const detail = state().productDetails.get(code);
      if (!product || !detail)
        throw new Error(`IOS_REVIEW_PRODUCT_NOT_FOUND: ${code}`);
      Object.assign(product, asRecord(body));
      Object.assign(detail, asRecord(body));
      mirrorUpdate(state(), dataStore, "products", code, product.productName);
      if (path.endsWith("/type")) {
        return {
          data: {
            productCode: code,
            productType: product.productType ?? 1,
            productTypeLabel: "Standard",
          },
        };
      }
      detail.clearancePrice = {
        uuid: `review-clearance-${code}`,
        storeCode: "REV001",
        storeName: "Demo Brisbane",
        productCode: code,
        clearanceBarcode:
          asRecord(body).clearanceBarcode ?? IOS_REVIEW_SAMPLE_BARCODE,
        clearancePrice: asRecord(body).clearancePrice ?? 9.95,
      };
      return { data: clone(detail.clearancePrice) };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/store-product-maintenance/set-codes",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const detail = current.productDetails.get(
        String(payload.productCode ?? ""),
      );
      if (!detail)
        throw new Error(
          `IOS_REVIEW_PRODUCT_NOT_FOUND: ${payload.productCode ?? ""}`,
        );
      if (Number(payload.productType) === 2) {
        const item = {
          uuid: nextId(current, "review-multi-code"),
          setCodeId: "",
          storeCode: payload.storeCode ?? "REV001",
          productCode: payload.productCode,
          multiCodeProductCode: payload.productCode,
          storeMultiCodeProductCode: payload.productCode,
          barcode: payload.barcode,
          purchasePrice: detail.storePrice?.purchasePrice ?? null,
          retailPrice: payload.retailPrice ?? null,
          discountRate: 0,
          isAutoPricing: false,
          isSpecialProduct: false,
          isActive: payload.isActive ?? true,
          rate: 1,
        };
        detail.multiCodes.push(item);
        detail.multiCodeCount = detail.multiCodes.length;
        mirrorCreate(current, dataStore, "products", item.uuid, "Multi code");
        return { data: clone(item), status: 201 };
      }
      const item = {
        setCodeId: nextId(current, "review-set-code"),
        productCode: payload.productCode,
        setProductCode: payload.productCode,
        setItemNumber: detail.itemNumber,
        setBarcode: payload.barcode,
        setPurchasePrice: detail.storePrice?.purchasePrice ?? null,
        setRetailPrice: payload.retailPrice ?? null,
        setQuantity: 1,
        setType: payload.productType ?? 1,
        setTypeDescription: "Standard",
        isActive: payload.isActive ?? true,
      };
      detail.setCodes.push(item);
      detail.setCodeCount = detail.setCodes.length;
      mirrorCreate(current, dataStore, "products", item.setCodeId, "Set code");
      return { data: clone(item), status: 201 };
    },
  );
  register(
    transport,
    ["PUT", "DELETE"],
    /^\/react\/v1\/store-product-maintenance\/(set-codes|multi-codes)\/([^/]+)$/i,
    ({ method, match, body }) => {
      const id = decodeURIComponent(match?.[2] ?? "");
      const detail = Array.from(state().productDetails.values()).find(
        (item) =>
          item.setCodes.some((code: JsonRecord) => code.setCodeId === id) ||
          item.multiCodes.some((code: JsonRecord) => code.uuid === id),
      );
      if (!detail) throw new Error(`IOS_REVIEW_PRODUCT_CODE_NOT_FOUND: ${id}`);
      // 页面当前对 Set/Multi Code 共用 /set-codes/:id；必须按真实 id 归属判断集合。
      const collection = detail.setCodes.some(
        (code: JsonRecord) => code.setCodeId === id,
      )
        ? "setCodes"
        : "multiCodes";
      const idKey = collection === "multiCodes" ? "uuid" : "setCodeId";
      const index = detail[collection].findIndex(
        (code: JsonRecord) => code[idKey] === id,
      );
      if (method === "DELETE") {
        detail[collection].splice(index, 1);
        detail.setCodeCount = detail.setCodes.length;
        detail.multiCodeCount = detail.multiCodes.length;
        mirrorRemove(state(), dataStore, "products", id);
        return { data: true };
      }
      const payload = asRecord(body);
      const values =
        collection === "setCodes"
          ? {
              setBarcode: payload.barcode,
              setRetailPrice: payload.retailPrice,
              isActive: payload.isActive,
            }
          : payload;
      detail[collection][index] = {
        ...detail[collection][index],
        ...values,
        [idKey]: id,
      };
      mirrorUpdate(state(), dataStore, "products", id, "Product code");
      return { data: clone(detail[collection][index]) };
    },
  );

  register(
    transport,
    ["POST"],
    "/react/v1/installment-orders/list",
    ({ body }) => {
      const payload = asRecord(body);
      const storeCodes = (payload.branchCode ? [payload.branchCode] : [])
        .map((value: unknown) => String(value).trim().toUpperCase())
        .filter(Boolean);
      const statuses = (payload.status != null ? [payload.status] : [])
        .map((value: unknown) => Number(value))
        .filter((value: number) => Number.isFinite(value));
      const customerPhone = String(payload.customerPhone ?? "")
        .trim()
        .toLowerCase();
      const customerName = String(payload.customerName ?? "")
        .trim()
        .toLowerCase();
      const startDate = String(payload.startDate ?? "").trim();
      const endDate = String(payload.endDate ?? "").trim();

      // 审核模式执行与真实接口一致的筛选，避免固定 fixture 掩盖页面参数错误。
      const filtered = state().installmentOrders.filter((order) => {
        const storeCode = String(order.storeCode ?? "").toUpperCase();
        const createdDate = toStoreLocalDateOnly(
          order.createdAt,
          order.storeCode,
        );
        if (storeCodes.length && !storeCodes.includes(storeCode)) return false;
        if (statuses.length && !statuses.includes(Number(order.status)))
          return false;
        if (
          customerPhone &&
          !String(order.customerPhone ?? "")
            .toLowerCase()
            .includes(customerPhone)
        )
          return false;
        if (
          customerName &&
          !String(order.customerName ?? "")
            .toLowerCase()
            .includes(customerName)
        )
          return false;
        if (startDate && createdDate < startDate) return false;
        if (endDate && createdDate > endDate) return false;
        return true;
      });
      const summaries = filtered.map(
        ({
          lines: _lines,
          payments: _payments,
          pickupInfo: _pickupInfo,
          cancellationInfo: _cancellationInfo,
          note: _note,
          deviceCode: _deviceCode,
          ...order
        }) => order,
      );
      const pageNumber = Number(payload.pageNumber ?? 1);
      const pageSize = Number(payload.pageSize ?? 20);
      return { data: pagedSlice(summaries, pageNumber, pageSize) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/installment-orders\/detail\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const order = state().installmentOrders.find(
        (item) => String(item.installmentGuid ?? "") === id,
      );
      if (!order) throw new Error(`IOS_REVIEW_INSTALLMENT_NOT_FOUND: ${id}`);
      const {
        lines = [],
        payments = [],
        pickupInfo = null,
        cancellationInfo = null,
        ...detailOrder
      } = order;
      return {
        data: {
          // 主单与明细集合保持独立，匹配 Central Backend 的详情契约。
          order: clone(detailOrder),
          lines: clone(lines),
          payments: clone(payments),
          pickupInfo: clone(pickupInfo),
          cancellationInfo: clone(cancellationInfo),
        },
      };
    },
  );

  register(transport, ["POST"], "/react/v1/store-vouchers/list", () => ({
    data: page(state().vouchers),
  }));
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-vouchers\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const voucher = findByAnyId(state().vouchers, id);
      if (!voucher) throw new Error(`IOS_REVIEW_VOUCHER_NOT_FOUND: ${id}`);
      return {
        data: {
          voucher: clone(voucher),
          ledger: clone(voucher.ledger ?? []),
          relatedOrders: clone(voucher.relatedOrders ?? []),
        },
      };
    },
  );

  register(
    transport,
    ["GET"],
    "/react/v1/seasonal-card-remaining/catalog",
    () => ({
      data: clone(state().seasonalCatalog),
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/seasonal-card-remaining/submissions",
    () => ({
      data: page(state().seasonalSubmissions),
    }),
  );
  register(
    transport,
    ["POST"],
    "/react/v1/seasonal-card-remaining/submissions",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const catalog = current.seasonalCatalog.find(
        (item) => item.catalogGuid === payload.catalogGuid,
      );
      const submission = {
        ...payload,
        submissionGuid: nextId(current, "review-seasonal-submission"),
        cardType: catalog?.cardType ?? 1,
        cardTypeName: catalog?.cardTypeName ?? "Christmas Card",
        unitPrice: payload.customUnitPrice ?? catalog?.fixedUnitPrice ?? 2,
        priceLabel: catalog?.priceLabel ?? "$2.00",
        submittedByName: "App Review Demo",
        submittedAt: current.now,
      };
      current.seasonalSubmissions.unshift(submission);
      mirrorCreate(
        current,
        dataStore,
        "seasonalCards",
        submission.submissionGuid,
        "Seasonal card submission",
      );
      return { data: clone(submission), status: 201 };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/seasonal-card-remaining\/submissions\/([^/]+)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const submission = findByAnyId(state().seasonalSubmissions, id);
      if (!submission)
        throw new Error(`IOS_REVIEW_SEASONAL_SUBMISSION_NOT_FOUND: ${id}`);
      return { data: clone(submission) };
    },
  );

  registerAttendanceRoutes(transport, dataStore, holder);
  registerUserRoutes(transport, dataStore, holder);
  registerEmployeeProfileRoutes(transport, dataStore, holder);
  registerDeviceRoutes(transport, dataStore, holder);
  registerReportRoutes(transport, holder);
}

function registerAttendanceRoutes(
  transport: ReviewTransport,
  dataStore: ReviewDataStore,
  holder: AppRouteStateHolder,
) {
  const state = () => holder.current;
  register(transport, ["GET"], "/react/v1/attendance/my/today", () => {
    const current = state();
    const punches = current.punches.filter(
      (item) => item.workDate === current.today,
    );
    const hasClockIn = punches.some((item) => item.punchType === "ClockIn");
    const hasClockOut = punches.some((item) => item.punchType === "ClockOut");
    return {
      data: {
        workDate: current.today,
        storeTimeZone: "Australia/Brisbane",
        schedules: clone(current.schedules.filter((item) => item.isMine)),
        punches: clone(punches),
        holidays: clone(current.holidays),
        nextPunchType: hasClockIn && !hasClockOut ? "ClockOut" : "ClockIn",
        canClockIn: !hasClockIn,
        canClockOut: hasClockIn && !hasClockOut,
      },
    };
  });
  register(transport, ["GET"], "/react/v1/attendance/my/week", ({ query }) => {
    const current = state();
    const weekStart = query.get("weekStartDate") ?? current.today;
    return {
      data: {
        weekStart,
        weekEnd: weekStart,
        days: [
          {
            workDate: current.today,
            dayOfWeek: new Date(`${current.today}T00:00:00Z`).getUTCDay(),
            schedules: clone(current.schedules.filter((item) => item.isMine)),
          },
        ],
      },
    };
  });
  register(transport, ["GET"], "/react/v1/attendance/my/availability", () => ({
    data: clone(state().availability),
  }));
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/my/availability",
    ({ body }) => {
      const current = state();
      const payload = asRecord(body);
      const segments = Array.isArray(payload.segments)
        ? payload.segments
        : [payload];
      const created = segments.map((segment: JsonRecord) => ({
        ...segment,
        availabilityGuid: nextId(current, "review-availability"),
        storeCode: payload.storeCode ?? "REV001",
        workDate: segment.availableDate ?? segment.workDate ?? current.today,
        note: segment.remark ?? segment.note,
        status: "Submitted",
      }));
      current.availability.push(...created);
      created.forEach((item) =>
        mirrorCreate(
          current,
          dataStore,
          "attendance",
          item.availabilityGuid,
          "Availability",
        ),
      );
      return { data: clone(created), status: 201 };
    },
  );
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/attendance\/my\/availability\/([^/]+)$/i,
    ({ match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const item = current.availability.find(
        (candidate) => candidate.availabilityGuid === id,
      );
      if (!item) throw new Error(`IOS_REVIEW_AVAILABILITY_NOT_FOUND: ${id}`);
      Object.assign(item, asRecord(body), {
        workDate:
          asRecord(body).availableDate ??
          asRecord(body).workDate ??
          item.workDate,
        note: asRecord(body).remark ?? asRecord(body).note ?? item.note,
      });
      mirrorUpdate(current, dataStore, "attendance", id, "Availability");
      return { data: clone(item) };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/attendance\/my\/availability\/([^/]+)\/cancel$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const item = state().availability.find(
        (candidate) => candidate.availabilityGuid === id,
      );
      if (item) item.status = "Cancelled";
      mirrorUpdate(
        state(),
        dataStore,
        "attendance",
        id,
        "Availability",
        "cancelled",
      );
      return { data: { success: true } };
    },
  );
  register(transport, ["POST"], "/react/v1/attendance/punch", ({ body }) => {
    const current = state();
    const payload = asRecord(body);
    const punch = {
      ...payload,
      punchGuid: nextId(current, "review-punch"),
      workDate: payload.workDate ?? current.today,
      punchType: payload.punchType ?? "ClockIn",
      punchTimeUtc: current.now,
      punchTimeLocal: current.now,
      status: "Normal",
      locationLatitude: payload.locationLatitude ?? -27.4698,
      locationLongitude: payload.locationLongitude ?? 153.0251,
      locationAccuracy: payload.locationAccuracy ?? 5,
    };
    current.punches.push(punch);
    mirrorCreate(
      current,
      dataStore,
      "attendance",
      punch.punchGuid,
      `Punch ${punch.punchType}`,
    );
    return { data: punch, status: 201 };
  });
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/location-samples",
    () => ({
      data: { success: true, simulated: true },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/attendance/my/leave-requests",
    () => ({
      data: clone(state().leaveRequests),
    }),
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/attendance\/(?:my|managed)\/leave-requests$/i,
    ({ body }) => {
      const current = state();
      const leave: JsonRecord = {
        ...asRecord(body),
        leaveGuid: nextId(current, "review-leave"),
        status: "Pending",
        submittedAt: current.now,
      };
      current.leaveRequests.unshift(leave);
      const approval = {
        approvalGuid: nextId(current, "review-approval"),
        sourceGuid: leave.leaveGuid,
        sourceType: "Leave",
        employeeName: asRecord(body).userGuid
          ? "Demo Staff"
          : "App Review Demo",
        storeCode: leave.storeCode ?? "REV001",
        storeName: "Demo Brisbane",
        workDate: leave.startDate,
        title: `${leave.leaveType ?? "Leave"} request`,
        detail: leave.reason,
        status: "Pending",
        submittedAt: current.now,
      };
      current.approvals.unshift(approval);
      mirrorCreate(
        current,
        dataStore,
        "attendance",
        leave.leaveGuid,
        "Leave request",
        "pending",
      );
      return { data: clone(leave), status: 201 };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/attendance\/my\/leave-requests\/([^/]+)\/cancel$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const leave = state().leaveRequests.find((item) => item.leaveGuid === id);
      if (leave) leave.status = "Cancelled";
      mirrorUpdate(
        state(),
        dataStore,
        "attendance",
        id,
        "Leave request",
        "cancelled",
      );
      return { data: { success: true } };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/leave-attachments/upload-signature",
    () => ({
      data: {
        url: "data:application/octet-stream;base64,UkVWSUVX",
        objectKey: "ios-review/attendance/leave-attachment.txt",
        headers: {},
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/attendance/approvals/pending",
    () => ({
      data: clone(
        state().approvals.filter((item) => item.status === "Pending"),
      ),
    }),
  );
  register(
    transport,
    ["POST"],
    /^\/react\/v1\/attendance\/approvals\/([^/]+)\/(approve|reject)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const decision = match?.[2] === "approve" ? "Approved" : "Rejected";
      const approval = state().approvals.find(
        (item) => item.approvalGuid === id,
      );
      if (approval) {
        approval.status = decision;
        const leave = state().leaveRequests.find(
          (item) => item.leaveGuid === approval.sourceGuid,
        );
        if (leave) leave.status = decision;
      }
      mirrorUpdate(
        state(),
        dataStore,
        "attendance",
        id,
        "Attendance approval",
        decision.toLowerCase(),
      );
      return { data: { success: true } };
    },
  );
  register(transport, ["GET"], "/react/v1/attendance/schedules/week", () => ({
    data: clone(state().schedules),
  }));
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/schedules",
    ({ body }) => {
      const current = state();
      const schedule = {
        ...asRecord(body),
        scheduleGuid: nextId(current, "review-schedule"),
        storeName: "Demo Brisbane",
        employeeName: "App Review Demo",
        status: asRecord(body).status ?? "Draft",
        isMine: asRecord(body).userGuid === "review-user",
      };
      current.schedules.push(schedule);
      mirrorCreate(
        current,
        dataStore,
        "attendance",
        schedule.scheduleGuid,
        "Attendance schedule",
        schedule.status,
      );
      return { data: clone(schedule), status: 201 };
    },
  );
  register(
    transport,
    ["PUT", "DELETE"],
    /^\/react\/v1\/attendance\/schedules\/([^/]+)$/i,
    ({ method, match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const index = current.schedules.findIndex(
        (item) => item.scheduleGuid === id,
      );
      if (index < 0) throw new Error(`IOS_REVIEW_SCHEDULE_NOT_FOUND: ${id}`);
      if (method === "DELETE") {
        current.schedules.splice(index, 1);
        mirrorRemove(current, dataStore, "attendance", id);
        return { data: { success: true } };
      }
      current.schedules[index] = {
        ...current.schedules[index],
        ...asRecord(body),
        scheduleGuid: id,
      };
      mirrorUpdate(
        current,
        dataStore,
        "attendance",
        id,
        "Attendance schedule",
        current.schedules[index].status,
      );
      return { data: clone(current.schedules[index]) };
    },
  );
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/schedules/publish-week",
    () => ({
      data: { success: true, publishedCount: state().schedules.length },
    }),
  );
  register(transport, ["GET"], "/react/v1/attendance/holidays", () => ({
    data: clone(state().holidays),
  }));
  register(
    transport,
    ["POST"],
    "/react/v1/attendance/holidays/sync",
    ({ body }) => ({
      data: {
        ...asRecord(body),
        syncedCount: state().holidays.length,
        createdCount: 0,
        updatedCount: state().holidays.length,
        skippedCount: 0,
        holidays: clone(state().holidays),
        syncedAt: state().now,
      },
    }),
  );
  register(transport, ["POST"], "/react/v1/attendance/holidays", ({ body }) => {
    const current = state();
    const holiday: JsonRecord = {
      ...asRecord(body),
      holidayGuid: nextId(current, "review-holiday"),
    };
    current.holidays.push(holiday);
    mirrorCreate(
      current,
      dataStore,
      "attendance",
      holiday.holidayGuid,
      String(holiday.holidayName ?? "Holiday"),
    );
    return { data: clone(holiday), status: 201 };
  });
  register(
    transport,
    ["PUT", "DELETE"],
    /^\/react\/v1\/attendance\/holidays\/([^/]+)$/i,
    ({ method, match, body }) => {
      const current = state();
      const id = decodeURIComponent(match?.[1] ?? "");
      const index = current.holidays.findIndex(
        (item) => item.holidayGuid === id,
      );
      if (index < 0) throw new Error(`IOS_REVIEW_HOLIDAY_NOT_FOUND: ${id}`);
      if (method === "DELETE") {
        current.holidays.splice(index, 1);
        mirrorRemove(current, dataStore, "attendance", id);
        return { data: { success: true } };
      }
      current.holidays[index] = {
        ...current.holidays[index],
        ...asRecord(body),
        holidayGuid: id,
      };
      mirrorUpdate(
        current,
        dataStore,
        "attendance",
        id,
        String(current.holidays[index].holidayName ?? "Holiday"),
      );
      return { data: clone(current.holidays[index]) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/attendance\/employees\/[^/]+\/week$/i,
    () => ({
      data: {
        weekStart: state().today,
        weekEnd: state().today,
        days: [
          {
            workDate: state().today,
            dayOfWeek: 1,
            schedules: clone(state().schedules),
          },
        ],
      },
    }),
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/attendance\/employees\/[^/]+\/records$/i,
    () => ({
      data: {
        schedules: clone(state().schedules),
        availability: clone(state().availability),
        leaveRequests: clone(state().leaveRequests),
      },
    }),
  );
}

function registerUserRoutes(
  transport: ReviewTransport,
  dataStore: ReviewDataStore,
  holder: AppRouteStateHolder,
) {
  const state = () => holder.current;
  register(transport, ["POST"], "/react/v1/store-users/grid", () => ({
    data: page(state().users),
  }));
  register(transport, ["POST"], "/react/v1/store-users", ({ body }) => {
    const current = state();
    const payload = asRecord(body);
    const user: JsonRecord = {
      ...payload,
      userGuid: nextId(current, "review-user"),
      status: "Active",
      isActive: true,
      createdAt: current.now,
      updatedAt: current.now,
    };
    current.users.push(user);
    mirrorCreate(
      current,
      dataStore,
      "users",
      user.userGuid,
      String(user.fullName ?? user.username ?? "Demo user"),
    );
    return { data: clone(user), status: 201 };
  });
  register(
    transport,
    ["GET", "PUT"],
    /^\/react\/v1\/store-users\/([^/]+)$/i,
    ({ method, match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      if (method === "PUT") {
        Object.assign(user, asRecord(body), {
          userGuid: id,
          updatedAt: state().now,
        });
        mirrorUpdate(
          state(),
          dataStore,
          "users",
          id,
          String(user.fullName ?? user.username ?? id),
        );
      }
      return { data: clone(user) };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/store-users\/([^/]+)\/profile$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      return {
        data: clone({ ...user, address: "123 Demo Street, Brisbane QLD 4000" }),
      };
    },
  );
  register(
    transport,
    ["PUT"],
    /^\/react\/v1\/store-users\/([^/]+)\/(status|password)$/i,
    ({ match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const action = match?.[2];
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      if (action === "status") {
        user.status = asRecord(body).status ?? user.status;
        user.isActive = String(user.status).toLowerCase() === "active";
      }
      mirrorUpdate(
        state(),
        dataStore,
        "users",
        id,
        String(user.fullName ?? user.username ?? id),
        action === "password" ? "password-reset" : String(user.status),
      );
      return { data: { success: true } };
    },
  );
  register(
    transport,
    ["GET", "POST"],
    /^\/Users\/guid\/([^/]+)\/roles$/i,
    ({ method, match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      const catalog = [
        {
          roleGUID: "review-role-store-staff",
          roleName: "StoreStaff",
          description: "Standard review store account",
          isActive: true,
        },
        {
          roleGUID: "review-role-store-manager",
          roleName: "StoreManager",
          description: "Derived from managed stores",
          isActive: true,
        },
      ];
      if (method === "POST") {
        const payload = asRecord(body);
        user.accessRoleGuids = Array.isArray(payload.RoleGuids)
          ? [...payload.RoleGuids]
          : [];
        mirrorUpdate(state(), dataStore, "users", id, "User role access");
        return { data: true };
      }
      const roleGuids = Array.isArray(user.accessRoleGuids)
        ? user.accessRoleGuids
        : [];
      return {
        data: catalog.filter((role) => roleGuids.includes(role.roleGUID)),
      };
    },
  );
  register(
    transport,
    ["GET"],
    /^\/Users\/guid\/([^/]+)\/permissions\/state$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      const directPermissionCodes = Array.isArray(user.directPermissionCodes)
        ? [...user.directPermissionCodes]
        : [];
      const inheritedPermissionCodes = ["Users.View"];
      return {
        data: {
          userGuid: id,
          isSuperAdmin: false,
          implicitAllPermissions: false,
          inheritedPermissionCodes,
          directPermissionCodes,
          effectivePermissionCodes: Array.from(
            new Set([...inheritedPermissionCodes, ...directPermissionCodes]),
          ),
          inheritedSources: [
            {
              roleName: "StoreStaff",
              permissionCodes: inheritedPermissionCodes,
            },
          ],
        },
      };
    },
  );
  register(
    transport,
    ["POST"],
    /^\/Users\/guid\/([^/]+)\/permissions$/i,
    ({ match, body }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const user = findByAnyId(state().users, id);
      if (!user) throw new Error(`IOS_REVIEW_USER_NOT_FOUND: ${id}`);
      const payload = asRecord(body);
      const permissions = payload.permissions ?? payload.Permissions;
      user.directPermissionCodes = Array.isArray(permissions)
        ? [...permissions]
        : [];
      mirrorUpdate(state(), dataStore, "users", id, "User direct access");
      return { data: true };
    },
  );
  register(
    transport,
    ["GET", "PUT", "DELETE"],
    /^\/Users\/guid\/[^/]+\/stores\/[^/]+\/pos-terminal-permissions$/i,
    ({ method, body }) => {
      const current = state();
      if (method === "PUT") {
        current.posPermissionCodes = Array.isArray(
          asRecord(body).grantedPermissionCodes,
        )
          ? [...asRecord(body).grantedPermissionCodes]
          : [];
        mirrorUpdate(
          current,
          dataStore,
          "users",
          "pos-permissions",
          "POS permissions",
        );
      } else if (method === "DELETE") {
        current.posPermissionCodes = [...IOS_REVIEW_PERMISSION_CODES];
        mirrorRemove(current, dataStore, "users", "pos-permissions");
      }
      const assignablePermissions = IOS_REVIEW_PERMISSION_CODES.map((code) => ({
        code,
        name: code,
        group: code.split(".")[0] ?? "General",
        description: `Demo permission ${code}`,
      }));
      return {
        data: {
          mode: method === "DELETE" ? "Inherited" : "Override",
          assignablePermissions,
          inheritedPermissionCodes: [...IOS_REVIEW_PERMISSION_CODES],
          overriddenPermissionCodes: clone(current.posPermissionCodes),
          grantedPermissionCodes: clone(current.posPermissionCodes),
          effectivePermissionCodes: clone(current.posPermissionCodes),
        },
      };
    },
  );
}

function registerEmployeeProfileRoutes(
  transport: ReviewTransport,
  dataStore: ReviewDataStore,
  holder: AppRouteStateHolder,
) {
  const state = () => holder.current;
  register(
    transport,
    ["GET", "PUT"],
    "/EmployeeProfiles/me",
    ({ method, body }) => {
      if (method === "PUT") {
        Object.assign(state().employeeProfile, asRecord(body), {
          updatedAt: state().now,
        });
        mirrorUpdate(
          state(),
          dataStore,
          "users",
          "employee-profile",
          "Employee profile",
        );
      }
      return { data: clone(state().employeeProfile) };
    },
  );
  register(
    transport,
    ["POST"],
    "/EmployeeProfiles/me/image-upload-signature",
    ({ body }) => ({
      data: {
        url: "data:application/octet-stream;base64,UkVWSUVX",
        objectKey: `ios-review/profile/${String(asRecord(body).kind ?? "avatar")}.svg`,
        headers: {},
      },
    }),
  );
  register(
    transport,
    ["POST"],
    "/EmployeeProfiles/me/images/complete",
    ({ body }) => {
      const kind = String(asRecord(body).kind ?? "avatar");
      const key = kind === "identity" ? "identityPhotoUrl" : "avatarUrl";
      state().employeeProfile[key] = "data:image/svg+xml,%3Csvg%3E%3C/svg%3E";
      state().employeeProfile.updatedAt = state().now;
      mirrorUpdate(
        state(),
        dataStore,
        "users",
        "employee-profile",
        "Employee profile image",
      );
      return { data: clone(state().employeeProfile) };
    },
  );
  register(
    transport,
    ["DELETE"],
    /^\/EmployeeProfiles\/me\/images\/(identity|avatar)$/i,
    ({ match }) => {
      const key = match?.[1] === "identity" ? "identityPhotoUrl" : "avatarUrl";
      state().employeeProfile[key] = "";
      state().employeeProfile.updatedAt = state().now;
      mirrorUpdate(
        state(),
        dataStore,
        "users",
        "employee-profile",
        "Employee profile image",
        "removed",
      );
      return { data: clone(state().employeeProfile) };
    },
  );
  register(transport, ["GET"], "/EmployeeProfiles/me/cashier-barcode", () => ({
    data: clone(state().cashierBarcode),
  }));
  register(
    transport,
    ["POST"],
    "/EmployeeProfiles/me/cashier-barcode/refresh",
    () => {
      state().cashierBarcode = {
        ...state().cashierBarcode,
        barcode: IOS_REVIEW_SAMPLE_BARCODE,
        updatedAt: state().now,
      };
      return { data: clone(state().cashierBarcode) };
    },
  );
  register(
    transport,
    ["POST"],
    "/EmployeeProfiles/me/cashier-barcode/print-confirmation",
    () => {
      state().cashierBarcode.printCount += 1;
      state().cashierBarcode.updatedAt = state().now;
      return { data: clone(state().cashierBarcode) };
    },
  );
}

function registerDeviceRoutes(
  transport: ReviewTransport,
  dataStore: ReviewDataStore,
  holder: AppRouteStateHolder,
) {
  const state = () => holder.current;
  register(transport, ["GET"], "/mobile/device-management/paged", () => ({
    data: {
      devices: clone(state().devices),
      pagination: {
        pageNumber: 1,
        pageSize: 20,
        totalCount: state().devices.length,
        totalPages: 1,
      },
    },
  }));
  register(
    transport,
    ["POST"],
    /^\/mobile\/device-management\/([^/]+)\/(activate|disable|lock)$/i,
    ({ match }) => {
      const id = decodeURIComponent(match?.[1] ?? "");
      const action = match?.[2] ?? "activate";
      const device = state().devices.find((item) => String(item.id) === id);
      if (!device) throw new Error(`IOS_REVIEW_DEVICE_NOT_FOUND: ${id}`);
      device.status = action === "activate" ? 1 : action === "disable" ? 0 : 2;
      device.updatedAt = state().now;
      mirrorUpdate(
        state(),
        dataStore,
        "devices",
        id,
        String(device.deviceName ?? id),
        device.status,
      );
      return { data: { success: true } };
    },
  );
  register(transport, ["GET"], "/mobile/app-device-status/paged", () => ({
    data: {
      items: clone(state().appDevices),
      pageNumber: 1,
      pageSize: 20,
      totalCount: state().appDevices.length,
      totalPages: 1,
    },
  }));
  register(transport, ["GET"], "/mobile/app-device-status/summary", () => ({
    data: {
      total: 1,
      online: 1,
      offline: 0,
      android: 0,
      ios: 1,
      unknownSystem: 0,
    },
  }));
  register(transport, ["POST"], "/mobile/app-device-status/heartbeat", () => ({
    data: { success: true, simulated: true },
  }));
}

function registerReportRoutes(
  transport: ReviewTransport,
  holder: AppRouteStateHolder,
) {
  const state = () => holder.current;
  register(
    transport,
    ["GET"],
    "/react/v1/product-movement-report/store-options",
    () => ({
      data: IOS_REVIEW_STORES.map((store) => ({
        storeCode: store.storeCode,
        storeName: store.storeName,
      })),
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/executive-branch-performance",
    () => ({
      data: {
        items: IOS_REVIEW_STORES.map((store, index) => ({
          branchCode: store.storeCode,
          branchName: store.storeName,
          revenue: 1250 + index * 375,
          revenueLY: 1100 + index * 300,
          transactions: 42 + index * 8,
          transactionsLY: 38 + index * 7,
        })),
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/executive-hourly-traffic",
    () => ({
      data: {
        items: [9, 10, 11, 12, 13].map((hour, index) => ({
          hour,
          revenue: 180 + index * 35,
          revenueLY: 160 + index * 30,
          transactions: 6 + index,
          transactionsLY: 5 + index,
        })),
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/branch-daily-performance",
    () => ({
      data: {
        items: IOS_REVIEW_STORES.map((store, index) => ({
          date: state().today,
          branchCode: store.storeCode,
          branchName: store.storeName,
          revenue: 1250 + index * 375,
          revenueLY: 1100 + index * 300,
          transactions: 42 + index * 8,
          transactionsLY: 38 + index * 7,
        })),
      },
    }),
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/dashboard\/(?:china-supplier-sales-rank|supplier-sales-rank)$/i,
    () => ({
      data: {
        items: [
          {
            supplierCode: "REV-SUP-001",
            supplierName: "Demo Supplier",
            totalAmount: 1250,
            compareTotalAmount: 1100,
            totalQuantity: 36,
            storeCount: 3,
            orderCount: 8,
            compareOrderCount: 7,
            averageTransaction: 156.25,
            compareAverageTransaction: 157.14,
          },
        ],
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/enhanced-sales-product-details",
    () => ({
      data: page(
        state().products.map((product, index) => ({
          ...product,
          quantity: 12 + index * 4,
          compareQuantity: 10 + index * 3,
          salesAmount: 240 + index * 80,
          compareSalesAmount: 210 + index * 70,
        })),
      ),
    }),
  );
  register(
    transport,
    ["GET"],
    /^\/react\/v1\/dashboard\/(?:china-supplier-store-sales|supplier-store-sales)$/i,
    () => ({
      data: {
        items: IOS_REVIEW_STORES.map((store, index) => ({
          branchCode: store.storeCode,
          branchName: store.storeName,
          supplierCode: "REV-SUP-001",
          supplierName: "Demo Supplier",
          totalAmount: 720 + index * 120,
          compareTotalAmount: 650 + index * 100,
          totalQuantity: 24 + index * 4,
          orderCount: 6 + index,
          compareOrderCount: 5 + index,
        })),
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/product-sales-by-branches",
    () => ({
      data: {
        items: IOS_REVIEW_STORES.map((store, index) => ({
          branchCode: store.storeCode,
          branchName: store.storeName,
          quantity: 12 + index,
          compareQuantity: 10 + index,
          salesAmount: 240 + index * 50,
          compareSalesAmount: 210 + index * 40,
        })),
      },
    }),
  );
  register(
    transport,
    ["GET"],
    "/react/v1/dashboard/statistics-freshness",
    () => ({
      data: {
        lastSuccessfulAtUtc: state().now,
        latestRunStatus: "Success",
      },
    }),
  );
}
