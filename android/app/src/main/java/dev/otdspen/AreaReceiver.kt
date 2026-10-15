package dev.otdspen

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent

/**
 * Receives the area from the OTD plugin:
 *
 *   adb shell am broadcast -a dev.otdspen.SET_AREA -n dev.otdspen/.AreaReceiver \
 *       --ez enabled true --ef tabletWidth 156 --ef tabletHeight 73 --ef width 112 --ef height 73 \
 *       --ef x 78 --ef y 36.5 --ef rotation 0
 *
 * The area is persisted, and MainActivity redraws via its SharedPreferences listener.
 */
class AreaReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val values = Area.extraKeys
            .filter { intent.hasExtra(it) }
            .associateWith { intent.getFloatExtra(it, 0f) }
        Area.save(Area.prefs(context), intent.getBooleanExtra("enabled", false), values)
    }
}
