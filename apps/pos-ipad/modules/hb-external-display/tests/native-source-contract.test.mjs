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

test("scene delegate selects the highest external screen mode before creating its window", async () => {
  const source = await read("ios/HBExternalDisplaySceneDelegate.swift");

  assert.match(source, /windowScene\.screen\.availableModes/);
  assert.match(
    source,
    /\$0\.size\.width\s*\*\s*\$0\.size\.height/,
  );
  assert.match(source, /windowScene\.screen\.currentMode\s*=\s*displayMode/);
  assert.match(
    source,
    /currentMode\s*=\s*displayMode[\s\S]*UIWindow\(windowScene:\s*windowScene\)/,
  );
});

test("app delegate subscriber selects the highest mode as an external screen connects", async () => {
  const [source, config] = await Promise.all([
    read("ios/HBExternalDisplayAppDelegateSubscriber.swift"),
    read("expo-module.config.json"),
  ]);

  assert.match(
    config,
    /"appDelegateSubscribers"\s*:\s*\["HBExternalDisplayAppDelegateSubscriber"\]/,
  );
  assert.match(source, /UIScreen\.didConnectNotification/);
  assert.match(source, /notification\.object\s+as\?\s+UIScreen/);
  assert.match(source, /screen\.availableModes\.max/);
  assert.match(source, /screen\.currentMode\s*=\s*displayMode/);
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

test("native snapshot accepts optional unit price and summary without breaking legacy fields", async () => {
  const source = await read("ios/HBExternalDisplayModels.swift");

  assert.match(source, /var unitPrice: HBExternalDisplayMoneyRecord\?/);
  assert.match(source, /let unitPrice: HBExternalDisplayMoney\?/);
  assert.match(source, /payload\["unitPrice"\]\s*=\s*unitPrice\.dictionary/);
  assert.match(source, /var summary: HBExternalDisplaySummaryRecord\?/);
  assert.match(source, /let summary: HBExternalDisplaySummary\?/);
  assert.match(source, /payload\["summary"\]\s*=\s*summary\.dictionary/);
  assert.match(source, /struct HBExternalDisplaySummary/);
  assert.match(source, /let itemQuantity: String/);
  assert.match(source, /let skuCount: Int/);
  assert.match(source, /let subtotal: HBExternalDisplayMoney/);
  assert.match(source, /"itemQuantity": itemQuantity/);
  assert.match(source, /"skuCount": skuCount/);
  assert.match(source, /"subtotal": subtotal\.dictionary/);
  assert.match(
    source,
    /unitPrice == nil[\s\S]*\? nil[\s\S]*: try unitPrice\?\.validated/,
  );
  assert.match(
    source,
    /summary == nil \? nil : try summary\?\.validated/,
  );
  assert.match(source, /var visibleItemStart: Int\?/);
  assert.match(source, /let visibleItemStart: Int\?/);
  assert.match(source, /payload\["visibleItemStart"\]\s*=\s*visibleItemStart/);
  assert.match(
    source,
    /let maximumVisibleItemStart = max\(0, items\.count - 12\)/,
  );
  assert.match(source, /visibleItemStart\s*<=\s*maximumVisibleItemStart/);
  assert.match(
    source,
    /throw HBExternalDisplayValidationError\.invalid\("visibleItemStart"\)/,
  );
});

test("UIKit fallback matches reference design and keeps idle advert full-screen", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  // 48pt title bar without any close control.
  assert.match(
    source,
    /windowTitleLabel\.text\s*=\s*localizedText\(english:\s*"Customer Display",\s*chinese:\s*"客显"\)/,
  );
  assert.match(
    source,
    /windowTitleLabel\.font\s*=\s*\.systemFont\(ofSize:\s*21,\s*weight:\s*\.bold\)/,
  );
  assert.match(source, /titleBar\.heightAnchor\.constraint\(equalToConstant:\s*48\)/);
  assert.doesNotMatch(source, /closeButton|dismissButton|UIButton/);

  assert.match(
    source,
    /snapshot\.mode\s*==\s*\.idle\s*&&\s*snapshot\.items\.isEmpty\s*&&\s*snapshot\.advert\s*!=\s*nil/,
  );
  assert.match(source, /orderPanel\.isHidden\s*=\s*fullScreenAdvert/);
  assert.match(
    source,
    /advertContainer\.layer\.cornerRadius\s*=\s*fullScreenAdvert\s*\?\s*0\s*:\s*12/,
  );
  assert.match(
    source,
    /advertContainer\.backgroundColor\s*=\s*fullScreenAdvert\s*\?\s*\.clear\s*:\s*UIColor\.white\.withAlphaComponent\(0\.035\)/,
  );
  assert.match(source, /UIView\.performWithoutAnimation/);

  for (const edge of ["leading", "trailing", "top", "bottom"]) {
    assert.match(
      source,
      new RegExp(
        `advertContainer\\.${edge}Anchor\\.constraint\\(equalTo: view\\.${edge}Anchor\\)`,
      ),
    );
  }

  assert.match(
    source,
    /orderPanel\.leadingAnchor\.constraint\([\s\S]*view\.safeAreaLayoutGuide\.leadingAnchor,[\s\S]*constant:\s*24/,
  );
  assert.match(
    source,
    /advertContainer\.trailingAnchor\.constraint\([\s\S]*view\.safeAreaLayoutGuide\.trailingAnchor,[\s\S]*constant:\s*-24/,
  );
  assert.match(
    source,
    /summaryPanel\.bottomAnchor\.constraint\([\s\S]*view\.safeAreaLayoutGuide\.bottomAnchor,[\s\S]*constant:\s*-24/,
  );
  assert.match(
    source,
    /advertContainer\.leadingAnchor\.constraint\(\s*equalTo:\s*orderPanel\.trailingAnchor,\s*constant:\s*18\s*\)/,
  );
  assert.match(
    source,
    /advertContainer\.widthAnchor\.constraint\(\s*equalTo:\s*orderPanel\.widthAnchor\s*\)/,
  );
  assert.match(
    source,
    /summaryPanel\.heightAnchor\.constraint\(equalToConstant:\s*132\)/,
  );
  assert.match(
    source,
    /summaryMetricsContainer\.widthAnchor\.constraint\([\s\S]*multiplier:\s*0\.47/,
  );
  assert.match(
    source,
    /amountDueContainer\.widthAnchor\.constraint\([\s\S]*multiplier:\s*0\.26/,
  );
  assert.match(
    source,
    /summarySections\.addArrangedSubview\(statusRegion\)/,
  );
});

test("UIKit fallback renders four-column order table and a full-width summary", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(source, /orderTitleLabel\.text\s*=\s*localizedText\(english:\s*"Your order",\s*chinese:\s*"您的订单"\)/);
  for (const title of ["Product", "Qty", "Unit price", "Amount"]) {
    assert.match(source, new RegExp(`english:\\s*"${title}"`));
  }
  assert.match(source, /label\.font\s*=\s*\.systemFont\(ofSize:\s*16,\s*weight:\s*\.semibold\)/);
  assert.match(source, /label\.textColor\s*=\s*UIColor\.white\.withAlphaComponent\(0\.88\)/);
  assert.match(source, /unitPriceLabel\.text\s*=\s*unitPriceText\(for:\s*item\)/);
  assert.match(source, /unitPriceLabel\.textColor\s*=\s*\.white/);
  assert.match(source, /quantityLabel\.textColor\s*=\s*UIColor\.white\.withAlphaComponent\(0\.66\)/);
  assert.match(source, /subtotalValueLabel\.text\s*=\s*format\(summary\.subtotal\)/);
  assert.match(
    source,
    /amountDueLabel\.text\s*=\s*localizedText\(english:\s*"Amount due",\s*chinese:\s*"应付总额"\)/,
  );
  assert.match(source, /totalValueLabel\.font\s*=\s*\.monospacedDigitSystemFont\(\s*ofSize:\s*42/);
  assert.match(
    source,
    /private func visibleItemWindow\([\s\S]*for snapshot: HBExternalDisplaySnapshot[\s\S]*-> HBExternalDisplayItemWindow/,
  );
  assert.match(source, /replaceItemRows\(with:\s*window\.items\)/);
  assert.match(source, /moreItemsLabel\.text\s*=\s*moreItemsText\(/);
});

test("UIKit fallback keeps the order heading and empty message at the top", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(
    source,
    /orderTitleLabel\.setContentHuggingPriority\(\.required,\s*for:\s*\.vertical\)/,
  );
  assert.match(
    source,
    /if items\.isEmpty \{[\s\S]*itemStack\.distribution\s*=\s*\.fill[\s\S]*emptyTopSpacer\.heightAnchor\.constraint\(equalToConstant:\s*22\)/,
  );
  assert.match(
    source,
    /itemStack\.distribution\s*=\s*\.fill[\s\S]*for item in items[\s\S]*cell\.heightAnchor\.constraint\(equalToConstant:\s*32\)/,
  );
  assert.match(
    source,
    /flexibleSpacer\.setContentHuggingPriority\(\.defaultLow,\s*for:\s*\.vertical\)[\s\S]*itemStack\.addArrangedSubview\(flexibleSpacer\)/,
  );
});

test("UIKit fallback slices from visibleItemStart and shows window overflow beside the title", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(source, /start = max\(itemCount - 12, 0\)/);
  assert.match(source, /Array\(snapshot\.items\[start\.\.<end\]\)/);
  assert.match(source, /hiddenAbove:\s*start/);
  assert.match(source, /hiddenBelow:\s*max\(itemCount - end, 0\)/);
  assert.match(
    source,
    /private func moreItemsText\(hiddenAbove: Int, hiddenBelow: Int\) -> String\?/,
  );
  assert.match(source, /hiddenAbove > 0 && hiddenBelow > 0/);
  assert.match(source, /hiddenAbove > 0/);
  assert.match(source, /hiddenBelow > 0/);
  assert.match(source, /return nil/);
  assert.match(source, /orderTitleRow\.addArrangedSubview\(moreItemsLabel\)/);
  assert.doesNotMatch(source, /orderPanel\.addArrangedSubview\(moreItemsLabel\)/);
  assert.match(source, /english:\s*"\\\(hiddenAbove\) earlier · \\\(hiddenBelow\) later"/);
  assert.match(source, /chinese:\s*"上方 \\\(hiddenAbove\) 件 · 下方 \\\(hiddenBelow\) 件"/);
  assert.match(source, /english:\s*"\\\(hiddenAbove\) earlier"/);
  assert.match(source, /chinese:\s*"前面还有 \\\(hiddenAbove\) 件"/);
  assert.match(source, /english:\s*"\\\(hiddenBelow\) later"/);
  assert.match(source, /chinese:\s*"后面还有 \\\(hiddenBelow\) 件"/);
});

test("UIKit fallback renders a localized status card per mode with change on success", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  for (const copy of [
    "Ready when you are",
    "Scan an item to begin",
    "Ready to pay",
    "Please follow the cashier's instructions",
    "Payment in progress",
    "Please follow the terminal prompts",
    "Your change",
    "Payment complete",
  ]) {
    assert.match(source, new RegExp(`english:\\s*"${copy.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}"`));
  }
  assert.match(source, /statusSubtitleLabel\.text\s*=\s*format\(change\)/);
  assert.match(source, /change\.cents\s*!=\s*0/);
  assert.match(source, /english:\s*"Change \\\(format\(change\)\)"/);
  assert.match(source, /chinese:\s*"找零 \\\(format\(change\)\)"/);
});

test("UIKit fallback always selects English copy regardless of iPad language", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(
    source,
    /private func localizedText\(english: String, chinese _: String\) -> String \{\s*english\s*\}/,
  );
  assert.doesNotMatch(source, /Locale\.preferredLanguages/);
});

test("UIKit fallback safely derives summary and unit price when optional fields are missing", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(
    source,
    /private func resolvedSummary\([\s\S]*for snapshot: HBExternalDisplaySnapshot[\s\S]*-> HBExternalDisplaySummary/,
  );
  assert.match(source, /if let summary = snapshot\.summary \{[\s\S]*return summary/);
  assert.match(
    source,
    /subtotal:\s*HBExternalDisplayMoney\(\s*cents:\s*snapshot\.total\.cents\s*\+\s*snapshot\.discount\.cents\s*\)/,
  );
  assert.match(
    source,
    /private func unitPriceText\(for item: HBExternalDisplayItem\) -> String/,
  );
  assert.match(source, /if let unitPrice = item\.unitPrice \{[\s\S]*return format\(unitPrice\)/);
  assert.match(source, /private func unitPriceText[\s\S]*return "—"/);
  assert.doesNotMatch(source, /Double\(item\.quantity\)/);
  assert.match(
    source,
    /private func formattedItemCount\([\s\S]*itemQuantity:\s*String,[\s\S]*skuCount:\s*Int[\s\S]*-> String/,
  );
});

test("UIKit fallback renders a non-zero discount as an explicit deduction", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(
    source,
    /discountValueLabel\.text\s*=\s*formatDiscount\(snapshot\.discount\)/,
  );
  assert.match(
    source,
    /private func formatDiscount\([\s\S]*absoluteCents\s*=\s*abs\(money\.cents\)[\s\S]*guard absoluteCents\s*>\s*0 else \{ return "\$0\.00" \}[\s\S]*format:\s*"−\$%d\.%02d"/,
  );
});

test("UIKit fallback fits media without cropping, reuses identical adverts, and clears identity on stop", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(source, /advertImageView\.contentMode\s*=\s*\.scaleAspectFit/);
  assert.match(source, /layer\.videoGravity\s*=\s*\.resizeAspect\b/);
  assert.match(source, /videoLayer\?\.frame\s*=\s*advertContainer\.bounds/);
  assert.match(
    source,
    /struct AdvertIdentity:[\s\S]*let kind: String[\s\S]*let localUri: String/,
  );
  assert.match(
    source,
    /let identity = AdvertIdentity\([\s\S]*kind:\s*advert\.kind\.rawValue,[\s\S]*localUri:\s*advert\.localUri[\s\S]*\)/,
  );
  assert.match(
    source,
    /guard currentAdvertIdentity != identity else \{ return nil \}[\s\S]*stopMedia\(\)/,
  );
  assert.match(source, /currentAdvertIdentity\s*=\s*identity/);
  assert.match(
    source,
    /func stopMedia\(\)[\s\S]*currentAdvertIdentity\s*=\s*nil/,
  );
  assert.match(
    source,
    /guard asset\.isPlayable else \{[\s\S]*clearAdvert\(\)[\s\S]*return "advert-video-unavailable"/,
  );
});

test("video advert becomes reusable only when healthy and tears down runtime failures safely", async () => {
  const source = await read("ios/HBExternalDisplayViewController.swift");

  assert.match(source, /private var videoStatusObservation: NSKeyValueObservation\?/);
  assert.match(source, /private var videoFailureObserver: NSObjectProtocol\?/);
  assert.match(source, /private var videoStalledObserver: NSObjectProtocol\?/);
  assert.match(source, /private var pendingVideoIdentity: AdvertIdentity\?/);
  assert.match(source, /private var videoStartupTimeoutWorkItem: DispatchWorkItem\?/);
  assert.match(source, /private var videoRetryWorkItem: DispatchWorkItem\?/);
  assert.match(
    source,
    /item\.observe\(\s*\\\.status,[\s\S]*options:\s*\[\.initial,\s*\.new\]/,
  );
  assert.match(source, /\.AVPlayerItemFailedToPlayToEndTime/);
  assert.match(source, /\.AVPlayerItemPlaybackStalled/);
  assert.match(source, /queue:\s*\.main/);
  assert.match(source, /DispatchQueue\.main\.async/);
  assert.match(
    source,
    /let templateItem\s*=\s*AVPlayerItem\(asset:\s*asset\)[\s\S]*AVPlayerLooper\(player:\s*player,\s*templateItem:\s*templateItem\)[\s\S]*guard let playbackItem\s*=\s*player\.currentItem[\s\S]*observeVideoPlayback\([\s\S]*item:\s*playbackItem,[\s\S]*advert:\s*advert,[\s\S]*identity:\s*identity/,
  );
  assert.match(
    source,
    /case \.readyToPlay:[\s\S]*cancelVideoStartupTimeout\(\)[\s\S]*pendingVideoIdentity\s*=\s*nil[\s\S]*currentAdvertIdentity\s*=\s*identity/,
  );
  assert.match(
    source,
    /case \.failed:[\s\S]*handleVideoPlaybackFailure\([\s\S]*item:\s*item,[\s\S]*advert:\s*advert,[\s\S]*identity:\s*identity/,
  );
  assert.match(
    source,
    /private func handleVideoPlaybackFailure[\s\S]*guard[\s\S]*!isHandlingVideoFailure[\s\S]*videoPlayerItem === item \|\| videoPlayer\?\.currentItem === item[\s\S]*recordVideoFailure\(for:\s*identity\)[\s\S]*clearAdvert\(\)[\s\S]*HBExternalDisplayCoordinator\.shared\.reportFailure\("advert-video-playback-failed"\)[\s\S]*scheduleVideoRetry\(advert:\s*advert,\s*identity:\s*identity\)/,
  );
  assert.match(
    source,
    /videoFailureCounts\[identity,\s*default:\s*0\]\s*\+=\s*1/,
  );
  assert.match(
    source,
    /videoFailureCounts\[identity,\s*default:\s*0\]\s*<\s*maximumVideoFailureCount/,
  );
  assert.match(
    source,
    /func stopMedia\(\)[\s\S]*cancelVideoStartupTimeout\(\)[\s\S]*cancelVideoRetry\(\)[\s\S]*videoStatusObservation\?\.invalidate\(\)[\s\S]*NotificationCenter\.default\.removeObserver\(videoFailureObserver\)[\s\S]*NotificationCenter\.default\.removeObserver\(videoStalledObserver\)[\s\S]*pendingVideoIdentity\s*=\s*nil[\s\S]*currentAdvertIdentity\s*=\s*nil/,
  );
  assert.match(
    source,
    /deinit \{[\s\S]*videoStartupTimeoutWorkItem\?\.cancel\(\)[\s\S]*videoRetryWorkItem\?\.cancel\(\)[\s\S]*videoStatusObservation\?\.invalidate\(\)[\s\S]*NotificationCenter\.default\.removeObserver\(videoFailureObserver\)[\s\S]*NotificationCenter\.default\.removeObserver\(videoStalledObserver\)/,
  );
  assert.match(
    source,
    /private func scheduleVideoStartupTimeout[\s\S]*handleVideoPlaybackFailure[\s\S]*DispatchQueue\.main\.asyncAfter/,
  );
  assert.match(
    source,
    /private func scheduleVideoRetry[\s\S]*videoFailureCounts\[identity,\s*default:\s*0\]\s*<\s*maximumVideoFailureCount[\s\S]*lastRequestedAdvertIdentity\s*==\s*identity[\s\S]*guard[\s\S]*videoRetryToken\s*==\s*token[\s\S]*lastRequestedAdvertIdentity\s*==\s*identity[\s\S]*render\(advert:\s*advert\)[\s\S]*reportFailure\(failure\)[\s\S]*DispatchQueue\.main\.asyncAfter/,
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
