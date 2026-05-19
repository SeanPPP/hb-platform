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
import java.nio.charset.Charset
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean
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
  fun printProductLabel(payload: ReadableMap, promise: Promise) {
    Thread {
      try {
        val command = buildProductLabelCommand(payload)
        writePrinterCommand(command, "GB18030")
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_PRODUCT_LABEL_ERROR", error.message, error)
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

  private fun buildProductLabelCommand(payload: ReadableMap): String {
    val productName = payload.getNullableString("productName")
    val itemNumber = payload.getNullableString("itemNumber")
    val supplierName = processCapitalization(payload.getNullableString("supplierName"))
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
    val dateBitmap = textToBitmap(todayString(), fontSizeToPixels(8f), false, "Arial", true, 2)
    val nameMaxWidth = max(
      1,
      labelWidth - priceDecimalBitmap.width - priceDotBitmap.width - priceIntegerBitmap.width - priceCurrencyBitmap.width,
    )
    val nameBitmap = longTextToBitmap(productName, fontSizeToPixels(10f), false, "Arial", 2, nameMaxWidth)
    val discountBitmap = if (discountRate > 0) {
      textToBitmap("${(discountRate * 100).roundToInt().toString().padStart(2, '0')}%OFF", fontSizeToPixels(8f), false, "sans-serif-light", true, 2)
    } else {
      null
    }
    val gradeBitmap = grade?.let {
      textToBitmap(it, fontSizeToPixels(8f), true, "sans-serif-black", true, 4)
    }

    val startY = 30
    val startX = labelWidth - priceDecimalBitmap.width
    val commands = mutableListOf(
      "! 0 200 200 $labelHeight 1",
      "PAGE-WIDTH $labelWidth",
      bitmapCommand(5, 5, nameBitmap),
      bitmapCommand(5, 120, itemBitmap),
      bitmapCommand(5 + itemBitmap.width + 10, 118, supplierBitmap),
    )

    if (barcode.isNotBlank()) {
      commands += "BARCODE-TEXT 7 0 5"
      commands += "BARCODE 128 1 2 30 5 145 $barcode"
    }

    if (discountBitmap != null) {
      commands += bitmapCommand(labelWidth - discountBitmap.width - dateBitmap.width - 20, 175, discountBitmap)
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
    commands += bitmapCommand(labelWidth - dateBitmap.width, 175, dateBitmap)
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

  private fun todayString(): String {
    return SimpleDateFormat("yyyy/MM/dd", Locale.US).format(Date())
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
  ): Bitmap {
    val safeText = text.ifBlank { " " }
    val paint = Paint().apply {
      isAntiAlias = true
      color = if (isInverse) Color.WHITE else Color.BLACK
      textSize = fontSize
      textAlign = Paint.Align.LEFT
      typeface = Typeface.create(fontFamily, if (isBold) Typeface.BOLD else Typeface.NORMAL)
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

  private fun bitmapCommand(x: Int, y: Int, bitmap: Bitmap): String {
    val widthBytes = (bitmap.width + 7) / 8
    return "EG $widthBytes ${bitmap.height} $x $y ${bitmapToHex(bitmap, widthBytes)}"
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
