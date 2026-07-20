using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Geometry;

namespace ThreadRhino
{
    internal static class ThreadGeometry
    {
        private const int SpiralPointsPerTurn = 12;

        public static ThreadGenerationResult Generate(RhinoDoc doc, Brep baseBrep, IList<ThreadFeatureDefinition> features)
        {
            if (doc == null || baseBrep == null)
                return ThreadGenerationResult.Fail("Não há uma peça-base válida para regenerar.");

            var validation = ThreadValidator.ValidateAll(doc, features);
            if (!string.IsNullOrWhiteSpace(validation))
                return ThreadGenerationResult.Fail(validation);

            var working = baseBrep.DuplicateBrep();
            if (working == null || !working.IsSolid || !working.IsManifold)
                return ThreadGenerationResult.Fail("A peça-base não é um sólido Brep fechado e manifold.");
            OrientOutward(working);

            foreach (var feature in features)
            {
                List<Brep> cutters;
                string cutterError;
                if (!TryBuildCutters(doc, feature, out cutters, out cutterError))
                    return ThreadGenerationResult.Fail(cutterError);

                foreach (var cutter in cutters)
                {
                    OrientOutward(cutter);
                    var difference = Brep.CreateBooleanDifference(working, cutter, doc.ModelAbsoluteTolerance, false);
                    if (difference == null || difference.Length != 1 || difference[0] == null)
                        return ThreadGenerationResult.Fail("A diferença booleana da rosca falhou ou dividiu a peça em vários sólidos.");
                    working = difference[0];
                    OrientOutward(working);
                }

                var validityLog = string.Empty;
                if (!working.IsSolid || !working.IsManifold || !working.IsValidWithLog(out validityLog))
                    return ThreadGenerationResult.Fail("A rosca gerou uma Brep inválida: " + validityLog);
            }

            OrientOutward(working);
            return ThreadGenerationResult.Ok(working);
        }

        private static void OrientOutward(Brep brep)
        {
            if (brep != null && brep.SolidOrientation == BrepSolidOrientation.Inward)
                brep.Flip();
        }

        internal static bool TrySectionContainsAxis(
            Brep brep,
            Point3d axisPoint,
            Vector3d axis,
            double tolerance,
            out bool containsAxis)
        {
            containsAxis = false;
            if (brep == null || !axis.Unitize())
                return false;

            var plane = new Plane(axisPoint, axis);
            if (!plane.IsValid)
                return false;

            var contours = Brep.CreateContourCurves(brep, plane);
            if (contours == null || contours.Length == 0)
                return false;

            var joined = Curve.JoinCurves(contours, tolerance);
            var candidates = joined != null && joined.Length > 0 ? joined : contours;
            var containingLoops = 0;
            var closedLoops = 0;
            foreach (var curve in candidates)
            {
                if (curve == null || !curve.IsClosed)
                    continue;
                closedLoops++;
                var containment = curve.Contains(axisPoint, plane, tolerance);
                if (containment == PointContainment.Inside)
                    containingLoops++;
                else if (containment == PointContainment.Coincident)
                    return false;
            }

            if (closedLoops == 0)
                return false;
            containsAxis = containingLoops % 2 == 1;
            return true;
        }

        private static bool TryBuildCutters(RhinoDoc doc, ThreadFeatureDefinition feature, out List<Brep> cutters, out string error)
        {
            cutters = new List<Brep>();
            error = null;
            var tolerance = doc.ModelAbsoluteTolerance;
            var axis = feature.EffectiveDirection;
            if (!axis.Unitize())
            {
                error = "O eixo da rosca é inválido.";
                return false;
            }

            Vector3d x;
            Vector3d y;
            if (!TryBuildFrame(axis, feature.ReferenceX, out x, out y))
            {
                error = "Não foi possível construir o plano inicial da rosca.";
                return false;
            }

            var start = feature.EffectiveStart;
            var length = feature.EffectiveLength;
            var clearance = feature.Clearance;
            var crestRadius = feature.Kind == ThreadKind.External
                ? feature.FaceRadius - clearance
                : feature.FaceRadius + clearance;

            if (clearance > tolerance)
            {
                Brep clearanceCutter;
                if (feature.Kind == ThreadKind.External)
                {
                    var outerRadius = feature.FaceRadius + Math.Max(clearance, tolerance * 20.0);
                    if (!TryCreateAnnularCutter(start, axis, x, y, length, crestRadius, outerRadius, tolerance, out clearanceCutter))
                    {
                        error = "Não foi possível criar o volume de compensação da rosca externa.";
                        return false;
                    }
                }
                else
                {
                    clearanceCutter = CreateCylinder(start, axis, x, y, length, crestRadius, tolerance * 10.0);
                    if (clearanceCutter == null)
                    {
                        error = "Não foi possível criar o volume de compensação da rosca interna.";
                        return false;
                    }
                }
                cutters.Add(clearanceCutter);
            }

            Brep threadCutter;
            if (!TryCreateHelicalCutter(feature, start, axis, x, y, crestRadius, tolerance, out threadCutter, out error))
                return false;
            cutters.Add(threadCutter);
            return true;
        }

