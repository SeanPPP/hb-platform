import { CameraView, useCameraPermissions } from "expo-camera";
import { useFocusEffect } from "expo-router";
import { useCallback, useEffect, useLayoutEffect, useRef, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import { ActivityIndicator, AppState, Linking, ScrollView, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type { FaceAttendanceEmployee, FaceAttendanceEntry, FaceAttendanceRuntime, FacePunchType } from "./face-attendance-contract";
import { faceAttendanceChinese, faceAttendanceEnglish, faceAttendanceErrorKey, type FaceAttendanceCopyKey } from "./face-attendance-copy";
import { captureFacePhoto } from "./face-photo-capture";
import { FaceRecordReview } from "./face-record-review";

import { PosKeyboardAwareScrollView, PosKeyboardAwareTextInput } from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

type Props = Readonly<{ runtime: FaceAttendanceRuntime; onBack(): void; onShowQr(): void }>;

export function FaceAttendanceReturnBar({ onPress }: Readonly<{ onPress(): void }>) {
  const { i18n } = useTranslation();
  const copy = (i18n.resolvedLanguage ?? i18n.language).startsWith("zh") ? faceAttendanceChinese : faceAttendanceEnglish;
  return <View style={styles.returnBar}><Action label={copy.face} onPress={onPress} testID="face-return" /></View>;
}

function statusKey(entry: FaceAttendanceEntry): FaceAttendanceCopyKey {
  if (entry.serverStatus === "verified") return "confirmed";
  if (entry.serverStatus === "needsReview") return "review";
  if (entry.serverStatus === "rejected") return "rejected";
  if (entry.serverStatus === "queued" || entry.serverStatus === "verifying") return "verifying";
  return "pending";
}

function displayTime(iso: string | null, locale: string, timeZone: string | null) {
  if (!iso) return "—";
  try {
    return new Intl.DateTimeFormat(locale, { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false, ...(timeZone ? { timeZone } : {}) }).format(new Date(iso));
  } catch { return iso; }
}

/** 相机只随页面焦点和前台状态挂载；人员、类型在拍摄前确认，保存结果由耐久队列提供。 */
export function FaceAttendanceScreen({ runtime, onBack, onShowQr }: Props) {
  const { i18n } = useTranslation();
  const locale = (i18n.resolvedLanguage ?? i18n.language ?? "en").startsWith("zh") ? "zh-CN" : "en-AU";
  const copy = locale === "zh-CN" ? faceAttendanceChinese : faceAttendanceEnglish;
  const t = (key: FaceAttendanceCopyKey) => copy[key];
  const state = useSyncExternalStore(runtime.subscribe, runtime.state, runtime.state);
  const { width } = useWindowDimensions();
  const [permission, requestPermission] = useCameraPermissions();
  const [focused, setFocused] = useState(false);
  const [foreground, setForeground] = useState(AppState.currentState === "active");
  const [cameraReady, setCameraReady] = useState(false);
  const [cameraFailed, setCameraFailed] = useState(false);
  const [cameraGeneration, setCameraGeneration] = useState(0);
  const [pictureSize, setPictureSize] = useState<string>();
  const [selectedGuid, setSelectedGuid] = useState<string | null>(null);
  const [punchType, setPunchType] = useState<FacePunchType>("clockIn");
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [saved, setSaved] = useState<FaceAttendanceEntry | null>(null);
  const [notice, setNotice] = useState<FaceAttendanceCopyKey | null>(null);
  const [mode, setMode] = useState<"punch" | "enroll" | "revoke">("punch");
  const [enrollmentPhotos, setEnrollmentPhotos] = useState<string[]>([]);
  const [reviewing, setReviewing] = useState<FaceAttendanceEntry | null>(null);
  const closeReview = useCallback(() => setReviewing(null), []);
  const camera = useRef<CameraView>(null);
  const mounted = useRef(true);
  const captureGeneration = useRef(0);
  const foregroundRef = useRef(AppState.currentState === "active");

  useFocusEffect(useCallback(() => {
    setFocused(true);
    return () => { captureGeneration.current++; setFocused(false); setCameraReady(false); setEnrollmentPhotos([]); setMode("punch"); };
  }, []));
  useLayoutEffect(() => { runtime.invalidateManagement?.(); }, [runtime]);
  useEffect(() => {
    mounted.current = true;
    void runtime.refresh().catch(() => undefined);
    const listener = AppState.addEventListener("change", (next) => {
      foregroundRef.current = next === "active";
      setForeground(next === "active");
      if (next !== "active") {
        captureGeneration.current++; setCameraReady(false); setEnrollmentPhotos([]); setMode("punch");
      }
    });
    return () => { mounted.current = false; listener.remove(); };
  }, [runtime]);

  const selected = state.employees.find(employee => employee.userGuid === selectedGuid) ?? null;
  const currentRecord = state.entries.find(entry => entry.eventGuid === saved?.eventGuid) ?? saved;
  const employees = state.employees.filter(employee =>
    `${employee.displayName} ${employee.employeeCode ?? ""}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
  const cameraActive = foreground && focused && !saved && !reviewing && mode !== "revoke";
  const canCapture = !busy && cameraReady && cameraActive && !!selected
    && (mode === "enroll" ? state.online && state.canManage && enrollmentPhotos.length < 3 : selected.enrollmentStatus === "active");

  function chooseEmployee(employee: FaceAttendanceEmployee) {
    if (busyRef.current || saved) return;
    setSelectedGuid(employee.userGuid);
    setMode("punch"); setEnrollmentPhotos([]); setNotice(null);
    const local = state.entries.filter(entry => entry.userGuid === employee.userGuid
      && entry.serverStatus !== "rejected" && entry.serverStatus !== "needsReview")
      .sort((left, right) => right.occurredAtUtc.localeCompare(left.occurredAtUtc) || right.localSequence - left.localSequence)[0];
    const latestType = local && (!employee.lastPunchTimeUtc || local.occurredAtUtc >= employee.lastPunchTimeUtc)
      ? local.punchType : employee.lastPunchType;
    setPunchType(latestType === "clockIn" ? "clockOut" : "clockIn");
  }

  async function perform(action: () => Promise<void>, fallback: FaceAttendanceCopyKey) {
    if (busyRef.current) return;
    busyRef.current = true; setBusy(true); setNotice(null);
    try { await action(); }
    catch (error) {
      if (mounted.current) {
        const key = faceAttendanceErrorKey((error as { code?: string } | null)?.code ?? (error instanceof Error ? error.message : null));
        setNotice(key === "genericFailure" ? fallback : key);
      }
    } finally {
      busyRef.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  function takePhoto() {
    if (!canCapture || !camera.current || !selected) return;
    const owner = selected;
    const confirmedType = punchType;
    const activeCamera = camera.current;
    const generation = captureGeneration.current;
    void perform(async () => {
      const photo = await captureFacePhoto(activeCamera, runtime.captureTime);
      try {
        if (mode === "enroll") {
          if (mounted.current && foregroundRef.current && captureGeneration.current === generation) setEnrollmentPhotos(current => [...current, photo.photoBase64]);
        } else {
          const entry = await runtime.record({ employee: owner, punchType: confirmedType,
            photoBase64: photo.photoBase64, capturedAtUtc: photo.capturedAtUtc,
            capturedUptimeMilliseconds: photo.capturedUptimeMilliseconds });
          if (mounted.current) { setSaved(entry); setCameraReady(false); }
        }
      } finally { await photo.release(); }
    }, "failedSave");
  }

  function reset() {
    setSaved(null); setSelectedGuid(null); setPunchType("clockIn"); setNotice(null);
    setMode("punch"); setEnrollmentPhotos([]); setCameraReady(false);
  }

  async function cameraDidBecomeReady() {
    const activeCamera = camera.current;
    if (!activeCamera) return;
    const generation = captureGeneration.current;
    try {
      const sizes = await activeCamera.getAvailablePictureSizesAsync();
      const best = sizes?.map(value => ({ value, area: value.split("x").map(Number).reduce((a, b) => a * b, 1) }))
        .filter(item => item.area >= 300_000 && item.area <= 1_300_000)
        .sort((a, b) => b.area - a.area)[0];
      if (mounted.current && camera.current === activeCamera && captureGeneration.current === generation && best) setPictureSize(best.value);
    } catch { /* 部分相机不返回尺寸列表，仍使用原生压缩质量和服务端大小上限。 */ }
    if (mounted.current && foregroundRef.current && camera.current === activeCamera && captureGeneration.current === generation) setCameraReady(true);
  }

  return <SafeAreaView style={styles.safe} testID="face-attendance-screen">
    <View style={styles.header}>
      <View style={styles.headerText}><Text style={styles.title}>{t("title")}</Text><Text style={styles.muted}>{t("subtitle")}</Text></View>
      <Text style={[styles.pill, state.online ? styles.success : styles.warning]}>{t(state.online ? "online" : "offline")}</Text>
      <Action label={t("qr")} onPress={onShowQr} disabled={busy} testID="face-show-qr" />
      <Action label={t("back")} onPress={onBack} disabled={busy} />
    </View>
    <PosKeyboardAwareScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
      <View style={[styles.workspace, width < 800 && styles.narrow]}>
        <View style={styles.cameraPanel}>
          <Text style={styles.heading}>{mode === "enroll" ? t("enrollTitle") : t("camera")}</Text>
          <Text style={styles.muted}>{mode === "enroll" ? t("enrollHint") : t("cameraHint")}</Text>
          <View style={styles.cameraStage}>
            {saved && currentRecord ? <View accessibilityLiveRegion="polite" style={styles.result} testID="face-saved-result">
              <Text style={styles.resultIcon}>{currentRecord.serverStatus === "verified" ? "✓" : "◷"}</Text>
              <Text style={styles.resultTitle}>{t(statusKey(currentRecord))}</Text>
              <Text style={styles.resultName}>{currentRecord.employeeName} · {t(currentRecord.punchType)}</Text>
              <Text style={styles.resultTime}>{displayTime(currentRecord.occurredAtUtc, locale, state.storeTimeZone)}</Text>
              <Text style={styles.resultHint}>{t(currentRecord.serverStatus ? "savedHint" : "offlineHint")}</Text>
              {currentRecord.reasonCode ? <Text style={styles.resultHint}>{t(faceAttendanceErrorKey(currentRecord.reasonCode))}</Text> : null}
            </View> : !permission?.granted ? <View style={styles.cameraMessage}>
              <Text style={styles.resultHint}>{t("cameraPermission")}</Text>
              <Action label={t(permission?.canAskAgain === false ? "settings" : "allowCamera")} primary
                onPress={() => { void perform(async () => { if (permission?.canAskAgain === false) await Linking.openSettings(); else await requestPermission(); }, "cameraUnavailable"); }} />
            </View> : cameraFailed ? <View style={styles.cameraMessage}>
              <Text style={styles.resultHint}>{t("cameraUnavailable")}</Text>
              <Action label={t("retry")} onPress={() => { setCameraFailed(false); setCameraReady(false); setCameraGeneration(value => value + 1); }} />
            </View> : cameraActive ? <>
              <CameraView key={cameraGeneration} ref={camera} style={StyleSheet.absoluteFill} facing="front" mode="picture"
                pictureSize={pictureSize} onCameraReady={() => { void cameraDidBecomeReady(); }}
                onMountError={() => { setCameraFailed(true); setCameraReady(false); }} testID="face-front-camera" />
              <View pointerEvents="none" style={styles.faceGuide} />
              {!cameraReady ? <ActivityIndicator color="white" style={styles.cameraSpinner} /> : null}
            </> : <View style={styles.cameraMessage}><Text style={styles.resultHint}>{mode === "revoke" ? t("revokeHint") : t("camera")}</Text></View>}
          </View>
          {mode === "enroll" ? <Text style={styles.heading}>{t("photoCount")} {enrollmentPhotos.length} / 3</Text> : null}
        </View>
        <View style={styles.controls}>
          <Text style={styles.heading}>{t("select")}</Text>
          <PosKeyboardAwareTextInput accessibilityLabel={t("search")} placeholder={t("search")} value={query}
            onChangeText={setQuery} editable={!busy && !saved} style={styles.search} testID="face-employee-search" />
          <ScrollView nestedScrollEnabled style={styles.employees} keyboardShouldPersistTaps="handled">
            {employees.map(employee => <PosPressable key={employee.userGuid}
              accessibilityRole="button" accessibilityState={{ selected: selectedGuid === employee.userGuid, disabled: busy || !!saved }}
              disabled={busy || !!saved} onPress={() => chooseEmployee(employee)}
              style={[styles.employee, selectedGuid === employee.userGuid && styles.selectedEmployee]} testID={`face-employee-${employee.userGuid}`}>
              <View style={styles.flex}><Text style={styles.employeeName}>{employee.displayName}</Text><Text style={styles.muted}>{employee.employeeCode ?? "—"}</Text></View>
              {employee.enrollmentStatus !== "active" ? <Text style={styles.smallWarning}>{t("notEnrolled")}</Text> : null}
            </PosPressable>)}
            {!employees.length ? <Text style={styles.empty}>{t(state.hasSyncedRoster ? "noEmployees" : "setup")}</Text> : null}
          </ScrollView>
          <Text style={styles.syncDate}>{t("lastSync")} {displayTime(state.lastSyncedAtUtc, locale, state.storeTimeZone)}</Text>
          <Text style={styles.heading}>{t("type")}</Text>
          <View style={styles.row}>{(["clockIn", "clockOut"] as const).map(type => <Action key={type} label={t(type)}
            primary={punchType === type} disabled={busy || !!saved || mode !== "punch"} onPress={() => setPunchType(type)}
            selected={punchType === type} testID={`face-type-${type}`} />)}</View>
          <Text style={styles.muted}>{t("suggestion")}</Text>
          {notice || state.lastErrorCode ? <Text accessibilityLiveRegion="polite" style={styles.error} testID="face-notice">{t(notice ?? faceAttendanceErrorKey(state.lastErrorCode))}</Text> : null}
          {selected && selected.enrollmentStatus !== "active" && mode === "punch" ? <Text style={styles.error}>{t("needsEnrollment")}</Text> : null}
          {saved ? <Action label={t("next")} primary onPress={reset} testID="face-next" />
            : mode === "revoke" ? <>
              <Text style={styles.error}>{t("revokeHint")}</Text>
              <Action label={t("revokeConfirm")} primary disabled={busy || !state.online || !state.canManage} onPress={() => {
                if (!selected) return;
                void perform(async () => { await runtime.revoke({ userGuid: selected.userGuid, reason: "manager-revoked" }); setMode("punch"); setNotice("revoked"); }, "genericFailure");
              }} testID="face-revoke-confirm" />
            </> : mode === "enroll" && enrollmentPhotos.length === 3 ? <Action label={t(busy ? "saving" : "finishEnrollment")} primary disabled={busy || !state.online || !state.canManage}
              onPress={() => { if (!selected) return; void perform(async () => { await runtime.enroll({ userGuid: selected.userGuid, photosBase64: enrollmentPhotos }); setEnrollmentPhotos([]); setMode("punch"); setNotice("enrolled"); }, "genericFailure"); }} testID="face-enroll-save" />
              : <Action label={t(busy ? "saving" : mode === "enroll" ? "captureEnrollment" : "photo")} primary disabled={!canCapture} onPress={takePhoto} testID="face-capture" />}
          {mode !== "punch" ? <Action label={t("cancel")} disabled={busy} onPress={() => { setMode("punch"); setEnrollmentPhotos([]); }} /> : null}
          {state.canManage && state.online && selected && !saved && mode === "punch" ? <View style={styles.row}>
            <Action label={t(selected.enrollmentStatus === "active" ? "replace" : "enroll")} disabled={busy} onPress={() => { setMode("enroll"); setEnrollmentPhotos([]); setNotice(null); }} testID="face-enroll" />
            {selected.enrollmentStatus === "active" ? <Action label={t("revoke")} disabled={busy} onPress={() => setMode("revoke")} testID="face-revoke" /> : null}
          </View> : null}
          <View style={styles.row}>
            <Action label={t(state.syncing ? "syncing" : "sync")} disabled={state.syncing || busy} onPress={() => { void perform(() => runtime.sync(true), "networkError"); }} testID="face-sync" />
            <Action label={t("refresh")} disabled={!state.online || busy} onPress={() => { void perform(() => runtime.refresh(), "networkError"); }} />
          </View>
        </View>
      </View>
      <View style={styles.records}>
        <Text style={styles.heading}>{t("records")}</Text>
        {state.entries.slice(0, 30).map(entry => <View key={entry.eventGuid} style={styles.record} testID={`face-record-${entry.eventGuid}`}>
          <View style={styles.flex}><Text style={styles.employeeName}>{entry.employeeName} · {t(entry.punchType)}</Text><Text style={styles.muted}>{displayTime(entry.occurredAtUtc, locale, state.storeTimeZone)}</Text>
            {entry.reasonCode ? <Text style={styles.error}>{t(faceAttendanceErrorKey(entry.reasonCode))}</Text> : null}</View>
          {state.online && entry.serverStatus && ((state.canViewPhotos && runtime.getPhoto) || (state.canReview && runtime.review)) ? <Action label={t("reviewRecord")} onPress={() => setReviewing(entry)} testID={`face-review-${entry.eventGuid}`} /> : null}
          <Text style={[styles.pill, entry.serverStatus === "verified" ? styles.success : styles.warning]}>{t(statusKey(entry))}</Text>
        </View>)}
        {!state.entries.length ? <Text style={styles.empty}>{t("noRecords")}</Text> : null}
      </View>
    </PosKeyboardAwareScrollView>
    {reviewing ? <FaceRecordReview runtime={runtime} entry={reviewing} canViewPhotos={state.canViewPhotos === true} canReview={state.canReview === true} dateLabel={displayTime(reviewing.occurredAtUtc, locale, state.storeTimeZone)} t={t} onClose={closeReview} /> : null}
  </SafeAreaView>;
}

function Action({ label, onPress, primary = false, disabled = false, selected, testID }: Readonly<{
  label: string; onPress(): void; primary?: boolean; disabled?: boolean; selected?: boolean; testID?: string;
}>) {
  return <PosPressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ disabled, selected }} disabled={disabled}
    onPress={onPress} testID={testID} style={[styles.button, primary && styles.primaryButton, disabled && styles.disabled]}>
    <Text style={[styles.buttonText, primary && styles.primaryText]}>{label}</Text>
  </PosPressable>;
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: posColors.canvas }, flex: { flex: 1 },
  returnBar: { paddingHorizontal: 20, paddingTop: 8, backgroundColor: posColors.canvas, alignItems: "flex-start" },
  header: { flexDirection: "row", alignItems: "center", gap: 10, padding: 20, flexWrap: "wrap" },
  headerText: { flex: 1, minWidth: 200, gap: 4 }, title: { fontSize: 26, fontWeight: "700", color: posColors.ink },
  content: { paddingHorizontal: 20, paddingBottom: 24, gap: 18 }, workspace: { flexDirection: "row", gap: 18, alignItems: "stretch" }, narrow: { flexDirection: "column" },
  cameraPanel: { flex: 1.15, padding: 18, gap: 12, backgroundColor: posColors.surface, borderRadius: 12, borderWidth: 1, borderColor: posColors.border },
  heading: { color: posColors.ink, fontSize: 18, fontWeight: "700" }, muted: { color: posColors.mutedInk, fontSize: 14, lineHeight: 20 },
  cameraStage: { width: "100%", minHeight: 300, aspectRatio: 1.25, borderRadius: 12, overflow: "hidden", backgroundColor: posColors.ink, alignItems: "center", justifyContent: "center" },
  faceGuide: { width: "47%", height: "70%", borderWidth: 2, borderColor: "rgba(255,255,255,0.85)", borderRadius: 130 },
  cameraMessage: { padding: 24, alignItems: "center", gap: 20 }, cameraSpinner: { position: "absolute" },
  controls: { flex: 1, minWidth: 300, gap: 12, backgroundColor: posColors.surface, borderRadius: 12, padding: 18, borderWidth: 1, borderColor: posColors.border },
  search: { minHeight: 48, borderWidth: 1, borderColor: posColors.border, borderRadius: 8, paddingHorizontal: 12, fontSize: 16, color: posColors.ink },
  employees: { maxHeight: 192, minHeight: 90 }, employee: { minHeight: 58, padding: 10, marginBottom: 6, borderWidth: 1, borderColor: posColors.border, borderRadius: 8, flexDirection: "row", alignItems: "center", gap: 8 },
  selectedEmployee: { backgroundColor: posColors.orangeSoft, borderColor: posColors.orange }, employeeName: { color: posColors.ink, fontSize: 16, fontWeight: "600" },
  smallWarning: { fontSize: 12, color: posColors.yellow }, syncDate: { color: posColors.mutedInk, fontSize: 12 },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 10 }, button: { minHeight: 44, paddingVertical: 11, paddingHorizontal: 16, borderRadius: 8, borderWidth: 1, borderColor: posColors.border, justifyContent: "center", alignItems: "center", backgroundColor: posColors.surface },
  primaryButton: { backgroundColor: posColors.orange, borderColor: posColors.orange }, buttonText: { color: posColors.ink, fontSize: 15, fontWeight: "600" }, primaryText: { color: "white" }, disabled: { opacity: 0.45 },
  pill: { fontSize: 13, paddingVertical: 7, paddingHorizontal: 10, borderRadius: 6, overflow: "hidden", fontWeight: "600" }, success: { backgroundColor: posColors.greenSoft, color: posColors.green }, warning: { backgroundColor: posColors.yellowSoft, color: posColors.yellow },
  error: { fontSize: 14, color: posColors.red, lineHeight: 20 }, empty: { paddingVertical: 18, color: posColors.mutedInk },
  records: { backgroundColor: posColors.surface, padding: 18, borderRadius: 12, borderWidth: 1, borderColor: posColors.border }, record: { flexDirection: "row", gap: 12, alignItems: "center", paddingVertical: 14, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: posColors.border },
  result: { padding: 24, gap: 12, alignItems: "center" }, resultIcon: { color: "white", fontSize: 44 }, resultTitle: { color: "white", fontSize: 24, fontWeight: "700", textAlign: "center" },
  resultName: { color: "white", fontSize: 19 }, resultTime: { color: "white", fontSize: 26, fontVariant: ["tabular-nums"] }, resultHint: { color: "#E5ECF2", fontSize: 15, lineHeight: 22, textAlign: "center" },
});
