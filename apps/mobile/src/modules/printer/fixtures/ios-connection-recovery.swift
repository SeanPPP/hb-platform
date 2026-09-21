import CoreFoundation
import Foundation

typealias RCTPromiseResolveBlock = (Any?) -> Void
typealias RCTPromiseRejectBlock = (String?, String?, Error?) -> Void

enum CBManagerState {
  case unknown
  case resetting
  case unsupported
  case unauthorized
  case poweredOff
  case poweredOn
}

enum CBPeripheralState {
  case disconnected
  case connected
}

enum CBCharacteristicWriteType {
  case withResponse
  case withoutResponse
}

struct CBCharacteristicProperties: OptionSet {
  let rawValue: Int

  static let write = CBCharacteristicProperties(rawValue: 1 << 0)
  static let writeWithoutResponse = CBCharacteristicProperties(rawValue: 1 << 1)
}

final class CBCharacteristic {
  let properties: CBCharacteristicProperties

  init(properties: CBCharacteristicProperties) {
    self.properties = properties
  }
}

final class CBPeripheral {
  let identifier: UUID
  weak var delegate: AnyObject?
  var state: CBPeripheralState = .connected
  var canSendWriteWithoutResponse = true
  var maximumWriteLength = 20
  private(set) var writes: [(Data, CBCharacteristic, CBCharacteristicWriteType)] = []

  init(identifier: UUID = UUID()) {
    self.identifier = identifier
  }

  func maximumWriteValueLength(for type: CBCharacteristicWriteType) -> Int {
    maximumWriteLength
  }

  func writeValue(_ data: Data, for characteristic: CBCharacteristic, type: CBCharacteristicWriteType) {
    writes.append((data, characteristic, type))
  }
}

final class CBCentralManager {
  var state: CBManagerState = .poweredOn
  var retrievedPeripherals: [UUID: CBPeripheral] = [:]
  private(set) var cancelledPeripherals: [CBPeripheral] = []
  private(set) var connectedPeripherals: [CBPeripheral] = []

  func retrievePeripherals(withIdentifiers identifiers: [UUID]) -> [CBPeripheral] {
    identifiers.compactMap { retrievedPeripherals[$0] }
  }

  func connect(_ peripheral: CBPeripheral, options: [String: Any]?) {
    connectedPeripherals.append(peripheral)
  }

  func cancelPeripheralConnection(_ peripheral: CBPeripheral) {
    // CoreBluetooth cancellation is asynchronous. Keep the peripheral connected so the
    // harness can deliver the stale ACK before the terminal disconnect callback.
    cancelledPeripherals.append(peripheral)
  }
}

final class HarnessModule {
  private let bluetoothQueue = DispatchQueue(label: "ios-connection-recovery.harness")
  private var centralManager = CBCentralManager()
  private var discoveredPeripherals: [String: CBPeripheral] = [:]
  private var connectedPeripheral: CBPeripheral?
  private var writeCharacteristic: CBCharacteristic?
  private var connectedAddress: String?
  private var connectResolve: RCTPromiseResolveBlock?
  private var connectReject: RCTPromiseRejectBlock?
  private var connectTimeoutWorkItem: DispatchWorkItem?
  private var connectionGeneration = 0
  private var activeConnectGeneration: Int?
  private var retiringPeripheralIDs: Set<ObjectIdentifier> = []
  private var pendingCharacteristicServiceCount = 0
  private var printResolve: RCTPromiseResolveBlock?
  private var printReject: RCTPromiseRejectBlock?
  private var pendingWriteChunks: [Data] = []
  private var pendingWriteType: CBCharacteristicWriteType?
  private var pendingWriteCharacteristic: CBCharacteristic?
  private var printTimeoutWorkItem: DispatchWorkItem?
  private var hasStatusListeners = false
  private static let statusEvent = "HbPrinterStatusChanged"

