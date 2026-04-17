"""Split closed Rhino solids into print-bed-sized parts with peg/socket fittings.

Run with Rhino's Python script runner. The script:
  1. Prompts for closed solid polysurfaces/Breps.
  2. Prompts for print bed size, tolerance, and peg dimensions.
  3. Splits each object into an axis-aligned grid sized so every final part,
     including outward pegs, should fit inside the bed.
  4. Adds cylindrical pegs to the lower-index side of each split and matching
     sockets to the neighboring part.
  5. Uses half-round fittings on vertical split faces so the flat side can sit
     near the print-bed side without needing support under a round peg.

Notes:
  - Units are your current Rhino model units. Defaults assume millimeters.
  - The selected objects should be closed solids. Meshes are not processed.
  - Cuts are aligned to World X/Y/Z. Rotate the object first if needed.
  - The original object is hidden after successful output so the split result
    is not visually masked by the source geometry.
"""

import datetime
import math
import os

import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc
import System


DEFAULT_BED_SIZE = (220.0, 220.0, 220.0)
DEFAULT_TOLERANCE = 0.3
DEFAULT_PIN_RADIUS = 3.0
DEFAULT_PIN_DEPTH = 6.0
DEFAULT_PINS_PER_SEAM = 2
DEFAULT_HIDE_ORIGINALS = True
PRINT_BED_UP_AXIS = 2
OUTPUT_LAYER = "Split print parts with fittings"
LOG_FILE_NAME = "split_for_print_with_fittings_debug.log"
LOG_FOLDER_OVERRIDE = None
LOG_FILE_PATH = None


def _message(text):
    print(text)
    Rhino.RhinoApp.WriteLine(text)
    _log(text)


def _script_folder():
    if LOG_FOLDER_OVERRIDE and os.path.isdir(LOG_FOLDER_OVERRIDE):
        return LOG_FOLDER_OVERRIDE
    try:
        return os.path.dirname(os.path.abspath(__file__))
    except Exception:
        return os.getcwd()


def _start_log():
    global LOG_FILE_PATH
    LOG_FILE_PATH = os.path.join(_script_folder(), LOG_FILE_NAME)
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    try:
        with open(LOG_FILE_PATH, "a") as log_file:
            log_file.write("\n")
            log_file.write("=" * 78 + "\n")
            log_file.write("Split for print run started: {0}\n".format(timestamp))
            log_file.write("Log file: {0}\n".format(LOG_FILE_PATH))
            log_file.write("=" * 78 + "\n")
    except Exception:
        LOG_FILE_PATH = None
    return LOG_FILE_PATH


def _log(text):
    if not LOG_FILE_PATH:
        return
    timestamp = datetime.datetime.now().strftime("%H:%M:%S")
    try:
        with open(LOG_FILE_PATH, "a") as log_file:
            log_file.write("[{0}] {1}\n".format(timestamp, text))
    except Exception:
        pass


def _format_number(value):
    return "{0:.6f}".format(float(value))


def _format_tuple(values):
    return "(" + ", ".join(_format_number(value) for value in values) + ")"


def _format_interval(interval):
    return "[{0}, {1}]".format(_format_number(interval[0]), _format_number(interval[1]))


def _format_bbox(bbox):
    dimensions = (bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y, bbox.Max.Z - bbox.Min.Z)
    return "min {0}, max {1}, dims {2}, center {3}".format(
        _format_tuple((bbox.Min.X, bbox.Min.Y, bbox.Min.Z)),
        _format_tuple((bbox.Max.X, bbox.Max.Y, bbox.Max.Z)),
        _format_tuple(dimensions),
        _format_tuple((bbox.Center.X, bbox.Center.Y, bbox.Center.Z)),
    )


def _log_intervals(intervals_by_axis):
    for axis in range(3):
        formatted = ", ".join(_format_interval(interval) for interval in intervals_by_axis[axis])
        _log("Axis {0} intervals: {1}".format(_axis_name(axis), formatted))


def _log_parts_summary(label, parts):
    if not parts:
        _log("{0}: no keyed parts".format(label))
        return

    entries = []
    for key in sorted(parts.keys()):
        entries.append("{0}:{1}".format(key, len(parts[key])))
    _log("{0}: {1}".format(label, ", ".join(entries)))


def _axis_name(axis):
    return ("X", "Y", "Z")[axis]


def _vector_for_axis(axis):
    if axis == 0:
        return Rhino.Geometry.Vector3d(1.0, 0.0, 0.0)
    if axis == 1:
        return Rhino.Geometry.Vector3d(0.0, 1.0, 0.0)
    return Rhino.Geometry.Vector3d(0.0, 0.0, 1.0)


def _coord(point, axis):
    if axis == 0:
        return point.X
    if axis == 1:
        return point.Y
    return point.Z


def _set_coord(coords, axis, value):
    coords[axis] = value


def _point_from_coords(coords):
    return Rhino.Geometry.Point3d(coords[0], coords[1], coords[2])


def _parse_bed_size(text):
    cleaned = text.lower().replace("x", ",").replace(";", ",").replace(" ", ",")
    values = []
    for item in cleaned.split(","):
        if item.strip():
            values.append(float(item.strip()))
    if len(values) != 3:
        raise ValueError("Expected three values: X,Y,Z")
    if min(values) <= 0.0:
        raise ValueError("Print bed dimensions must be positive")
    return (values[0], values[1], values[2])


