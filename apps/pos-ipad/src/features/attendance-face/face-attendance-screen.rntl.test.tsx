import { beforeEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";
import { AppState, type AppStateStatus } from "react-native";

import type { FaceAttendanceEntry, FaceAttendanceRecordInput, FaceAttendanceRuntime, FaceAttendanceState } from "./face-attendance-contract";
import { FaceAttendanceScreen } from "./face-attendance-screen";
import { FaceRecordReview } from "./face-record-review";

let mockLanguage = "zh";
let mockPermission = { granted: true, canAskAgain: true };
const mockTakePhoto = jest.fn<() => Promise<{ base64: string; uri: string }>>();
const mockDelete = jest.fn();
const mockPermissionRequest = jest.fn<() => Promise<void>>();

jest.mock("react-i18next", () => ({ useTranslation: () => ({ i18n: { language: mockLanguage, resolvedLanguage: mockLanguage } }) }));
jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  return { useFocusEffect: (callback: () => void) => React.useEffect(callback, [callback]) };
});
jest.mock("expo-file-system", () => ({ Paths: { cache: { uri: "file:///cache/" } }, File: class { exists = true; delete = mockDelete; }, Directory: class { exists = false; } }));
jest.mock("expo-camera", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { View } = jest.requireActual<typeof import("react-native")>("react-native");
  return {
    useCameraPermissions: () => [mockPermission, mockPermissionRequest],
    CameraView: React.forwardRef(function MockCamera(props: any, ref) {
      React.useImperativeHandle(ref, () => ({ takePictureAsync: mockTakePhoto, getAvailablePictureSizesAsync: async () => ["640x480"] }), []);
      const ready = React.useRef(props.onCameraReady);
      React.useEffect(() => { ready.current(); }, []);
      return React.createElement(View, props);
    }),
  };
});

class Runtime implements FaceAttendanceRuntime {
  current: FaceAttendanceState = {
    employees: [{ userGuid: "employee-1", displayName: "陈明", employeeCode: "E001", enrollmentVersion: "1", enrollmentStatus: "active", lastPunchType: null, lastPunchTimeUtc: null }],
    entries: [], rosterVersion: "1", serverTimeUtc: "2026-09-09T00:00:00.000Z", lastSyncedAtUtc: "2026-09-09T00:00:00.000Z",
    storeTimeZone: "Australia/Brisbane", canManage: false, canViewPhotos: false, canReview: false, online: false, hasSyncedRoster: true, syncing: false, lastErrorCode: null,
  };
  state = () => this.current;
  captureTime = () => ({ capturedAtUtc: "2026-09-09T00:00:00.000Z", capturedUptimeMilliseconds: 123_456 });
  subscribe = () => () => undefined;
  refresh = jest.fn<() => Promise<void>>().mockResolvedValue();
  sync = jest.fn<() => Promise<void>>().mockResolvedValue();
  enroll = jest.fn<FaceAttendanceRuntime["enroll"]>().mockResolvedValue();
  getPhoto = jest.fn<NonNullable<FaceAttendanceRuntime["getPhoto"]>>().mockResolvedValue("/9j/AA==");
  review = jest.fn<NonNullable<FaceAttendanceRuntime["review"]>>().mockResolvedValue();
  revoke = jest.fn<FaceAttendanceRuntime["revoke"]>().mockResolvedValue();
  record = jest.fn<(input: FaceAttendanceRecordInput) => Promise<FaceAttendanceEntry>>().mockImplementation(async input => ({
    eventGuid: "event-1", userGuid: input.employee.userGuid, employeeName: input.employee.displayName,
    punchType: input.punchType, occurredAtUtc: input.capturedAtUtc, deviceObservedAtUtc: input.capturedAtUtc, localSequence: 1,
    timeTrusted: true, localState: "pending-upload", serverStatus: null, reasonCode: null,
  }));
}