  private func sendEvent(withName name: String, body: Any?) {}

// __PRODUCTION_METHODS__
}

struct HarnessFailure: Error, CustomStringConvertible {
  let description: String
}

func expect(_ condition: @autoclosure () -> Bool, _ message: String) throws {
  if !condition() {
    throw HarnessFailure(description: message)
  }
}

func pumpMainQueue(until condition: @escaping () -> Bool, timeout: TimeInterval = 1) {
  let deadline = Date().addingTimeInterval(timeout)
  while !condition() && Date() < deadline {
    _ = RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.01))
  }
}

extension HarnessModule {
  private func attach(_ peripheral: CBPeripheral, characteristic: CBCharacteristic) {
    connectedPeripheral = peripheral
    connectedAddress = peripheral.identifier.uuidString
    writeCharacteristic = characteristic
    discoveredPeripherals[peripheral.identifier.uuidString] = peripheral
    centralManager.retrievedPeripherals[peripheral.identifier] = peripheral
  }

  func verifyTimeoutRetiresSessionAndIgnoresLateAck() throws {
    let oldPeripheral = CBPeripheral()
    let oldCharacteristic = CBCharacteristic(properties: .write)
    attach(oldPeripheral, characteristic: oldCharacteristic)

    var firstRejects: [(String?, String?)] = []
    writePrinterCommand(
      String(repeating: "A", count: 45),
      encoding: "UTF-8",
      resolver: { _ in },
      rejecter: { code, message, _ in firstRejects.append((code, message)) }
    )
    try expect(oldPeripheral.writes.count == 1, "withResponse 打印 A 应仅发送首个分片并等待 ACK")

    printTimeoutWorkItem?.perform()
    pumpMainQueue(until: { !firstRejects.isEmpty })
    try expect(firstRejects.first?.0 == "PRINT_TIMEOUT", "打印 A 应保留原始 PRINT_TIMEOUT")
    try expect(connectedPeripheral == nil && writeCharacteristic == nil, "超时后必须立即清除旧会话")
    try expect(
      centralManager.cancelledPeripherals.last === oldPeripheral,
      "超时后必须取消旧 peripheral"
    )
    try expect(
      retiringPeripheralIDs.contains(ObjectIdentifier(oldPeripheral)),
      "异步取消完成前必须隔离旧 peripheral"
    )

    var retryRejects: [(String?, String?)] = []
    connect(
      oldPeripheral.identifier.uuidString,
      resolver: { _ in },
      rejecter: { code, message, _ in retryRejects.append((code, message)) }
    )
    pumpMainQueue(until: { !retryRejects.isEmpty })
    try expect(retryRejects.first?.0 == "CONNECT_ERROR", "重试不得复用仍在退役的旧 peripheral")
    try expect(
      !centralManager.connectedPeripherals.contains(where: { $0 === oldPeripheral }),
      "退役实例不得再次交给 CoreBluetooth connect"
    )
    try expect(oldPeripheral.writes.count == 1, "重试不得在旧 peripheral 上自动重放打印 A")

    let newPeripheral = CBPeripheral()
    let newCharacteristic = CBCharacteristic(properties: .write)
    attach(newPeripheral, characteristic: newCharacteristic)
    var secondResolved = false
    writePrinterCommand(
      String(repeating: "B", count: 45),
      encoding: "UTF-8",
      resolver: { _ in secondResolved = true },
      rejecter: { code, message, _ in
        fatalError("打印 B 不应失败: \(code ?? "") \(message ?? "")")
      }
    )
    try expect(newPeripheral.writes.count == 1, "打印 B 应等待自己的首个 ACK")

    peripheral(oldPeripheral, didWriteValueFor: oldCharacteristic, error: nil)
    try expect(newPeripheral.writes.count == 1, "打印 A 的迟到 ACK 不得推进打印 B")
    try expect(!secondResolved, "打印 A 的迟到 ACK 不得结算打印 B")

    peripheral(newPeripheral, didWriteValueFor: newCharacteristic, error: nil)
    peripheral(newPeripheral, didWriteValueFor: newCharacteristic, error: nil)
    peripheral(newPeripheral, didWriteValueFor: newCharacteristic, error: nil)
    pumpMainQueue(until: { secondResolved })
    try expect(secondResolved, "打印 B 应由自己的 ACK 完成")
  }

