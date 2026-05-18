# -*- coding: utf-8 -*-

import System
import Rhino
import Eto.Drawing as drawing
import Eto.Forms as forms
import rhinoscriptsyntax as rs
import scriptcontext as sc


STATE_KEY = "KINO_ISOLATION_STATE"
FORM_KEY = "KINO_ISOLATION_FORM"


def _as_guid(obj_id):
    if isinstance(obj_id, System.Guid):
        return obj_id
    try:
        return System.Guid(str(obj_id))
    except Exception:
        return None


def _valid_ids(ids):
    result = []
    for obj_id in ids or []:
        guid = _as_guid(obj_id)
        if guid and sc.doc.Objects.Find(guid):
            result.append(guid)
    return result


def _visible_object_ids():
    ids = []
    for rhobj in sc.doc.Objects:
        if not rhobj:
            continue
        if rhobj.IsDeleted or rhobj.IsHidden:
            continue
        ids.append(rhobj.Id)
    return ids


def _get_state():
    state = sc.sticky.get(STATE_KEY)
    if not state:
        return None

    return {
        "group_ids": _valid_ids(state.get("group_ids")),
        "hidden_outside_ids": _valid_ids(state.get("hidden_outside_ids")),
        "hidden_group_ids": _valid_ids(state.get("hidden_group_ids")),
        "single_id": _as_guid(state.get("single_id")) if state.get("single_id") else None,
        "edit_mode": bool(state.get("edit_mode")),
    }


def _set_state(group_ids, hidden_outside_ids, hidden_group_ids=None, single_id=None, edit_mode=False):
    sc.sticky[STATE_KEY] = {
        "group_ids": list(group_ids or []),
        "hidden_outside_ids": list(hidden_outside_ids or []),
        "hidden_group_ids": list(hidden_group_ids or []),
        "single_id": single_id,
        "edit_mode": edit_mode,
    }


def _clear_state():
    if STATE_KEY in sc.sticky:
        del sc.sticky[STATE_KEY]


def _hide(ids):
    ids = _valid_ids(ids)
    if ids:
        rs.HideObjects(ids)


def _show(ids):
    ids = _valid_ids(ids)
    if ids:
        rs.ShowObjects(ids)


def _selected_ids():
    return _valid_ids(rs.SelectedObjects() or [])


def _clear_selection():
    try:
        rs.UnselectAllObjects()
    except Exception:
        pass


def _apply_group_visibility(group_ids, hidden_outside_ids):
    group_ids = _valid_ids(group_ids)
    group_set = set(group_ids)
    hidden_outside_ids = [obj_id for obj_id in _valid_ids(hidden_outside_ids) if obj_id not in group_set]

    _show(group_ids)
    _hide(hidden_outside_ids)
    _set_state(group_ids, hidden_outside_ids)
    sc.doc.Views.Redraw()


def isolate_selected_group():
    finish_isolation(redraw=False, quiet=True)

    group_ids = _selected_ids()
    if not group_ids:
        print("Kino: selecione os objetos do grupo antes de clicar em Isolar selecionados.")
        return

    group_set = set(group_ids)
    visible_ids = _visible_object_ids()
    hidden_outside_ids = [obj_id for obj_id in visible_ids if obj_id not in group_set]

    _apply_group_visibility(group_ids, hidden_outside_ids)
    _clear_selection()
    print("Kino: grupo isolado com {} objeto(s).".format(len(group_ids)))


def add_selected_to_group():
    state = _get_state()
    if not state or not state["group_ids"]:
        print("Kino: primeiro isole um grupo.")
        return

    selected_ids = _selected_ids()
    if not selected_ids:
        print("Kino: selecione objetos para adicionar ao grupo isolado.")
        return

    group_ids = list(set(state["group_ids"] + selected_ids))
    hidden_outside_ids = [obj_id for obj_id in state["hidden_outside_ids"] if obj_id not in set(group_ids)]

    _apply_group_visibility(group_ids, hidden_outside_ids)
    _clear_selection()
    print("Kino: {} objeto(s) no grupo isolado.".format(len(group_ids)))


def remove_selected_from_group():
    state = _get_state()
    if not state or not state["group_ids"]:
        print("Kino: primeiro isole um grupo.")
        return

    selected_ids = set(_selected_ids())
    if not selected_ids:
        print("Kino: selecione objetos para remover do grupo isolado.")
        return

    group_ids = [obj_id for obj_id in state["group_ids"] if obj_id not in selected_ids]
    removed_ids = [obj_id for obj_id in state["group_ids"] if obj_id in selected_ids]
    hidden_outside_ids = list(set(state["hidden_outside_ids"] + removed_ids))

    _hide(removed_ids)
    _apply_group_visibility(group_ids, hidden_outside_ids)
    _clear_selection()
    print("Kino: {} objeto(s) no grupo isolado.".format(len(group_ids)))


def show_all_for_group_edit():
    state = _get_state()
    if not state or not state["group_ids"]:
        print("Kino: primeiro isole um grupo.")
        return

    _show(state["hidden_outside_ids"])
    _show(state["group_ids"])
    _set_state(state["group_ids"], state["hidden_outside_ids"], edit_mode=True)
    _clear_selection()

    sc.doc.Views.Redraw()
    print("Kino: todos os objetos visiveis para editar o grupo isolado.")


