import { Redirect } from "expo-router";

export default function AttendanceLegacyRoute() {
  return <Redirect href={"/(tabs)/attendance-personal" as unknown as Parameters<typeof Redirect>[0]["href"]} />;
}
