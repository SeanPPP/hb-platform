export const IOS_REVIEW_SAMPLE_BARCODE = "9330000000017";

export const IOS_REVIEW_LOCATION = {
  latitude: -27.4698,
  longitude: 153.0251,
  accuracy: 5,
} as const;

export const IOS_REVIEW_BANNER = {
  title: "App Review Demo / 审核演示",
  description:
    "Local sample data / 本地样例数据 · Resets on restart or sign-out / 重启或退出后重置",
  accessibilityLabel:
    "App Review Demo，审核演示。Local sample data，本地样例数据。Resets on restart or sign-out，重启或退出后重置。",
} as const;

export async function printIosReviewDocument(documentId: string) {
  return {
    success: true as const,
    simulated: true as const,
    documentId,
  };
}

export function createIosReviewExport(
  reportName: string,
  format: "csv" | "json" = "csv"
) {
  const content =
    format === "csv"
      ? "date,store,sales\n2026-07-16,REV001,1250.00\n"
      : JSON.stringify({ reportName, store: "REV001", sales: 1250 });
  const mimeType = format === "csv" ? "text/csv" : "application/json";
  return {
    fileName: `${reportName}.${format}`,
    mimeType,
    uri: `data:${mimeType};charset=utf-8,${encodeURIComponent(content)}`,
  };
}

export function createIosReviewImagePreview(fileName = "demo-product.jpg") {
  return {
    fileName,
    uri: "data:image/svg+xml;charset=utf-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%22640%22%20height%3D%22480%22%3E%3Crect%20width%3D%22100%25%22%20height%3D%22100%25%22%20fill%3D%22%23efefef%22%2F%3E%3Ctext%20x%3D%2250%25%22%20y%3D%2250%25%22%20text-anchor%3D%22middle%22%3EDemo%20Product%3C%2Ftext%3E%3C%2Fsvg%3E",
  };
}
