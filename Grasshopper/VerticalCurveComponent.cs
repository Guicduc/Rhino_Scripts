using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace VerticalCurve
{
    public class VerticalCurveInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get { return "Vertical Curve"; }
        }

        public override string Version
        {
            get { return "1.0.0"; }
        }

        public override string AuthorName
        {
            get { return "Codex"; }
        }

        public override string Description
        {
            get { return "Identifies curves that are vertical in the World Z direction."; }
        }
    }

    public class VerticalCurveComponent : GH_Component
    {
        public VerticalCurveComponent()
            : base(
                "Curve Is Vertical",
                "IsVertical",
                "Identifies whether each curve is vertical in World Z by checking if X/Y variation stays within tolerance while Z varies.",
                "Custom",
                "Analysis")
        {
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("7F487C28-3D3B-4E4F-B30E-7F5362D099C6"); }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to test.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "Maximum allowed X/Y drift for a curve to be considered vertical. If zero or negative, the Rhino document absolute tolerance is used.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Is Vertical", "V", "True for each curve that is vertical.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Vertical Curves", "VC", "Curves classified as vertical.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Other Curves", "OC", "Curves not classified as vertical.", GH_ParamAccess.list);
            pManager.AddNumberParameter("XY Drift", "D", "Maximum X or Y bounding-box variation measured for each curve.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Height", "H", "Z bounding-box variation measured for each curve.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Curve> curves = new List<Curve>();
            double tolerance = 0.0;

            if (!DA.GetDataList(0, curves)) return;
            DA.GetData(1, ref tolerance);

            if (!RhinoMath.IsValidDouble(tolerance) || tolerance < 0.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tolerance must be a valid non-negative number.");
                return;
            }

            if (tolerance == 0.0)
            {
                tolerance = RhinoDoc.ActiveDoc != null ? RhinoDoc.ActiveDoc.ModelAbsoluteTolerance : RhinoMath.SqrtEpsilon;
            }

            List<bool> isVertical = new List<bool>();
            List<Curve> verticalCurves = new List<Curve>();
            List<Curve> otherCurves = new List<Curve>();
            List<double> xyDrifts = new List<double>();
            List<double> heights = new List<double>();

            foreach (Curve curve in curves)
            {
                bool vertical = false;
                double xyDrift = 0.0;
                double height = 0.0;

                if (curve != null && curve.IsValid)
                {
                    BoundingBox box = curve.GetBoundingBox(true);

                    if (box.IsValid)
                    {
                        xyDrift = Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y);
                        height = box.Max.Z - box.Min.Z;
                        vertical = xyDrift <= tolerance && height > tolerance;
                    }
                }

                isVertical.Add(vertical);
                xyDrifts.Add(xyDrift);
                heights.Add(height);

                if (vertical)
                {
                    verticalCurves.Add(curve);
                }
                else if (curve != null)
                {
                    otherCurves.Add(curve);
                }
            }

            DA.SetDataList(0, isVertical);
            DA.SetDataList(1, verticalCurves);
            DA.SetDataList(2, otherCurves);
            DA.SetDataList(3, xyDrifts);
            DA.SetDataList(4, heights);
        }
    }
}
