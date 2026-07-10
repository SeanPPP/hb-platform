var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __esm = (fn, res) => function __init() {
  return fn && (res = (0, fn[__getOwnPropNames(fn)[0]])(fn = 0)), res;
};
var __commonJS = (cb, mod) => function __require() {
  return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));

// <define:import.meta.env>
var define_import_meta_env_default;
var init_define_import_meta_env = __esm({
  "<define:import.meta.env>"() {
    define_import_meta_env_default = {};
  }
});

// node_modules/.pnpm/dayjs@1.11.21/node_modules/dayjs/dayjs.min.js
var require_dayjs_min = __commonJS({
  "node_modules/.pnpm/dayjs@1.11.21/node_modules/dayjs/dayjs.min.js"(exports, module) {
    init_define_import_meta_env();
    !function(t, e) {
      "object" == typeof exports && "undefined" != typeof module ? module.exports = e() : "function" == typeof define && define.amd ? define(e) : (t = "undefined" != typeof globalThis ? globalThis : t || self).dayjs = e();
    }(exports, function() {
      "use strict";
      var t = 1e3, e = 6e4, n = 36e5, r = "millisecond", i = "second", s = "minute", u = "hour", a = "day", o = "week", c = "month", f = "quarter", h = "year", d = "date", l = "Invalid Date", $ = /^(\d{4})[-/]?(\d{1,2})?[-/]?(\d{0,2})[Tt\s]*(\d{1,2})?:?(\d{1,2})?:?(\d{1,2})?[.:]?(\d+)?$/, y = /\[([^\]]+)]|YYYY|YY|M{1,4}|D{1,2}|d{1,4}|H{1,2}|h{1,2}|a|A|m{1,2}|s{1,2}|Z{1,2}|SSS/g, M = { name: "en", weekdays: "Sunday_Monday_Tuesday_Wednesday_Thursday_Friday_Saturday".split("_"), months: "January_February_March_April_May_June_July_August_September_October_November_December".split("_"), ordinal: function(t2) {
        var e2 = ["th", "st", "nd", "rd"], n2 = t2 % 100;
        return "[" + t2 + (e2[(n2 - 20) % 10] || e2[n2] || e2[0]) + "]";
      } }, m = function(t2, e2, n2) {
        var r2 = String(t2);
        return !r2 || r2.length >= e2 ? t2 : "" + Array(e2 + 1 - r2.length).join(n2) + t2;
      }, v = { s: m, z: function(t2) {
        var e2 = -t2.utcOffset(), n2 = Math.abs(e2), r2 = Math.floor(n2 / 60), i2 = n2 % 60;
        return (e2 <= 0 ? "+" : "-") + m(r2, 2, "0") + ":" + m(i2, 2, "0");
      }, m: function t2(e2, n2) {
        if (e2.date() < n2.date()) return -t2(n2, e2);
        var r2 = 12 * (n2.year() - e2.year()) + (n2.month() - e2.month()), i2 = e2.clone().add(r2, c), s2 = n2 - i2 < 0, u2 = e2.clone().add(r2 + (s2 ? -1 : 1), c);
        return +(-(r2 + (n2 - i2) / (s2 ? i2 - u2 : u2 - i2)) || 0);
      }, a: function(t2) {
        return t2 < 0 ? Math.ceil(t2) || 0 : Math.floor(t2);
      }, p: function(t2) {
        return { M: c, y: h, w: o, d: a, D: d, h: u, m: s, s: i, ms: r, Q: f }[t2] || String(t2 || "").toLowerCase().replace(/s$/, "");
      }, u: function(t2) {
        return void 0 === t2;
      } }, g = "en", D = {};
      D[g] = M;
      var p = "$isDayjsObject", S = function(t2) {
        return t2 instanceof _ || !(!t2 || !t2[p]);
      }, w = function t2(e2, n2, r2) {
        var i2;
        if (!e2) return g;
        if ("string" == typeof e2) {
          var s2 = e2.toLowerCase();
          D[s2] && (i2 = s2), n2 && (D[s2] = n2, i2 = s2);
          var u2 = e2.split("-");
          if (!i2 && u2.length > 1) return t2(u2[0]);
        } else {
          var a2 = e2.name;
          D[a2] = e2, i2 = a2;
        }
        return !r2 && i2 && (g = i2), i2 || !r2 && g;
      }, O = function(t2, e2) {
        if (S(t2)) return t2.clone();
        var n2 = "object" == typeof e2 ? e2 : {};
        return n2.date = t2, n2.args = arguments, new _(n2);
      }, b = v;
      b.l = w, b.i = S, b.w = function(t2, e2) {
        return O(t2, { locale: e2.$L, utc: e2.$u, x: e2.$x, $offset: e2.$offset });
      };
      var _ = function() {
        function M2(t2) {
          this.$L = w(t2.locale, null, true), this.parse(t2), this.$x = this.$x || t2.x || {}, this[p] = true;
        }
        var m2 = M2.prototype;
        return m2.parse = function(t2) {
          this.$d = function(t3) {
            var e2 = t3.date, n2 = t3.utc;
            if (null === e2) return /* @__PURE__ */ new Date(NaN);
            if (b.u(e2)) return /* @__PURE__ */ new Date();
            if (e2 instanceof Date) return new Date(e2);
            if ("string" == typeof e2 && !/Z$/i.test(e2)) {
              var r2 = e2.match($);
              if (r2) {
                var i2 = r2[2] - 1 || 0, s2 = (r2[7] || "0").substring(0, 3);
                return n2 ? new Date(Date.UTC(r2[1], i2, r2[3] || 1, r2[4] || 0, r2[5] || 0, r2[6] || 0, s2)) : new Date(r2[1], i2, r2[3] || 1, r2[4] || 0, r2[5] || 0, r2[6] || 0, s2);
              }
            }
            return new Date(e2);
          }(t2), this.init();
        }, m2.init = function() {
          var t2 = this.$d;
          this.$y = t2.getFullYear(), this.$M = t2.getMonth(), this.$D = t2.getDate(), this.$W = t2.getDay(), this.$H = t2.getHours(), this.$m = t2.getMinutes(), this.$s = t2.getSeconds(), this.$ms = t2.getMilliseconds();
        }, m2.$utils = function() {
          return b;
        }, m2.isValid = function() {
          return !(this.$d.toString() === l);
        }, m2.isSame = function(t2, e2) {
          var n2 = O(t2);
          return this.startOf(e2) <= n2 && n2 <= this.endOf(e2);
        }, m2.isAfter = function(t2, e2) {
          return O(t2) < this.startOf(e2);
        }, m2.isBefore = function(t2, e2) {
          return this.endOf(e2) < O(t2);
        }, m2.$g = function(t2, e2, n2) {
          return b.u(t2) ? this[e2] : this.set(n2, t2);
        }, m2.unix = function() {
          return Math.floor(this.valueOf() / 1e3);
        }, m2.valueOf = function() {
          return this.$d.getTime();
        }, m2.startOf = function(t2, e2) {
          var n2 = this, r2 = !!b.u(e2) || e2, f2 = b.p(t2), l2 = function(t3, e3) {
            var i2 = b.w(n2.$u ? Date.UTC(n2.$y, e3, t3) : new Date(n2.$y, e3, t3), n2);
            return r2 ? i2 : i2.endOf(a);
          }, $2 = function(t3, e3) {
            return b.w(n2.toDate()[t3].apply(n2.toDate("s"), (r2 ? [0, 0, 0, 0] : [23, 59, 59, 999]).slice(e3)), n2);
          }, y2 = this.$W, M3 = this.$M, m3 = this.$D, v2 = "set" + (this.$u ? "UTC" : "");
          switch (f2) {
            case h:
              return r2 ? l2(1, 0) : l2(31, 11);
            case c:
              return r2 ? l2(1, M3) : l2(0, M3 + 1);
            case o:
              var g2 = this.$locale().weekStart || 0, D2 = (y2 < g2 ? y2 + 7 : y2) - g2;
              return l2(r2 ? m3 - D2 : m3 + (6 - D2), M3);
            case a:
            case d:
              return $2(v2 + "Hours", 0);
            case u:
              return $2(v2 + "Minutes", 1);
            case s:
              return $2(v2 + "Seconds", 2);
            case i:
              return $2(v2 + "Milliseconds", 3);
            default:
              return this.clone();
          }
        }, m2.endOf = function(t2) {
          return this.startOf(t2, false);
        }, m2.$set = function(t2, e2) {
          var n2, o2 = b.p(t2), f2 = "set" + (this.$u ? "UTC" : ""), l2 = (n2 = {}, n2[a] = f2 + "Date", n2[d] = f2 + "Date", n2[c] = f2 + "Month", n2[h] = f2 + "FullYear", n2[u] = f2 + "Hours", n2[s] = f2 + "Minutes", n2[i] = f2 + "Seconds", n2[r] = f2 + "Milliseconds", n2)[o2], $2 = o2 === a ? this.$D + (e2 - this.$W) : e2;
          if (o2 === c || o2 === h) {
            var y2 = this.clone().set(d, 1);
            y2.$d[l2]($2), y2.init(), this.$d = y2.set(d, Math.min(this.$D, y2.daysInMonth())).$d;
          } else l2 && this.$d[l2]($2);
          return this.init(), this;
        }, m2.set = function(t2, e2) {
          return this.clone().$set(t2, e2);
        }, m2.get = function(t2) {
          return this[b.p(t2)]();
        }, m2.add = function(r2, f2) {
          var d2, l2 = this;
          r2 = Number(r2);
          var $2 = b.p(f2), y2 = function(t2) {
            var e2 = O(l2);
            return b.w(e2.date(e2.date() + Math.round(t2 * r2)), l2);
          };
          if ($2 === c) return this.set(c, this.$M + r2);
          if ($2 === h) return this.set(h, this.$y + r2);
          if ($2 === a) return y2(1);
          if ($2 === o) return y2(7);
          var M3 = (d2 = {}, d2[s] = e, d2[u] = n, d2[i] = t, d2)[$2] || 1, m3 = this.$d.getTime() + r2 * M3;
          return b.w(m3, this);
        }, m2.subtract = function(t2, e2) {
          return this.add(-1 * t2, e2);
        }, m2.format = function(t2) {
          var e2 = this, n2 = this.$locale();
          if (!this.isValid()) return n2.invalidDate || l;
          var r2 = t2 || "YYYY-MM-DDTHH:mm:ssZ", i2 = b.z(this), s2 = this.$H, u2 = this.$m, a2 = this.$M, o2 = n2.weekdays, c2 = n2.months, f2 = n2.meridiem, h2 = function(t3, n3, i3, s3) {
            return t3 && (t3[n3] || t3(e2, r2)) || i3[n3].slice(0, s3);
          }, d2 = function(t3) {
            return b.s(s2 % 12 || 12, t3, "0");
          }, $2 = f2 || function(t3, e3, n3) {
            var r3 = t3 < 12 ? "AM" : "PM";
            return n3 ? r3.toLowerCase() : r3;
          };
          return r2.replace(y, function(t3, r3) {
            return r3 || function(t4) {
              switch (t4) {
                case "YY":
                  return String(e2.$y).slice(-2);
                case "YYYY":
                  return b.s(e2.$y, 4, "0");
                case "M":
                  return a2 + 1;
                case "MM":
                  return b.s(a2 + 1, 2, "0");
                case "MMM":
                  return h2(n2.monthsShort, a2, c2, 3);
                case "MMMM":
                  return h2(c2, a2);
                case "D":
                  return e2.$D;
                case "DD":
                  return b.s(e2.$D, 2, "0");
                case "d":
                  return String(e2.$W);
                case "dd":
                  return h2(n2.weekdaysMin, e2.$W, o2, 2);
                case "ddd":
                  return h2(n2.weekdaysShort, e2.$W, o2, 3);
                case "dddd":
                  return o2[e2.$W];
                case "H":
                  return String(s2);
                case "HH":
                  return b.s(s2, 2, "0");
                case "h":
                  return d2(1);
                case "hh":
                  return d2(2);
                case "a":
                  return $2(s2, u2, true);
                case "A":
                  return $2(s2, u2, false);
                case "m":
                  return String(u2);
                case "mm":
                  return b.s(u2, 2, "0");
                case "s":
                  return String(e2.$s);
                case "ss":
                  return b.s(e2.$s, 2, "0");
                case "SSS":
                  return b.s(e2.$ms, 3, "0");
                case "Z":
                  return i2;
              }
              return null;
            }(t3) || i2.replace(":", "");
          });
        }, m2.utcOffset = function() {
          return 15 * -Math.round(this.$d.getTimezoneOffset() / 15);
        }, m2.diff = function(r2, d2, l2) {
          var $2, y2 = this, M3 = b.p(d2), m3 = O(r2), v2 = (m3.utcOffset() - this.utcOffset()) * e, g2 = this - m3, D2 = function() {
            return b.m(y2, m3);
          };
          switch (M3) {
            case h:
              $2 = D2() / 12;
              break;
            case c:
              $2 = D2();
              break;
            case f:
              $2 = D2() / 3;
              break;
            case o:
              $2 = (g2 - v2) / 6048e5;
              break;
            case a:
              $2 = (g2 - v2) / 864e5;
              break;
            case u:
              $2 = g2 / n;
              break;
            case s:
              $2 = g2 / e;
              break;
            case i:
              $2 = g2 / t;
              break;
            default:
              $2 = g2;
          }
          return l2 ? $2 : b.a($2);
        }, m2.daysInMonth = function() {
          return this.endOf(c).$D;
        }, m2.$locale = function() {
          return D[this.$L];
        }, m2.locale = function(t2, e2) {
          if (!t2) return this.$L;
          var n2 = this.clone(), r2 = w(t2, e2, true);
          return r2 && (n2.$L = r2), n2;
        }, m2.clone = function() {
          return b.w(this.$d, this);
        }, m2.toDate = function() {
          return new Date(this.valueOf());
        }, m2.toJSON = function() {
          return this.isValid() ? this.toISOString() : null;
        }, m2.toISOString = function() {
          return this.$d.toISOString();
        }, m2.toString = function() {
          return this.$d.toUTCString();
        }, M2;
      }(), Y = _.prototype;
      return O.prototype = Y, [["$ms", r], ["$s", i], ["$m", s], ["$H", u], ["$W", a], ["$M", c], ["$y", h], ["$D", d]].forEach(function(t2) {
        Y[t2[1]] = function(e2) {
          return this.$g(e2, t2[0], t2[1]);
        };
      }), O.extend = function(t2, e2) {
        return t2.$i || (t2(e2, _, O), t2.$i = true), O;
      }, O.locale = w, O.isDayjs = S, O.unix = function(t2) {
        return O(1e3 * t2);
      }, O.en = D[g], O.Ls = D, O.p = {}, O;
    });
  }
});

