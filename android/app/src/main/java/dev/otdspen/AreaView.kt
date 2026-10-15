package dev.otdspen

import android.content.Context
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.view.MotionEvent
import android.view.Surface
import android.view.View

/**
 * Draws OTD's area and a dot under the pen, and consumes every touch so the phone ignores it.
 */
class AreaView(context: Context) : View(context) {
    var area: Area? = null
        set(value) {
            field = value
            invalidate()
        }

    private val density = resources.displayMetrics.density

    private val areaFill = Paint().apply { color = Color.rgb(16, 20, 26) }
    private val areaStroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeWidth = 2 * density
        color = Color.rgb(79, 195, 247)
    }
    private val penPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        strokeWidth = 2 * density
        color = Color.rgb(255, 202, 40)
    }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textSize = 14 * density
        color = Color.rgb(120, 120, 120)
    }

    private val path = Path()
    private val matrix = Matrix()

    private var penX = 0f
    private var penY = 0f
    private var penState = PenState.Away

    private enum class PenState { Away, Hovering, Touching }

    init {
        setBackgroundColor(Color.BLACK)
        // Pen hover events only reach views that can take focus or have a hover listener.
        isFocusable = true
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)

        val area = area
        if (area != null) {
            path.reset()
            path.addRect(-area.width / 2, -area.height / 2, area.width / 2, area.height / 2, Path.Direction.CW)
            path.transform(areaToView(area))
            canvas.drawPath(path, areaFill)
            canvas.drawPath(path, areaStroke)
        }

        when (penState) {
            PenState.Hovering -> {
                penPaint.style = Paint.Style.STROKE
                canvas.drawCircle(penX, penY, 6 * density, penPaint)
            }
            PenState.Touching -> {
                penPaint.style = Paint.Style.FILL
                canvas.drawCircle(penX, penY, 6 * density, penPaint)
            }
            PenState.Away -> {}
        }

        val status = if (area == null)
            "otd-spen · waiting for the area from OpenTabletDriver (press Apply in OTD)"
        else
            "otd-spen · area %.1f × %.1f mm · %.0f°".format(area.width, area.height, area.rotation)
        canvas.drawText(status, 16 * density, height - 16 * density, textPaint)
    }

    /**
     * Maps tablet mm (as OTD sees it) to view pixels. The plugin's tablet frame is the screen in
     * ROTATION_270 (phone top pointing right); in ROTATION_90 the same digitizer is upside down.
     */
    private fun areaToView(area: Area): Matrix {
        matrix.reset()
        matrix.setRotate(area.rotation)
        matrix.postTranslate(area.x, area.y)
        matrix.postScale(width / area.tabletWidth, height / area.tabletHeight)
        if (display?.rotation == Surface.ROTATION_90)
            matrix.postRotate(180f, width / 2f, height / 2f)
        return matrix
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (isPen(event)) {
            penState = when (event.actionMasked) {
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> PenState.Hovering
                else -> PenState.Touching
            }
            movePen(event)
        }
        return true
    }

    override fun onHoverEvent(event: MotionEvent): Boolean {
        if (isPen(event)) {
            penState = if (event.actionMasked == MotionEvent.ACTION_HOVER_EXIT) PenState.Away else PenState.Hovering
            movePen(event)
        }
        return true
    }

    override fun onGenericMotionEvent(event: MotionEvent): Boolean = true

    private fun movePen(event: MotionEvent) {
        penX = event.x
        penY = event.y
        invalidate()
    }

    private fun isPen(event: MotionEvent): Boolean {
        val tool = event.getToolType(0)
        return tool == MotionEvent.TOOL_TYPE_STYLUS || tool == MotionEvent.TOOL_TYPE_ERASER
    }

    override fun onLayout(changed: Boolean, left: Int, top: Int, right: Int, bottom: Int) {
        super.onLayout(changed, left, top, right, bottom)
        // Ask the system not to treat edge swipes as back/home gestures (it caps how much it honours).
        systemGestureExclusionRects = listOf(Rect(0, 0, width, height))
    }

    override fun onConfigurationChanged(newConfig: Configuration?) {
        super.onConfigurationChanged(newConfig)
        invalidate()
    }
}
