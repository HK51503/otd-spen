using SPen.Evdev;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Presents the digitizer to OTD in landscape, so a 0° area in OTD is the phone held sideways.
    /// </summary>
    /// <remarks>
    /// Phone digitizers report in the display's natural (portrait) orientation with the origin at the top-left.
    /// We rotate into Android's ROTATION_270 landscape: the phone's top edge points right. Holding it the other way
    /// round is a 180° area rotation in OTD.
    /// </remarks>
    public static class Landscape
    {
        public static int MaxX(SPenDevice device) => device.MaxY;
        public static int MaxY(SPenDevice device) => device.MaxX;
        public static double WidthMm(SPenDevice device) => device.HeightMm;
        public static double HeightMm(SPenDevice device) => device.WidthMm;

        public static PenState Rotate(PenState state, SPenDevice device)
        {
            int x = state.X, y = state.Y;
            int tiltX = state.TiltX, tiltY = state.TiltY;

            state.X = device.MaxY - y;
            state.Y = x;
            state.TiltX = -tiltY;
            state.TiltY = tiltX;
            return state;
        }
    }
}
