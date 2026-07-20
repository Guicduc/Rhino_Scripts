using System;

namespace ThreadRhino
{
    internal static class ThreadMath
    {
        public const double ExternalDepthFactor = 0.613434661;
        public const double InternalDepthFactor = 0.541265877;
        public const double InternalMinorDiameterFactor = 1.082531755;
        public const double ExternalMinorDiameterFactor = 1.226869322;

        public static double FundamentalTriangleHeight(double pitch)
        {
            return Math.Sqrt(3.0) * pitch * 0.5;
        }

        public static double ExternalThreadDepth(double pitch)
        {
            return ExternalDepthFactor * pitch;
        }

        public static double InternalThreadDepth(double pitch)
        {
            return InternalDepthFactor * pitch;
        }

        public static double BasicInternalMinorDiameter(double nominalDiameter, double pitch)
        {
            return nominalDiameter - InternalMinorDiameterFactor * pitch;
        }

        public static double BasicExternalMinorDiameter(double nominalDiameter, double pitch)
        {
            return nominalDiameter - ExternalMinorDiameterFactor * pitch;
        }

        public static double ExternalCrestOpening(double pitch)
        {
            return pitch * 7.0 / 8.0;
        }

        public static double ExternalRootFlat(double pitch)
        {
            return pitch / 6.0;
        }

        public static double InternalMinorOpening(double pitch)
        {
            return pitch * 3.0 / 4.0;
        }

        public static double InternalRootFlat(double pitch)
        {
            return pitch / 8.0;
        }
    }
}