        private static bool TryCreateHelicalCutter(
            ThreadFeatureDefinition feature,
            Point3d threadStart,
            Vector3d axis,
            Vector3d x,
            Vector3d y,
            double crestRadius,
            double tolerance,
            out Brep cutter,
            out string error)
        {
            cutter = null;
            error = null;
            var pitch = feature.Pitch;
            var overrun = pitch;
            var spiralStart = threadStart - axis * overrun;
            var spiralLength = feature.EffectiveLength + overrun * 2.0;
            var turns = spiralLength / pitch;
            var margin = Math.Max(pitch * 0.35, tolerance * 20.0);

            double railRadius;
            PolylineCurve section;
            if (feature.Kind == ThreadKind.External)
            {
                var rootRadius = crestRadius - ThreadMath.ExternalThreadDepth(pitch);
                var outerRadius = crestRadius + margin;
                var crestHalfWidth = ThreadMath.ExternalCrestOpening(pitch) * 0.5;
                var rootHalfWidth = ThreadMath.ExternalRootFlat(pitch) * 0.5;
                var outerHalfWidth = crestHalfWidth + margin / Math.Sqrt(3.0);
                railRadius = outerRadius;
                section = CreateSection(
                    spiralStart,
                    x,
                    axis,
                    outerRadius,
                    rootRadius,
                    outerHalfWidth,
                    rootHalfWidth);
            }
            else
            {
                var rootRadius = crestRadius + ThreadMath.InternalThreadDepth(pitch);
                var innerRadius = Math.Max(tolerance * 5.0, crestRadius - margin);
                var minorHalfWidth = ThreadMath.InternalMinorOpening(pitch) * 0.5;
                var rootHalfWidth = ThreadMath.InternalRootFlat(pitch) * 0.5;
                var innerHalfWidth = minorHalfWidth + (crestRadius - innerRadius) / Math.Sqrt(3.0);
                railRadius = innerRadius;
                section = CreateSection(
                    spiralStart,
                    x,
                    axis,
                    innerRadius,
                    rootRadius,
                    innerHalfWidth,
                    rootHalfWidth);
            }

            if (section == null || !section.IsClosed)
            {
                error = "Não foi possível construir o perfil de 60° da rosca.";
                return false;
            }

            var axisRail = new LineCurve(spiralStart, spiralStart + axis * spiralLength);
            var radiusPoint = spiralStart + x * railRadius;
            var spiral = NurbsCurve.CreateSpiral(
                axisRail,
                axisRail.Domain.Min,
                axisRail.Domain.Max,
                radiusPoint,
                pitch,
                turns,
                railRadius,
                railRadius,
                SpiralPointsPerTurn);
            if (spiral == null)
            {
                error = "O Rhino não conseguiu construir a hélice da rosca.";
                return false;
            }

            if (!feature.RightHanded)
            {
                var mirror = Transform.Mirror(spiralStart, y);
                spiral.Transform(mirror);
            }

            var sweep = new SweepOneRail
            {
                SweepTolerance = tolerance,
                AngleToleranceRadians = RhinoMath.ToRadians(1.0),
                ClosedSweep = false,
                GlobalShapeBlending = true,
                MiterType = 0,
            };
            sweep.SetRoadlikeUpDirection(axis);
            var swept = sweep.PerformSweep(spiral, section);
            if (swept == null || swept.Length == 0)
                swept = Brep.CreateFromSweep(spiral, section, true, tolerance);
            if (swept == null || swept.Length == 0)
            {
                error = "O sweep do perfil ao longo da hélice falhou.";
                return false;
            }

            var joined = swept.Length == 1 ? swept[0] : Brep.JoinBreps(swept, tolerance).FirstOrDefault();
            if (joined == null)
            {
                error = "Não foi possível unir as superfícies do cortador helicoidal.";
                return false;
            }

            var capped = joined.CapPlanarHoles(tolerance) ?? joined;
            if (!capped.IsSolid)
            {
                error = "O cortador helicoidal não pôde ser fechado como sólido.";
                return false;
            }
            OrientOutward(capped);

            var maximumRadius = feature.Kind == ThreadKind.External
                ? crestRadius + margin * 1.5
                : crestRadius + ThreadMath.InternalThreadDepth(pitch) + margin;
            var clip = CreateCylinder(threadStart, axis, x, y, feature.EffectiveLength, maximumRadius, tolerance * 5.0);
            if (clip == null)
            {
                error = "Não foi possível limitar o cortador ao comprimento da rosca.";
                return false;
            }
            OrientOutward(clip);

            var clipped = Brep.CreateBooleanIntersection(capped, clip, tolerance, false);
            if (clipped == null || clipped.Length != 1 || clipped[0] == null || !clipped[0].IsSolid)
            {
                error = "Não foi possível recortar o cortador helicoidal nas extremidades da rosca.";
                return false;
            }

            cutter = clipped[0];
            OrientOutward(cutter);
            return true;
        }

