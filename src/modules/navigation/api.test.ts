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
    routeName: "seasonal-cards",
    titleKey: "tabs.seasonalCards",
    icon: "cards-outline",
    permission: "SeasonalCards.Remaining.ViewManagedStore",
    order: 23,
  },
]);

assertEqual(normalized.length, 4, "normalizer keeps legacy attendance routes and seasonal cards");
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
  "seasonal-cards",
  "seasonal cards route remains available"
);
