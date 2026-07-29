import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const moduleRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
);

async function read(relativePath) {
  return readFile(path.join(moduleRoot, relativePath), "utf8");
}

test("scene delegate uses the iPadOS noninteractive scene lifecycle", async () => {
  const source = await read("ios/HBExternalDisplaySceneDelegate.swift");

  assert.match(
    source,
    /session\.role\s*==\s*\.windowExternalDisplayNonInteractive/,
  );
  assert.match(source, /UIWindow\(windowScene:\s*windowScene\)/);
  assert.match(source, /isUserInteractionEnabled\s*=\s*false/);
  assert.match(source, /windowScene\(\s*_\s+windowScene:[\s\S]*didUpdate/);
  assert.doesNotMatch(source, /UIScreen\.screens/);
});

test("primary scene delegate adopts the AppDelegate main window without restarting the Expo root", async () => {
  const source = await read("ios/HBPrimarySceneDelegate.swift");

  assert.match(source, /session\.role\s*==\s*\.windowApplication/);
  assert.match(
    source,
    /protocol HBPrimaryWindowAppDelegate[\s\S]*var window: UIWindow\?/,
  );
  assert.match(
    source,
    /guard\s+let appDelegate\s*=\s*UIApplication\.shared\.delegate\s+as\?\s+HBPrimaryWindowAppDelegate,\s*let appWindow\s*=\s*appDelegate\.window/,
  );
  assert.match(
    source,
    /appWindow\.windowScene\s*=\s*windowScene/,
  );
  assert.match(source, /window\s*=\s*appWindow/);
  assert.match(source, /appWindow\.makeKeyAndVisible\(\)/);
  assert.match(
    source,
    /existingWindowScene\.activationState\s*!=\s*\.unattached/,
  );
  assert.match(source, /existingWindowScene\s*!==\s*windowScene/);
  assert.match(source, /openURLContexts/);
  assert.match(source, /continue userActivity/);
  assert.match(source, /UIApplication\.shared\.delegate\?\.application\?/);
  assert.doesNotMatch(source, /\bUIWindow\s*\(/);
  assert.doesNotMatch(source, /startReactNative/);
  assert.doesNotMatch(source, /didStartReactNative/);
  assert.doesNotMatch(source, /remainingAttempts/);
  assert.doesNotMatch(source, /factory\.startReactNative/);
  assert.doesNotMatch(source, /appDelegate\.window\s*=\s*nil/);
});

test("coordinator owns a monotonic revision gate and required status events", async () => {
  const source = await read("ios/HBExternalDisplayCoordinator.swift");

  assert.match(source, /snapshot\.revision\s*>\s*latestRevision/);
  for (const event of [
    "connected",
    "disconnected",
    "resolutionChanged",
    "ready",
    "failed",
    "enabledChanged",
  ]) {
    assert.match(source, new RegExp(`"${event}"`));
  }
});

test("native revision gate is scoped to a JS producer session and replays after render", async () => {
  const [coordinator, swiftModule] = await Promise.all([
    read("ios/HBExternalDisplayCoordinator.swift"),
    read("ios/HBExternalDisplayModule.swift"),
  ]);

  assert.match(swiftModule, /producerSessionID\s*=\s*UUID\(\)\.uuidString/);
  assert.match(swiftModule, /beginProducerSession/);
  assert.match(swiftModule, /endProducerSession/);
  assert.match(coordinator, /activeProducerSessionID/);
  assert.match(
    coordinator,
    /guard\s+producerSessionID\s*==\s*activeProducerSessionID/,
  );
  assert.match(
    coordinator,
    /beginProducerSession[\s\S]*latestRevision\s*=\s*-1/,
  );
  assert.match(
    coordinator,
    /markReactSurfaceRendered[\s\S]*snapshotEventSink\?\(latestSnapshot\.dictionary\)/,
  );
});

test("producer epoch reset clears transaction state and returns every external endpoint to waiting", async () => {
  const coordinator = await read("ios/HBExternalDisplayCoordinator.swift");

  assert.match(
    coordinator,
    /private func resetProducerEpoch\(\)[\s\S]*latestRevision\s*=\s*-1[\s\S]*latestSnapshot\s*=\s*nil[\s\S]*reactSurfaceReady\s*=\s*false/,
  );
  assert.match(
    coordinator,
    /private func resetProducerEpoch\(\)[\s\S]*controller\?\.stopMedia\(\)[\s\S]*controller\?\.removeReactSurface\(\)[\s\S]*controller\?\.showWaitingState\(\)/,
  );
  assert.match(
    coordinator,
    /if isNewProducerSession \{[\s\S]*resetProducerEpoch\(\)[\s\S]*\}/,
  );
  assert.match(
    coordinator,
    /func endProducerSession[\s\S]*guard\s+producerSessionID\s*==\s*activeProducerSessionID[\s\S]*resetProducerEpoch\(\)/,
  );
});

test("session invalidation can force native waiting state without relying on the RN snapshot bridge", async () => {
  const [coordinator, swiftModule] = await Promise.all([
    read("ios/HBExternalDisplayCoordinator.swift"),
    read("ios/HBExternalDisplayModule.swift"),
  ]);

  assert.match(swiftModule, /AsyncFunction\("forceBlank"\)/);
  assert.match(
    coordinator,
    /func forceBlank[\s\S]*guard\s+producerSessionID\s*==\s*activeProducerSessionID/,
  );
  assert.match(
    coordinator,
    /func forceBlank[\s\S]*latestSnapshot\s*=\s*nil[\s\S]*controller\?\.stopMedia\(\)[\s\S]*controller\?\.removeReactSurface\(\)[\s\S]*controller\?\.showWaitingState\(\)/,
  );
  assert.match(
    coordinator,
    /func forceBlank[\s\S]*reason:\s*"sensitive-content-reset"/,
  );
});

test("native snapshot is allowlisted and advertisements are local files only", async () => {
  const [models, viewController] = await Promise.all([
    read("ios/HBExternalDisplayModels.swift"),
    read("ios/HBExternalDisplayViewController.swift"),
  ]);
  const combined = `${models}\n${viewController}`;

  assert.match(models, /advert\.localUri/);
  assert.match(viewController, /url\.isFileURL/);
  assert.doesNotMatch(
    combined,
    /deviceAuthorization|accessToken|refreshToken|cardReference|customerId/,
  );
});

test("Expo module exports the external display bridge", async () => {
  const [moduleConfig, swiftModule] = await Promise.all([
    read("expo-module.config.json"),
    read("ios/HBExternalDisplayModule.swift"),
  ]);

  assert.match(moduleConfig, /"HBExternalDisplayModule"/);
  assert.match(swiftModule, /Name\("HBExternalDisplay"\)/);
  assert.match(swiftModule, /Events\("onStatusChanged",\s*"onSnapshotChanged"\)/);
  assert.match(swiftModule, /AsyncFunction\("publishSnapshot"\)/);
});

test("second React Native surface mounts only after registration and render handshakes", async () => {
  const [factory, coordinator, swiftModule] = await Promise.all([
    read("ios/HBExternalDisplayReactSurfaceFactory.swift"),
    read("ios/HBExternalDisplayCoordinator.swift"),
    read("ios/HBExternalDisplayModule.swift"),
  ]);

  assert.match(factory, /ExpoAppDelegate/);
  assert.match(factory, /rootViewFactory/);
  assert.match(factory, /withModuleName:\s*"HBExternalDisplay"/);
  assert.match(factory, /isUserInteractionEnabled\s*=\s*false/);
  assert.match(coordinator, /reactSurfaceReady/);
  assert.match(coordinator, /markReactSurfaceRendered/);
  assert.match(swiftModule, /AsyncFunction\("markReactSurfaceReady"\)/);
  assert.match(swiftModule, /AsyncFunction\("markReactSurfaceRendered"\)/);
  assert.match(swiftModule, /Events\("onStatusChanged",\s*"onSnapshotChanged"\)/);
});

test("JavaScript registers a noninteractive AppRegistry root", async () => {
  const source = await read(
    "../../src/core/peripherals/customer-display/native/external-display-react-surface.tsx",
  );

  assert.match(source, /AppRegistry\.registerComponent/);
  assert.match(source, /HBExternalDisplay/);
  assert.match(source, /markReactSurfaceReady/);
  assert.match(source, /markReactSurfaceRendered\(surfaceId\)/);
  assert.ok(
    source.indexOf('addListener(\n      "onSnapshotChanged"') <
      source.indexOf("markReactSurfaceRendered(surfaceId)"),
    "snapshot listener must be attached before native replay handshake",
  );
  assert.match(source, /pointerEvents="none"/);
  assert.match(source, /accessible=\{false\}/);
});
