import { stripUrlQuery } from "./upload";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  stripUrlQuery("https://cdn.example.com/path/ad.jpg?X-Amz-Algorithm=AWS4-HMAC-SHA256&sig=123"),
  "https://cdn.example.com/path/ad.jpg",
  "signed upload URLs are normalized without query strings"
);
