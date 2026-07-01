const fs = require("fs");
const path = require("path");
const {
  IOSConfig,
  createRunOncePlugin,
  withDangerousMod,
  withInfoPlist,
  withXcodeProject,
} = require("@expo/config-plugins");

const MODULE_SWIFT = `import CoreBluetooth
import Foundation

@objc(HbPrinterModule)
class HbPrinterModule: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
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
      guard let peripheral = self.connectedPeripheral,
            peripheral.state == .connected,
            let characteristic = self.writeCharacteristic else {
        self.reject(reject, "PRINT_ERROR", "No Bluetooth printer is connected.")
        return
      }

      guard self.printResolve == nil else {
        self.reject(reject, "PRINT_IN_PROGRESS", "Bluetooth printer is already printing.")
        return
      }

      do {
        let data = try self.encodeCommand(command, encoding: encoding)
        if data.isEmpty {
          self.resolve(resolve, true)
          return
        }

        let writeType: CBCharacteristicWriteType = characteristic.properties.contains(.write) ? .withResponse : .withoutResponse
        let chunkSize = max(20, peripheral.maximumWriteValueLength(for: writeType))
        self.pendingWriteChunks = stride(from: 0, to: data.count, by: chunkSize).map { offset in
          data.subdata(in: offset..<min(offset + chunkSize, data.count))
        }
        self.pendingWriteType = writeType
        self.pendingWriteCharacteristic = characteristic
        self.printResolve = resolve
        self.printReject = reject

        // BLE 写入有 MTU 和发送窗口上限；必须等系统回调后再继续，避免假成功。
        let workItem = DispatchWorkItem { [weak self] in
          guard let self else {
            return
          }
          self.failPendingPrint("PRINT_TIMEOUT", "Bluetooth printer write timed out.")
        }
        self.printTimeoutWorkItem = workItem
        self.bluetoothQueue.asyncAfter(deadline: .now() + .seconds(15), execute: workItem)
        self.flushPendingWrites()
      } catch {
        self.reject(reject, "PRINT_ERROR", error.localizedDescription)
      }
    }
  }

  @objc(printProductLabel:printType:resolver:rejecter:)
  func printProductLabel(
    _ payload: NSDictionary,
    printType: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    rejectUnsupportedLabelPrint(reject)
  }

  @objc(printDiscountLabel:printType:resolver:rejecter:)
  func printDiscountLabel(
    _ payload: NSDictionary,
    printType: String?,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    rejectUnsupportedLabelPrint(reject)
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
    rejectUnsupportedLabelPrint(reject)
  }

  @objc(printWarehouseProductLabel:resolver:rejecter:)
  func printWarehouseProductLabel(
    _ payload: NSDictionary,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    rejectUnsupportedLabelPrint(reject)
  }

  @objc(printWarehouseLocationLabel:resolver:rejecter:)
  func printWarehouseLocationLabel(
    _ payload: NSDictionary,
    resolver resolve: @escaping RCTPromiseResolveBlock,
    rejecter reject: @escaping RCTPromiseRejectBlock
  ) {
    rejectUnsupportedLabelPrint(reject)
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
      userInfo: [NSLocalizedDescriptionKey: "Unsupported printer command encoding: \\(encoding ?? "")"]
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
`;

const MODULE_EXPORTS = `#import <React/RCTBridgeModule.h>

@interface RCT_EXTERN_MODULE(HbPrinterModule, NSObject)

RCT_EXTERN_METHOD(getStatus:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(scanPrinters:(nonnull NSNumber *)durationMs
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(connect:(NSString *)address
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(disconnect:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(print:(NSString *)command
                  encoding:(NSString *)encoding
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printProductLabel:(NSDictionary *)payload
                  printType:(NSString *)printType
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printDiscountLabel:(NSDictionary *)payload
                  printType:(NSString *)printType
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printClearanceLabel:(NSDictionary *)payload
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printBigDiscountLabel:(NSDictionary *)payload
                  printType:(NSString *)printType
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printWarehouseProductLabel:(NSDictionary *)payload
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

RCT_EXTERN_METHOD(printWarehouseLocationLabel:(NSDictionary *)payload
                  resolver:(RCTPromiseResolveBlock)resolve
                  rejecter:(RCTPromiseRejectBlock)reject)

+ (BOOL)requiresMainQueueSetup
{
  return NO;
}

@end
`;

const BLUETOOTH_USAGE = "Used to scan and connect Bluetooth receipt and label printers";
const BRIDGE_IMPORT = "#import <React/RCTBridgeModule.h>";

function addSourceFile(project, projectName, fileName) {
  const relativePath = `${projectName}/${fileName}`;
  if (project.hasFile(relativePath)) {
    return;
  }

  IOSConfig.XcodeUtils.addBuildSourceFileToGroup({
    project,
    groupName: projectName,
    filepath: relativePath,
  });
}

function getIosProjectName(config) {
  // EAS clean prebuild 的 AppDelegate 尚未生成，需用 Expo 配置里的 name 兜底。
  return IOSConfig.XcodeUtils.getHackyProjectName(
    config.modRequest.platformProjectRoot,
    config
  );
}

function withIosPrinterModule(config) {
  config = withInfoPlist(config, (config) => {
    config.modResults.NSBluetoothAlwaysUsageDescription = BLUETOOTH_USAGE;
    config.modResults.NSBluetoothPeripheralUsageDescription = BLUETOOTH_USAGE;
    return config;
  });

  config = withDangerousMod(config, [
    "ios",
    async (config) => {
      const projectRoot = config.modRequest.platformProjectRoot;
      const projectName = getIosProjectName(config);
      const appSourceRoot = path.join(projectRoot, projectName);
      const bridgingHeaderPath = path.join(appSourceRoot, `${projectName}-Bridging-Header.h`);
      fs.mkdirSync(appSourceRoot, { recursive: true });

      // iOS 原生目录是生成物，插件在 prebuild/EAS 时重建 BLE 打印桥源码。
      fs.writeFileSync(path.join(appSourceRoot, "HbPrinterModule.swift"), MODULE_SWIFT);
      fs.writeFileSync(path.join(appSourceRoot, "HbPrinterModule.m"), MODULE_EXPORTS);
      const bridgingHeader = fs.existsSync(bridgingHeaderPath)
        ? fs.readFileSync(bridgingHeaderPath, "utf8")
        : "";
      if (!bridgingHeader.includes(BRIDGE_IMPORT)) {
        fs.writeFileSync(
          bridgingHeaderPath,
          `${bridgingHeader.trimEnd()}\n${BRIDGE_IMPORT}\n`
        );
      }
      return config;
    },
  ]);

  return withXcodeProject(config, (config) => {
    const projectName = getIosProjectName(config);
    addSourceFile(config.modResults, projectName, "HbPrinterModule.swift");
    addSourceFile(config.modResults, projectName, "HbPrinterModule.m");
    return config;
  });
}

module.exports = createRunOncePlugin(withIosPrinterModule, "with-ios-printer-module", "1.0.0");
