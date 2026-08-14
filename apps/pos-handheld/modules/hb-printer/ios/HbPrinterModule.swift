import CoreBluetooth
import ExpoModulesCore
import Foundation

private final class PrinterException: Exception {
  private let printerCode: String
  private let printerReason: String

  init(_ code: String, _ reason: String) {
    printerCode = code
    printerReason = reason
    super.init(name: "HbPrinterException", description: reason, code: code)
  }

  override var code: String { printerCode }
  override var reason: String { printerReason }
}

private enum PrinterOperationKind: String {
  case print
  case drawer
}

private struct PendingOperation {
  let id: String
  let kind: PrinterOperationKind
  let promise: Promise
  let timeout: DispatchWorkItem
  // CoreBluetooth 的 ACK 不带 operationId；必须把操作绑定到建立它的连接与写入 characteristic。
  let connectionEpoch: UInt64
  let peripheral: CBPeripheral
  let characteristic: CBCharacteristic
}

private struct PendingReconnect {
  let peripheral: CBPeripheral
  let peripheralId: String
  let timeoutMs: Int
  let promise: Promise
  let timeout: DispatchWorkItem
}

/**
 * Expo Module 不是 NSObject，不能直接实现 CoreBluetooth 的 NSObjectProtocol delegate。
 * 把 Apple delegate 放在此 helper，Module 仅持有状态机和 JS bridge，避免原生目标无法编译。
 */
private final class PrinterBluetoothDelegate: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
  weak var owner: HbPrinterModule?
  private let queue: DispatchQueue
  private(set) var centralManager: CBCentralManager?

  init(queue: DispatchQueue) {
    self.queue = queue
    super.init()
  }

  func start() {
    guard centralManager == nil else { return }
    centralManager = CBCentralManager(delegate: self, queue: queue)
  }

  func centralManagerDidUpdateState(_ central: CBCentralManager) {
    owner?.handleCentralManagerStateChange(central)
  }

  func centralManager(
    _ central: CBCentralManager,
    didDiscover peripheral: CBPeripheral,
    advertisementData: [String: Any],
    rssi RSSI: NSNumber
  ) {
    owner?.handlePeripheralDiscovery(peripheral, advertisementData: advertisementData, rssi: RSSI)
  }

  func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
    owner?.handlePeripheralConnected(peripheral)
  }

  func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
    owner?.handlePeripheralConnectFailure(peripheral, error: error)
  }

  func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
    owner?.handlePeripheralDisconnect(peripheral, error: error)
  }

  func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
    owner?.handleServiceDiscovery(peripheral, error: error)
  }

  func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
    owner?.handleCharacteristicDiscovery(peripheral, service: service, error: error)
  }

  func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
    owner?.handleWriteCompletion(peripheral, characteristic: characteristic, error: error)
  }

  func peripheralIsReady(toSendWriteWithoutResponse peripheral: CBPeripheral) {
    owner?.handleWriteWithoutResponseReady(peripheral)
  }
}

public final class HbPrinterModule: Module {
  private let bluetoothQueue = DispatchQueue(label: "com.hbweb.poshandheld.printer.ble")
  private lazy var bluetoothDelegate = PrinterBluetoothDelegate(queue: bluetoothQueue)
  private var discoveredPeripherals: [String: CBPeripheral] = [:]
  private var discoveredDevices: [String: [String: Any]] = [:]
  private var connectedPeripheral: CBPeripheral?
  private var writeCharacteristic: CBCharacteristic?
  private var writeType: CBCharacteristicWriteType?
  private var connectedPeripheralId: String?
  private var connectionState: String = "disconnected"
  private var scanPromise: Promise?
  private var scanTimeout: DispatchWorkItem?
  private var connectPromise: Promise?
  private var connectTimeout: DispatchWorkItem?
  // 每次断开或显式建立连接都推进 epoch，过期回调不能再影响当前操作。
  private var connectionEpoch: UInt64 = 0
  private var disconnectingPeripheral: CBPeripheral?
  private var pendingReconnect: PendingReconnect?
  private var servicesAwaitingCharacteristics = 0
  private var pendingOperation: PendingOperation?
  private var pendingChunks: [Data] = []
  private let maxRememberedOperationIds = 512
  // 此状态只在 bluetoothQueue 串行访问：既防止同一 ID 重放，又将常驻内存限制为 512 条。
  private var usedOperationIds = Set<String>()
  private var usedOperationIdOrder: [String] = []

