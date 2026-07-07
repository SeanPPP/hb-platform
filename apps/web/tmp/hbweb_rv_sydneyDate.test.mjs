// src/utils/tagColors.ts
var DEFAULT_TAG_COLORS = [
  "red",
  "orange",
  "gold",
  "lime",
  "green",
  "cyan",
  "blue",
  "purple",
  "magenta",
  "volcano"
];
function hashText(value) {
  let hash = 0;
  for (let index = 0; index < value.length; index += 1) {
    hash = (hash << 5) - hash + value.charCodeAt(index);
    hash |= 0;
  }
  return hash;
}
function getStableTagColor(value, colors = DEFAULT_TAG_COLORS) {
  if (!value) {
    return "default";
  }
  return colors[Math.abs(hashText(value)) % colors.length] || "default";
}
function getDateTagColor(value) {
  return getStableTagColor(value);
}

// src/utils/sydneyDate.ts
var SYDNEY_TIME_ZONE = "Australia/Sydney";
var dateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})(?:T00:00:00(?:\.0+)?)?$/;
var sydneyDateFormatter = new Intl.DateTimeFormat("en-CA", {
  timeZone: SYDNEY_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit"
});
function formatDateParts(year, month, day) {
  return `${year}/${month}/${day}`;
}
function formatIntlDate(date) {
  const parts = sydneyDateFormatter.formatToParts(date).reduce((result, part) => {
    if (part.type !== "literal") {
      result[part.type] = part.value;
    }
    return result;
  }, {});
  return parts.year && parts.month && parts.day ? formatDateParts(parts.year, parts.month, parts.day) : sydneyDateFormatter.format(date).replace(/-/g, "/");
}
function formatSydneyDate(value) {
  if (!value) {
    return "--";
  }
  const trimmedValue = value.trim();
  if (!trimmedValue) {
    return "--";
  }
  const dateOnlyMatch = trimmedValue.match(dateOnlyPattern);
  if (dateOnlyMatch) {
    return formatDateParts(dateOnlyMatch[1], dateOnlyMatch[2], dateOnlyMatch[3]);
  }
  const date = new Date(trimmedValue);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return formatIntlDate(date);
}
function getSydneyDateTagColor(value) {
  const displayDate = formatSydneyDate(value);
  if (displayDate === "--") {
    return "default";
  }
  return getDateTagColor(displayDate);
}

// src/utils/sydneyDate.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\uFF0C\u5B9E\u9645: ${String(actual)}\uFF0C\u671F\u671B: ${String(expected)}`);
  }
}
function main() {
  assertEqual(
    formatSydneyDate("2026-06-26T00:00:00"),
    "2026/06/26",
    "\u65E0\u65F6\u533A\u8D27\u67DC\u65E5\u671F\u5E94\u6309\u6089\u5C3C\u4E1A\u52A1\u65E5\u671F\u663E\u793A\u4E14\u4E0D\u663E\u793A\u65F6\u95F4"
  );
  assertEqual(
    formatSydneyDate("2026-06-25T14:30:00Z"),
    "2026/06/26",
    "\u5E26 UTC \u65F6\u533A\u7684\u65F6\u95F4\u6233\u5E94\u8F6C\u6362\u6210\u6089\u5C3C\u65E5\u671F"
  );
  assertEqual(formatSydneyDate(void 0), "--", "\u7A7A\u65E5\u671F\u5E94\u663E\u793A\u5360\u4F4D\u7B26");
  assertEqual(formatSydneyDate("bad-date"), "bad-date", "\u975E\u6CD5\u65E5\u671F\u5E94\u4FDD\u7559\u539F\u503C\u4FBF\u4E8E\u6392\u67E5\u6570\u636E");
  assertEqual(getSydneyDateTagColor("2026-06-26T00:00:00"), "blue", "\u6089\u5C3C\u65E5\u671F\u5E94\u6709\u7A33\u5B9A\u989C\u8272");
  assertEqual(getSydneyDateTagColor("2026-06-25T00:00:00"), "cyan", "\u4E0D\u540C\u65E5\u671F\u5E94\u6620\u5C04\u5230\u4E0D\u540C\u989C\u8272");
  assertEqual(getSydneyDateTagColor(void 0), "default", "\u7A7A\u65E5\u671F\u4E0D\u5E94\u663E\u793A\u5F69\u8272\u6807\u7B7E");
  console.log("sydneyDate.test: ok");
}
main();
