using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Geometry;

namespace ThreadRhino
{
    internal sealed class ThreadDialog : Dialog<bool>
    {
        private readonly RhinoDoc _doc;
        private readonly Brep _baseBrep;
        private readonly List<ThreadFeatureDefinition> _features;
        private readonly bool _editMode;
        private readonly ThreadPreviewConduit _preview;
        private readonly UITimer _previewTimer = new UITimer { Interval = 0.35 };
        private readonly List<ThreadCatalogEntry> _availableCatalog = new List<ThreadCatalogEntry>();
        private readonly List<double> _availablePitches = new List<double>();

        private readonly ListBox _featureList = new ListBox { Height = 105 };
        private readonly DropDown _kind = new DropDown { Width = 150 };
        private readonly DropDown _mode = new DropDown { Width = 150 };
        private readonly DropDown _size = new DropDown { Width = 120 };
        private readonly DropDown _pitchList = new DropDown { Width = 120 };
        private readonly DropDown _hand = new DropDown { Width = 150 };
        private readonly NumericStepper _nominal = Number(0.1, 10000.0, 2);
        private readonly NumericStepper _customPitch = Number(0.01, 1000.0, 1);
        private readonly NumericStepper _offset = Number(0.0, 1000000.0, 0.0);
        private readonly NumericStepper _length = Number(0.001, 1000000.0, 1.0);
        private readonly NumericStepper _clearance = Number(0.0, 1000.0, 0.20);
        private readonly CheckBox _fullLength = new CheckBox { Text = "Comprimento total", Checked = true };
        private readonly CheckBox _invertStart = new CheckBox { Text = "Inverter lado inicial", Checked = false };
        private readonly Label _measured = new Label { Wrap = WrapMode.Word };
        private readonly Label _status = new Label { Wrap = WrapMode.Word };
        private readonly Button _delete = new Button { Text = "Excluir rosca" };
        private readonly Button _ok = new Button { Text = "Aplicar", Enabled = false };
        private readonly Button _cancel = new Button { Text = "Cancelar" };

        private bool _loading;
        private bool _hasLoadedFeature;
        private int _selectedIndex = -1;
        private bool _loadedStartFromA;
        private Brep _latestPreview;

        public Brep ResultBrep { get; private set; }
        public List<ThreadFeatureDefinition> ResultFeatures { get; private set; }

        public ThreadDialog(
            RhinoDoc doc,
            Brep baseBrep,
            IEnumerable<ThreadFeatureDefinition> features,
            int selectedIndex,
            bool editMode)
        {
            _doc = doc;
            _baseBrep = baseBrep.DuplicateBrep();
            _features = ThreadDefinitionList.Duplicate(features);
            _editMode = editMode;
            _preview = new ThreadPreviewConduit(doc);

            Title = editMode ? "Editar roscas — RhinoThread" : "Criar rosca — RhinoThread";
            Padding = new Padding(12);
            Resizable = true;
            ClientSize = new Size(670, editMode ? 610 : 500);
            MinimumSize = new Size(620, editMode ? 560 : 450);

            _kind.DataStore = new[] { "Externa", "Interna" };
            _mode.DataStore = new[] { "ISO Métrica", "Custom" };
            _size.DataStore = new string[0];
            _pitchList.DataStore = new string[0];
            _hand.DataStore = new[] { "Direita", "Esquerda" };

            WireEvents();
            Content = BuildLayout();
            DefaultButton = _ok;
            AbortButton = _cancel;

            RefreshFeatureList();
            if (_features.Count > 0)
            {
                _selectedIndex = Math.Max(0, Math.Min(selectedIndex, _features.Count - 1));
                LoadFeature(_selectedIndex);
                _loading = true;
                try
                {
                    _featureList.SelectedIndex = _selectedIndex;
                }
                finally
                {
                    _loading = false;
                }
                _hasLoadedFeature = true;
            }
            else
            {
                SetEditorEnabled(false);
            }

            _previewTimer.Elapsed += OnPreviewTimer;
            Closed += OnDialogClosed;
            SchedulePreview();
        }

        private static NumericStepper Number(double min, double max, double value)
        {
            return new NumericStepper
            {
                MinValue = min,
                MaxValue = max,
                Value = value,
                DecimalPlaces = 3,
                Width = 120,
            };
        }