  public func definition() -> ModuleDefinition {
    Name("HbPrinter")
    Events("printerStatus", "printerOperation", "printerError")

    OnCreate {
      self.bluetoothQueue.async {
        self.bluetoothDelegate.owner = self
        self.bluetoothDelegate.start()
      }
    }

    OnDestroy {
      self.bluetoothQueue.async {
        self.cancelScan(
          code: "PRINTER_MODULE_DESTROYED",
          message: "打印模块已卸载，扫描未完成。"
        )
        self.disconnectInternal(
          reason: "module-destroyed",
          connectFailureCode: "PRINTER_MODULE_DESTROYED",
          connectFailureMessage: "打印模块已卸载，连接未完成。",
          operationMessage: "打印模块已卸载，无法确认打印或开箱结果。"
        )
        self.bluetoothDelegate.owner = nil
      }
    }

    AsyncFunction("getStatus") { () -> [String: Any] in
      self.bluetoothQueue.sync { self.statusPayload() }
    }

    AsyncFunction("scan") { (durationMs: Int, includeAll: Bool, promise: Promise) in
      self.bluetoothQueue.async {
        guard self.ensureBluetoothReady(promise) else { return }
        guard self.scanPromise == nil else {
          promise.reject(PrinterException("PRINTER_SCAN_IN_PROGRESS", "蓝牙打印机扫描正在进行。"))
          return
        }

        self.discoveredPeripherals.removeAll()
        self.discoveredDevices.removeAll()
        self.scanPromise = promise
        self.bluetoothDelegate.centralManager?.scanForPeripherals(withServices: nil, options: [
          CBCentralManagerScanOptionAllowDuplicatesKey: false,
        ])
        let boundedDuration = min(max(durationMs, 1_500), 30_000)
        let timeout = DispatchWorkItem { [weak self] in
          self?.finishScan(includeAll: includeAll)
        }
        self.scanTimeout = timeout
        self.bluetoothQueue.asyncAfter(deadline: .now() + .milliseconds(boundedDuration), execute: timeout)
      }
    }

    AsyncFunction("connect") { (peripheralId: String, timeoutMs: Int, promise: Promise) in
      self.bluetoothQueue.async {
        guard self.ensureBluetoothReady(promise) else { return }
        guard self.connectPromise == nil, self.pendingReconnect == nil else {
          promise.reject(PrinterException("PRINTER_CONNECT_IN_PROGRESS", "蓝牙打印机连接正在进行。"))
          return
        }
        guard let identifier = UUID(uuidString: peripheralId) else {
          promise.reject(PrinterException("PRINTER_INVALID_ID", "iOS 打印机标识必须是外设 UUID。"))
          return
        }
        guard let peripheral = self.discoveredPeripherals[peripheralId]
          ?? self.bluetoothDelegate.centralManager?.retrievePeripherals(withIdentifiers: [identifier]).first else {
          promise.reject(PrinterException("PRINTER_NOT_FOUND", "找不到打印机；请先重新扫描。"))
          return
        }

        self.disconnectInternal(reason: "switch-printer")
        let boundedTimeout = min(max(timeoutMs, 3_000), 30_000)
        if self.disconnectingPeripheral != nil {
          let timeout = DispatchWorkItem { [weak self] in
            self?.failPendingReconnect(
              code: "PRINTER_CONNECT_TIMEOUT",
              message: "等待旧蓝牙连接断开超时。"
            )
          }
          // 旧连接的 didDisconnect 到达前绝不复用同一 CBPeripheral，避免迟到 ACK 被归因到新连接。
          self.pendingReconnect = PendingReconnect(
            peripheral: peripheral,
            peripheralId: peripheralId,
            timeoutMs: boundedTimeout,
            promise: promise,
            timeout: timeout
          )
          self.connectionState = "connecting"
          self.emitStatus(reason: "waiting-for-disconnect")
          self.bluetoothQueue.asyncAfter(deadline: .now() + .milliseconds(boundedTimeout), execute: timeout)
          return
        }
        self.beginConnect(
          peripheral: peripheral,
          peripheralId: peripheralId,
          timeoutMs: boundedTimeout,
          promise: promise
        )
      }
    }

    AsyncFunction("disconnect") { () -> [String: Any] in
      self.bluetoothQueue.sync {
        self.disconnectInternal(reason: "requested")
        return self.statusPayload()
      }
    }

    AsyncFunction("write") { (operationId: String, bytes: [UInt8], timeoutMs: Int, kind: String, promise: Promise) in
      self.bluetoothQueue.async {
        guard let operationKind = PrinterOperationKind(rawValue: kind) else {
          promise.reject(PrinterException("PRINTER_INVALID_OPERATION", "未知打印机操作类型。"))
          return
        }
        self.startWrite(
          operationId: operationId,
          data: Data(bytes),
          timeoutMs: timeoutMs,
          kind: operationKind,
          promise: promise
        )
      }
    }

    AsyncFunction("printText") { (
      operationId: String,
      text: String,
      encoding: String,
      appendLineFeed: Bool,
      cutAfterPrint: Bool,
      timeoutMs: Int,
      promise: Promise
    ) in
      self.bluetoothQueue.async {
        do {
          var data = try self.encode(text: text, encoding: encoding)
          if appendLineFeed { data.append(0x0A) }
          if cutAfterPrint { data.append(contentsOf: [0x1D, 0x56, 0x00]) }
          self.startWrite(
            operationId: operationId,
            data: data,
            timeoutMs: timeoutMs,
            kind: .print,
            promise: promise
          )
        } catch let error as PrinterException {
          promise.reject(error)
        } catch {
          promise.reject(PrinterException("PRINTER_ENCODING_FAILED", error.localizedDescription))
        }
      }
    }

    AsyncFunction("openCashDrawer") { (
      operationId: String,
      pin: Int,
      onTime: Int,
      offTime: Int,
      timeoutMs: Int,
      promise: Promise
    ) in
      self.bluetoothQueue.async {
        guard pin == 0 || pin == 1 else {
          promise.reject(PrinterException("PRINTER_DRAWER_PIN_INVALID", "钱箱针脚只能是 0 或 1。"))
          return
        }
        let boundedOnTime = UInt8(min(max(onTime, 1), 255))
        let boundedOffTime = UInt8(min(max(offTime, 1), 255))
        // ESC/POS 钱箱脉冲由打印机 RJ11 口执行；命令无法证明物理开箱，超时一律标记为 unknown。
        self.startWrite(
          operationId: operationId,
          data: Data([0x1B, 0x70, UInt8(pin), boundedOnTime, boundedOffTime]),
          timeoutMs: timeoutMs,
          kind: .drawer,
          promise: promise
        )
      }
    }
  }