        private static PolylineCurve CreateSection(
            Point3d axisPoint,
            Vector3d radial,
            Vector3d axis,
            double railRadius,
            double rootRadius,
            double railHalfWidth,
            double rootHalfWidth)
        {
            var points = new List<Point3d>
            {
                axisPoint + radial * railRadius - axis * railHalfWidth,
                axisPoint + radial * rootRadius - axis * rootHalfWidth,
                axisPoint + radial * rootRadius + axis * rootHalfWidth,
                axisPoint + radial * railRadius + axis * railHalfWidth,
            };
            points.Add(points[0]);
            return new PolylineCurve(points);
        }

        private static bool TryCreateAnnularCutter(
            Point3d start,
            Vector3d axis,
            Vector3d x,
            Vector3d y,
            double length,
            double innerRadius,
            double outerRadius,
            double tolerance,
            out Brep annulus)
        {
            annulus = null;
            if (innerRadius <= tolerance || outerRadius <= innerRadius + tolerance)
                return false;

            var outer = CreateCylinder(start, axis, x, y, length, outerRadius, tolerance * 10.0);
            var inner = CreateCylinder(start, axis, x, y, length, innerRadius, tolerance * 12.0);
            if (outer == null || inner == null)
                return false;
            OrientOutward(outer);
            OrientOutward(inner);
            var result = Brep.CreateBooleanDifference(outer, inner, tolerance, false);
            if (result == null || result.Length != 1 || result[0] == null || !result[0].IsSolid)
                return false;
            annulus = result[0];
            OrientOutward(annulus);
            return true;
        }

        private static Brep CreateCylinder(
            Point3d start,
            Vector3d axis,
            Vector3d x,
            Vector3d y,
            double length,
            double radius,
            double axialExtension)
        {
            if (radius <= 0.0 || length <= 0.0)
                return null;
            var extendedStart = start - axis * axialExtension;
            var extendedLength = length + axialExtension * 2.0;
            var plane = new Plane(extendedStart, x, y);
            var cylinder = new Cylinder(new Circle(plane, radius), extendedLength);
            return Brep.CreateFromCylinder(cylinder, true, true);
        }

        private static bool TryBuildFrame(Vector3d axis, Vector3d referenceX, out Vector3d x, out Vector3d y)
        {
            x = referenceX;
            x -= axis * Vector3d.Multiply(x, axis);
            if (!x.Unitize())
            {
                x = Math.Abs(Vector3d.Multiply(axis, Vector3d.XAxis)) < 0.9 ? Vector3d.XAxis : Vector3d.YAxis;
                x -= axis * Vector3d.Multiply(x, axis);
                if (!x.Unitize())
                {
                    y = Vector3d.Unset;
                    return false;
                }
            }

            y = Vector3d.CrossProduct(axis, x);
            return y.Unitize();
        }
    }
}