def _get_brep_from_object(object_id):
    rhino_object = sc.doc.Objects.Find(object_id)
    if rhino_object is None:
        return None

    geometry = rhino_object.Geometry
    if isinstance(geometry, Rhino.Geometry.Extrusion):
        geometry = geometry.ToBrep()

    if isinstance(geometry, Rhino.Geometry.Brep):
        if geometry.IsSolid:
            return geometry.DuplicateBrep()
        return None

    return None


def _make_intervals(min_value, max_value, count):
    intervals = []
    length = max_value - min_value
    for index in range(count):
        start = min_value + (length * float(index) / float(count))
        end = min_value + (length * float(index + 1) / float(count))
        intervals.append((start, end))
    return intervals


def _plan_axis_intervals(bbox, bed_size, pin_depth):
    mins = (bbox.Min.X, bbox.Min.Y, bbox.Min.Z)
    maxs = (bbox.Max.X, bbox.Max.Y, bbox.Max.Z)
    intervals_by_axis = []
    counts = []

    for axis in range(3):
        dimension = maxs[axis] - mins[axis]
        if dimension <= bed_size[axis]:
            count = 1
        else:
            usable = bed_size[axis] - pin_depth
            if usable <= 0.0:
                raise ValueError(
                    "Bed {0} dimension must be larger than peg depth.".format(_axis_name(axis))
                )
            count = int(math.ceil(dimension / usable))

        counts.append(count)
        intervals_by_axis.append(_make_intervals(mins[axis], maxs[axis], count))

    return intervals_by_axis, counts


def _make_box_brep(x_interval, y_interval, z_interval):
    box = Rhino.Geometry.Box(
        Rhino.Geometry.Plane.WorldXY,
        Rhino.Geometry.Interval(x_interval[0], x_interval[1]),
        Rhino.Geometry.Interval(y_interval[0], y_interval[1]),
        Rhino.Geometry.Interval(z_interval[0], z_interval[1]),
    )
    return box.ToBrep()


def _boolean_intersection(subject, cutter, tolerance):
    try:
        result = Rhino.Geometry.Brep.CreateBooleanIntersection([subject], [cutter], tolerance)
    except Exception:
        result = None

    if not result:
        return []

    return [brep for brep in result if brep is not None and brep.IsValid]


def _plane_splitter_brep(bbox, axis, cut_value, tolerance):
    center = bbox.Center
    origin = Rhino.Geometry.Point3d(center.X, center.Y, center.Z)
    if axis == 0:
        origin.X = cut_value
    elif axis == 1:
        origin.Y = cut_value
    else:
        origin.Z = cut_value

    normal = _vector_for_axis(axis)
    plane = Rhino.Geometry.Plane(origin, normal)
    extent = max(bbox.Diagonal.Length, 1.0) + max(tolerance * 100.0, 10.0)
    surface = Rhino.Geometry.PlaneSurface(
        plane,
        Rhino.Geometry.Interval(-extent, extent),
        Rhino.Geometry.Interval(-extent, extent),
    )
    return surface.ToBrep()


def _cap_split_piece(piece, tolerance):
    if piece is None or not piece.IsValid:
        return None
    if piece.IsSolid:
        return piece

    try:
        capped = piece.CapPlanarHoles(tolerance)
    except Exception:
        capped = None

    if capped is not None and capped.IsValid:
        return capped

    return piece


def _split_brep_once_with_plane(brep, splitter, tolerance):
    try:
        result = brep.Split(splitter, tolerance)
    except Exception as first_error:
        _log("brep.Split(splitter, tolerance) failed: {0}".format(first_error))
        try:
            result = brep.Split([splitter], tolerance)
        except Exception as second_error:
            _log("brep.Split([splitter], tolerance) failed: {0}".format(second_error))
            result = None

    if result and len(result) > 1:
        pieces = []
        for item in result:
            capped = _cap_split_piece(item, tolerance)
            if capped is not None and capped.IsValid:
                pieces.append(capped)
        return pieces

    return [brep]


def _split_with_planes(brep, intervals_by_axis, tolerance):
    pieces = [brep]
    bbox = brep.GetBoundingBox(True)
    _log("Plane split input bbox: {0}".format(_format_bbox(bbox)))

    for axis in range(3):
        for cut_index, interval in enumerate(intervals_by_axis[axis][:-1]):
            splitter = _plane_splitter_brep(bbox, axis, interval[1], tolerance)
            next_pieces = []
            pieces_before = len(pieces)
            split_piece_count = 0
            unchanged_piece_count = 0
            for piece in pieces:
                split_pieces = _split_brep_once_with_plane(piece, splitter, tolerance)
                if len(split_pieces) > 1:
                    split_piece_count += 1
                else:
                    unchanged_piece_count += 1
                next_pieces.extend(split_pieces)
            pieces = next_pieces
            solid_count = len([piece for piece in pieces if piece is not None and piece.IsSolid])
            _log(
                "Plane cut axis {0} cut #{1} at {2}: before {3}, split {4}, unchanged {5}, after {6}, solids {7}".format(
                    _axis_name(axis),
                    cut_index + 1,
                    _format_number(interval[1]),
                    pieces_before,
                    split_piece_count,
                    unchanged_piece_count,
                    len(pieces),
                    solid_count,
                )
            )

    return pieces


def _interval_index_for_value(value, intervals, tolerance):
    for index, interval in enumerate(intervals):
        if value <= interval[1] + tolerance:
            return index
    return len(intervals) - 1


def _classify_piece_key(piece, intervals_by_axis, tolerance):
    bbox = piece.GetBoundingBox(True)
    center = bbox.Center
    return (
        _interval_index_for_value(center.X, intervals_by_axis[0], tolerance),
        _interval_index_for_value(center.Y, intervals_by_axis[1], tolerance),
        _interval_index_for_value(center.Z, intervals_by_axis[2], tolerance),
    )