  private func beginConnect(
    peripheral: CBPeripheral,
    peripheralId: String,
    timeoutMs: Int,
    promise: Promise
  ) {
    connectionEpoch &+= 1
    connectPromise = promise
    connectionState = "connecting"
    connectedPeripheral = peripheral
    connectedPeripheralId = peripheralId
    peripheral.delegate = bluetoothDelegate
    emitStatus(reason: "connecting")
    bluetoothDelegate.centralManager?.connect(peripheral, options: nil)

        let timeout = DispatchWorkItem { [weak self] in
          self?.failConnect(code: "PRINTER_CONNECT_TIMEOUT", message: "连接蓝牙打印机超时。")
        }
    connectTimeout = timeout
    bluetoothQueue.asyncAfter(deadline: .now() + .milliseconds(timeoutMs), execute: timeout)
  }

  fileprivate func handleCentralManagerStateChange(_ central: CBCentralManager) {
    bluetoothQueue.async {
      if central.state != .poweredOn {
        let failure = self.bluetoothReadinessFailure(central)
        self.emitStatus(reason: "bluetooth-state-changed")
        self.failScan(code: failure.code, message: failure.message)
        self.failConnect(code: failure.code, message: failure.message)
        self.finishPendingOperation(state: "unknown", message: "蓝牙状态改变，无法确认操作结果。")
      }
    }
  }

