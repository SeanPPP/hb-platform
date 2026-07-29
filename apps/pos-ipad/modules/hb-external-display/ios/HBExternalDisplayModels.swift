import ExpoModulesCore
import Foundation

private let maximumSafeJavaScriptInteger = 9_007_199_254_740_991

enum HBExternalDisplayMode: String {
  case idle
  case cart
  case payment
  case change
  case success
}

enum HBExternalDisplayAdvertKind: String {
  case image
  case video
}

struct HBExternalDisplayMoney {
  let cents: Int

  var dictionary: [String: Any] {
    [
      "currency": "AUD",
      "cents": cents,
    ]
  }
}

struct HBExternalDisplayItem {
  let name: String
  let quantity: String
  let amount: HBExternalDisplayMoney

  var dictionary: [String: Any] {
    [
      "name": name,
      "quantity": quantity,
      "amount": amount.dictionary,
    ]
  }
}

struct HBExternalDisplayAdvert {
  let kind: HBExternalDisplayAdvertKind
  let localUri: String
  let url: URL

  var dictionary: [String: Any] {
    [
      "kind": kind.rawValue,
      "localUri": localUri,
    ]
  }
}

struct HBExternalDisplaySnapshot {
  let revision: Int
  let mode: HBExternalDisplayMode
  let items: [HBExternalDisplayItem]
  let gst: HBExternalDisplayMoney
  let discount: HBExternalDisplayMoney
  let total: HBExternalDisplayMoney
  let change: HBExternalDisplayMoney
  let advert: HBExternalDisplayAdvert?

  var dictionary: [String: Any] {
    [
      "revision": revision,
      "mode": mode.rawValue,
      "items": items.map(\.dictionary),
      "gst": gst.dictionary,
      "discount": discount.dictionary,
      "total": total.dictionary,
      "change": change.dictionary,
      "advert": advert?.dictionary ?? NSNull(),
    ]
  }
}

struct HBExternalDisplayMoneyRecord: Record {
  @Field
  var currency = ""

  @Field
  var cents = 0

  func validated(field: String) throws -> HBExternalDisplayMoney {
    guard currency == "AUD" else {
      throw HBExternalDisplayValidationError.invalid("\(field).currency")
    }
    guard
      cents >= -maximumSafeJavaScriptInteger,
      cents <= maximumSafeJavaScriptInteger
    else {
      throw HBExternalDisplayValidationError.invalid("\(field).cents")
    }
    return HBExternalDisplayMoney(cents: cents)
  }
}

struct HBExternalDisplayItemRecord: Record {
  @Field
  var name = ""

  @Field
  var quantity = ""

  @Field
  var amount = HBExternalDisplayMoneyRecord()

  func validated(index: Int) throws -> HBExternalDisplayItem {
    guard (1...160).contains(name.count) else {
      throw HBExternalDisplayValidationError.invalid("items[\(index)].name")
    }
    guard
      quantity.range(
        of: #"^-?\d+(?:\.\d{1,3})?$"#,
        options: .regularExpression
      ) != nil
    else {
      throw HBExternalDisplayValidationError.invalid("items[\(index)].quantity")
    }

    return try HBExternalDisplayItem(
      name: name,
      quantity: quantity,
      amount: amount.validated(field: "items[\(index)].amount")
    )
  }
}

struct HBExternalDisplayAdvertRecord: Record {
  @Field
  var kind = ""

  @Field
  var localUri = ""

  func validated() throws -> HBExternalDisplayAdvert {
    guard let parsedKind = HBExternalDisplayAdvertKind(rawValue: kind) else {
      throw HBExternalDisplayValidationError.invalid("advert.kind")
    }
    guard
      localUri.count <= 2_048,
      let url = URL(string: localUri),
      url.isFileURL,
      !url.path.isEmpty,
      url.host == nil || url.host == "" || url.host == "localhost"
    else {
      throw HBExternalDisplayValidationError.invalid("advert.localUri")
    }

    return HBExternalDisplayAdvert(
      kind: parsedKind,
      localUri: localUri,
      url: url
    )
  }
}

struct HBExternalDisplaySnapshotRecord: Record {
  @Field
  var revision = -1

  @Field
  var mode = ""

  @Field
  var items: [HBExternalDisplayItemRecord] = []

  @Field
  var gst = HBExternalDisplayMoneyRecord()

  @Field
  var discount = HBExternalDisplayMoneyRecord()

  @Field
  var total = HBExternalDisplayMoneyRecord()

  @Field
  var change = HBExternalDisplayMoneyRecord()

  @Field
  var advert: HBExternalDisplayAdvertRecord?

  func validated() throws -> HBExternalDisplaySnapshot {
    guard revision >= 0 else {
      throw HBExternalDisplayValidationError.invalid("revision")
    }
    guard let parsedMode = HBExternalDisplayMode(rawValue: mode) else {
      throw HBExternalDisplayValidationError.invalid("mode")
    }
    guard items.count <= 100 else {
      throw HBExternalDisplayValidationError.invalid("items")
    }

    return try HBExternalDisplaySnapshot(
      revision: revision,
      mode: parsedMode,
      items: items.enumerated().map { index, item in
        try item.validated(index: index)
      },
      gst: gst.validated(field: "gst"),
      discount: discount.validated(field: "discount"),
      total: total.validated(field: "total"),
      change: change.validated(field: "change"),
      advert: advert == nil ? nil : try advert?.validated()
    )
  }
}

enum HBExternalDisplayValidationError: LocalizedError {
  case invalid(String)

  var errorDescription: String? {
    switch self {
    case .invalid(let field):
      return "Invalid external display field: \(field)"
    }
  }
}