// src/services/localSupplierInvoiceService.purchaseSalesAnalysis.test.ts
init_define_import_meta_env();
var import_dayjs2 = __toESM(require_dayjs_min(), 1);

// src/services/localSupplierInvoiceService.ts
init_define_import_meta_env();

// src/utils/request.ts
init_define_import_meta_env();

// src/utils/clientPublicIp.ts
init_define_import_meta_env();
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

// src/utils/centerLogClient.ts
init_define_import_meta_env();
var importMetaEnv = define_import_meta_env_default ?? {};
var API_BASE_URL = (importMetaEnv.VITE_API_BASE_URL || "").trim();
var CENTER_LOG_INGEST_PATH = "/api/system/logs/ingest";
var CENTER_LOG_PROJECT = (importMetaEnv.VITE_CENTER_LOG_PROJECT || "hbweb_rv").trim();
var CENTER_LOG_KEY = (importMetaEnv.VITE_CENTER_LOG_KEY || "").trim();
var CENTER_LOG_ENVIRONMENT = (importMetaEnv.VITE_CENTER_LOG_ENVIRONMENT || importMetaEnv.MODE || "development").trim();
var CENTER_LOG_SERVICE_NAME = (importMetaEnv.VITE_CENTER_LOG_SERVICE_NAME || "hbweb_rv-web").trim();
var CENTER_LOG_SOURCE_TYPE = "Web";
var MAX_MESSAGE_LENGTH = 2e3;
var MAX_STACK_LENGTH = 12e3;
var MAX_PROPERTY_LENGTH = 1e3;
function trimText(value, maxLength) {
  if (!value) {
    return void 0;
  }
  const normalized = value.trim();
  if (!normalized) {
    return void 0;
  }
  return normalized.length > maxLength ? `${normalized.slice(0, maxLength - 3)}...` : normalized;
}
function buildApiUrl(path) {
  return `${API_BASE_URL}${path}`.replace(/([^:]\/)\/+/g, "$1");
}
function getRequestPath(url, options) {
  if (!url) {
    return void 0;
  }
  try {
    const resolved = new URL(url, typeof window !== "undefined" ? window.location.origin : "http://localhost");
    return options?.stripQuery ? resolved.pathname : `${resolved.pathname}${resolved.search}`;
  } catch {
    return options?.stripQuery ? url.split("?")[0] : url;
  }
}
function sanitizeProperties(properties) {
  if (!properties) {
    return void 0;
  }
  const sanitizedEntries = [];
  Object.entries(properties).forEach(([key, value]) => {
    if (value === void 0 || value === null || value === "") {
      return;
    }
    if (typeof value === "string") {
      const trimmedValue = trimText(value, MAX_PROPERTY_LENGTH);
      if (trimmedValue) {
        sanitizedEntries.push([key, trimmedValue]);
      }
      return;
    }
    sanitizedEntries.push([key, value]);
  });
  return sanitizedEntries.length ? Object.fromEntries(sanitizedEntries) : void 0;
}
function summarizeResponsePayloadForLog(payload) {
  if (payload === void 0 || payload === null || payload === "") {
    return void 0;
  }
  if (typeof payload === "string") {
    return { message: trimText(payload, MAX_PROPERTY_LENGTH) };
  }
  if (typeof payload !== "object") {
    return { message: trimText(String(payload), MAX_PROPERTY_LENGTH) };
  }
  const raw = payload;
  const summary = {};
  ["success", "isSuccess", "message", "code", "errorCode"].forEach((key) => {
    const value = raw[key];
    if (typeof value === "boolean" || typeof value === "number") {
      summary[key] = value;
      return;
    }
    if (typeof value === "string") {
      const trimmed = trimText(value, MAX_PROPERTY_LENGTH);
      if (trimmed) {
        summary[key] = trimmed;
      }
    }
  });
  return Object.keys(summary).length ? summary : void 0;
}
function isCenterLogIngestRequest(url) {
  const requestPath = getRequestPath(url) || "";
  return requestPath.startsWith(CENTER_LOG_INGEST_PATH);
}
function isCenterLogConfigured() {
  return Boolean(CENTER_LOG_KEY);
}
function sendCenterLog(payload) {
  if (!isCenterLogConfigured()) {
    return;
  }
  const item = {
    ...payload,
    message: trimText(payload.message, MAX_MESSAGE_LENGTH) || "\u672A\u77E5\u9519\u8BEF",
    timestampUtc: payload.timestampUtc || (/* @__PURE__ */ new Date()).toISOString(),
    projectCode: CENTER_LOG_PROJECT,
    environment: CENTER_LOG_ENVIRONMENT,
    sourceType: CENTER_LOG_SOURCE_TYPE,
    serviceName: CENTER_LOG_SERVICE_NAME || void 0,
    exceptionMessage: trimText(payload.exceptionMessage, MAX_MESSAGE_LENGTH),
    stackTrace: trimText(payload.stackTrace, MAX_STACK_LENGTH),
    requestPath: trimText(payload.requestPath, MAX_PROPERTY_LENGTH),
    traceId: trimText(payload.traceId, MAX_PROPERTY_LENGTH),
    category: trimText(payload.category || payload.sourceType, MAX_PROPERTY_LENGTH),
    userId: trimText(payload.userId, MAX_PROPERTY_LENGTH),
    userName: trimText(payload.userName, MAX_PROPERTY_LENGTH),
    properties: sanitizeProperties(payload.properties)
  };
  void fetch(buildApiUrl(CENTER_LOG_INGEST_PATH), {
    method: "POST",
    credentials: "include",
    keepalive: true,
    headers: {
      "Content-Type": "application/json",
      "X-Log-Project": CENTER_LOG_PROJECT,
      "X-Log-Key": CENTER_LOG_KEY
    },
    body: JSON.stringify({ logs: [item] })
  }).catch(() => {
  });
}
function normalizeUnknownError(error) {
  if (error instanceof Error) {
    return {
      message: error.message,
      exceptionType: error.name,
      stackTrace: error.stack
    };
  }
  return {
    message: typeof error === "string" ? error : "\u672A\u77E5\u5F02\u5E38",
    exceptionType: typeof error,
    stackTrace: void 0
  };
}
function isAbortOrCanceledError(error) {
  if (typeof DOMException !== "undefined" && error instanceof DOMException && error.name === "AbortError") {
    return true;
  }
  if (error instanceof Error) {
    return error.name === "AbortError" || error.name === "CanceledError";
  }
  return false;
}
function reportRequestError(input) {
  if (isAbortOrCanceledError(input.error)) {
    return;
  }
  if (isCenterLogIngestRequest(input.url)) {
    return;
  }
  const normalizedError = normalizeUnknownError(input.error);
  sendCenterLog({
    level: input.statusCode && input.statusCode < 500 ? "Warning" : "Error",
    sourceType: "frontend-request",
    message: normalizedError.message,
    exceptionType: normalizedError.exceptionType,
    exceptionMessage: normalizedError.message,
    stackTrace: normalizedError.stackTrace,
    requestPath: getRequestPath(input.url),
    requestMethod: input.method,
    statusCode: input.statusCode,
    traceId: input.traceId,
    properties: {
      // 只记录失败摘要，避免把后端响应里的客户资料、token 等敏感字段写进前端日志。
      responsePayload: summarizeResponsePayloadForLog(input.responsePayload)
    }
  });
}

