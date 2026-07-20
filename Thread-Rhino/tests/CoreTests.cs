using System;
using System.Linq;

namespace ThreadRhino
{
    internal static class CoreTests
    {
        private static int _failures;

        public static int Main()
        {
            Assert(ThreadCatalog.Entries.Count == 17, "O catálogo deve conter 17 diâmetros preferenciais.");

            var m6 = ThreadCatalog.Find("M6");
            Assert(m6 != null, "M6 deve existir no catálogo.");
            AssertClose(m6.DiameterMm, 6.0, 1e-12, "Diâmetro M6");
            Assert(m6.PitchesMm.SequenceEqual(new[] { 1.0, 0.75, 0.5 }), "Passos M6 devem incluir grosso e finos.");

            AssertClose(ThreadMath.FundamentalTriangleHeight(1.0), Math.Sqrt(3.0) / 2.0, 1e-9, "Altura fundamental");
            AssertClose(ThreadMath.BasicInternalMinorDiameter(6.0, 1.0), 4.917468245, 1e-9, "Diâmetro menor interno M6x1");
            AssertClose(ThreadMath.BasicExternalMinorDiameter(6.0, 1.0), 4.773130678, 1e-9, "Diâmetro menor externo M6x1");
            AssertClose(ThreadMath.ExternalThreadDepth(1.0) * 2.0, 1.226869322, 1e-9, "Profundidade externa diametral");
            AssertClose(ThreadMath.InternalThreadDepth(1.0) * 2.0, 1.082531754, 1e-9, "Profundidade interna diametral");

            var externalD10 = ThreadCatalog.FindCompatibleEntries(10.0, false, 0.01);
            Assert(externalD10.Count == 1 && externalD10[0].Name == "M10", "Uma face externa D10 deve oferecer somente M10.");
            Assert(!externalD10.Any(x => x.Name == "M2"), "Uma face externa D10 não deve oferecer M2.");

            var internalM6 = ThreadMath.BasicInternalMinorDiameter(6.0, 1.0);
            var internalEntries = ThreadCatalog.FindCompatibleEntries(internalM6, true, 0.01);
            Assert(internalEntries.Any(x => x.Name == "M6"), "O furo básico de M6x1 deve oferecer M6.");
            var internalPitches = ThreadCatalog.FindCompatiblePitches(m6, internalM6, true, 0.01);
            Assert(internalPitches.Count == 1 && Math.Abs(internalPitches[0] - 1.0) < 1e-12, "O furo básico M6x1 deve oferecer somente passo 1 mm.");

            var nonStandard = ThreadCatalog.FindCompatibleEntries(9.73, false, 0.01);
            Assert(nonStandard.Count == 0, "Um diâmetro externo fora do catálogo deve exigir Custom.");

            foreach (var entry in ThreadCatalog.Entries)
            {
                Assert(entry.PitchesMm.Count > 0, entry.Name + " deve possuir pelo menos um passo.");
                Assert(entry.PitchesMm.All(x => x > 0.0), entry.Name + " não pode possuir passo nulo ou negativo.");
                Assert(entry.PitchesMm.Distinct().Count() == entry.PitchesMm.Count, entry.Name + " não pode possuir passos duplicados.");
            }

            if (_failures == 0)
            {
                Console.WriteLine("ThreadRhino core tests: PASS");
                return 0;
            }

            Console.Error.WriteLine("ThreadRhino core tests: FAIL ({0})", _failures);
            return 1;
        }

        private static void Assert(bool condition, string message)
        {
            if (condition)
                return;
            _failures++;
            Console.Error.WriteLine("FAIL: " + message);
        }

        private static void AssertClose(double actual, double expected, double tolerance, string label)
        {
            Assert(Math.Abs(actual - expected) <= tolerance, string.Format("{0}: esperado {1}, obtido {2}", label, expected, actual));
        }
    }
}
