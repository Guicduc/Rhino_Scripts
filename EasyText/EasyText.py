# EasyText for Rhino
# Workflow: run command, click insertion point, type in-place, press Ctrl+Enter.

import Rhino
import Rhino.UI
import System
import System.Drawing
import rhinoscriptsyntax as rs
import scriptcontext as sc
import Eto.Forms as forms
import Eto.Drawing as drawing


DEFAULT_TEXT_HEIGHT = 10.0
DEFAULT_FONT = "Arial"
EDITOR_WIDTH = 180
EDITOR_HEIGHT = 70
EDITOR_OFFSET_X = 0
EDITOR_OFFSET_Y = 0
DRAG_THRESHOLD_PIXELS = 8
MIN_BOX_EDITOR_WIDTH = 60
MIN_BOX_EDITOR_HEIGHT = 30
MIN_TEXT_HEIGHT = 0.1


class InlineTextEditor(forms.Form):
    def __init__(self):
        forms.Form.__init__(self)

        self.Title = ""
        self.ShowInTaskbar = False
        self.Topmost = True
        self.Resizable = True
        self.WindowStyle = getattr(forms.WindowStyle, "None")
        self.Padding = drawing.Padding(0)
        self.ClientSize = drawing.Size(EDITOR_WIDTH, EDITOR_HEIGHT)
        self.accepted = False

        self.text_area = forms.TextArea()
        self.text_area.Wrap = True
        self.text_area.Font = drawing.Font(DEFAULT_FONT, 13)
        self.text_area.BackgroundColor = drawing.Colors.White
        self.text_area.TextColor = drawing.Colors.Black
        self.text_area.KeyDown += self.on_key_down
        self.text_area.LostFocus += self.on_lost_focus

        self.Content = self.text_area

    def on_key_down(self, sender, e):
        if e.Key == forms.Keys.Enter and (e.Modifiers & forms.Keys.Control) == forms.Keys.Control:
            e.Handled = True
            self.accept()
        elif e.Key == forms.Keys.Escape:
            e.Handled = True
            self.cancel()

    def on_lost_focus(self, sender, e):
        if not self.accepted:
            self.cancel()

    def accept(self):
        if self.text and self.text.strip():
            self.accepted = True
            self.Close()

    def cancel(self):
        self.accepted = False
        self.Close()

    @property
    def text(self):
        return self.text_area.Text


class TextPointGetter(Rhino.Input.Custom.GetPoint):
    def __init__(self):
        Rhino.Input.Custom.GetPoint.__init__(self)
        self.down_point = Rhino.Geometry.Point3d.Unset
        self.up_point = Rhino.Geometry.Point3d.Unset
        self.current_point = Rhino.Geometry.Point3d.Unset
        self.down_window_point = None
        self.up_window_point = None
        self.current_window_point = None
        self.pick_view = None
        self.is_down = False

    def OnMouseDown(self, e):
        Rhino.Input.Custom.GetPoint.OnMouseDown(self, e)
        self.down_point = e.Point
        self.current_point = e.Point
        self.down_window_point = e.WindowPoint
        self.current_window_point = e.WindowPoint
        self.pick_view = e.Viewport.ParentView
        self.is_down = True

    def OnMouseMove(self, e):
        Rhino.Input.Custom.GetPoint.OnMouseMove(self, e)
        if self.is_down:
            self.current_point = e.Point
            self.current_window_point = e.WindowPoint
            self.pick_view = e.Viewport.ParentView

    def OnMouseUp(self, e):
        try:
            Rhino.Input.Custom.GetPoint.OnMouseUp(self, e)
        except:
            pass
        self.up_point = e.Point
        self.up_window_point = e.WindowPoint
        self.current_point = e.Point
        self.current_window_point = e.WindowPoint
        self.pick_view = e.Viewport.ParentView
        self.is_down = False

    def OnDynamicDraw(self, e):
        Rhino.Input.Custom.GetPoint.OnDynamicDraw(self, e)
        if not self.is_dragging():
            return

        try:
            plane = e.Viewport.ConstructionPlane()
            corners = get_box_corners(self.down_point, e.CurrentPoint, plane)
            corners.append(corners[0])
            e.Display.DrawPolyline(corners, System.Drawing.Color.DodgerBlue, 2)
        except:
            pass

    def drag_distance(self):
        start = self.down_window_point
        end = self.up_window_point or self.current_window_point
        if not start or not end:
            return 0

        dx = float(end.X - start.X)
        dy = float(end.Y - start.Y)
        return (dx * dx + dy * dy) ** 0.5

    def is_dragging(self):
        return self.drag_distance() >= DRAG_THRESHOLD_PIXELS