def _split_into_grid_by_planes(brep, intervals_by_axis, tolerance):
    pieces = _split_with_planes(brep, intervals_by_axis, tolerance)
    _log("Plane split produced {0} raw piece(s).".format(len(pieces)))
    parts = {}
    for piece_index, piece in enumerate(pieces):
        key = _classify_piece_key(piece, intervals_by_axis, tolerance)
        parts.setdefault(key, []).append(piece)
        _log(
            "Plane piece #{0}: key {1}, solid {2}, bbox {3}".format(
                piece_index + 1,
                key,
                piece.IsSolid,
                _format_bbox(piece.GetBoundingBox(True)),
            )
        )
    _log_parts_summary("Plane keyed parts", parts)
    return parts


def _split_into_grid_by_boxes(brep, intervals_by_axis, tolerance):
    parts = {}
    x_intervals, y_intervals, z_intervals = intervals_by_axis

    for ix, x_interval in enumerate(x_intervals):
        for iy, y_interval in enumerate(y_intervals):
            for iz, z_interval in enumerate(z_intervals):
                cutter = _make_box_brep(x_interval, y_interval, z_interval)
                result = _boolean_intersection(brep, cutter, tolerance)
                if result:
                    parts[(ix, iy, iz)] = result
                    result_dims = []
                    for result_brep in result:
                        result_dims.append(_format_tuple(_bbox_dimensions(result_brep.GetBoundingBox(True))))
                    _log(
                        "Box cell {0}: result count {1}, dims {2}".format(
                            (ix, iy, iz), len(result), "; ".join(result_dims)
                        )
                    )
                else:
                    _log(
                        "Box cell {0}: no result for X {1}, Y {2}, Z {3}".format(
                            (ix, iy, iz),
                            _format_interval(x_interval),
                            _format_interval(y_interval),
                            _format_interval(z_interval),
                        )
                    )

    _log_parts_summary("Box keyed parts", parts)
    return parts


def _delete_object_quietly(object_id):
    try:
        sc.doc.Objects.Delete(object_id, True)
    except Exception:
        pass


def _get_doc_brep(object_id):
    rhino_object = sc.doc.Objects.Find(object_id)
    if rhino_object is None:
        return None

    geometry = rhino_object.Geometry
    if isinstance(geometry, Rhino.Geometry.Extrusion):
        geometry = geometry.ToBrep()

    if isinstance(geometry, Rhino.Geometry.Brep):
        return geometry.DuplicateBrep()

    return None


def _split_doc_object_with_cutter(piece_id, cutter_id):
    try:
        result = rs.SplitBrep(piece_id, [cutter_id], True)
    except Exception as error:
        _log("rs.SplitBrep failed for piece {0}: {1}".format(piece_id, error))
        result = None

    if result:
        return list(result), len(result) > 1

    return [piece_id], False


def _split_into_grid_by_doc_planes(brep, intervals_by_axis, tolerance):
    temp_ids = []
    parts = {}
    bbox = brep.GetBoundingBox(True)
    source_id = sc.doc.Objects.AddBrep(brep)
    if source_id == System.Guid.Empty:
        _log("Document plane split could not add temporary source brep.")
        return parts

    temp_ids.append(source_id)
    piece_ids = [source_id]
    _log("Document plane split input bbox: {0}".format(_format_bbox(bbox)))

    try:
        for axis in range(3):
            for cut_index, interval in enumerate(intervals_by_axis[axis][:-1]):
                splitter = _plane_splitter_brep(bbox, axis, interval[1], tolerance)
                cutter_id = sc.doc.Objects.AddBrep(splitter)
                if cutter_id == System.Guid.Empty:
                    _log(
                        "Document plane cut axis {0} cut #{1}: failed to add cutter.".format(
                            _axis_name(axis), cut_index + 1
                        )
                    )
                    continue

                temp_ids.append(cutter_id)
                next_piece_ids = []
                split_count = 0
                unchanged_count = 0
                for piece_id in piece_ids:
                    result_ids, did_split = _split_doc_object_with_cutter(piece_id, cutter_id)
                    if did_split:
                        split_count += 1
                        temp_ids.extend(result_ids)
                    else:
                        unchanged_count += 1
                    next_piece_ids.extend(result_ids)

                piece_ids = next_piece_ids
                _delete_object_quietly(cutter_id)
                _log(
                    "Document plane cut axis {0} cut #{1} at {2}: split {3}, unchanged {4}, after {5}".format(
                        _axis_name(axis),
                        cut_index + 1,
                        _format_number(interval[1]),
                        split_count,
                        unchanged_count,
                        len(piece_ids),
                    )
                )

        for piece_index, piece_id in enumerate(piece_ids):
            piece = _get_doc_brep(piece_id)
            if piece is None:
                _log("Document plane piece #{0}: missing geometry for id {1}".format(piece_index + 1, piece_id))
                continue

            piece = _cap_split_piece(piece, tolerance)
            if piece is None:
                _log("Document plane piece #{0}: cap returned no geometry.".format(piece_index + 1))
                continue

            key = _classify_piece_key(piece, intervals_by_axis, tolerance)
            parts.setdefault(key, []).append(piece)
            _log(
                "Document plane piece #{0}: key {1}, solid {2}, bbox {3}".format(
                    piece_index + 1,
                    key,
                    piece.IsSolid,
                    _format_bbox(piece.GetBoundingBox(True)),
                )
            )
    finally:
        for object_id in temp_ids:
            _delete_object_quietly(object_id)

    _log_parts_summary("Document plane keyed parts", parts)
    return parts


