export const faceAttendanceEnglish = {
  title: "Face attendance", subtitle: "Select your name, confirm the punch type, then take a photo.",
  back: "Back to sales", qr: "QR & audit", face: "Face attendance", online: "Online", offline: "Offline",
  select: "1  Select your name", search: "Name or employee number", noEmployees: "No employees available",
  setup: "Connect to sync this store's employees before first use.", notEnrolled: "Face not enrolled",
  type: "2  Confirm punch type", clockIn: "Clock in", clockOut: "Clock out", suggestion: "Suggested from synced and locally saved records. Please confirm.",
  camera: "3  Face the front camera", cameraHint: "Only one person in frame. Keep your face clear and well lit.",
  allowCamera: "Allow camera", cameraPermission: "Camera access is needed to take an attendance photo.", settings: "Open settings",
  cameraUnavailable: "Camera unavailable. Check the device camera and try again.", retry: "Try again",
  photo: "Confirm and take photo", saving: "Saving…", next: "Next employee", sync: "Sync now", syncing: "Syncing…",
  refresh: "Refresh employees", lastSync: "Employees last synced", records: "Recent records", noRecords: "No attendance records on this device",
  pending: "Recorded, awaiting sync", verifying: "Uploaded, awaiting verification", confirmed: "Attendance confirmed", review: "Needs attention", rejected: "Verification unsuccessful",
  offlineHint: "Saved securely on this iPad. Photos and data upload when the app reconnects.",
  savedHint: "The original photo time and confirmed punch type are preserved during sync.",
  failedSave: "Not recorded. Check available storage and try again.",
  enrolled: "Face enrollment saved", enroll: "Enroll face", replace: "Re-enroll face", revoke: "Revoke enrollment",
  enrollTitle: "Manager enrollment", enrollHint: "Confirm the selected employee. Take three clear front-facing photos.",
  captureEnrollment: "Take photo", photoCount: "Photos captured", finishEnrollment: "Save enrollment", cancel: "Cancel",
  revokeConfirm: "Confirm revocation", revokeHint: "This employee will need a new enrollment before using face attendance.",
  revoked: "Face enrollment revoked", onlineManager: "Sign in as a manager online again, then refresh employees.",
  networkError: "Unable to reach the server. Saved records remain on this iPad.",
  faceMismatch: "The photo did not match the selected employee. Please try again.",
  quality: "Use one clear, well-lit face in the photo, then try again.",
  timeReview: "The original capture time needs manager review.",
  conflict: "This record conflicts with existing attendance and needs manager review.",
  enrollmentChanged: "Employee or enrollment details changed. Manager review is required.",
  genericFailure: "The request was unsuccessful. Please retry or ask your manager.",
  employeeRequired: "Select an employee before taking a photo.", photoTooLarge: "Photo could not be saved. Please take another photo.",
  reviewRecord: "View / review", reviewReason: "Review reason", confirmOriginal: "Verify and confirm original time",
  rejectRecord: "Reject record", close: "Close", reviewHelp: "For timeline conflicts or changed employee details, use attendance corrections and approvals in the management portal.",
  managerRejected: "The manager did not confirm this record. Please contact your manager.", featureDisabled: "Face attendance is not enabled for this store. Please use QR attendance.", verificationRetry: "Verification is pending. The server will retry automatically.",
  reviewSaved: "Review saved", photoExpired: "The attendance photo has expired.", loadingPhoto: "Loading private photo…",
  needsEnrollment: "Ask a manager to enroll this employee's face first.",
} as const;

