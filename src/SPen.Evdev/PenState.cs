using System;

namespace SPen.Evdev
{
    [Flags]
    public enum PenButtons
    {
        None = 0,
        Stylus = 1 << 0,
        Stylus2 = 1 << 1,
    }

    /// <summary>
    /// Snapshot of the pen after a SYN_REPORT, in raw digitizer units.
    /// </summary>
    public struct PenState
    {
        public long TimestampMicros;
        public int X;
        public int Y;
        public int Pressure;
        public int Distance;
        public int TiltX;
        public int TiltY;

        /// <summary>Pen is within hover range (BTN_TOOL_PEN or BTN_TOOL_RUBBER held).</summary>
        public bool InRange;
        public bool Touching;
        public bool Eraser;
        public PenButtons Buttons;

        public override string ToString() =>
            $"{(InRange ? (Touching ? "DOWN " : "HOVER") : "OUT  ")} x={X,6} y={Y,6} p={Pressure,5} d={Distance,3} " +
            $"tilt=({TiltX,4},{TiltY,4}) btn={Buttons}{(Eraser ? " eraser" : "")}";
    }

    /// <summary>
    /// Folds a stream of evdev events into <see cref="PenState"/> frames.
    /// </summary>
    public sealed class PenStateAccumulator
    {
        private PenState _state;
        private bool _dropping;

        // Reading the node with `cat` gives us no initial key state (no EVIOCGKEY), so if the stream
        // starts while the pen is already in range/touching we never see the BTN_TOOL_PEN/BTN_TOUCH
        // press. Until those keys are seen, infer them from ABS activity instead.
        private bool _toolKeySeen;
        private bool _touchKeySeen;

        public PenState State => _state;

        /// <summary>Number of SYN_DROPPED events seen (kernel buffer overruns).</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Raised for events this accumulator doesn't understand, so callers can log them.</summary>
        public event Action<InputEvent>? UnhandledEvent;

        /// <summary>
        /// Applies one event. Returns true when a complete frame (SYN_REPORT) is available in <see cref="State"/>.
        /// </summary>
        public bool Process(in InputEvent ev)
        {
            if (ev.Type == EvdevCodes.EV_SYN)
            {
                if (ev.Code == EvdevCodes.SYN_DROPPED)
                {
                    // Discard everything up to and including the next SYN_REPORT.
                    // We can't EVIOCGABS through `cat`, so we just resume with the next full frame.
                    _dropping = true;
                    DroppedCount++;
                    return false;
                }

                if (ev.Code == EvdevCodes.SYN_REPORT)
                {
                    if (_dropping)
                    {
                        _dropping = false;
                        return false;
                    }

                    _state.TimestampMicros = ev.TimestampMicros;
                    return true;
                }

                return false;
            }

            if (_dropping)
                return false;

            switch (ev.Type)
            {
                case EvdevCodes.EV_ABS:
                    switch (ev.Code)
                    {
                        case EvdevCodes.ABS_X:
                            _state.X = ev.Value;
                            InferInRange();
                            return false;
                        case EvdevCodes.ABS_Y:
                            _state.Y = ev.Value;
                            InferInRange();
                            return false;
                        case EvdevCodes.ABS_PRESSURE:
                            _state.Pressure = ev.Value;
                            InferInRange();
                            if (!_touchKeySeen)
                                _state.Touching = ev.Value > 0;
                            return false;
                        case EvdevCodes.ABS_DISTANCE: _state.Distance = ev.Value; return false;
                        case EvdevCodes.ABS_TILT_X: _state.TiltX = ev.Value; return false;
                        case EvdevCodes.ABS_TILT_Y: _state.TiltY = ev.Value; return false;
                    }
                    break;

                case EvdevCodes.EV_KEY:
                    bool down = ev.Value != 0;
                    switch (ev.Code)
                    {
                        case EvdevCodes.BTN_TOOL_PEN:
                            SetInRange(down, eraser: false);
                            return false;
                        case EvdevCodes.BTN_TOOL_RUBBER:
                            SetInRange(down, eraser: down);
                            return false;
                        case EvdevCodes.BTN_TOUCH:
                            _touchKeySeen = true;
                            _state.Touching = down;
                            return false;
                        case EvdevCodes.BTN_STYLUS:
                            SetButton(PenButtons.Stylus, down);
                            return false;
                        case EvdevCodes.BTN_STYLUS2:
                            SetButton(PenButtons.Stylus2, down);
                            return false;
                    }
                    break;
            }

            UnhandledEvent?.Invoke(ev);
            return false;
        }

        private void InferInRange()
        {
            if (!_toolKeySeen)
                _state.InRange = true;
        }

        private void SetInRange(bool inRange, bool eraser)
        {
            _toolKeySeen = true;
            _state.InRange = inRange;
            _state.Eraser = eraser;
            if (!inRange)
            {
                // The driver doesn't always zero these before releasing the tool.
                _state.Pressure = 0;
                _state.Distance = 0;
                _state.Touching = false;
            }
        }

        private void SetButton(PenButtons button, bool down)
        {
            if (down)
                _state.Buttons |= button;
            else
                _state.Buttons &= ~button;
        }
    }
}