  func verifyWriteErrorRetiresSession() throws {
    let peripheralUnderTest = CBPeripheral()
    let characteristic = CBCharacteristic(properties: .write)
    attach(peripheralUnderTest, characteristic: characteristic)
    var rejects: [(String?, String?)] = []
    writePrinterCommand(
      String(repeating: "E", count: 25),
      encoding: "UTF-8",
      resolver: { _ in },
      rejecter: { code, message, _ in rejects.append((code, message)) }
    )

    let writeError = NSError(domain: "Harness", code: 17, userInfo: [
      NSLocalizedDescriptionKey: "write failed",
    ])
    self.peripheral(peripheralUnderTest, didWriteValueFor: characteristic, error: writeError)
    pumpMainQueue(until: { !rejects.isEmpty })
    try expect(rejects.first?.0 == "PRINT_ERROR", "写回调错误应保留 PRINT_ERROR")
    try expect(connectedPeripheral == nil && writeCharacteristic == nil, "写回调错误后必须清除旧会话")
    try expect(
      retiringPeripheralIDs.contains(ObjectIdentifier(peripheralUnderTest)),
      "写回调错误后必须隔离旧 peripheral"
    )
  }

  func verifyReadinessWithoutPrintKeepsHealthySession() throws {
    let healthyPeripheral = CBPeripheral()
    let characteristic = CBCharacteristic(properties: .writeWithoutResponse)
    attach(healthyPeripheral, characteristic: characteristic)

    peripheralIsReady(toSendWriteWithoutResponse: healthyPeripheral)
    try expect(connectedPeripheral === healthyPeripheral, "无在途打印时 readiness 不得关闭健康会话")
    try expect(writeCharacteristic === characteristic, "无在途打印时 readiness 不得清除写特征")
    try expect(centralManager.cancelledPeripherals.isEmpty, "无在途打印时不得请求断开")
  }

  func verifyEncodingFailureKeepsHealthySession() throws {
    let healthyPeripheral = CBPeripheral()
    let characteristic = CBCharacteristic(properties: .write)
    attach(healthyPeripheral, characteristic: characteristic)
    var rejects: [(String?, String?)] = []

    writePrinterCommand(
      "content",
      encoding: "NOT-A-REAL-ENCODING",
      resolver: { _ in },
      rejecter: { code, message, _ in rejects.append((code, message)) }
    )
    pumpMainQueue(until: { !rejects.isEmpty })
    try expect(rejects.first?.0 == "PRINT_ERROR", "内容编码失败应以 PRINT_ERROR 返回")
    try expect(connectedPeripheral === healthyPeripheral, "内容编码失败不得清连接")
    try expect(writeCharacteristic === characteristic, "内容编码失败不得清写特征")
    try expect(centralManager.cancelledPeripherals.isEmpty, "内容编码失败不得请求断开")
  }
}

do {
  try HarnessModule().verifyTimeoutRetiresSessionAndIgnoresLateAck()
  print("PASS timeout retires old session and stale ACK cannot advance retry")
  try HarnessModule().verifyWriteErrorRetiresSession()
  print("PASS didWrite error retires old session")
  try HarnessModule().verifyReadinessWithoutPrintKeepsHealthySession()
  print("PASS readiness without active print keeps healthy session")
  try HarnessModule().verifyEncodingFailureKeepsHealthySession()
  print("PASS encoding failure keeps healthy session")
} catch {
  fputs("FAIL \(error)\n", stderr)
  exit(1)
}