def _parts_score(parts):
    if not parts:
        return (0, 0, 0, 0)
    x_count = len(set(key[0] for key in parts.keys()))
    y_count = len(set(key[1] for key in parts.keys()))
    z_count = len(set(key[2] for key in parts.keys()))
    return (len(parts), x_count, y_count, z_count)


def _split_into_grid(brep, intervals_by_axis, tolerance):
    _log("Starting plane-split coverage pass.")
    plane_parts = _split_into_grid_by_planes(brep, intervals_by_axis, tolerance)
    _log("Starting document plane-split coverage pass.")
    doc_plane_parts = _split_into_grid_by_doc_planes(brep, intervals_by_axis, tolerance)
    _log("Starting box-intersection coverage pass.")
    box_parts = _split_into_grid_by_boxes(brep, intervals_by_axis, tolerance)

    candidates = [
        ("plane-split", plane_parts),
        ("document-plane-split", doc_plane_parts),
        ("box-intersection", box_parts),
    ]
    best_method, best_parts = sorted(
        candidates,
        key=lambda item: _parts_score(item[1]),
        reverse=True,
    )[0]

    for method, parts in candidates:
        _log("{0} score: {1}".format(method, _parts_score(parts)))

    _log("Selected {0}.".format(best_method))
    return best_parts, best_method, len(box_parts), len(plane_parts), len(doc_plane_parts)


def _sample_offsets(axis, radius, half_round=False):
    other_axes = [item for item in range(3) if item != axis]
    offsets = []

    zero = [0.0, 0.0, 0.0]
    offsets.append(tuple(zero))

    for other_axis in other_axes:
        positive = [0.0, 0.0, 0.0]
        positive[other_axis] = radius
        offsets.append(tuple(positive))

        if half_round and other_axis == PRINT_BED_UP_AXIS:
            continue

        negative = [0.0, 0.0, 0.0]
        negative[other_axis] = -radius
        offsets.append(tuple(negative))

    return offsets


def _inside_one_brep_for_all_samples(breps, points, tolerance):
    for brep in breps:
        all_inside = True
        for point in points:
            if not brep.IsPointInside(point, tolerance, True):
                all_inside = False
                break
        if all_inside:
            return True
    return False


def _candidate_supported(lower_breps, upper_breps, point, axis, socket_radius, tolerance, half_round):
    normal = _vector_for_axis(axis)
    check_depth = max(tolerance * 5.0, 0.1)
    offsets = _sample_offsets(axis, socket_radius, half_round)

    lower_points = []
    upper_points = []
    for offset in offsets:
        lower_point = Rhino.Geometry.Point3d(
            point.X + offset[0] - normal.X * check_depth,
            point.Y + offset[1] - normal.Y * check_depth,
            point.Z + offset[2] - normal.Z * check_depth,
        )
        upper_point = Rhino.Geometry.Point3d(
            point.X + offset[0] + normal.X * check_depth,
            point.Y + offset[1] + normal.Y * check_depth,
            point.Z + offset[2] + normal.Z * check_depth,
        )
        lower_points.append(lower_point)
        upper_points.append(upper_point)

    return (
        _inside_one_brep_for_all_samples(lower_breps, lower_points, tolerance)
        and _inside_one_brep_for_all_samples(upper_breps, upper_points, tolerance)
    )


def _spread_fractions(count):
    if count <= 1:
        return [0.5]
    return [float(index + 1) / float(count + 1) for index in range(count)]


def _ideal_pin_fractions(count, width_a, width_b, bed_side_axis=None, bed_side_fraction=None):
    if bed_side_axis is not None and bed_side_fraction is not None:
        spread = _spread_fractions(count)
        if bed_side_axis == 0:
            return [(bed_side_fraction, fraction) for fraction in spread]
        return [(fraction, bed_side_fraction) for fraction in spread]

    if count <= 1:
        return [(0.5, 0.5)]

    if count == 2:
        if width_a >= width_b:
            return [(0.33, 0.5), (0.67, 0.5)]
        return [(0.5, 0.33), (0.5, 0.67)]

    if count == 3:
        if width_a >= width_b:
            return [(0.25, 0.5), (0.5, 0.5), (0.75, 0.5)]
        return [(0.5, 0.25), (0.5, 0.5), (0.5, 0.75)]

    columns = int(math.ceil(math.sqrt(float(count))))
    rows = int(math.ceil(float(count) / float(columns)))
    fractions = []
    for row in range(rows):
        for column in range(columns):
            if len(fractions) >= count:
                break
            fractions.append(
                (
                    float(column + 1) / float(columns + 1),
                    float(row + 1) / float(rows + 1),
                )
            )
    return fractions


def _candidate_fraction_grid(min_fraction, max_fraction, prefer_low):
    fractions = [0.18, 0.25, 0.33, 0.42, 0.5, 0.58, 0.67, 0.75, 0.82]
    if prefer_low:
        fractions.extend(
            [
                min_fraction,
                min_fraction + 0.02,
                min_fraction + 0.05,
                min_fraction + 0.08,
                min_fraction + 0.12,
            ]
        )

    valid = []
    for fraction in fractions:
        if fraction < min_fraction or fraction > max_fraction:
            continue
        if not any(abs(fraction - existing) < 0.001 for existing in valid):
            valid.append(fraction)

    return sorted(valid)


def _point_on_seam(axis, cut_value, interval_a, interval_b, fraction_a, fraction_b):
    coords = [0.0, 0.0, 0.0]
    other_axes = [item for item in range(3) if item != axis]

    _set_coord(coords, axis, cut_value)
    _set_coord(
        coords,
        other_axes[0],
        interval_a[0] + (interval_a[1] - interval_a[0]) * fraction_a,
    )
    _set_coord(
        coords,
        other_axes[1],
        interval_b[0] + (interval_b[1] - interval_b[0]) * fraction_b,
    )
    return _point_from_coords(coords)