describe("FaceAttendanceScreen", () => {
  beforeEach(() => {
    jest.spyOn(AppState, "addEventListener").mockImplementation(() => ({ remove: jest.fn() }));
    mockLanguage = "zh"; mockPermission = { granted: true, canAskAgain: true };
    mockTakePhoto.mockReset().mockResolvedValue({ base64: "aW1hZ2U=", uri: "file:///cache/photo.jpg" });
    mockDelete.mockReset(); mockPermissionRequest.mockReset().mockResolvedValue();
    Object.defineProperty(AppState, "currentState", { value: "active", configurable: true });
  });

  it("必须先选姓名，前置拍照保存后显示待同步，下一位清空姓名", async () => {
    const runtime = new Runtime();
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    expect(screen.getByTestId("face-front-camera").props.facing).toBe("front");
    expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(true);
    await fireEvent.press(screen.getByTestId("face-employee-employee-1"));
    await fireEvent.press(screen.getByTestId("face-type-clockOut"));
    await waitFor(() => expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId("face-capture"));
    await waitFor(() => expect(screen.getByText("已记录，待同步")).toBeTruthy());
    expect(runtime.record).toHaveBeenCalledWith(expect.objectContaining({ employee: runtime.current.employees[0], punchType: "clockOut", photoBase64: "aW1hZ2U=", capturedAtUtc: "2026-09-09T00:00:00.000Z", capturedUptimeMilliseconds: 123_456 }));
    expect(mockDelete).toHaveBeenCalledTimes(1);
    expect(screen.queryByTestId("face-front-camera")).toBeNull();
    await fireEvent.press(screen.getByTestId("face-next"));
    expect(screen.getByTestId("face-employee-employee-1").props.accessibilityState.selected).toBe(false);
    expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(true);
    await screen.unmount();
  });

  it("磁盘写入失败不显示已保存，并且拍摄中的重复点击只保存一次", async () => {
    const runtime = new Runtime();
    let reject!: (error: Error) => void;
    runtime.record.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail; }));
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    await fireEvent.press(screen.getByTestId("face-employee-employee-1"));
    await waitFor(() => expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId("face-capture"));
    await fireEvent.press(screen.getByTestId("face-capture"));
    expect(runtime.record).toHaveBeenCalledTimes(1);
    await act(async () => reject(new Error("SQLITE_FULL")));
    await waitFor(() => expect(screen.getByText("未记录成功，请检查存储空间后重试。")).toBeTruthy());
    expect(screen.queryByTestId("face-saved-result")).toBeNull();
    await screen.unmount();
  });

  it("离线隐藏录入按钮，店长在线三张照片录入不生成打卡", async () => {
    const runtime = new Runtime();
    runtime.current = { ...runtime.current, online: true, canManage: true };
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    await fireEvent.press(screen.getByTestId("face-employee-employee-1"));
    await fireEvent.press(screen.getByTestId("face-enroll"));
    for (let index = 0; index < 3; index++) {
      await waitFor(() => expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(false));
      await fireEvent.press(screen.getByTestId("face-capture"));
    }
    await waitFor(() => expect(screen.getByTestId("face-enroll-save")).toBeTruthy());
    await fireEvent.press(screen.getByTestId("face-enroll-save"));
    expect(runtime.enroll).toHaveBeenCalledWith({ userGuid: "employee-1", photosBase64: ["aW1hZ2U=", "aW1hZ2U=", "aW1hZ2U="] });
    expect(runtime.record).not.toHaveBeenCalled();
    await screen.unmount();
  });

  it("拒绝相机权限时提供授权入口，不可拍照，英文文案保持单语言", async () => {
    mockPermission = { granted: false, canAskAgain: true }; mockLanguage = "en";
    const screen = await render(<FaceAttendanceScreen runtime={new Runtime()} onBack={() => undefined} onShowQr={() => undefined} />);
    expect(screen.queryByTestId("face-front-camera")).toBeNull();
    expect(screen.getByText("Face attendance")).toBeTruthy();
    expect(screen.queryByText("人脸打卡")).toBeNull();
    await fireEvent.press(screen.getByText("Allow camera"));
    expect(mockPermissionRequest).toHaveBeenCalledTimes(1);
    await screen.unmount();
  });

  it("进入后台卸载相机，恢复前台重新就绪", async () => {
    let listener!: (state: AppStateStatus) => void;
    const spy = jest.spyOn(AppState, "addEventListener").mockImplementation((_event, callback) => {
      listener = callback; return { remove: jest.fn() };
    });
    const screen = await render(<FaceAttendanceScreen runtime={new Runtime()} onBack={() => undefined} onShowQr={() => undefined} />);
    expect(screen.getByTestId("face-front-camera")).toBeTruthy();
    await act(async () => listener("background"));
    expect(screen.queryByTestId("face-front-camera")).toBeNull();
    await act(async () => listener("active"));
    await waitFor(() => expect(screen.getByTestId("face-front-camera")).toBeTruthy());
    await screen.unmount(); spy.mockRestore();
  });
  it("普通员工不显示处理入口，店长查看私有照片并填写原因后确认原事件", async () => {
    const runtime = new Runtime();
    runtime.current = { ...runtime.current, entries: [{ eventGuid: "review-1", userGuid: "employee-1", employeeName: "陈明", punchType: "clockIn", occurredAtUtc: "2026-09-09T00:00:00.000Z", deviceObservedAtUtc: "2026-09-09T00:00:00.000Z", localSequence: 1, timeTrusted: false, localState: "pending-review", serverStatus: "needsReview", reasonCode: "DEVICE_TIME_UNTRUSTED" }] };
    const normal = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    expect(normal.queryByTestId("face-review-review-1")).toBeNull();
    await normal.unmount();
    runtime.current = { ...runtime.current, canManage: true, canViewPhotos: true, canReview: true, online: true };
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    await fireEvent.press(screen.getByTestId("face-review-review-1"));
    await waitFor(() => expect(screen.getByTestId("face-review-photo")).toBeTruthy());
    expect(runtime.getPhoto).toHaveBeenCalledWith("review-1");
    expect(screen.queryByTestId("face-front-camera")).toBeNull();
    expect(screen.getByTestId("face-review-approve").props.accessibilityState.disabled).toBe(true);
    await fireEvent.changeText(screen.getByTestId("face-review-reason"), "已核对原始时间");
    await fireEvent.press(screen.getByTestId("face-review-approve"));
    await waitFor(() => expect(runtime.review).toHaveBeenCalledWith({ eventGuid: "review-1", decision: "approve", reason: "已核对原始时间" }));
    await waitFor(() => expect(screen.getByText("处理结果已保存")).toBeTruthy());
    await fireEvent.press(screen.getByTestId("face-review-close"));
    expect(screen.queryByTestId("face-review-photo")).toBeNull();
    await screen.unmount();
  });

  it("原始相机照片删除失败时不进入本地队列，也不显示记录成功", async () => {
    const runtime = new Runtime();
    mockDelete.mockImplementation(() => { throw new Error("permission denied"); });
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    await fireEvent.press(screen.getByTestId("face-employee-employee-1"));
    await waitFor(() => expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId("face-capture"));
    expect(runtime.record).not.toHaveBeenCalled();
    expect(screen.queryByTestId("face-saved-result")).toBeNull();
    await screen.unmount();
  });

  it("录入拍摄过程中进入后台，迟到的照片不会恢复录入状态", async () => {
    let appState!: (state: AppStateStatus) => void;
    jest.spyOn(AppState, "addEventListener").mockImplementation((_event, listener) => { appState = listener; return { remove: jest.fn() }; });
    let resolvePhoto!: (value: { base64: string; uri: string }) => void;
    mockTakePhoto.mockImplementation(() => new Promise(resolve => { resolvePhoto = resolve; }));
    const runtime = new Runtime(); runtime.current = { ...runtime.current, online: true, canManage: true };
    const screen = await render(<FaceAttendanceScreen runtime={runtime} onBack={() => undefined} onShowQr={() => undefined} />);
    await fireEvent.press(screen.getByTestId("face-employee-employee-1"));
    await fireEvent.press(screen.getByTestId("face-enroll"));
    await waitFor(() => expect(screen.getByTestId("face-capture").props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId("face-capture"));
    await act(async () => appState("background"));
    await act(async () => resolvePhoto({ base64: "aW1hZ2U=", uri: "file:///cache/photo.jpg" }));
    expect(screen.queryByTestId("face-front-camera")).toBeNull();
    expect(screen.queryByText("店长现场录入")).toBeNull();
    expect(runtime.enroll).not.toHaveBeenCalled();
    expect(runtime.record).not.toHaveBeenCalled();
    expect(mockDelete).toHaveBeenCalledTimes(1);
    await screen.unmount();
  });

  it("照片查看权限失效立即清空已加载照片，全部管理权限失效关闭详情", async () => {
    const runtime = new Runtime();
    const entry: FaceAttendanceEntry = { eventGuid: "review-1", userGuid: "employee-1", employeeName: "陈明", punchType: "clockIn", occurredAtUtc: "2026-09-09T00:00:00.000Z", deviceObservedAtUtc: "2026-09-09T00:00:00.000Z", localSequence: 1, timeTrusted: false, localState: "pending-review", serverStatus: "needsReview", reasonCode: "DEVICE_TIME_UNTRUSTED" };
    const onClose = jest.fn();
    const props = { runtime, entry, dateLabel: "2026-09-09 10:00", t: (key: any) => key, onClose };
    const screen = await render(<FaceRecordReview {...props} canViewPhotos canReview />);
    await waitFor(() => expect(screen.getByTestId("face-review-photo")).toBeTruthy());
    await screen.rerender(<FaceRecordReview {...props} canViewPhotos={false} canReview />);
    expect(screen.queryByTestId("face-review-photo")).toBeNull();
    expect(onClose).not.toHaveBeenCalled();
    await screen.rerender(<FaceRecordReview {...props} canViewPhotos={false} canReview={false} />);
    expect(screen.queryByTestId("face-review-photo")).toBeNull();
    expect(onClose).toHaveBeenCalledTimes(1);
    await screen.unmount();
  });

});
