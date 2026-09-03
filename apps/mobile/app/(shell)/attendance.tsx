import { Redirect } from "expo-router";

export default function AttendanceLegacyRoute() {
  return <Redirect href={"/(shell)/attendance-personal" as unknown as Parameters<typeof Redirect>[0]["href"]} />;
}
