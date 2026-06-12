import {
  buildAdvertisementGridPayload,
  buildAdvertisementPayload,
  normalizeAdvertisementDetail,
  normalizeAdvertisementsResponse,
} from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

assertDeepEqual(
  buildAdvertisementGridPayload({}),
  {
    title: undefined,
    mediaType: undefined,
    storeCode: undefined,
    isEnabled: undefined,
    pageNumber: 1,
    pageSize: 20,
  },
  "default advertisement grid payload keeps pagination defaults"
);

assertDeepEqual(
  buildAdvertisementGridPayload({
    pageNumber: 2.8,
    pageSize: 50.2,
    title: "  Mid year  ",
    mediaType: "video",
    storeCode: " STO01 ",
    isEnabled: false,
  }),
  {
    title: "Mid year",
    mediaType: "video",
    storeCode: "STO01",
    isEnabled: false,
    pageNumber: 2,
    pageSize: 50,
  },
  "grid payload trims filters and normalizes pagination"
);

assertDeepEqual(
  buildAdvertisementPayload({
    title: "  Hero banner ",
    description: "  Store opening ",
    mediaType: "image",
    mediaUrl: " https://cdn.example.com/ad.jpg ",
    thumbnailUrl: " https://cdn.example.com/thumb.jpg ",
    objectKey: " ads/hero.jpg ",
    originalFileName: " hero.jpg ",
    contentType: " image/jpeg ",
    fileSize: 2048.9,
    effectiveStart: " 2026-06-01T00:00:00Z ",
    effectiveEnd: "2026-06-30T23:59:59Z",
    isEnabled: true,
    sortOrder: 7.6,
    stores: [{ storeCode: " STO01 " }, { storeCode: " " }, { storeCode: "STO02" }],
  }),
  {
    title: "Hero banner",
    description: "Store opening",
    mediaType: "image",
    mediaUrl: "https://cdn.example.com/ad.jpg",
    thumbnailUrl: "https://cdn.example.com/thumb.jpg",
    objectKey: "ads/hero.jpg",
    originalFileName: "hero.jpg",
    contentType: "image/jpeg",
    fileSize: 2048,
    effectiveStart: "2026-06-01T00:00:00Z",
    effectiveEnd: "2026-06-30T23:59:59Z",
    isEnabled: true,
    sortOrder: 7,
    stores: [{ storeCode: "STO01" }, { storeCode: "STO02" }],
  },
  "save payload trims fields and omits blank store scopes"
);

assertDeepEqual(
  buildAdvertisementPayload({
    title: "All stores",
    mediaType: "video",
    mediaUrl: "https://cdn.example.com/all.mp4",
    effectiveStart: "2026-06-01T00:00:00Z",
    effectiveEnd: "2026-06-30T23:59:59Z",
    isEnabled: true,
    stores: [],
  }),
  {
    title: "All stores",
    description: "",
    mediaType: "video",
    mediaUrl: "https://cdn.example.com/all.mp4",
    thumbnailUrl: "",
    objectKey: "",
    originalFileName: "",
    contentType: "",
    fileSize: 0,
    effectiveStart: "2026-06-01T00:00:00Z",
    effectiveEnd: "2026-06-30T23:59:59Z",
    isEnabled: true,
    sortOrder: 0,
    stores: [],
  },
  "empty store scopes are preserved for all-store advertisements"
);

const list = normalizeAdvertisementsResponse({
  success: true,
  data: {
    Items: [
      {
        AdvertisementId: 99,
        Title: "Video promo",
        Description: "EOFY video",
        MediaType: "video",
        MediaUrl: "https://cdn.example.com/ad.mp4",
        ThumbnailUrl: "https://cdn.example.com/ad.jpg",
        ObjectKey: "ads/video.mp4",
        OriginalFileName: "promo.mp4",
        ContentType: "video/mp4",
        FileSize: "4096",
        EffectiveStart: "2026-05-20T00:00:00Z",
        EffectiveEnd: "2026-05-27T00:00:00Z",
        IsEnabled: "1",
        SortOrder: "6",
        Stores: [{ StoreCode: "BNE01" }, { StoreCode: "SYD02" }],
      },
    ],
    TotalCount: "8",
    Page: "3",
    Limit: "100",
  },
});

assertEqual(list.items.length, 1, "list normalization keeps items");
assertEqual(list.items[0]?.id, "99", "list normalization coerces id to string");
assertEqual(list.items[0]?.mediaType, "video", "list normalization keeps media type");
assertEqual(list.items[0]?.fileSize, 4096, "list normalization coerces file size");
assertEqual(list.items[0]?.isEnabled, true, "list normalization coerces enabled flag");
assertEqual(list.items[0]?.stores[1]?.storeCode, "SYD02", "list normalization keeps store scopes");
assertEqual(list.total, 8, "list normalization keeps total count");
assertEqual(list.pageNumber, 3, "list normalization keeps page number");
assertEqual(list.pageSize, 100, "list normalization keeps page size");

const detail = normalizeAdvertisementDetail({
  item: {
    id: "ad-2",
    title: "Static banner",
    mediaType: "image",
    mediaUrl: "https://cdn.example.com/ad-2.jpg",
    isEnabled: 0,
    stores: [{ storeCode: "MEL01" }],
  },
});

assertEqual(detail?.id, "ad-2", "detail normalization unwraps item payload");
assertEqual(detail?.isEnabled, false, "detail normalization coerces disabled flag");
assertEqual(detail?.stores[0]?.storeCode, "MEL01", "detail normalization keeps nested stores");
