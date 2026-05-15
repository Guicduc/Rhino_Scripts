// #! csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Eto.Forms;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Rhino.Input.Custom;

NestingRhinoScript.Run();

enum RotationMode
{
    None,
    Half,
    Quarter,
    Free,
}

sealed class NestingCancelledException : Exception
{
    public NestingCancelledException() : base("Nesting cancelled.") { }
}

sealed class OrientationInfo
{
    public double Angle;
    public bool Mirrored;
    public double ContentWidth;
    public double ContentHeight;
    public List<Curve> DisplayCurves = new List<Curve>();
    public List<Curve> CollisionCurves = new List<Curve>();
    public List<Brep> MaterialBreps = new List<Brep>();
    public List<Point3d> MaterialPoints = new List<Point3d>();
    public List<Point3d> AnchorPoints = new List<Point3d>();
}

sealed class PartInfo
{
    public string PartKey;
    public string Label;
    public List<Guid> ObjectIds = new List<Guid>();
    public List<Curve> Curves2d = new List<Curve>();
    public List<Curve> ClosedCurves2d = new List<Curve>();
    public Point3d Center;
    public List<OrientationInfo> Orientations = new List<OrientationInfo>();
    public double Width;
    public double Height;
    public int Quantity = 1;
}

sealed class PackInput
{
    public string PartKey;
    public string Label;
    public int InstanceIndex;
    public List<OrientationInfo> Orientations = new List<OrientationInfo>();
}

sealed class PlacementInfo
{
    public string PartKey;
    public string Label;
    public int InstanceIndex;
    public double X;
    public double Y;
    public double Width;
    public double Height;
    public OrientationInfo Orientation;
    public List<Curve> CollisionCurves = new List<Curve>();
    public List<Brep> MaterialBreps = new List<Brep>();
    public List<Point3d> MaterialPoints = new List<Point3d>();
    public List<Point3d> AnchorPoints = new List<Point3d>();
    public BoundingBox Bounds;
    public Tuple<int, double, double, double, double> Score;
}

sealed class SheetInfo
{
    public int Index;
    public double Width;
    public double Height;
    public List<PlacementInfo> Placements = new List<PlacementInfo>();
}

sealed class LayoutResult
{
    public double SheetWidth;
    public double SheetHeight;
    public List<SheetInfo> Sheets = new List<SheetInfo>();
}

sealed class NestingSettings
{
    public double SheetWidth;
    public double SheetHeight;
    public double Spacing;
    public int SetCount;
    public RotationMode RotationMode;
    public bool AllowMirroring;
}

sealed class NestingProgress : IDisposable
{
    private readonly int _total;
    private bool _shown;
    private bool _cancelled;
    private int _tick;

    public NestingProgress(int total, string label)
    {
        _total = Math.Max(1, total);
        _shown = Rhino.UI.StatusBar.ShowProgressMeter(0, _total, label, true, true) != 0;
        RhinoApp.EscapeKeyPressed += OnEscapeKeyPressed;
        SetMessage(label);
        RhinoApp.Wait();
    }

    public void Dispose()
    {
        RhinoApp.EscapeKeyPressed -= OnEscapeKeyPressed;
        if (_shown)
            Rhino.UI.StatusBar.HideProgressMeter();
        SetMessage(string.Empty);
    }

    private void OnEscapeKeyPressed(object sender, EventArgs e)
    {
        _cancelled = true;
    }

    private static void SetMessage(string message)
    {
        try { Rhino.UI.StatusBar.SetMessagePane(message); }
        catch { }
    }

    public void CheckCancelled()
    {
        _tick++;
        if (_tick % 250 != 0)
            return;
        RhinoApp.Wait();
        if (_cancelled)
            throw new NestingCancelledException();
    }

    public void Update(int position, string label)
    {
        SetMessage(label);
        Rhino.UI.StatusBar.UpdateProgressMeter(Math.Max(0, Math.Min(_total, position)), true);
        RhinoApp.Wait();
        if (_cancelled)
            throw new NestingCancelledException();
    }
}

sealed class NestingDialog : Dialog<bool>
{
    private readonly List<PartInfo> _parts;
    private readonly ListBox _partList = new ListBox();
    private readonly NumericStepper _quantity = new NumericStepper { MinValue = 1, MaxValue = 999, DecimalPlaces = 0, Value = 1 };
    private readonly NumericStepper _sheetWidth = new NumericStepper { MinValue = 1, MaxValue = 100000, DecimalPlaces = 2, Value = 2440 };
    private readonly NumericStepper _sheetHeight = new NumericStepper { MinValue = 1, MaxValue = 100000, DecimalPlaces = 2, Value = 1220 };
    private readonly NumericStepper _spacing = new NumericStepper { MinValue = 0, MaxValue = 1000, DecimalPlaces = 2, Value = 10 };
    private readonly NumericStepper _setCount = new NumericStepper { MinValue = 1, MaxValue = 999, DecimalPlaces = 0, Value = 1 };
    private readonly DropDown _rotation = new DropDown();
    private readonly CheckBox _allowMirroring = new CheckBox { Text = "Allow mirroring", Checked = false };

