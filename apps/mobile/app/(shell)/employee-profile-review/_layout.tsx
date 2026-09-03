import { Stack } from "expo-router";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

export const unstable_settings = {
  initialRouteName: "index",
};

export default function EmployeeProfileReviewStackLayout() {
  const { t } = useAppTranslation("employeeProfileReview");
  return (
    <Stack screenOptions={{ headerBackTitle: t("actions.back"), gestureEnabled: true }}>
      <Stack.Screen name="index" options={{ headerShown: false }} />
      <Stack.Screen name="[requestId]" options={{ title: t("detail.title") }} />
    </Stack>
  );
}