// src/utils/request.ts
var RequestError = class extends Error {
  status;
  payload;
  constructor(message, status, payload) {
    super(message);
    this.name = "RequestError";
    this.status = status;
    this.payload = payload;
  }
};
function buildQueryString(params) {
  if (!params) {
    return "";
  }
  const searchParams = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value === void 0 || value === null || value === "") {
      return;
    }
    if (Array.isArray(value)) {
      value.forEach((item) => {
        if (item !== void 0 && item !== null && item !== "") {
          searchParams.append(key, String(item));
        }
      });
      return;
    }
    searchParams.append(key, String(value));
  });
  const query = searchParams.toString();
  return query ? `?${query}` : "";
}
var API_BASE_URL2 = (define_import_meta_env_default?.VITE_API_BASE_URL || "").trim();
var LOGIN_PATH = "/login";
var AUTH_EXPIRED_EVENT = "hbweb:auth-expired";
var AUTH_WHITELIST = /* @__PURE__ */ new Set([
  "/api/Auth/session/login",
  "/api/Auth/session/logout",
  "/api/Auth/session/refresh"
]);
var authRedirecting = false;
var refreshPromise = null;
function buildRequestUrl(url, params) {
  const requestPath = url.startsWith("http://") || url.startsWith("https://") ? url : `${API_BASE_URL2}${url}`.replace(/([^:]\/)\/+/g, "$1");
  return `${requestPath}${buildQueryString(params)}`;
}
async function tryRefreshToken() {
  if (refreshPromise) {
    return refreshPromise;
  }
  refreshPromise = (async () => {
    try {
      const refreshUrl = buildRequestUrl("/api/Auth/session/refresh");
      const response = await fetch(refreshUrl, {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          ...await getClientPublicIpHeaders()
        },
        body: JSON.stringify({})
      });
      if (!response.ok) {
        return false;
      }
      const payload = await response.json();
      return !!(payload?.success ?? payload?.data);
    } catch {
      return false;
    } finally {
      refreshPromise = null;
    }
  })();
  return refreshPromise;
}
function handleUnauthorized(requestUrl2) {
  if (typeof window === "undefined" || authRedirecting) {
    return;
  }
  const currentPath = `${window.location.pathname}${window.location.search}`;
  const normalizedUrl = requestUrl2.replace(API_BASE_URL2, "");
  if (window.location.pathname === LOGIN_PATH || AUTH_WHITELIST.has(normalizedUrl)) {
    return;
  }
  authRedirecting = true;
  window.dispatchEvent(new Event(AUTH_EXPIRED_EVENT));
  window.location.replace(`${LOGIN_PATH}?redirect=${encodeURIComponent(currentPath)}`);
}
async function parseResponse(response) {
  const contentType = response.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    return await response.json();
  }
  return await response.text();
}
async function rawFetch(url, options = {}) {
  const { method = "GET", params, data, headers, signal } = options;
  const requestUrl2 = buildRequestUrl(url, params);
  const isFormDataBody = typeof FormData !== "undefined" && data instanceof FormData;
  const response = await fetch(requestUrl2, {
    method,
    credentials: "include",
    headers: {
      // FormData 必须交给浏览器/运行时自动补 multipart boundary，不能手动写 JSON 头。
      ...data && !isFormDataBody ? { "Content-Type": "application/json" } : {},
      ...headers
    },
    body: data ? isFormDataBody ? data : JSON.stringify(data) : void 0,
    signal
  });
  const payload = await parseResponse(response);
  return { response, payload };
}
async function request(url, options = {}) {
  const { skipAuthRedirect = false } = options;
  const normalizedUrl = url.replace(API_BASE_URL2, "");
  let response;
  let payload;
  try {
    const result = await rawFetch(url, options);
    response = result.response;
    payload = result.payload;
  } catch (error) {
    reportRequestError({
      url,
      method: options.method ?? "GET",
      error
    });
    throw error;
  }
  if (!response.ok) {
    if (response.status === 401 && !skipAuthRedirect && !AUTH_WHITELIST.has(normalizedUrl)) {
      const refreshed = await tryRefreshToken();
      if (refreshed) {
        const retryResult = await rawFetch(url, options);
        if (retryResult.response.ok) {
          return retryResult.payload;
        }
      }
      handleUnauthorized(url);
    }
    const message = typeof payload === "object" && payload !== null && "message" in payload && typeof payload.message === "string" ? payload.message : `\u8BF7\u6C42\u5931\u8D25 (${response.status})`;
    if (!isCenterLogIngestRequest(url)) {
      reportRequestError({
        url,
        method: options.method ?? "GET",
        statusCode: response.status,
        error: new RequestError(message, response.status, payload),
        responsePayload: payload,
        traceId: response.headers.get("x-trace-id") ?? response.headers.get("trace-id") ?? void 0
      });
    }
    throw new RequestError(message, response.status, payload);
  }
  return payload;
}
function unwrapApiData(payload) {
  if (payload && typeof payload === "object") {
    const response = payload;
    const success = response.success ?? response.isSuccess;
    if (success === false) {
      const code = response.code ?? response.errorCode;
      const message = response.message || "\u8BF7\u6C42\u5931\u8D25";
      throw new RequestError(code ? `${code}: ${message}` : message, 200, payload);
    }
    if ("data" in payload) {
      return response.data;
    }
  }
  return payload;
}
request.get = (url, options) => request(url, { ...options, method: "GET" });
request.post = (url, data, options) => request(url, { ...options, method: "POST", data });
request.put = (url, data, options) => request(url, { ...options, method: "PUT", data });
request.patch = (url, data, options) => request(url, { ...options, method: "PATCH", data });
request.delete = (url, options) => request(url, { ...options, method: "DELETE" });
var request_default = request;

