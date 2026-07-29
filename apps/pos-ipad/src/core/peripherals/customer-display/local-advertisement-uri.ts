export function normalizeAdvertisementCacheRootUri(value: string): string {
  const root = parseLocalFileUri(value, "advertisement cache root");
  if (root.path === "/") {
    throw new TypeError(
      "Customer display advertisement cache root is invalid.",
    );
  }
  return root.uri;
}

export function normalizeLocalAdvertisementUri(
  value: string,
  advertisementCacheRootUri: string,
): string {
  const root = parseLocalFileUri(
    advertisementCacheRootUri,
    "advertisement cache root",
  );
  const file = parseLocalFileUri(value, "local advertisement URI");
  if (
    file.host !== root.host ||
    file.path === root.path ||
    !file.path.startsWith(`${root.path}/`)
  ) {
    throw new TypeError(
      "Customer display local advertisement URI is invalid.",
    );
  }
  return file.uri;
}

function parseLocalFileUri(
  value: string,
  label: string,
): Readonly<{ host: string; path: string; uri: string }> {
  if (typeof value !== "string" || value.length > 2_048) {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  let parsed: URL;
  try {
    parsed = new URL(value);
  } catch {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  const host = parsed.hostname.toLowerCase();
  if (
    parsed.protocol !== "file:" ||
    (host !== "" && host !== "localhost") ||
    parsed.username ||
    parsed.password ||
    parsed.search ||
    parsed.hash
  ) {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  let decodedPath: string;
  try {
    decodedPath = decodeURIComponent(parsed.pathname);
  } catch {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  if (
    !decodedPath.startsWith("/") ||
    /[\u0000-\u001f\u007f]/u.test(decodedPath)
  ) {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  const segments = decodedPath.split("/").filter(Boolean);
  if (
    segments.length === 0 ||
    segments.some(
      (segment) =>
        segment === "." ||
        segment === ".." ||
        segment.includes("/") ||
        segment.includes("\\"),
    )
  ) {
    throw new TypeError(`Customer display ${label} is invalid.`);
  }
  const path = `/${segments.join("/")}`;
  return Object.freeze({
    host: host === "localhost" ? "" : host,
    path,
    uri: `file://${path}`,
  });
}
