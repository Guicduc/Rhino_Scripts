using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace ThreadRhino
{
    internal enum ThreadKind
    {
        External = 0,
        Internal = 1,
    }

    internal sealed class ThreadFeatureDefinition
    {
        public Guid Id = Guid.NewGuid();
        public string Label = "Rosca";
        public ThreadKind Kind;
        public bool IsCustom;
        public string SizeName = "M6";
        public double NominalDiameter;
        public double Pitch;
        public bool RightHanded = true;
        public bool FullLength = true;
        public double Offset;
        public double Length;
        public double Clearance;
        public Point3d FaceStart;
        public Point3d FaceEnd;
        public Vector3d ReferenceX;
        public bool StartFromA = true;
        public double FaceRadius;

        public double FaceLength
        {
            get { return FaceStart.DistanceTo(FaceEnd); }
        }

        public Vector3d AxisDirection
        {
            get
            {
                var direction = FaceEnd - FaceStart;
                if (!direction.Unitize())
                    return Vector3d.Unset;
                return direction;
            }
        }

        public Point3d EffectiveStart
        {
            get
            {
                var axis = AxisDirection;
                if (!StartFromA)
                    axis.Reverse();
                var end = StartFromA ? FaceStart : FaceEnd;
                return end + axis * EffectiveOffset;
            }
        }

        public Vector3d EffectiveDirection
        {
            get
            {
                var axis = AxisDirection;
                if (!StartFromA)
                    axis.Reverse();
                return axis;
            }
        }

        public double EffectiveOffset
        {
            get { return FullLength ? 0.0 : Offset; }
        }

        public double EffectiveLength
        {
            get { return FullLength ? FaceLength : Length; }
        }

        public ThreadFeatureDefinition Duplicate()
        {
            return (ThreadFeatureDefinition)MemberwiseClone();
        }

        public void ApplySimilarityTransform(Transform transform, double scale, bool reversing)
        {
            var oldStart = FaceStart;
            var xPoint = oldStart + ReferenceX;
            FaceStart.Transform(transform);
            FaceEnd.Transform(transform);
            xPoint.Transform(transform);
            ReferenceX = xPoint - FaceStart;
            ReferenceX.Unitize();

            var factor = Math.Abs(scale);
            NominalDiameter *= factor;
            Pitch *= factor;
            Offset *= factor;
            Length *= factor;
            Clearance *= factor;
            FaceRadius *= factor;

            if (Math.Abs(factor - 1.0) > 1e-9)
            {
                IsCustom = true;
                SizeName = "Custom";
            }

            if (reversing)
                RightHanded = !RightHanded;
        }
    }

    internal sealed class ThreadGenerationResult
    {
        public Brep Brep;
        public string Error;

        public bool Success
        {
            get { return Brep != null && string.IsNullOrWhiteSpace(Error); }
        }

        public static ThreadGenerationResult Fail(string error)
        {
            return new ThreadGenerationResult { Error = error };
        }

        public static ThreadGenerationResult Ok(Brep brep)
        {
            return new ThreadGenerationResult { Brep = brep };
        }
    }

    internal static class ThreadDefinitionList
    {
        public static List<ThreadFeatureDefinition> Duplicate(IEnumerable<ThreadFeatureDefinition> source)
        {
            var result = new List<ThreadFeatureDefinition>();
            if (source == null)
                return result;
            foreach (var feature in source)
                result.Add(feature.Duplicate());
            return result;
        }
    }
}
