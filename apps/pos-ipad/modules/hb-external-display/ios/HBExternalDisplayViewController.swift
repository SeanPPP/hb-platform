import AVFoundation
import UIKit

final class HBExternalDisplayViewController: UIViewController {
  private struct AdvertIdentity: Hashable {
    let kind: String
    let localUri: String
  }

  private struct HBExternalDisplayItemWindow {
    let items: [HBExternalDisplayItem]
    let hiddenAbove: Int
    let hiddenBelow: Int
  }

  // 48pt 窗口标题栏。
  private let titleBar = UIView()
  private let titleDivider = UIView()
  private let windowTitleLabel = UILabel()

  // 左侧订单表。
  private let orderPanel = UIStackView()
  private let orderTitleLabel = UILabel()
  private let tableHeaderRow = UIStackView()
  private let itemStack = UIStackView()
  private let moreItemsLabel = UILabel()

  // 右侧广告媒体区。
  private let advertContainer = UIView()
  private let advertImageView = UIImageView()

  // 底部全宽汇总区，按 47% / 26% / 27% 分栏。
  private let summaryPanel = UIView()
  private let summarySections = UIStackView()
  private let summaryMetricsContainer = UIView()
  private let summaryMetricsStack = UIStackView()
  private let metricsRow = UIStackView()
  private let itemCountLabel = UILabel()
  private let subtotalValueLabel = UILabel()
  private let gstValueLabel = UILabel()
  private let discountValueLabel = UILabel()
  private let amountDueContainer = UIView()
  private let amountDueStack = UIStackView()
  private let amountDueLabel = UILabel()
  private let totalValueLabel = UILabel()
  private let statusRegion = UIView()
  private let statusCard = UIView()
  private let statusStack = UIStackView()
  private let statusTitleLabel = UILabel()
  private let statusSubtitleLabel = UILabel()

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
  private let mintColor = UIColor(red: 0.447, green: 0.902, blue: 0.765, alpha: 1)
  private let amberColor = UIColor(red: 1, green: 0.761, blue: 0.11, alpha: 1)

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
    rootView.backgroundColor = UIColor(red: 0.035, green: 0.067, blue: 0.122, alpha: 1)
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
    renderStatusCard(mode: .idle, change: HBExternalDisplayMoney(cents: 0))
    replaceItemRows(with: [])
    moreItemsLabel.text = nil
    moreItemsLabel.isHidden = true
    itemCountLabel.text = formattedItemCount(itemQuantity: "0", skuCount: 0)
    subtotalValueLabel.text = "$0.00"
    gstValueLabel.text = "$0.00"
    discountValueLabel.text = "$0.00"
    totalValueLabel.text = "$0.00"
    clearAdvert()
  }

  @discardableResult
  func render(snapshot: HBExternalDisplaySnapshot) -> String? {
    updateFallbackLayout(
      fullScreenAdvert:
        snapshot.mode == .idle
        && snapshot.items.isEmpty
        && snapshot.advert != nil
    )
    renderStatusCard(mode: snapshot.mode, change: snapshot.change)
    let window = visibleItemWindow(for: snapshot)
    replaceItemRows(with: window.items)
    moreItemsLabel.text = moreItemsText(
      hiddenAbove: window.hiddenAbove,
      hiddenBelow: window.hiddenBelow
    )
    moreItemsLabel.isHidden =
      window.hiddenAbove == 0 && window.hiddenBelow == 0
    let summary = resolvedSummary(for: snapshot)
    itemCountLabel.text = formattedItemCount(
      itemQuantity: summary.itemQuantity,
      skuCount: summary.skuCount
    )
    subtotalValueLabel.text = format(summary.subtotal)
    gstValueLabel.text = format(snapshot.gst)
    discountValueLabel.text = formatDiscount(snapshot.discount)
    totalValueLabel.text = format(snapshot.total)

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
    windowTitleLabel.text = localizedText(english: "Customer Display", chinese: "客显")
    windowTitleLabel.font = .systemFont(ofSize: 21, weight: .bold)
    windowTitleLabel.textColor = .white
    windowTitleLabel.numberOfLines = 1

    orderTitleLabel.text = localizedText(english: "Your order", chinese: "您的订单")
    orderTitleLabel.font = .systemFont(ofSize: 30, weight: .bold)
    orderTitleLabel.textColor = mintColor
    orderTitleLabel.numberOfLines = 1
    orderTitleLabel.setContentHuggingPriority(.required, for: .vertical)
    orderTitleLabel.setContentCompressionResistancePriority(.required, for: .vertical)

    moreItemsLabel.font = .systemFont(ofSize: 14, weight: .medium)
    moreItemsLabel.textColor = UIColor.white.withAlphaComponent(0.58)
    moreItemsLabel.numberOfLines = 1

    itemCountLabel.font = .systemFont(ofSize: 17, weight: .medium)
    itemCountLabel.textColor = UIColor.white.withAlphaComponent(0.72)

    amountDueLabel.text = localizedText(english: "Amount due", chinese: "应付总额")
    amountDueLabel.font = .systemFont(ofSize: 17, weight: .semibold)
    amountDueLabel.textColor = UIColor.white.withAlphaComponent(0.82)

    totalValueLabel.font = .monospacedDigitSystemFont(ofSize: 42, weight: .heavy)
    totalValueLabel.textColor = amberColor
    totalValueLabel.adjustsFontSizeToFitWidth = true
    totalValueLabel.minimumScaleFactor = 0.72

    statusTitleLabel.font = .systemFont(ofSize: 27, weight: .bold)
    statusTitleLabel.textColor = mintColor
    statusTitleLabel.numberOfLines = 2
    statusTitleLabel.textAlignment = .center
    statusTitleLabel.adjustsFontSizeToFitWidth = true
    statusTitleLabel.minimumScaleFactor = 0.75

    statusSubtitleLabel.font = .systemFont(ofSize: 15, weight: .medium)
    statusSubtitleLabel.textColor = UIColor.white.withAlphaComponent(0.88)
    statusSubtitleLabel.numberOfLines = 2
    statusSubtitleLabel.textAlignment = .center
  }

  private func configureLayout() {
    titleBar.backgroundColor = UIColor(red: 0.027, green: 0.078, blue: 0.149, alpha: 1)
    titleBar.translatesAutoresizingMaskIntoConstraints = false
    windowTitleLabel.translatesAutoresizingMaskIntoConstraints = false
    titleDivider.backgroundColor = UIColor.white.withAlphaComponent(0.22)
    titleDivider.translatesAutoresizingMaskIntoConstraints = false
    titleBar.addSubview(windowTitleLabel)
    titleBar.addSubview(titleDivider)
    view.addSubview(titleBar)

    orderPanel.axis = .vertical
    orderPanel.alignment = .fill
    orderPanel.spacing = 0
    orderPanel.backgroundColor = UIColor(red: 0.027, green: 0.078, blue: 0.149, alpha: 1)
    orderPanel.layer.cornerRadius = 12
    orderPanel.layer.borderWidth = 1
    orderPanel.layer.borderColor = UIColor.white.withAlphaComponent(0.24).cgColor
    orderPanel.layer.masksToBounds = true
    orderPanel.isLayoutMarginsRelativeArrangement = true
    orderPanel.directionalLayoutMargins = NSDirectionalEdgeInsets(
      top: 16,
      leading: 16,
      bottom: 16,
      trailing: 16
    )
    orderPanel.translatesAutoresizingMaskIntoConstraints = false
    let orderTitleRow = UIStackView()
    orderTitleRow.axis = .horizontal
    orderTitleRow.alignment = .center
    orderTitleRow.spacing = 12
    orderTitleLabel.setContentHuggingPriority(.required, for: .horizontal)
    orderTitleLabel.setContentCompressionResistancePriority(.required, for: .horizontal)
    moreItemsLabel.setContentHuggingPriority(.required, for: .horizontal)
    moreItemsLabel.setContentCompressionResistancePriority(.required, for: .horizontal)
    let orderTitleSpacer = UIView()
    orderTitleSpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
    orderTitleSpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    orderTitleRow.addArrangedSubview(orderTitleLabel)
    orderTitleRow.addArrangedSubview(orderTitleSpacer)
    orderTitleRow.addArrangedSubview(moreItemsLabel)
    orderPanel.addArrangedSubview(orderTitleRow)
    orderPanel.setCustomSpacing(12, after: orderTitleRow)

    tableHeaderRow.axis = .horizontal
    tableHeaderRow.alignment = .center
    tableHeaderRow.spacing = 0
    tableHeaderRow.addArrangedSubview(
      makeColumnHeader(localizedText(english: "Product", chinese: "商品"), alignment: .left)
    )
    tableHeaderRow.addArrangedSubview(
      makeColumnHeader(localizedText(english: "Qty", chinese: "数量"), alignment: .right, width: 72)
    )
    tableHeaderRow.addArrangedSubview(
      makeColumnHeader(localizedText(english: "Unit price", chinese: "单价"), alignment: .right, width: 104)
    )
    tableHeaderRow.addArrangedSubview(
      makeColumnHeader(localizedText(english: "Amount", chinese: "金额"), alignment: .right, width: 104)
    )
    tableHeaderRow.heightAnchor.constraint(equalToConstant: 38).isActive = true
    orderPanel.addArrangedSubview(tableHeaderRow)

    let tableDivider = UIView()
    tableDivider.backgroundColor = UIColor.white.withAlphaComponent(0.28)
    tableDivider.heightAnchor.constraint(equalToConstant: 1).isActive = true
    orderPanel.addArrangedSubview(tableDivider)

    itemStack.axis = .vertical
    itemStack.alignment = .fill
    itemStack.distribution = .fill
    itemStack.spacing = 0
    itemStack.setContentHuggingPriority(.defaultLow, for: .vertical)
    itemStack.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
    orderPanel.addArrangedSubview(itemStack)
    view.addSubview(orderPanel)

    advertContainer.backgroundColor = UIColor.white.withAlphaComponent(0.035)
    advertContainer.layer.cornerRadius = 12
    advertContainer.layer.borderWidth = 1
    advertContainer.layer.borderColor = UIColor.white.withAlphaComponent(0.24).cgColor
    advertContainer.layer.masksToBounds = true
    advertContainer.translatesAutoresizingMaskIntoConstraints = false
    advertImageView.contentMode = .scaleAspectFit
    advertImageView.translatesAutoresizingMaskIntoConstraints = false
    advertContainer.addSubview(advertImageView)
    view.addSubview(advertContainer)

    summaryPanel.backgroundColor = UIColor(red: 0.027, green: 0.078, blue: 0.149, alpha: 1)
    summaryPanel.layer.cornerRadius = 12
    summaryPanel.layer.borderWidth = 1
    summaryPanel.layer.borderColor = UIColor.white.withAlphaComponent(0.24).cgColor
    summaryPanel.layer.masksToBounds = true
    summaryPanel.translatesAutoresizingMaskIntoConstraints = false
    summarySections.axis = .horizontal
    summarySections.alignment = .fill
    summarySections.distribution = .fill
    summarySections.spacing = 0
    summarySections.translatesAutoresizingMaskIntoConstraints = false
    summarySections.addArrangedSubview(summaryMetricsContainer)
    summarySections.addArrangedSubview(amountDueContainer)
    summarySections.addArrangedSubview(statusRegion)
    summaryPanel.addSubview(summarySections)
    view.addSubview(summaryPanel)

    summaryMetricsStack.axis = .vertical
    summaryMetricsStack.alignment = .fill
    summaryMetricsStack.distribution = .fill
    summaryMetricsStack.spacing = 10
    summaryMetricsStack.translatesAutoresizingMaskIntoConstraints = false
    summaryMetricsStack.addArrangedSubview(itemCountLabel)
    summaryMetricsStack.addArrangedSubview(metricsRow)
    summaryMetricsContainer.addSubview(summaryMetricsStack)

    metricsRow.axis = .horizontal
    metricsRow.alignment = .fill
    metricsRow.distribution = .fill
    metricsRow.spacing = 14
    let subtotalMetric = makeMetric(
      title: localizedText(english: "Subtotal", chinese: "小计"),
      valueLabel: subtotalValueLabel
    )
    let gstMetric = makeMetric(title: "GST", valueLabel: gstValueLabel)
    let discountMetric = makeMetric(
      title: localizedText(english: "Discount", chinese: "优惠"),
      valueLabel: discountValueLabel
    )
    let firstMetricDivider = makeVerticalDivider()
    let secondMetricDivider = makeVerticalDivider()
    metricsRow.addArrangedSubview(subtotalMetric)
    metricsRow.addArrangedSubview(firstMetricDivider)
    metricsRow.addArrangedSubview(gstMetric)
    metricsRow.addArrangedSubview(secondMetricDivider)
    metricsRow.addArrangedSubview(discountMetric)
    subtotalMetric.widthAnchor.constraint(equalTo: gstMetric.widthAnchor).isActive = true
    gstMetric.widthAnchor.constraint(equalTo: discountMetric.widthAnchor).isActive = true

    amountDueStack.axis = .vertical
    amountDueStack.alignment = .fill
    amountDueStack.distribution = .fill
    amountDueStack.spacing = 3
    amountDueStack.translatesAutoresizingMaskIntoConstraints = false
    amountDueStack.addArrangedSubview(amountDueLabel)
    amountDueStack.addArrangedSubview(totalValueLabel)
    amountDueContainer.addSubview(amountDueStack)
    let amountLeadingDivider = makeVerticalDivider()
    let amountTrailingDivider = makeVerticalDivider()
    amountDueContainer.addSubview(amountLeadingDivider)
    amountDueContainer.addSubview(amountTrailingDivider)

    statusRegion.addSubview(statusCard)
    statusCard.layer.cornerRadius = 10
    statusCard.layer.borderWidth = 1
    statusCard.layer.borderColor = mintColor.cgColor
    statusCard.layer.masksToBounds = true
    statusCard.translatesAutoresizingMaskIntoConstraints = false
    statusStack.axis = .vertical
    statusStack.alignment = .fill
    statusStack.spacing = 7
    statusStack.translatesAutoresizingMaskIntoConstraints = false
    statusStack.addArrangedSubview(statusTitleLabel)
    statusStack.addArrangedSubview(statusSubtitleLabel)
    statusCard.addSubview(statusStack)

    NSLayoutConstraint.activate([
      titleBar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
      titleBar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
      titleBar.topAnchor.constraint(equalTo: view.topAnchor),
      titleBar.heightAnchor.constraint(equalToConstant: 48),
      windowTitleLabel.leadingAnchor.constraint(equalTo: titleBar.leadingAnchor, constant: 24),
      windowTitleLabel.centerYAnchor.constraint(equalTo: titleBar.centerYAnchor),
      titleDivider.leadingAnchor.constraint(equalTo: titleBar.leadingAnchor),
      titleDivider.trailingAnchor.constraint(equalTo: titleBar.trailingAnchor),
      titleDivider.bottomAnchor.constraint(equalTo: titleBar.bottomAnchor),
      titleDivider.heightAnchor.constraint(equalToConstant: 1),

      summarySections.leadingAnchor.constraint(equalTo: summaryPanel.leadingAnchor, constant: 16),
      summarySections.trailingAnchor.constraint(equalTo: summaryPanel.trailingAnchor, constant: -16),
      summarySections.topAnchor.constraint(equalTo: summaryPanel.topAnchor, constant: 16),
      summarySections.bottomAnchor.constraint(equalTo: summaryPanel.bottomAnchor, constant: -16),
      summaryMetricsContainer.widthAnchor.constraint(
        equalTo: summarySections.widthAnchor,
        multiplier: 0.47
      ),
      amountDueContainer.widthAnchor.constraint(
        equalTo: summarySections.widthAnchor,
        multiplier: 0.26
      ),
      summaryMetricsStack.leadingAnchor.constraint(equalTo: summaryMetricsContainer.leadingAnchor),
      summaryMetricsStack.trailingAnchor.constraint(equalTo: summaryMetricsContainer.trailingAnchor, constant: -18),
      summaryMetricsStack.topAnchor.constraint(equalTo: summaryMetricsContainer.topAnchor),
      summaryMetricsStack.bottomAnchor.constraint(equalTo: summaryMetricsContainer.bottomAnchor),
      amountDueStack.leadingAnchor.constraint(equalTo: amountDueContainer.leadingAnchor, constant: 18),
      amountDueStack.trailingAnchor.constraint(equalTo: amountDueContainer.trailingAnchor, constant: -18),
      amountDueStack.centerYAnchor.constraint(equalTo: amountDueContainer.centerYAnchor),
      amountLeadingDivider.leadingAnchor.constraint(equalTo: amountDueContainer.leadingAnchor),
      amountLeadingDivider.topAnchor.constraint(equalTo: amountDueContainer.topAnchor),
      amountLeadingDivider.bottomAnchor.constraint(equalTo: amountDueContainer.bottomAnchor),
      amountTrailingDivider.trailingAnchor.constraint(equalTo: amountDueContainer.trailingAnchor),
      amountTrailingDivider.topAnchor.constraint(equalTo: amountDueContainer.topAnchor),
      amountTrailingDivider.bottomAnchor.constraint(equalTo: amountDueContainer.bottomAnchor),
      statusCard.leadingAnchor.constraint(equalTo: statusRegion.leadingAnchor, constant: 16),
      statusCard.trailingAnchor.constraint(equalTo: statusRegion.trailingAnchor),
      statusCard.topAnchor.constraint(equalTo: statusRegion.topAnchor),
      statusCard.bottomAnchor.constraint(equalTo: statusRegion.bottomAnchor),
      statusStack.leadingAnchor.constraint(equalTo: statusCard.leadingAnchor, constant: 14),
      statusStack.trailingAnchor.constraint(equalTo: statusCard.trailingAnchor, constant: -14),
      statusStack.centerYAnchor.constraint(equalTo: statusCard.centerYAnchor),

      advertImageView.leadingAnchor.constraint(equalTo: advertContainer.leadingAnchor),
      advertImageView.trailingAnchor.constraint(equalTo: advertContainer.trailingAnchor),
      advertImageView.topAnchor.constraint(equalTo: advertContainer.topAnchor),
      advertImageView.bottomAnchor.constraint(equalTo: advertContainer.bottomAnchor),
    ])

    transactionLayoutConstraints = [
      orderPanel.leadingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.leadingAnchor,
        constant: 24
      ),
      orderPanel.topAnchor.constraint(equalTo: titleBar.bottomAnchor, constant: 24),
      orderPanel.bottomAnchor.constraint(equalTo: summaryPanel.topAnchor, constant: -18),
      advertContainer.leadingAnchor.constraint(
        equalTo: orderPanel.trailingAnchor,
        constant: 18
      ),
      advertContainer.trailingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.trailingAnchor,
        constant: -24
      ),
      advertContainer.topAnchor.constraint(equalTo: orderPanel.topAnchor),
      advertContainer.bottomAnchor.constraint(equalTo: orderPanel.bottomAnchor),
      advertContainer.widthAnchor.constraint(equalTo: orderPanel.widthAnchor),
      summaryPanel.leadingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.leadingAnchor,
        constant: 24
      ),
      summaryPanel.trailingAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.trailingAnchor,
        constant: -24
      ),
      summaryPanel.bottomAnchor.constraint(
        equalTo: view.safeAreaLayoutGuide.bottomAnchor,
        constant: -24
      ),
      summaryPanel.heightAnchor.constraint(equalToConstant: 132),
    ]

    fullScreenAdvertLayoutConstraints = [
      advertContainer.leadingAnchor.constraint(equalTo: view.leadingAnchor),
      advertContainer.trailingAnchor.constraint(equalTo: view.trailingAnchor),
      advertContainer.topAnchor.constraint(equalTo: view.topAnchor),
      advertContainer.bottomAnchor.constraint(equalTo: view.bottomAnchor),
    ]

    NSLayoutConstraint.activate(transactionLayoutConstraints)
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
      titleBar.isHidden = fullScreenAdvert
      orderPanel.isHidden = fullScreenAdvert
      summaryPanel.isHidden = fullScreenAdvert
      advertContainer.backgroundColor = fullScreenAdvert
        ? .clear
        : UIColor.white.withAlphaComponent(0.035)
      advertContainer.layer.cornerRadius = fullScreenAdvert ? 0 : 12
      advertContainer.layer.borderWidth = fullScreenAdvert ? 0 : 1
      NSLayoutConstraint.activate(
        fullScreenAdvert
          ? fullScreenAdvertLayoutConstraints
          : transactionLayoutConstraints
      )
      view.layoutIfNeeded()
    }
  }

  private func makeColumnHeader(
    _ title: String,
    alignment: NSTextAlignment,
    width: CGFloat? = nil
  ) -> UILabel {
    let label = UILabel()
    label.text = title
    label.font = .systemFont(ofSize: 16, weight: .semibold)
    label.textColor = UIColor.white.withAlphaComponent(0.88)
    label.textAlignment = alignment
    if let width {
      label.widthAnchor.constraint(equalToConstant: width).isActive = true
      label.setContentHuggingPriority(.required, for: .horizontal)
      label.setContentCompressionResistancePriority(.required, for: .horizontal)
    } else {
      label.setContentHuggingPriority(.defaultLow, for: .horizontal)
      label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    }
    return label
  }

  private func makeMetric(
    title: String,
    valueLabel: UILabel
  ) -> UIView {
    let titleLabel = UILabel()
    titleLabel.text = title
    titleLabel.font = .systemFont(ofSize: 14, weight: .medium)
    titleLabel.textColor = UIColor.white.withAlphaComponent(0.64)

    valueLabel.font = .monospacedDigitSystemFont(ofSize: 23, weight: .bold)
    valueLabel.textColor = .white
    valueLabel.adjustsFontSizeToFitWidth = true
    valueLabel.minimumScaleFactor = 0.72

    let stack = UIStackView(arrangedSubviews: [titleLabel, valueLabel])
    stack.axis = .vertical
    stack.alignment = .fill
    stack.distribution = .fill
    stack.spacing = 5
    return stack
  }

  private func makeVerticalDivider() -> UIView {
    let divider = UIView()
    divider.backgroundColor = UIColor.white.withAlphaComponent(0.26)
    divider.translatesAutoresizingMaskIntoConstraints = false
    divider.widthAnchor.constraint(equalToConstant: 1).isActive = true
    return divider
  }

  private func visibleItemWindow(
    for snapshot: HBExternalDisplaySnapshot
  ) -> HBExternalDisplayItemWindow {
    let itemCount = snapshot.items.count
    let start: Int
    if let visibleItemStart = snapshot.visibleItemStart {
      start = min(max(visibleItemStart, 0), max(itemCount - 12, 0))
    } else {
      start = max(itemCount - 12, 0)
    }
    let end = min(start + 12, itemCount)
    return HBExternalDisplayItemWindow(
      items: Array(snapshot.items[start..<end]),
      hiddenAbove: start,
      hiddenBelow: max(itemCount - end, 0)
    )
  }

  private func moreItemsText(hiddenAbove: Int, hiddenBelow: Int) -> String? {
    if hiddenAbove > 0 && hiddenBelow > 0 {
      return localizedText(
        english: "\(hiddenAbove) earlier · \(hiddenBelow) later",
        chinese: "上方 \(hiddenAbove) 件 · 下方 \(hiddenBelow) 件"
      )
    }
    if hiddenAbove > 0 {
      return localizedText(
        english: "\(hiddenAbove) earlier",
        chinese: "前面还有 \(hiddenAbove) 件"
      )
    }
    if hiddenBelow > 0 {
      return localizedText(
        english: "\(hiddenBelow) later",
        chinese: "后面还有 \(hiddenBelow) 件"
      )
    }
    return nil
  }

  private func replaceItemRows(with items: [HBExternalDisplayItem]) {
    itemStack.arrangedSubviews.forEach { row in
      itemStack.removeArrangedSubview(row)
      row.removeFromSuperview()
    }

    if items.isEmpty {
      itemStack.distribution = .fill
      let emptyTopSpacer = UIView()
      emptyTopSpacer.heightAnchor.constraint(equalToConstant: 22).isActive = true
      emptyTopSpacer.setContentHuggingPriority(.required, for: .vertical)
      emptyTopSpacer.setContentCompressionResistancePriority(.required, for: .vertical)

      let emptyLabel = UILabel()
      emptyLabel.text = localizedText(
        english: "Your basket is empty",
        chinese: "购物篮为空"
      )
      emptyLabel.font = .systemFont(ofSize: 18, weight: .medium)
      emptyLabel.textColor = UIColor.white.withAlphaComponent(0.5)
      emptyLabel.setContentHuggingPriority(.required, for: .vertical)
      emptyLabel.setContentCompressionResistancePriority(.required, for: .vertical)

      // 空态文案紧跟表头，剩余空间由透明占位吸收，避免启动兜底跳版。
      let flexibleSpacer = UIView()
      flexibleSpacer.setContentHuggingPriority(.defaultLow, for: .vertical)
      flexibleSpacer.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
      itemStack.addArrangedSubview(emptyTopSpacer)
      itemStack.addArrangedSubview(emptyLabel)
      itemStack.addArrangedSubview(flexibleSpacer)
      return
    }

    itemStack.distribution = .fill
    for item in items {
      let nameLabel = UILabel()
      nameLabel.text = item.name
      nameLabel.font = .systemFont(ofSize: 16, weight: .medium)
      nameLabel.textColor = .white
      nameLabel.lineBreakMode = .byTruncatingTail
      nameLabel.numberOfLines = 1

      let quantityLabel = UILabel()
      quantityLabel.text = "× \(item.quantity)"
      quantityLabel.font = .monospacedDigitSystemFont(ofSize: 16, weight: .regular)
      quantityLabel.textColor = UIColor.white.withAlphaComponent(0.66)
      quantityLabel.textAlignment = .right
      quantityLabel.widthAnchor.constraint(equalToConstant: 72).isActive = true

      let unitPriceLabel = UILabel()
      unitPriceLabel.text = unitPriceText(for: item)
      unitPriceLabel.font = .monospacedDigitSystemFont(ofSize: 16, weight: .medium)
      unitPriceLabel.textColor = .white
      unitPriceLabel.textAlignment = .right
      unitPriceLabel.widthAnchor.constraint(equalToConstant: 104).isActive = true

      let amountLabel = UILabel()
      amountLabel.text = format(item.amount)
      amountLabel.font = .monospacedDigitSystemFont(ofSize: 17, weight: .bold)
      amountLabel.textColor = .white
      amountLabel.textAlignment = .right
      amountLabel.widthAnchor.constraint(equalToConstant: 104).isActive = true

      let row = UIStackView(
        arrangedSubviews: [nameLabel, quantityLabel, unitPriceLabel, amountLabel]
      )
      row.axis = .horizontal
      row.alignment = .center
      row.spacing = 0
      row.translatesAutoresizingMaskIntoConstraints = false

      let cell = UIView()
      cell.heightAnchor.constraint(equalToConstant: 32).isActive = true
      cell.setContentHuggingPriority(.required, for: .vertical)
      cell.setContentCompressionResistancePriority(.required, for: .vertical)
      let divider = UIView()
      divider.backgroundColor = UIColor.white.withAlphaComponent(0.15)
      divider.translatesAutoresizingMaskIntoConstraints = false
      cell.addSubview(row)
      cell.addSubview(divider)
      NSLayoutConstraint.activate([
        row.leadingAnchor.constraint(equalTo: cell.leadingAnchor),
        row.trailingAnchor.constraint(equalTo: cell.trailingAnchor),
        row.topAnchor.constraint(equalTo: cell.topAnchor),
        row.bottomAnchor.constraint(equalTo: divider.topAnchor),
        divider.leadingAnchor.constraint(equalTo: cell.leadingAnchor),
        divider.trailingAnchor.constraint(equalTo: cell.trailingAnchor),
        divider.bottomAnchor.constraint(equalTo: cell.bottomAnchor),
        divider.heightAnchor.constraint(equalToConstant: 1),
      ])
      itemStack.addArrangedSubview(cell)
    }

    // 商品行严格固定 32pt 并从顶部排列，剩余空间由底部弹性占位吸收，不滚动。
    let flexibleSpacer = UIView()
    flexibleSpacer.setContentHuggingPriority(.defaultLow, for: .vertical)
    flexibleSpacer.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
    itemStack.addArrangedSubview(flexibleSpacer)
  }

  private func resolvedSummary(
    for snapshot: HBExternalDisplaySnapshot
  ) -> HBExternalDisplaySummary {
    if let summary = snapshot.summary {
      return summary
    }

    let quantityTotal = snapshot.items.reduce(Decimal.zero) { total, item in
      total + (Decimal(string: item.quantity) ?? 0)
    }
    return HBExternalDisplaySummary(
      itemQuantity: NSDecimalNumber(decimal: quantityTotal).stringValue,
      skuCount: snapshot.items.count,
      subtotal: HBExternalDisplayMoney(
        cents: snapshot.total.cents + snapshot.discount.cents
      )
    )
  }

  private func formattedItemCount(
    itemQuantity: String,
    skuCount: Int
  ) -> String {
    localizedText(
      english: "\(itemQuantity) items · \(skuCount) SKU",
      chinese: "\(itemQuantity) 件商品 · \(skuCount) 个货号"
    )
  }

  private func unitPriceText(for item: HBExternalDisplayItem) -> String {
    if let unitPrice = item.unitPrice {
      return format(unitPrice)
    }
    return "—"
  }

  private func renderStatusCard(
    mode: HBExternalDisplayMode,
    change: HBExternalDisplayMoney
  ) {
    switch mode {
    case .idle:
      statusTitleLabel.text = localizedText(english: "Ready when you are", chinese: "准备开始")
      statusSubtitleLabel.text = localizedText(english: "Scan an item to begin", chinese: "请扫描商品")
    case .cart:
      statusTitleLabel.text = localizedText(english: "Ready to pay", chinese: "可以付款")
      statusSubtitleLabel.text = localizedText(
        english: "Please follow the cashier's instructions",
        chinese: "请按收银员提示付款"
      )
    case .payment:
      statusTitleLabel.text = localizedText(english: "Payment in progress", chinese: "正在付款")
      statusSubtitleLabel.text = localizedText(
        english: "Please follow the terminal prompts",
        chinese: "请按终端提示完成付款"
      )
    case .change:
      statusTitleLabel.text = localizedText(english: "Your change", chinese: "找零")
      statusSubtitleLabel.text = format(change)
    case .success:
      statusTitleLabel.text = localizedText(english: "Payment complete", chinese: "付款完成")
      statusSubtitleLabel.text = change.cents != 0
        ? localizedText(
          english: "Change \(format(change))",
          chinese: "找零 \(format(change))"
        )
        : localizedText(
          english: "Thank you for shopping with us",
          chinese: "谢谢惠顾"
        )
    }
  }

  // UIKit 启动兜底也固定使用英文，避免 React surface 就绪前短暂显示中文。
  private func localizedText(english: String, chinese _: String) -> String {
    english
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

  private func render(advert: HBExternalDisplayAdvert?) -> String? {
    guard let advert else {
      resetVideoRetryState()
      clearAdvert()
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
      clearAdvert()
      return "advert-file-unavailable"
    }

    switch advert.kind {
    case .image:
      guard let image = UIImage(contentsOfFile: url.path) else {
        clearAdvert()
        return "advert-image-unavailable"
      }
      advertImageView.image = image
      advertImageView.isHidden = false
      currentAdvertIdentity = identity

    case .video:
      guard
        videoFailureCounts[identity, default: 0]
          < maximumVideoFailureCount
      else {
        clearAdvert()
        return "advert-video-retry-exhausted"
      }
      let asset = AVURLAsset(url: url)
      guard asset.isPlayable else {
        recordVideoFailure(for: identity)
        clearAdvert()
        return "advert-video-unavailable"
      }
      advertImageView.isHidden = true
      let player = AVQueuePlayer()
      let templateItem = AVPlayerItem(asset: asset)
      videoLooper = AVPlayerLooper(player: player, templateItem: templateItem)
      guard let playbackItem = player.currentItem else {
        recordVideoFailure(for: identity)
        clearAdvert()
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
    // 先撤销媒体再上报，避免播放器 teardown 再次触发失败通知。
    clearAdvert()
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

  private func clearAdvert() {
    stopMedia()
    advertImageView.image = nil
    advertImageView.isHidden = true
  }
}
