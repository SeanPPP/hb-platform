import AVFoundation
import UIKit

final class HBExternalDisplayViewController: UIViewController {
  private struct AdvertIdentity: Hashable {
    let kind: String
    let localUri: String
  }

  private let modeLabel = UILabel()
  private let itemStack = UIStackView()
  private let itemCountLabel = UILabel()
  private let gstValueLabel = UILabel()
  private let discountValueLabel = UILabel()
  private let totalValueLabel = UILabel()
  private let changeValueLabel = UILabel()
  private let checkoutPanel = UIStackView()
  private let advertContainer = UIView()
  private let advertImageView = UIImageView()
  private let brandStack = UIStackView()
  private let brandTitleLabel = UILabel()
  private let brandSubtitleLabel = UILabel()

  private var videoPlayer: AVQueuePlayer?
  private var videoLooper: AVPlayerLooper?
  private var videoLayer: AVPlayerLayer?
  private var videoPlayerItem: AVPlayerItem?
  private var videoStatusObservation: NSKeyValueObservation?
  private var videoFailureObserver: NSObjectProtocol?
  private var videoStalledObserver: NSObjectProtocol?
  private var videoStartupTimeoutWorkItem: DispatchWorkItem?
  private var videoStartupTimeoutToken: UUID?
  private var videoRetryWorkItem: DispatchWorkItem?
  private var videoRetryToken: UUID?
  private var reactSurface: UIView?
  private var currentAdvertIdentity: AdvertIdentity?
  private var pendingVideoIdentity: AdvertIdentity?
  private var lastRequestedAdvertIdentity: AdvertIdentity?
  private var videoFailureCounts: [AdvertIdentity: Int] = [:]
  private var isHandlingVideoFailure = false
  private var transactionLayoutConstraints: [NSLayoutConstraint] = []
  private var fullScreenAdvertLayoutConstraints: [NSLayoutConstraint] = []
  private var isShowingFullScreenAdvert = false
  private let maximumVideoFailureCount = 2
  private let videoRetryDelay: TimeInterval = 0.75
  private let videoStartupTimeout: TimeInterval = 5

  var hasReactSurface: Bool {
    reactSurface != nil
  }

  deinit {
    videoStartupTimeoutWorkItem?.cancel()
    videoRetryWorkItem?.cancel()
    videoStatusObservation?.invalidate()
    if let videoFailureObserver {
      NotificationCenter.default.removeObserver(videoFailureObserver)
    }
    if let videoStalledObserver {
      NotificationCenter.default.removeObserver(videoStalledObserver)
    }
  }

  override func loadView() {
    let rootView = UIView()
    rootView.backgroundColor = UIColor(red: 0.035, green: 0.055, blue: 0.09, alpha: 1)
    rootView.isUserInteractionEnabled = false
    view = rootView

    configureLabels()
    configureLayout()
    showWaitingState()
  }

  override func viewDidLayoutSubviews() {
    super.viewDidLayoutSubviews()
    videoLayer?.frame = advertContainer.bounds
  }

  func showWaitingState() {
    resetVideoRetryState()
    updateFallbackLayout(fullScreenAdvert: false)
    modeLabel.text = "Welcome"
    replaceItemRows(with: [])
    itemCountLabel.text = "Ready when you are"
    gstValueLabel.text = "$0.00"
    discountValueLabel.text = "$0.00"
    totalValueLabel.text = "$0.00"
    changeValueLabel.text = "$0.00"
    showBrandPlaceholder()
  }

  @discardableResult
  func render(snapshot: HBExternalDisplaySnapshot) -> String? {
    updateFallbackLayout(
      fullScreenAdvert:
        snapshot.mode == .idle
        && snapshot.items.isEmpty
        && snapshot.advert != nil
    )
    modeLabel.text = title(for: snapshot.mode)
    replaceItemRows(with: Array(snapshot.items.prefix(12)))
    let hiddenCount = max(snapshot.items.count - 12, 0)
    itemCountLabel.text = hiddenCount == 0
      ? "\(snapshot.items.count) item(s)"
      : "\(snapshot.items.count) item(s) · \(hiddenCount) more"
    gstValueLabel.text = format(snapshot.gst)
    discountValueLabel.text = formatDiscount(snapshot.discount)
    totalValueLabel.text = format(snapshot.total)
    changeValueLabel.text = format(snapshot.change)

    return render(advert: snapshot.advert)
  }