  fileprivate func handlePeripheralDiscovery(
    _ peripheral: CBPeripheral,
    advertisementData: [String: Any],
    rssi RSSI: NSNumber
  ) {
    bluetoothQueue.async {
      let id = peripheral.identifier.uuidString
      let advertisementName = advertisementData[CBAdvertisementDataLocalNameKey] as? String
      let peripheralName = peripheral.name
      let candidateNames = [advertisementName, peripheralName]
        .compactMap { $0?.trimmingCharacters(in: .whitespacesAndNewlines) }
        .filter { !$0.isEmpty }
      let name = candidateNames.first ?? "Bluetooth Printer"
      self.discoveredPeripherals[id] = peripheral
      self.discoveredDevices[id] = [
        "id": id,
        "name": name,
        "rssi": RSSI,
        "isXprinter": candidateNames.contains(where: self.isXprinter),
      ]
    }
  }

  fileprivate func handlePeripheralConnected(_ peripheral: CBPeripheral) {
    bluetoothQueue.async {
      guard peripheral === self.connectedPeripheral else { return }
      peripheral.discoverServices(nil)
    }
  }

  fileprivate func handlePeripheralConnectFailure(_ peripheral: CBPeripheral, error: Error?) {
    bluetoothQueue.async {
      guard peripheral === self.connectedPeripheral else { return }
      self.failConnect(code: "PRINTER_CONNECT_FAILED", message: error?.localizedDescription ?? "无法连接蓝牙打印机。")
    }
  }

  fileprivate func handlePeripheralDisconnect(_ peripheral: CBPeripheral, error: Error?) {
    bluetoothQueue.async {
      if peripheral === self.disconnectingPeripheral {
        self.disconnectingPeripheral = nil
        self.startPendingReconnect()
        return
      }
      guard peripheral === self.connectedPeripheral else { return }
      self.disconnectInternal(
        reason: "disconnected",
        connectFailureCode: "PRINTER_CONNECT_INTERRUPTED",
        connectFailureMessage: error?.localizedDescription ?? "蓝牙打印机在连接完成前断开。",
        operationMessage: error?.localizedDescription ?? "打印机在操作期间断开，无法确认结果。",
        cancelTransport: false
      )
    }
  }

  fileprivate func handleServiceDiscovery(_ peripheral: CBPeripheral, error: Error?) {
    bluetoothQueue.async {
      guard peripheral === self.connectedPeripheral else { return }
      if let error {
        self.failConnect(code: "PRINTER_SERVICE_DISCOVERY_FAILED", message: error.localizedDescription)
        return
      }
      let services = peripheral.services ?? []
      guard !services.isEmpty else {
        self.failConnect(code: "PRINTER_NO_WRITABLE_SERVICE", message: "打印机没有公开可写 BLE 服务。")
        return
      }
      self.servicesAwaitingCharacteristics = services.count
      services.forEach { peripheral.discoverCharacteristics(nil, for: $0) }
    }
  }