def _distance_fraction(pair_a, pair_b):
    dx = pair_a[0] - pair_b[0]
    dy = pair_a[1] - pair_b[1]
    return math.sqrt(dx * dx + dy * dy)


def _find_pin_points(
    lower_breps,
    upper_breps,
    axis,
    cut_value,
    intervals_by_axis,
    cell_index,
    requested_count,
    socket_radius,
    tolerance,
):
    other_axes = [item for item in range(3) if item != axis]
    interval_a = intervals_by_axis[other_axes[0]][cell_index[other_axes[0]]]
    interval_b = intervals_by_axis[other_axes[1]][cell_index[other_axes[1]]]

    width_a = interval_a[1] - interval_a[0]
    width_b = interval_b[1] - interval_b[0]
    support_margin = max(tolerance * 2.0, 0.1)
    bed_side_lift = max(tolerance * 0.05, 0.01)
    round_margin = socket_radius + support_margin
    half_round = PRINT_BED_UP_AXIS in other_axes

    min_margin_a = round_margin
    max_margin_a = round_margin
    min_margin_b = round_margin
    max_margin_b = round_margin

    if half_round:
        bed_side_axis = other_axes.index(PRINT_BED_UP_AXIS)
        if bed_side_axis == 0:
            min_margin_a = bed_side_lift
            max_margin_a = round_margin
        else:
            min_margin_b = bed_side_lift
            max_margin_b = round_margin
    else:
        bed_side_axis = None

    if width_a <= min_margin_a + max_margin_a or width_b <= min_margin_b + max_margin_b:
        return []

    min_fraction_a = min_margin_a / width_a
    max_fraction_a = 1.0 - (max_margin_a / width_a)
    min_fraction_b = min_margin_b / width_b
    max_fraction_b = 1.0 - (max_margin_b / width_b)

    bed_side_fraction = None
    if half_round:
        if bed_side_axis == 0:
            bed_side_fraction = min_fraction_a
        else:
            bed_side_fraction = min_fraction_b

    grid_a = _candidate_fraction_grid(min_fraction_a, max_fraction_a, bed_side_axis == 0)
    grid_b = _candidate_fraction_grid(min_fraction_b, max_fraction_b, bed_side_axis == 1)

    valid = []
    for fa in grid_a:
        for fb in grid_b:
            point = _point_on_seam(axis, cut_value, interval_a, interval_b, fa, fb)
            if _candidate_supported(
                lower_breps,
                upper_breps,
                point,
                axis,
                socket_radius,
                tolerance,
                half_round,
            ):
                valid.append((fa, fb, point))

    if not valid:
        return []

    selected = []
    ideals = _ideal_pin_fractions(
        requested_count,
        width_a,
        width_b,
        bed_side_axis,
        bed_side_fraction,
    )
    minimum_spacing = socket_radius * 2.5

    for ideal in ideals:
        best_item = None
        best_score = None
        for item in valid:
            too_close = False
            for selected_item in selected:
                if item[2].DistanceTo(selected_item[2]) < minimum_spacing:
                    too_close = True
                    break
            if too_close:
                continue

            score = _distance_fraction((item[0], item[1]), ideal)
            if best_score is None or score < best_score:
                best_item = item
                best_score = score

        if best_item is not None:
            selected.append(best_item)

    return [item[2] for item in selected]


def _cylinder_brep(origin, axis, radius, depth):
    normal = _vector_for_axis(axis)
    plane = Rhino.Geometry.Plane(origin, normal)
    circle = Rhino.Geometry.Circle(plane, radius)
    cylinder = Rhino.Geometry.Cylinder(circle, depth)
    return cylinder.ToBrep(True, True)


def _half_round_clip_box(origin, axis, radius, depth, tolerance, flat_extra=0.0):
    padding = max(tolerance * 4.0, 0.1)
    coords_min = [
        origin.X - radius - padding,
        origin.Y - radius - padding,
        origin.Z - radius - padding,
    ]
    coords_max = [
        origin.X + radius + padding,
        origin.Y + radius + padding,
        origin.Z + radius + padding,
    ]

    coords_min[axis] = _coord(origin, axis) - padding
    coords_max[axis] = _coord(origin, axis) + depth + padding
    coords_min[PRINT_BED_UP_AXIS] = _coord(origin, PRINT_BED_UP_AXIS) - flat_extra

    return _make_box_brep(
        (coords_min[0], coords_max[0]),
        (coords_min[1], coords_max[1]),
        (coords_min[2], coords_max[2]),
    )


def _fitting_breps(origin, axis, radius, depth, half_round, tolerance, flat_extra=0.0):
    cylinder = _cylinder_brep(origin, axis, radius, depth)
    if not half_round:
        return [cylinder]

    cutter = _half_round_clip_box(origin, axis, radius, depth, tolerance, flat_extra)
    result = _boolean_intersection(cylinder, cutter, tolerance)
    if result:
        return result

    return [cylinder]


def _offset_point(point, axis, distance):
    normal = _vector_for_axis(axis)
    return Rhino.Geometry.Point3d(
        point.X + normal.X * distance,
        point.Y + normal.Y * distance,
        point.Z + normal.Z * distance,
    )


