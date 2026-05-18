"""Exporta cada objeto selecionado no Rhino como um arquivo separado.

Uso:
1. Execute no Rhino via RunPythonScript.
2. Selecione os objetos.
3. Escolha a pasta, o nome base e a extensao.

Exemplo:
    Se voce escolher C:\Export\peca.stl, o script cria:
    C:\Export\peca_001.stl
    C:\Export\peca_002.stl
    C:\Export\peca_003.stl
"""

import os
import re

import System
import rhinoscriptsyntax as rs
import scriptcontext as sc


EXPORT_EXTENSIONS = [
    "3dm",
    "3ds",
    "ai",
    "dwg",
    "dxf",
    "fbx",
    "iges",
    "igs",
    "obj",
    "pdf",
    "sat",
    "skp",
    "step",
    "stl",
]
DEFAULT_EXTENSION = "stl"


def clean_filename(value, fallback):
    """Remove caracteres invalidos para nomes de arquivo no Windows."""
    value = value or fallback
    value = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", value)
    value = re.sub(r"\s+", " ", value).strip()
    value = value.rstrip(". ")
    return value or fallback


def unique_path(folder, filename, extension):
    path = os.path.join(folder, "{}.{}".format(filename, extension))
    if not os.path.exists(path):
        return path

    counter = 2
    while True:
        candidate = os.path.join(folder, "{}_{}.{}".format(filename, counter, extension))
        if not os.path.exists(candidate):
            return candidate
        counter += 1


def clear_hidden_attribute(path):
    if not os.path.exists(path):
        return

    try:
        attrs = System.IO.File.GetAttributes(path)
        if attrs & System.IO.FileAttributes.Hidden:
            attrs = attrs & ~System.IO.FileAttributes.Hidden
            System.IO.File.SetAttributes(path, attrs)
    except Exception as error:
        print("Aviso: nao foi possivel remover atributo oculto de {}: {}".format(path, error))


def get_export_template_path():
    folder = rs.BrowseForFolder(message="Escolha a pasta de exportacao")
    if not folder:
        return None

    base_name = rs.StringBox(
        message="Nome base dos arquivos",
        default_value="objeto",
        title="Exportar arquivos individuais",
    )
    if not base_name:
        return None
    base_name = clean_filename(base_name, "objeto")

    extension = rs.ListBox(
        EXPORT_EXTENSIONS,
        message="Selecione a extensao dos arquivos",
        title="Formato de exportacao",
        default=DEFAULT_EXTENSION,
    )
    if not extension:
        return None
    extension = extension.lower().strip().lstrip(".")

    if not folder or not extension:
        return None

    return folder, base_name, extension


def get_export_options(extension):
    """Opcoes extras passadas ao comando Export.

    Para formatos que abrem janelas de configuracao, o Rhino pode ainda pedir
    confirmacoes conforme a versao e os plugins instalados.
    """
    return " _Enter"


def build_filename(base_name, index, padding):
    return clean_filename(
        "{}_{}".format(base_name, str(index).zfill(padding)),
        "{}_{}".format(base_name, str(index).zfill(padding)),
    )


def export_object(object_id, path, options):
    rs.UnselectAllObjects()
    rs.SelectObject(object_id)

    command = '-_Export "{}"{}'.format(path, options)
    return rs.Command(command, echo=False)


def main():
    objects = rs.GetObjects("Selecione os objetos para exportar individualmente", preselect=True)
    if not objects:
        print("Nenhum objeto selecionado.")
        return

    template = get_export_template_path()
    if not template:
        print("Exportacao cancelada: arquivo de destino nao escolhido.")
        return
    folder, base_name, extension = template
    padding = 3

    options = get_export_options(extension)
    original_selection = rs.SelectedObjects() or []
    exported = []
    failed = []

    rs.EnableRedraw(False)
    try:
        for index, object_id in enumerate(objects, start=1):
            filename = build_filename(base_name, index, padding)
            path = unique_path(folder, filename, extension)

            if export_object(object_id, path, options):
                clear_hidden_attribute(path)
                exported.append(path)
            else:
                failed.append(object_id)
    finally:
        rs.UnselectAllObjects()
        if original_selection:
            rs.SelectObjects(original_selection)
        rs.EnableRedraw(True)
        sc.doc.Views.Redraw()

    print("Exportacao concluida.")
    print("Arquivos exportados: {}".format(len(exported)))
    if failed:
        print("Falhas: {}".format(len(failed)))
    for path in exported:
        print(path)


if __name__ == "__main__":
    main()
