import { useEffect, useRef, useState } from "react";
import { AppState, Image, Modal, StyleSheet, Text, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import type { FaceAttendanceEntry, FaceAttendanceRuntime } from "./face-attendance-contract";
import { faceAttendanceErrorKey, type FaceAttendanceCopyKey } from "./face-attendance-copy";

import { PosKeyboardAwareScrollView, PosKeyboardAwareTextInput } from "@/ui/controls/pos-keyboard-aware-scroll-view";
import { PosPressable } from "@/ui/controls/pos-pressable";
import { posColors } from "@/ui/theme";

type Props = Readonly<{ runtime: FaceAttendanceRuntime; entry: FaceAttendanceEntry; dateLabel: string;
  t(key: FaceAttendanceCopyKey): string; canViewPhotos: boolean; canReview: boolean; onClose(): void }>;

export function FaceRecordReview({ runtime, entry, dateLabel, t, canViewPhotos, canReview, onClose }: Props) {
  const [photo, setPhoto] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [done, setDone] = useState(false);
  const [message, setMessage] = useState<FaceAttendanceCopyKey | null>(null);
  useEffect(() => {
    let active = true;
    // 授权照片只存在当前视图内存；退出或切后台立即清空，不建立本地相册/文件副本。
    if (!canViewPhotos) { setPhoto(null); setLoading(false); }
    else void runtime.getPhoto?.(entry.eventGuid).then(value => { if (active) setPhoto(value); })
      .catch(error => { if (active) setMessage(faceAttendanceErrorKey(error?.code ?? error?.message)); })
      .finally(() => { if (active) setLoading(false); });
    const listener = AppState.addEventListener("change", state => { if (state !== "active") { active = false; setPhoto(null); onClose(); } });
    return () => { active = false; listener.remove(); };
  }, [runtime, entry.eventGuid, canViewPhotos, onClose]);
  useEffect(() => {
    // 切换收银员会同步撤销能力；失去全部权限时关闭上一个人的私有记录。
    if (!canViewPhotos && !canReview) onClose();
  }, [canViewPhotos, canReview, onClose]);

  async function review(decision: "approve" | "reject") {
    if (busyRef.current || !reason.trim() || !runtime.review) return;
    busyRef.current = true; setBusy(true); setMessage(null);
    try { await runtime.review({ eventGuid: entry.eventGuid, decision, reason: reason.trim() }); setDone(true); setMessage("reviewSaved"); }
    catch (error) { setMessage(faceAttendanceErrorKey((error as { code?: string; message?: string })?.code ?? (error as Error)?.message)); }
    finally { busyRef.current = false; setBusy(false); }
  }
  return <Modal visible transparent={false} animationType="fade" onRequestClose={() => { if (!busy) onClose(); }}>
    <SafeAreaView style={styles.safe}>
      <PosKeyboardAwareScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>{entry.employeeName} · {t(entry.punchType)}</Text>
        <Text style={styles.body}>{dateLabel}</Text>
        {canViewPhotos && loading ? <Text style={styles.body}>{t("loadingPhoto")}</Text> : canViewPhotos && photo ? <Image accessibilityLabel={t("reviewRecord")}
          source={{ uri: `data:image/jpeg;base64,${photo}` }} style={styles.photo} resizeMode="contain" testID="face-review-photo" /> : null}
        {entry.reasonCode ? <Text style={styles.body}>{t(faceAttendanceErrorKey(entry.reasonCode))}</Text> : null}
        <Text style={styles.body}>{t("reviewHelp")}</Text>
        {message ? <Text accessibilityLiveRegion="polite" style={styles.message}>{t(message)}</Text> : null}
        {canReview && entry.serverStatus === "needsReview" && !done ? <>
          <PosKeyboardAwareTextInput accessibilityLabel={t("reviewReason")} placeholder={t("reviewReason")}
            value={reason} onChangeText={setReason} maxLength={500} editable={!busy} style={styles.input} testID="face-review-reason" />
          <View style={styles.row}>
            <Button label={t("confirmOriginal")} disabled={busy || !reason.trim()} onPress={() => { void review("approve"); }} testID="face-review-approve" />
            <Button label={t("rejectRecord")} disabled={busy || !reason.trim()} onPress={() => { void review("reject"); }} testID="face-review-reject" />
          </View>
        </> : null}
        <Button label={t("close")} disabled={busy} onPress={onClose} testID="face-review-close" />
      </PosKeyboardAwareScrollView>
    </SafeAreaView>
  </Modal>;
}
function Button({ label, disabled, onPress, testID }: Readonly<{ label: string; disabled: boolean; onPress(): void; testID: string }>) {
  return <PosPressable accessibilityRole="button" accessibilityLabel={label} disabled={disabled} accessibilityState={{ disabled }} onPress={onPress}
    testID={testID} style={[styles.button, disabled && styles.disabled]}><Text style={styles.buttonText}>{label}</Text></PosPressable>;
}
const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: posColors.canvas }, content: { padding: 24, gap: 16, maxWidth: 800, width: "100%", alignSelf: "center" },
  title: { color: posColors.ink, fontSize: 24, fontWeight: "700" }, body: { color: posColors.mutedInk, fontSize: 16, lineHeight: 24 },
  photo: { width: "100%", height: 280, backgroundColor: posColors.ink, borderRadius: 10 },
  input: { minHeight: 48, borderWidth: 1, borderColor: posColors.border, padding: 12, borderRadius: 8, color: posColors.ink },
  row: { flexDirection: "row", flexWrap: "wrap", gap: 12 }, button: { minHeight: 44, padding: 12, borderRadius: 8, backgroundColor: posColors.orange, alignItems: "center" },
  buttonText: { color: "white", fontSize: 16, fontWeight: "600" }, disabled: { opacity: 0.45 }, message: { color: posColors.red, fontSize: 16 },
});
