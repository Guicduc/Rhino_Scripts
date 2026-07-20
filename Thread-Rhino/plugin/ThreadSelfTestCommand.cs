using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;

namespace ThreadRhino
{
    [Guid("dff1f102-d7f7-441f-8a34-da91f36ec8d1")]
    [CommandStyle(Style.Hidden | Style.NotUndoable)]
    public sealed class RhinoThreadSelfTestCommand : Command
    {
        public override string EnglishName
        {
            get { return "RhinoThreadSelfTest"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var messages = new List<string>();
            var success = true;
            var originalUnits = doc.ModelUnitSystem;
            var originalTolerance = doc.ModelAbsoluteTolerance;
            var originalAngleTolerance = doc.ModelAngleToleranceDegrees;
            try
            {
                doc.ModelUnitSystem = UnitSystem.Millimeters;
                doc.ModelAbsoluteTolerance = 0.01;
                doc.ModelAngleToleranceDegrees = 1.0;

                success &= TestExternal(doc, true, 0.0, messages);
                success &= TestExternal(doc, false, 0.20, messages);
                success &= TestLongExternal(doc, messages);
                success &= TestInternal(doc, true, 0.0, messages);
                success &= TestInternal(doc, false, 0.20, messages);
                success &= TestMultiple(doc, messages);
            }
            catch (Exception ex)
            {
                success = false;
                messages.Add("EXCEPTION: " + ex);
            }
            finally
            {
                doc.ModelUnitSystem = originalUnits;
                doc.ModelAbsoluteTolerance = originalTolerance;
                doc.ModelAngleToleranceDegrees = originalAngleTolerance;
            }

            messages.Insert(0, success ? "PASS" : "FAIL");
            var logPath = Environment.GetEnvironmentVariable("THREADRHINO_SELFTEST_LOG");
            if (string.IsNullOrWhiteSpace(logPath))
                logPath = Path.Combine(Path.GetTempPath(), "ThreadRhino-SelfTest.log");
            try { File.WriteAllLines(logPath, messages); }
            catch { }

            foreach (var message in messages)
                RhinoApp.WriteLine("RhinoThread self-test: " + message);
            return success ? Result.Success : Result.Failure;
        }

        private static bool TestExternal(RhinoDoc doc, bool rightHanded, double clearance, List<string> messages)
        {
            var feature = Feature(ThreadKind.External, 3.0, 6.0, 1.0, 10.0, rightHanded, clearance);
            return Check(ThreadGeometry.Generate(doc, CylinderBrep(3.0, 10.0), new List<ThreadFeatureDefinition> { feature }), "external", messages);
        }

        private static bool TestInternal(RhinoDoc doc, bool rightHanded, double clearance, List<string> messages)
        {
            var minorRadius = ThreadMath.BasicInternalMinorDiameter(6.0, 1.0) * 0.5;
            var shell = Brep.CreateBooleanDifference(CylinderBrep(6.0, 10.0), CylinderBrep(minorRadius, 10.0), doc.ModelAbsoluteTolerance, false);
            if (shell == null || shell.Length != 1)
            {
                messages.Add("internal preform failed");
                return false;
            }
            var feature = Feature(ThreadKind.Internal, minorRadius, 6.0, 1.0, 10.0, rightHanded, clearance);
            return Check(ThreadGeometry.Generate(doc, shell[0], new List<ThreadFeatureDefinition> { feature }), "internal", messages);
        }

        private static bool TestLongExternal(RhinoDoc doc, List<string> messages)
        {
            var feature = Feature(ThreadKind.External, 5.0, 10.0, 1.5, 100.0, true, 0.20);
            feature.IsCustom = true;
            feature.SizeName = "Custom";
            var result = ThreadGeometry.Generate(
                doc,
                CylinderBrep(5.0, 100.0),
                new List<ThreadFeatureDefinition> { feature });
            if (!Check(result, "external D10x100", messages))
                return false;

            bool containsCore;
            var coreKnown = ThreadGeometry.TrySectionContainsAxis(
                result.Brep,
                new Point3d(0.0, 0.0, 50.0),
                Vector3d.ZAxis,
                doc.ModelAbsoluteTolerance,
                out containsCore);
            var volumeProperties = VolumeMassProperties.Compute(result.Brep);
            var rootRadius = 5.0 - 0.20 - ThreadMath.ExternalThreadDepth(1.5);
            var minimumCoreVolume = Math.PI * rootRadius * rootRadius * 100.0;
            if (!coreKnown || !containsCore || volumeProperties == null || volumeProperties.Volume < minimumCoreVolume * 0.98)
            {
                messages.Add("external D10x100: núcleo ausente ou volume abaixo do raio de raiz.");
                return false;
            }
            messages.Add("external D10x100 core: PASS");
            return true;
        }

        private static bool TestMultiple(RhinoDoc doc, List<string> messages)
        {
            var first = Feature(ThreadKind.External, 5.0, 10.0, 1.5, 8.0, true, 0.10);
            first.FullLength = false;
            var second = Feature(ThreadKind.External, 5.0, 10.0, 1.5, 8.0, false, 0.10);
            second.FullLength = false;
            second.Offset = 11.0;
            second.FaceEnd = new Point3d(0.0, 0.0, 20.0);
            first.FaceEnd = new Point3d(0.0, 0.0, 20.0);
            return Check(ThreadGeometry.Generate(doc, CylinderBrep(5.0, 20.0), new List<ThreadFeatureDefinition> { first, second }), "multiple", messages);
        }

        private static ThreadFeatureDefinition Feature(ThreadKind kind, double radius, double nominal, double pitch, double length, bool rightHanded, double clearance)
        {
            return new ThreadFeatureDefinition
            {
                Kind = kind,
                IsCustom = false,
                SizeName = "Test",
                NominalDiameter = nominal,
                Pitch = pitch,
                RightHanded = rightHanded,
                FullLength = true,
                Length = length,
                Clearance = clearance,
                FaceStart = Point3d.Origin,
                FaceEnd = new Point3d(0.0, 0.0, length),
                ReferenceX = Vector3d.XAxis,
                StartFromA = true,
                FaceRadius = radius,
            };
        }

        private static Brep CylinderBrep(double radius, double height)
        {
            return Brep.CreateFromCylinder(new Cylinder(new Circle(Plane.WorldXY, radius), height), true, true);
        }

        private static bool Check(ThreadGenerationResult result, string name, List<string> messages)
        {
            if (!result.Success)
            {
                messages.Add(name + ": " + result.Error);
                return false;
            }
            if (!result.Brep.IsValid || !result.Brep.IsSolid || !result.Brep.IsManifold)
            {
                messages.Add(name + ": invalid output");
                return false;
            }
            messages.Add(name + ": PASS");
            return true;
        }
    }
}
