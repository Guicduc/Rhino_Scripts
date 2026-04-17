# -*- coding: utf-8 -*-

import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc


def get_text_content(obj_id):
    obj = sc.doc.Objects.Find(obj_id)
    if not obj:
        return None

    geom = obj.Geometry

    # Texto normal
    if isinstance(geom, Rhino.Geometry.TextEntity):
        return geom.PlainText

    # TextDot
    if isinstance(geom, Rhino.Geometry.TextDot):
        return geom.Text

    return None


def select_equal_texts():
    # Seleciona um texto de referência
    ref_id = rs.GetObject(
        "Selecione um texto de referência",
        preselect=True,
        select=False
    )

    if not ref_id:
        return

    ref_text = get_text_content(ref_id)

    if ref_text is None:
        print("O objeto selecionado não é um texto válido.")
        return

    rs.UnselectAllObjects()

    matched_ids = []

    # Procura por textos normais
    for obj in sc.doc.Objects:
        if not obj:
            continue

        geom = obj.Geometry

        if isinstance(geom, Rhino.Geometry.TextEntity):
            if geom.PlainText == ref_text:
                matched_ids.append(obj.Id)

        elif isinstance(geom, Rhino.Geometry.TextDot):
            if geom.Text == ref_text:
                matched_ids.append(obj.Id)

    if matched_ids:
        rs.SelectObjects(matched_ids)
        print("{} textos encontrados com o conteúdo: '{}'".format(len(matched_ids), ref_text))
    else:
        print("Nenhum texto igual encontrado.")


if __name__ == "__main__":
    select_equal_texts()