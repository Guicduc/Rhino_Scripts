using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace CenterRectangle
{
    public class CenterRectangleInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get { return "Center Rectangle"; }
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
            get { return "Creates a rectangle centered on a point from width and height values."; }
        }
    }

    public class CenterRectangleComponent : GH_Component
    {
        public CenterRectangleComponent()
            : base(
                "Center Rectangle",
                "CenterRect",
                "Creates a rectangle centered on a point using the same flow as native Grasshopper components: +/- half X and +/- half Y into Rectangle.",
                "Custom",
                "Geometry")
        {
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("9D187B59-89F3-4B6A-9A6D-CDF626EF40DB"); }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Center", "C", "Center point of the rectangle.", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("X Size", "X", "Rectangle size in the local X direction.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Y Size", "Y", "Rectangle size in the local Y direction.", GH_ParamAccess.item, 5.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddRectangleParameter("Rectangle", "R", "Centered rectangle.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Boundary", "B", "Rectangle boundary curve.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Area", "A", "Rectangle area.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d center = Point3d.Origin;
            double xSize = 0.0;
            double ySize = 0.0;

            if (!DA.GetData(0, ref center)) return;
            if (!DA.GetData(1, ref xSize)) return;
            if (!DA.GetData(2, ref ySize)) return;

            if (!Rhino.RhinoMath.IsValidDouble(xSize) || !Rhino.RhinoMath.IsValidDouble(ySize))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "X and Y sizes must be valid numbers.");
                return;
            }

            double halfX = Math.Abs(xSize) * 0.5;
            double halfY = Math.Abs(ySize) * 0.5;

            Plane plane = Plane.WorldXY;
            plane.Origin = center;

            Rectangle3d rectangle = new Rectangle3d(
                plane,
                new Interval(-halfX, halfX),
                new Interval(-halfY, halfY));

            DA.SetData(0, rectangle);
            DA.SetData(1, rectangle.ToNurbsCurve());
            DA.SetData(2, Math.Abs(xSize * ySize));
        }
    }
}
