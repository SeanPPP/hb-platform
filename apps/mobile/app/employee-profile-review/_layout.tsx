import { Stack } from "expo-router";
import { useAppTranslation } from "@/shared/i18n/use-app-translation";

export default function EmployeeProfileReviewStackLayout() {
  const { t } = useAppTranslation("employeeProfileReview");
  return (
    <Stack screenOptions={{ headerBackTitle: t("actions.back") }}>
      <Stack.Screen name="[requestId]" options={{ title: t("detail.title") }} />
    </Stack>
  );
}
