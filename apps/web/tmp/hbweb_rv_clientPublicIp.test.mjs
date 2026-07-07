// src/utils/clientPublicIp.ts
var CLIENT_PUBLIC_IP_HEADER = "X-Client-Public-IP";
var CACHE_KEY = "hbweb:client-public-ipv4";
var CACHE_TTL_MS = 5 * 60 * 1e3;
var PUBLIC_IP_ENDPOINTS = [
  "https://api.ipify.org?format=json",
  "https://checkip.amazonaws.com"
];
function isPublicIpv4(value) {
  if (!value) {
    return false;
  }
  const parts = value.trim().split(".").map((part) => Number(part));
  if (parts.length !== 4 || parts.some((part) => !Number.isInteger(part) || part < 0 || part > 255)) {
    return false;
  }
  const [first, second] = parts;
  return !(first === 10 || first === 127 || first === 0 || first >= 224 || first === 169 && second === 254 || first === 172 && second >= 16 && second <= 31 || first === 192 && second === 168 || first === 192 && second === 0 && (parts[2] === 0 || parts[2] === 2) || first === 192 && second === 88 && parts[2] === 99 || first === 198 && (second === 18 || second === 19) || first === 198 && second === 51 && parts[2] === 100 || first === 203 && second === 0 && parts[2] === 113 || first === 100 && second >= 64 && second <= 127);
}
function readCachedPublicIp() {
  try {
    const cached = window.sessionStorage.getItem(CACHE_KEY);
    if (!cached) {
      return void 0;
    }
    const parsed = JSON.parse(cached);
    if (parsed.expiresAt > Date.now() && isPublicIpv4(parsed.ip)) {
      return parsed.ip;
    }
  } catch {
    return void 0;
  }
  return void 0;
}
function writeCachedPublicIp(ip) {
  try {
    window.sessionStorage.setItem(
      CACHE_KEY,
      JSON.stringify({ ip, expiresAt: Date.now() + CACHE_TTL_MS })
    );
  } catch {
  }
}
async function fetchWithTimeout(url) {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), 1500);
  try {
    return await fetch(url, {
      cache: "no-store",
      signal: controller.signal
    });
  } finally {
    window.clearTimeout(timeoutId);
  }
}
async function resolveClientPublicIpv4() {
  if (typeof window === "undefined") {
    return void 0;
  }
  const cachedIp = readCachedPublicIp();
  if (cachedIp) {
    return cachedIp;
  }
  for (const endpoint of PUBLIC_IP_ENDPOINTS) {
    try {
      const response = await fetchWithTimeout(endpoint);
      if (!response.ok) {
        continue;
      }
      const text = await response.text();
      const parsedIp = text.trim().startsWith("{") ? JSON.parse(text).ip : text.trim();
      if (typeof parsedIp === "string" && isPublicIpv4(parsedIp)) {
        writeCachedPublicIp(parsedIp);
        return parsedIp;
      }
    } catch {
    }
  }
  return void 0;
}
async function getClientPublicIpHeaders() {
  const ip = await resolveClientPublicIpv4();
  return ip ? { [CLIENT_PUBLIC_IP_HEADER]: ip } : {};
}

// src/utils/clientPublicIp.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var originalWindow = globalThis.window;
var originalFetch = globalThis.fetch;
var storage = /* @__PURE__ */ new Map();
Object.defineProperty(globalThis, "window", {
  configurable: true,
  value: {
    sessionStorage: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value)
    },
    setTimeout,
    clearTimeout
  }
});
globalThis.fetch = async () => new Response(JSON.stringify({ ip: "8.8.8.23" }), {
  status: 200,
  headers: { "Content-Type": "application/json" }
});
var headers = await getClientPublicIpHeaders();
assertEqual(headers["X-Client-Public-IP"], "8.8.8.23", "\u767B\u5F55\u8BF7\u6C42\u5E94\u5E26\u7528\u6237\u8BBE\u5907\u516C\u7F51 IPv4");
storage.clear();
globalThis.fetch = async () => new Response(JSON.stringify({ ip: "203.0.113.23" }), {
  status: 200,
  headers: { "Content-Type": "application/json" }
});
var reservedHeaders = await getClientPublicIpHeaders();
assertEqual(
  Object.keys(reservedHeaders).length,
  0,
  "\u4FDD\u7559/\u6587\u6863 IPv4 \u7F51\u6BB5\u4E0D\u5E94\u4F5C\u4E3A\u7528\u6237\u516C\u7F51 IP header"
);
storage.clear();
globalThis.fetch = async () => new Response("service unavailable", {
  status: 503,
  headers: { "Content-Type": "text/plain" }
});
var failedHeaders = await getClientPublicIpHeaders();
assertEqual(
  Object.keys(failedHeaders).length,
  0,
  "\u516C\u7F51 IP \u67E5\u8BE2\u5931\u8D25\u65F6\u5E94\u8FD4\u56DE\u7A7A headers \u4E14\u4E0D\u963B\u585E\u767B\u5F55"
);
globalThis.fetch = originalFetch;
Object.defineProperty(globalThis, "window", {
  configurable: true,
  value: originalWindow
});
