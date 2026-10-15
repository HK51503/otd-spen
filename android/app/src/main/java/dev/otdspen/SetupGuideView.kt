package dev.otdspen

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.graphics.Color
import android.provider.Settings
import android.util.TypedValue
import android.view.Gravity
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

/**
 * Step-by-step instructions for enabling USB debugging, shown over the area while something's missing.
 */
class SetupGuideView(context: Context) : ScrollView(context) {
    private val density = resources.displayMetrics.density

    private val developerStep = Step(
        "Turn on Developer options",
        "Settings → About phone → Software information → tap \"Build number\" 7 times.",
        "Open Software information",
        Settings.ACTION_DEVICE_INFO_SETTINGS,
    )
    private val usbDebuggingStep = Step(
        "Turn on USB debugging",
        "Settings → Developer options → USB debugging.\n" +
            "Also turn on \"Disable adb authorization timeout\" so you don't have to re-approve your PC every week.",
        "Open Developer options",
        Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS,
    )
    private val connectStep = Step(
        "Connect to your PC",
        "Plug the phone into the computer running OpenTabletDriver with a USB data cable.\n" +
            "When the phone asks \"Allow USB debugging?\", tick \"Always allow from this computer\" and tap Allow.",
        null,
        null,
    )

    init {
        setBackgroundColor(Color.argb(235, 0, 0, 0))
        isFillViewport = true

        val column = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_VERTICAL
            val pad = dp(32)
            setPadding(pad, pad, pad, pad)
        }
        column.addView(text("Set up USB debugging", 22f, Color.WHITE))
        column.addView(text("otd-spen needs USB debugging so OpenTabletDriver can read the S Pen over the cable.", 14f, GREY).apply {
            setPadding(0, dp(4), 0, dp(16))
        })
        for (step in listOf(developerStep, usbDebuggingStep, connectStep))
            column.addView(step.view)
        addView(column)
    }

    fun update(state: UsbDebuggingState) {
        developerStep.done = state.developerOptions
        usbDebuggingStep.done = state.usbDebugging
        connectStep.done = state.usbConnected && state.adbOverUsb != false
        connectStep.note = when {
            state.usbConnected && state.adbOverUsb == false ->
                "USB is connected, but debugging isn't active on it. Try another cable or port, or set the USB mode to \"File transfer\"."
            else -> null
        }
    }

    private inner class Step(title: String, body: String, buttonText: String?, action: String?) {
        private val titleView = text("", 17f, Color.WHITE)
        private val noteView = text("", 14f, AMBER).apply { visibility = GONE }
        private val title = title

        val view = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(8), 0, dp(8))
            addView(titleView)
            addView(text(body, 14f, GREY).apply { setPadding(dp(28), dp(2), 0, 0) })
            addView(noteView.apply { setPadding(dp(28), dp(2), 0, 0) })
            if (buttonText != null && action != null) {
                addView(Button(context).apply {
                    text = buttonText
                    setOnClickListener { openSettings(action) }
                }, LinearLayout.LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT).apply {
                    marginStart = dp(24)
                })
            }
        }

        var done: Boolean = false
            set(value) {
                field = value
                titleView.text = (if (value) "✓  " else "•  ") + title
                titleView.setTextColor(if (value) GREEN else Color.WHITE)
            }

        var note: String? = null
            set(value) {
                field = value
                noteView.text = value ?: ""
                noteView.visibility = if (value == null) GONE else VISIBLE
            }

        init {
            done = false
        }
    }

    private fun openSettings(action: String) {
        val intent = Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try {
            context.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            // Developer options can't be opened before they're enabled; fall back to the main settings.
            context.startActivity(Intent(Settings.ACTION_SETTINGS).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    private fun text(value: String, sizeSp: Float, color: Int) = TextView(context).apply {
        text = value
        setTextSize(TypedValue.COMPLEX_UNIT_SP, sizeSp)
        setTextColor(color)
    }

    private fun dp(value: Int) = (value * density).toInt()

    private companion object {
        val GREY = Color.rgb(170, 170, 170)
        val GREEN = Color.rgb(102, 187, 106)
        val AMBER = Color.rgb(255, 202, 40)
    }
}