export type FaceAttendanceCopyKey = keyof typeof faceAttendanceEnglish;
export const faceAttendanceChinese: Record<FaceAttendanceCopyKey, string> = {
  title: "人脸打卡", subtitle: "选择姓名，确认上下班，再拍照记录。",
  back: "返回收银", qr: "二维码与审计", face: "人脸打卡", online: "已联网", offline: "离线",
  select: "1  选择姓名", search: "姓名或员工编号", noEmployees: "暂无可选员工",
  setup: "首次使用请联网同步本店员工名单。", notEnrolled: "未录入人脸",
  type: "2  确认打卡类型", clockIn: "上班", clockOut: "下班", suggestion: "根据已同步及本机记录建议，请确认本次打卡类型。",
  camera: "3  面向前置摄像头", cameraHint: "画面中只保留一人，保持面部清晰、光线充足。",
  allowCamera: "允许使用相机", cameraPermission: "需要相机权限才能拍摄打卡照片。", settings: "打开设置",
  cameraUnavailable: "相机无法使用，请检查设备相机后重试。", retry: "重试",
  photo: "确认并拍照", saving: "正在保存…", next: "下一位", sync: "立即同步", syncing: "正在同步…",
  refresh: "刷新员工", lastSync: "员工资料同步于", records: "最近记录", noRecords: "本机暂无打卡记录",
  pending: "已记录，待同步", verifying: "已上传，待核验", confirmed: "打卡已确认", review: "需处理", rejected: "核验未通过",
  offlineHint: "记录已安全保存在本机，应用恢复联网后自动上传照片和数据。",
  savedHint: "同步时保留拍照时间和已确认的上下班类型。",
  failedSave: "未记录成功，请检查存储空间后重试。",
  enrolled: "人脸录入已保存", enroll: "录入人脸", replace: "重新录入", revoke: "撤销录入",
  enrollTitle: "店长现场录入", enrollHint: "请核对所选员工，拍摄三张清晰正脸照片。",
  captureEnrollment: "拍摄照片", photoCount: "已拍摄", finishEnrollment: "保存录入", cancel: "取消",
  revokeConfirm: "确认撤销", revokeHint: "撤销后，该员工需要重新录入才能使用人脸打卡。",
  revoked: "已撤销人脸录入", onlineManager: "请店长重新在线登录，再刷新员工资料。",
  networkError: "暂时无法连接服务器，本机已保存记录会继续保留。",
  faceMismatch: "照片与所选员工不匹配，请重新拍摄。",
  quality: "请确保只有一张清晰人脸且光线充足，然后重新拍摄。",
  timeReview: "原始拍照时间需要店长复核。", conflict: "记录与已有考勤冲突，需要店长复核。",
  enrollmentChanged: "员工或人脸录入资料发生变化，需要店长复核。",
  genericFailure: "操作未成功，请重试或联系店长。", employeeRequired: "请先选择员工，再拍照。",
  reviewRecord: "查看／处理", reviewReason: "处理原因", confirmOriginal: "核验并确认原时间",
  rejectRecord: "不予确认", close: "关闭", reviewHelp: "时间线冲突或员工资料变更，请在管理后台通过补卡／审批处理。",
  managerRejected: "店长未确认此记录，请联系店长。", featureDisabled: "本店尚未启用人脸打卡，请使用二维码打卡。", verificationRetry: "核验尚未完成，服务器会自动重试。",
  reviewSaved: "处理结果已保存", photoExpired: "打卡照片已超过保留期限。", loadingPhoto: "正在读取照片…",
  photoTooLarge: "照片未能保存，请重新拍摄。", needsEnrollment: "请先由店长为该员工录入人脸。",
};

export function faceAttendanceErrorKey(code: string | null | undefined): FaceAttendanceCopyKey {
  const value = (code ?? "").toLowerCase();
  if (/manager_rejected/.test(value)) return "managerRejected";
  if (/attendance_disabled|gateway_disabled/.test(value)) return "featureDisabled";
  if (/recognition_retry/.test(value)) return "verificationRetry";
  if (/photo_expired/.test(value)) return "photoExpired";
  if (/manager|permission|forbidden|auth_required/.test(value)) return "onlineManager";
  if (/no_face|multiple_faces|poor_quality|invalid_photo|photo_invalid|faces_inconsistent/.test(value)) return "quality";
  if (/mismatch|not_matched|match_failed|below_threshold/.test(value)) return "faceMismatch";
  if (/time|clock|anchor/.test(value)) return "timeReview";
  if (/conflict|sequence|overlap|out_of_order|segment_limit|day_complete|no_schedule/.test(value)) return "conflict";
  if (/changed|version|revoked/.test(value)) return "enrollmentChanged";
  if (/not_enrolled|enrollment_required/.test(value)) return "needsEnrollment";
  if (/network|gateway|unavailable|offline|http_response|request_aborted|timeout|transport/.test(value)) return "networkError";
  if (/photo_too_large|too_large/.test(value)) return "photoTooLarge";
  return "genericFailure";
}
