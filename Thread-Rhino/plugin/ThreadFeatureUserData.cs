using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rhino.DocObjects.Custom;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Runtime;

namespace ThreadRhino
{
    [Guid("c9393391-974d-4b5a-81e2-2eb47a07319a")]
    public sealed class ThreadFeatureUserData : UserData
    {
        private const int ArchiveVersion = 1;

        internal Brep BaseBrep;
        internal List<ThreadFeatureDefinition> Features = new List<ThreadFeatureDefinition>();
        internal bool TransformCompatible = true;
        internal string TransformMessage = string.Empty;

        public override string Description
        {
            get { return "Editable RhinoThread feature data"; }
        }

        public override bool ShouldWrite
        {
            get { return BaseBrep != null && BaseBrep.IsValid; }
        }

        internal static ThreadFeatureUserData Find(CommonObject geometry)
        {
            if (geometry == null || !geometry.HasUserData)
                return null;
            return geometry.UserData.Find(typeof(ThreadFeatureUserData)) as ThreadFeatureUserData;
        }

        internal static ThreadFeatureUserData Create(Brep baseBrep, IEnumerable<ThreadFeatureDefinition> features)
        {
            return new ThreadFeatureUserData
            {
                BaseBrep = baseBrep != null ? baseBrep.DuplicateBrep() : null,
                Features = ThreadDefinitionList.Duplicate(features),
                TransformCompatible = true,
                TransformMessage = string.Empty,
            };
        }

        internal ThreadFeatureUserData DuplicateData()
        {
            return Create(BaseBrep, Features);
        }

        protected override void OnDuplicate(UserData source)
        {
            var other = source as ThreadFeatureUserData;
            if (other == null)
                return;

            BaseBrep = other.BaseBrep != null ? other.BaseBrep.DuplicateBrep() : null;
            Features = ThreadDefinitionList.Duplicate(other.Features);
            TransformCompatible = other.TransformCompatible;
            TransformMessage = other.TransformMessage;
        }

        protected override void OnTransform(Transform transform)
        {
            if (BaseBrep != null)
                BaseBrep.Transform(transform);

            Vector3d translation;
            double dilation;
            Transform rotation;
            var similarity = transform.DecomposeSimilarity(out translation, out dilation, out rotation, 1e-9);
            if (similarity == TransformSimilarityType.NotSimilarity)
            {
                TransformCompatible = false;
                TransformMessage = "A peça recebeu escala não uniforme ou cisalhamento. A geometria continua válida, mas a rosca não pode mais ser regenerada.";
            }
            else
            {
                var reversing = similarity == TransformSimilarityType.OrientationReversing;
                foreach (var feature in Features)
                    feature.ApplySimilarityTransform(transform, dilation, reversing);
            }

            base.OnTransform(transform);
        }

        protected override bool Write(BinaryArchiveWriter archive)
        {
            archive.WriteInt(ArchiveVersion);
            archive.WriteBool(TransformCompatible);
            archive.WriteString(TransformMessage ?? string.Empty);
            archive.WriteGeometry(BaseBrep);
            archive.WriteInt(Features.Count);
            foreach (var feature in Features)
                WriteFeature(archive, feature);
            return !archive.WriteErrorOccured;
        }

        protected override bool Read(BinaryArchiveReader archive)
        {
            var version = archive.ReadInt();
            if (version != ArchiveVersion)
                return false;

            TransformCompatible = archive.ReadBool();
            TransformMessage = archive.ReadString();
            BaseBrep = archive.ReadGeometry() as Brep;
            var count = archive.ReadInt();
            if (count < 0 || count > 10000)
                return false;

            Features = new List<ThreadFeatureDefinition>(count);
            for (var i = 0; i < count; i++)
                Features.Add(ReadFeature(archive));
            return !archive.ReadErrorOccured && BaseBrep != null;
        }

        private static void WriteFeature(BinaryArchiveWriter archive, ThreadFeatureDefinition feature)
        {
            archive.WriteGuid(feature.Id);
            archive.WriteString(feature.Label ?? string.Empty);
            archive.WriteInt((int)feature.Kind);
            archive.WriteBool(feature.IsCustom);
            archive.WriteString(feature.SizeName ?? string.Empty);
            archive.WriteDouble(feature.NominalDiameter);
            archive.WriteDouble(feature.Pitch);
            archive.WriteBool(feature.RightHanded);
            archive.WriteBool(feature.FullLength);
            archive.WriteDouble(feature.Offset);
            archive.WriteDouble(feature.Length);
            archive.WriteDouble(feature.Clearance);
            archive.WritePoint3d(feature.FaceStart);
            archive.WritePoint3d(feature.FaceEnd);
            archive.WriteVector3d(feature.ReferenceX);
            archive.WriteBool(feature.StartFromA);
            archive.WriteDouble(feature.FaceRadius);
        }

        private static ThreadFeatureDefinition ReadFeature(BinaryArchiveReader archive)
        {
            return new ThreadFeatureDefinition
            {
                Id = archive.ReadGuid(),
                Label = archive.ReadString(),
                Kind = (ThreadKind)archive.ReadInt(),
                IsCustom = archive.ReadBool(),
                SizeName = archive.ReadString(),
                NominalDiameter = archive.ReadDouble(),
                Pitch = archive.ReadDouble(),
                RightHanded = archive.ReadBool(),
                FullLength = archive.ReadBool(),
                Offset = archive.ReadDouble(),
                Length = archive.ReadDouble(),
                Clearance = archive.ReadDouble(),
                FaceStart = archive.ReadPoint3d(),
                FaceEnd = archive.ReadPoint3d(),
                ReferenceX = archive.ReadVector3d(),
                StartFromA = archive.ReadBool(),
                FaceRadius = archive.ReadDouble(),
            };
        }
    }
}
