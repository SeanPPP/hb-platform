import WpfVersionsScreen from "@/modules/wpf-versions/screen";
import { VersionManagementGuard } from "@/modules/navigation/version-management-guard";

export default function WpfVersionsRoute() {
  return (
    <VersionManagementGuard>
      <WpfVersionsScreen />
    </VersionManagementGuard>
  );
}
