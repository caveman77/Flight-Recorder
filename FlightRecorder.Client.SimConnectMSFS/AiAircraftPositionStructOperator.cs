namespace FlightRecorder.Client
{

    public partial class AiAircraftPositionStructOperator
    {
        public static partial AiAircraftPositionSetStruct ToSet(AiAircraftPositionStruct variables);

        public static double InterpolateWrap(double x1, double x2, double interpolation, double min, double max)
        {
            var diff = x1 - x2;
            if (diff < 0) diff = -diff;

            var range = max - min;
            var wrapDiff = range - diff;

            if (wrapDiff < diff)
            {
                // Wrap
                if (x1 > x2)
                {
                    x2 += range;
                }
                else if (x1 < x2)
                {
                    x2 -= range;
                }

                var value = x1 * interpolation + x2 * (1 - interpolation);

                if (value < min)
                {
                    value += range;
                }
                else if (value > max)
                {
                    value -= range;
                }

                return value;
            }
            else
            {
                return x1 * interpolation + x2 * (1 - interpolation);
            }
        }

    }
}