def _add_fitting_geometry(
    parts,
    intervals_by_axis,
    counts,
    pin_radius,
    pin_depth,
    clearance,
    pins_per_seam,
    tolerance,
):
    pins_by_part = {}
    sockets_by_part = {}
    warnings = []

    socket_radius = pin_radius + clearance
    socket_depth = pin_depth + clearance
    socket_start_backoff = max(tolerance * 3.0, clearance)
    _log(
        "Fitting setup: pin_radius {0}, pin_depth {1}, socket_radius {2}, socket_depth {3}, pins_per_seam {4}".format(
            _format_number(pin_radius),
            _format_number(pin_depth),
            _format_number(socket_radius),
            _format_number(socket_depth),
            pins_per_seam,
        )
    )

    for axis in range(3):
        if counts[axis] < 2:
            continue

        for index, cut_interval in enumerate(intervals_by_axis[axis][:-1]):
            cut_value = cut_interval[1]
            for cell_index in list(parts.keys()):
                if cell_index[axis] != index:
                    continue

                neighbor = list(cell_index)
                neighbor[axis] += 1
                neighbor = tuple(neighbor)
                if neighbor not in parts:
                    continue

                points = _find_pin_points(
                    parts[cell_index],
                    parts[neighbor],
                    axis,
                    cut_value,
                    intervals_by_axis,
                    cell_index,
                    pins_per_seam,
                    socket_radius,
                    tolerance,
                )
                _log(
                    "Fitting seam axis {0} cut {1}, part {2} to {3}: found {4} point(s).".format(
                        _axis_name(axis),
                        _format_number(cut_value),
                        cell_index,
                        neighbor,
                        len(points),
                    )
                )

                if not points:
                    warnings.append(
                        "No supported fitting point found at {0} cut {1:.3f} for part {2}.".format(
                            _axis_name(axis), cut_value, cell_index
                        )
                    )
                    continue

                for point in points:
                    half_round = axis != PRINT_BED_UP_AXIS
                    socket_flat_extra = max(tolerance * 4.0, clearance)
                    _log(
                        "Creating {0} fitting at {1} for part {2} to socket part {3}.".format(
                            "half-round" if half_round else "round",
                            _format_tuple((point.X, point.Y, point.Z)),
                            cell_index,
                            neighbor,
                        )
                    )
                    pins = _fitting_breps(point, axis, pin_radius, pin_depth, half_round, tolerance)
                    socket_origin = _offset_point(point, axis, -socket_start_backoff)
                    sockets = _fitting_breps(
                        socket_origin,
                        axis,
                        socket_radius,
                        socket_depth + socket_start_backoff,
                        half_round,
                        tolerance,
                        socket_flat_extra,
                    )

                    pins_by_part.setdefault(cell_index, []).extend(pins)
                    sockets_by_part.setdefault(neighbor, []).extend(sockets)

    _log_parts_summary("Pins by part", pins_by_part)
    _log_parts_summary("Sockets by part", sockets_by_part)
    return pins_by_part, sockets_by_part, warnings


def _boolean_union(breps, tolerance):
    if not breps:
        return [], True
    if len(breps) == 1:
        return breps, True

    try:
        result = Rhino.Geometry.Brep.CreateBooleanUnion(breps, tolerance)
    except Exception:
        result = None

    if result:
        valid_result = [brep for brep in result if brep is not None and brep.IsValid]
        if valid_result:
            return valid_result, True
        _log("Boolean union returned an empty valid result; preserving original breps.")

    return breps, False


def _boolean_difference(subjects, cutters, tolerance):
    if not cutters:
        return subjects, True
    if not subjects:
        return subjects, False

    try:
        result = Rhino.Geometry.Brep.CreateBooleanDifference(subjects, cutters, tolerance)
    except Exception:
        result = None

    if result:
        valid_result = [brep for brep in result if brep is not None and brep.IsValid]
        if valid_result:
            return valid_result, True
        _log("Boolean difference returned an empty valid result; preserving original breps.")

    doc_result = _boolean_difference_in_document(subjects, cutters)
    if doc_result:
        return doc_result, True

    current = list(subjects)
    any_failure = result is None
    any_success = False
    for cutter_index, cutter in enumerate(cutters):
        valid_single_result = []
        try:
            single_result = Rhino.Geometry.Brep.CreateBooleanDifference(current, [cutter], tolerance)
        except Exception as error:
            _log("Single socket subtraction #{0} raised {1}; preserving current breps.".format(cutter_index + 1, error))
            single_result = None

        if single_result:
            valid_single_result = [
                brep for brep in single_result if brep is not None and brep.IsValid
            ]

        if valid_single_result:
            current = valid_single_result
            any_success = True
            continue

        doc_single_result = _boolean_difference_in_document(current, [cutter])
        if doc_single_result:
            current = doc_single_result
            any_success = True
            continue

        _log("Single socket subtraction #{0} failed in both RhinoCommon and document fallback.".format(cutter_index + 1))
        any_failure = True

    return current, any_success and not any_failure


def _boolean_difference_in_document(subjects, cutters):
    subject_ids = []
    cutter_ids = []
    result_ids = []

    try:
        for brep in subjects:
            object_id = sc.doc.Objects.AddBrep(brep)
            if object_id != System.Guid.Empty:
                subject_ids.append(object_id)

        for cutter in cutters:
            object_id = sc.doc.Objects.AddBrep(cutter)
            if object_id != System.Guid.Empty:
                cutter_ids.append(object_id)

        if not subject_ids or not cutter_ids:
            return []

        try:
            result_ids = rs.BooleanDifference(subject_ids, cutter_ids, True)
        except Exception as error:
            _log("Document BooleanDifference raised {0}.".format(error))
            result_ids = None

        if not result_ids:
            return []

        breps = []
        for object_id in result_ids:
            brep = _get_doc_brep(object_id)
            if brep is not None and brep.IsValid:
                breps.append(brep)

        return breps
    finally:
        for object_id in subject_ids:
            _delete_object_quietly(object_id)
        for object_id in cutter_ids:
            _delete_object_quietly(object_id)
        for object_id in result_ids or []:
            _delete_object_quietly(object_id)


