using System;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ThreadRhino
{
    internal sealed class ThreadPreviewConduit : DisplayConduit, IDisposable
    {
        private readonly RhinoDoc _doc;
        private readonly DisplayMaterial _material = new DisplayMaterial(Color.FromArgb(40, 180, 230), 0.35);
        private Brep _preview;

        public ThreadPreviewConduit(RhinoDoc doc)
        {
            _doc = doc;
            Enabled = true;
        }

        public void SetPreview(Brep brep)
        {
            _preview = brep;
            if (_doc != null)
                _doc.Views.Redraw();
        }

        public void Clear()
        {
            _preview = null;
            if (_doc != null)
                _doc.Views.Redraw();
        }

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (_preview != null)
                e.IncludeBoundingBox(_preview.GetBoundingBox(true));
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (_preview == null)
                return;
            e.Display.DrawBrepShaded(_preview, _material);
            e.Display.DrawBrepWires(_preview, Color.DeepSkyBlue, 1);
        }

        public void Dispose()
        {
            Enabled = false;
            _preview = null;
            if (_doc != null)
                _doc.Views.Redraw();
        }
    }
}