  func stopMedia() {
    cancelVideoStartupTimeout()
    cancelVideoRetry()
    videoStatusObservation?.invalidate()
    videoStatusObservation = nil
    if let videoFailureObserver {
      NotificationCenter.default.removeObserver(videoFailureObserver)
    }
    videoFailureObserver = nil
    if let videoStalledObserver {
      NotificationCenter.default.removeObserver(videoStalledObserver)
    }
    videoStalledObserver = nil
    videoPlayerItem = nil
    pendingVideoIdentity = nil
    videoPlayer?.pause()
    videoLayer?.removeFromSuperlayer()
    videoLayer = nil
    videoLooper = nil
    videoPlayer = nil
    currentAdvertIdentity = nil
  }

  func installReactSurface(
    initialProperties: [AnyHashable: Any]
  ) -> String? {
    guard reactSurface == nil else { return nil }

    do {
      let surface = try HBExternalDisplayReactSurfaceFactory.makeSurface(
        initialProperties: initialProperties
      )
      surface.translatesAutoresizingMaskIntoConstraints = false
      surface.isUserInteractionEnabled = false
      surface.isOpaque = false
      surface.backgroundColor = .clear
      view.addSubview(surface)
      NSLayoutConstraint.activate([
        surface.leadingAnchor.constraint(equalTo: view.leadingAnchor),
        surface.trailingAnchor.constraint(equalTo: view.trailingAnchor),
        surface.topAnchor.constraint(equalTo: view.topAnchor),
        surface.bottomAnchor.constraint(equalTo: view.bottomAnchor),
      ])
      reactSurface = surface
      return nil
    } catch {
      return error.localizedDescription
    }
  }

  func removeReactSurface() {
    reactSurface?.removeFromSuperview()
    reactSurface = nil
  }

  private func configureLabels() {
    modeLabel.font = .systemFont(ofSize: 34, weight: .semibold)
    modeLabel.textColor = UIColor(red: 0.41, green: 0.89, blue: 0.76, alpha: 1)
    modeLabel.numberOfLines = 1

    itemCountLabel.font = .systemFont(ofSize: 17, weight: .medium)
    itemCountLabel.textColor = UIColor.white.withAlphaComponent(0.58)

    brandTitleLabel.text = "HOT BARGAIN"
    brandTitleLabel.font = .systemFont(ofSize: 42, weight: .black)
    brandTitleLabel.textColor = .white
    brandTitleLabel.textAlignment = .center

    brandSubtitleLabel.text = "Thank you for shopping with us"
    brandSubtitleLabel.font = .systemFont(ofSize: 19, weight: .medium)
    brandSubtitleLabel.textColor = UIColor.white.withAlphaComponent(0.64)
    brandSubtitleLabel.textAlignment = .center
  }

