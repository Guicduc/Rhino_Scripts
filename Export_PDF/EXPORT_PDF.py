# -*- coding: utf-8 -*-
from __future__ import unicode_literals

import os
import System
import Rhino
import rhinoscriptsyntax as rs
import scriptcontext as sc

DPI  = 300
TOL  = rs.UnitAbsoluteTolerance()
SECT = "ExportPDFPrefs"              # onde as preferências ficam guardadas

# ------------------------------------------------------------
# helpers – geometria
# ------------------------------------------------------------
def tl_br(rect_id):
    pts = rs.PolylineVertices(rect_id) or rs.CurveEditPoints(rect_id)
    if not pts: return None
    if pts[0].DistanceTo(pts[-1]) < TOL:
        pts = pts[:-1]
    if len(pts) != 4: return None
    y_max, y_min = max(p.Y for p in pts), min(p.Y for p in pts)
    tl = min([p for p in pts if abs(p.Y - y_max) < TOL], key=lambda p: p.X)
    br = max([p for p in pts if abs(p.Y - y_min) < TOL], key=lambda p: p.X)
    return tl, br

def mm2px(mm, dpi=DPI):
    return int(mm / 25.4 * dpi)

def center_pt(obj_id):
    bb = rs.BoundingBox(obj_id)
    return (bb[0] + bb[6]) / 2.0 if bb else None

def first_obj_in_rect(tl, br, cand_ids):
    min_x, max_x, min_y, max_y = tl.X, br.X, br.Y, tl.Y
    for oid in cand_ids:
        c = center_pt(oid)
        if c and (min_x - TOL) <= c.X <= (max_x + TOL) and (min_y - TOL) <= c.Y <= (max_y + TOL):
            return oid
    return None

# ------------------------------------------------------------
# helpers – texto
# ------------------------------------------------------------
def is_editable_text(oid):
    """Retorna True se o objeto for Text ou TextDot."""
    return rs.IsText(oid) or rs.IsTextDot(oid)

def write_label(oid, text):
    """Atualiza o conteúdo se for Text/TextDot, não cria nada novo."""
    if not oid or not is_editable_text(oid):
        return False
    if rs.IsTextDot(oid):
        rs.TextDotText(oid, text)
    else:
        rs.TextObjectText(oid, text)
    return True

# ------------------------------------------------------------
# helpers – preferências persistentes
# ------------------------------------------------------------
def get_pref(key, default):
    val = rs.GetDocumentData(SECT, key)
    return val if val is not None else default

def set_pref(key, value):
    rs.SetDocumentData(SECT, key, value)

# ------------------------------------------------------------
# função principal
# ------------------------------------------------------------
def export_combine():
    rect_ids = rs.GetObjects(u"Selecione retângulos", rs.filter.curve, preselect=True)
    if not rect_ids:
        return

    out_dir = rs.BrowseForFolder(message=u"Pasta destino dos PDFs")
    if not out_dir:
        return

    # ---------- interface única ----------
    keys = [
        u"Nome do arquivo PDF (sem .pdf)",
        u"Nome do Projetista",
        u"Data (DD/MM/AAAA)",
        u"Nº da Revisão"
    ]
    defaults = [
        get_pref("Titulo",   u"SheetSet"),
        get_pref("Projetista", u"Fulano de Tal"),
        get_pref("Data",       u"06/06/2025"),
        get_pref("Revisao",    u"1")
    ]
    vals = rs.PropertyListBox(keys, defaults, title=u"Informações do Desenho")
    if not vals:
        return

    base        = vals[0].strip()
    proj_label  = u"PROJETISTA: {}".format(vals[1].strip())
    data_label  = vals[2].strip()                    # sem “DATA:”
    rev_label   = u"REVISAO R{}".format(vals[3].strip())

    # grava para uso futuro
    set_pref("Titulo",   base)
    set_pref("Projetista", vals[1].strip())
    set_pref("Data",       vals[2].strip())
    set_pref("Revisao",    vals[3].strip())

    # ---------- aplica projetista e data ----------
    def apply_to_named(name, text):
        count = 0
        for oid in rs.ObjectsByName(name) or []:
            if write_label(oid, text):
                count += 1
        return count

    print(u"✔ Projetista → {} objeto(s) atualizados".format(apply_to_named("Projetista", proj_label)))
    print(u"✔ Data       → {} objeto(s) atualizados".format(apply_to_named("Data",       data_label)))

    rev_objs    = [oid for oid in rs.ObjectsByName("Revisao") if is_editable_text(oid)] or []
    pagina_objs = [oid for oid in rs.ObjectsByName("Pagina")  if is_editable_text(oid)] or []

    # ---------- numeração + PDFs ----------
    total  = len(rect_ids)
    digits = max(2, len(str(total)))

    view   = sc.doc.Views.ActiveView
    a4_mm  = (210.0, 297.0)
    pdf_all, temp_paths = Rhino.FileIO.FilePdf.Create(), []

    for idx, rid in enumerate(rect_ids, 1):
        pair = tl_br(rid)
        if not pair:
            print(u"Ignorado (não é retângulo): {}".format(rid)); continue
        tl, br = pair

        # página NN/TT
        page_label = u"{}/{}".format(str(idx).zfill(digits), str(total).zfill(digits))
        pid_page = first_obj_in_rect(tl, br, pagina_objs)
        if pid_page:
            write_label(pid_page, page_label)

        # revisão
        pid_rev = first_obj_in_rect(tl, br, rev_objs)
        if pid_rev:
            write_label(pid_rev, rev_label)
            rev_objs.remove(pid_rev)

        # captura
        w, h = abs(br.X - tl.X), abs(tl.Y - br.Y)
        w_mm, h_mm = a4_mm if h >= w else (a4_mm[1], a4_mm[0])
        size_px = System.Drawing.Size(mm2px(w_mm), mm2px(h_mm))
        cap = Rhino.Display.ViewCaptureSettings(view, size_px, DPI)
        cap.SetWindowRect(tl, br)

        pdf_one = Rhino.FileIO.FilePdf.Create(); pdf_one.AddPage(cap)
        tmp = os.path.join(out_dir, u"{}_{}.pdf".format(base, idx))
        pdf_one.Write(tmp); temp_paths.append(tmp)
        pdf_all.AddPage(cap)
        print(u"Página {} capturada.".format(idx))

    # ---------- grava combinado e limpa ----------
    final_path = os.path.join(out_dir, u"{}.pdf".format(base))
    pdf_all.Write(final_path)
    for p in temp_paths:
        try: os.remove(p)
        except: pass
    print(u"\n✔ PDF combinado salvo em:\n{}\n(individuais removidos)".format(final_path))

# ------------------------------------------------------------
# execução
# ------------------------------------------------------------
if __name__ == "__main__":
    export_combine()