        private Control BuildLayout()
        {
            var layout = new DynamicLayout { Spacing = new Size(8, 8) };

            if (_editMode)
            {
                layout.Add(new Label { Text = "Recursos de rosca desta peça" });
                layout.Add(_featureList);
                layout.Add(new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Items = { _delete },
                });
            }

            layout.Add(new GroupBox
            {
                Text = "Especificação",
                Content = new StackLayout
                {
                    Padding = 8,
                    Spacing = 8,
                    Items =
                    {
                        Row("Tipo", _kind, "Padrão", _mode),
                        Row("Tamanho", _size, "Passo", _pitchList),
                        Row("Diâmetro nominal (mm)", _nominal, "Passo custom (mm)", _customPitch),
                        Row("Sentido", _hand, string.Empty, new Label()),
                    },
                },
            });

            layout.Add(new GroupBox
            {
                Text = "Posição e impressão 3D",
                Content = new StackLayout
                {
                    Padding = 8,
                    Spacing = 8,
                    Items =
                    {
                        new StackLayout { Orientation = Orientation.Horizontal, Spacing = 12, Items = { _fullLength, _invertStart } },
                        Row("Offset inicial (mm)", _offset, "Comprimento (mm)", _length),
                        Row("Compensação radial (mm)", _clearance, string.Empty, new Label()),
                        new Label { Text = "A compensação é aplicada a esta peça. Se for usada no macho e na fêmea, os afastamentos se somam.", Wrap = WrapMode.Word },
                    },
                },
            });