    public NestingSettings ResultSettings;

    public NestingDialog(List<PartInfo> parts)
    {
        _parts = parts;
        Title = "Basic Rhino Nesting (C#)";
        Padding = new Eto.Drawing.Padding(12);
        Resizable = true;
        ClientSize = new Eto.Drawing.Size(720, 500);
        _partList.Height = 260;

        _rotation.DataStore = new[] { "No rotation", "180 only", "90 degree steps", "Free rotation (15 degree steps)" };
        _rotation.SelectedIndex = 2;
        _partList.SelectedIndexChanged += (_, __) => SyncSelection();
        _quantity.ValueChanged += (_, __) => UpdateQuantity();

        var highlight = new Button { Text = "Highlight Selected" };
        highlight.Click += (_, __) => HighlightSelected();

        var refresh = new Button { Text = "Refresh Orientations" };
        refresh.Click += (_, __) => RefreshRotations();

        var run = new Button { Text = "Run Nesting" };
        run.Click += (_, __) => RunClicked();

        var cancel = new Button { Text = "Cancel" };
        cancel.Click += (_, __) => Close(false);

        var layout = new DynamicLayout { Spacing = new Eto.Drawing.Size(8, 8) };
        layout.Add(new Label { Text = "Select a part, edit quantity, and use Highlight Selected to identify it in Rhino." });
        layout.Add(_partList);
        layout.Add(new StackLayout { Orientation = Orientation.Horizontal, Items = { new Label { Text = "Quantity" }, _quantity, highlight } });
        layout.Add(new StackLayout { Orientation = Orientation.Horizontal, Items = { new Label { Text = "Sheet width" }, _sheetWidth, new Label { Text = "Sheet height" }, _sheetHeight } });
        layout.Add(new StackLayout { Orientation = Orientation.Horizontal, Items = { new Label { Text = "Number of sets" }, _setCount } });
        layout.Add(new StackLayout { Orientation = Orientation.Horizontal, Items = { new Label { Text = "Spacing" }, _spacing, new Label { Text = "Rotation" }, _rotation, _allowMirroring, refresh } });
        layout.Add(new StackLayout { Orientation = Orientation.Horizontal, Items = { cancel, run } });
        Content = layout;

        RefreshList();
        if (_parts.Count > 0)
            _partList.SelectedIndex = 0;
    }

    private void RefreshList()
    {
        _partList.DataStore = _parts.Select(p => string.Format("{0} | {1:0.##} x {2:0.##} | Qty {3}", p.Label, p.Width, p.Height, p.Quantity)).ToList();
    }

    private PartInfo SelectedPart => _partList.SelectedIndex >= 0 && _partList.SelectedIndex < _parts.Count ? _parts[_partList.SelectedIndex] : null;

    private void SyncSelection()
    {
        var part = SelectedPart;
        if (part == null)
            return;
        _quantity.Value = part.Quantity;
        Highlight(part);
    }

    private void UpdateQuantity()
    {
        var part = SelectedPart;
        if (part == null)
            return;
        part.Quantity = Math.Max(1, (int)Math.Round(_quantity.Value));
        var index = _partList.SelectedIndex;
        RefreshList();
        _partList.SelectedIndex = index;
    }

    private void HighlightSelected()
    {
        var part = SelectedPart;
        if (part != null)
            Highlight(part);
    }