def _finish_parts(parts, pins_by_part, sockets_by_part, tolerance):
    finished = {}
    boolean_warnings = []

    for key, part_breps in parts.items():
        _log(
            "Finishing part {0}: base breps {1}, pins {2}, sockets {3}".format(
                key,
                len(part_breps),
                len(pins_by_part.get(key, [])),
                len(sockets_by_part.get(key, [])),
            )
        )
        current = list(part_breps)
        if key in sockets_by_part:
            current, difference_ok = _boolean_difference(current, sockets_by_part[key], tolerance)
            _log("Part {0}: after socket difference count {1}, ok {2}".format(key, len(current), difference_ok))
            if not difference_ok:
                boolean_warnings.append("Socket subtraction may have failed for part {0}.".format(key))

        if key in pins_by_part:
            current, union_ok = _boolean_union(current + pins_by_part[key], tolerance)
            _log("Part {0}: after peg union count {1}, ok {2}".format(key, len(current), union_ok))
            if not union_ok:
                boolean_warnings.append("Peg union may have failed for part {0}.".format(key))

        finished[key] = current

    _log_parts_summary("Finished parts", finished)
    return finished, boolean_warnings


def _combined_bbox(breps):
    bbox = Rhino.Geometry.BoundingBox.Empty
    for brep in breps:
        bbox.Union(brep.GetBoundingBox(True))
    return bbox


def _bbox_dimensions(bbox):
    return (bbox.Max.X - bbox.Min.X, bbox.Max.Y - bbox.Min.Y, bbox.Max.Z - bbox.Min.Z)


def _fits_bed(breps, bed_size, tolerance):
    dimensions = _bbox_dimensions(_combined_bbox(breps))
    return (
        dimensions[0] <= bed_size[0] + tolerance
        and dimensions[1] <= bed_size[1] + tolerance
        and dimensions[2] <= bed_size[2] + tolerance
    ), dimensions


def _ensure_output_layer():
    layer_index = sc.doc.Layers.FindByFullPath(OUTPUT_LAYER, True)
    if layer_index >= 0:
        return layer_index

    layer = Rhino.DocObjects.Layer()
    layer.Name = OUTPUT_LAYER
    return sc.doc.Layers.Add(layer)


def _add_parts_to_document(finished_parts, source_name, layer_index, bed_size, tolerance):
    attr_template = Rhino.DocObjects.ObjectAttributes()
    attr_template.LayerIndex = layer_index

    added = 0
    added_ids = []
    oversized = []
    for key in sorted(finished_parts.keys()):
        breps = finished_parts[key]
        if not breps:
            _log("Output part {0} key {1}: skipped because it has no breps.".format(source_name, key))
            continue

        fits, dimensions = _fits_bed(breps, bed_size, tolerance)
        _log(
            "Output part {0} key {1}: breps {2}, dimensions {3}, fits_bed {4}".format(
                source_name,
                key,
                len(breps),
                _format_tuple(dimensions),
                fits,
            )
        )
        if not fits:
            oversized.append((key, dimensions))

        for item_index, brep in enumerate(breps):
            attrs = attr_template.Duplicate()
            attrs.Name = "{0}_part_{1}_{2}_{3}_{4}".format(
                source_name, key[0] + 1, key[1] + 1, key[2] + 1, item_index + 1
            )
            object_id = sc.doc.Objects.AddBrep(brep, attrs)
            if object_id != System.Guid.Empty:
                added_ids.append(object_id)
                added += 1

    return added, added_ids, oversized


def _object_label(object_id, sequence):
    name = rs.ObjectName(object_id)
    if name:
        return name
    return "object_{0}".format(sequence)


def _confirm_large_part_count(total_cells):
    if total_cells <= 80:
        return True
    answer = rs.GetString(
        "This will create up to {0} grid cells. Continue? Yes/No".format(total_cells),
        "Yes",
        ["Yes", "No"],
    )
    return answer == "Yes"


