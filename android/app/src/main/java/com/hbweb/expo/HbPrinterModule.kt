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
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import java.nio.charset.Charset
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean

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
        val activeSocket = socket
        if (activeSocket == null || !activeSocket.isConnected) {
          promise.reject("PRINT_NOT_CONNECTED", "No Bluetooth printer is connected.")
          return@Thread
        }

        val charset = Charset.forName(encoding ?: "GB18030")
        val bytes = command.toByteArray(charset)
        val outputStream = activeSocket.outputStream
        outputStream.write(bytes)
        outputStream.flush()
        promise.resolve(true)
      } catch (error: Exception) {
        promise.reject("PRINT_ERROR", error.message, error)
      }
    }.start()
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
}