            layout.Add(_measured);
            layout.Add(_status);
            layout.Add(new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Items = { _cancel, _ok },
            });
            return layout;
        }

        private static StackLayout Row(string labelA, Control controlA, string labelB, Control controlB)
        {
            return new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Items =
                {
                    new Label { Text = labelA, Width = 145 },
                    controlA,
                    new Label { Text = labelB, Width = 130 },
                    controlB,
                },
            };
        }

        private void WireEvents()
        {
            _featureList.SelectedIndexChanged += OnFeatureSelectionChanged;
            _kind.SelectedIndexChanged += OnKindChanged;
            _mode.SelectedIndexChanged += OnModeChanged;
            _size.SelectedIndexChanged += OnSizeChanged;
            _pitchList.SelectedIndexChanged += OnControlChanged;
            _hand.SelectedIndexChanged += OnControlChanged;
            _nominal.ValueChanged += OnControlChanged;
            _customPitch.ValueChanged += OnControlChanged;
            _offset.ValueChanged += OnControlChanged;
            _length.ValueChanged += OnControlChanged;
            _clearance.ValueChanged += OnControlChanged;
            _fullLength.CheckedChanged += OnFullLengthChanged;
            _invertStart.CheckedChanged += OnControlChanged;
            _delete.Click += OnDeleteClicked;
            _ok.Click += OnApplyClicked;
            _cancel.Click += delegate { Close(false); };
        }

        private void OnControlChanged(object sender, EventArgs e)
        {
            if (!_loading)
                SchedulePreview();
        }

        private void OnKindChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;

            var feature = CurrentFeature();
            if (feature == null)
                return;

            var desiredSize = SelectedCatalogEntry() != null ? SelectedCatalogEntry().Name : feature.SizeName;
            var desiredPitch = CurrentPitchMillimeters(feature);
            _loading = true;
            try
            {
                var hasIsoMatch = RefreshCompatibleCatalog(feature, desiredSize, desiredPitch);
                if (!hasIsoMatch || _mode.SelectedIndex == 1)
                    SwitchToCustomForMeasuredFace(feature, desiredPitch);
                UpdateModeControls();
                UpdateMeasuredLabel(feature);
            }
            finally
            {
                _loading = false;
            }
            SchedulePreview();
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            if (!_loading && _mode.SelectedIndex == 0 && _availableCatalog.Count == 0)
            {
                _loading = true;
                _mode.SelectedIndex = 1;
                _loading = false;
            }
            else if (!_loading && _mode.SelectedIndex == 1)
            {
                var entry = SelectedCatalogEntry();
                if (entry != null)
                {
                    _loading = true;
                    try
                    {
                        _nominal.Value = entry.DiameterMm;
                        _customPitch.Value = SelectedPitchMm(entry);
                    }
                    finally
                    {
                        _loading = false;
                    }
                }
            }
            UpdateModeControls();
            OnControlChanged(sender, e);
        }

        private void OnSizeChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;
            UpdatePitchList(null, CurrentFeature());
            SchedulePreview();
        }

        private void OnFullLengthChanged(object sender, EventArgs e)
        {
            UpdateLengthControls();
            OnControlChanged(sender, e);
        }

        private void OnFeatureSelectionChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;
            if (_hasLoadedFeature && _selectedIndex >= 0 && _selectedIndex < _features.Count)
                SyncFeatureFromControls(_features[_selectedIndex]);
            var next = _featureList.SelectedIndex;
            if (next >= 0 && next < _features.Count)
            {
                _selectedIndex = next;
                LoadFeature(next);
                _hasLoadedFeature = true;
                SchedulePreview();
            }
        }

        private void OnDeleteClicked(object sender, EventArgs e)
        {
            if (_selectedIndex < 0 || _selectedIndex >= _features.Count)
                return;
            _features.RemoveAt(_selectedIndex);
            _selectedIndex = Math.Min(_selectedIndex, _features.Count - 1);
            RefreshFeatureList();
            if (_selectedIndex >= 0)
            {
                LoadFeature(_selectedIndex);
                _loading = true;
                try
                {
                    _featureList.SelectedIndex = _selectedIndex;
                }
                finally
                {
                    _loading = false;
                }
                _hasLoadedFeature = true;
            }
            else
            {
                _hasLoadedFeature = false;
                SetEditorEnabled(false);
            }
            SchedulePreview();
        }

        private void OnApplyClicked(object sender, EventArgs e)
        {
            RefreshPreview();
            if (_latestPreview == null || !_ok.Enabled)
                return;
            ResultBrep = _latestPreview.DuplicateBrep();
            ResultFeatures = ThreadDefinitionList.Duplicate(_features);
            Close(true);
        }

        private void OnPreviewTimer(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            RefreshPreview();
        }

        private void OnDialogClosed(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            _preview.Dispose();
        }

        private void SchedulePreview()
        {
            _ok.Enabled = false;
            _status.Text = "Atualizando prévia…";
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void RefreshPreview()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _features.Count)
                SyncFeatureFromControls(_features[_selectedIndex]);

            RefreshFeatureListPreservingSelection();
            var result = ThreadGeometry.Generate(_doc, _baseBrep, _features);
            if (!result.Success)
            {
                _latestPreview = null;
                _preview.Clear();
                _status.Text = "Não foi possível gerar a prévia: " + result.Error;
                _ok.Enabled = false;
                return;
            }

            _latestPreview = result.Brep;
            _preview.SetPreview(result.Brep);
            _status.Text = _features.Count == 0
                ? "Todas as roscas serão removidas e a peça-base será restaurada."
                : "Prévia válida. A geometria só será substituída ao clicar em Aplicar.";
            _ok.Enabled = true;
        }

        private void LoadFeature(int index)
        {
            if (index < 0 || index >= _features.Count)
                return;
            var feature = _features[index];
            _loading = true;
            try
            {
                _kind.SelectedIndex = feature.Kind == ThreadKind.External ? 0 : 1;
                _hand.SelectedIndex = feature.RightHanded ? 0 : 1;
                _nominal.Value = ThreadUnits.ModelToMillimeters(_doc, feature.NominalDiameter);
                _customPitch.Value = ThreadUnits.ModelToMillimeters(_doc, feature.Pitch);
                var pitchMm = ThreadUnits.ModelToMillimeters(_doc, feature.Pitch);
                var hasIsoOptions = RefreshCompatibleCatalog(feature, feature.SizeName, pitchMm);
                _mode.SelectedIndex = feature.IsCustom || !hasIsoOptions ? 1 : 0;
                if (!feature.IsCustom && !hasIsoOptions)
                    SwitchToCustomForMeasuredFace(feature, pitchMm);
                _fullLength.Checked = feature.FullLength;
                _offset.Value = Math.Max(0.0, ThreadUnits.ModelToMillimeters(_doc, feature.Offset));
                _length.Value = Math.Max(0.001, ThreadUnits.ModelToMillimeters(_doc, feature.Length));
                _clearance.Value = Math.Max(0.0, ThreadUnits.ModelToMillimeters(_doc, feature.Clearance));
                _loadedStartFromA = feature.StartFromA;
                _invertStart.Checked = false;
                UpdateMeasuredLabel(feature);
                SetEditorEnabled(true);
                UpdateModeControls();
                UpdateLengthControls();
            }
            finally
            {
                _loading = false;
            }
        }

        private void SyncFeatureFromControls(ThreadFeatureDefinition feature)
        {
            feature.Kind = _kind.SelectedIndex == 1 ? ThreadKind.Internal : ThreadKind.External;
            feature.IsCustom = _mode.SelectedIndex == 1 || SelectedCatalogEntry() == null;
            if (feature.IsCustom)
            {
                feature.SizeName = "Custom";
                feature.NominalDiameter = ThreadUnits.MillimetersToModel(_doc, _nominal.Value);
                feature.Pitch = ThreadUnits.MillimetersToModel(_doc, _customPitch.Value);
            }
            else
            {
                var entry = SelectedCatalogEntry();
                feature.SizeName = entry.Name;
                feature.NominalDiameter = ThreadUnits.MillimetersToModel(_doc, entry.DiameterMm);
                feature.Pitch = ThreadUnits.MillimetersToModel(_doc, SelectedPitchMm(entry));
            }
            feature.RightHanded = _hand.SelectedIndex != 1;
            feature.FullLength = _fullLength.Checked == true;
            feature.Offset = ThreadUnits.MillimetersToModel(_doc, _offset.Value);
            feature.Length = ThreadUnits.MillimetersToModel(_doc, _length.Value);
            feature.Clearance = ThreadUnits.MillimetersToModel(_doc, _clearance.Value);
            feature.StartFromA = _invertStart.Checked == true ? !_loadedStartFromA : _loadedStartFromA;
            feature.Label = string.Format("Rosca {0}", _selectedIndex + 1);
        }

        private ThreadCatalogEntry SelectedCatalogEntry()
        {
            if (_availableCatalog.Count == 0)
                return null;
            var index = Math.Max(0, Math.Min(_size.SelectedIndex, _availableCatalog.Count - 1));
            return _availableCatalog[index];
        }

        private double SelectedPitchMm(ThreadCatalogEntry entry)
        {
            if (_availablePitches.Count == 0)
                return entry != null && entry.PitchesMm.Count > 0 ? entry.PitchesMm[0] : 1.0;
            var index = Math.Max(0, Math.Min(_pitchList.SelectedIndex, _availablePitches.Count - 1));
            return _availablePitches[index];
        }

        private void UpdatePitchList(double? desiredPitchMm, ThreadFeatureDefinition feature)
        {
            var entry = SelectedCatalogEntry();
            _availablePitches.Clear();
            if (entry != null && feature != null)
            {
                var compatible = ThreadCatalog.FindCompatiblePitches(
                    entry,
                    ThreadUnits.ModelToMillimeters(_doc, feature.FaceRadius * 2.0),
                    _kind.SelectedIndex == 1,
                    DiameterToleranceMillimeters());
                _availablePitches.AddRange(compatible);
            }

            _pitchList.DataStore = _availablePitches.Select(x => x.ToString("0.###") + " mm").ToList();
            if (_availablePitches.Count == 0)
            {
                _pitchList.SelectedIndex = -1;
                return;
            }

            var index = 0;
            if (desiredPitchMm.HasValue)
            {
                var bestError = double.MaxValue;
                for (var i = 0; i < _availablePitches.Count; i++)
                {
                    var error = Math.Abs(_availablePitches[i] - desiredPitchMm.Value);
                    if (error < bestError)
                    {
                        bestError = error;
                        index = i;
                    }
                }
            }
            _pitchList.SelectedIndex = index;
        }

        private bool RefreshCompatibleCatalog(
            ThreadFeatureDefinition feature,
            string desiredSize,
            double? desiredPitchMm)
        {
            _availableCatalog.Clear();
            _availablePitches.Clear();
            if (feature != null)
            {
                var compatible = ThreadCatalog.FindCompatibleEntries(
                    ThreadUnits.ModelToMillimeters(_doc, feature.FaceRadius * 2.0),
                    _kind.SelectedIndex == 1,
                    DiameterToleranceMillimeters());
                _availableCatalog.AddRange(compatible);
            }

            _size.DataStore = _availableCatalog.Select(x => x.Name).ToList();
            if (_availableCatalog.Count == 0)
            {
                _size.SelectedIndex = -1;
                _pitchList.DataStore = new string[0];
                _pitchList.SelectedIndex = -1;
                return false;
            }

            var catalogIndex = _availableCatalog.FindIndex(
                x => string.Equals(x.Name, desiredSize, StringComparison.OrdinalIgnoreCase));
            _size.SelectedIndex = catalogIndex >= 0 ? catalogIndex : 0;
            UpdatePitchList(desiredPitchMm, feature);
            return true;
        }

        private ThreadFeatureDefinition CurrentFeature()
        {
            return _selectedIndex >= 0 && _selectedIndex < _features.Count
                ? _features[_selectedIndex]
                : null;
        }

        private double CurrentPitchMillimeters(ThreadFeatureDefinition feature)
        {
            var entry = SelectedCatalogEntry();
            if (_mode.SelectedIndex == 0 && entry != null && _availablePitches.Count > 0)
                return SelectedPitchMm(entry);
            if (_customPitch.Value > 0.0)
                return _customPitch.Value;
            return feature != null ? ThreadUnits.ModelToMillimeters(_doc, feature.Pitch) : 1.0;
        }

        private double DiameterToleranceMillimeters()
        {
            return Math.Max(
                ThreadUnits.ModelToMillimeters(_doc, _doc.ModelAbsoluteTolerance * 2.0),
                0.01);
        }

        private void SwitchToCustomForMeasuredFace(ThreadFeatureDefinition feature, double pitchMm)
        {
            var safePitch = pitchMm > 0.0 ? pitchMm : 1.0;
            var measuredDiameterMm = ThreadUnits.ModelToMillimeters(_doc, feature.FaceRadius * 2.0);
            _mode.SelectedIndex = 1;
            _customPitch.Value = safePitch;
            _nominal.Value = _kind.SelectedIndex == 1
                ? measuredDiameterMm + ThreadMath.InternalThreadDepth(safePitch) * 2.0
                : measuredDiameterMm;
        }

        private void UpdateMeasuredLabel(ThreadFeatureDefinition feature)
        {
            var compatibility = _availableCatalog.Count == 0
                ? "nenhuma combinação ISO compatível; use Custom"
                : "ISO compatível: " + string.Join(", ", _availableCatalog.Select(x => x.Name));
            _measured.Text = string.Format(
                "Face medida: Ø {0:0.###} mm × {1:0.###} mm — {2}.",
                ThreadUnits.ModelToMillimeters(_doc, feature.FaceRadius * 2.0),
                ThreadUnits.ModelToMillimeters(_doc, feature.FaceLength),
                compatibility);
        }

        private void UpdateModeControls()
        {
            var custom = _mode.SelectedIndex == 1;
            _mode.Enabled = _features.Count > 0 && _availableCatalog.Count > 0;
            _size.Enabled = !custom && _availableCatalog.Count > 0;
            _pitchList.Enabled = !custom && _availablePitches.Count > 0;
            _nominal.Enabled = custom;
            _customPitch.Enabled = custom;
        }

        private void UpdateLengthControls()
        {
            var partial = _fullLength.Checked != true;
            _offset.Enabled = partial;
            _length.Enabled = partial;
        }

        private void SetEditorEnabled(bool enabled)
        {
            _kind.Enabled = enabled;
            _mode.Enabled = enabled;
            _size.Enabled = enabled;
            _pitchList.Enabled = enabled;
            _hand.Enabled = enabled;
            _nominal.Enabled = enabled;
            _customPitch.Enabled = enabled;
            _offset.Enabled = enabled;
            _length.Enabled = enabled;
            _clearance.Enabled = enabled;
            _fullLength.Enabled = enabled;
            _invertStart.Enabled = enabled;
            _delete.Enabled = enabled && _editMode;
        }

        private void RefreshFeatureList()
        {
            _loading = true;
            try
            {
                _featureList.DataStore = _features.Select(FeatureSummary).ToList();
            }
            finally
            {
                _loading = false;
            }
        }

        private void RefreshFeatureListPreservingSelection()
        {
            var selected = _selectedIndex;
            RefreshFeatureList();
            if (selected >= 0 && selected < _features.Count)
            {
                _loading = true;
                try
                {
                    _featureList.SelectedIndex = selected;
                }
                finally
                {
                    _loading = false;
                }
            }
        }

        private string FeatureSummary(ThreadFeatureDefinition feature)
        {
            var designation = feature.IsCustom ? "Custom" : feature.SizeName;
            return string.Format(
                "{0} — {1} {2} × {3:0.###} mm — {4:0.###} mm",
                feature.Label,
                feature.Kind == ThreadKind.External ? "externa" : "interna",
                designation,
                ThreadUnits.ModelToMillimeters(_doc, feature.Pitch),
                ThreadUnits.ModelToMillimeters(_doc, feature.EffectiveLength));
        }
    }
}
