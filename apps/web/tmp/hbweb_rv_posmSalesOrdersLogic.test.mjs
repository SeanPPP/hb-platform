var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
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

// node_modules/.pnpm/dayjs@1.11.21/node_modules/dayjs/dayjs.min.js
var require_dayjs_min = __commonJS({
  "node_modules/.pnpm/dayjs@1.11.21/node_modules/dayjs/dayjs.min.js"(exports, module) {
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

// src/pages/PosmSalesOrders/posmSalesOrdersLogic.ts
var SORT_FIELD_BY_COLUMN = {
  orderGuid: "orderGuid",
  branchCode: "branchCode",
  branchName: "branchCode",
  deviceCode: "deviceCode",
  date: "orderTime",
  time: "orderTime",
  orderTime: "orderTime",
  skuCount: "skuCount",
  itemCount: "itemCount",
  totalAmount: "totalAmount",
  discountAmount: "discountAmount",
  actualAmount: "actualPay",
  actualPay: "actualPay"
};
var NUMBER_RANGES = [
  { name: "skuCount", min: "skuCountMin", max: "skuCountMax" },
  { name: "itemCount", min: "itemCountMin", max: "itemCountMax" },
  { name: "totalAmount", min: "totalAmountMin", max: "totalAmountMax" },
  { name: "discountAmount", min: "discountAmountMin", max: "discountAmountMax" },
  { name: "actualPay", min: "actualPayMin", max: "actualPayMax" }
];
function trimOrUndefined(value) {
  const normalized = value?.trim();
  return normalized || void 0;
}
function resolvePosmSalesOrderClientUtcOffsetMinutes(startDate, fallbackOffsetMinutes) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(startDate?.trim() ?? "");
  if (!match) return fallbackOffsetMinutes;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1) return fallbackOffsetMinutes;
  const localMidnight = /* @__PURE__ */ new Date(0);
  localMidnight.setFullYear(year, month - 1, day);
  localMidnight.setHours(0, 0, 0, 0);
  if (localMidnight.getFullYear() !== year || localMidnight.getMonth() !== month - 1 || localMidnight.getDate() !== day) {
    return fallbackOffsetMinutes;
  }
  return -localMidnight.getTimezoneOffset();
}
function createPosmSalesOrderColumnFilterDraft(applied, changes = {}) {
  return { ...applied, ...changes };
}
function normalizePosmSalesOrderFilterNumber(value, integer) {
  if (value === null || value === void 0 || value === "") return void 0;
  const normalized = Number(value);
  if (!Number.isFinite(normalized)) return void 0;
  return integer ? Math.round(normalized) : normalized;
}
function mapPosmSalesOrderSortState(columnKey, order) {
  const field = SORT_FIELD_BY_COLUMN[String(columnKey ?? "")] ?? "orderTime";
  return { field, direction: order === "descend" ? "desc" : "asc" };
}
function validatePosmSalesOrderNumberRanges(filters) {
  for (const range of NUMBER_RANGES) {
    const min = filters[range.min];
    const max = filters[range.max];
    if (typeof min === "number" && typeof max === "number" && min > max) {
      return { isValid: false, range: range.name };
    }
  }
  return { isValid: true };
}
function syncTopFiltersToColumnFilters(current, top) {
  return { ...current, startDate: top.startDate, endDate: top.endDate, branchCode: top.branchCode };
}
function syncColumnFiltersToTopFilters(columns) {
  return {
    startDate: columns.startDate ?? "",
    endDate: columns.endDate ?? "",
    branchCode: columns.branchCode ?? ""
  };
}
function createResetPosmSalesOrderState(today, pageSize) {
  return {
    startDate: today,
    endDate: today,
    branchCode: "",
    orderType: -1 /* All */,
    keyword: "",
    page: 1,
    pageSize,
    columnFilters: {
      startDate: today,
      endDate: today,
      branchCode: "",
      orderGuidKeyword: void 0,
      deviceCodeKeyword: void 0,
      timeStart: void 0,
      timeEnd: void 0,
      skuCountMin: void 0,
      skuCountMax: void 0,
      itemCountMin: void 0,
      itemCountMax: void 0,
      totalAmountMin: void 0,
      totalAmountMax: void 0,
      discountAmountMin: void 0,
      discountAmountMax: void 0,
      actualPayMin: void 0,
      actualPayMax: void 0
    },
    sort: { field: "orderTime", direction: "asc" }
  };
}
function applyPosmSalesOrderQueryChange(state, changes) {
  return {
    ...state,
    ...changes,
    columnFilters: { ...state.columnFilters, ...changes.columnFilters },
    sort: changes.sort ?? state.sort,
    page: 1
  };
}
function applyPosmSalesOrderTopFilterDraft(state, topDraft) {
  return applyPosmSalesOrderQueryChange(state, {
    ...topDraft,
    columnFilters: syncTopFiltersToColumnFilters(state.columnFilters, topDraft)
  });
}
function applyPosmSalesOrderColumnFilterDraft(state, columnDraft) {
  return applyPosmSalesOrderQueryChange(state, {
    ...syncColumnFiltersToTopFilters(columnDraft),
    columnFilters: columnDraft
  });
}
function isLatestPosmSalesOrderRequest(requestId, latestRequestId2) {
  return requestId === latestRequestId2;
}
function buildPosmSalesOrderListQuery(state, overrides = {}) {
  const nextState = {
    ...state,
    ...overrides,
    columnFilters: { ...state.columnFilters, ...overrides.columnFilters },
    sort: overrides.sort ?? state.sort
  };
  const filters = nextState.columnFilters;
  const startDate = trimOrUndefined(nextState.startDate);
  return {
    startDate,
    endDate: trimOrUndefined(nextState.endDate),
    branchCode: trimOrUndefined(nextState.branchCode),
    orderType: nextState.orderType,
    keyword: trimOrUndefined(nextState.keyword),
    orderGuidKeyword: trimOrUndefined(filters.orderGuidKeyword),
    deviceCodeKeyword: trimOrUndefined(filters.deviceCodeKeyword),
    timeStart: filters.timeStart,
    timeEnd: filters.timeEnd,
    clientUtcOffsetMinutes: resolvePosmSalesOrderClientUtcOffsetMinutes(
      startDate,
      -(/* @__PURE__ */ new Date()).getTimezoneOffset()
    ),
    skuCountMin: filters.skuCountMin,
    skuCountMax: filters.skuCountMax,
    itemCountMin: filters.itemCountMin,
    itemCountMax: filters.itemCountMax,
    totalAmountMin: filters.totalAmountMin,
    totalAmountMax: filters.totalAmountMax,
    discountAmountMin: filters.discountAmountMin,
    discountAmountMax: filters.discountAmountMax,
    actualPayMin: filters.actualPayMin,
    actualPayMax: filters.actualPayMax,
    sortField: nextState.sort.field,
    sortDirection: nextState.sort.direction,
    pageNumber: nextState.page,
    pageSize: nextState.pageSize
  };
}

// src/pages/PosmSalesOrders/time.ts
var import_dayjs = __toESM(require_dayjs_min(), 1);
var timezoneSuffixPattern = /(Z|[+-]\d{2}:?\d{2})$/i;
var dotNetIsoTimestampPattern = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-]\d{2}:?\d{2})?$/i;
function isLeapYear(year) {
  return year % 400 === 0 || year % 4 === 0 && year % 100 !== 0;
}
function isValidPosmSalesOrderTimestamp(value) {
  const match = dotNetIsoTimestampPattern.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  const daysInMonth = [
    31,
    isLeapYear(year) ? 29 : 28,
    31,
    30,
    31,
    30,
    31,
    31,
    30,
    31,
    30,
    31
  ];
  if (year < 1 || month < 1 || month > 12 || day < 1 || day > daysInMonth[month - 1] || hour > 23 || minute > 59 || second > 59) {
    return false;
  }
  const timezoneSuffix = match[7];
  if (timezoneSuffix && timezoneSuffix.toUpperCase() !== "Z") {
    const offsetDigits = timezoneSuffix.slice(1).replace(":", "");
    const offsetHours = Number(offsetDigits.slice(0, 2));
    const offsetMinutes = Number(offsetDigits.slice(2, 4));
    if (offsetHours > 14 || offsetMinutes > 59 || offsetHours === 14 && offsetMinutes !== 0) {
      return false;
    }
  }
  return true;
}
function normalizePosmSalesOrderUtcTime(value) {
  const trimmed = value.trim();
  if (timezoneSuffixPattern.test(trimmed)) {
    return trimmed;
  }
  return `${trimmed}Z`;
}
function formatPosmSalesOrderLocalTime(value, format) {
  if (!value?.trim()) {
    return "-";
  }
  const trimmed = value.trim();
  if (!isValidPosmSalesOrderTimestamp(trimmed)) {
    return value;
  }
  const parsed = (0, import_dayjs.default)(normalizePosmSalesOrderUtcTime(trimmed));
  return parsed.isValid() ? parsed.format(format) : value;
}

