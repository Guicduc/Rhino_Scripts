"""Lay selected Rhino objects flat on the World XY plane.

Run with Rhino's Python script runner. The script:
  1. Prompts for objects.
  2. Finds a flat reference direction for each object.
     - Breps, surfaces, and extrusions use the largest planar face.
     - Meshes use the largest mesh face normal.
     - Planar curves use their curve plane normal.
  3. Rotates each object so that reference normal points to World +Z.
  4. Moves each object so the bottom of its bounding box sits on Z=0.
  5. Arranges the flattened objects in rows so they do not overlap.

Objects that do not provide a usable reference plane are left unchanged and
reported at the end.
"""

import math

import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc


TARGET_NORMAL = Rhino.Geometry.Vector3d.ZAxis
MIN_VECTOR_LENGTH = 1.0e-9
DEFAULT_LAYOUT_GAP = 10.0


def _message(text):
    print(text)
    Rhino.RhinoApp.WriteLine(text)


def _face_area(face):
    try:
        area = Rhino.Geometry.AreaMassProperties.Compute(face)
    except Exception:
        area = None

    if area is None:
        return 0.0
    return area.Area


def _face_normal(face):
    domain_u = face.Domain(0)
    domain_v = face.Domain(1)
    u = 0.5 * (domain_u.T0 + domain_u.T1)
    v = 0.5 * (domain_v.T0 + domain_v.T1)

    try:
        normal = face.NormalAt(u, v)
    except Exception:
        return None

    if normal.IsTiny(MIN_VECTOR_LENGTH):
        return None

    normal.Unitize()
    return normal


def _largest_planar_face_normal(brep, tolerance):
    best_area = 0.0
    best_normal = None

    for face in brep.Faces:
        is_planar = False
        try:
            is_planar = face.IsPlanar(tolerance)
        except Exception:
            is_planar = face.IsPlanar()

        if not is_planar:
            continue

        area = _face_area(face)
        if area <= best_area:
            continue

        normal = _face_normal(face)
        if normal is None:
            continue

        best_area = area
        best_normal = normal

    return best_normal


def _brep_reference_normal(geometry, tolerance):
    if isinstance(geometry, Rhino.Geometry.Extrusion):
        geometry = geometry.ToBrep()

    if isinstance(geometry, Rhino.Geometry.Brep):
        return _largest_planar_face_normal(geometry, tolerance)

    return None


def _mesh_face_area(mesh, face):
    vertices = mesh.Vertices
    a = Rhino.Geometry.Point3d(vertices[face.A])
    b = Rhino.Geometry.Point3d(vertices[face.B])
    c = Rhino.Geometry.Point3d(vertices[face.C])

    area = 0.5 * Rhino.Geometry.Vector3d.CrossProduct(b - a, c - a).Length
    if face.IsQuad:
        d = Rhino.Geometry.Point3d(vertices[face.D])
        area += 0.5 * Rhino.Geometry.Vector3d.CrossProduct(c - a, d - a).Length

    return area


def _mesh_reference_normal(mesh):
    if mesh.Faces.Count == 0:
        return None

    mesh.FaceNormals.ComputeFaceNormals()

    best_area = 0.0
    best_normal = None
    for index in range(mesh.Faces.Count):
        face = mesh.Faces[index]
        area = _mesh_face_area(mesh, face)
        if area <= best_area:
            continue

        normal = Rhino.Geometry.Vector3d(mesh.FaceNormals[index])
        if normal.IsTiny(MIN_VECTOR_LENGTH):
            continue

        normal.Unitize()
        best_area = area
        best_normal = normal

    return best_normal


def _curve_reference_normal(curve, tolerance):
    plane = Rhino.Geometry.Plane.Unset
    try:
        rc, plane = curve.TryGetPlane(tolerance)
    except Exception:
        rc = False

    if not rc:
        return None

    normal = Rhino.Geometry.Vector3d(plane.Normal)
    if normal.IsTiny(MIN_VECTOR_LENGTH):
        return None

    normal.Unitize()
    return normal


def _reference_normal(geometry, tolerance):
    normal = _brep_reference_normal(geometry, tolerance)
    if normal is not None:
        return normal

    if isinstance(geometry, Rhino.Geometry.Mesh):
        return _mesh_reference_normal(geometry)

    if isinstance(geometry, Rhino.Geometry.Curve):
        return _curve_reference_normal(geometry, tolerance)

    return None


def _rotation_from_normal(normal, center):
    normal = Rhino.Geometry.Vector3d(normal)
    normal.Unitize()

    dot = Rhino.Geometry.Vector3d.Multiply(normal, TARGET_NORMAL)
    if dot < 0.0:
        normal.Reverse()
        dot = -dot

    dot = max(-1.0, min(1.0, dot))

    if abs(dot - 1.0) <= 1.0e-9:
        return Rhino.Geometry.Transform.Identity

    axis = Rhino.Geometry.Vector3d.CrossProduct(normal, TARGET_NORMAL)
    if axis.IsTiny(MIN_VECTOR_LENGTH):
        return Rhino.Geometry.Transform.Identity

    axis.Unitize()
    angle = math.acos(dot)
    return Rhino.Geometry.Transform.Rotation(angle, axis, center)


