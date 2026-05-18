# -*- coding: utf-8 -*-
"""Create fixed-geometry arrows for Rhino.

Workflow:
1. Run the script/alias `arrow`.
2. Click the arrow tip.
3. Click points for the arrow curve.
4. Press Enter to finish.
5. Move the mouse to preview the arrow head size, click to confirm, or type a multiplier.

The arrow head is real geometry: a closed triangular boundary with a solid hatch.
It does not scale with the viewport zoom.
"""

import System.Drawing
import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc


HEAD_LENGTH_RATIO = 0.30
HEAD_WIDTH_RATIO = 0.70
MIN_SEGMENT_LENGTH_MULTIPLIER = 10.0
DEFAULT_HEAD_MULTIPLIER = 1.0
PREVIEW_COLOR = System.Drawing.Color.DodgerBlue


def _get_model_tolerance():
    try:
        return max(float(sc.doc.ModelAbsoluteTolerance), 0.001)
    except:
        return 0.001


def _is_valid_segment(a, b):
    return a and b and a.IsValid and b.IsValid and a.DistanceTo(b) > _get_model_tolerance()


def _get_arrow_direction(points):
    tip = points[0]

    for point in points[1:]:
        if _is_valid_segment(tip, point):
            direction = tip - point
            direction.Unitize()
            return direction, tip.DistanceTo(point)

    return None, 0.0


def _get_base_head_length(points):
    direction, segment_length = _get_arrow_direction(points)
    if not direction:
        return 0.0

    tolerance = _get_model_tolerance()
    head_length = max(segment_length * HEAD_LENGTH_RATIO, tolerance * MIN_SEGMENT_LENGTH_MULTIPLIER)
    return min(head_length, segment_length * 0.95)


def _get_perpendicular(direction, view):
    if view:
        plane = view.ActiveViewport.ConstructionPlane()
    else:
        plane = Rhino.Geometry.Plane.WorldXY

    normal = Rhino.Geometry.Vector3d(plane.ZAxis)
    side = Rhino.Geometry.Vector3d.CrossProduct(normal, direction)

    if not side.Unitize():
        side = Rhino.Geometry.Vector3d.CrossProduct(Rhino.Geometry.Vector3d.ZAxis, direction)

    if not side.Unitize():
        side = Rhino.Geometry.Vector3d.CrossProduct(Rhino.Geometry.Vector3d.XAxis, direction)

    side.Unitize()
    return side


def _make_arrow_head(points, view, multiplier=DEFAULT_HEAD_MULTIPLIER):
    direction, segment_length = _get_arrow_direction(points)
    if not direction:
        return None

    multiplier = max(float(multiplier), 0.01)
    head_length = min(_get_base_head_length(points) * multiplier, segment_length * 0.95)
    head_width = head_length * HEAD_WIDTH_RATIO

    tip = points[0]
    side = _get_perpendicular(direction, view)
    base_center = tip - direction * head_length
    left = base_center + side * (head_width * 0.5)
    right = base_center - side * (head_width * 0.5)

    return [tip, left, right, tip]


def _get_multiplier_from_point(points, point):
    base_length = _get_base_head_length(points)
    if base_length <= 0.0 or not point:
        return DEFAULT_HEAD_MULTIPLIER

    multiplier = points[0].DistanceTo(point) / base_length
    return max(multiplier, 0.01)


def _draw_arrow_preview(e, points, multiplier):
    if len(points) > 1:
        e.Display.DrawPolyline(points, PREVIEW_COLOR, 2)

    head_points = _make_arrow_head(points, e.Viewport.ParentView, multiplier)
    if head_points:
        e.Display.DrawPolyline(head_points, PREVIEW_COLOR, 2)


def _get_head_multiplier(points, view):
    gp = Rhino.Input.Custom.GetPoint()
    gp.SetCommandPrompt(
        "Ajuste o tamanho da seta. Clique para confirmar, Enter usa 1.0, ou digite o multiplicador"
    )
    gp.AcceptNothing(True)
    gp.AcceptNumber(True, False)
    gp.SetBasePoint(points[0], True)
    gp.DrawLineFromPoint(points[0], True)

    def dynamic_draw(sender, e):
        multiplier = _get_multiplier_from_point(points, e.CurrentPoint)
        _draw_arrow_preview(e, points, multiplier)

    gp.DynamicDraw += dynamic_draw
    result = gp.Get()

    if result == Rhino.Input.GetResult.Point:
        return _get_multiplier_from_point(points, gp.Point())

    if result == Rhino.Input.GetResult.Number:
        return max(float(gp.Number()), 0.01)

    if result == Rhino.Input.GetResult.Nothing:
        return DEFAULT_HEAD_MULTIPLIER

    return None


