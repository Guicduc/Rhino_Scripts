using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace ThreadRhino
{
    internal static class ThreadCommandSupport
    {
        public static bool TryAttachData(Brep result, Brep baseBrep, IList<ThreadFeatureDefinition> features, out string error)
        {
            error = null;
            if (result == null)
            {
                error = "O resultado da rosca é inválido.";
                return false;
            }

            var existing = ThreadFeatureUserData.Find(result);
            if (existing != null)
                result.UserData.Remove(existing);

            if (features.Count == 0)
                return true;

            var data = ThreadFeatureUserData.Create(baseBrep, features);
            if (!result.UserData.Add(data))
            {
                error = "Não foi possível anexar os dados editáveis da rosca ao sólido.";
                return false;
            }
            return true;
        }

        public static bool TryReplace(RhinoDoc doc, Guid objectId, Brep result, out string error)
        {
            error = null;
            if (!doc.Objects.Replace(objectId, result))
            {
                error = "O Rhino não conseguiu substituir o objeto selecionado pelo resultado rosqueado.";
                return false;
            }
            doc.Views.Redraw();
            return true;
        }

        public static void ShowError(string message)
        {
            Rhino.UI.Dialogs.ShowMessage(message, "RhinoThread");
        }
    }

    [Guid("e52dd39f-9dfd-4e7a-9df5-c1319558a51b")]
    public sealed class RhinoThreadCommand : Command
    {
        public override string EnglishName
        {
            get { return "RhinoThread"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!ThreadUnits.HasUsableUnits(doc))
            {
                ThreadCommandSupport.ShowError("Defina uma unidade válida em Propriedades do Documento antes de criar a rosca.");
                return Result.Failure;
            }

            var get = new GetObject();
            get.SetCommandPrompt("Selecione uma face cilíndrica para criar a rosca");
            get.GeometryFilter = ObjectType.Surface;
            get.SubObjectSelect = true;
            get.EnablePreSelect(true, true);
            get.Get();
            if (get.CommandResult() != Result.Success)
                return get.CommandResult();

            var objRef = get.Object(0);
            ThreadFaceInfo faceInfo;
            string error;
            if (!ThreadFaceAnalyzer.TryAnalyze(doc, objRef, out faceInfo, out error))
            {
                ThreadCommandSupport.ShowError(error);
                return Result.Failure;
            }

            var rhinoObject = doc.Objects.FindId(faceInfo.ObjectId);
            if (rhinoObject == null)
            {
                ThreadCommandSupport.ShowError("O objeto selecionado não está mais disponível no documento.");
                return Result.Failure;
            }

            var stored = ThreadFeatureUserData.Find(rhinoObject.Geometry);
            if (stored != null && !stored.TransformCompatible)
            {
                ThreadCommandSupport.ShowError(stored.TransformMessage);
                return Result.Failure;
            }

            var baseBrep = stored != null && stored.BaseBrep != null
                ? stored.BaseBrep.DuplicateBrep()
                : faceInfo.CurrentBrep.DuplicateBrep();
            var features = stored != null
                ? ThreadDefinitionList.Duplicate(stored.Features)
                : new List<ThreadFeatureDefinition>();

            var measuredDiameterMm = ThreadUnits.ModelToMillimeters(doc, faceInfo.Radius * 2.0);
            var isInternal = faceInfo.DetectedKind == ThreadKind.Internal;
            var catalogEntry = ThreadCatalog.FindClosest(measuredDiameterMm, isInternal);
            var pitchMm = ThreadCatalog.FindClosestPitch(catalogEntry, measuredDiameterMm, isInternal);
            var diameterToleranceMm = Math.Max(
                ThreadUnits.ModelToMillimeters(doc, doc.ModelAbsoluteTolerance * 2.0),
                0.01);
            var isoCompatible = ThreadCatalog.FindCompatiblePitches(
                catalogEntry,
                measuredDiameterMm,
                isInternal,
                diameterToleranceMm).Count > 0;
            var nominalDiameterMm = isoCompatible
                ? catalogEntry.DiameterMm
                : (isInternal
                    ? measuredDiameterMm + ThreadMath.InternalThreadDepth(pitchMm) * 2.0
                    : measuredDiameterMm);
            var feature = new ThreadFeatureDefinition
            {
                Id = Guid.NewGuid(),
                Label = string.Format("Rosca {0}", features.Count + 1),
                Kind = faceInfo.DetectedKind,
                IsCustom = !isoCompatible,
                SizeName = isoCompatible ? catalogEntry.Name : "Custom",
                NominalDiameter = ThreadUnits.MillimetersToModel(doc, nominalDiameterMm),
                Pitch = ThreadUnits.MillimetersToModel(doc, pitchMm),
                RightHanded = true,
                FullLength = true,
                Offset = 0.0,
                Length = faceInfo.FaceStart.DistanceTo(faceInfo.FaceEnd),
                Clearance = ThreadUnits.MillimetersToModel(doc, 0.20),
                FaceStart = faceInfo.FaceStart,
                FaceEnd = faceInfo.FaceEnd,
                ReferenceX = faceInfo.ReferenceX,
                StartFromA = faceInfo.StartFromA,
                FaceRadius = faceInfo.Radius,
            };
            features.Add(feature);

            using (var dialog = new ThreadDialog(doc, baseBrep, features, features.Count - 1, false))
            {
                if (!dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)))
                    return Result.Cancel;

                var resultBrep = dialog.ResultBrep;
                var resultFeatures = dialog.ResultFeatures;
                if (!ThreadCommandSupport.TryAttachData(resultBrep, baseBrep, resultFeatures, out error))
                {
                    ThreadCommandSupport.ShowError(error);
                    return Result.Failure;
                }
                if (!ThreadCommandSupport.TryReplace(doc, faceInfo.ObjectId, resultBrep, out error))
                {
                    ThreadCommandSupport.ShowError(error);
                    return Result.Failure;
                }
            }

            RhinoApp.WriteLine("RhinoThread: rosca criada. Use RhinoThreadEdit para alterar os parâmetros.");
            return Result.Success;
        }
    }

    [Guid("fd2cac53-2e30-48a6-a3af-8c24390363da")]
    public sealed class RhinoThreadEditCommand : Command
    {
        public override string EnglishName
        {
            get { return "RhinoThreadEdit"; }
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            if (!ThreadUnits.HasUsableUnits(doc))
            {
                ThreadCommandSupport.ShowError("Defina uma unidade válida em Propriedades do Documento antes de editar a rosca.");
                return Result.Failure;
            }

            var get = new GetObject();
            get.SetCommandPrompt("Selecione uma peça criada pelo RhinoThread");
            get.GeometryFilter = ObjectType.Brep | ObjectType.Extrusion;
            get.SubObjectSelect = false;
            get.EnablePreSelect(true, true);
            get.Get();
            if (get.CommandResult() != Result.Success)
                return get.CommandResult();

            var objRef = get.Object(0);
            var rhinoObject = doc.Objects.FindId(objRef.ObjectId);
            var stored = rhinoObject != null ? ThreadFeatureUserData.Find(rhinoObject.Geometry) : null;
            if (stored == null || stored.BaseBrep == null || stored.Features.Count == 0)
            {
                ThreadCommandSupport.ShowError("O objeto selecionado não possui recursos editáveis do RhinoThread.");
                return Result.Failure;
            }
            if (!stored.TransformCompatible)
            {
                ThreadCommandSupport.ShowError(stored.TransformMessage);
                return Result.Failure;
            }

            var baseBrep = stored.BaseBrep.DuplicateBrep();
            string error;
            using (var dialog = new ThreadDialog(doc, baseBrep, stored.Features, 0, true))
            {
                if (!dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)))
                    return Result.Cancel;

                var resultBrep = dialog.ResultBrep;
                var resultFeatures = dialog.ResultFeatures;
                if (!ThreadCommandSupport.TryAttachData(resultBrep, baseBrep, resultFeatures, out error))
                {
                    ThreadCommandSupport.ShowError(error);
                    return Result.Failure;
                }
                if (!ThreadCommandSupport.TryReplace(doc, objRef.ObjectId, resultBrep, out error))
                {
                    ThreadCommandSupport.ShowError(error);
                    return Result.Failure;
                }
            }

            RhinoApp.WriteLine("RhinoThread: recursos de rosca atualizados.");
            return Result.Success;
        }
    }
}