  fileprivate func handleCharacteristicDiscovery(_ peripheral: CBPeripheral, service: CBService, error: Error?) {
    bluetoothQueue.async {
      guard peripheral === self.connectedPeripheral else { return }
      defer {
        self.servicesAwaitingCharacteristics = max(0, self.servicesAwaitingCharacteristics - 1)
        if self.writeCharacteristic == nil && self.servicesAwaitingCharacteristics == 0 {
          self.failConnect(code: "PRINTER_NO_WRITABLE_CHARACTERISTIC", message: "打印机没有公开可写 BLE characteristic。")
        }
      }
      if let error {
        self.failConnect(code: "PRINTER_CHARACTERISTIC_DISCOVERY_FAILED", message: error.localizedDescription)
        return
      }
      guard let characteristic = service.characteristics?.first(where: {
        $0.properties.contains(.write) || $0.properties.contains(.writeWithoutResponse)
      }) else { return }

      self.writeCharacteristic = characteristic
      self.writeType = characteristic.properties.contains(.write) ? .withResponse : .withoutResponse
      self.connectionState = "ready"
      self.connectTimeout?.cancel()
      self.connectTimeout = nil
      let promise = self.connectPromise
      self.connectPromise = nil
      self.emitStatus(reason: "ready")
      promise?.resolve(self.statusPayload())
    }
  }

  fileprivate func handleWriteCompletion(_ peripheral: CBPeripheral, characteristic: CBCharacteristic, error: Error?) {
    bluetoothQueue.async {
      guard let operation = self.pendingOperation,
            operation.connectionEpoch == self.connectionEpoch,
            peripheral === self.connectedPeripheral,
            characteristic === self.writeCharacteristic,
            peripheral === operation.peripheral,
            characteristic === operation.characteristic else { return }
      if let error {
        self.finishPendingOperation(state: "unknown", message: error.localizedDescription)
        return
      }
      self.flushPendingChunks()
    }
  }

  fileprivate func handleWriteWithoutResponseReady(_ peripheral: CBPeripheral) {
    bluetoothQueue.async {
      guard let operation = self.pendingOperation,
            operation.connectionEpoch == self.connectionEpoch,
            peripheral === self.connectedPeripheral,
            peripheral === operation.peripheral,
            operation.characteristic === self.writeCharacteristic else { return }
      self.flushPendingChunks()
    }
  }

  private func startWrite(
    operationId: String,
    data: Data,
    timeoutMs: Int,
    kind: PrinterOperationKind,
    promise: Promise
  ) {
    let normalizedId = operationId.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !normalizedId.isEmpty else {
      promise.reject(PrinterException("PRINTER_OPERATION_ID_REQUIRED", "打印操作必须带有可审计的 operationId。"))
      return
    }
    guard pendingOperation == nil else {
      promise.reject(PrinterException("PRINTER_OPERATION_IN_PROGRESS", "已有打印机操作进行中。"))
      return
    }
    guard !usedOperationIds.contains(normalizedId) else {
      promise.reject(PrinterException(
        "PRINTER_OPERATION_ALREADY_USED",
        "该 operationId 已执行过；为避免重复打印或开箱，原生层拒绝再次发送。"
      ))
      return
    }
    guard let peripheral = connectedPeripheral,
          peripheral.state == .connected,
          let characteristic = writeCharacteristic,
          let writeType else {
      promise.reject(PrinterException("PRINTER_NOT_CONNECTED", "未连接可写的蓝牙打印机。"))
      return
    }
    // 与 Android 一致：连接就绪即记忆 ID，空数据和后续 unknown 都不能以相同 ID 重放。
    rememberOperationId(normalizedId)
    guard !data.isEmpty else {
      let result = resultPayload(operationId: normalizedId, state: "failed", message: "空打印命令被拒绝。")
      promise.resolve(result)
      return
    }

    let maxChunkSize = max(20, peripheral.maximumWriteValueLength(for: writeType))
    pendingChunks = stride(from: 0, to: data.count, by: maxChunkSize).map { offset in
      data.subdata(in: offset ..< min(offset + maxChunkSize, data.count))
    }
    let connectionEpoch = self.connectionEpoch
    let boundedTimeout = min(max(timeoutMs, 3_000), 120_000)
    let timeout = DispatchWorkItem { [weak self] in
      self?.handleWriteTimeout(operationId: normalizedId, connectionEpoch: connectionEpoch)
    }
    pendingOperation = PendingOperation(
      id: normalizedId,
      kind: kind,
      promise: promise,
      timeout: timeout,
      connectionEpoch: connectionEpoch,
      peripheral: peripheral,
      characteristic: characteristic
    )
    bluetoothQueue.asyncAfter(deadline: .now() + .milliseconds(boundedTimeout), execute: timeout)
    flushPendingChunks()
  }