// src/services/localSupplierInvoiceService.ts
var API_BASE = "/api/react/v1/local-supplier-invoices";
var PURCHASE_SALES_ANALYSIS_API_BASE = `${API_BASE}/purchase-sales-analysis`;
var PURCHASE_SALES_ANALYSIS_ALLOWED_PAGE_SIZES = /* @__PURE__ */ new Set([50, 100, 200]);
function readNumber(value, fallback = 0) {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}
function readOptionalNumber(value) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}
function readString(value) {
  return typeof value === "string" && value.trim() ? value : void 0;
}
function normalizePurchaseSalesAnalysisPageSize(value) {
  return typeof value === "number" && PURCHASE_SALES_ANALYSIS_ALLOWED_PAGE_SIZES.has(value) ? value : 100;
}
function normalizePurchaseSalesAnalysisRow(raw) {
  if (!raw || typeof raw !== "object") {
    return null;
  }
  const record = raw;
  const storeCode = readString(record.storeCode ?? record.StoreCode);
  const productCode = readString(record.productCode ?? record.ProductCode);
  const supplierCode = readString(record.supplierCode ?? record.SupplierCode);
  if (!storeCode || !productCode || !supplierCode) {
    return null;
  }
  return {
    storeCode,
    storeName: readString(record.storeName ?? record.StoreName),
    productCode,
    itemNumber: readString(record.itemNumber ?? record.ItemNumber),
    barcode: readString(record.barcode ?? record.Barcode),
    productName: readString(record.productName ?? record.ProductName),
    productImage: readString(record.productImage ?? record.ProductImage),
    supplierCode,
    supplierName: readString(record.supplierName ?? record.SupplierName),
    latestPurchaseDate: readString(record.latestPurchaseDate ?? record.LatestPurchaseDate) ?? null,
    latestPurchaseQty: readOptionalNumber(record.latestPurchaseQty ?? record.LatestPurchaseQty),
    previousPurchaseDate: readString(record.previousPurchaseDate ?? record.PreviousPurchaseDate) ?? null,
    previousPurchaseQty: readOptionalNumber(record.previousPurchaseQty ?? record.PreviousPurchaseQty),
    purchaseIntervalDays: readOptionalNumber(record.purchaseIntervalDays ?? record.PurchaseIntervalDays),
    salesBetweenPurchases: readOptionalNumber(record.salesBetweenPurchases ?? record.SalesBetweenPurchases),
    salesQty30: readNumber(record.salesQty30 ?? record.SalesQty30),
    salesQty60: readNumber(record.salesQty60 ?? record.SalesQty60),
    salesQty90: readNumber(record.salesQty90 ?? record.SalesQty90),
    salesStatisticLastUpdate: readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null
  };
}
function normalizePurchaseSalesAnalysisResponse(raw) {
  const record = raw && typeof raw === "object" ? raw : {};
  const items = Array.isArray(record.items ?? record.Items) ? (record.items ?? record.Items).map(normalizePurchaseSalesAnalysisRow).filter((item) => item !== null) : [];
  return {
    items,
    total: readNumber(record.total ?? record.Total),
    page: readNumber(record.page ?? record.Page, 1),
    pageSize: normalizePurchaseSalesAnalysisPageSize(record.pageSize ?? record.PageSize),
    salesStatisticLastUpdate: readString(record.salesStatisticLastUpdate ?? record.SalesStatisticLastUpdate) ?? null,
    calculationNote: readString(record.calculationNote ?? record.CalculationNote) ?? "\u8FDB\u8D27\u6309\u8BA2\u5355\u65E5\u671F\u8303\u56F4\u8FC7\u6EE4\u3001\u6309\u8FDB\u8D27\u53D1\u751F\u65E5\u671F\u6C47\u603B\uFF1B\u6700\u8FD1\u4E00\u6B21\u540E\u768430/60/90\u5929\u9500\u91CF\u4ECE\u6700\u8FD1\u8FDB\u8D27\u5F53\u5929\u5F00\u59CB\u7EDF\u8BA1\u3002"
  };
}
function normalizePurchaseSalesAnalysisStoreOptions(raw) {
  const items = Array.isArray(raw) ? raw : Array.isArray(raw?.data) ? raw.data : [];
  return items.map((item) => {
    if (!item || typeof item !== "object") {
      return null;
    }
    const record = item;
    const label = readString(record.label ?? record.Label);
    const value = readString(record.value ?? record.Value);
    if (!label || !value) {
      return null;
    }
    return { label, value };
  }).filter((item) => item !== null);
}
function normalizePurchaseSalesAnalysisSupplierOptions(raw) {
  return normalizePurchaseSalesAnalysisStoreOptions(raw);
}
function buildPurchaseSalesAnalysisQuery(query) {
  return {
    ...query,
    page: typeof query.page === "number" && query.page > 0 ? query.page : 1,
    pageSize: normalizePurchaseSalesAnalysisPageSize(query.pageSize)
  };
}
async function getLocalSupplierPurchaseSalesAnalysis(query, signal) {
  const response = await request_default.get(PURCHASE_SALES_ANALYSIS_API_BASE, {
    params: buildPurchaseSalesAnalysisQuery(query),
    signal
  });
  return normalizePurchaseSalesAnalysisResponse(unwrapApiData(response));
}
async function getLocalSupplierPurchaseSalesAnalysisSupplierOptions(storeCode) {
  const response = await request_default.get(`${PURCHASE_SALES_ANALYSIS_API_BASE}/supplier-options`, {
    params: storeCode ? { storeCode } : void 0
  });
  return normalizePurchaseSalesAnalysisSupplierOptions(unwrapApiData(response));
}
var __localSupplierInvoiceServiceTestOnly = {
  buildPurchaseSalesAnalysisQuery,
  normalizePurchaseSalesAnalysisResponse,
  normalizePurchaseSalesAnalysisStoreOptions,
  normalizePurchaseSalesAnalysisSupplierOptions,
  normalizePurchaseSalesAnalysisPageSize
};

