# -*- coding: utf-8 -*-
"""Use a selected dimension's named style and layer for new objects."""

import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc


def is_dimension(rhino_object, geometry, component_index):
    """Exclude text and leaders from Rhino's annotation selection filter."""
    return isinstance(geometry, Rhino.Geometry.Dimension)


def activate_dimension_settings():
    object_id = rs.GetObject(
        "Selecione a cota com o estilo e layer desejados",
        filter=rs.filter.annotation,
        preselect=True,
        select=False,
        custom_filter=is_dimension,
    )
    if not object_id:
        return

    obj = sc.doc.Objects.FindId(object_id)
    if obj is None or not isinstance(obj.Geometry, Rhino.Geometry.Dimension):
        return

    # Use the registered style, without changing it or copying per-object overrides.
    style = sc.doc.DimStyles.FindId(obj.Geometry.DimensionStyleId)
    if style is None or style.IsDeleted:
        print("Nao foi possivel encontrar o estilo da cota.")
        return

    previous_style_index = sc.doc.DimStyles.CurrentIndex
    if not sc.doc.DimStyles.SetCurrent(style.Index, True):
        print("Nao foi possivel ativar o estilo da cota.")
        return

    if not sc.doc.Layers.SetCurrentLayerIndex(obj.Attributes.LayerIndex, True):
        if not sc.doc.DimStyles.SetCurrent(previous_style_index, True):
            print("Nao foi possivel restaurar o estilo anterior.")
        print("Nao foi possivel ativar o layer da cota.")
        return

    layer = sc.doc.Layers[obj.Attributes.LayerIndex]
    sc.doc.Views.Redraw()
    print("Estilo ativo: {} | Layer ativo: {}".format(style.Name, layer.FullPath))


if __name__ == "__main__":
    activate_dimension_settings()