  private func handleWriteTimeout(operationId: String, connectionEpoch: UInt64) {
    guard let operation = pendingOperation,
          operation.id == operationId,
          operation.connectionEpoch == connectionEpoch,
          connectionEpoch == self.connectionEpoch else { return }
    // ACK 可能在超时后才到达；先撤销当前连接，禁止它落入下一次操作的上下文。
    disconnectInternal(
      reason: "operation-timeout",
      operationMessage: "BLE 写入超时，无法确认打印或开箱结果。"
    )
  }

  private func rememberOperationId(_ operationId: String) {
    usedOperationIds.insert(operationId)
    usedOperationIdOrder.append(operationId)
    if usedOperationIdOrder.count > maxRememberedOperationIds {
      let oldestOperationId = usedOperationIdOrder.removeFirst()
      usedOperationIds.remove(oldestOperationId)
    }
  }

  private func flushPendingChunks() {
    guard let operation = pendingOperation,
          operation.connectionEpoch == connectionEpoch,
          operation.peripheral === connectedPeripheral,
          operation.characteristic === writeCharacteristic,
          let peripheral = connectedPeripheral,
          peripheral.state == .connected,
          let characteristic = writeCharacteristic,
          let writeType,
          peripheral === operation.peripheral,
          characteristic === operation.characteristic else { return }

    while !pendingChunks.isEmpty {
      if writeType == .withoutResponse && !peripheral.canSendWriteWithoutResponse { return }
      let chunk = pendingChunks.removeFirst()
      peripheral.writeValue(chunk, for: characteristic, type: writeType)
      if writeType == .withResponse { return }
    }

    if writeType == .withoutResponse {
      // CoreBluetooth 只确认字节已进入发送队列，没有外设 ACK；打印和开箱结果必须保守保留为 unknown。
      finishPendingOperation(
        state: "unknown",
        message: "BLE 无响应写入已排队，但打印机没有返回确认。"
      )
      return
    }
    finishPendingOperation(state: "completed", message: "BLE 命令已传输到打印机。")
  }

  private func finishPendingOperation(state: String, message: String?) {
    guard let operation = pendingOperation else { return }
    operation.timeout.cancel()
    pendingOperation = nil
    pendingChunks.removeAll()
    let result = resultPayload(operationId: operation.id, state: state, message: message)
    operation.promise.resolve(result)
    sendEvent("printerOperation", result.merging(["kind": operation.kind.rawValue]) { _, new in new })
  }

  private func finishScan(includeAll: Bool) {
    guard let promise = scanPromise else { return }
    stopScanTransport()
    scanPromise = nil
    let devices = discoveredDevices.values
      .filter { includeAll || ($0["isXprinter"] as? Bool ?? false) }
      .sorted { ($0["name"] as? String ?? "") < ($1["name"] as? String ?? "") }
    promise.resolve(devices)
  }

  private func cancelScan(code: String, message: String) {
    guard let promise = scanPromise else { return }
    stopScanTransport()
    scanPromise = nil
    sendEvent("printerError", ["code": code, "message": message])
    promise.reject(PrinterException(code, message))
  }

  private func failScan(code: String, message: String) {
    cancelScan(code: code, message: message)
  }