// src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/helpers.ts
init_define_import_meta_env();
var import_dayjs = __toESM(require_dayjs_min(), 1);
var TRANSPARENT_IMAGE_FALLBACK = "data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=";
var DEFAULT_PRODUCT_IMAGE_BASE_URL = "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200";
var DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE = 100;
var PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS = [50, 100, 200];
var PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY = "latestPurchaseDate";
var PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER = "desc";
var PURCHASE_SALES_ANALYSIS_SORT_FIELDS = [
  "itemNumber",
  "productName",
  "latestPurchaseDate",
  "previousPurchaseDate",
  "purchaseIntervalDays",
  "salesBetweenPurchases",
  "salesQty30",
  "salesQty60",
  "salesQty90"
];
function getDefaultPurchaseSalesAnalysisDateRange(referenceDate = (0, import_dayjs.default)()) {
  return [referenceDate.subtract(180, "day"), referenceDate];
}
function normalizePurchaseSalesAnalysisPageSize2(value) {
  return PURCHASE_SALES_ANALYSIS_PAGE_SIZE_OPTIONS.includes(
    value
  ) ? value : DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE;
}
function toPurchaseSalesAnalysisSort(sortBy, sorterOrder) {
  const isAllowedSortField = PURCHASE_SALES_ANALYSIS_SORT_FIELDS.includes(
    sortBy
  );
  if (!sortBy || !sorterOrder || !isAllowedSortField) {
    return {
      sortBy: PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_BY,
      sortOrder: PURCHASE_SALES_ANALYSIS_DEFAULT_SORT_ORDER
    };
  }
  return {
    sortBy,
    sortOrder: sorterOrder === "ascend" ? "asc" : "desc"
  };
}
function buildDefaultProductImageUrl(itemNumber, productCode) {
  const imageKey = String(itemNumber || productCode || "").trim();
  return imageKey ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(imageKey)}.jpg` : "";
}
function buildPurchaseSalesAnalysisImageSourceChain(productImage, itemNumber, productCode) {
  const chain = [
    String(productImage || "").trim(),
    buildDefaultProductImageUrl(itemNumber, productCode),
    TRANSPARENT_IMAGE_FALLBACK
  ].filter(Boolean);
  return Array.from(new Set(chain));
}

// src/services/localSupplierInvoiceService.purchaseSalesAnalysis.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var originalFetch = globalThis.fetch;
var requestUrl = "";
var requestMethod = "";
var supplierOptionsRequestUrl = "";
var refreshRequestCount = 0;
globalThis.fetch = async (input, init) => {
  const url = String(input);
  if (url.startsWith("https://api.ipify.org")) {
    return new Response(JSON.stringify({ ip: "8.8.8.88" }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  if (url.endsWith("/api/Auth/session/refresh")) {
    refreshRequestCount += 1;
    return new Response(JSON.stringify({ success: true, data: {} }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  if (url.includes("/api/react/v1/local-supplier-invoices/purchase-sales-analysis/supplier-options")) {
    supplierOptionsRequestUrl = url;
    return new Response(JSON.stringify({
      success: true,
      data: [
        { Label: "Malmar", Value: "200" },
        { Label: "", Value: "BROKEN" }
      ]
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  if (url.includes("/api/react/v1/local-supplier-invoices/purchase-sales-analysis")) {
    requestUrl = url;
    requestMethod = String(init?.method || "GET");
    return new Response(JSON.stringify({
      success: true,
      data: {
        Items: [
          {
            StoreCode: "S001",
            StoreName: "Sydney",
            ProductCode: "P001",
            ItemNumber: "HB001",
            Barcode: "9350001",
            ProductName: "\u82F9\u679C",
            ProductImage: "https://example.com/a.jpg",
            SupplierCode: "SUP01",
            SupplierName: "\u4F9B\u5E94\u5546 A",
            LatestPurchaseDate: "2026-06-01",
            LatestPurchaseQty: 12.5,
            PreviousPurchaseDate: "2026-05-12",
            PreviousPurchaseQty: 9,
            PurchaseIntervalDays: 20,
            SalesBetweenPurchases: 18,
            SalesQty30: 20,
            SalesQty60: 30,
            SalesQty90: 40,
            SalesStatisticLastUpdate: "2026-06-25T08:00:00"
          },
          {
            ProductCode: "BROKEN"
          }
        ],
        Total: 1,
        Page: 2,
        PageSize: 999,
        SalesStatisticLastUpdate: "2026-06-25T09:00:00"
      }
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" }
    });
  }
  return new Response(JSON.stringify({ success: true, data: null }), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
};
try {
  const defaultRange = getDefaultPurchaseSalesAnalysisDateRange((0, import_dayjs2.default)("2026-06-25"));
  assertEqual(defaultRange[0].format("YYYY-MM-DD"), "2025-12-27", "\u9ED8\u8BA4\u5F00\u59CB\u65E5\u671F\u5E94\u4E3A 180 \u5929\u524D");
  assertEqual(defaultRange[1].format("YYYY-MM-DD"), "2026-06-25", "\u9ED8\u8BA4\u7ED3\u675F\u65E5\u671F\u5E94\u4E3A\u5F53\u5929");
  assertEqual(
    normalizePurchaseSalesAnalysisPageSize2(void 0),
    DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE,
    "\u5206\u9875\u9ED8\u8BA4\u503C\u5E94\u4E3A 100"
  );
  assertEqual(normalizePurchaseSalesAnalysisPageSize2(50), 50, "\u5141\u8BB8\u7684\u5206\u9875\u503C\u5E94\u4FDD\u7559");
  assertEqual(
    normalizePurchaseSalesAnalysisPageSize2(80),
    DEFAULT_PURCHASE_SALES_ANALYSIS_PAGE_SIZE,
    "\u4E0D\u5141\u8BB8\u7684\u5206\u9875\u503C\u5E94\u56DE\u9000\u5230 100"
  );
  assertDeepEqual(
    toPurchaseSalesAnalysisSort("salesQty90", "descend"),
    { sortBy: "salesQty90", sortOrder: "desc" },
    "\u6392\u5E8F\u5668\u5E94\u8F6C\u6362\u4E3A\u540E\u7AEF\u9700\u8981\u7684 sortBy \u548C sortOrder"
  );
  assertDeepEqual(
    toPurchaseSalesAnalysisSort("supplierName", "ascend"),
    { sortBy: "latestPurchaseDate", sortOrder: "desc" },
    "\u4E0D\u5728\u540E\u7AEF\u767D\u540D\u5355\u5185\u7684\u6392\u5E8F\u5B57\u6BB5\u5E94\u56DE\u9000\u9ED8\u8BA4\u6392\u5E8F"
  );
  const normalized = __localSupplierInvoiceServiceTestOnly.normalizePurchaseSalesAnalysisResponse({
    Items: [
      {
        StoreCode: "S001",
        ProductCode: "P001",
        SupplierCode: "SUP01",
        SalesQty30: 1,
        SalesQty60: 2,
        SalesQty90: 3
      },
      {
        StoreCode: "S002"
      }
    ],
    Total: 9,
    Page: 3,
    PageSize: 70
  });
  assertEqual(normalized.items.length, 1, "normalizer \u5E94\u8FC7\u6EE4\u6389\u7F3A\u5C11\u5173\u952E\u5B57\u6BB5\u7684\u884C");
  assertEqual(normalized.total, 9, "normalizer \u5E94\u4FDD\u7559\u603B\u6570");
  assertEqual(normalized.pageSize, 100, "normalizer \u5E94\u628A\u975E\u6CD5 pageSize \u5F52\u4E00\u5316\u4E3A 100");
  const normalizedSupplierOptions = __localSupplierInvoiceServiceTestOnly.normalizePurchaseSalesAnalysisSupplierOptions({
    data: [
      { Label: "Malmar", Value: "200" },
      { Label: "", Value: "BROKEN" }
    ]
  });
  assertDeepEqual(
    normalizedSupplierOptions,
    [{ label: "Malmar", value: "200" }],
    "\u4F9B\u5E94\u5546\u9009\u9879 normalizer \u5E94\u8FC7\u6EE4\u7F3A\u5C11 label \u6216 value \u7684\u9879"
  );
  const supplierOptions = await getLocalSupplierPurchaseSalesAnalysisSupplierOptions("S001");
  const parsedSupplierOptionsUrl = new URL(supplierOptionsRequestUrl, "https://example.test");
  assertEqual(
    parsedSupplierOptionsUrl.searchParams.get("storeCode"),
    "S001",
    "\u4F9B\u5E94\u5546\u9009\u9879\u63A5\u53E3\u5E94\u6309\u5DF2\u9009\u5206\u5E97\u4F20\u53C2"
  );
  assertDeepEqual(supplierOptions, [{ label: "Malmar", value: "200" }], "\u4F9B\u5E94\u5546\u9009\u9879\u63A5\u53E3\u5E94\u5F52\u4E00\u5316\u54CD\u5E94");
  const result = await getLocalSupplierPurchaseSalesAnalysis({
    storeCode: "S001",
    supplierCode: "SUP01",
    orderDateStart: "2026-01-01",
    orderDateEnd: "2026-06-25",
    keyword: "\u82F9\u679C",
    sortBy: "salesQty60",
    sortOrder: "desc",
    page: 2,
    pageSize: 200
  });
  const parsedUrl = new URL(requestUrl, "https://example.test");
  assertEqual(requestMethod, "GET", "\u67E5\u8BE2\u63A5\u53E3\u5E94\u4F7F\u7528 GET");
  assertEqual(parsedUrl.searchParams.get("sortBy"), "salesQty60", "\u6392\u5E8F\u5B57\u6BB5\u5E94\u900F\u4F20\u5230\u540E\u7AEF");
  assertEqual(parsedUrl.searchParams.get("sortOrder"), "desc", "\u6392\u5E8F\u65B9\u5411\u5E94\u900F\u4F20\u5230\u540E\u7AEF");
  assertEqual(parsedUrl.searchParams.get("pageSize"), "200", "\u5408\u6CD5 pageSize \u5E94\u900F\u4F20\u5230\u540E\u7AEF");
  assertEqual(result.items.length, 1, "\u63A5\u53E3 normalizer \u5E94\u8FC7\u6EE4\u65E0\u6548\u884C");
  assertEqual(result.pageSize, 100, "\u63A5\u53E3\u54CD\u5E94\u4E2D\u7684\u975E\u6CD5 pageSize \u5E94\u56DE\u9000\u5230 100");
  const imageChainWithFallback = buildPurchaseSalesAnalysisImageSourceChain(
    "https://img.example.com/a.jpg",
    "HB001",
    "P001"
  );
  assertDeepEqual(
    imageChainWithFallback,
    [
      "https://img.example.com/a.jpg",
      `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/HB001.jpg`,
      TRANSPARENT_IMAGE_FALLBACK
    ],
    "\u56FE\u7247\u515C\u5E95\u94FE\u8DEF\u5E94\u5305\u542B\u539F\u56FE\u3001\u9ED8\u8BA4 COS \u56FE\u548C\u900F\u660E\u56FE"
  );
  const imageChainWithoutPrimary = buildPurchaseSalesAnalysisImageSourceChain(void 0, "", "");
  assertDeepEqual(
    imageChainWithoutPrimary,
    [TRANSPARENT_IMAGE_FALLBACK],
    "\u7F3A\u5C11\u8D27\u53F7\u548C\u4E3B\u56FE\u65F6\u5E94\u76F4\u63A5\u9000\u56DE\u900F\u660E\u56FE"
  );
  assertEqual(refreshRequestCount, 0, "\u6B63\u5E38\u6D4B\u8BD5\u4E0D\u5E94\u89E6\u53D1\u5237\u65B0\u4EE4\u724C\u8BF7\u6C42");
} finally {
  globalThis.fetch = originalFetch;
}
