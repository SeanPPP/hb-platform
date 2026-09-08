import AppDownloadsScreen from "@/modules/app-downloads/screen";
import { VersionManagementGuard } from "@/modules/navigation/version-management-guard";

export default function AppDownloadsRoute() {
  return (
    <VersionManagementGuard>
      <AppDownloadsScreen />
    </VersionManagementGuard>
  );
}
