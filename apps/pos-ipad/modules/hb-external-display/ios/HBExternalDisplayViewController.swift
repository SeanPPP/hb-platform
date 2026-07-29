import AVFoundation
import UIKit

final class HBExternalDisplayViewController: UIViewController {
  private let modeLabel = UILabel()
  private let itemStack = UIStackView()
  private let itemCountLabel = UILabel()
  private let gstValueLabel = UILabel()
  private let discountValueLabel = UILabel()
  private let totalValueLabel = UILabel()
  private let changeValueLabel = UILabel()
  private let advertContainer = UIView()
  private let advertImageView = UIImageView()
  private let brandStack = UIStackView()
  private let brandTitleLabel = UILabel()
  private let brandSubtitleLabel = UILabel()

  private var videoPlayer: AVQueuePlayer?
  private var videoLooper: AVPlayerLooper?
  private var videoLayer: AVPlayerLayer?
  private var reactSurface: UIView?

  var hasReactSurface: Bool {
    reactSurface != nil
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
    modeLabel.text = title(for: snapshot.mode)
    replaceItemRows(with: Array(snapshot.items.prefix(12)))
    let hiddenCount = max(snapshot.items.count - 12, 0)
    itemCountLabel.text = hiddenCount == 0
      ? "\(snapshot.items.count) item(s)"
      : "\(snapshot.items.count) item(s) · \(hiddenCount) more"
    gstValueLabel.text = format(snapshot.gst)
    discountValueLabel.text = format(snapshot.discount)
    totalValueLabel.text = format(snapshot.total)
    changeValueLabel.text = format(snapshot.change)

    return render(advert: snapshot.advert)
  }

  func stopMedia() {
    videoPlayer?.pause()
    videoLayer?.removeFromSuperlayer()
    videoLayer = nil
    videoLooper = nil
    videoPlayer = nil
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
    let content = UIStackView()
    content.axis = .horizontal
    content.alignment = .fill
    content.distribution = .fill
    content.spacing = 32
    content.translatesAutoresizingMaskIntoConstraints = false
    view.addSubview(content)

    let checkoutPanel = UIStackView()
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

    advertImageView.contentMode = .scaleAspectFill
    advertImageView.translatesAutoresizingMaskIntoConstraints = false
    advertContainer.addSubview(advertImageView)

    brandStack.axis = .vertical
    brandStack.alignment = .fill
    brandStack.spacing = 14
    brandStack.translatesAutoresizingMaskIntoConstraints = false
    brandStack.addArrangedSubview(brandTitleLabel)
    brandStack.addArrangedSubview(brandSubtitleLabel)
    advertContainer.addSubview(brandStack)

    content.addArrangedSubview(checkoutPanel)
    content.addArrangedSubview(advertContainer)

    NSLayoutConstraint.activate([
      content.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 34),
      content.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -34),
      content.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 28),
      content.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -28),
      checkoutPanel.widthAnchor.constraint(greaterThanOrEqualTo: content.widthAnchor, multiplier: 0.52),
      advertContainer.widthAnchor.constraint(greaterThanOrEqualTo: content.widthAnchor, multiplier: 0.34),
      advertImageView.leadingAnchor.constraint(equalTo: advertContainer.leadingAnchor),
      advertImageView.trailingAnchor.constraint(equalTo: advertContainer.trailingAnchor),
      advertImageView.topAnchor.constraint(equalTo: advertContainer.topAnchor),
      advertImageView.bottomAnchor.constraint(equalTo: advertContainer.bottomAnchor),
      brandStack.leadingAnchor.constraint(equalTo: advertContainer.leadingAnchor, constant: 24),
      brandStack.trailingAnchor.constraint(equalTo: advertContainer.trailingAnchor, constant: -24),
      brandStack.centerYAnchor.constraint(equalTo: advertContainer.centerYAnchor),
    ])
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
    stopMedia()
    advertImageView.image = nil

    guard let advert else {
      showBrandPlaceholder()
      return nil
    }

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

    case .video:
      advertImageView.isHidden = true
      brandStack.isHidden = true
      let player = AVQueuePlayer()
      let item = AVPlayerItem(url: url)
      videoLooper = AVPlayerLooper(player: player, templateItem: item)
      let layer = AVPlayerLayer(player: player)
      layer.videoGravity = .resizeAspectFill
      advertContainer.layer.insertSublayer(layer, at: 0)
      videoPlayer = player
      videoLayer = layer
      player.play()
      view.setNeedsLayout()
    }

    return nil
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
}
