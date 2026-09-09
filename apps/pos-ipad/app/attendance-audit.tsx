import { Redirect, type Href, useRouter } from "expo-router";
import { useEffect, useState } from "react";
import { View } from "react-native";


import { usePosRuntime } from "@/core/runtime/pos-runtime-context";
import {
  AttendanceAuditScreen,
  AttendanceAuditUnavailableScreen,
  resolveAttendanceAuditRuntimeFactory,
  type AttendanceAuditPresenter,
} from "@/features/attendance-audit";
import { FaceAttendanceScreen, FaceAttendanceReturnBar } from "@/features/attendance-face/face-attendance-screen";
import {
  isActiveCashierBoundToDevice,
  resolveProtectedSalesRouteGate,
  useCashierLoginStore,
} from "@/features/cashier-login";
import { BootstrapScreen } from "@/ui/screens/bootstrap-screen";
import { usePosShellStore } from "@/ui/shell/pos-shell-store";

type AttendanceAuditBinding = Readonly<{
  cashier: object;
  presenter: AttendanceAuditPresenter;
  services: object;
}>;

/**
 * 考勤与审计复用销售路由的设备/收银员 lease。Audit.View 只控制审计区，
 * 不阻止 WPF 等价的考勤 QR；可信身份和权限只能由零参数组合根注入。
 */
export default function AttendanceAuditRoute() {
  const router = useRouter();
  const runtime = usePosRuntime();
  const activeCashier = useCashierLoginStore(
    (state) => state.activeCashier,
  );
  const clearActiveCashier = useCashierLoginStore(
    (state) => state.clearActiveCashier,
  );
  const gate = resolveProtectedSalesRouteGate(
    runtime.state,
    activeCashier,
  );
  const factory = runtime.services
    ? resolveAttendanceAuditRuntimeFactory(runtime.services)
    : null;
  const [binding, setBinding] =
    useState<AttendanceAuditBinding | null>(null);
  const [runtimeUnavailable, setRuntimeUnavailable] = useState(false);
  const [attendanceView, setAttendanceView] = useState<"face" | "audit">(() => runtime.services?.faceAttendance?.state().hasSyncedRoster ? "face" : "audit");
  const presenter =
    binding?.services === runtime.services &&
    binding.cashier === activeCashier
      ? binding.presenter
      : null;

  useEffect(() => {
    if (
      gate !== "check-device-identity" ||
      !activeCashier ||
      !runtime.services
    ) {
      setBinding(null);
      setRuntimeUnavailable(false);
      return undefined;
    }

    let cancelled = false;
    let createdPresenter: AttendanceAuditPresenter | null = null;
    const cashier = activeCashier;
    const services = runtime.services;
    setBinding(null);
    setRuntimeUnavailable(false);
    void services.deviceSession
      .getDeviceIdentity()
      .then((identity) => {
        if (cancelled) return;
        if (
          !identity ||
          !isActiveCashierBoundToDevice(cashier, identity)
        ) {
          clearActiveCashier();
          return;
        }
        if (!factory) {
          setRuntimeUnavailable(true);
          return;
        }
        try {
          createdPresenter = factory.createPresenter();
        } catch {
          setRuntimeUnavailable(true);
          return;
        }
        if (cancelled) {
          createdPresenter.destroy();
          createdPresenter = null;
          return;
        }
        setBinding({ cashier, presenter: createdPresenter, services });
      })
      .catch(() => {
        if (!cancelled) clearActiveCashier();
      });

    return () => {
      cancelled = true;
      createdPresenter?.destroy();
      createdPresenter = null;
    };
  }, [
    activeCashier,
    clearActiveCashier,
    factory,
    gate,
    runtime.services,
  ]);

  // 网络信号来源：connectivity 由 NetworkStatusBridge 后端探测驱动，
  // 后端恢复后自动翻转为 online；runtime.state.backend 启动后不更新。
  const connectivity = usePosShellStore(
    (state) => state.connectivity,
  );

  useEffect(() => {
    if (!presenter) return;
    // checking 视为在线（未知时乐观）；考勤审计上传由后台调度器消费此门禁，
    // 恢复后自动继续上传，无需手动重试。
    presenter.setOnline(
      connectivity === "online" || connectivity === "checking",
    );
  }, [connectivity, presenter]);

  if (gate === "redirect-index") {
    return <Redirect href={"/" as Href} />;
  }
  if (gate === "redirect-login") {
    return <Redirect href={"/login" as Href} />;
  }
  if (runtimeUnavailable) {
    return (
      <AttendanceAuditUnavailableScreen
        onBack={() => router.dismissTo("/sales" as Href)}
      />
    );
  }
  if (!presenter) {
    return <BootstrapScreen />;
  }

  if (runtime.services?.faceAttendance && attendanceView === "face") {
    return <FaceAttendanceScreen runtime={runtime.services.faceAttendance}
      onBack={() => router.dismissTo("/sales" as Href)}
      onShowQr={() => setAttendanceView("audit")} />;
  }

  return (
    <View style={{ flex: 1 }}>
      {runtime.services?.faceAttendance ? <FaceAttendanceReturnBar onPress={() => setAttendanceView("face")} /> : null}
    <AttendanceAuditScreen
      onBack={() => router.dismissTo("/sales" as Href)}
      presenter={presenter}
    />
    </View>
  );
}
