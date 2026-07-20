using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreadRhino
{
    internal sealed class ThreadCatalogEntry
    {
        public string Name { get; private set; }
        public double DiameterMm { get; private set; }
        public IReadOnlyList<double> PitchesMm { get; private set; }

        public ThreadCatalogEntry(string name, double diameterMm, params double[] pitchesMm)
        {
            Name = name;
            DiameterMm = diameterMm;
            PitchesMm = Array.AsReadOnly(pitchesMm);
        }
    }

    internal static class ThreadCatalog
    {
        private static readonly List<ThreadCatalogEntry> EntriesInternal = new List<ThreadCatalogEntry>
        {
            new ThreadCatalogEntry("M2", 2.0, 0.40, 0.25),
            new ThreadCatalogEntry("M2.5", 2.5, 0.45, 0.35),
            new ThreadCatalogEntry("M3", 3.0, 0.50, 0.35),
            new ThreadCatalogEntry("M4", 4.0, 0.70, 0.50),
            new ThreadCatalogEntry("M5", 5.0, 0.80, 0.50),
            new ThreadCatalogEntry("M6", 6.0, 1.00, 0.75, 0.50),
            new ThreadCatalogEntry("M8", 8.0, 1.25, 1.00, 0.75),
            new ThreadCatalogEntry("M10", 10.0, 1.50, 1.25, 1.00, 0.75),
            new ThreadCatalogEntry("M12", 12.0, 1.75, 1.50, 1.25, 1.00),
            new ThreadCatalogEntry("M14", 14.0, 2.00, 1.50, 1.25, 1.00),
            new ThreadCatalogEntry("M16", 16.0, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M18", 18.0, 2.50, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M20", 20.0, 2.50, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M22", 22.0, 2.50, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M24", 24.0, 3.00, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M27", 27.0, 3.00, 2.00, 1.50, 1.00),
            new ThreadCatalogEntry("M30", 30.0, 3.50, 3.00, 2.00, 1.50, 1.00),
        };

        public static IReadOnlyList<ThreadCatalogEntry> Entries
        {
            get { return EntriesInternal.AsReadOnly(); }
        }

        public static ThreadCatalogEntry Find(string name)
        {
            return EntriesInternal.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static ThreadCatalogEntry FindClosest(double measuredDiameterMm, bool isInternal)
        {
            ThreadCatalogEntry bestEntry = null;
            double bestError = double.MaxValue;

            foreach (var entry in EntriesInternal)
            {
                foreach (var pitch in entry.PitchesMm)
                {
                    var expected = isInternal
                        ? ThreadMath.BasicInternalMinorDiameter(entry.DiameterMm, pitch)
                        : entry.DiameterMm;
                    var error = Math.Abs(expected - measuredDiameterMm);
                    if (error < bestError)
                    {
                        bestError = error;
                        bestEntry = entry;
                    }
                }
            }

            return bestEntry ?? EntriesInternal[0];
        }

        public static double FindClosestPitch(ThreadCatalogEntry entry, double measuredDiameterMm, bool isInternal)
        {
            if (entry == null || entry.PitchesMm.Count == 0)
                return 1.0;
            if (!isInternal)
                return entry.PitchesMm[0];

            return entry.PitchesMm
                .OrderBy(p => Math.Abs(ThreadMath.BasicInternalMinorDiameter(entry.DiameterMm, p) - measuredDiameterMm))
                .First();
        }

        public static IReadOnlyList<double> FindCompatiblePitches(
            ThreadCatalogEntry entry,
            double measuredDiameterMm,
            bool isInternal,
            double toleranceMm)
        {
            if (entry == null)
                return Array.AsReadOnly(new double[0]);

            var safeTolerance = Math.Max(0.0, toleranceMm);
            var compatible = entry.PitchesMm
                .Where(pitch =>
                {
                    var expectedDiameter = isInternal
                        ? ThreadMath.BasicInternalMinorDiameter(entry.DiameterMm, pitch)
                        : entry.DiameterMm;
                    return Math.Abs(expectedDiameter - measuredDiameterMm) <= safeTolerance;
                })
                .ToArray();
            return Array.AsReadOnly(compatible);
        }

        public static IReadOnlyList<ThreadCatalogEntry> FindCompatibleEntries(
            double measuredDiameterMm,
            bool isInternal,
            double toleranceMm)
        {
            var compatible = EntriesInternal
                .Where(entry => FindCompatiblePitches(entry, measuredDiameterMm, isInternal, toleranceMm).Count > 0)
                .ToArray();
            return Array.AsReadOnly(compatible);
        }
    }
}