def _copy_attributes(source_id, target_ids):
    if not source_id or not target_ids:
        return

    source = sc.doc.Objects.Find(source_id)
    if not source:
        return

    for target_id in target_ids:
        target = sc.doc.Objects.Find(target_id)
        if target:
            target.Attributes = source.Attributes.Duplicate()
            target.CommitChanges()


def _add_solid_hatch(boundary_id):
    hatch_id = None

    try:
        hatch_id = rs.AddHatch(boundary_id, "Solid")
    except:
        hatch_id = None

    if hatch_id:
        return hatch_id

    pattern_index = sc.doc.HatchPatterns.Find("Solid", True)
    if pattern_index < 0:
        pattern_index = sc.doc.HatchPatterns.CurrentHatchPatternIndex

    boundary = sc.doc.Objects.Find(boundary_id)
    if not boundary:
        return None

    hatches = Rhino.Geometry.Hatch.Create(boundary.Geometry, pattern_index, 0.0, 1.0)
    if not hatches:
        return None

    hatch_id = sc.doc.Objects.AddHatch(hatches[0])
    return hatch_id


def _group_objects(object_ids):
    object_ids = [object_id for object_id in object_ids if object_id]
    if not object_ids:
        return None

    group_name = rs.AddGroup()
    if group_name:
        rs.AddObjectsToGroup(object_ids, group_name)

    return group_name


def _add_arrow_geometry(points, view, multiplier):
    if len(points) == 2:
        curve_id = rs.AddLine(points[0], points[1])
    else:
        curve_id = rs.AddPolyline(points)

    if not curve_id:
        print("Nao foi possivel criar a curva da seta.")
        return

    head_points = _make_arrow_head(points, view, multiplier)
    if not head_points:
        print("A curva precisa ter pelo menos um segmento valido.")
        rs.DeleteObject(curve_id)
        return

    boundary_id = rs.AddPolyline(head_points)
    hatch_id = _add_solid_hatch(boundary_id)

    _copy_attributes(curve_id, [boundary_id, hatch_id])
    object_ids = [curve_id, boundary_id, hatch_id]
    _group_objects(object_ids)

    rs.UnselectAllObjects()
    rs.SelectObjects([object_id for object_id in object_ids if object_id])

    sc.doc.Views.Redraw()


def _draw_preview(e, points):
    preview_points = list(points)
    preview_points.append(e.CurrentPoint)

    if len(preview_points) > 1:
        e.Display.DrawPolyline(preview_points, PREVIEW_COLOR, 2)

    head_points = _make_arrow_head(preview_points, e.Viewport.ParentView)
    if head_points:
        e.Display.DrawPolyline(head_points, PREVIEW_COLOR, 2)


def _get_curve_points():
    tip = rs.GetPoint("Clique no ponto onde estara a ponta da seta")
    if not tip:
        return None, None

    points = [tip]
    view = sc.doc.Views.ActiveView

    while True:
        gp = Rhino.Input.Custom.GetPoint()
        gp.SetCommandPrompt("Clique o proximo ponto da curva. Enter finaliza")
        gp.AcceptNothing(True)

        if len(points) > 1:
            gp.AddOption("Undo")

        gp.SetBasePoint(points[-1], True)
        gp.DrawLineFromPoint(points[-1], True)

        def dynamic_draw(sender, e):
            _draw_preview(e, points)

        gp.DynamicDraw += dynamic_draw
        result = gp.Get()

        if result == Rhino.Input.GetResult.Point:
            point = gp.Point()
            if _is_valid_segment(points[-1], point):
                points.append(point)
                view = gp.View() or view
            continue

        if result == Rhino.Input.GetResult.Option:
            if gp.Option().EnglishName == "Undo" and len(points) > 1:
                points.pop()
            continue

        if result == Rhino.Input.GetResult.Nothing:
            if len(points) >= 2:
                return points, view
            print("Clique pelo menos um segundo ponto para definir a curva.")
            continue

        return None, None


def arrow():
    points, view = _get_curve_points()
    if not points:
        return

    multiplier = _get_head_multiplier(points, view)
    if multiplier is None:
        return

    _add_arrow_geometry(points, view, multiplier)


def main():
    arrow()


if __name__ == "__main__":
    main()
