import CoreBluetooth
import CoreImage
import Foundation
import UIKit

@objc(HbPrinterModule)
class HbPrinterModule: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
  private struct PriceParts {
    let integer: String
    let decimal: String
  }

  private struct PrinterBitmap {
    let width: Int
    let height: Int
    let hex: String
    let inkMinY: Int
    let inkMaxY: Int

    var widthBytes: Int {
      (width + 7) / 8
    }

    var inkHeight: Int {
      max(1, inkMaxY - inkMinY + 1)
    }
  }

  private let bluetoothQueue = DispatchQueue(label: "com.hbweb.expo.printer.ble")
  private var centralManager: CBCentralManager!
  private var discoveredPrinters: [String: [String: Any]] = [:]
  private var discoveredPeripherals: [String: CBPeripheral] = [:]
  private var scanResolve: RCTPromiseResolveBlock?
  private var scanReject: RCTPromiseRejectBlock?
  private var scanFinishWorkItem: DispatchWorkItem?
  private var connectedPeripheral: CBPeripheral?
  private var writeCharacteristic: CBCharacteristic?
  private var connectedAddress: String?
  private var connectResolve: RCTPromiseResolveBlock?
  private var connectReject: RCTPromiseRejectBlock?
  private var connectTimeoutWorkItem: DispatchWorkItem?
  private var pendingCharacteristicServiceCount = 0
  private var printResolve: RCTPromiseResolveBlock?
  private var printReject: RCTPromiseRejectBlock?
  private var pendingWriteChunks: [Data] = []
  private var pendingWriteType: CBCharacteristicWriteType?
  private var pendingWriteCharacteristic: CBCharacteristic?
  private var printTimeoutWorkItem: DispatchWorkItem?

  override init() {
    super.init()
    centralManager = CBCentralManager(delegate: self, queue: bluetoothQueue)
  }

  @objc(getStatus:rejecter:)
  func getStatus(_ resolve: @escaping RCTPromiseResolveBlock, rejecter reject: @escaping RCTPromiseRejectBlock) {
    bluetoothQueue.async {
      let state = self.centralManager.state
      let status = NSMutableDictionary()
      status["supported"] = state != .unsupported
      status["enabled"] = state == .poweredOn
      status["connected"] = self.connectedPeripheral?.state == .connected && self.writeCharacteristic != nil
      status["address"] = self.connectedAddress ?? NSNull()
      self.resolve(resolve, status)
    }
  }

  @objc(scanPrinters:resolver:rejecter:)
  func scanPrinters(
    _ durationMs: NSNumber,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      guard self.ensureBluetoothReady(reject) else {
        return
      }

      guard self.scanResolve == nil else {
        self.reject(reject, "SCAN_IN_PROGRESS", "Bluetooth printer scan is already running.")
        return
      }

      // iOS BLE 没有 Android MAC 地址，这里用 peripheral UUID 作为后续 connect(address) 的稳定地址。
      self.discoveredPrinters.removeAll()
      self.discoveredPeripherals.removeAll()
      self.scanResolve = resolve
      self.scanReject = reject
      if self.centralManager.isScanning {
        self.centralManager.stopScan()
      }
      self.centralManager.scanForPeripherals(withServices: nil, options: [
        CBCentralManagerScanOptionAllowDuplicatesKey: false,
      ])

      let delayMs = max(durationMs.intValue, 1500)
      let workItem = DispatchWorkItem { [weak self] in
        self?.finishScan()
      }
      self.scanFinishWorkItem = workItem
      self.bluetoothQueue.asyncAfter(deadline: .now() + .milliseconds(delayMs), execute: workItem)
    }
  }

  @objc(connect:resolver:rejecter:)
  func connect(
    _ address: String,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      guard self.ensureBluetoothReady(reject) else {
        return
      }

      guard self.connectResolve == nil else {
        self.reject(reject, "CONNECT_IN_PROGRESS", "Bluetooth printer connection is already running.")
        return
      }

      guard let uuid = UUID(uuidString: address) else {
        self.reject(reject, "INVALID_ADDRESS", "iOS Bluetooth printer address must be a peripheral UUID.")
        return
      }

      guard let peripheral = self.discoveredPeripherals[address]
        ?? self.centralManager.retrievePeripherals(withIdentifiers: [uuid]).first else {
        self.reject(reject, "CONNECT_ERROR", "Bluetooth printer was not found. Please scan again before connecting.")
        return
      }

      self.disconnectInternal()
      self.connectResolve = resolve
      self.connectReject = reject
      peripheral.delegate = self
      self.connectedPeripheral = peripheral
      self.connectedAddress = address
      self.centralManager.connect(peripheral, options: nil)

      // 连接和服务发现都依赖外设回调，超时后清理状态，避免 Promise 悬挂。
      let workItem = DispatchWorkItem { [weak self] in
        guard let self else {
          return
        }
        self.failPendingConnect("CONNECT_TIMEOUT", "Bluetooth printer connection timed out.")
      }
      self.connectTimeoutWorkItem = workItem
      self.bluetoothQueue.asyncAfter(deadline: .now() + .seconds(12), execute: workItem)
    }
  }

  @objc(disconnect:rejecter:)
  func disconnect(_ resolve: @escaping RCTPromiseResolveBlock, rejecter reject: @escaping RCTPromiseRejectBlock) {
    bluetoothQueue.async {
      self.disconnectInternal()
      self.resolve(resolve, true)
    }
  }

  @objc(print:encoding:resolver:rejecter:)
  func print(
    _ command: String,
    encoding: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      self.writePrinterCommand(command, encoding: encoding, resolver: resolve, rejecter: reject)
    }
  }

  @objc(printProductLabel:printType:resolver:rejecter:)
  func printProductLabel(
    _ payload: NSDictionary,
    printType: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      let command = self.buildProductLabelCommand(payload, printType: printType)
      self.writePrinterCommand(command, encoding: "GB18030", resolver: resolve, rejecter: reject)
    }
  }

  @objc(printDiscountLabel:printType:resolver:rejecter:)
  func printDiscountLabel(
    _ payload: NSDictionary,
    printType: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      let command = self.buildDiscountLabelCommand(payload, printType: printType)
      self.writePrinterCommand(command, encoding: "GB18030", resolver: resolve, rejecter: reject)
    }
  }

  @objc(printClearanceLabel:resolver:rejecter:)
  func printClearanceLabel(
    _ payload: NSDictionary,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    rejectUnsupportedLabelPrint(reject)
  }

  @objc(printBigDiscountLabel:printType:resolver:rejecter:)
  func printBigDiscountLabel(
    _ payload: NSDictionary,
    printType: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      let command = self.buildBigDiscountLabelCommand(payload, printType: printType)
      self.writePrinterCommand(command, encoding: "GB18030", resolver: resolve, rejecter: reject)
    }
  }

  @objc(printWarehouseProductLabel:resolver:rejecter:)
  func printWarehouseProductLabel(
    _ payload: NSDictionary,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      let command = self.buildWarehouseProductLabelCommand(payload)
      self.writePrinterCommand(command, encoding: "GB18030", resolver: resolve, rejecter: reject)
    }
  }

  @objc(printWarehouseLocationLabel:resolver:rejecter:)
  func printWarehouseLocationLabel(
    _ payload: NSDictionary,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    bluetoothQueue.async {
      let command = self.buildWarehouseLocationLabelCommand(payload)
      self.writePrinterCommand(command, encoding: "GB18030", resolver: resolve, rejecter: reject)
    }
  }

  func centralManagerDidUpdateState(_ central: CBCentralManager) {
    if central.state != .poweredOn {
      failPendingScan("BLUETOOTH_DISABLED", "Bluetooth is turned off.")
      if connectResolve != nil {
        failPendingConnect("BLUETOOTH_DISABLED", "Bluetooth is turned off.")
      }
    }
  }

  func centralManager(
    _ central: CBCentralManager,
    didDiscover peripheral: CBPeripheral,
    advertisementData: [String: Any],
    rssi RSSI: NSNumber
  ) {
    let address = peripheral.identifier.uuidString
    discoveredPeripherals[address] = peripheral
    let name = peripheral.name
      ?? advertisementData[CBAdvertisementDataLocalNameKey] as? String
      ?? "Bluetooth Printer"
    discoveredPrinters[address] = [
      "name": name,
      "address": address,
      "bonded": false,
      "connected": address == connectedAddress && peripheral.state == .connected,
    ]
  }

  func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
    peripheral.discoverServices(nil)
  }

  func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
    failPendingConnect("CONNECT_ERROR", error?.localizedDescription ?? "Failed to connect Bluetooth printer.")
  }

  func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
    if peripheral.identifier.uuidString == connectedAddress {
      if printResolve != nil {
        failPendingPrint("PRINT_ERROR", error?.localizedDescription ?? "Bluetooth printer disconnected while printing.")
      }
      writeCharacteristic = nil
      connectedPeripheral = nil
      connectedAddress = nil
    }
  }

  func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
    if let error {
      failPendingConnect("CONNECT_ERROR", error.localizedDescription)
      return
    }

    let services = peripheral.services ?? []
    if services.isEmpty {
      failPendingConnect("CONNECT_ERROR", "Bluetooth printer did not expose writable services.")
      return
    }

    pendingCharacteristicServiceCount = services.count
    services.forEach { service in
      peripheral.discoverCharacteristics(nil, for: service)
    }
  }

  func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
    defer {
      pendingCharacteristicServiceCount = max(0, pendingCharacteristicServiceCount - 1)
      if writeCharacteristic == nil && pendingCharacteristicServiceCount == 0 && connectResolve != nil {
        failPendingConnect("CONNECT_ERROR", "Bluetooth printer did not expose a writable characteristic.")
      }
    }

    if let error {
      failPendingConnect("CONNECT_ERROR", error.localizedDescription)
      return
    }

    guard let characteristic = service.characteristics?.first(where: { item in
      item.properties.contains(.write) || item.properties.contains(.writeWithoutResponse)
    }) else {
      return
    }

    writeCharacteristic = characteristic
    connectTimeoutWorkItem?.cancel()
    connectTimeoutWorkItem = nil
    let resolve = connectResolve
    connectResolve = nil
    connectReject = nil
    self.resolve(resolve, true)
  }

  func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
    if let error {
      failPendingPrint("PRINT_ERROR", error.localizedDescription)
      return
    }

    flushPendingWrites()
  }

  func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
    flushPendingWrites()
  }

  private func writePrinterCommand(
    _ command: String,
    encoding: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    guard let peripheral = connectedPeripheral,
          peripheral.state == .connected,
          let characteristic = writeCharacteristic else {
      self.reject(reject, "PRINT_ERROR", "No Bluetooth printer is connected.")
      return
    }

    guard printResolve == nil else {
      self.reject(reject, "PRINT_IN_PROGRESS", "Bluetooth printer is already printing.")
      return
    }

    do {
      let data = try encodeCommand(command, encoding: encoding)
      if data.isEmpty {
        self.resolve(resolve, true)
        return
      }

      let writeType: CBCharacteristicWriteType = characteristic.properties.contains(.write) ? .withResponse : .withoutResponse
      let chunkSize = max(20, peripheral.maximumWriteValueLength(for: writeType))
      pendingWriteChunks = stride(from: 0, to: data.count, by: chunkSize).map { offset in
        data.subdata(in: offset..<min(offset + chunkSize, data.count))
      }
      pendingWriteType = writeType
      pendingWriteCharacteristic = characteristic
      printResolve = resolve
      printReject = reject

      // 位图标签的 EG 命令明显更大，按数据量放宽超时，避免慢速 BLE withResponse 误报失败。
      let timeoutSeconds = printTimeoutSeconds(forByteCount: data.count)
      // BLE 写入有 MTU 和发送窗口上限；必须等系统回调后再继续，避免假成功。
      let workItem = DispatchWorkItem { [weak self] in
        guard let self else {
          return
        }
        self.failPendingPrint("PRINT_TIMEOUT", "Bluetooth printer write timed out.")
      }
      printTimeoutWorkItem = workItem
      bluetoothQueue.asyncAfter(deadline: .now() + .seconds(timeoutSeconds), execute: workItem)
      flushPendingWrites()
    } catch {
      self.reject(reject, "PRINT_ERROR", error.localizedDescription)
    }
  }

  private func printTimeoutSeconds(forByteCount byteCount: Int) -> Int {
    max(15, min(120, 10 + Int(ceil(Double(byteCount) / 1024.0))))
  }

  private func ensureBluetoothReady(_ reject: @escaping RCTPromiseRejectBlock) -> Bool {
    switch centralManager.state {
    case .poweredOn:
      return true
    case .unsupported:
      self.reject(reject, "BLUETOOTH_UNSUPPORTED", "Bluetooth is not supported on this device.")
    case .unauthorized:
      self.reject(reject, "BLUETOOTH_UNAUTHORIZED", "Bluetooth permission was not granted.")
    case .poweredOff:
      self.reject(reject, "BLUETOOTH_DISABLED", "Bluetooth is turned off.")
    default:
      self.reject(reject, "BLUETOOTH_NOT_READY", "Bluetooth is not ready yet. Please try again.")
    }
    return false
  }

  private func finishScan() {
    guard let resolve = scanResolve else {
      return
    }

    centralManager.stopScan()
    scanFinishWorkItem?.cancel()
    scanFinishWorkItem = nil
    scanResolve = nil
    scanReject = nil
    let printers = discoveredPrinters.values.sorted {
      String(describing: $0["name"] ?? "") < String(describing: $1["name"] ?? "")
    }
    self.resolve(resolve, printers)
  }

  private func failPendingScan(_ code: String, _ message: String) {
    guard let reject = scanReject else {
      return
    }

    centralManager.stopScan()
    scanFinishWorkItem?.cancel()
    scanFinishWorkItem = nil
    scanResolve = nil
    scanReject = nil
    self.reject(reject, code, message)
  }

  private func failPendingConnect(_ code: String, _ message: String) {
    guard connectResolve != nil || connectReject != nil else {
      return
    }

    let reject = connectReject
    connectTimeoutWorkItem?.cancel()
    connectTimeoutWorkItem = nil
    connectResolve = nil
    connectReject = nil
    disconnectInternal()
    self.reject(reject, code, message)
  }

  private func flushPendingWrites() {
    guard let peripheral = connectedPeripheral,
          peripheral.state == .connected,
          let characteristic = pendingWriteCharacteristic,
          let writeType = pendingWriteType else {
      failPendingPrint("PRINT_ERROR", "No Bluetooth printer is connected.")
      return
    }

    while !pendingWriteChunks.isEmpty {
      if writeType == .withoutResponse && !peripheral.canSendWriteWithoutResponse {
        return
      }

      let chunk = pendingWriteChunks.removeFirst()
      peripheral.writeValue(chunk, for: characteristic, type: writeType)
      if writeType == .withResponse {
        return
      }
    }

    finishPendingPrint()
  }

  private func finishPendingPrint() {
    let resolve = printResolve
    printTimeoutWorkItem?.cancel()
    printTimeoutWorkItem = nil
    printResolve = nil
    printReject = nil
    pendingWriteChunks.removeAll()
    pendingWriteType = nil
    pendingWriteCharacteristic = nil
    self.resolve(resolve, true)
  }

  private func failPendingPrint(_ code: String, _ message: String) {
    guard printResolve != nil || printReject != nil else {
      return
    }

    let reject = printReject
    printTimeoutWorkItem?.cancel()
    printTimeoutWorkItem = nil
    printResolve = nil
    printReject = nil
    pendingWriteChunks.removeAll()
    pendingWriteType = nil
    pendingWriteCharacteristic = nil
    self.reject(reject, code, message)
  }

  private func disconnectInternal() {
    if printResolve != nil {
      failPendingPrint("PRINT_ERROR", "Bluetooth printer disconnected while printing.")
    }
    if let peripheral = connectedPeripheral {
      centralManager.cancelPeripheralConnection(peripheral)
    }
    writeCharacteristic = nil
    connectedPeripheral = nil
    connectedAddress = nil
  }

  private func buildProductLabelCommand(_ payload: NSDictionary, printType: String?) -> String {
    let isSmall = printType?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == "small"
    let width = isSmall ? 472 : 570
    let height = isSmall ? 320 : 400
    let productName = dictString(payload, "productName")
    let itemNumber = dictString(payload, "itemNumber")
    let supplierName = formatSupplierAbbreviation(dictString(payload, "supplierName"))
    let barcode = cpclText(dictString(payload, "barcode"), maxLength: 64)
    let retailPrice = dictDouble(payload, "retailPrice")
    let discountRate = dictDouble(payload, "discountRate") ?? 0
    let grade = formatGrade(dictString(payload, "grade"))
    let price = formatPriceParts(retailPrice)

    // 普通商品标签对齐 Android：所有文字先渲染成单色位图，再写入 CPCL EG 命令。
    let priceIntegerBitmap = textToBitmap(price.integer, fontSize: fontSizeToPixels(40), isBold: true, fontFamily: "sans-serif-black")
    let priceDotBitmap = textToBitmap(".", fontSize: fontSizeToPixels(20), isBold: false, fontFamily: "sans-serif-black")
    let priceDecimalBitmap = textToBitmap(price.decimal, fontSize: fontSizeToPixels(20), isBold: false, fontFamily: "sans-serif-black")
    let priceCurrencyBitmap = textToBitmap("$", fontSize: fontSizeToPixels(20), isBold: false, fontFamily: "sans-serif-black")
    let itemBitmap = textToBitmap(itemNumber, fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black")
    let supplierBitmap = textToBitmap(supplierName, fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-light", isInverse: true, padding: 2)
    let dateBitmap = textToBitmap(todayString(), fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let priceCurrencyX = width - priceDecimalBitmap.width - priceDotBitmap.width - priceIntegerBitmap.width - priceCurrencyBitmap.width
    // 商品名从 x=5 开始绘制，右侧预留价格块和 10px 间距，避免长名称压住价格。
    let nameMaxWidth = max(1, priceCurrencyX - 10)
    let nameBitmap = longTextToBitmap(productName, fontSize: fontSizeToPixels(10), isBold: false, fontFamily: "Arial", maxLines: 2, maxWidth: nameMaxWidth)
    let discountBitmap: PrinterBitmap?
    if discountRate > 0 {
      discountBitmap = textToBitmap(discountLabel(discountRate), fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    } else {
      discountBitmap = nil
    }

    let gradeBitmap: PrinterBitmap?
    if grade.isEmpty {
      gradeBitmap = nil
    } else {
      gradeBitmap = textToBitmap(grade, fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 4)
    }

    let startY = 30
    // iOS 位图高度包含 UIKit 行高；普通标签小数点按真实墨迹底部和整数对齐。
    let priceDotY = startY + priceIntegerBitmap.inkMaxY - priceDotBitmap.inkMaxY
    let startX = width - priceDecimalBitmap.width
    var commands = [
      "! 0 200 200 \(height) 1",
      "PAGE-WIDTH \(width)",
      bitmapCommand(5, 5, nameBitmap),
      bitmapCommand(5, 120, itemBitmap),
      bitmapCommand(5 + itemBitmap.width + 10, 118, supplierBitmap),
    ]

    if !barcode.isEmpty {
      let barcodeType = isValidEan13(barcode) ? "EAN13" : "128"
      commands.append("BARCODE-TEXT 7 0 5")
      commands.append("BARCODE \(barcodeType) 1 2 30 5 145 \(barcode)")
    }

    if let discountBitmap {
      commands.append(bitmapCommand(width - discountBitmap.width - dateBitmap.width - 20, 175, discountBitmap))
    }

    if let gradeBitmap {
      commands.append(bitmapCommand(300, 175, gradeBitmap))
    }

    commands.append(bitmapCommand(startX, startY, priceDecimalBitmap))
    commands.append(bitmapCommand(startX - priceDotBitmap.width, priceDotY, priceDotBitmap))
    commands.append(bitmapCommand(startX - priceDotBitmap.width - priceIntegerBitmap.width, startY, priceIntegerBitmap))
    commands.append(bitmapCommand(
      priceCurrencyX,
      startY,
      priceCurrencyBitmap
    ))
    commands.append(bitmapCommand(width - dateBitmap.width, 175, dateBitmap))
    commands.append("PRINT")
    return commands.joined(separator: "\r\n") + "\r\n"
  }

  private func buildDiscountLabelCommand(_ payload: NSDictionary, printType: String?) -> String {
    let isSmall = printType?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == "small"
    let width = isSmall ? 472 : 570
    let height = isSmall ? 320 : 400
    let productName = dictString(payload, "productName")
    let itemNumber = dictString(payload, "itemNumber")
    let barcodeValue = dictString(payload, "barcode")
    let barcode = barcodeValue.isEmpty ? itemNumber : barcodeValue
    let retailPrice = dictDouble(payload, "retailPrice") ?? 0
    let discountRate = dictDouble(payload, "discountRate") ?? 0
    let discountValue = discountRate * 100
    let nowPrice = retailPrice * (1 - discountRate)

    // 折扣标签对齐 Android：折扣数字、Now 价、二维码和日期全部使用位图绘制。
    let nowLabelBitmap = textToBitmap("Now", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let nowPriceBitmap = textToBitmap("$\(formatMoney(nowPrice))", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let discountBitmap = textToBitmap(String(format: "%02d", Int(discountValue.rounded())), fontSize: fontSizeToPixels(44), isBold: false, fontFamily: "sans-serif-black")
    let offBitmap = textToBitmap("OFF", fontSize: fontSizeToPixels(16), isBold: true, fontFamily: "sans-serif-black")
    let percentBitmap = textToBitmap("%", fontSize: fontSizeToPixels(20), isBold: true, fontFamily: "sans-serif-black")
    let dateBitmap = textToBitmap(todayString(), fontSize: fontSizeToPixels(8), isBold: false, fontFamily: "Arial", isInverse: true, padding: 2)
    let itemBitmap = itemNumber.isEmpty ? nil : textToBitmap(itemNumber, fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black")

    let startY = 20
    let startX = width - discountBitmap.width - percentBitmap.width - offBitmap.width + 20
    let rightMargin = 12
    let columnGap = 10
    let nowGroupGap = 6
    let qrBitmap = barcode.isEmpty ? nil : createQrCodeBitmap(barcode, size: 64)
    let qrVisualWidth = qrBitmap?.width ?? 64
    let effectiveLabelBottom = 204
    let bottomMargin = 10
    let infoBandBottom = effectiveLabelBottom - bottomMargin
    let qrX = 10
    let qrY = infoBandBottom - (qrBitmap?.height ?? 64)
    let nowPriceX = width - rightMargin - nowPriceBitmap.width
    let nowLabelX = nowPriceX - nowGroupGap - nowLabelBitmap.width
    let nowLabelY = infoBandBottom - nowLabelBitmap.height
    let nowPriceY = infoBandBottom - nowPriceBitmap.height
    let dateX = qrX + qrVisualWidth + columnGap
    let dateY = infoBandBottom - dateBitmap.height
    let itemX = dateX
    let itemY = dateY - (itemBitmap?.height ?? 0) - 6
    // iOS 位图高度包含 UIKit 行高；OFF 按真实墨迹底部和折扣数字对齐，避免压住 Now 价。
    let discountOffY = startY + discountBitmap.inkMaxY - offBitmap.inkMaxY
    let nameMaxWidth = max(1, width - discountBitmap.width - percentBitmap.width - offBitmap.width + 10)
    let nameBitmap = longTextToBitmap(productName, fontSize: fontSizeToPixels(10), isBold: false, fontFamily: "Arial", maxLines: 2, maxWidth: nameMaxWidth)

    var commands = [
      "! 0 200 200 \(height) 1",
      "PAGE-WIDTH \(width)",
      bitmapCommand(5, 5, nameBitmap),
      bitmapCommand(startX, startY, discountBitmap),
      bitmapCommand(startX + discountBitmap.width, startY, percentBitmap),
      bitmapCommand(
        startX + discountBitmap.width + percentBitmap.width / 2,
        discountOffY,
        offBitmap
      ),
    ]

    if let itemBitmap {
      commands.append(bitmapCommand(itemX, itemY, itemBitmap))
    }

    if let qrBitmap {
      commands.append(bitmapCommand(qrX, qrY, qrBitmap))
    }

    commands.append(bitmapCommand(dateX, dateY, dateBitmap))
    commands.append(bitmapCommand(nowLabelX, nowLabelY, nowLabelBitmap))
    commands.append(bitmapCommand(nowPriceX, nowPriceY, nowPriceBitmap))
    commands.append("PRINT")
    return commands.joined(separator: "\r\n") + "\r\n"
  }

  private func buildBigDiscountLabelCommand(_ payload: NSDictionary, printType: String?) -> String {
    let productName = dictString(payload, "productName")
    let barcode = dictString(payload, "barcode")
    let retailPrice = dictDouble(payload, "retailPrice") ?? 0
    let discountRate = dictDouble(payload, "discountRate") ?? 0
    let paperWidth = 480
    let afterDiscount = retailPrice * (1 - discountRate)
    let price = formatPriceParts(afterDiscount)
    let saveAmount = retailPrice * discountRate

    var commands = [
      "! 0 200 200 1200 1",
      "PAGE-WIDTH \(paperWidth)",
    ]

    commands.append(contentsOf: buildBigDiscountHeaderCommands(discountRate: discountRate, printType: printType, paperWidth: paperWidth))

    let currencyBitmap = textToBitmap("$", fontSize: fontSizeToPixels(20), isBold: false, fontFamily: "sans-serif-black")
    let wasCurrencyBitmap = textToBitmap("$", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black")
    let saveCurrencyBitmap = textToBitmap("$", fontSize: fontSizeToPixels(8), isBold: false, fontFamily: "sans-serif-black")
    let eaBitmap = textToBitmap("ea", fontSize: fontSizeToPixels(8), isBold: false, fontFamily: "sans-serif-light")
    let wasBitmap = textToBitmap("WAS ", fontSize: fontSizeToPixels(10), isBold: true, fontFamily: "sans-serif-light")
    let saveBitmap = textToBitmap("SAVE", fontSize: fontSizeToPixels(16), isBold: true, fontFamily: "sans-serif-light", isInverse: true, padding: 2)
    let intBitmap = textToBitmap(price.integer, fontSize: fontSizeToPixels(60), isBold: true, fontFamily: "sans-serif-black")
    let decimalBitmap = textToBitmap(price.decimal, fontSize: fontSizeToPixels(30), isBold: false, fontFamily: "sans-serif-black")
    let dotBitmap = textToBitmap(".", fontSize: fontSizeToPixels(36), isBold: false, fontFamily: "sans-serif-black")
    let rrpBitmap = textToBitmap(formatMoney(retailPrice), fontSize: fontSizeToPixels(10), isBold: true, fontFamily: "sans-serif-condensed")
    let saveAmountBitmap = textToBitmap(formatMoney(saveAmount), fontSize: fontSizeToPixels(16), isBold: true, fontFamily: "sans-serif-condensed")
    let nameBitmap = longTextToBitmap(productName, fontSize: fontSizeToPixels(10), isBold: false, fontFamily: "Arial", maxLines: 4, maxWidth: paperWidth)
    let dashLineBitmap = createDashLineBitmap(width: 450, height: 2)
    let dateBitmap = textToBitmap(todayString(), fontSize: fontSizeToPixels(8), isBold: false, fontFamily: "Arial", isInverse: true, padding: 2)

    let startY = 220
    var startX = (paperWidth - currencyBitmap.width - intBitmap.width) / 2
    var eaX = startX + currencyBitmap.width + intBitmap.width + 30
    if (Int(price.decimal) ?? 0) != 0 {
      startX = (paperWidth - currencyBitmap.width - intBitmap.width - dotBitmap.width - decimalBitmap.width) / 2
      eaX = startX + currencyBitmap.width + intBitmap.width + dotBitmap.width + decimalBitmap.width + 12
      // 大折扣当前价的小数点按真实墨迹底部对齐个位数字，避免 UIKit 行高把点压低。
      let priceDotY = startY + intBitmap.inkMaxY - dotBitmap.inkMaxY
      commands.append(bitmapCommand(startX + currencyBitmap.width + intBitmap.width, priceDotY, dotBitmap))
      commands.append(bitmapCommand(startX + currencyBitmap.width + intBitmap.width + dotBitmap.width, startY, decimalBitmap))
    }

    commands.append(bitmapCommand(startX, startY, currencyBitmap))
    commands.append(bitmapCommand(startX + currencyBitmap.width, startY, intBitmap))
    commands.append(bitmapCommand(eaX, startY + Int(Double(intBitmap.height) * 0.9), eaBitmap))

    let rrpStartY = startY + intBitmap.height + 20
    commands.append(bitmapCommand(5, rrpStartY, wasBitmap))
    commands.append(bitmapCommand(5 + wasBitmap.width, rrpStartY, wasCurrencyBitmap))
    commands.append(bitmapCommand(5 + wasBitmap.width + wasCurrencyBitmap.width, rrpStartY, rrpBitmap))

    if discountRate > 0 {
      commands.append("LINE 5 \(rrpStartY) \(5 + wasBitmap.width + rrpBitmap.width) \(rrpStartY + wasBitmap.height) 2")
      commands.append("LINE 5 \(rrpStartY + wasBitmap.height) \(5 + wasBitmap.width + rrpBitmap.width) \(rrpStartY) 2")
      let saveStartX = 30 + wasBitmap.width + wasCurrencyBitmap.width + rrpBitmap.width
      commands.append(bitmapCommand(saveStartX, rrpStartY, saveBitmap))
      commands.append(bitmapCommand(saveStartX + saveBitmap.width + 5, rrpStartY, saveCurrencyBitmap))
      commands.append(bitmapCommand(saveStartX + saveBitmap.width + 5 + saveCurrencyBitmap.width + 5, rrpStartY, saveAmountBitmap))
    }

    commands.append(bitmapCommand(5, 550 - nameBitmap.height - 5, nameBitmap))
    commands.append(bitmapCommand(15, 550, dashLineBitmap))

    if !barcode.isEmpty {
      commands.append("BARCODE 128 1 2 30 15 560 \(cpclText(barcode))")
    }

    commands.append(bitmapCommand(paperWidth - dateBitmap.width - 10, 630 - dateBitmap.height, dateBitmap))
    commands.append("PRINT")
    return commands.joined(separator: "\r\n") + "\r\n"
  }

  private func buildBigDiscountHeaderCommands(discountRate: Double, printType: String?, paperWidth: Int) -> [String] {
    let discount = discountRate * 100
    let title = printType?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    if !title.isEmpty {
      let titleBitmap = textToBitmap(title, fontSize: fontSizeToPixels(25), isBold: true, fontFamily: "sans-serif-black")
      return [bitmapCommand(paperWidth / 2 - titleBitmap.width / 2, 80, titleBitmap)]
    }

    if discount <= 10 || discount > 100 {
      let specialBitmap = textToBitmap("Special", fontSize: fontSizeToPixels(40), isBold: true, fontFamily: "sans-serif-black")
      return [bitmapCommand(paperWidth / 2 - specialBitmap.width / 2, 40, specialBitmap)]
    }

    if abs(discount - 50) < 0.01 {
      let halfBitmap = textToBitmap("1/2", fontSize: fontSizeToPixels(40), isBold: true, fontFamily: "sans-serif-black")
      let priceBitmap = textToBitmap("PRICE", fontSize: fontSizeToPixels(25), isBold: true, fontFamily: "sans-serif-black")
      return [
        bitmapCommand(130, 20, halfBitmap),
        bitmapCommand(120, 20 + halfBitmap.height, priceBitmap),
      ]
    }

    let discountBitmap = textToBitmap(String(Int(discount.rounded())), fontSize: fontSizeToPixels(40), isBold: true, fontFamily: "sans-serif-black")
    let percentBitmap = textToBitmap("%", fontSize: fontSizeToPixels(24), isBold: true, fontFamily: "sans-serif-condensed")
    let offBitmap = textToBitmap("OFF", fontSize: fontSizeToPixels(20), isBold: true, fontFamily: "sans-serif-black")
    let startX = (paperWidth - discountBitmap.width - percentBitmap.width) / 2
    return [
      bitmapCommand(startX, 20, discountBitmap),
      bitmapCommand(startX + discountBitmap.width, 20, percentBitmap),
      bitmapCommand((paperWidth - offBitmap.width) / 2, 20 + discountBitmap.height + 20 - offBitmap.inkHeight, offBitmap),
    ]
  }

  private func buildWarehouseProductLabelCommand(_ payload: NSDictionary) -> String {
    let width = 570
    let height = 208
    let productName = dictString(payload, "productName")
    let itemNumber = dictString(payload, "itemNumber")
    let barcodeValue = dictString(payload, "barcode")
    let barcode = barcodeValue.isEmpty ? itemNumber : barcodeValue
    let middlePackageQuantity = dictDouble(payload, "middlePackageQuantity")
    let purchasePrice = dictDouble(payload, "purchasePrice")
    let retailPrice = dictDouble(payload, "retailPrice")
    let locationCode = dictString(payload, "locationCode")
    let locationBarcode = dictString(payload, "locationBarcode")
    let domesticPrice = dictDouble(payload, "domesticPrice")
    let oemPrice = dictDouble(payload, "oemPrice")
    let importPrice = dictDouble(payload, "importPrice")
    let displayPrice = retailPrice ?? domesticPrice ?? oemPrice ?? importPrice
    let costPrice = purchasePrice ?? importPrice ?? domesticPrice ?? oemPrice
    let priceDetails = [
      "PK \(formatOptionalQuantity(middlePackageQuantity))",
      "COST \(formatOptionalMoney(costPrice))",
      "RRP \(formatOptionalMoney(displayPrice))",
    ]
    let contentWidth = width - 40

    // 仓库商品标签按 Android 的信息密度布局：标题/商品/货号居中，货位和价格区固定。
    let titleBitmap = textToBitmap("WAREHOUSE PRODUCT", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let nameBitmap = longTextToBitmap(productName, fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "Arial", maxLines: 1, maxWidth: max(180, contentWidth))
    let itemText = itemNumber.isEmpty ? "--" : itemNumber
    let itemBitmap = textToBitmap("ITEM \(cpclText(itemText))", fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    let packBitmap = textToBitmap(priceDetails[0], fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    let costBitmap = textToBitmap(priceDetails[1], fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    let rrpBitmap = textToBitmap(priceDetails[2], fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    let locationText = locationCode.isEmpty ? "UNASSIGNED" : locationCode
    let locationBitmap = textToBitmap("LOC \(cpclText(locationText))", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let locationBarcodeBitmap = locationBarcode.isEmpty ? nil : textToBitmap(cpclText(locationBarcode), fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    let dateBitmap = textToBitmap(todayString(), fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    func centerX(_ bitmap: PrinterBitmap) -> Int { max(0, (width - bitmap.width) / 2) }

    var commands = [
      "! 0 200 200 \(height) 1",
      "PAGE-WIDTH \(width)",
      bitmapCommand(centerX(titleBitmap), 12, titleBitmap),
      bitmapCommand(centerX(nameBitmap), 38, nameBitmap),
      bitmapCommand(centerX(itemBitmap), 66, itemBitmap),
      bitmapCommand(20, 92, locationBitmap),
      bitmapCommand(width - dateBitmap.width - 20, 92, dateBitmap),
    ]

    if let locationBarcodeBitmap {
      commands.append(bitmapCommand(centerX(locationBarcodeBitmap), 120, locationBarcodeBitmap))
    }

    if !barcode.isEmpty {
      commands.append("BARCODE 128 1 1 38 24 146 \(cpclText(barcode))")
      commands.append(bitmapCommand(380, 132, packBitmap))
      commands.append(bitmapCommand(380, 156, costBitmap))
      commands.append(bitmapCommand(380, 180, rrpBitmap))
    } else {
      commands.append(bitmapCommand(centerX(packBitmap), 132, packBitmap))
      commands.append(bitmapCommand(centerX(costBitmap), 156, costBitmap))
      commands.append(bitmapCommand(centerX(rrpBitmap), 180, rrpBitmap))
    }

    commands.append("PRINT")
    return commands.joined(separator: "\r\n") + "\r\n"
  }

  private func buildWarehouseLocationLabelCommand(_ payload: NSDictionary) -> String {
    let width = 570
    let height = 208
    let locationCode = dictString(payload, "locationCode")
    let locationBarcode = dictString(payload, "locationBarcode")
    let locationGuid = dictString(payload, "locationGuid")
    let itemValue = dictString(payload, "itemNumber")
    let itemNumber = itemValue.isEmpty ? "--" : itemValue
    let productValue = dictString(payload, "productName")
    let productName = productValue.isEmpty ? "--" : productValue
    let quantityValue = dictDouble(payload, "middlePackageQuantity").map { Int($0.rounded()) }
    let middlePackageQuantity = (quantityValue ?? 1) > 0 ? (quantityValue ?? 1) : 1
    let displayCode = locationCode.isEmpty ? (locationBarcode.isEmpty ? locationGuid : locationBarcode) : locationCode
    let barcode = locationBarcode.isEmpty ? displayCode : locationBarcode

    let titleBitmap = textToBitmap("LOCATION", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    let codeBitmap = longTextToBitmap(displayCode, fontSize: fontSizeToPixels(15), isBold: true, fontFamily: "sans-serif-black", maxLines: 1, maxWidth: width - 30)
    // 货位标签补充商品信息，空货位保持占位，避免打印布局跳动。
    let rightAreaLeft = width - 178
    let leftTextWidth = rightAreaLeft - 28
    let itemBitmap = longTextToBitmap("ITEM \(itemNumber)", fontSize: fontSizeToPixels(9), isBold: true, fontFamily: "sans-serif-black", maxLines: 1, maxWidth: leftTextWidth)
    let nameBitmap = longTextToBitmap("DESC \(productName)", fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "Arial", maxLines: 1, maxWidth: leftTextWidth)
    let innerBitmap = textToBitmap("INNER", fontSize: fontSizeToPixels(8), isBold: true, fontFamily: "sans-serif-black")
    let innerQuantityBitmap = textToBitmap(String(middlePackageQuantity), fontSize: fontSizeToPixels(16), isBold: true, fontFamily: "sans-serif-black")
    let dateBitmap = textToBitmap(todayString(), fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black", isInverse: true, padding: 2)
    func centerX(_ bitmap: PrinterBitmap) -> Int { max(0, (width - bitmap.width) / 2) }
    let rightPadding = 20
    let quantityX = width - rightPadding - innerQuantityBitmap.width
    let innerX = max(rightAreaLeft, quantityX - innerBitmap.width - 8)
    let quantityY = 72
    let innerY = quantityY + max(0, innerQuantityBitmap.height - innerBitmap.height - 3)
    let dateX = width - rightPadding - dateBitmap.width
    let dateY = height - dateBitmap.height - 10

    var commands = [
      "! 0 200 200 \(height) 1",
      "PAGE-WIDTH \(width)",
      bitmapCommand(centerX(titleBitmap), 8, titleBitmap),
      bitmapCommand(centerX(codeBitmap), 28, codeBitmap),
      bitmapCommand(18, 78, itemBitmap),
      bitmapCommand(18, 108, nameBitmap),
      bitmapCommand(innerX, innerY, innerBitmap),
      bitmapCommand(quantityX, quantityY, innerQuantityBitmap),
      bitmapCommand(dateX, dateY, dateBitmap),
    ]

    if !barcode.isEmpty {
      commands.append("BARCODE 128 1 1 44 24 150 \(cpclText(barcode))")
    }

    commands.append("PRINT")
    return commands.joined(separator: "\r\n") + "\r\n"
  }

  private func dictString(_ payload: NSDictionary, _ key: String) -> String {
    guard let value = payload[key], !(value is NSNull) else {
      return ""
    }

    if let text = value as? String {
      return cpclText(text)
    }

    return cpclText(String(describing: value))
  }

  private func dictDouble(_ payload: NSDictionary, _ key: String) -> Double? {
    guard let value = payload[key], !(value is NSNull) else {
      return nil
    }

    if let number = value as? NSNumber {
      return number.doubleValue
    }

    if let text = value as? String {
      return Double(text.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    return nil
  }

  private func cpclText(_ value: String, maxLength: Int = 80) -> String {
    let normalized = value
      .replacingOccurrences(of: "[\\r\\n]+", with: " ", options: .regularExpression)
      .trimmingCharacters(in: .whitespacesAndNewlines)
    return String(normalized.prefix(maxLength))
  }

  private func fontSizeToPixels(_ fontSize: CGFloat) -> CGFloat {
    fontSize * 3
  }

  private func formatPriceParts(_ value: Double?) -> PriceParts {
    let cents = Int(((value ?? 0) * 100).rounded())
    return PriceParts(integer: String(cents / 100), decimal: String(format: "%02d", abs(cents % 100)))
  }

  private func formatMoney(_ value: Double) -> String {
    String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), value)
  }

  private func formatOptionalMoney(_ value: Double?) -> String {
    guard let value else {
      return "--"
    }
    return formatMoney(value)
  }

  private func formatOptionalQuantity(_ value: Double?) -> String {
    guard let value else {
      return "--"
    }
    let rounded = Int(value.rounded())
    return abs(value - Double(rounded)) < 0.01 ? String(rounded) : String(format: "%.2f", locale: Locale(identifier: "en_US_POSIX"), value)
  }

  private func todayString() -> String {
    let formatter = DateFormatter()
    formatter.locale = Locale(identifier: "en_US_POSIX")
    formatter.dateFormat = "yyyy/MM/dd"
    return formatter.string(from: Date())
  }

  private func discountLabel(_ discountRate: Double) -> String {
    "\(String(format: "%02d", Int((discountRate * 100).rounded())))%OFF"
  }

  private func formatSupplierAbbreviation(_ value: String) -> String {
    let locale = Locale(identifier: "en_US")
    let words = cpclText(value)
      .lowercased(with: locale)
      .split(whereSeparator: { $0.isWhitespace })
      .map { word -> String in
        let text = String(word)
        return String(text.prefix(1)).uppercased(with: locale) + String(text.dropFirst())
      }

    if words.isEmpty {
      return ""
    }

    if words.count == 1 {
      return String(words[0].prefix(3)).uppercased(with: locale)
    }

    return words
      .prefix(4)
      .map { String($0.prefix(1)).uppercased(with: locale) }
      .joined(separator: ".")
  }

  private func formatGrade(_ value: String) -> String {
    String(cpclText(value).uppercased(with: Locale(identifier: "en_US")).prefix(1))
  }

  private func isValidEan13(_ value: String) -> Bool {
    let barcode = cpclText(value, maxLength: 64)
    guard barcode.range(of: #"^\d{13}$"#, options: .regularExpression) != nil else {
      return false
    }

    let digits = barcode.compactMap { $0.wholeNumberValue }
    let checkDigit = digits.prefix(12).enumerated().reduce(0) { sum, item in
      sum + item.element * (item.offset.isMultiple(of: 2) ? 1 : 3)
    }
    return (10 - (checkDigit % 10)) % 10 == digits[12]
  }

  private func printerFont(fontFamily: String, fontSize: CGFloat, isBold: Bool) -> UIFont {
    if fontFamily == "Arial" {
      let fontName = isBold ? "Arial-BoldMT" : "Arial"
      return UIFont(name: fontName, size: fontSize) ?? UIFont.systemFont(ofSize: fontSize, weight: isBold ? .bold : .regular)
    }

    if fontFamily.contains("black") {
      return UIFont.systemFont(ofSize: fontSize, weight: .black)
    }

    if fontFamily.contains("light") {
      return UIFont.systemFont(ofSize: fontSize, weight: isBold ? .bold : .light)
    }

    return UIFont.systemFont(ofSize: fontSize, weight: isBold ? .bold : .regular)
  }

  private func textToBitmap(
    _ text: String,
    fontSize: CGFloat,
    isBold: Bool,
    fontFamily: String,
    isInverse: Bool = false,
    padding: Int = 0
  ) -> PrinterBitmap {
    let safeText = cpclText(text).isEmpty ? " " : cpclText(text)
    let font = printerFont(fontFamily: fontFamily, fontSize: fontSize, isBold: isBold)
    let attributes: [NSAttributedString.Key: Any] = [
      .font: font,
      .foregroundColor: isInverse ? UIColor.white : UIColor.black,
    ]
    let measuredSize = (safeText as NSString).size(withAttributes: attributes)
    let width = max(1, Int(ceil(measuredSize.width)) + padding * 2)
    let height = max(1, Int(ceil(measuredSize.height)) + padding * 2)

    return renderPrinterBitmap(width: width, height: height, isInverse: isInverse) {
      (safeText as NSString).draw(
        at: CGPoint(x: CGFloat(padding), y: CGFloat(padding)),
        withAttributes: attributes
      )
    }
  }

  private func longTextToBitmap(
    _ text: String,
    fontSize: CGFloat,
    isBold: Bool,
    fontFamily: String,
    maxLines: Int,
    maxWidth: Int
  ) -> PrinterBitmap {
    let safeText = cpclText(text).isEmpty ? " " : cpclText(text)
    let font = printerFont(fontFamily: fontFamily, fontSize: fontSize, isBold: isBold)
    let attributes: [NSAttributedString.Key: Any] = [
      .font: font,
      .foregroundColor: UIColor.black,
    ]
    let lines = wrapText(safeText, font: font, maxWidth: maxWidth, maxLines: maxLines)
    let lineHeight = ceil(font.lineHeight)
    let measuredWidth = lines.map { measureText($0, font: font) }.max() ?? 1
    let width = max(1, min(maxWidth, Int(ceil(measuredWidth))))
    let height = max(1, Int(lineHeight) * lines.count)

    return renderPrinterBitmap(width: width, height: height, isInverse: false) {
      lines.enumerated().forEach { index, line in
        (line as NSString).draw(
          at: CGPoint(x: 0, y: CGFloat(index) * lineHeight),
          withAttributes: attributes
        )
      }
    }
  }

  private func wrapText(_ text: String, font: UIFont, maxWidth: Int, maxLines: Int) -> [String] {
    var lines: [String] = []
    var current = ""

    for character in text {
      let next = current + String(character)
      if !current.isEmpty && measureText(next, font: font) > CGFloat(maxWidth) && lines.count < maxLines - 1 {
        lines.append(current)
        current = String(character)
      } else {
        current = next
      }
    }

    if !current.isEmpty || lines.isEmpty {
      lines.append(current)
    }

    return Array(lines.prefix(maxLines))
  }

  private func measureText(_ text: String, font: UIFont) -> CGFloat {
    (text as NSString).size(withAttributes: [.font: font]).width
  }

  private func renderPrinterBitmap(width: Int, height: Int, isInverse: Bool, draw: () -> Void) -> PrinterBitmap {
    let size = CGSize(width: width, height: height)
    let format = UIGraphicsImageRendererFormat()
    format.scale = 1
    format.opaque = true
    let renderer = UIGraphicsImageRenderer(size: size, format: format)
    let image = renderer.image { _ in
      (isInverse ? UIColor.black : UIColor.white).setFill()
      UIRectFill(CGRect(origin: .zero, size: size))
      draw()
    }
    return convertImageToPrinterBitmap(image, width: width, height: height)
  }

  private func convertImageToPrinterBitmap(_ image: UIImage, width: Int, height: Int) -> PrinterBitmap {
    guard let cgImage = image.cgImage else {
      return PrinterBitmap(width: width, height: height, hex: "", inkMinY: 0, inkMaxY: max(0, height - 1))
    }

    var rgba = [UInt8](repeating: 255, count: width * height * 4)
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    rgba.withUnsafeMutableBytes { buffer in
      guard let context = CGContext(
        data: buffer.baseAddress,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: width * 4,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      ) else {
        return
      }
      context.draw(cgImage, in: CGRect(x: 0, y: 0, width: width, height: height))
    }

    // CPCL EG 使用 1bit 单色图，阈值保持和 Android isBlack 一致。
    let inkBounds = bitmapInkVerticalBounds(rgba, width: width, height: height)
    return PrinterBitmap(width: width, height: height, hex: bitmapToHex(rgba, width: width, height: height), inkMinY: inkBounds.minY, inkMaxY: inkBounds.maxY)
  }

  private func bitmapCommand(_ x: Int, _ y: Int, _ bitmap: PrinterBitmap) -> String {
    "EG \(bitmap.widthBytes) \(bitmap.height) \(x) \(y) \(bitmap.hex)"
  }

  private func createQrCodeBitmap(_ value: String, size: Int) -> PrinterBitmap {
    guard
      let data = value.data(using: .utf8),
      let filter = CIFilter(name: "CIQRCodeGenerator")
    else {
      return textToBitmap(value, fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    }

    filter.setValue(data, forKey: "inputMessage")
    filter.setValue("L", forKey: "inputCorrectionLevel")

    guard let outputImage = filter.outputImage else {
      return textToBitmap(value, fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    }

    let context = CIContext(options: nil)
    guard let cgImage = context.createCGImage(outputImage, from: outputImage.extent) else {
      return textToBitmap(value, fontSize: fontSizeToPixels(7), isBold: true, fontFamily: "sans-serif-black")
    }

    let targetSize = max(1, size)
    // CoreImage 的 QR 默认带 quiet zone；裁到黑色边界后再缩放，贴近 Android MARGIN=0。
    let qrImage = UIImage(cgImage: cropQrQuietZone(cgImage) ?? cgImage)
    return renderPrinterBitmap(width: targetSize, height: targetSize, isInverse: false) {
      qrImage.draw(in: CGRect(x: 0, y: 0, width: targetSize, height: targetSize))
    }
  }

  private func cropQrQuietZone(_ image: CGImage) -> CGImage? {
    let width = image.width
    let height = image.height
    guard width > 0, height > 0 else {
      return nil
    }

    var rgba = [UInt8](repeating: 255, count: width * height * 4)
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    rgba.withUnsafeMutableBytes { buffer in
      guard let context = CGContext(
        data: buffer.baseAddress,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: width * 4,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
      ) else {
        return
      }
      context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
    }

    var minX = width
    var minY = height
    var maxX = -1
    var maxY = -1

    for y in 0..<height {
      for x in 0..<width {
        let offset = (y * width + x) * 4
        if isBlackPixel(rgba, offset: offset) {
          minX = min(minX, x)
          minY = min(minY, y)
          maxX = max(maxX, x)
          maxY = max(maxY, y)
        }
      }
    }

    guard maxX >= minX, maxY >= minY else {
      return nil
    }

    return image.cropping(to: CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1))
  }

  private func createDashLineBitmap(width: Int, height: Int) -> PrinterBitmap {
    let safeWidth = max(1, width)
    let safeHeight = max(1, height)
    return renderPrinterBitmap(width: safeWidth, height: safeHeight, isInverse: false) {
      // 大折扣标签底部虚线按 Android 的 10px 实线、18px 步进绘制。
      let path = UIBezierPath()
      var x: CGFloat = 0
      while x < CGFloat(safeWidth) {
        path.move(to: CGPoint(x: x, y: CGFloat(safeHeight) / 2))
        path.addLine(to: CGPoint(x: min(x + 10, CGFloat(safeWidth)), y: CGFloat(safeHeight) / 2))
        x += 18
      }
      UIColor.black.setStroke()
      path.lineWidth = CGFloat(safeHeight)
      path.stroke()
    }
  }

  private func bitmapToHex(_ rgba: [UInt8], width: Int, height: Int) -> String {
    let widthBytes = (width + 7) / 8
    var hex = ""
    hex.reserveCapacity(widthBytes * height * 2)

    for y in 0..<height {
      for byteIndex in 0..<widthBytes {
        var value: UInt8 = 0
        for bit in 0..<8 {
          let x = byteIndex * 8 + bit
          let offset = (y * width + x) * 4
          if x < width && isBlackPixel(rgba, offset: offset) {
            value |= UInt8(1 << (7 - bit))
          }
        }
        hex += String(format: "%02X", value)
      }
    }

    return hex
  }

  private func bitmapInkVerticalBounds(_ rgba: [UInt8], width: Int, height: Int) -> (minY: Int, maxY: Int) {
    var minY = height
    var maxY = -1

    for y in 0..<height {
      for x in 0..<width {
        let offset = (y * width + x) * 4
        if isBlackPixel(rgba, offset: offset) {
          minY = min(minY, y)
          maxY = max(maxY, y)
        }
      }
    }

    // 空白图回退到整个位图，避免调用方对齐时拿到无效边界。
    if maxY < minY {
      return (0, max(0, height - 1))
    }

    return (minY, maxY)
  }

  private func isBlackPixel(_ rgba: [UInt8], offset: Int) -> Bool {
    guard offset + 3 < rgba.count else {
      return false
    }

    let alpha = Int(rgba[offset + 3])
    if alpha == 0 {
      return false
    }

    let luminance = (Int(rgba[offset]) * 299 + Int(rgba[offset + 1]) * 587 + Int(rgba[offset + 2]) * 114) / 1000
    return luminance < 200
  }

  private func encodeCommand(_ command: String, encoding: String?) throws -> Data {
    let normalizedEncoding = encoding?.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
    if normalizedEncoding == nil || normalizedEncoding == "UTF-8" || normalizedEncoding == "UTF8" {
      return Data(command.utf8)
    }

    if normalizedEncoding == "GB18030" || normalizedEncoding == "GBK" || normalizedEncoding == "GB2312" {
      let cfEncoding = CFStringConvertIANACharSetNameToEncoding("GB18030" as CFString)
      if cfEncoding != kCFStringEncodingInvalidId {
        let nsEncoding = CFStringConvertEncodingToNSStringEncoding(cfEncoding)
        let stringEncoding = String.Encoding(rawValue: nsEncoding)
        if let data = command.data(using: stringEncoding) {
          return data
        }
      }
    }

    throw NSError(
      domain: "HbPrinterModule",
      code: 1,
      userInfo: [NSLocalizedDescriptionKey: "Unsupported printer command encoding: \(encoding ?? "")"]
    )
  }

  private func rejectUnsupportedLabelPrint(_ reject: @escaping RCTPromiseRejectBlock) {
    // iOS 先只支持原始命令写入；Android 位图标签渲染未复刻时必须明确失败。
    reject("IOS_LABEL_PRINT_UNSUPPORTED", "iOS label bitmap printing is not supported yet.", nil)
  }

  private func resolve(_ resolve: RCTPromiseResolveBlock?, _ value: Any?) {
    DispatchQueue.main.async {
      resolve?(value)
    }
  }

  private func reject(_ reject: RCTPromiseRejectBlock?, _ code: String, _ message: String) {
    DispatchQueue.main.async {
      reject?(code, message, nil)
    }
  }
}
