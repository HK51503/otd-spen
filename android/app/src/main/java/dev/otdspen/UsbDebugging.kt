package dev.otdspen

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.database.ContentObserver
import android.os.BatteryManager
import android.os.Handler
import android.os.Looper
import android.provider.Settings

/**
 * What the phone needs before the OTD plugin can reach it over adb.
 */
data class UsbDebuggingState(
    val developerOptions: Boolean,
    val usbDebugging: Boolean,
    val usbConnected: Boolean,
    /** USB is connected with the adb function active (null if the system doesn't say). */
    val adbOverUsb: Boolean?,
) {
    val ready: Boolean get() = developerOptions && usbDebugging && usbConnected && adbOverUsb != false
}

/**
 * Watches developer options, the USB debugging switch and the USB connection, and reports changes.
 */
class UsbDebuggingMonitor(private val context: Context, private val onChange: (UsbDebuggingState) -> Unit) {
    private companion object {
        // UsbManager.ACTION_USB_STATE and its extras are @hide, but the sticky broadcast is readable by apps.
        const val ACTION_USB_STATE = "android.hardware.usb.action.USB_STATE"
        const val EXTRA_CONNECTED = "connected"
        const val EXTRA_ADB = "adb"
    }

    private val handler = Handler(Looper.getMainLooper())
    private var usbState: Intent? = null

    private val usbReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action == ACTION_USB_STATE)
                usbState = intent
            publish()
        }
    }

    private val settingsObserver = object : ContentObserver(handler) {
        override fun onChange(selfChange: Boolean) = publish()
    }

    fun start() {
        val filter = IntentFilter().apply {
            addAction(ACTION_USB_STATE)
            addAction(Intent.ACTION_POWER_CONNECTED)
            addAction(Intent.ACTION_POWER_DISCONNECTED)
        }
        usbState = context.registerReceiver(usbReceiver, filter, Context.RECEIVER_EXPORTED)
        for (setting in listOf(Settings.Global.ADB_ENABLED, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED))
            context.contentResolver.registerContentObserver(Settings.Global.getUriFor(setting), false, settingsObserver)
        publish()
    }

    fun stop() {
        context.unregisterReceiver(usbReceiver)
        context.contentResolver.unregisterContentObserver(settingsObserver)
    }

    fun current(): UsbDebuggingState {
        val resolver = context.contentResolver
        val usb = usbState
        val connected = usb?.getBooleanExtra(EXTRA_CONNECTED, false) ?: pluggedIntoUsb()
        return UsbDebuggingState(
            developerOptions = Settings.Global.getInt(resolver, Settings.Global.DEVELOPMENT_SETTINGS_ENABLED, 0) != 0,
            usbDebugging = Settings.Global.getInt(resolver, Settings.Global.ADB_ENABLED, 0) != 0,
            usbConnected = connected,
            adbOverUsb = if (usb != null && connected && usb.hasExtra(EXTRA_ADB)) usb.getBooleanExtra(EXTRA_ADB, false) else null,
        )
    }

    /** Fallback when there's no USB_STATE broadcast: charging from a USB port. */
    private fun pluggedIntoUsb(): Boolean {
        val battery = context.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        return battery?.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) == BatteryManager.BATTERY_PLUGGED_USB
    }

    private fun publish() = onChange(current())
}