def get_text_height():
    try:
        dimstyle = sc.doc.DimStyles.Current
        if dimstyle and dimstyle.TextHeight > 0:
            return dimstyle.TextHeight
    except:
        pass
    return DEFAULT_TEXT_HEIGHT


def get_insertion_plane(point, view):
    if view:
        plane = view.ActiveViewport.ConstructionPlane()
    else:
        plane = Rhino.Geometry.Plane.WorldXY

    plane.Origin = point
    return plane


def add_text(text, plane):
    height = get_text_height()
    object_id = rs.AddText(text, plane, height, DEFAULT_FONT, 0, 0)
    if object_id:
        rs.SelectObject(object_id)
    return object_id


def duplicate_dimstyle(text_height):
    style = sc.doc.DimStyles.Current.Duplicate()
    style.TextHeight = max(float(text_height), MIN_TEXT_HEIGHT)
    return style


def normalize_text(text):
    return text.replace("\r\n", "\n").replace("\r", "\n")


def apply_text_wrapping(entity, text, wrapped, rect_width):
    clean_text = normalize_text(text)

    try:
        entity.PlainText = clean_text
    except:
        try:
            entity.Text = clean_text
        except:
            pass

    if wrapped and rect_width > 0.0:
        try:
            entity.FormatWidth = float(rect_width)
        except:
            pass

        try:
            entity.WrapText()
        except:
            pass

    return entity


def create_text_entity(text, plane, text_height, wrapped=False, rect_width=0.0):
    style = duplicate_dimstyle(text_height)
    width = max(float(rect_width), 0.0)
    clean_text = normalize_text(text)

    try:
        entity = Rhino.Geometry.TextEntity.Create(
            clean_text,
            plane,
            style,
            wrapped and width > 0.0,
            width,
            0.0,
        )
    except:
        entity = None

    if entity is None:
        entity = Rhino.Geometry.TextEntity()
        entity.Plane = plane
        entity.TextHeight = max(float(text_height), MIN_TEXT_HEIGHT)
        try:
            entity.PlainText = clean_text
        except:
            entity.Text = clean_text

    try:
        entity.Justification = Rhino.Geometry.TextJustification.TopLeft
    except:
        pass

    return apply_text_wrapping(entity, clean_text, wrapped, width)


def get_entity_plane_size(entity, plane):
    bbox = entity.GetBoundingBox(True)
    if not bbox.IsValid:
        return 0.0, 0.0

    xs = []
    ys = []
    for corner in bbox.GetCorners():
        vector = corner - plane.Origin
        xs.append(Rhino.Geometry.Vector3d.Multiply(vector, plane.XAxis))
        ys.append(Rhino.Geometry.Vector3d.Multiply(vector, plane.YAxis))

    return max(xs) - min(xs), max(ys) - min(ys)


def fit_text_height(text, plane, box_width, box_height):
    if box_width <= 0.0 or box_height <= 0.0:
        return get_text_height()

    low = MIN_TEXT_HEIGHT
    high = max(float(box_height), MIN_TEXT_HEIGHT)

    for _ in range(18):
        mid = (low + high) * 0.5
        entity = create_text_entity(text, plane, mid, True, box_width)
        width, height = get_entity_plane_size(entity, plane)

        if width <= box_width * 1.01 and height <= box_height * 1.01:
            low = mid
        else:
            high = mid

    return max(low, MIN_TEXT_HEIGHT)


def add_wrapped_text(text, plane, box_width, box_height):
    text_height = fit_text_height(text, plane, box_width, box_height)
    entity = create_text_entity(text, plane, text_height, True, box_width)
    object_id = sc.doc.Objects.AddText(entity)
    if object_id:
        rs.SelectObject(object_id)
    return object_id


def get_dpi_scale():
    try:
        graphics = System.Drawing.Graphics.FromHwnd(System.IntPtr.Zero)
        scale_x = graphics.DpiX / 96.0
        scale_y = graphics.DpiY / 96.0
        graphics.Dispose()
        return scale_x, scale_y
    except:
        return 1.0, 1.0


def screen_to_eto_point(x, y):
    scale_x, scale_y = get_dpi_scale()
    return drawing.Point(
        int((x + EDITOR_OFFSET_X) / scale_x),
        int((y + EDITOR_OFFSET_Y) / scale_y),
    )


def screen_to_eto_size(width, height):
    scale_x, scale_y = get_dpi_scale()
    return drawing.Size(
        max(int(width / scale_x), MIN_BOX_EDITOR_WIDTH),
        max(int(height / scale_y), MIN_BOX_EDITOR_HEIGHT),
    )


