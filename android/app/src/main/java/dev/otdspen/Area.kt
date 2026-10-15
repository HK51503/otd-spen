package dev.otdspen

import android.content.Context
import android.content.SharedPreferences

/**
 * OpenTabletDriver's tablet area, in mm of the tablet as the OTD plugin presents it
 * (landscape, origin top-left with the phone's top edge pointing right).
 */
data class Area(
    val tabletWidth: Float,
    val tabletHeight: Float,
    val width: Float,
    val height: Float,
    /** Centre of the area. */
    val x: Float,
    val y: Float,
    /** Degrees, clockwise. */
    val rotation: Float,
) {
    companion object {
        private const val PREFS = "area"
        private const val ENABLED = "enabled"
        private val KEYS = listOf("tabletWidth", "tabletHeight", "width", "height", "x", "y", "rotation")

        fun prefs(context: Context): SharedPreferences =
            context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

        fun load(prefs: SharedPreferences): Area? {
            if (!prefs.getBoolean(ENABLED, false) || KEYS.any { !prefs.contains(it) })
                return null
            val v = KEYS.map { prefs.getFloat(it, 0f) }
            return Area(v[0], v[1], v[2], v[3], v[4], v[5], v[6])
        }

        /** Stores the extras of a SET_AREA broadcast. */
        fun save(prefs: SharedPreferences, enabled: Boolean, values: Map<String, Float>) {
            prefs.edit().apply {
                putBoolean(ENABLED, enabled && KEYS.all { it in values })
                for (key in KEYS)
                    values[key]?.let { putFloat(key, it) }
            }.apply()
        }

        val extraKeys: List<String> get() = KEYS
    }
}
