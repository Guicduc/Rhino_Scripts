# -*- coding: utf-8 -*-

import Rhino
import scriptcontext as sc

def select_all_annotations(clear_first=True):
    if clear_first:
        sc.doc.Objects.UnselectAll()

    # Types that cover the common annotation objects across Rhino versions
    ann_types = (
        Rhino.Geometry.TextEntity,
        Rhino.Geometry.TextDot,
        Rhino.Geometry.Leader,
        Rhino.Geometry.Dimension
    )

    count = 0

    for rhobj in sc.doc.Objects:
        if not rhobj:
            continue

        geo = rhobj.Geometry
        if not geo:
            continue

        if isinstance(geo, ann_types):
            if sc.doc.Objects.Select(rhobj.Id, True):
                count += 1

    sc.doc.Views.Redraw()
    print("{} annotations selected.".format(count))

if __name__ == "__main__":
    select_all_annotations()
