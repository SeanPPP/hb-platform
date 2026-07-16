import type {
  AxiosAdapter,
  AxiosRequestConfig,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from "axios";
import {
  IOS_REVIEW_DOMAIN_NAMES,
  iosReviewDataStore,
  type IosReviewDomainName,
  type ReviewDataStore,
} from "./data-store";
import { registerIosReviewAppRoutes } from "./app-routes";

type ReviewHttpMethod = "GET" | "POST" | "PUT" | "PATCH" | "DELETE";

export interface ReviewTransportRequest {
  method: ReviewHttpMethod;
  path: string;
  query: URLSearchParams;
  body: unknown;
  config: AxiosRequestConfig;
  match: RegExpMatchArray | null;
}

export interface ReviewTransportResult {
  data: unknown;
  status?: number;
  headers?: Record<string, string>;
}

export interface ReviewTransportRoute {
  method: ReviewHttpMethod;
  path: string | RegExp;
  handle(request: ReviewTransportRequest):
    | ReviewTransportResult
    | Promise<ReviewTransportResult>;
}

export interface ReviewTransport {
  register(route: ReviewTransportRoute): void;
  dispatch(config: AxiosRequestConfig): Promise<ReviewTransportResult>;
}

export class IosReviewUnhandledRequestError extends Error {
  readonly code = "IOS_REVIEW_UNHANDLED_REQUEST";

  constructor(method: string, path: string) {
    super(`IOS_REVIEW_UNHANDLED_REQUEST: ${method.toUpperCase()} ${path}`);
    this.name = "IosReviewUnhandledRequestError";
  }
}

function parseRequestUrl(rawUrl: string) {
  const absoluteUrl = /^[a-z][a-z\d+.-]*:/i.test(rawUrl)
    ? new URL(rawUrl)
    : new URL(rawUrl, "https://ios-review.invalid");
  return absoluteUrl;
}

export function normalizeIosReviewRequestPath(rawUrl = "/") {
  const url = parseRequestUrl(rawUrl);
  const normalized = `/${url.pathname}`.replace(/\/{2,}/g, "/");
  const withoutApiPrefix = normalized.replace(/^\/api(?=\/|$)/i, "");
  if (!withoutApiPrefix || withoutApiPrefix === "/") return "/";
  return withoutApiPrefix.replace(/\/$/, "");
}

function parseBody(data: unknown) {
  if (typeof data !== "string") return data;
  try {
    return JSON.parse(data);
  } catch {
    return data;
  }
}

function buildQuery(url: URL, params: unknown) {
  const query = new URLSearchParams(url.searchParams);
  if (params instanceof URLSearchParams) {
    params.forEach((value, key) => query.append(key, value));
    return query;
  }
  if (!params || typeof params !== "object") return query;

  Object.entries(params as Record<string, unknown>).forEach(([key, value]) => {
    if (value === null || value === undefined) return;
    const values = Array.isArray(value) ? value : [value];
    values.forEach((item) => query.append(key, String(item)));
  });
  return query;
}

function isDomain(value: string): value is IosReviewDomainName {
  return (IOS_REVIEW_DOMAIN_NAMES as readonly string[]).includes(value);
}

export function createIosReviewTransport(
  dataStore: ReviewDataStore = iosReviewDataStore
): ReviewTransport {
  const routes: ReviewTransportRoute[] = [];

  const register = (route: ReviewTransportRoute) => routes.push(route);

  // 通用 CRUD 端点供页面适配层复用；未登记真实端点仍会明确失败。
  register({
    method: "GET",
    path: /^\/ios-review\/([A-Za-z-]+)$/,
    handle: ({ match }) => {
      const domain = match?.[1] ?? "";
      if (!isDomain(domain)) throw new Error(`IOS_REVIEW_UNKNOWN_DOMAIN: ${domain}`);
      return { data: dataStore.list(domain) };
    },
  });
  register({
    method: "POST",
    path: /^\/ios-review\/([A-Za-z-]+)$/,
    handle: ({ match, body }) => {
      const domain = match?.[1] ?? "";
      if (!isDomain(domain)) throw new Error(`IOS_REVIEW_UNKNOWN_DOMAIN: ${domain}`);
      return {
        data: dataStore.create(
          domain,
          typeof body === "object" && body !== null
            ? (body as Record<string, unknown>)
            : {}
        ),
        status: 201,
      };
    },
  });
  for (const method of ["PUT", "PATCH"] as const) {
    register({
      method,
      path: /^\/ios-review\/([A-Za-z-]+)\/([^/]+)$/,
      handle: ({ match, body }) => {
        const domain = match?.[1] ?? "";
        if (!isDomain(domain)) throw new Error(`IOS_REVIEW_UNKNOWN_DOMAIN: ${domain}`);
        return {
          data: dataStore.update(
            domain,
            decodeURIComponent(match?.[2] ?? ""),
            typeof body === "object" && body !== null
              ? (body as Record<string, unknown>)
              : {}
          ),
        };
      },
    });
  }
  register({
    method: "DELETE",
    path: /^\/ios-review\/([A-Za-z-]+)\/([^/]+)$/,
    handle: ({ match }) => {
      const domain = match?.[1] ?? "";
      if (!isDomain(domain)) throw new Error(`IOS_REVIEW_UNKNOWN_DOMAIN: ${domain}`);
      return {
        data: { success: dataStore.remove(domain, decodeURIComponent(match?.[2] ?? "")) },
      };
    },
  });

  const transport: ReviewTransport = {
    register,
    async dispatch(config) {
      const method = (config.method ?? "GET").toUpperCase() as ReviewHttpMethod;
      const rawUrl = config.url ?? "/";
      const path = normalizeIosReviewRequestPath(rawUrl);
      const url = parseRequestUrl(rawUrl);

      for (const route of routes) {
        if (route.method !== method) continue;
        const match =
          typeof route.path === "string"
            ? route.path === path
              ? null
              : undefined
            : path.match(route.path) ?? undefined;
        if (match === undefined) continue;
        return route.handle({
          method,
          path,
          query: buildQuery(url, config.params),
          body: parseBody(config.data),
          config,
          match,
        });
      }

      throw new IosReviewUnhandledRequestError(method, path);
    },
  };

  // 关键位置：默认 transport 注册真实 App 端点，审核请求未匹配时仍统一 fail-closed。
  registerIosReviewAppRoutes(transport, dataStore);
  return transport;
}

export function createIosReviewAxiosAdapter(
  transport: ReviewTransport = createIosReviewTransport()
): AxiosAdapter {
  return async (config): Promise<AxiosResponse> => {
    const result = await transport.dispatch(config);
    return {
      data: result.data,
      status: result.status ?? 200,
      statusText: result.status === 201 ? "Created" : "OK",
      headers: result.headers ?? {},
      config: config as InternalAxiosRequestConfig,
      request: { simulated: true },
    };
  };
}

export const iosReviewTransport = createIosReviewTransport();
export const iosReviewAxiosAdapter = createIosReviewAxiosAdapter(
  iosReviewTransport
);