def _drop_to_world_xy(object_id):
    bbox = rs.BoundingBox(object_id)
    if not bbox:
        return False

    min_z = min(point.Z for point in bbox)
    translation = Rhino.Geometry.Transform.Translation(0.0, 0.0, -min_z)
    return bool(rs.TransformObject(object_id, translation, copy=False))


def _bbox_limits(object_id):
    bbox = rs.BoundingBox(object_id)
    if not bbox:
        return None

    min_x = min(point.X for point in bbox)
    max_x = max(point.X for point in bbox)
    min_y = min(point.Y for point in bbox)
    max_y = max(point.Y for point in bbox)
    min_z = min(point.Z for point in bbox)
    return min_x, max_x, min_y, max_y, min_z


def _move_object(object_id, x_distance, y_distance, z_distance):
    transform = Rhino.Geometry.Transform.Translation(x_distance, y_distance, z_distance)
    return bool(rs.TransformObject(object_id, transform, copy=False))


def _arrange_flat_objects(object_ids, gap):
    boxes = []
    total_area = 0.0
    widest = 0.0
    start_x = None
    start_y = None

    for object_id in object_ids:
        limits = _bbox_limits(object_id)
        if limits is None:
            continue

        min_x, max_x, min_y, max_y, min_z = limits
        width = max(max_x - min_x, 0.0)
        height = max(max_y - min_y, 0.0)
        boxes.append(
            {
                "id": object_id,
                "min_x": min_x,
                "min_y": min_y,
                "min_z": min_z,
                "width": width,
                "height": height,
            }
        )
        total_area += max(width, gap) * max(height, gap)
        widest = max(widest, width)
        start_x = min_x if start_x is None else min(start_x, min_x)
        start_y = max_y if start_y is None else max(start_y, max_y)

    if not boxes:
        return 0

    target_row_width = max(widest, math.sqrt(total_area) * 1.5)
    cursor_x = start_x
    cursor_y = start_y
    row_height = 0.0
    arranged = 0

    for box in boxes:
        if cursor_x > start_x and cursor_x + box["width"] > start_x + target_row_width:
            cursor_x = start_x
            cursor_y -= row_height + gap
            row_height = 0.0

        x_distance = cursor_x - box["min_x"]
        y_distance = (cursor_y - box["height"]) - box["min_y"]
        z_distance = -box["min_z"]

        if _move_object(box["id"], x_distance, y_distance, z_distance):
            arranged += 1

        cursor_x += box["width"] + gap
        row_height = max(row_height, box["height"])

    return arranged


def _object_label(object_id, index):
    name = rs.ObjectName(object_id)
    if name:
        return name
    return "object_{0}".format(index)


def LayFlatXY():
    object_ids = rs.GetObjects(
        "Select objects to lay flat on the World XY plane",
        preselect=True,
    )
    if not object_ids:
        _message("No objects selected.")
        return

    tolerance = sc.doc.ModelAbsoluteTolerance
    if tolerance <= 0.0:
        tolerance = 0.01

    gap = rs.GetReal("Gap between flattened objects", DEFAULT_LAYOUT_GAP, 0.0)
    if gap is None:
        _message("Lay flat canceled.")
        return

    laid_flat = []
    laid_flat_ids = []
    skipped = []

    rs.EnableRedraw(False)
    try:
        for index, object_id in enumerate(object_ids, 1):
            rhino_object = sc.doc.Objects.Find(object_id)
            if rhino_object is None:
                skipped.append("{0}: object was not found".format(object_id))
                continue

            geometry = rhino_object.Geometry
            normal = _reference_normal(geometry, tolerance)
            label = _object_label(object_id, index)

            if normal is None:
                skipped.append("{0}: no planar face, mesh face, or planar curve was found".format(label))
                continue

            bbox = rhino_object.Geometry.GetBoundingBox(True)
            if not bbox.IsValid:
                skipped.append("{0}: invalid bounding box".format(label))
                continue

            rotation = _rotation_from_normal(normal, bbox.Center)
            if not rs.TransformObject(object_id, rotation, copy=False):
                skipped.append("{0}: rotation failed".format(label))
                continue

            if not _drop_to_world_xy(object_id):
                skipped.append("{0}: move to Z=0 failed".format(label))
                continue

            rs.ShowObject(object_id)
            laid_flat.append(label)
            laid_flat_ids.append(object_id)

        arranged_count = _arrange_flat_objects(laid_flat_ids, gap)

    finally:
        rs.EnableRedraw(True)
        if laid_flat_ids:
            rs.UnselectAllObjects()
            for object_id in laid_flat_ids:
                rs.SelectObject(object_id)
            rs.ZoomSelected()
        sc.doc.Views.Redraw()

    _message("Laid {0} object(s) flat on World XY.".format(len(laid_flat)))
    _message("Arranged {0} object(s) with a gap of {1:g}.".format(arranged_count, gap))

    if skipped:
        _message("Skipped {0} object(s):".format(len(skipped)))
        for item in skipped:
            _message("  " + item)


if __name__ == "__main__":
    LayFlatXY()
