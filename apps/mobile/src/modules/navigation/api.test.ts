import { normalizeAppNavigationMenu } from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const normalized = normalizeAppNavigationMenu([
  {
    RouteName: "attendance",
    TitleKey: "tabs.attendance",
    Icon: "calendar-clock",
    Permission: "Attendance.Schedule.ViewSelf",
    Order: 20,
  },
  {
    routeName: "attendance-personal",
    titleKey: "tabs.attendancePersonal",
    icon: "account-clock-outline",
    permission: "Attendance.Schedule.ViewSelf",
    order: 21,
  },
  {
    routeName: "attendance-management",
    titleKey: "tabs.attendanceManagement",
    icon: "calendar-edit",
    permission: "Attendance.Schedule.ViewStore",
    order: 22,
  },
  {
    routeName: "advertisements",
    titleKey: "tabs.advertisements",
    icon: "bullhorn-outline",
    permission: "Advertisements.View",
    order: 22.5,
  },
  {
    routeName: "promotions",
    titleKey: "tabs.promotions",
    icon: "ticket-percent-outline",
    permission: "Promotions.View",
    order: 22.6,
  },
  {
    routeName: "reports",
    titleKey: "tabs.reports",
    icon: "chart-line",
    permission: "Reports.View",
    order: 22.7,
  },
  {
    routeName: "seasonal-cards",
    titleKey: "tabs.seasonalCards",
    icon: "cards-outline",
    permission: "SeasonalCards.Remaining.ViewManagedStore",
    order: 23,
  },
]);

assertEqual(normalized.length, 7, "normalizer keeps legacy attendance routes, advertisements, promotions, reports, and seasonal cards");
assertEqual(normalized[0]?.routeName, "attendance", "legacy attendance route stays available");
assertEqual(
  normalized[1]?.routeName,
  "attendance-personal",
  "explicit attendance personal route remains available"
);
assertEqual(
  normalized[2]?.routeName,
  "attendance-management",
  "explicit attendance management route remains available"
);
assertEqual(
  normalized[3]?.routeName,
  "advertisements",
  "advertisements route remains available"
);
assertEqual(
  normalized[4]?.routeName,
  "promotions",
  "promotions route remains available"
);
assertEqual(
  normalized[5]?.routeName,
  "reports",
  "reports route remains available"
);
assertEqual(
  normalized[6]?.routeName,
  "seasonal-cards",
  "seasonal cards route remains available"
);