    private static void Highlight(PartInfo part)
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null)
            return;
        doc.Objects.UnselectAll();
        foreach (var id in part.ObjectIds)
        {
            var rhinoObject = doc.Objects.FindId(id);
            if (rhinoObject != null)
                rhinoObject.Select(true);
        }
        doc.Views.Redraw();
    }

    private void RefreshRotations()
    {
        var spacing = _spacing.Value;
        var mode = RotationValue();
        var allowMirroring = _allowMirroring.Checked == true;
        foreach (var part in _parts)
            part.Orientations = NestingRhinoScript.BuildOrientations(part.Curves2d, part.ClosedCurves2d, part.Center, mode, spacing, allowMirroring);
        MessageBox.Show(this, "Orientation candidates refreshed.", "Nesting");
    }

    private RotationMode RotationValue()
    {
        if (_rotation.SelectedIndex == 0) return RotationMode.None;
        if (_rotation.SelectedIndex == 1) return RotationMode.Half;
        if (_rotation.SelectedIndex == 2) return RotationMode.Quarter;
        return RotationMode.Free;
    }

    private void RunClicked()
    {
        var spacing = _spacing.Value;
        var mode = RotationValue();
        var allowMirroring = _allowMirroring.Checked == true;
        foreach (var part in _parts)
        {
            part.Orientations = NestingRhinoScript.BuildOrientations(part.Curves2d, part.ClosedCurves2d, part.Center, mode, spacing, allowMirroring);
            if (part.Orientations.Count == 0)
            {
                MessageBox.Show(this, string.Format("Failed to build orientation candidates for '{0}'.", part.Label), "Nesting");
                return;
            }
        }

        ResultSettings = new NestingSettings
        {
            SheetWidth = _sheetWidth.Value,
            SheetHeight = _sheetHeight.Value,
            Spacing = spacing,
            SetCount = Math.Max(1, (int)Math.Round(_setCount.Value)),
            RotationMode = mode,
            AllowMirroring = allowMirroring,
        };
        Close(true);
    }
}

static class NestingRhinoScript
{
    private const int FreeRotationStepDegrees = 15;
    private const double SheetDisplayGap = 50.0;
    private const int AnchorSamplesPerCurve = 8;
    private const int MaxAnchorPoints = 20;
    private const int MaxGuideValues = 32;
    private static string DebugLogPath => System.Environment.GetEnvironmentVariable("NESTING_DEBUG_LOG");

    private static void DebugLog(string message)
    {
        if (string.IsNullOrWhiteSpace(DebugLogPath))
            return;

        try
        {
            File.AppendAllText(DebugLogPath, message + System.Environment.NewLine);
        }
        catch
        {
        }
    }

