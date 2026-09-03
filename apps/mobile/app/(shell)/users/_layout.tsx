import { Stack } from "expo-router";

export const unstable_settings = {
  initialRouteName: "index",
};

export default function UserStackLayout() {
  return <Stack screenOptions={{ headerShown: false, gestureEnabled: true }} />;
}
