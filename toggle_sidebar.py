import Rhino
import scriptcontext as sc
import System

KEY = "ToggleRightPanelContainer"

if KEY in sc.sticky:
    data = sc.sticky[KEY]
    dock_id = data[0]
    panel_ids = data[1]

    for panel_id in panel_ids:
        Rhino.UI.Panels.OpenPanel(dock_id, panel_id, False)

    del sc.sticky[KEY]

else:
    layers_id = Rhino.UI.PanelIds.Layers
    dock_id = Rhino.UI.Panels.PanelDockBar(layers_id)

    if dock_id != System.Guid.Empty:
        panel_ids = []

        for panel_id in Rhino.UI.Panels.GetOpenPanelIds():
            if Rhino.UI.Panels.PanelDockBar(panel_id) == dock_id:
                panel_ids.append(panel_id)

        sc.sticky[KEY] = (dock_id, panel_ids)

        for panel_id in panel_ids:
            Rhino.UI.Panels.ClosePanel(panel_id)