  private func configureLayout() {
    checkoutPanel.axis = .vertical
    checkoutPanel.alignment = .fill
    checkoutPanel.spacing = 18
    checkoutPanel.translatesAutoresizingMaskIntoConstraints = false

    itemStack.axis = .vertical
    itemStack.alignment = .fill
    itemStack.distribution = .fillEqually
    itemStack.spacing = 2

    let divider = UIView()
    divider.backgroundColor = UIColor.white.withAlphaComponent(0.12)
    divider.translatesAutoresizingMaskIntoConstraints = false
    divider.heightAnchor.constraint(equalToConstant: 1).isActive = true

    checkoutPanel.addArrangedSubview(modeLabel)
    checkoutPanel.addArrangedSubview(itemStack)
    checkoutPanel.addArrangedSubview(itemCountLabel)
    checkoutPanel.addArrangedSubview(divider)
    checkoutPanel.addArrangedSubview(makeTotalsPanel())

    advertContainer.backgroundColor = UIColor.white.withAlphaComponent(0.055)
    advertContainer.layer.cornerRadius = 24
    advertContainer.layer.masksToBounds = true
    advertContainer.translatesAutoresizingMaskIntoConstraints = false

    advertImageView.contentMode = .scaleAspectFit
    advertImageView.translatesAutoresizingMaskIntoConstraints = false
    advertContainer.addSubview(advertImageView)

    brandStack.axis = .vertical
    brandStack.alignment = .fill
    brandStack.spacing = 14
    brandStack.translatesAutoresizingMaskIntoConstraints = false
    brandStack.addArrangedSubview(brandTitleLabel)
    brandStack.addArrangedSubview(brandSubtitleLabel)
    advertContainer.addSubview(brandStack)

    view.addSubview(checkoutPanel)
    view.addSubview(advertContainer)

    // 扣除 32pt 栏间距后保持交易与广告区域精确为 60/40。
    transactionLayoutConstraints = [
      checkoutPanel.leadingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.leadingAnchor,
        constant: 34
      ),
      checkoutPanel.topAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.topAnchor,
        constant: 28
      ),
      checkoutPanel.bottomAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.bottomAnchor,
        constant: -28
      ),
      advertContainer.leadingAnchor.constraint(
        equalTo: checkoutPanel.trailingAnchor,
        constant: 32
      ),
      advertContainer.trailingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.trailingAnchor,
        constant: -34
      ),
      advertContainer.topAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.topAnchor,
        constant: 28
      ),
      advertContainer.bottomAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.bottomAnchor,
        constant: -28
      ),
      checkoutPanel.widthAnchor.constraint(
        equalTo: advertContainer.widthAnchor,
        multiplier: 1.5
      ),
    ]
    fullScreenAdvertLayoutConstraints = [
      advertContainer.leadingAnchor.constraint(equalTo: view.leadingAnchor),
      advertContainer.trailingAnchor.constraint(equalTo: view.trailingAnchor),
      advertContainer.topAnchor.constraint(equalTo: view.topAnchor),
      advertContainer.bottomAnchor.constraint(equalTo: view.bottomAnchor),
    ]

    NSLayoutConstraint.activate(transactionLayoutConstraints)
    NSLayoutConstraint.activate([
      advertImageView.leadingAnchor.constraint(equalTo: advertContainer.leadingAnchor),
      advertImageView.trailingAnchor.constraint(equalTo: advertContainer.trailingAnchor),
      advertImageView.topAnchor.constraint(equalTo: advertContainer.topAnchor),
      advertImageView.bottomAnchor.constraint(equalTo: advertContainer.bottomAnchor),
      brandStack.leadingAnchor.constraint(equalTo: advertContainer.leadingAnchor, constant: 24),
      brandStack.trailingAnchor.constraint(equalTo: advertContainer.trailingAnchor, constant: -24),
      brandStack.centerYAnchor.constraint(equalTo: advertContainer.centerYAnchor),
    ])
  }

  private func updateFallbackLayout(fullScreenAdvert: Bool) {
    guard fullScreenAdvert != isShowingFullScreenAdvert else { return }
    isShowingFullScreenAdvert = fullScreenAdvert

    UIView.performWithoutAnimation {
      NSLayoutConstraint.deactivate(
        fullScreenAdvert
          ? transactionLayoutConstraints
          : fullScreenAdvertLayoutConstraints
      )
      checkoutPanel.isHidden = fullScreenAdvert
      advertContainer.backgroundColor = fullScreenAdvert
        ? .clear
        : UIColor.white.withAlphaComponent(0.055)
      advertContainer.layer.cornerRadius = fullScreenAdvert ? 0 : 24
      NSLayoutConstraint.activate(
        fullScreenAdvert
          ? fullScreenAdvertLayoutConstraints
          : transactionLayoutConstraints
      )
      view.layoutIfNeeded()
    }
  }

  private func makeTotalsPanel() -> UIView {
    let stack = UIStackView()
    stack.axis = .vertical
    stack.spacing = 8

    stack.addArrangedSubview(makeTotalRow(title: "GST", valueLabel: gstValueLabel))
    stack.addArrangedSubview(makeTotalRow(title: "Discount", valueLabel: discountValueLabel))
    stack.addArrangedSubview(
      makeTotalRow(title: "TOTAL", valueLabel: totalValueLabel, emphasized: true)
    )
    stack.addArrangedSubview(
      makeTotalRow(title: "CHANGE", valueLabel: changeValueLabel, emphasized: true)
    )
    return stack
  }

  private func makeTotalRow(
    title: String,
    valueLabel: UILabel,
    emphasized: Bool = false
  ) -> UIView {
    let titleLabel = UILabel()
    titleLabel.text = title
    titleLabel.font = .systemFont(
      ofSize: emphasized ? 25 : 19,
      weight: emphasized ? .bold : .medium
    )
    titleLabel.textColor = emphasized ? .white : UIColor.white.withAlphaComponent(0.66)

    valueLabel.font = .monospacedDigitSystemFont(
      ofSize: emphasized ? 38 : 22,
      weight: emphasized ? .bold : .medium
    )
    valueLabel.textColor = emphasized
      ? UIColor(red: 1, green: 0.78, blue: 0.24, alpha: 1)
      : .white
    valueLabel.textAlignment = .right

    let row = UIStackView(arrangedSubviews: [titleLabel, valueLabel])
    row.axis = .horizontal
    row.alignment = .lastBaseline
    row.distribution = .fill
    return row
  }

  private func replaceItemRows(with items: [HBExternalDisplayItem]) {
    itemStack.arrangedSubviews.forEach { row in
      itemStack.removeArrangedSubview(row)
      row.removeFromSuperview()
    }

    if items.isEmpty {
      let emptyLabel = UILabel()
      emptyLabel.text = "Your basket is empty"
      emptyLabel.font = .systemFont(ofSize: 23, weight: .medium)
      emptyLabel.textColor = UIColor.white.withAlphaComponent(0.5)
      itemStack.addArrangedSubview(emptyLabel)
      return
    }

    for item in items {
      let nameLabel = UILabel()
      nameLabel.text = item.name
      nameLabel.font = .systemFont(ofSize: 21, weight: .medium)
      nameLabel.textColor = .white
      nameLabel.lineBreakMode = .byTruncatingTail

      let quantityLabel = UILabel()
      quantityLabel.text = "× \(item.quantity)"
      quantityLabel.font = .monospacedDigitSystemFont(ofSize: 19, weight: .medium)
      quantityLabel.textColor = UIColor.white.withAlphaComponent(0.64)
      quantityLabel.textAlignment = .right
      quantityLabel.widthAnchor.constraint(greaterThanOrEqualToConstant: 76).isActive = true

      let amountLabel = UILabel()
      amountLabel.text = format(item.amount)
      amountLabel.font = .monospacedDigitSystemFont(ofSize: 22, weight: .semibold)
      amountLabel.textColor = .white
      amountLabel.textAlignment = .right
      amountLabel.widthAnchor.constraint(greaterThanOrEqualToConstant: 116).isActive = true

      let row = UIStackView(arrangedSubviews: [nameLabel, quantityLabel, amountLabel])
      row.axis = .horizontal
      row.alignment = .center
      row.spacing = 14
      itemStack.addArrangedSubview(row)
    }
  }

  private func render(advert: HBExternalDisplayAdvert?) -> String? {
    guard let advert else {
      resetVideoRetryState()
      showBrandPlaceholder()
      return nil
    }

    let identity = AdvertIdentity(
      kind: advert.kind.rawValue,
      localUri: advert.localUri
    )
    prepareVideoRetryState(for: identity)
    guard currentAdvertIdentity != identity else { return nil }
    guard pendingVideoIdentity != identity else { return nil }

    stopMedia()
    advertImageView.image = nil

    let url = advert.url
    guard url.isFileURL, FileManager.default.fileExists(atPath: url.path) else {
      showBrandPlaceholder()
      return "advert-file-unavailable"
    }

    switch advert.kind {
    case .image:
      guard let image = UIImage(contentsOfFile: url.path) else {
        showBrandPlaceholder()
        return "advert-image-unavailable"
      }
      advertImageView.image = image
      advertImageView.isHidden = false
      brandStack.isHidden = true
      currentAdvertIdentity = identity

    case .video:
      guard
        videoFailureCounts[identity, default: 0]
          < maximumVideoFailureCount
      else {
        showBrandPlaceholder()
        return "advert-video-retry-exhausted"
      }
      let asset = AVURLAsset(url: url)
      guard asset.isPlayable else {
        recordVideoFailure(for: identity)
        showBrandPlaceholder()
        return "advert-video-unavailable"
      }
      advertImageView.isHidden = true
      brandStack.isHidden = true
      let player = AVQueuePlayer()
      let templateItem = AVPlayerItem(asset: asset)
      videoLooper = AVPlayerLooper(player: player, templateItem: templateItem)
      guard let playbackItem = player.currentItem else {
        recordVideoFailure(for: identity)
        showBrandPlaceholder()
        return "advert-video-unavailable"
      }
      let layer = AVPlayerLayer(player: player)
      layer.videoGravity = .resizeAspect
      advertContainer.layer.insertSublayer(layer, at: 0)
      videoPlayer = player
      videoLayer = layer
      observeVideoPlayback(
        item: playbackItem,
        advert: advert,
        identity: identity
      )
      player.play()
      view.setNeedsLayout()
    }

    return nil
  }

  private func prepareVideoRetryState(for identity: AdvertIdentity) {
    guard lastRequestedAdvertIdentity != identity else { return }
    lastRequestedAdvertIdentity = identity
    videoFailureCounts.removeAll()
  }

  private func resetVideoRetryState() {
    lastRequestedAdvertIdentity = nil
    videoFailureCounts.removeAll()
  }

  private func recordVideoFailure(for identity: AdvertIdentity) {
    videoFailureCounts[identity, default: 0] += 1
  }

  private func observeVideoPlayback(
    item: AVPlayerItem,
    advert: HBExternalDisplayAdvert,
    identity: AdvertIdentity
  ) {
    videoPlayerItem = item
    pendingVideoIdentity = identity
    videoStatusObservation = item.observe(
      \.status,
      options: [.initial, .new]
    ) { [weak self, weak item] _, _ in
      DispatchQueue.main.async {
        guard let self, let item else { return }
        self.handleVideoStatusChange(
          item: item,
          advert: advert,
          identity: identity
        )
      }
    }
    videoFailureObserver = NotificationCenter.default.addObserver(
      forName: .AVPlayerItemFailedToPlayToEndTime,
      object: nil,
      queue: .main
    ) { [weak self] notification in
      guard let item = notification.object as? AVPlayerItem else { return }
      self?.handleVideoPlaybackFailure(
        item: item,
        advert: advert,
        identity: identity
      )
    }
    videoStalledObserver = NotificationCenter.default.addObserver(
      forName: .AVPlayerItemPlaybackStalled,
      object: nil,
      queue: .main
    ) { [weak self] notification in
      guard let item = notification.object as? AVPlayerItem else { return }
      self?.handleVideoPlaybackFailure(
        item: item,
        advert: advert,
        identity: identity
      )
    }
    scheduleVideoStartupTimeout(
      item: item,
      advert: advert,
      identity: identity
    )
  }

  private func handleVideoStatusChange(
    item: AVPlayerItem,
    advert: HBExternalDisplayAdvert,
    identity: AdvertIdentity
  ) {
    precondition(Thread.isMainThread)
    guard videoPlayerItem === item else { return }

    switch item.status {
    case .readyToPlay:
      cancelVideoStartupTimeout()
      pendingVideoIdentity = nil
      currentAdvertIdentity = identity
    case .failed:
      handleVideoPlaybackFailure(
        item: item,
        advert: advert,
        identity: identity
      )
    case .unknown:
      break
    @unknown default:
      break
    }
  }

  private func handleVideoPlaybackFailure(
    item: AVPlayerItem,
    advert: HBExternalDisplayAdvert,
    identity: AdvertIdentity
  ) {
    precondition(Thread.isMainThread)
    guard
      !isHandlingVideoFailure,
      pendingVideoIdentity == identity || currentAdvertIdentity == identity,
      videoPlayerItem === item || videoPlayer?.currentItem === item
    else {
      return
    }

    isHandlingVideoFailure = true
    defer { isHandlingVideoFailure = false }
    recordVideoFailure(for: identity)
    // 先撤销观察再上报，避免播放器 teardown 再次触发失败通知。
    showBrandPlaceholder()
    HBExternalDisplayCoordinator.shared.reportFailure("advert-video-playback-failed")
    scheduleVideoRetry(advert: advert, identity: identity)
  }

  private func scheduleVideoStartupTimeout(
    item: AVPlayerItem,
    advert: HBExternalDisplayAdvert,
    identity: AdvertIdentity
  ) {
    cancelVideoStartupTimeout()
    let token = UUID()
    videoStartupTimeoutToken = token
    let workItem = DispatchWorkItem { [weak self, weak item] in
      guard
        let self,
        let item,
        self.videoStartupTimeoutToken == token,
        self.pendingVideoIdentity == identity,
        self.lastRequestedAdvertIdentity == identity
      else {
        return
      }

      self.videoStartupTimeoutWorkItem = nil
      self.videoStartupTimeoutToken = nil
      self.handleVideoPlaybackFailure(
        item: item,
        advert: advert,
        identity: identity
      )
    }
    videoStartupTimeoutWorkItem = workItem
    DispatchQueue.main.asyncAfter(
      deadline: .now() + videoStartupTimeout,
      execute: workItem
    )
  }

  private func cancelVideoStartupTimeout() {
    videoStartupTimeoutWorkItem?.cancel()
    videoStartupTimeoutWorkItem = nil
    videoStartupTimeoutToken = nil
  }

  private func scheduleVideoRetry(
    advert: HBExternalDisplayAdvert,
    identity: AdvertIdentity
  ) {
    guard
      videoFailureCounts[identity, default: 0] < maximumVideoFailureCount,
      lastRequestedAdvertIdentity == identity
    else {
      return
    }

    cancelVideoRetry()
    let token = UUID()
    videoRetryToken = token
    let workItem = DispatchWorkItem { [weak self] in
      guard
        let self,
        self.videoRetryToken == token,
        self.lastRequestedAdvertIdentity == identity
      else {
        return
      }

      self.videoRetryWorkItem = nil
      self.videoRetryToken = nil
      if let failure = self.render(advert: advert) {
        HBExternalDisplayCoordinator.shared.reportFailure(failure)
      }
    }
    videoRetryWorkItem = workItem
    DispatchQueue.main.asyncAfter(
      deadline: .now() + videoRetryDelay,
      execute: workItem
    )
  }

  private func cancelVideoRetry() {
    videoRetryWorkItem?.cancel()
    videoRetryWorkItem = nil
    videoRetryToken = nil
  }

  private func showBrandPlaceholder() {
    stopMedia()
    advertImageView.image = nil
    advertImageView.isHidden = true
    brandStack.isHidden = false
  }

  private func title(for mode: HBExternalDisplayMode) -> String {
    switch mode {
    case .idle:
      return "Welcome"
    case .cart:
      return "Your order"
    case .payment:
      return "Payment"
    case .change:
      return "Your change"
    case .success:
      return "Thank you"
    }
  }

  private func format(_ money: HBExternalDisplayMoney) -> String {
    let absoluteCents = abs(money.cents)
    let sign = money.cents < 0 ? "-" : ""
    return String(
      format: "%@$%d.%02d",
      sign,
      absoluteCents / 100,
      absoluteCents % 100
    )
  }

  private func formatDiscount(_ money: HBExternalDisplayMoney) -> String {
    let absoluteCents = abs(money.cents)
    guard absoluteCents > 0 else { return "$0.00" }

    // snapshot 保存的是折扣绝对金额；UIKit fallback 同样明确呈现为减项。
    return String(
      format: "−$%d.%02d",
      absoluteCents / 100,
      absoluteCents % 100
    )
  }
}