  private func failConnect(code: String, message: String) {
    guard connectPromise != nil || connectedPeripheral != nil || pendingReconnect != nil else { return }
    if connectPromise == nil, connectedPeripheral == nil {
      failPendingReconnect(code: code, message: message)
      return
    }
    let hasPendingConnect = connectPromise != nil
    connectionState = "failed"
    emitStatus(reason: "connect-failed")
    disconnectInternal(
      reason: "connect-failed",
      connectFailureCode: code,
      connectFailureMessage: message,
      operationMessage: "连接失败，无法确认打印或开箱结果。"
    )
    // 没有等待中的 JS Promise 时仍上报原生连接故障，便于诊断已建立连接后的异常回调。
    if !hasPendingConnect {
      sendEvent("printerError", ["code": code, "message": message])
    }
  }

  private func startPendingReconnect() {
    guard let reconnect = pendingReconnect else { return }
    pendingReconnect = nil
    reconnect.timeout.cancel()
    beginConnect(
      peripheral: reconnect.peripheral,
      peripheralId: reconnect.peripheralId,
      timeoutMs: reconnect.timeoutMs,
      promise: reconnect.promise
    )
  }

  private func failPendingReconnect(code: String, message: String) {
    guard let reconnect = pendingReconnect else { return }
    pendingReconnect = nil
    reconnect.timeout.cancel()
    connectionState = "disconnected"
    emitStatus(reason: "connect-failed")
    sendEvent("printerError", ["code": code, "message": message])
    reconnect.promise.reject(PrinterException(code, message))
  }

  private func disconnectInternal(
    reason: String,
    connectFailureCode: String = "PRINTER_CONNECT_CANCELLED",
    connectFailureMessage: String = "蓝牙打印机连接已取消。",
    operationMessage: String = "蓝牙连接断开，无法确认打印或开箱结果。",
    cancelTransport: Bool = true
  ) {
    // 先摘除 Promise 并取消计时器；同一串行队列上的 timeout/蓝牙回调随后到达时只能观察到 nil，
    // 从而保证连接请求恰好结束一次，不会悬挂或二次 reject。
    let pendingConnect = connectPromise
    connectPromise = nil
    connectTimeout?.cancel()
    connectTimeout = nil
    let reconnect = pendingReconnect
    pendingReconnect = nil
    reconnect?.timeout.cancel()
    // 先推进 epoch，再清空引用。即使 timeout 与 delegate 回调排队竞争，旧上下文也无法再命中。
    connectionEpoch &+= 1
    let peripheral = connectedPeripheral
    if cancelTransport, let peripheral {
      // 已连接或正在断开的外设都可能遗留 withResponse ACK；连接中失败不应让后续重连永远等待 didDisconnect。
      if peripheral.state == .connected || peripheral.state == .disconnecting {
        disconnectingPeripheral = peripheral
      }
      bluetoothDelegate.centralManager?.cancelPeripheralConnection(peripheral)
    }
    writeCharacteristic = nil
    writeType = nil
    connectedPeripheral = nil
    connectedPeripheralId = nil
    connectionState = "disconnected"
    finishPendingOperation(state: "unknown", message: operationMessage)
    emitStatus(reason: reason)
    if let pendingConnect {
      sendEvent("printerError", ["code": connectFailureCode, "message": connectFailureMessage])
      pendingConnect.reject(PrinterException(connectFailureCode, connectFailureMessage))
    }
    if let reconnect {
      sendEvent("printerError", ["code": connectFailureCode, "message": connectFailureMessage])
      reconnect.promise.reject(PrinterException(connectFailureCode, connectFailureMessage))
    }
  }

  private func stopScanTransport() {
    bluetoothDelegate.centralManager?.stopScan()
    scanTimeout?.cancel()
    scanTimeout = nil
  }

  private func ensureBluetoothReady(_ promise: Promise) -> Bool {
    guard let centralManager = bluetoothDelegate.centralManager else {
      let failure = bluetoothReadinessFailure(nil)
      promise.reject(PrinterException(failure.code, failure.message))
      return false
    }
    guard centralManager.state == .poweredOn,
          CBCentralManager.authorization == .allowedAlways else {
      let failure = bluetoothReadinessFailure(centralManager)
      promise.reject(PrinterException(failure.code, failure.message))
      return false
    }
    return true
  }