    public static void Run()
    {
        var doc = RhinoDoc.ActiveDoc;
        if (doc == null)
            return;

        try
        {
            var ids = SelectCurveIds();
            if (ids == null || ids.Count == 0)
                return;

            Transform toWorld;
            Transform fromWorld;
            var parts = BuildParts(ids, 10.0, RotationMode.Quarter, false, out toWorld, out fromWorld);
            if (parts.Count == 0)
            {
                Rhino.UI.Dialogs.ShowMessage("No valid planar curves were found.", "Nesting");
                return;
            }

            var dialog = new NestingDialog(parts);
            if (!dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow))
                return;

            Dictionary<string, PartInfo> lookup;
            var inputs = BuildPackInputs(parts, dialog.ResultSettings.SetCount, out lookup);
            LayoutResult layout;
            using (var progress = new NestingProgress(inputs.Count, "Computing nesting"))
                layout = ShapeAwarePack(dialog.ResultSettings.SheetWidth, dialog.ResultSettings.SheetHeight, inputs, dialog.ResultSettings.Spacing, progress);

            DrawLayout(doc, layout, fromWorld);
            Rhino.UI.Dialogs.ShowMessage(string.Format("Created {0} sheet(s) with {1} part instance(s).", layout.Sheets.Count, inputs.Count), "Nesting complete");
        }
        catch (NestingCancelledException)
        {
            Rhino.UI.Dialogs.ShowMessage("Nesting cancelled.", "Nesting");
        }
        catch (Exception ex)
        {
            Rhino.UI.Dialogs.ShowMessage(ex.Message, "Nesting C#");
        }
    }

    private static double Tolerance => RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01;

    private static List<double> RotationAngles(RotationMode mode)
    {
        if (mode == RotationMode.None) return new List<double> { 0.0 };
        if (mode == RotationMode.Half) return new List<double> { 0.0, 180.0 };
        if (mode == RotationMode.Quarter) return new List<double> { 0.0, 90.0, 180.0, 270.0 };
        return Enumerable.Range(0, 360 / FreeRotationStepDegrees).Select(i => i * (double)FreeRotationStepDegrees).ToList();
    }

    private static IEnumerable<Tuple<double, bool>> OrientationVariants(RotationMode mode, bool allowMirroring)
    {
        var mirrorStates = allowMirroring ? new[] { false, true } : new[] { false };
        foreach (var mirrored in mirrorStates)
            foreach (var angle in RotationAngles(mode))
                yield return Tuple.Create(angle, mirrored);
    }

    private static List<Guid> SelectCurveIds()
    {
        var get = new GetObject();
        get.SetCommandPrompt("Select planar curves to nest");
        get.GeometryFilter = ObjectType.Curve;
        get.GroupSelect = true;
        get.SubObjectSelect = false;
        get.EnablePreSelect(true, true);
        get.GetMultiple(1, 0);
        if (get.CommandResult() != Rhino.Commands.Result.Success)
            return null;
        return Enumerable.Range(0, get.ObjectCount).Select(i => get.Object(i).ObjectId).ToList();
    }

    private static List<PartInfo> BuildParts(List<Guid> ids, double spacing, RotationMode mode, bool allowMirroring, out Transform toWorld, out Transform fromWorld)
    {
        var doc = RhinoDoc.ActiveDoc;
        var active = doc.Views.ActiveView;
        if (active != null)
        {
            var plane = active.ActiveViewport.ConstructionPlane();
            toWorld = Transform.PlaneToPlane(plane, Plane.WorldXY);
            fromWorld = Transform.PlaneToPlane(Plane.WorldXY, plane);
        }
        else
        {
            toWorld = Transform.Identity;
            fromWorld = Transform.Identity;
        }

        var processed = new HashSet<string>();
        var parts = new List<PartInfo>();
        var counter = 1;
        foreach (var id in ids)
        {
            var rhinoObject = doc.Objects.FindId(id);
            if (rhinoObject == null)
                continue;

            var memberIds = GroupMemberIds(rhinoObject);
            var partKey = memberIds.Count > 1 ? string.Join("|", memberIds.OrderBy(x => x)) : id.ToString();
            if (processed.Contains(partKey))
                continue;

            var curves = new List<Curve>();
            var sourceBounds = BoundingBox.Empty;
            var directClosedCount = 0;
            foreach (var memberId in memberIds)
            {
                var member = doc.Objects.FindId(memberId);
                var curveObj = member?.Geometry as Curve;
                if (curveObj == null)
                    continue;
                var sourceBox = curveObj.GetBoundingBox(true);
                if (sourceBox.IsValid)
                    sourceBounds = sourceBounds.IsValid ? BoundingBox.Union(sourceBounds, sourceBox) : sourceBox;
                var curve = curveObj.DuplicateCurve();
                curve.Transform(toWorld);
                curve.Transform(Transform.PlanarProjection(Plane.WorldXY));
                curves.Add(curve);
                if (curve.IsClosed && curve.IsPlanar(Tolerance))
                    directClosedCount++;
            }

            if (curves.Count == 0)
                continue;

            var closed = CollectMaterialCurves(curves);

            var bbox = CombineBoundingBox(curves);
            if (!bbox.IsValid)
                continue;

            var label = !string.IsNullOrWhiteSpace(rhinoObject.Attributes.Name) ? rhinoObject.Attributes.Name : string.Format("Part {0}", counter);
            DebugLog(string.Format(
                "part={0} curves={1} direct_closed={2} material_loops={3} source_z=[{4:0.####},{5:0.####}]",
                label,
                curves.Count,
                directClosedCount,
                closed.Count,
                sourceBounds.IsValid ? sourceBounds.Min.Z : 0.0,
                sourceBounds.IsValid ? sourceBounds.Max.Z : 0.0));

            var part = new PartInfo
            {
                PartKey = partKey,
                Label = label,
                ObjectIds = memberIds,
                Curves2d = curves,
                ClosedCurves2d = closed,
                Center = bbox.Center,
                Width = bbox.Max.X - bbox.Min.X,
                Height = bbox.Max.Y - bbox.Min.Y,
            };
            part.Orientations = BuildOrientations(curves, closed, part.Center, mode, spacing, allowMirroring);
            if (part.Orientations.Count > 0)
            {
                parts.Add(part);
                processed.Add(partKey);
                counter++;
            }
        }

        return parts;
    }

    private static List<Guid> GroupMemberIds(RhinoObject obj)
    {
        var groups = obj.Attributes.GetGroupList();
        if (groups == null || groups.Length == 0)
            return new List<Guid> { obj.Id };
        var members = RhinoDoc.ActiveDoc.Groups.GroupMembers(groups[0]);
        if (members == null)
            return new List<Guid> { obj.Id };
        return members.Where(x => x != null && x.ObjectType == ObjectType.Curve).Select(x => x.Id).ToList();
    }

    private static BoundingBox CombineBoundingBox(IEnumerable<Curve> curves)
    {
        var bbox = BoundingBox.Empty;
        foreach (var curve in curves)
        {
            var curveBox = curve.GetBoundingBox(true);
            if (!curveBox.IsValid)
                continue;
            bbox = bbox.IsValid ? BoundingBox.Union(bbox, curveBox) : curveBox;
        }
        return bbox;
    }

    private static string CurveSignature(Curve curve)
    {
        if (curve == null)
            return null;
        var bbox = curve.GetBoundingBox(true);
        if (!bbox.IsValid)
            return null;
        return string.Format(
            "{0:0.####}|{1:0.####}|{2:0.####}|{3:0.####}|{4:0.####}",
            bbox.Min.X,
            bbox.Min.Y,
            bbox.Max.X,
            bbox.Max.Y,
            curve.GetLength());
    }

    private static void TryAddMaterialCurve(List<Curve> materialCurves, HashSet<string> seen, Curve curve)
    {
        if (curve == null || !curve.IsClosed || !curve.IsPlanar(Tolerance))
            return;

        var signature = CurveSignature(curve);
        if (signature != null && !seen.Add(signature))
            return;

        materialCurves.Add(curve.DuplicateCurve());
    }

    private static List<Curve> CollectMaterialCurves(List<Curve> curves)
    {
        var materialCurves = new List<Curve>();
        var seen = new HashSet<string>();

        foreach (var curve in curves)
            TryAddMaterialCurve(materialCurves, seen, curve);

        var joined = Curve.JoinCurves(curves.Select(c => c.DuplicateCurve()).ToList(), Tolerance);
        if (joined != null)
        {
            foreach (var curve in joined)
                TryAddMaterialCurve(materialCurves, seen, curve);
        }

        return materialCurves;
    }

    public static List<OrientationInfo> BuildOrientations(List<Curve> curves2d, List<Curve> closedCurves2d, Point3d center, RotationMode mode, double spacing, bool allowMirroring)
    {
        var result = new List<OrientationInfo>();
        var seen = new HashSet<string>();
        foreach (var variant in OrientationVariants(mode, allowMirroring))
        {
            var angle = variant.Item1;
            var mirrored = variant.Item2;
            var rotated = curves2d.Select(c => c.DuplicateCurve()).ToList();
            var rotatedClosed = closedCurves2d.Select(c => c.DuplicateCurve()).ToList();
            if (mirrored)
            {
                var mirror = Transform.Scale(new Plane(center, Vector3d.XAxis, Vector3d.YAxis), -1.0, 1.0, 1.0);
                rotated.ForEach(c => c.Transform(mirror));
                rotatedClosed.ForEach(c => c.Transform(mirror));
            }
            if (Math.Abs(angle) > Tolerance)
            {
                var rotation = Transform.Rotation(RhinoMath.ToRadians(angle), Vector3d.ZAxis, center);
                rotated.ForEach(c => c.Transform(rotation));
                rotatedClosed.ForEach(c => c.Transform(rotation));
            }

            var bbox = CombineBoundingBox(rotated);
            if (!bbox.IsValid)
                continue;

            var width = bbox.Max.X - bbox.Min.X;
            var height = bbox.Max.Y - bbox.Min.Y;
            var key = string.Format("{0:0.#####}|{1:0.#####}|{2:0.#####}|{3}", width, height, angle % 360.0, mirrored ? 1 : 0);
            if (!seen.Add(key))
                continue;

            var normalize = Transform.Translation(-bbox.Min.X, -bbox.Min.Y, 0.0);
            rotated.ForEach(c => c.Transform(normalize));
            rotatedClosed.ForEach(c => c.Transform(normalize));
            var normalized = CombineBoundingBox(rotated);
            var collisionCurves = rotatedClosed.Count > 0 ? rotatedClosed.Select(c => c.DuplicateCurve()).ToList() : rotated.Select(c => c.DuplicateCurve()).ToList();
            var materialBreps = Brep.CreatePlanarBreps(rotatedClosed, Tolerance)?.Where(b => b != null).Select(b => b.DuplicateBrep()).ToList() ?? new List<Brep>();
            var materialPoints = materialBreps.SelectMany(MaterialReferencePoints).ToList();

            result.Add(new OrientationInfo
            {
                Angle = angle,
                Mirrored = mirrored,
                ContentWidth = width,
                ContentHeight = height,
                DisplayCurves = rotated,
                CollisionCurves = collisionCurves,
                MaterialBreps = materialBreps,
                MaterialPoints = materialPoints,
                AnchorPoints = CollectAnchorPoints(collisionCurves.Count > 0 ? collisionCurves : rotated, normalized),
            });
        }

        return result.OrderBy(x => x.ContentWidth * x.ContentHeight).ThenBy(x => x.ContentWidth).ThenBy(x => x.ContentHeight).ThenBy(x => x.Mirrored ? 1 : 0).ToList();
    }

    private static IEnumerable<Point3d> MaterialReferencePoints(Brep brep)
    {
        using (var area = AreaMassProperties.Compute(brep))
        {
            if (area != null)
                yield return area.Centroid;
        }
    }

    private static List<Point3d> CollectAnchorPoints(List<Curve> curves, BoundingBox bbox)
    {
        var points = new List<Point3d>
        {
            new Point3d(bbox.Min.X, bbox.Min.Y, 0),
            new Point3d(bbox.Max.X, bbox.Min.Y, 0),
            new Point3d(bbox.Min.X, bbox.Max.Y, 0),
            new Point3d(bbox.Max.X, bbox.Max.Y, 0),
            bbox.Center,
        };

        foreach (var curve in curves)
        {
            var parameters = curve.DivideByCount(Math.Max(2, AnchorSamplesPerCurve), true);
            if (parameters != null)
                points.AddRange(parameters.Select(curve.PointAt));
        }

        return CapPoints(points);
    }

    private static List<Point3d> CapPoints(IEnumerable<Point3d> points)
    {
        var unique = points.GroupBy(p => string.Format("{0:0.####}|{1:0.####}", p.X, p.Y)).Select(g => g.First()).ToList();
        if (unique.Count <= MaxAnchorPoints)
            return unique;
        return Enumerable.Range(0, MaxAnchorPoints).Select(i => unique[(int)Math.Round(i * (unique.Count - 1.0) / Math.Max(1, MaxAnchorPoints - 1.0))]).Distinct().ToList();
    }

    private static List<double> CapValues(IEnumerable<double> values)
    {
        var unique = values.Select(v => Math.Round(v, 4)).Distinct().OrderBy(v => v).ToList();
        if (unique.Count <= MaxGuideValues)
            return unique;
        return Enumerable.Range(0, MaxGuideValues).Select(i => unique[(int)Math.Round(i * (unique.Count - 1.0) / Math.Max(1, MaxGuideValues - 1.0))]).Distinct().OrderBy(v => v).ToList();
    }

    private static List<PackInput> BuildPackInputs(List<PartInfo> parts, int setCount, out Dictionary<string, PartInfo> lookup)
    {
        lookup = parts.ToDictionary(p => p.PartKey, p => p);
        var multiplier = Math.Max(1, setCount);
        return parts.SelectMany(p => Enumerable.Range(0, p.Quantity * multiplier).Select(i => new PackInput { PartKey = p.PartKey, Label = p.Label, InstanceIndex = i, Orientations = p.Orientations })).ToList();
    }

    private static bool IsBetterScore(Tuple<int, double, double, double, double> candidate, Tuple<int, double, double, double, double> current)
    {
        if (current == null)
            return true;
        if (candidate == null)
            return false;

        if (candidate.Item1 != current.Item1) return candidate.Item1 < current.Item1;
        if (candidate.Item2 != current.Item2) return candidate.Item2 < current.Item2;
        if (candidate.Item3 != current.Item3) return candidate.Item3 < current.Item3;
        if (candidate.Item4 != current.Item4) return candidate.Item4 < current.Item4;
        return candidate.Item5 < current.Item5;
    }

    private static LayoutResult ShapeAwarePack(double sheetWidth, double sheetHeight, List<PackInput> inputs, double spacing, NestingProgress progress)
    {
        var ordered = inputs.OrderByDescending(i => i.Orientations.Max(o => o.ContentWidth * o.ContentHeight)).ThenByDescending(i => i.Orientations.Max(o => Math.Max(o.ContentWidth, o.ContentHeight))).ToList();
        var sheets = new List<SheetInfo>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var input = ordered[index];
            progress.Update(index, string.Format("Placing {0} ({1}/{2})", input.Label, index + 1, ordered.Count));
            PlacementInfo best = null;
            SheetInfo bestSheet = null;
            foreach (var sheet in sheets)
            {
                progress.CheckCancelled();
                var placement = FindPlacement(sheet, input, spacing, progress);
                if (placement != null && (best == null || IsBetterScore(placement.Score, best.Score)))
                {
                    best = placement;
                    bestSheet = sheet;
                }
            }

            if (best == null)
            {
                var newSheet = new SheetInfo { Index = sheets.Count, Width = sheetWidth, Height = sheetHeight };
                best = FindPlacement(newSheet, input, spacing, progress);
                if (best == null)
                    throw new Exception(string.Format("Part '{0}' does not fit on a {1} x {2} sheet with the current spacing/rotation/mirroring settings.", input.Label, sheetWidth, sheetHeight));
                sheets.Add(newSheet);
                bestSheet = newSheet;
            }

            bestSheet.Placements.Add(best);
            progress.Update(index + 1, string.Format("Placed {0} ({1}/{2})", input.Label, index + 1, ordered.Count));
        }

        return new LayoutResult { SheetWidth = sheetWidth, SheetHeight = sheetHeight, Sheets = sheets };
    }

    private static PlacementInfo FindPlacement(SheetInfo sheet, PackInput input, double spacing, NestingProgress progress)
    {
        PlacementInfo best = null;
        foreach (var orientation in input.Orientations)
        {
            var xValues = OrientationCandidateXValues(sheet, orientation, spacing);
            var yValues = OrientationCandidateYValues(sheet, orientation, spacing);
            foreach (var anchor in orientation.AnchorPoints)
            {
                progress.CheckCancelled();
                foreach (var xValue in xValues)
                {
                    var x = xValue - anchor.X;
                    foreach (var yValue in yValues)
                    {
                        var y = yValue - anchor.Y;
                        if (!PlacementWithinSheet(x, y, orientation, sheet.Width, sheet.Height, spacing))
                            continue;

                        var collisionCurves = orientation.CollisionCurves.Select(c => { var d = c.DuplicateCurve(); d.Translate(x, y, 0); return d; }).ToList();
                        var materialBreps = orientation.MaterialBreps.Select(b => { var d = b.DuplicateBrep(); d.Translate(x, y, 0); return d; }).ToList();
                        var materialPoints = orientation.MaterialPoints.Select(p => new Point3d(p.X + x, p.Y + y, 0)).ToList();
                        var bbox = new BoundingBox(new Point3d(x, y, 0), new Point3d(x + orientation.ContentWidth, y + orientation.ContentHeight, 0));

                        if (CandidateCollides(sheet, collisionCurves, materialBreps, materialPoints, bbox, spacing))
                            continue;

                        var placement = new PlacementInfo
                        {
                            PartKey = input.PartKey,
                            Label = input.Label,
                            InstanceIndex = input.InstanceIndex,
                            X = x,
                            Y = y,
                            Width = orientation.ContentWidth,
                            Height = orientation.ContentHeight,
                            Orientation = orientation,
                            CollisionCurves = collisionCurves,
                            MaterialBreps = materialBreps,
                            MaterialPoints = materialPoints,
                            AnchorPoints = orientation.AnchorPoints.Select(p => new Point3d(p.X + x, p.Y + y, 0)).ToList(),
                            Bounds = bbox,
                            Score = Tuple.Create(sheet.Index, y + orientation.ContentHeight, x, x + orientation.ContentWidth, y),
                        };

                        if (best == null || IsBetterScore(placement.Score, best.Score))
                            best = placement;
                    }
                }
            }
        }
        return best;
    }

    private static List<double> OrientationCandidateXValues(SheetInfo sheet, OrientationInfo orientation, double spacing)
    {
        var margin = spacing * 0.5;
        var values = new List<double> { margin, Math.Max(margin, sheet.Width - margin - orientation.ContentWidth) };
        foreach (var placement in sheet.Placements)
        {
            values.AddRange(new[] { placement.X, placement.X + placement.Width, placement.X - spacing, placement.X + placement.Width + spacing });
            values.AddRange(placement.AnchorPoints.SelectMany(p => new[] { p.X - spacing, p.X, p.X + spacing }));
        }
        return CapValues(values);
    }

    private static List<double> OrientationCandidateYValues(SheetInfo sheet, OrientationInfo orientation, double spacing)
    {
        var margin = spacing * 0.5;
        var values = new List<double> { margin, Math.Max(margin, sheet.Height - margin - orientation.ContentHeight) };
        foreach (var placement in sheet.Placements)
        {
            values.AddRange(new[] { placement.Y, placement.Y + placement.Height, placement.Y - spacing, placement.Y + placement.Height + spacing });
            values.AddRange(placement.AnchorPoints.SelectMany(p => new[] { p.Y - spacing, p.Y, p.Y + spacing }));
        }
        return CapValues(values);
    }

    private static bool PlacementWithinSheet(double x, double y, OrientationInfo orientation, double sheetWidth, double sheetHeight, double spacing)
    {
        var margin = spacing * 0.5;
        return x >= margin - Tolerance && y >= margin - Tolerance && x + orientation.ContentWidth <= sheetWidth - margin + Tolerance && y + orientation.ContentHeight <= sheetHeight - margin + Tolerance;
    }

    private static bool CandidateCollides(SheetInfo sheet, List<Curve> collisionCurves, List<Brep> materialBreps, List<Point3d> materialPoints, BoundingBox bounds, double spacing)
    {
        foreach (var placement in sheet.Placements)
        {
            if (!ExpandedOverlap(bounds, placement.Bounds, spacing))
                continue;

            var intersects = CurveSetsIntersect(collisionCurves, placement.CollisionCurves);
            if (intersects && spacing > Tolerance)
                return true;

            if (spacing > Tolerance)
            {
                var distance = CurveSetsMinDistance(collisionCurves, placement.CollisionCurves);
                if (distance.HasValue && distance.Value < spacing - Tolerance)
                    return true;
            }

            if (materialPoints.Any(p => PointInAnyBrep(p, placement.MaterialBreps)))
                return true;
            if (placement.MaterialPoints.Any(p => PointInAnyBrep(p, materialBreps)))
                return true;
        }

        return false;
    }

    private static bool ExpandedOverlap(BoundingBox a, BoundingBox b, double amount)
    {
        var expandedA = new BoundingBox(new Point3d(a.Min.X - amount, a.Min.Y - amount, 0), new Point3d(a.Max.X + amount, a.Max.Y + amount, 0));
        var expandedB = new BoundingBox(new Point3d(b.Min.X - amount, b.Min.Y - amount, 0), new Point3d(b.Max.X + amount, b.Max.Y + amount, 0));
        return !(expandedA.Max.X <= expandedB.Min.X || expandedB.Max.X <= expandedA.Min.X || expandedA.Max.Y <= expandedB.Min.Y || expandedB.Max.Y <= expandedA.Min.Y);
    }

    private static bool CurveSetsIntersect(List<Curve> a, List<Curve> b)
    {
        foreach (var curveA in a)
            foreach (var curveB in b)
                if (Intersection.CurveCurve(curveA, curveB, Tolerance, Tolerance)?.Count > 0)
                    return true;
        return false;
    }

    private static double? CurveSetsMinDistance(List<Curve> a, List<Curve> b)
    {
        double? minimum = null;
        foreach (var curveA in a)
        {
            foreach (var curveB in b)
            {
                Point3d pa;
                Point3d pb;
                if (!curveA.ClosestPoints(curveB, out pa, out pb))
                    continue;
                var distance = pa.DistanceTo(pb);
                if (!minimum.HasValue || distance < minimum.Value)
                    minimum = distance;
            }
        }
        return minimum;
    }

    private static bool PointInAnyBrep(Point3d point, List<Brep> breps)
    {
        foreach (var brep in breps)
        {
            foreach (var face in brep.Faces)
            {
                double u;
                double v;
                if (!face.ClosestPoint(point, out u, out v))
                    continue;
                var facePoint = face.PointAt(u, v);
                if (facePoint.DistanceTo(point) > Tolerance)
                    continue;
                if (face.IsPointOnFace(u, v) == PointFaceRelation.Interior)
                    return true;
            }
        }
        return false;
    }

    private static void DrawLayout(RhinoDoc doc, LayoutResult layout, Transform fromWorld)
    {
        var sheetLayer = EnsureLayer(doc, "Nesting Output - Sheets", Color.DarkRed);
        var partLayer = EnsureLayer(doc, "Nesting Output - Parts", Color.DarkBlue);
        foreach (var sheet in layout.Sheets)
        {
            var originX = sheet.Index * (layout.SheetWidth + SheetDisplayGap);
            DrawSheet(doc, originX, 0, layout.SheetWidth, layout.SheetHeight, fromWorld, sheetLayer);
            foreach (var placement in sheet.Placements)
                DrawPlacement(doc, placement, originX, 0, fromWorld, partLayer);
        }
        doc.Views.Redraw();
    }

    private static int EnsureLayer(RhinoDoc doc, string name, Color color)
    {
        var existing = doc.Layers.FindByFullPath(name, -1);
        if (existing >= 0)
            return existing;
        return doc.Layers.Add(new Layer { Name = name, Color = color });
    }

    private static void DrawSheet(RhinoDoc doc, double x, double y, double width, double height, Transform fromWorld, int layerIndex)
    {
        var polyline = new Polyline(new[] { new Point3d(x, y, 0), new Point3d(x + width, y, 0), new Point3d(x + width, y + height, 0), new Point3d(x, y + height, 0), new Point3d(x, y, 0) });
        var curve = new PolylineCurve(polyline);
        curve.Transform(fromWorld);
        doc.Objects.AddCurve(curve, new ObjectAttributes { LayerIndex = layerIndex });
    }

    private static void DrawPlacement(RhinoDoc doc, PlacementInfo placement, double originX, double originY, Transform fromWorld, int layerIndex)
    {
        var translation = Transform.Translation(originX + placement.X, originY + placement.Y, 0);
        foreach (var curve in placement.Orientation.DisplayCurves)
        {
            var duplicate = curve.DuplicateCurve();
            duplicate.Transform(translation);
            duplicate.Transform(fromWorld);
            doc.Objects.AddCurve(duplicate, new ObjectAttributes { LayerIndex = layerIndex });
        }
    }
}