// src/pages/PosmSalesOrders/posmSalesOrdersLogic.test.ts
process.env.TZ = "Australia/Brisbane";
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function assert(condition, label) {
  if (!condition) throw new Error(label);
}
var currentState = {
  startDate: "2026-06-10",
  endDate: "2026-06-11",
  branchCode: "S01",
  orderType: 1 /* Paid */,
  keyword: "  invoice  ",
  page: 3,
  pageSize: 50,
  columnFilters: {
    orderGuidKeyword: "  ORDER-01  ",
    deviceCodeKeyword: "  POS-2  ",
    timeStart: "08:30:00",
    timeEnd: "18:15:59",
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180
  },
  sort: { field: "actualPay", direction: "desc" }
};
var appliedState = {
  ...currentState,
  startDate: "2026-06-10",
  endDate: "2026-06-11",
  branchCode: "S01",
  columnFilters: {
    ...currentState.columnFilters,
    startDate: "2026-06-10",
    endDate: "2026-06-11",
    branchCode: "S01"
  }
};
var pendingTopDraft = {
  startDate: "2026-07-01",
  endDate: "2026-07-02",
  branchCode: "S09",
  orderType: 3 /* Refunded */,
  keyword: "new keyword"
};
var queryBeforeTopSearch = buildPosmSalesOrderListQuery(appliedState);
assert(
  queryBeforeTopSearch.startDate === "2026-06-10" && queryBeforeTopSearch.endDate === "2026-06-11" && queryBeforeTopSearch.branchCode === "S01",
  "\u9876\u90E8 draft \u6539\u53D8\u540E\uFF0C\u5DF2\u5E94\u7528\u67E5\u8BE2\u4ECD\u5E94\u4FDD\u6301\u4E0D\u53D8"
);
var searchedState = applyPosmSalesOrderTopFilterDraft(appliedState, pendingTopDraft);
var searchedQuery = buildPosmSalesOrderListQuery(searchedState);
assert(
  searchedState.page === 1 && searchedQuery.startDate === "2026-07-01" && searchedQuery.endDate === "2026-07-02" && searchedQuery.branchCode === "S09" && searchedQuery.orderType === 3 /* Refunded */ && searchedQuery.keyword === "new keyword",
  "\u70B9\u51FB\u67E5\u8BE2\u540E\u624D\u5E94\u63D0\u4EA4\u9876\u90E8 draft \u5E76\u56DE\u5230\u7B2C 1 \u9875"
);
var sortedBeforeTopSearch = applyPosmSalesOrderQueryChange(appliedState, {
  sort: { field: "totalAmount", direction: "desc" }
});
var sortedBeforeTopSearchQuery = buildPosmSalesOrderListQuery(sortedBeforeTopSearch);
assert(
  sortedBeforeTopSearchQuery.startDate === "2026-06-10" && sortedBeforeTopSearchQuery.branchCode === "S01" && sortedBeforeTopSearchQuery.sortField === "totalAmount",
  "\u9876\u90E8 draft \u672A\u63D0\u4EA4\u65F6\u6392\u5E8F\u5FC5\u987B\u7EE7\u7EED\u4F7F\u7528\u4E0A\u6B21 applied \u6761\u4EF6"
);
var filteredFromPageThree = applyPosmSalesOrderColumnFilterDraft(appliedState, {
  ...appliedState.columnFilters,
  deviceCodeKeyword: "POS-NEW"
});
assert(
  filteredFromPageThree.page === 1 && buildPosmSalesOrderListQuery(filteredFromPageThree).deviceCodeKeyword === "POS-NEW",
  "\u975E\u7B2C\u4E00\u9875\u5E94\u7528\u5217\u8FC7\u6EE4\u53EA\u80FD\u751F\u6210 page=1 \u67E5\u8BE2"
);
var requestView = { data: "initial", loading: true, error: "" };
var oldRequestId = 1;
var latestRequestId = 2;
if (isLatestPosmSalesOrderRequest(oldRequestId, latestRequestId)) {
  requestView = { data: "stale", loading: false, error: "stale error" };
}
assertDeepEqual(
  requestView,
  { data: "initial", loading: true, error: "" },
  "\u65E7\u8BF7\u6C42\u4E0D\u5F97\u66F4\u65B0 data\u3001loading \u6216 error"
);
if (isLatestPosmSalesOrderRequest(latestRequestId, latestRequestId)) {
  requestView = { data: "latest", loading: false, error: "" };
}
assertDeepEqual(
  requestView,
  { data: "latest", loading: false, error: "" },
  "\u6700\u65B0\u8BF7\u6C42\u53EF\u4EE5\u63D0\u4EA4 data \u548C loading"
);
var appliedFilters = { orderGuidKeyword: "APPLIED", skuCountMin: 2 };
var draftFilters = createPosmSalesOrderColumnFilterDraft(appliedFilters, {
  orderGuidKeyword: "DRAFT"
});
assertDeepEqual(
  appliedFilters,
  { orderGuidKeyword: "APPLIED", skuCountMin: 2 },
  "\u7F16\u8F91\u8349\u7A3F\u4E0D\u5F97\u4FEE\u6539\u5DF2\u5E94\u7528\u5217\u8FC7\u6EE4"
);
assertDeepEqual(
  draftFilters,
  { orderGuidKeyword: "DRAFT", skuCountMin: 2 },
  "\u8349\u7A3F\u5E94\u4ECE\u5DF2\u5E94\u7528\u6761\u4EF6\u590D\u5236\u5E76\u72EC\u7ACB\u66F4\u65B0"
);
assert(
  normalizePosmSalesOrderFilterNumber(3.6, true) === 4,
  "SKU\u6570\u548C\u4EF6\u6570\u7684\u5C0F\u6570\u5E94\u89C4\u8303\u5316\u4E3A\u6574\u6570"
);
assert(
  normalizePosmSalesOrderFilterNumber("12.25", false) === 12.25,
  "\u91D1\u989D\u8FC7\u6EE4\u5E94\u4FDD\u7559\u5C0F\u6570"
);
assert(
  normalizePosmSalesOrderFilterNumber("", true) === void 0,
  "\u7A7A\u6570\u503C\u5E94\u7701\u7565"
);
assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, { page: 1 }),
  {
    startDate: "2026-06-10",
    endDate: "2026-06-11",
    branchCode: "S01",
    orderType: 1 /* Paid */,
    keyword: "invoice",
    orderGuidKeyword: "ORDER-01",
    deviceCodeKeyword: "POS-2",
    timeStart: "08:30:00",
    timeEnd: "18:15:59",
    clientUtcOffsetMinutes: 600,
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180,
    sortField: "actualPay",
    sortDirection: "desc",
    pageNumber: 1,
    pageSize: 50
  },
  "\u641C\u7D22\u65F6\u5E94\u6620\u5C04\u6240\u6709\u6761\u4EF6\u5E76\u4F7F\u7528\u663E\u5F0F page=1"
);
assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, {
    startDate: "2026-06-12",
    endDate: "2026-06-12",
    branchCode: "",
    orderType: -1 /* All */,
    keyword: "   ",
    page: 1,
    columnFilters: {
      ...currentState.columnFilters,
      orderGuidKeyword: "  ",
      deviceCodeKeyword: ""
    }
  }),
  {
    startDate: "2026-06-12",
    endDate: "2026-06-12",
    branchCode: void 0,
    orderType: -1 /* All */,
    keyword: void 0,
    orderGuidKeyword: void 0,
    deviceCodeKeyword: void 0,
    timeStart: "08:30:00",
    timeEnd: "18:15:59",
    clientUtcOffsetMinutes: 600,
    skuCountMin: 1,
    skuCountMax: 10,
    itemCountMin: 2,
    itemCountMax: 20,
    totalAmountMin: 10.5,
    totalAmountMax: 200.75,
    discountAmountMin: 0,
    discountAmountMax: 30,
    actualPayMin: 9,
    actualPayMax: 180,
    sortField: "actualPay",
    sortDirection: "desc",
    pageNumber: 1,
    pageSize: 50
  },
  "\u6587\u672C\u6761\u4EF6\u5E94 trim \u4E14\u7A7A\u767D\u7701\u7565\uFF0C\u540C\u65F6 overrides \u5E94\u5408\u5E76\u5217\u8FC7\u6EE4"
);
var partialOverrideQuery = buildPosmSalesOrderListQuery(currentState, {
  columnFilters: { deviceCodeKeyword: " D9 " }
});
assert(
  partialOverrideQuery.orderGuidKeyword === "ORDER-01" && partialOverrideQuery.deviceCodeKeyword === "D9",
  "\u5C40\u90E8\u5217\u8FC7\u6EE4 override \u5E94\u4E0E\u5DF2\u6709\u5217\u8FC7\u6EE4\u5408\u5E76"
);
var sortMappings = [
  ["orderGuid", "orderGuid"],
  ["branchName", "branchCode"],
  ["deviceCode", "deviceCode"],
  ["date", "orderTime"],
  ["time", "orderTime"],
  ["skuCount", "skuCount"],
  ["itemCount", "itemCount"],
  ["totalAmount", "totalAmount"],
  ["discountAmount", "discountAmount"],
  ["actualAmount", "actualPay"]
];
sortMappings.forEach(([columnKey, field]) => {
  assertDeepEqual(
    mapPosmSalesOrderSortState(columnKey, "descend"),
    { field, direction: "desc" },
    `${columnKey} \u5E94\u6620\u5C04\u5230 ${field}`
  );
});
assertDeepEqual(
  mapPosmSalesOrderSortState("unknown", void 0),
  { field: "orderTime", direction: "asc" },
  "\u672A\u77E5\u6392\u5E8F\u5E94\u56DE\u9000\u9ED8\u8BA4\u503C"
);
var syncedColumns = syncTopFiltersToColumnFilters(
  { deviceCodeKeyword: "D1", startDate: "old", endDate: "old", branchCode: "OLD" },
  { startDate: "2026-07-01", endDate: "2026-07-02", branchCode: "S02" }
);
assertDeepEqual(
  syncedColumns,
  { deviceCodeKeyword: "D1", startDate: "2026-07-01", endDate: "2026-07-02", branchCode: "S02" },
  "\u9876\u90E8\u65E5\u671F\u548C\u5206\u5E97\u5E94\u540C\u6B65\u5230\u5217\u8FC7\u6EE4\u4E14\u4FDD\u7559\u5176\u4ED6\u6761\u4EF6"
);
assertDeepEqual(
  syncColumnFiltersToTopFilters(syncedColumns),
  { startDate: "2026-07-01", endDate: "2026-07-02", branchCode: "S02" },
  "\u5217\u5934\u65E5\u671F\u548C\u5206\u5E97\u5E94\u540C\u6B65\u56DE\u9876\u90E8"
);
assertDeepEqual(
  createResetPosmSalesOrderState("2026-07-14", 50),
  {
    startDate: "2026-07-14",
    endDate: "2026-07-14",
    branchCode: "",
    orderType: -1 /* All */,
    keyword: "",
    page: 1,
    pageSize: 50,
    columnFilters: { startDate: "2026-07-14", endDate: "2026-07-14", branchCode: "" },
    sort: { field: "orderTime", direction: "asc" }
  },
  "\u603B\u91CD\u7F6E\u5E94\u6062\u590D\u4ECA\u65E5\u3001\u5168\u90E8\u3001\u7A7A\u5173\u952E\u8BCD\u548C\u9ED8\u8BA4\u6392\u5E8F"
);
var resetQuery = buildPosmSalesOrderListQuery(
  currentState,
  createResetPosmSalesOrderState("2026-07-14", 50)
);
assert(
  resetQuery.orderGuidKeyword === void 0 && resetQuery.skuCountMin === void 0 && resetQuery.actualPayMax === void 0 && resetQuery.sortField === "orderTime" && resetQuery.sortDirection === "asc",
  "\u603B\u91CD\u7F6E override \u5E94\u771F\u6B63\u6E05\u7A7A\u65E7\u5217\u8FC7\u6EE4\u5E76\u6062\u590D\u9ED8\u8BA4\u6392\u5E8F"
);
assertDeepEqual(
  validatePosmSalesOrderNumberRanges({ skuCountMin: 5, skuCountMax: 4 }),
  { isValid: false, range: "skuCount" },
  "min \u5927\u4E8E max \u65F6\u5E94\u8FD4\u56DE\u53EF\u8BC6\u522B\u7684\u533A\u95F4\u6821\u9A8C\u5931\u8D25"
);
assert(
  validatePosmSalesOrderNumberRanges({ actualPayMin: 0, actualPayMax: 0 }).isValid,
  "\u76F8\u7B49\u8FB9\u754C\u5E94\u901A\u8FC7\u6821\u9A8C"
);
function formatLocalDate(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
function formatLocalTime(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
assert(
  resolvePosmSalesOrderClientUtcOffsetMinutes("2026-06-10", -123) === 600,
  "\u5BA2\u6237\u7AEF\u504F\u79FB\u5E94\u6309\u9009\u4E2D\u65E5\u671F\u7684\u672C\u5730\u96F6\u70B9\u8BA1\u7B97"
);
assert(
  resolvePosmSalesOrderClientUtcOffsetMinutes("", 345) === 345 && resolvePosmSalesOrderClientUtcOffsetMinutes("2026-02-30", 345) === 345,
  "\u7A7A\u503C\u6216\u975E\u6CD5\u9009\u4E2D\u65E5\u671F\u5E94\u56DE\u9000\u5F53\u524D\u5BA2\u6237\u7AEF\u504F\u79FB"
);
assert(
  normalizePosmSalesOrderUtcTime("2026-07-17T00:15:01") === "2026-07-17T00:15:01Z",
  "\u65E0\u65F6\u533A\u540E\u7F00\u7684\u8BA2\u5355\u65F6\u95F4\u5E94\u8865\u5145 Z"
);
assert(
  normalizePosmSalesOrderUtcTime("2026-07-17 00:15:01") === "2026-07-17 00:15:01Z",
  "\u7A7A\u683C\u5206\u9694\u7684\u65E0\u540E\u7F00\u8BA2\u5355\u65F6\u95F4\u4E5F\u5E94\u53EA\u8865\u5145 Z"
);
for (const timestamp of [
  "2026-07-17T00:15:01Z",
  "2026-07-17T00:15:01+10:00",
  "2026-07-17T00:15:01+1000"
]) {
  assert(
    normalizePosmSalesOrderUtcTime(timestamp) === timestamp,
    `\u5DF2\u6709\u65F6\u533A\u540E\u7F00\u7684\u8BA2\u5355\u65F6\u95F4\u5E94\u4FDD\u6301\u4E0D\u53D8: ${timestamp}`
  );
}
var utcWithoutSuffix = "2026-07-17T00:15:01";
var utcWithoutSuffixLocal = /* @__PURE__ */ new Date(`${utcWithoutSuffix}Z`);
assert(
  formatPosmSalesOrderLocalTime(utcWithoutSuffix, "YYYY-MM-DD") === formatLocalDate(utcWithoutSuffixLocal),
  "\u65E0\u65F6\u533A\u540E\u7F00\u7684\u8BA2\u5355 UTC \u65E5\u671F\u5E94\u8F6C\u6362\u4E3A\u6D4F\u89C8\u5668\u672C\u5730\u65E5\u671F"
);
assert(
  formatPosmSalesOrderLocalTime(utcWithoutSuffix, "HH:mm:ss") === formatLocalTime(utcWithoutSuffixLocal),
  "\u65E0\u65F6\u533A\u540E\u7F00\u7684\u8BA2\u5355 UTC \u65F6\u95F4\u5E94\u8F6C\u6362\u4E3A\u6D4F\u89C8\u5668\u672C\u5730\u65F6\u95F4"
);
var explicitUtc = "2026-07-17T00:15:01Z";
assert(
  formatPosmSalesOrderLocalTime(explicitUtc, "HH:mm:ss") === formatLocalTime(new Date(explicitUtc)),
  "\u5E26 Z \u7684\u8BA2\u5355\u65F6\u95F4\u5E94\u6309\u663E\u5F0F UTC \u8F6C\u6362\u4E3A\u6D4F\u89C8\u5668\u672C\u5730\u65F6\u95F4"
);
var explicitOffset = "2026-07-17T00:15:01+02:00";
assert(
  formatPosmSalesOrderLocalTime(explicitOffset, "HH:mm:ss") === formatLocalTime(new Date(explicitOffset)),
  "\u5E26 offset \u7684\u8BA2\u5355\u65F6\u95F4\u5E94\u4FDD\u7559\u539F\u65F6\u533A\u8BED\u4E49\u5E76\u8F6C\u6362\u4E3A\u6D4F\u89C8\u5668\u672C\u5730\u65F6\u95F4"
);
assert(
  formatPosmSalesOrderLocalTime("2026-07-16T14:30:00Z", "YYYY-MM-DD HH:mm:ss") === "2026-07-17 00:30:00",
  "UTC \u8BA2\u5355\u65F6\u95F4\u8DE8\u65E5\u540E\u5E94\u663E\u793A Brisbane \u5F53\u5730\u65E5\u671F\u548C\u65F6\u95F4"
);
for (const [timestamp, expected] of [
  ["2026-07-17 00:15:01.1234567", "2026-07-17 10:15:01"],
  ["2026-07-17T00:15:01+10:00", "2026-07-17 00:15:01"],
  ["2026-07-17T00:15:01+1000", "2026-07-17 00:15:01"]
]) {
  assert(
    formatPosmSalesOrderLocalTime(timestamp, "YYYY-MM-DD HH:mm:ss") === expected,
    `.NET/ISO \u5408\u6CD5\u65F6\u95F4\u683C\u5F0F\u5E94\u88AB\u63A5\u53D7: ${timestamp}`
  );
}
assert(
  formatPosmSalesOrderLocalTime("not-a-date", "HH:mm:ss") === "not-a-date",
  "\u975E\u6CD5\u8BA2\u5355\u65F6\u95F4\u5E94\u4FDD\u7559\u539F\u6587\uFF0C\u907F\u514D\u9690\u85CF\u540E\u7AEF\u5F02\u5E38\u6570\u636E"
);
assert(
  formatPosmSalesOrderLocalTime("2026-02-30T00:00:00Z", "YYYY-MM-DD") === "2026-02-30T00:00:00Z",
  "\u4E0D\u5B58\u5728\u7684\u65E5\u5386\u65E5\u671F\u4E0D\u5F97\u88AB dayjs \u6B63\u5E38\u5316"
);
for (const invalidTimestamp of [
  "0000-01-01T00:00:00Z",
  "2026-13-01T00:00:00Z",
  "2026-07-17T24:00:00Z",
  "2026-07-17T00:60:00Z",
  "2026-07-17T00:00:60Z",
  "2026-07-17T00:00:00.12345678Z",
  "2026-07-17T00:00:00+14:01",
  "2026-07-17T00:00:00+1060"
]) {
  assert(
    formatPosmSalesOrderLocalTime(invalidTimestamp, "YYYY-MM-DD") === invalidTimestamp,
    `\u8D8A\u754C\u6216\u4E0D\u7B26\u5408 .NET/ISO \u683C\u5F0F\u7684\u8BA2\u5355\u65F6\u95F4\u5E94\u4FDD\u7559\u539F\u6587: ${invalidTimestamp}`
  );
}
assert(
  formatPosmSalesOrderLocalTime("", "HH:mm:ss") === "-" && formatPosmSalesOrderLocalTime(null, "HH:mm:ss") === "-" && formatPosmSalesOrderLocalTime(void 0, "HH:mm:ss") === "-",
  "\u7A7A\u8BA2\u5355\u65F6\u95F4\u5E94\u663E\u793A\u5360\u4F4D\u7B26"
);
var changedState = applyPosmSalesOrderQueryChange(currentState, {
  columnFilters: { ...currentState.columnFilters, deviceCodeKeyword: "D9" }
});
assert(changedState.page === 1, "\u66F4\u6362\u6761\u4EF6\u5E94\u56DE\u5230\u7B2C 1 \u9875");
assert(
  changedState.pageSize === 50 && changedState.sort.field === "actualPay",
  "\u66F4\u6362\u6761\u4EF6\u5E94\u4FDD\u7559\u9875\u5927\u5C0F\u548C\u6392\u5E8F"
);
assertDeepEqual(
  buildPosmSalesOrderListQuery(changedState, { page: 4 }),
  { ...buildPosmSalesOrderListQuery(changedState), pageNumber: 4 },
  "\u7FFB\u9875\u5E94\u4FDD\u7559\u5168\u90E8\u7B5B\u9009\u4E0E\u6392\u5E8F\u6761\u4EF6"
);
console.log("posmSalesOrdersLogic.test: ok");