def main():
    log_path = _start_log()
    if log_path:
        _message("Debug log: {0}".format(log_path))

    object_ids = rs.GetObjects(
        "Select closed solid polysurfaces/Breps to split for printing",
        rs.filter.polysurface | rs.filter.surface,
        preselect=True,
    )
    if not object_ids:
        _log("No objects selected. Run canceled.")
        return
    _log("Selected object ids: {0}".format(", ".join(str(object_id) for object_id in object_ids)))

    bed_text = rs.GetString(
        "Print bed size X,Y,Z in model units",
        "{0},{1},{2}".format(DEFAULT_BED_SIZE[0], DEFAULT_BED_SIZE[1], DEFAULT_BED_SIZE[2]),
    )
    if not bed_text:
        return

    try:
        bed_size = _parse_bed_size(bed_text)
    except Exception as error:
        rs.MessageBox("Could not read print bed size: {0}".format(error), 16, "Split for print")
        return

    clearance = rs.GetReal("Socket radial clearance / tolerance offset", DEFAULT_TOLERANCE, 0.0)
    if clearance is None:
        return

    pin_radius = rs.GetReal("Peg radius", DEFAULT_PIN_RADIUS, sc.doc.ModelAbsoluteTolerance * 2.0)
    if pin_radius is None:
        return

    pin_depth = rs.GetReal("Peg depth across each split", DEFAULT_PIN_DEPTH, sc.doc.ModelAbsoluteTolerance * 2.0)
    if pin_depth is None:
        return

    pins_per_seam = rs.GetInteger("Pegs per split seam", DEFAULT_PINS_PER_SEAM, 1, 8)
    if pins_per_seam is None:
        _log("Run canceled while requesting pins per seam.")
        return

    default_hide = "Yes" if DEFAULT_HIDE_ORIGINALS else "No"
    hide_originals_answer = rs.GetString(
        "Hide original object after successful split? Yes/No",
        default_hide,
        ["Yes", "No"],
    )
    if not hide_originals_answer:
        _log("Run canceled while requesting hide-originals option.")
        return
    hide_originals = hide_originals_answer == "Yes"

    doc_tolerance = sc.doc.ModelAbsoluteTolerance
    if doc_tolerance <= 0.0:
        doc_tolerance = 0.01

    if min(bed_size) <= (pin_radius + clearance) * 2.0:
        rs.MessageBox("The print bed is too small for the requested fitting radius.", 16, "Split for print")
        _log("Run stopped: bed too small for fitting radius.")
        return

    _log(
        "Run settings: bed {0}, clearance {1}, pin_radius {2}, pin_depth {3}, pins_per_seam {4}, hide_originals {5}, doc_tolerance {6}".format(
            _format_tuple(bed_size),
            _format_number(clearance),
            _format_number(pin_radius),
            _format_number(pin_depth),
            pins_per_seam,
            hide_originals,
            _format_number(doc_tolerance),
        )
    )

    rs.EnableRedraw(False)
    layer_index = _ensure_output_layer()

    total_added = 0
    all_added_ids = []
    all_warnings = []
    all_oversized = []

    try:
        for sequence, object_id in enumerate(object_ids, 1):
            brep = _get_brep_from_object(object_id)
            if brep is None:
                all_warnings.append("Skipped {0}: object is not a closed Brep/polysurface.".format(object_id))
                continue

            bbox = brep.GetBoundingBox(True)
            intervals_by_axis, counts = _plan_axis_intervals(bbox, bed_size, pin_depth)
            total_cells = counts[0] * counts[1] * counts[2]
            _log("Processing {0}: source object id {1}".format(_object_label(object_id, sequence), object_id))
            _log("Source bbox: {0}".format(_format_bbox(bbox)))
            _log("Planned grid counts: {0} x {1} x {2} = {3}".format(counts[0], counts[1], counts[2], total_cells))
            _log_intervals(intervals_by_axis)
            if not _confirm_large_part_count(total_cells):
                all_warnings.append("Skipped {0}: canceled large split.".format(object_id))
                _log("Skipped {0}: user canceled large split.".format(object_id))
                continue

            label = _object_label(object_id, sequence)
            _message(
                "Splitting {0}: grid {1} x {2} x {3}.".format(label, counts[0], counts[1], counts[2])
            )

            parts, split_method, box_cell_count, plane_cell_count, doc_plane_cell_count = _split_into_grid(
                brep, intervals_by_axis, doc_tolerance
            )
            if not parts:
                all_warnings.append("No split parts were created for {0}.".format(label))
                continue
            _message(
                "Created geometry in {0} of {1} planned grid cell(s) for {2} using {3}.".format(
                    len(parts), total_cells, label, split_method
                )
            )
            _message(
                "Split coverage check for {0}: plane-split {1} cell(s), document-plane-split {2} cell(s), box-intersection {3} cell(s).".format(
                    label, plane_cell_count, doc_plane_cell_count, box_cell_count
                )
            )

            pins_by_part, sockets_by_part, fitting_warnings = _add_fitting_geometry(
                parts,
                intervals_by_axis,
                counts,
                pin_radius,
                pin_depth,
                clearance,
                pins_per_seam,
                doc_tolerance,
            )
            all_warnings.extend(fitting_warnings)

            finished_parts, boolean_warnings = _finish_parts(
                parts, pins_by_part, sockets_by_part, doc_tolerance
            )
            all_warnings.extend(boolean_warnings)

            added, added_ids, oversized = _add_parts_to_document(
                finished_parts, label, layer_index, bed_size, doc_tolerance
            )
            total_added += added
            all_added_ids.extend(added_ids)
            if hide_originals and added > 0:
                rs.HideObject(object_id)
                _log("Hidden original object {0} after successful output.".format(object_id))
            for key, dimensions in oversized:
                all_oversized.append((label, key, dimensions))

    except Exception as error:
        rs.EnableRedraw(True)
        rs.MessageBox("Split failed: {0}".format(error), 16, "Split for print")
        _log("Unhandled split failure: {0}".format(error))
        raise
    finally:
        rs.EnableRedraw(True)
        sc.doc.Views.Redraw()

    summary = "Created {0} fitted print part Brep object(s) on layer '{1}'.".format(total_added, OUTPUT_LAYER)
    _message(summary)
    if all_added_ids:
        rs.UnselectAllObjects()
        for object_id in all_added_ids:
            rs.SelectObject(object_id)

    if all_oversized:
        _message("Parts exceeding the print bed after fittings:")
        for label, key, dimensions in all_oversized:
            _message(
                "  {0} part {1}: {2:.3f} x {3:.3f} x {4:.3f}".format(
                    label, key, dimensions[0], dimensions[1], dimensions[2]
                )
            )

    if all_warnings:
        _message("Warnings:")
        for warning in all_warnings:
            _message("  " + warning)
    _log("Run complete. Added ids: {0}".format(", ".join(str(object_id) for object_id in all_added_ids)))


if __name__ == "__main__":
    main()