def isolate_selected_inside_group():
    state = _get_state()
    if not state or not state["group_ids"]:
        print("Kino: primeiro isole um grupo.")
        return

    selected_ids = _selected_ids()
    if len(selected_ids) != 1:
        print("Kino: selecione exatamente um objeto do grupo para isolar.")
        return

    selected_id = selected_ids[0]
    group_ids = state["group_ids"]
    if selected_id not in set(group_ids):
        print("Kino: o objeto selecionado nao pertence ao grupo isolado.")
        return

    hidden_group_ids = [obj_id for obj_id in group_ids if obj_id != selected_id]
    _hide(state["hidden_outside_ids"])
    _hide(hidden_group_ids)
    _show([selected_id])
    _set_state(group_ids, state["hidden_outside_ids"], hidden_group_ids, selected_id)
    _clear_selection()
    rs.SelectObject(selected_id)

    sc.doc.Views.Redraw()
    print("Kino: objeto isolado dentro do grupo.")


def return_to_group():
    state = _get_state()
    if not state or not state["group_ids"]:
        print("Kino: nao existe isolamento de grupo ativo.")
        return

    _show(state["group_ids"])
    _hide(state["hidden_outside_ids"])
    _set_state(state["group_ids"], state["hidden_outside_ids"])
    _clear_selection()

    sc.doc.Views.Redraw()
    print("Kino: voltou para o grupo isolado.")


def finish_isolation(redraw=True, quiet=False):
    state = _get_state()
    if not state:
        if not quiet:
            print("Kino: nenhum isolamento ativo.")
        return

    _show(state["hidden_outside_ids"])
    _show(state["group_ids"])
    _clear_state()
    _clear_selection()

    if redraw:
        sc.doc.Views.Redraw()
    if not quiet:
        print("Kino: isolamento encerrado. Objetos anteriores foram restaurados.")


def _state_text():
    state = _get_state()
    if not state or not state["group_ids"]:
        return "Status: nenhum isolamento ativo"

    if state["edit_mode"]:
        return "Status: todos visiveis para editar o grupo"

    if state["single_id"]:
        return "Status: objeto isolado dentro do grupo"

    return "Status: grupo isolado ({} objeto(s))".format(len(state["group_ids"]))


class KinoIsolationForm(forms.Form):
    def __init__(self):
        self.Title = "Kino Isolation"
        self.ClientSize = drawing.Size(270, 250)
        self.Padding = drawing.Padding(12)
        self.Resizable = False

        self.status_label = forms.Label(Text=_state_text())

        isolate_group_button = self._make_button("Isolar selecionados", self.on_isolate_group)
        edit_all_button = self._make_button("Mostrar todos para editar", self.on_show_all)
        add_button = self._make_button("Adicionar selecionados", self.on_add)
        remove_button = self._make_button("Remover selecionados", self.on_remove)
        isolate_single_button = self._make_button("Isolar objeto selecionado", self.on_isolate_single)
        return_group_button = self._make_button("Voltar para grupo", self.on_return_to_group)
        finish_button = self._make_button("Encerrar isolamento", self.on_finish)

        layout = forms.DynamicLayout()
        layout.Spacing = drawing.Size(0, 8)
        layout.AddRow(self.status_label)
        layout.AddRow(isolate_group_button)
        layout.AddRow(edit_all_button)
        layout.AddRow(add_button)
        layout.AddRow(remove_button)
        layout.AddRow(isolate_single_button)
        layout.AddRow(return_group_button)
        layout.AddRow(finish_button)
        layout.Add(None)

        self.Content = layout
        self.Closed += self.on_closed

    def _make_button(self, text, handler):
        button = forms.Button(Text=text)
        button.Height = 32
        button.Click += handler
        return button

    def refresh_status(self):
        self.status_label.Text = _state_text()

    def on_isolate_group(self, sender, event):
        isolate_selected_group()
        self.refresh_status()

    def on_show_all(self, sender, event):
        show_all_for_group_edit()
        self.refresh_status()

    def on_add(self, sender, event):
        add_selected_to_group()
        self.refresh_status()

    def on_remove(self, sender, event):
        remove_selected_from_group()
        self.refresh_status()

    def on_isolate_single(self, sender, event):
        isolate_selected_inside_group()
        self.refresh_status()

    def on_return_to_group(self, sender, event):
        return_to_group()
        self.refresh_status()

    def on_finish(self, sender, event):
        finish_isolation()
        self.refresh_status()

    def on_closed(self, sender, event):
        if FORM_KEY in sc.sticky:
            del sc.sticky[FORM_KEY]


def show_kino_isolation_gui():
    existing_form = sc.sticky.get(FORM_KEY)
    if existing_form:
        try:
            existing_form.refresh_status()
            existing_form.Visible = True
            return
        except Exception:
            del sc.sticky[FORM_KEY]

    form = KinoIsolationForm()
    sc.sticky[FORM_KEY] = form
    form.Owner = Rhino.UI.RhinoEtoApp.MainWindow
    form.Show()


if __name__ == "__main__":
    show_kino_isolation_gui()