def get_editor_location(point, view):
    if not view:
        return drawing.Point(200, 200)

    try:
        client_point = view.ActiveViewport.WorldToClient(point)
        viewport_rect = view.ScreenRectangle

        return screen_to_eto_point(
            viewport_rect.Left + client_point.X,
            viewport_rect.Top + client_point.Y,
        )
    except:
        try:
            client_point = view.ActiveViewport.WorldToClient(point)
            screen_point = view.ClientToScreen(client_point)
            return screen_to_eto_point(screen_point.X, screen_point.Y)
        except:
            return drawing.Point(200, 200)


def get_screen_point(point, view):
    client_point = view.ActiveViewport.WorldToClient(point)
    viewport_rect = view.ScreenRectangle
    return (
        viewport_rect.Left + client_point.X,
        viewport_rect.Top + client_point.Y,
    )


def get_editor_rect_from_drag(pick):
    start = pick.down_window_point
    end = pick.up_window_point or pick.current_window_point
    view = pick.pick_view

    if start and end and view:
        viewport_rect = view.ScreenRectangle
        x1 = viewport_rect.Left + start.X
        y1 = viewport_rect.Top + start.Y
        x2 = viewport_rect.Left + end.X
        y2 = viewport_rect.Top + end.Y
    else:
        x1, y1 = get_screen_point(pick.down_point, view)
        x2, y2 = get_screen_point(pick.up_point, view)

    left = min(x1, x2)
    top = min(y1, y2)
    width = abs(x2 - x1)
    height = abs(y2 - y1)

    return screen_to_eto_point(left, top), screen_to_eto_size(width, height)


def show_inline_editor(location, size):
    editor = InlineTextEditor()
    editor.ClientSize = drawing.Size(int(size.Width), int(size.Height))
    editor.Location = location
    editor.Owner = Rhino.UI.RhinoEtoApp.MainWindow
    editor.Show()
    editor.text_area.Focus()

    while editor.Visible:
        forms.Application.Instance.RunIteration()

    if editor.accepted:
        return editor.text
    return None


def plane_coordinate(plane, point):
    vector = point - plane.Origin
    return (
        Rhino.Geometry.Vector3d.Multiply(vector, plane.XAxis),
        Rhino.Geometry.Vector3d.Multiply(vector, plane.YAxis),
    )


def get_box_corners(start_point, end_point, plane):
    x1, y1 = plane_coordinate(plane, start_point)
    x2, y2 = plane_coordinate(plane, end_point)

    left = min(x1, x2)
    right = max(x1, x2)
    bottom = min(y1, y2)
    top = max(y1, y2)

    return [
        plane.PointAt(left, top),
        plane.PointAt(right, top),
        plane.PointAt(right, bottom),
        plane.PointAt(left, bottom),
    ]


def get_text_box(start_point, end_point, view):
    plane = get_insertion_plane(start_point, view)
    x1, y1 = plane_coordinate(plane, start_point)
    x2, y2 = plane_coordinate(plane, end_point)

    left = min(x1, x2)
    right = max(x1, x2)
    bottom = min(y1, y2)
    top = max(y1, y2)

    text_plane = Rhino.Geometry.Plane(plane)
    text_plane.Origin = plane.PointAt(left, top)
    return text_plane, right - left, top - bottom


def get_pick():
    gp = TextPointGetter()
    gp.SetCommandPrompt(" ")
    result = gp.Get(True)

    if result != Rhino.Input.GetResult.Point:
        return None
    if gp.CommandResult() != Rhino.Commands.Result.Success:
        return None

    if not gp.up_point.IsValid:
        gp.up_point = gp.Point()
    if not gp.down_point.IsValid:
        gp.down_point = gp.up_point
    if not gp.pick_view:
        gp.pick_view = gp.View()

    return gp


def main():
    pick = get_pick()
    if not pick:
        return

    view = pick.pick_view

    if pick.is_dragging():
        location, size = get_editor_rect_from_drag(pick)
        text = show_inline_editor(location, size)

        if not text:
            return

        plane, width, height = get_text_box(pick.down_point, pick.up_point, view)
        object_id = add_wrapped_text(text, plane, width, height)
    else:
        point = pick.up_point if pick.up_point.IsValid else pick.Point()
        location = get_editor_location(point, view)
        size = drawing.Size(EDITOR_WIDTH, EDITOR_HEIGHT)
        text = show_inline_editor(location, size)

        if not text:
            return

        object_id = add_text(text, get_insertion_plane(point, view))

    if object_id:
        sc.doc.Views.Redraw()


if __name__ == "__main__":
    main()
