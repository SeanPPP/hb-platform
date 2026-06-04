import {
  API_PORT,
  API_HOST_PRESETS,
  DEFAULT_API_HOST,
  buildApiBaseUrl,
  normalizeApiHost,
} from "./config";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(DEFAULT_API_HOST, "hotbargain.vip", "default API host uses production domain");
assertEqual(API_PORT, "5002", "API port uses published backend port");
assertEqual(
  buildApiBaseUrl("hotbargain.vip"),
  "https://hotbargain.vip/api",
  "production API base URL uses HTTPS Nginx proxy and api path"
);
assertEqual(
  buildApiBaseUrl("192.168.31.247"),
  "http://192.168.31.247:5002/api",
  "local API base URL keeps direct backend port"
);
assertEqual(
  normalizeApiHost("http://192.168.31.247:5002/api"),
  "192.168.31.247",
  "normalization strips protocol, port, and path"
);
assertEqual(
  normalizeApiHost("https://hotbargain.vip/api"),
  "hotbargain.vip",
  "normalization keeps only the hostname for domains"
);
assertEqual(
  API_HOST_PRESETS.map((preset) => preset.host).join(","),
  "hotbargain.vip,192.168.31.247",
  "server presets include production first and local fallback second"
);
