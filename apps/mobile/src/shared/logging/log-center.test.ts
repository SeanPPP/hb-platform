import {
  buildLogCenterEndpoint,
  isLogCenterIngestUrl,
  normalizeLogCenterConfig,
} from "./log-center";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const config = normalizeLogCenterConfig(
  {
    endpoint: "https://logs.example.com/api/system/logs/ingest",
    key: "test-log-key",
    environment: "production",
    serviceName: "HbwebExpoApp",
    projectCode: "HbwebExpo",
  },
  "https://hotbargain.vip/api"
);

assertEqual(config.enabled, true, "config is enabled when endpoint, key, and environment exist");
assertEqual(config.projectCode, "HbwebExpo", "config keeps project code from app config");
assertEqual(config.serviceName, "HbwebExpoApp", "config keeps service name from app config");
assertEqual(
  buildLogCenterEndpoint("https://hotbargain.vip/api"),
  "https://hotbargain.vip/api/system/logs/ingest",
  "ingest endpoint is derived from API base URL"
);
assertEqual(
  isLogCenterIngestUrl("https://hotbargain.vip/api/system/logs/ingest?batch=1"),
  true,
  "ingest requests are recognized even with query strings"
);
assertEqual(
  isLogCenterIngestUrl("https://hotbargain.vip/api/react/v1/product-warehouse/mobile/P001"),
  false,
  "business API requests are not treated as log-ingest requests"
);
