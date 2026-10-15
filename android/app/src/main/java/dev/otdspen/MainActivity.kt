package dev.otdspen

import android.app.Activity
import android.content.SharedPreferences
import android.content.pm.ActivityInfo
import android.os.Bundle
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.view.WindowManager

/**
 * A black, full-screen surface for the S Pen: swallows all input so nothing on the phone reacts,
 * keeps the screen on at the highest refresh rate, and draws OTD's area.
 */
class MainActivity : Activity() {
    private lateinit var view: AreaView
    private lateinit var prefs: SharedPreferences

    private val areaListener = SharedPreferences.OnSharedPreferenceChangeListener { p, _ ->
        showArea(Area.load(p))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        window.attributes = window.attributes.apply {
            // Draw under the camera cutout so the view covers the whole digitizer.
            layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS
            preferredDisplayModeId = fastestDisplayMode()
        }
        window.setDecorFitsSystemWindows(false)

        view = AreaView(this)
        setContentView(view)

        prefs = Area.prefs(this)
        showArea(Area.load(prefs))
        prefs.registerOnSharedPreferenceChangeListener(areaListener)
    }

    /**
     * Shows the area and orients the screen the way the phone is held, as told by the area's rotation:
     * around 0° means the phone's top edge points right (the plugin's frame), around 180° the other way round.
     */
    private fun showArea(area: Area?) {
        val rotation = ((area?.rotation ?: 0f) % 360f + 360f) % 360f
        requestedOrientation = if (rotation > 90f && rotation < 270f)
            ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE
        else
            ActivityInfo.SCREEN_ORIENTATION_REVERSE_LANDSCAPE
        view.area = area
    }

    override fun onResume() {
        super.onResume()
        hideSystemBars()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus)
            hideSystemBars()
    }

    override fun onDestroy() {
        prefs.unregisterOnSharedPreferenceChangeListener(areaListener)
        super.onDestroy()
    }

    private fun hideSystemBars() {
        window.insetsController?.apply {
            hide(WindowInsets.Type.systemBars())
            systemBarsBehavior = WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    /**
     * Highest refresh rate at the current resolution. The digitizer seems to follow the display's
     * adaptive refresh, so keeping the panel fast may avoid gaps in the pen stream.
     */
    private fun fastestDisplayMode(): Int {
        val display = display ?: return 0
        val current = display.mode
        return display.supportedModes
            .filter { it.physicalWidth == current.physicalWidth && it.physicalHeight == current.physicalHeight }
            .maxByOrNull { it.refreshRate }
            ?.modeId ?: 0
    }
}
