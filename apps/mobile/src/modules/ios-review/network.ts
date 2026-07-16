import { isIosReviewSessionActive } from "./session";

const LOCAL_SCHEMES = new Set(["file:", "data:", "blob:", "about:"]);

function getRequestUrl(input: RequestInfo | URL) {
  if (typeof input === "string") return input;
  if (input instanceof URL) return input.toString();
  return input.url;
}

export async function reviewAwareFetch(
  input: RequestInfo | URL,
  init?: RequestInit,
  fetcher: typeof fetch = globalThis.fetch,
  reviewActive = isIosReviewSessionActive()
) {
  if (!reviewActive) return fetcher(input, init);

  const rawUrl = getRequestUrl(input);
  const scheme = rawUrl.match(/^([a-z][a-z\d+.-]*:)/i)?.[1].toLowerCase();
  if (!scheme || !LOCAL_SCHEMES.has(scheme)) {
    throw new Error(`IOS_REVIEW_NETWORK_BLOCKED: ${rawUrl}`);
  }

  return fetcher(input, init);
}

export function noOpIosReviewSideEffect<T>(result: T): Promise<T> {
  return Promise.resolve(result);
}
