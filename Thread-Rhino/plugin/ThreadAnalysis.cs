using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ThreadRhino
{
    internal sealed class ThreadFaceInfo
    {
        public Guid ObjectId;
        public Brep CurrentBrep;
        public ThreadKind DetectedKind;
        public double Radius;
        public Point3d FaceStart;
        public Point3d FaceEnd;
        public Vector3d ReferenceX;
        public bool StartFromA;
    }

    internal static class ThreadUnits
    {
        public static bool HasUsableUnits(RhinoDoc doc)
        {
            return doc != null && doc.ModelUnitSystem != UnitSystem.None && doc.ModelUnitSystem != UnitSystem.Unset;
        }

        public static double MillimetersToModel(RhinoDoc doc, double millimeters)
        {
            return millimeters * RhinoMath.UnitScale(UnitSystem.Millimeters, doc.ModelUnitSystem);
        }

        public static double ModelToMillimeters(RhinoDoc doc, double modelValue)
        {
            return modelValue * RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Millimeters);
        }
    }

    internal static class ThreadFaceAnalyzer
    {
        public static bool TryAnalyze(RhinoDoc doc, ObjRef objRef, out ThreadFaceInfo info, out string error)
        {
            info = null;
            error = null;
            if (doc == null || objRef == null)
            {
                error = "Documento ou seleção inválida.";
                return false;
            }

            var face = objRef.Face();
            if (face == null)
            {
                error = "Selecione uma face cilíndrica do sólido, não apenas o objeto inteiro.";
                return false;
            }

            var brep = face.Brep;
            if (brep == null || !brep.IsSolid || !brep.IsManifold)
            {
                error = "A face precisa pertencer a uma polissuperfície fechada e manifold.";
                return false;
            }

            Cylinder cylinder;
            if (!face.TryGetCylinder(out cylinder, doc.ModelAbsoluteTolerance) || !cylinder.IsValid)
            {
                error = "A face selecionada não é um cilindro circular válido. Cones, SubD e malhas não são aceitos nesta versão.";
                return false;
            }

            var axis = cylinder.Axis;
            if (!axis.Unitize())
            {
                error = "Não foi possível determinar o eixo da face cilíndrica.";
                return false;
            }

            double minAxis;
            double maxAxis;
            if (!TryGetAxialExtents(face, cylinder.Center, axis, out minAxis, out maxAxis) || maxAxis - minAxis <= doc.ModelAbsoluteTolerance)
            {
                error = "Não foi possível determinar as extremidades da face cilíndrica.";
                return false;
            }

            var faceStart = cylinder.Center + axis * minAxis;
            var faceEnd = cylinder.Center + axis * maxAxis;
            var selectionPoint = objRef.SelectionPoint();
            if (!selectionPoint.IsValid)
                selectionPoint = face.PointAt(face.Domain(0).Mid, face.Domain(1).Mid);

            var projected = cylinder.Center + axis * Vector3d.Multiply(selectionPoint - cylinder.Center, axis);
            var referenceX = selectionPoint - projected;
            if (!referenceX.Unitize())
            {
                referenceX = cylinder.BasePlane.XAxis;
                referenceX -= axis * Vector3d.Multiply(referenceX, axis);
                referenceX.Unitize();
            }

            double u;
            double v;
            var kind = ThreadKind.External;
            if (face.ClosestPoint(selectionPoint, out u, out v))
            {
                var facePoint = face.PointAt(u, v);
                var normal = face.NormalAt(u, v);
                if (face.OrientationIsReversed)
                    normal.Reverse();
                var radial = facePoint - (cylinder.Center + axis * Vector3d.Multiply(facePoint - cylinder.Center, axis));
                if (radial.Unitize() && normal.Unitize())
                {
                    var probeDistance = Math.Max(doc.ModelAbsoluteTolerance * 5.0, cylinder.Radius * 0.00001);
                    var towardAxisIsInside = brep.IsPointInside(
                        facePoint - radial * probeDistance,
                        doc.ModelAbsoluteTolerance,
                        false);
                    var awayFromAxisIsInside = brep.IsPointInside(
                        facePoint + radial * probeDistance,
                        doc.ModelAbsoluteTolerance,
                        false);

                    if (towardAxisIsInside != awayFromAxisIsInside)
                        kind = towardAxisIsInside ? ThreadKind.External : ThreadKind.Internal;
                    else
                        kind = Vector3d.Multiply(normal, radial) >= 0.0 ? ThreadKind.External : ThreadKind.Internal;
                }
            }

            var selectionParameter = Vector3d.Multiply(selectionPoint - cylinder.Center, axis);
            var startFromA = Math.Abs(selectionParameter - minAxis) <= Math.Abs(selectionParameter - maxAxis);

            info = new ThreadFaceInfo
            {
                ObjectId = objRef.ObjectId,
                CurrentBrep = brep.DuplicateBrep(),
                DetectedKind = kind,
                Radius = cylinder.Radius,
                FaceStart = faceStart,
                FaceEnd = faceEnd,
                ReferenceX = referenceX,
                StartFromA = startFromA,
            };
            return true;
        }

        private static bool TryGetAxialExtents(BrepFace face, Point3d origin, Vector3d axis, out double minimum, out double maximum)
        {
            minimum = double.MaxValue;
            maximum = double.MinValue;
            var points = new List<Point3d>();

            var edgeIndices = face.AdjacentEdges();
            if (edgeIndices != null)
            {
                foreach (var edgeIndex in edgeIndices)
                {
                    if (edgeIndex < 0 || edgeIndex >= face.Brep.Edges.Count)
                        continue;
                    var edge = face.Brep.Edges[edgeIndex];
                    points.Add(edge.PointAtStart);
                    points.Add(edge.PointAtEnd);
                    var parameters = edge.DivideByCount(12, true);
                    if (parameters != null)
                        points.AddRange(parameters.Select(edge.PointAt));
                }
            }

            var domainU = face.Domain(0);
            var domainV = face.Domain(1);
            for (var iu = 0; iu <= 4; iu++)
            {
                var u = domainU.ParameterAt(iu / 4.0);
                for (var iv = 0; iv <= 4; iv++)
                {
                    var v = domainV.ParameterAt(iv / 4.0);
                    if (face.IsPointOnFace(u, v) != PointFaceRelation.Exterior)
                        points.Add(face.PointAt(u, v));
                }
            }

            foreach (var point in points)
            {
                var value = Vector3d.Multiply(point - origin, axis);
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }

            return minimum < double.MaxValue && maximum > double.MinValue;
        }
    }

    internal static class ThreadValidator
    {
        public static string ValidateAll(RhinoDoc doc, IList<ThreadFeatureDefinition> features)
        {
            if (!ThreadUnits.HasUsableUnits(doc))
                return "Defina uma unidade válida no documento antes de criar a rosca.";

            for (var i = 0; i < features.Count; i++)
            {
                var error = ValidateFeature(doc, features[i]);
                if (!string.IsNullOrWhiteSpace(error))
                    return string.Format("Rosca {0}: {1}", i + 1, error);
            }

            for (var i = 0; i < features.Count; i++)
            {
                for (var j = i + 1; j < features.Count; j++)
                {
                    if (FeaturesOverlap(features[i], features[j], doc.ModelAbsoluteTolerance))
                        return string.Format("As roscas {0} e {1} ocupam regiões sobrepostas da mesma face cilíndrica.", i + 1, j + 1);
                }
            }

            return null;
        }

        public static string ValidateFeature(RhinoDoc doc, ThreadFeatureDefinition feature)
        {
            var tolerance = doc.ModelAbsoluteTolerance;
            if (feature.FaceLength <= tolerance || !feature.AxisDirection.IsValid)
                return "o eixo ou o comprimento da face não é válido.";
            if (feature.FaceRadius <= tolerance)
                return "o raio da face não é válido.";
            if (feature.Pitch <= tolerance * 10.0)
                return "o passo é pequeno demais para a tolerância atual do documento.";
            if (tolerance > feature.Pitch / 20.0)
                return "reduza a tolerância absoluta do documento para no máximo passo/20.";
            if (feature.Clearance < 0.0)
                return "a compensação radial não pode ser negativa.";
            if (feature.EffectiveOffset < -tolerance || feature.EffectiveLength <= tolerance)
                return "offset e comprimento não são válidos.";
            if (feature.EffectiveOffset + feature.EffectiveLength > feature.FaceLength + tolerance)
                return "offset + comprimento ultrapassa a face cilíndrica.";

            var depth = feature.Kind == ThreadKind.External
                ? ThreadMath.ExternalThreadDepth(feature.Pitch)
                : ThreadMath.InternalThreadDepth(feature.Pitch);
            if (feature.Kind == ThreadKind.External && feature.FaceRadius - feature.Clearance - depth <= tolerance)
                return "a profundidade da rosca alcança ou atravessa o eixo da peça.";

            if (!feature.IsCustom)
            {
                var expectedDiameter = feature.Kind == ThreadKind.External
                    ? feature.NominalDiameter
                    : ThreadMath.BasicInternalMinorDiameter(feature.NominalDiameter, feature.Pitch);
                var measuredDiameter = feature.FaceRadius * 2.0;
                var matchTolerance = Math.Max(tolerance * 2.0, ThreadUnits.MillimetersToModel(doc, 0.01));
                if (Math.Abs(expectedDiameter - measuredDiameter) > matchTolerance)
                {
                    return string.Format(
                        "diâmetro medido {0:0.###} mm; esperado {1:0.###} mm para {2}. Corrija a peça ou use Custom.",
                        ThreadUnits.ModelToMillimeters(doc, measuredDiameter),
                        ThreadUnits.ModelToMillimeters(doc, expectedDiameter),
                        feature.SizeName);
                }
            }

            return null;
        }

        private static bool FeaturesOverlap(ThreadFeatureDefinition a, ThreadFeatureDefinition b, double tolerance)
        {
            var axisA = a.AxisDirection;
            var axisB = b.AxisDirection;
            if (!axisA.Unitize() || !axisB.Unitize())
                return false;
            if (Math.Abs(Vector3d.Multiply(axisA, axisB)) < 0.999999)
                return false;
            if (Math.Abs(a.FaceRadius - b.FaceRadius) > tolerance * 2.0 || a.Kind != b.Kind)
                return false;

            var separation = Vector3d.CrossProduct(b.FaceStart - a.FaceStart, axisA).Length;
            if (separation > tolerance * 2.0)
                return false;

            var origin = a.FaceStart;
            var a0 = Vector3d.Multiply(a.EffectiveStart - origin, axisA);
            var a1 = Vector3d.Multiply(a.EffectiveStart + a.EffectiveDirection * a.EffectiveLength - origin, axisA);
            var b0 = Vector3d.Multiply(b.EffectiveStart - origin, axisA);
            var b1 = Vector3d.Multiply(b.EffectiveStart + b.EffectiveDirection * b.EffectiveLength - origin, axisA);
            var minA = Math.Min(a0, a1);
            var maxA = Math.Max(a0, a1);
            var minB = Math.Min(b0, b1);
            var maxB = Math.Max(b0, b1);
            return Math.Min(maxA, maxB) - Math.Max(minA, minB) > tolerance;
        }
    }
}