  private func bluetoothReadinessFailure(
    _ centralManager: CBCentralManager?
  ) -> (code: String, message: String) {
    // 关机是当前最直接可修复的状态，应优先于授权状态提示用户开启蓝牙。
    if centralManager?.state == .poweredOff {
      return ("PRINTER_BLUETOOTH_POWERED_OFF", "蓝牙已关闭；请开启蓝牙后重试。")
    }

    switch CBCentralManager.authorization {
    case .denied:
      return (
        "PRINTER_BLUETOOTH_PERMISSION_REQUIRED",
        "蓝牙权限已被拒绝；请在系统设置中允许此应用使用蓝牙。"
      )
    case .restricted:
      return (
        "PRINTER_BLUETOOTH_RESTRICTED",
        "蓝牙访问受到系统限制，无法在设置中解除；请联系设备管理员。"
      )
    case .notDetermined:
      return (
        "PRINTER_BLUETOOTH_AUTHORIZATION_PENDING",
        "蓝牙授权尚未完成；请完成系统授权弹窗后重试。"
      )
    case .allowedAlways:
      break
    @unknown default:
      return ("PRINTER_BLUETOOTH_UNAVAILABLE", "无法确认蓝牙权限状态。")
    }

    guard let centralManager else {
      return ("PRINTER_BLUETOOTH_UNAVAILABLE", "蓝牙模块正在初始化，请稍后重试。")
    }
    switch centralManager.state {
    case .poweredOff:
      return ("PRINTER_BLUETOOTH_POWERED_OFF", "蓝牙已关闭；请开启蓝牙后重试。")
    case .unknown, .resetting, .unsupported, .unauthorized, .poweredOn:
      return ("PRINTER_BLUETOOTH_UNAVAILABLE", "蓝牙当前不可用，请稍后重试。")
    @unknown default:
      return ("PRINTER_BLUETOOTH_UNAVAILABLE", "无法确认蓝牙状态。")
    }
  }

  private func isXprinter(_ name: String) -> Bool {
    let normalized = name.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    return normalized == "printer001"
      || normalized.contains("xprinter")
      || normalized.contains("x-printer")
      || normalized.contains("芯烨")
  }

  private func encode(text: String, encoding: String) throws -> Data {
    switch encoding.lowercased() {
    case "utf8", "utf-8":
      return Data(text.utf8)
    case "gb18030", "gbk", "gb2312":
      let cfEncoding = CFStringEncoding(CFStringEncodings.GB_18030_2000.rawValue)
      let nsEncoding = CFStringConvertEncodingToNSStringEncoding(cfEncoding)
      guard let data = text.data(using: String.Encoding(rawValue: nsEncoding), allowLossyConversion: false) else {
        throw PrinterException("PRINTER_ENCODING_FAILED", "文本无法编码为 GB18030。")
      }
      return data
    default:
      throw PrinterException("PRINTER_ENCODING_UNSUPPORTED", "不支持的打印文本编码：\(encoding)。")
    }
  }

  private func statusPayload() -> [String: Any] {
    let bluetoothState = bluetoothDelegate.centralManager?.state
    // 显式提升为 Any，避免 Swift 在三元分支中把 String 与 NSNull 视为不兼容类型。
    let writeMode: Any
    switch writeType {
    case .withResponse:
      writeMode = "withResponse"
    case .withoutResponse:
      writeMode = "withoutResponse"
    default:
      writeMode = NSNull()
    }

    return [
      "supported": bluetoothState != nil && bluetoothState != .unsupported,
      "enabled": bluetoothState == .poweredOn,
      "connection": connectionState,
      "peripheralId": connectedPeripheralId ?? NSNull(),
      "writeMode": writeMode,
    ]
  }

  private func resultPayload(operationId: String, state: String, message: String?) -> [String: Any] {
    [
      "operationId": operationId,
      "state": state,
      "message": message ?? NSNull(),
    ]
  }

  private func emitStatus(reason: String) {
    var event = statusPayload()
    event["reason"] = reason
    sendEvent("printerStatus", event)
  }
}
