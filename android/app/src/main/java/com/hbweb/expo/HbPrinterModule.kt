package com.hbweb.expo

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothSocket
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.ReadableMap
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.MultiFormatWriter
import java.nio.charset.Charset
import java.text.SimpleDateFormat
import java.util.Date
import java.util.EnumMap
import java.util.Locale
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.max
import kotlin.math.roundToInt

class HbPrinterModule(
  reactContext: ReactApplicationContext
) : ReactContextBaseJavaModule(reactContext) {
  private val appContext = reactContext.applicationContext
  private val bluetoothAdapter: BluetoothAdapter? by lazy {
    val manager = appContext.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager
    manager?.adapter
  }
  private val handler = Handler(Looper.getMainLooper())
  private val printerUuid: UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
  private val labelWidth = 570
  private val labelHeight = 400

  @Volatile
  private var socket: BluetoothSocket? = null

  @Volatile
  private var connectedAddress: String? = null

  override fun getName(): String = "HbPrinterModule"

  @ReactMethod
  fun getStatus(promise: Promise) {
    try {
      val adapter = bluetoothAdapter
      val map = Arguments.createMap()
      map.putBoolean("supported", adapter != null)
      map.putBoolean("enabled", adapter?.isEnabled == true)
      map.putBoolean("connected", socket?.isConnected == true)
      map.putString("address", connectedAddress)
      promise.resolve(map)
    } catch (error: Exception) {
      promise.reject("STATUS_ERROR", error.message, error)
    }
  }

  @SuppressLint("MissingPermission")
  @ReactMethod
  fun scanPrinters(durationMs: Int, promise: Promise) {
    val adapter = bluetoothAdapter
    if (adapter == null) {
      promise.reject("BLUETOOTH_UNSUPPORTED", "Bluetooth is not supported on this device.")
      return
    }

    if (!adapter.isEnabled) {
      promise.reject("BLUETOOTH_DISABLED", "Bluetooth is turned off.")
      return
    }

    val devices = ConcurrentHashMap<String, WritablePrinterDevice>()
    adapter.bondedDevices?.forEach { device ->
      devices[device.address] = WritablePrinterDevice(
        name = device.name,
        address = device.address,
        bonded = true,
        connected = device.address == connectedAddress && socket?.isConnected == true
      )
    }

    val resolved = AtomicBoolean(false)
    var receiver: BroadcastReceiver? = null

    fun finishScan() {
      if (!resolved.compareAndSet(false, true)) {
        return
      }

      try {
        if (adapter.isDiscovering) {
          adapter.cancelDiscovery()
        }
      } catch (_: Exception) {
      }

      receiver?.let {
        try {
          appContext.unregisterReceiver(it)
        } catch (_: Exception) {
        }
      }

      val array = Arguments.createArray()
      devices.values.sortedBy { it.name ?: it.address }.forEach { printer ->
        val map = Arguments.createMap()
        map.putString("name", printer.name)
        map.putString("address", printer.address)
        map.putBoolean("bonded", printer.bonded)
        map.putBoolean("connected", printer.connected)
        array.pushMap(map)
      }
      promise.resolve(array)
    }

    receiver = object : BroadcastReceiver() {
      override fun onReceive(context: Context?, intent: Intent?) {
        when (intent?.action) {
          BluetoothDevice.ACTION_FOUND -> {
            val device = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
              intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice::class.java)
            } else {
              @Suppress("DEPRECATION")
              intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE)
            }

            if (device?.address != null) {
              devices[device.address] = WritablePrinterDevice(
                name = device.name,
                address = device.address,
                bonded = device.bondState == BluetoothDevice.BOND_BONDED,
                connected = device.address == connectedAddress && socket?.isConnected == true
              )
            }
          }
          BluetoothAdapter.ACTION_DISCOVERY_FINISHED -> finishScan()
        }
      }
    }

    val filter = IntentFilter().apply {
      addAction(BluetoothDevice.ACTION_FOUND)
      addAction(BluetoothAdapter.ACTION_DISCOVERY_FINISHED)
    }

    try {
      if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        appContext.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED)
      } else {
        @Suppress("DEPRECATION")
        appContext.registerReceiver(receiver, filter)
      }

      if (adapter.isDiscovering) {
        adapter.cancelDiscovery()
      }
      adapter.startDiscovery()
      handler.postDelayed({ finishScan() }, durationMs.coerceAtLeast(1500).toLong())
    } catch (error: Exception) {
      try {
        appContext.unregisterReceiver(receiver)
      } catch (_: Exception) {
      }
      promise.reject("SCAN_ERROR", error.message, error)
    }
  }

  @SuppressLint("MissingPermission")
  @ReactMethod
  fun connect(address: String, promise: Promise) {
    val adapter = bluetoothAdapter
    if (adapter == null) {
      promise.reject("BLUETOOTH_UNSUPPORTED", "Bluetooth is not supported on this device.")
      return
    }

    if (!adapter.isEnabled) {
      promise.reject("BLUETOOTH_DISABLED", "Bluetooth is turned off.")
      return
    }

    Thread {
      try {
        disconnectInternal()
        if (adapter.isDiscovering) {
          adapter.cancelDiscovery()
        }

        val device = adapter.getRemoteDevice(address)
        val nextSocket = device.createRfcommSocketToServiceRecord(printerUuid)
        nextSocket.connect()
        socket = nextSocket
        connectedAddress = address
        promise.resolve(true)
      } catch (error: Exception) {
        disconnectInternal()
        promise.reject("CONNECT_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun disconnect(promise: Promise) {
    try {
      disconnectInternal()
      promise.resolve(true)
    } catch (error: Exception) {
      promise.reject("DISCONNECT_ERROR", error.message, error)
    }
  }

  @ReactMethod
  fun print(command: String, encoding: String?, promise: Promise) {
    Thread {
      try {
        writePrinterCommand(command, encoding ?: "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printProductLabel(payload: ReadableMap, printType: String?, promise: Promise) {
    Thread {
      try {
        val command = buildProductLabelCommand(payload, printType?.trim().orEmpty())
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_PRODUCT_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printDiscountLabel(payload: ReadableMap, printType: String?, promise: Promise) {
    Thread {
      try {
        val command = buildDiscountLabelCommand(payload, printType?.trim().orEmpty())
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_DISCOUNT_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printClearanceLabel(payload: ReadableMap, promise: Promise) {
    Thread {
      try {
        val command = buildClearanceLabelCommand(payload)
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_CLEARANCE_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printBigDiscountLabel(payload: ReadableMap, printType: String?, promise: Promise) {
    Thread {
      try {
        val command = buildBigDiscountLabelCommand(payload, printType?.trim().orEmpty())
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_BIG_DISCOUNT_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printWarehouseProductLabel(payload: ReadableMap, promise: Promise) {
    Thread {
      try {
        val command = buildWarehouseProductLabelCommand(payload)
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_WAREHOUSE_PRODUCT_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  @ReactMethod
  fun printWarehouseLocationLabel(payload: ReadableMap, promise: Promise) {
    Thread {
      try {
        val command = buildWarehouseLocationLabelCommand(payload)
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_WAREHOUSE_LOCATION_LABEL_ERROR", error.message, error)
      }
    }.start()
  }

  private fun writePrinterCommand(command: String, encoding: String) {
    val activeSocket = socket
    if (activeSocket == null || !activeSocket.isConnected) {
      throw IllegalStateException("No Bluetooth printer is connected.")
    }

    val charset = Charset.forName(encoding)
    val outputStream = activeSocket.outputStream
    outputStream.write(command.toByteArray(charset))
    outputStream.flush()
  }

  private fun buildProductLabelCommand(payload: ReadableMap, printType: String = ""): String {
    val isSmall = printType.equals("small", ignoreCase = true)
    val w = if (isSmall) 472 else labelWidth
    val h = if (isSmall) 320 else labelHeight
    val productName = payload.getNullableString("productName")
    val itemNumber = payload.getNullableString("itemNumber")
    val supplierName = formatSupplierAbbreviation(payload.getNullableString("supplierName"))
    val barcode = payload.getNullableString("barcode")
    val retailPrice = payload.getNullableDouble("retailPrice")
    val discountRate = payload.getNullableDouble("discountRate") ?: 0.0
    val grade = payload.getNullableString("grade").trim().uppercase(Locale.US).firstOrNull()?.toString()

    val price = formatPriceParts(retailPrice)
    val priceIntegerBitmap = textToBitmap(price.integer, fontSizeToPixels(40f), true, "sans-serif-black")
    val priceDotBitmap = textToBitmap(".", fontSizeToPixels(20f), true, "sans-serif-black")
    val priceDecimalBitmap = textToBitmap(price.decimal, fontSizeToPixels(20f), true, "sans-serif-black")
    val priceCurrencyBitmap = textToBitmap("$", fontSizeToPixels(20f), false, "sans-serif-black")
    val itemBitmap = textToBitmap(itemNumber, fontSizeToPixels(8f), true, "sans-serif-black")
    val supplierBitmap = textToBitmap(supplierName, fontSizeToPixels(8f), true, "sans-serif-light", true, 2)
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), true, "sans-serif-black", true, 2)
    val nameMaxWidth = max(
      1,
      w - priceDecimalBitmap.width - priceDotBitmap.width - priceIntegerBitmap.width - priceCurrencyBitmap.width,
    )
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(10f), false, "Arial", 2, nameMaxWidth)
    val discountBitmap = if (discountRate > 0) {
      textToBitmap("${(discountRate * 100).roundToInt().toString().padStart(2, '0')}%OFF", fontSizeToPixels(8f), true, "sans-serif-black", true, 2)
    } else {
      null
    }
    val gradeBitmap = grade?.let {
      textToBitmap(it, fontSizeToPixels(8f), true, "sans-serif-black", true, 4)
    }

    val startY = 30
    val startX = w - priceDecimalBitmap.width
    val commands = mutableListOf(
      "! 0 200 200 $h 1",
      "PAGE-WIDTH $w",
      bitmapCommand(5, 5, nameBitmap),
      bitmapCommand(5, 120, itemBitmap),
      bitmapCommand(5 + itemBitmap.width + 10, 118, supplierBitmap),
    )

    if (barcode.isNotBlank()) {
      commands += "BARCODE-TEXT 7 0 5"
      commands += "BARCODE 128 1 2 30 5 145 $barcode"
    }

    if (discountBitmap != null) {
      commands += bitmapCommand(w - discountBitmap.width - dateBitmap.width - 20, 175, discountBitmap)
    }

    if (gradeBitmap != null) {
      commands += bitmapCommand(300, 175, gradeBitmap)
    }

    commands += bitmapCommand(startX, startY, priceDecimalBitmap)
    commands += bitmapCommand(startX - priceDotBitmap.width, startY + priceIntegerBitmap.height - 10, priceDotBitmap)
    commands += bitmapCommand(startX - priceDotBitmap.width - priceIntegerBitmap.width, startY, priceIntegerBitmap)
    commands += bitmapCommand(
      startX - priceDotBitmap.width - priceIntegerBitmap.width - priceCurrencyBitmap.width,
      startY,
      priceCurrencyBitmap,
    )
    commands += bitmapCommand(w - dateBitmap.width, 175, dateBitmap)
    commands += "PRINT"

    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun buildDiscountLabelCommand(payload: ReadableMap, printType: String = ""): String {
    val isSmall = printType.equals("small", ignoreCase = true)
    val w = if (isSmall) 472 else labelWidth
    val h = if (isSmall) 320 else labelHeight
    val productName = payload.getNullableString("productName")
    val itemNumber = payload.getNullableString("itemNumber")
    val barcode = payload.getNullableString("barcode").ifBlank { itemNumber }
    val retailPrice = payload.getNullableDouble("retailPrice") ?: 0.0
    val discountRate = payload.getNullableDouble("discountRate") ?: 0.0
    val discountValue = discountRate * 100.0
    val nowPrice = retailPrice * (1.0 - discountRate)

    val nowLabelBitmap = textToBitmap("Now", fontSizeToPixels(8f), true, "sans-serif-black", true, 2)
    val nowPriceBitmap = textToBitmap("$${formatMoney(nowPrice)}", fontSizeToPixels(16f), true, "sans-serif-black", true, 2)
    val discountBitmap = textToBitmap(discountValue.roundToInt().toString().padStart(2, '0'), fontSizeToPixels(44f), false, "sans-serif-black")
    val offBitmap = textToBitmap("OFF", fontSizeToPixels(16f), true, "sans-serif-black")
    val percentBitmap = textToBitmap("%", fontSizeToPixels(20f), true, "sans-serif-black")
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), false, "Arial", true, 2)
    val itemBitmap = itemNumber.takeIf { it.isNotBlank() }?.let {
      textToBitmap(it, fontSizeToPixels(8f), true, "sans-serif-black")
    }

    val startY = 20
    val startX = w - discountBitmap.width - percentBitmap.width - offBitmap.width + 20
    val rightMargin = 12
    val columnGap = 10
    val nowGroupGap = 6
    val qrBitmap = barcode.takeIf { it.isNotBlank() }?.let { createQrCodeBitmap(it, 64) }
    val qrVisualWidth = qrBitmap?.width ?: 64
    val effectiveLabelBottom = 204
    val bottomMargin = 10
    val infoBandBottom = effectiveLabelBottom - bottomMargin
    val qrX = 10
    val qrY = infoBandBottom - (qrBitmap?.height ?: 64)
    val nowPriceX = w - rightMargin - nowPriceBitmap.width
    val nowLabelX = nowPriceX - nowGroupGap - nowLabelBitmap.width
    val nowLabelY = infoBandBottom - nowLabelBitmap.height
    val nowPriceY = infoBandBottom - nowPriceBitmap.height
    val dateX = qrX + qrVisualWidth + columnGap
    val dateY = infoBandBottom - dateBitmap.height
    val itemX = dateX
    val itemY = dateY - (itemBitmap?.height ?: 0) - 6
    val nameMaxWidth = max(1, w - discountBitmap.width - percentBitmap.width - offBitmap.width + 10)
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(10f), false, "Arial", 2, nameMaxWidth)

    val commands = mutableListOf(
      "! 0 200 200 $h 1",
      "PAGE-WIDTH $w",
      bitmapCommand(5, 5, nameBitmap),
      bitmapCommand(startX, startY, discountBitmap),
      bitmapCommand(startX + discountBitmap.width, startY, percentBitmap),
      bitmapCommand(
        startX + discountBitmap.width + percentBitmap.width / 2,
        startY + discountBitmap.height - offBitmap.height,
        offBitmap,
      ),
    )

    if (itemBitmap != null) {
      commands += bitmapCommand(itemX, itemY, itemBitmap)
    }

    if (qrBitmap != null) {
      commands += bitmapCommand(qrX, qrY, qrBitmap)
    }

    commands += bitmapCommand(dateX, dateY, dateBitmap)
    commands += bitmapCommand(nowLabelX, nowLabelY, nowLabelBitmap)
    commands += bitmapCommand(nowPriceX, nowPriceY, nowPriceBitmap)
    commands += "PRINT"

    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun buildClearanceLabelCommand(payload: ReadableMap): String {
    val w = 614
    val h = 205
    val productName = payload.getNullableString("productName")
    val itemNumber = payload.getNullableString("itemNumber")
    val supplierName = formatSupplierAbbreviation(payload.getNullableString("supplierName"))
    val barcode = payload.getNullableString("clearanceBarcode").ifBlank {
      payload.getNullableString("barcode")
    }
    val retailPrice = payload.getNullableDouble("retailPrice") ?: 0.0
    val discountRate = payload.getNullableDouble("discountRate") ?: 0.0
    val clearancePrice = payload.getNullableDouble("clearancePrice") ?: (retailPrice * (1.0 - discountRate))

    val clearanceLabelBitmap = textToBitmap("CLEARANCE", fontSizeToPixels(30f), true, "sans-serif-black", true, 2)
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(7f), true, "sans-serif-black", true, 2)
    val itemBitmap = textToBitmap(itemNumber, fontSizeToPixels(7f), true, "sans-serif-black")
    val price = formatPriceParts(clearancePrice)
    val priceIntegerBitmap = textToBitmap(price.integer, fontSizeToPixels(40f), true, "sans-serif-black")
    val priceDotBitmap = textToBitmap(".", fontSizeToPixels(20f), true, "sans-serif-black")
    val priceDecimalBitmap = textToBitmap(price.decimal, fontSizeToPixels(20f), true, "sans-serif-black")
    val priceCurrencyBitmap = textToBitmap("$", fontSizeToPixels(20f), false, "sans-serif-black")
    val priceTotalWidth = priceCurrencyBitmap.width + priceIntegerBitmap.width + priceDotBitmap.width + priceDecimalBitmap.width
    val qrBitmap = barcode.takeIf { it.isNotBlank() }?.let { createQrCodeBitmap(it, 64) }
    val qrVisualWidth = qrBitmap?.width ?: 0
    val infoAreaLeft = qrVisualWidth + 15
    val nameMaxWidth = max(1, w - priceTotalWidth - infoAreaLeft - 15)
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(8f), false, "Arial", 2, nameMaxWidth)

    val nameY = 21
    val priceStartY = nameY + 8
    val priceStartX = w - priceDecimalBitmap.width - 68
    val clearanceLabelY = h - clearanceLabelBitmap.height - 5
    val clearanceLabelX = max(0, (w - clearanceLabelBitmap.width) / 2)
    val qrY = clearanceLabelY - (qrBitmap?.height ?: 64) - 5
    val infoY = qrY + 5
    val dateY = infoY + max(itemBitmap.height, dateBitmap.height) + 3

    val commands = mutableListOf(
      "! 0 200 200 $h 1",
      "PAGE-WIDTH $w",
      bitmapCommand(5, nameY, nameBitmap),
      bitmapCommand(priceStartX, priceStartY, priceDecimalBitmap),
      bitmapCommand(priceStartX - priceDotBitmap.width, priceStartY + priceIntegerBitmap.height - 10, priceDotBitmap),
      bitmapCommand(priceStartX - priceDotBitmap.width - priceIntegerBitmap.width, priceStartY, priceIntegerBitmap),
      bitmapCommand(
        priceStartX - priceDotBitmap.width - priceIntegerBitmap.width - priceCurrencyBitmap.width,
        priceStartY,
        priceCurrencyBitmap,
      ),
      bitmapCommand(clearanceLabelX, clearanceLabelY, clearanceLabelBitmap),
      bitmapCommand(infoAreaLeft, infoY, itemBitmap),
      bitmapCommand(infoAreaLeft, dateY, dateBitmap),
    )

    if (qrBitmap != null) {
      commands += bitmapCommand(5, qrY, qrBitmap)
    }
    commands += "PRINT"

    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun buildBigDiscountLabelCommand(payload: ReadableMap, printType: String): String {
    val productName = payload.getNullableString("productName")
    val barcode = payload.getNullableString("barcode")
    val retailPrice = payload.getNullableDouble("retailPrice") ?: 0.0
    val discountRate = payload.getNullableDouble("discountRate") ?: 0.0
    val paperWidth = 480
    val afterDiscount = retailPrice * (1.0 - discountRate)
    val price = formatPriceParts(afterDiscount)
    val saveAmount = retailPrice * discountRate

    val commands = mutableListOf(
      "! 0 200 200 1200 1",
      "PAGE-WIDTH $paperWidth",
    )

    commands += buildBigDiscountHeaderCommands(discountRate, printType, paperWidth)

    val currencyBitmap = textToBitmap("$", fontSizeToPixels(20f), false, "sans-serif-black")
    val wasCurrencyBitmap = textToBitmap("$", fontSizeToPixels(8f), true, "sans-serif-black")
    val saveCurrencyBitmap = textToBitmap("$", fontSizeToPixels(8f), false, "sans-serif-black")
    val eaBitmap = textToBitmap("ea", fontSizeToPixels(8f), false, "sans-serif-light", true, 2)
    val wasBitmap = textToBitmap("WAS ", fontSizeToPixels(10f), true, "sans-serif-light")
    val saveBitmap = textToBitmap("SAVE", fontSizeToPixels(16f), true, "sans-serif-light", true, 2)
    val intBitmap = textToBitmap(price.integer, fontSizeToPixels(60f), true, "sans-serif-black")
    val decimalBitmap = textToBitmap(price.decimal, fontSizeToPixels(24f), true, "sans-serif-black")
    val dotBitmap = textToBitmap(".", fontSizeToPixels(20f), true, "sans-serif-black")
    val rrpBitmap = textToBitmap(formatMoney(retailPrice), fontSizeToPixels(10f), true, "sans-serif-condensed")
    val saveAmountBitmap = textToBitmap(formatMoney(saveAmount), fontSizeToPixels(16f), true, "sans-serif-condensed")
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(10f), false, "Arial", 4, paperWidth)
    val dashLineBitmap = createDashLineBitmap(450, 2)
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), false, "Arial", true, 2)

    val startY = 220
    var startX = (paperWidth - currencyBitmap.width - intBitmap.width) / 2
    if (price.decimal.toIntOrNull() != 0) {
      startX = (paperWidth - currencyBitmap.width - intBitmap.width - dotBitmap.width - decimalBitmap.width) / 2
      commands += bitmapCommand(startX + currencyBitmap.width + intBitmap.width, startY + (intBitmap.height * 0.9).toInt(), dotBitmap)
      commands += bitmapCommand(startX + currencyBitmap.width + intBitmap.width + dotBitmap.width, startY, decimalBitmap)
    }

    commands += bitmapCommand(startX, startY, currencyBitmap)
    commands += bitmapCommand(startX + currencyBitmap.width, startY, intBitmap)
    commands += bitmapCommand(startX + currencyBitmap.width + intBitmap.width + 30, startY + (intBitmap.height * 0.9).toInt(), eaBitmap)

    val rrpStartY = startY + intBitmap.height + 20
    commands += bitmapCommand(5, rrpStartY, wasBitmap)
    commands += bitmapCommand(5 + wasBitmap.width, rrpStartY, wasCurrencyBitmap)
    commands += bitmapCommand(5 + wasBitmap.width + wasCurrencyBitmap.width, rrpStartY, rrpBitmap)

    if (discountRate > 0) {
      commands += "LINE 5 $rrpStartY ${5 + wasBitmap.width + rrpBitmap.width} ${rrpStartY + wasBitmap.height} 2"
      commands += "LINE 5 ${rrpStartY + wasBitmap.height} ${5 + wasBitmap.width + rrpBitmap.width} $rrpStartY 2"
      val saveStartX = 30 + wasBitmap.width + wasCurrencyBitmap.width + rrpBitmap.width
      commands += bitmapCommand(saveStartX, rrpStartY, saveBitmap)
      commands += bitmapCommand(saveStartX + saveBitmap.width + 5, rrpStartY, saveCurrencyBitmap)
      commands += bitmapCommand(saveStartX + saveBitmap.width + 5 + saveCurrencyBitmap.width + 5, rrpStartY, saveAmountBitmap)
    }

    commands += bitmapCommand(5, 550 - nameBitmap.height - 5, nameBitmap)
    commands += bitmapCommand(15, 550, dashLineBitmap)

    if (barcode.isNotBlank()) {
      commands += "BARCODE 128 1 2 30 15 560 ${cpclText(barcode)}"
    }

    commands += bitmapCommand(paperWidth - dateBitmap.width - 10, 630 - dateBitmap.height, dateBitmap)
    commands += "PRINT"

    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun buildBigDiscountHeaderCommands(discountRate: Double, printType: String, paperWidth: Int): List<String> {
    val discount = discountRate * 100.0
    if (printType.isNotBlank()) {
      val titleBitmap = textToBitmap(printType, fontSizeToPixels(25f), true, "sans-serif-black")
      return listOf(bitmapCommand(paperWidth / 2 - titleBitmap.width / 2, 80, titleBitmap))
    }

    if (discount <= 10.0 || discount > 100.0) {
      val specialBitmap = textToBitmap("Special", fontSizeToPixels(40f), true, "sans-serif-black")
      return listOf(bitmapCommand(paperWidth / 2 - specialBitmap.width / 2, 40, specialBitmap))
    }

    if (abs(discount - 50.0) < 0.01) {
      val halfBitmap = textToBitmap("1/2", fontSizeToPixels(40f), true, "sans-serif-black")
      val priceBitmap = textToBitmap("PRICE", fontSizeToPixels(25f), true, "sans-serif-black")
      return listOf(
        bitmapCommand(130, 20, halfBitmap),
        bitmapCommand(120, 20 + halfBitmap.height, priceBitmap),
      )
    }

    val discountBitmap = textToBitmap(discount.roundToInt().toString(), fontSizeToPixels(40f), true, "sans-serif-black")
    val percentBitmap = textToBitmap("%", fontSizeToPixels(24f), true, "sans-serif-condensed")
    val offBitmap = textToBitmap("OFF", fontSizeToPixels(20f), true, "sans-serif-black")
    val startX = (paperWidth - discountBitmap.width - percentBitmap.width) / 2
    return listOf(
      bitmapCommand(startX, 20, discountBitmap),
      bitmapCommand(startX + discountBitmap.width, 20, percentBitmap),
      bitmapCommand((paperWidth - offBitmap.width) / 2, 20 + discountBitmap.height + 20, offBitmap),
    )
  }

  private fun buildWarehouseProductLabelCommand(payload: ReadableMap): String {
    val w = labelWidth
    val h = 360
    val productName = payload.getNullableString("productName")
    val itemNumber = payload.getNullableString("itemNumber")
    val productCode = payload.getNullableString("productCode")
    val barcode = payload.getNullableString("barcode").ifBlank { itemNumber.ifBlank { productCode } }
    val supplierName = processCapitalization(payload.getNullableString("supplierName"))
    val retailPrice = payload.getNullableDouble("retailPrice")
    val locationCode = payload.getNullableString("locationCode")
    val locationBarcode = payload.getNullableString("locationBarcode")
    val domesticPrice = payload.getNullableDouble("domesticPrice")
    val oemPrice = payload.getNullableDouble("oemPrice")
    val importPrice = payload.getNullableDouble("importPrice")
    val displayCode = itemNumber.ifBlank { productCode }
    val displayPrice = retailPrice ?: domesticPrice ?: oemPrice ?: importPrice
    val priceDetail = buildList {
      domesticPrice?.let { add("D ${formatMoney(it)}") }
      oemPrice?.let { add("O ${formatMoney(it)}") }
      importPrice?.let { add("I ${formatMoney(it)}") }
    }.joinToString("   ")
    val qrBitmap = barcode.takeIf { it.isNotBlank() }?.let { createQrCodeBitmap(it, 88) }
    val contentWidth = w - (qrBitmap?.width ?: 0) - 35

    val titleBitmap = textToBitmap("WAREHOUSE", fontSizeToPixels(11f), true, "sans-serif-black", true, 3)
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(10f), true, "Arial", 2, max(180, contentWidth))
    val itemBitmap = textToBitmap("ITEM ${cpclText(displayCode)}", fontSizeToPixels(8f), true, "sans-serif-black")
    val productCodeBitmap = textToBitmap("CODE ${cpclText(productCode)}", fontSizeToPixels(8f), true, "sans-serif-black")
    val supplierBitmap = supplierName.takeIf { it.isNotBlank() }?.let {
      textToBitmap("SUP ${cpclText(it)}", fontSizeToPixels(8f), true, "sans-serif-light", true, 2)
    }
    val priceBitmap = textToBitmap(
      "PRICE ${formatOptionalMoney(displayPrice)}",
      fontSizeToPixels(10f),
      true,
      "sans-serif-black",
      true,
      3,
    )
    val priceDetailBitmap = priceDetail.takeIf { it.isNotBlank() }?.let {
      textToBitmap(it, fontSizeToPixels(8f), true, "sans-serif-black")
    }
    val locationBitmap = textToBitmap(
      "LOC ${cpclText(locationCode.ifBlank { "UNASSIGNED" })}",
      fontSizeToPixels(10f),
      true,
      "sans-serif-black",
      true,
      3,
    )
    val locationBarcodeBitmap = locationBarcode.takeIf { it.isNotBlank() }?.let {
      textToBitmap("LOC BAR ${cpclText(it)}", fontSizeToPixels(8f), true, "sans-serif-black")
    }
    val barcodeTextBitmap = barcode.takeIf { it.isNotBlank() }?.let {
      textToBitmap("BAR ${cpclText(it)}", fontSizeToPixels(8f), true, "sans-serif-black")
    }
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), true, "sans-serif-black", true, 2)

    val commands = mutableListOf(
      "! 0 200 200 $h 1",
      "PAGE-WIDTH $w",
      bitmapCommand(10, 8, titleBitmap),
      bitmapCommand(10, 46, nameBitmap),
      bitmapCommand(10, 116, itemBitmap),
      bitmapCommand(10, 144, productCodeBitmap),
      bitmapCommand(10, 176, priceBitmap),
      bitmapCommand(10, 206, locationBitmap),
      bitmapCommand(w - dateBitmap.width - 10, 206, dateBitmap),
    )

    if (supplierBitmap != null) {
      commands += bitmapCommand(10, 236, supplierBitmap)
    }

    if (priceDetailBitmap != null) {
      commands += bitmapCommand(10, 266, priceDetailBitmap)
    }

    if (locationBarcodeBitmap != null) {
      commands += bitmapCommand(10, 292, locationBarcodeBitmap)
    }

    if (barcodeTextBitmap != null) {
      commands += bitmapCommand(10, 318, barcodeTextBitmap)
    }

    if (qrBitmap != null) {
      commands += bitmapCommand(w - qrBitmap.width - 15, 46, qrBitmap)
    }

    if (barcode.isNotBlank()) {
      commands += "BARCODE-TEXT 7 0 5"
      commands += "BARCODE 128 1 2 24 10 330 ${cpclText(barcode)}"
    }

    commands += "PRINT"
    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun buildWarehouseLocationLabelCommand(payload: ReadableMap): String {
    val w = labelWidth
    val h = 300
    val locationCode = payload.getNullableString("locationCode")
    val locationBarcode = payload.getNullableString("locationBarcode")
    val locationGuid = payload.getNullableString("locationGuid")
    val productCount = payload.getNullableDouble("productCount")?.roundToInt() ?: 0
    val displayCode = locationCode.ifBlank { locationBarcode.ifBlank { locationGuid } }
    val barcode = locationBarcode.ifBlank { displayCode }

    val titleBitmap = textToBitmap("LOCATION", fontSizeToPixels(12f), true, "sans-serif-black", true, 3)
    val codeBitmap = longTextToBitmap(displayCode, fontSizeToPixels(22f), true, "sans-serif-black", 2, w - 30)
    val barcodeTextBitmap = textToBitmap("BAR ${cpclText(barcode)}", fontSizeToPixels(8f), true, "sans-serif-black")
    val countBitmap = textToBitmap("QTY $productCount", fontSizeToPixels(14f), true, "sans-serif-black", true, 3)
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), true, "sans-serif-black", true, 2)

    val commands = mutableListOf(
      "! 0 200 200 $h 1",
      "PAGE-WIDTH $w",
      bitmapCommand(10, 8, titleBitmap),
      bitmapCommand(10, 55, codeBitmap),
      bitmapCommand(10, 170, countBitmap),
      bitmapCommand(w - dateBitmap.width - 10, 170, dateBitmap),
      bitmapCommand(10, 210, barcodeTextBitmap),
    )

    if (barcode.isNotBlank()) {
      commands += "BARCODE-TEXT 7 0 5"
      commands += "BARCODE 128 1 2 40 10 240 ${cpclText(barcode)}"
    }

    commands += "PRINT"
    return commands.joinToString("\r\n", postfix = "\r\n")
  }

  private fun ReadableMap.getNullableString(key: String): String {
    return if (hasKey(key) && !isNull(key)) getString(key)?.trim().orEmpty() else ""
  }

  private fun ReadableMap.getNullableDouble(key: String): Double? {
    return if (hasKey(key) && !isNull(key)) getDouble(key) else null
  }

  private fun formatPriceParts(value: Double?): PriceParts {
    val safeValue = value ?: 0.0
    val cents = (safeValue * 100).roundToInt()
    val integer = cents / 100
    val decimal = (cents % 100).toString().padStart(2, '0')
    return PriceParts(integer.toString(), decimal)
  }

  private fun todayString(pattern: String = "yyyy/MM/dd"): String {
    return SimpleDateFormat(pattern, Locale.US).format(Date())
  }

  private fun formatMoney(value: Double): String {
    return String.format(Locale.US, "%.2f", value)
  }

  private fun formatOptionalMoney(value: Double?): String {
    return if (value == null) "--" else formatMoney(value)
  }

  private fun cpclText(value: String): String {
    return value.replace(Regex("[\\r\\n]+"), " ").trim()
  }

  private fun fontSizeToPixels(fontSize: Float): Float {
    return fontSize * 3f
  }

  private fun textToBitmap(
    text: String,
    fontSize: Float,
    isBold: Boolean,
    fontFamily: String,
    isInverse: Boolean = false,
    padding: Int = 0,
    strikeThrough: Boolean = false,
  ): Bitmap {
    val safeText = text.ifBlank { " " }
    val paint = Paint().apply {
      isAntiAlias = true
      color = if (isInverse) Color.WHITE else Color.BLACK
      textSize = fontSize
      textAlign = Paint.Align.LEFT
      typeface = Typeface.create(fontFamily, if (isBold) Typeface.BOLD else Typeface.NORMAL)
      if (strikeThrough) {
        flags = flags or Paint.STRIKE_THRU_TEXT_FLAG
      }
    }
    val bounds = android.graphics.Rect()
    paint.getTextBounds(safeText, 0, safeText.length, bounds)
    val measuredWidth = paint.measureText(safeText)
    val width = max(1, ceil(max(measuredWidth, bounds.width().toFloat())).toInt() + padding * 2)
    val height = max(1, bounds.height() + padding * 2)
    val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    canvas.drawColor(if (isInverse) Color.BLACK else Color.WHITE)
    canvas.drawText(safeText, padding.toFloat(), (height - bounds.bottom - padding).toFloat(), paint)
    return bitmap
  }

  private fun longTextToBitmap(
    text: String,
    fontSize: Float,
    isBold: Boolean,
    fontFamily: String,
    maxLines: Int,
    maxWidth: Int,
  ): Bitmap {
    val safeText = text.ifBlank { " " }
    val paint = Paint().apply {
      isAntiAlias = true
      color = Color.BLACK
      textSize = fontSize
      textAlign = Paint.Align.LEFT
      typeface = Typeface.create(fontFamily, if (isBold) Typeface.BOLD else Typeface.NORMAL)
    }
    val lines = wrapText(safeText, paint, maxWidth, maxLines)
    val metrics = paint.fontMetrics
    val lineHeight = ceil(metrics.descent - metrics.ascent).toInt()
    val width = max(1, minOf(maxWidth, ceil(lines.maxOf { paint.measureText(it) }).toInt()))
    val height = max(1, lineHeight * lines.size)
    val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    canvas.drawColor(Color.WHITE)
    lines.forEachIndexed { index, line ->
      canvas.drawText(line, 0f, index * lineHeight - metrics.ascent, paint)
    }
    return bitmap
  }

  private fun wrapText(text: String, paint: Paint, maxWidth: Int, maxLines: Int): List<String> {
    val lines = mutableListOf<String>()
    var current = StringBuilder()
    text.forEach { char ->
      val next = current.toString() + char
      if (current.isNotEmpty() && paint.measureText(next) > maxWidth && lines.size < maxLines - 1) {
        lines += current.toString()
        current = StringBuilder(char.toString())
      } else {
        current.append(char)
      }
    }
    if (current.isNotEmpty() || lines.isEmpty()) {
      lines += current.toString()
    }
    return lines.take(maxLines)
  }

  private fun processCapitalization(value: String): String {
    return value
      .lowercase(Locale.US)
      .split(Regex("\\s+"))
      .filter { it.isNotBlank() }
      .joinToString(" ") { word -> word.replaceFirstChar { it.titlecase(Locale.US) } }
  }

  private fun formatSupplierAbbreviation(value: String): String {
    val words = value
      .lowercase(Locale.US)
      .split(Regex("\\s+"))
      .filter { it.isNotBlank() }
      .map { word -> word.replaceFirstChar { it.titlecase(Locale.US) } }

    if (words.isEmpty()) {
      return ""
    }

    if (words.size == 1) {
      return words.first().take(3).uppercase(Locale.US)
    }

    return words
      .take(4)
      .map { it.first().uppercaseChar() }
      .joinToString(".")
  }

  private fun bitmapCommand(x: Int, y: Int, bitmap: Bitmap): String {
    val widthBytes = (bitmap.width + 7) / 8
    return "EG $widthBytes ${bitmap.height} $x $y ${bitmapToHex(bitmap, widthBytes)}"
  }

  private fun createQrCodeBitmap(value: String, size: Int): Bitmap {
    val hints = EnumMap<EncodeHintType, Any>(EncodeHintType::class.java).apply {
      put(EncodeHintType.MARGIN, 0)
      put(EncodeHintType.CHARACTER_SET, "UTF-8")
    }
    val matrix = MultiFormatWriter().encode(
      value,
      BarcodeFormat.QR_CODE,
      size,
      size,
      hints,
    )
    val bitmap = Bitmap.createBitmap(matrix.width, matrix.height, Bitmap.Config.ARGB_8888)
    for (y in 0 until matrix.height) {
      for (x in 0 until matrix.width) {
        bitmap.setPixel(x, y, if (matrix.get(x, y)) Color.BLACK else Color.WHITE)
      }
    }
    return bitmap
  }

  private fun createDashLineBitmap(width: Int, height: Int): Bitmap {
    val bitmap = Bitmap.createBitmap(max(1, width), max(1, height), Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    canvas.drawColor(Color.WHITE)
    val paint = Paint().apply {
      color = Color.BLACK
      strokeWidth = height.toFloat()
    }
    var x = 0f
    while (x < width) {
      canvas.drawLine(x, height / 2f, minOf(x + 10f, width.toFloat()), height / 2f, paint)
      x += 18f
    }
    return bitmap
  }

  private fun bitmapToHex(bitmap: Bitmap, widthBytes: Int): String {
    val hex = StringBuilder(widthBytes * bitmap.height * 2)
    for (y in 0 until bitmap.height) {
      for (byteIndex in 0 until widthBytes) {
        var value = 0
        for (bit in 0 until 8) {
          val x = byteIndex * 8 + bit
          if (x < bitmap.width && isBlack(bitmap.getPixel(x, y))) {
            value = value or (1 shl (7 - bit))
          }
        }
        hex.append(value.toString(16).padStart(2, '0').uppercase(Locale.US))
      }
    }
    return hex.toString()
  }

  private fun isBlack(pixel: Int): Boolean {
    val alpha = Color.alpha(pixel)
    if (alpha == 0) {
      return false
    }
    val luminance = (Color.red(pixel) * 299 + Color.green(pixel) * 587 + Color.blue(pixel) * 114) / 1000
    return luminance < 200
  }

  private fun disconnectInternal() {
    try {
      socket?.close()
    } catch (_: Exception) {
    } finally {
      socket = null
      connectedAddress = null
    }
  }

  data class WritablePrinterDevice(
    val name: String?,
    val address: String,
    val bonded: Boolean,
    val connected: Boolean,
  )

  data class PriceParts(
    val integer: String,
    val decimal: String,
  )
}
