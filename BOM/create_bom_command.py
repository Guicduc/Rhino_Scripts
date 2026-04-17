"""CreateBomCommand Rhino entry point.

This module defines a minimal RhinoPython command that prompts the user to
select objects in the document and reports the selection.
"""

from __future__ import annotations


def CreateBomCommand() -> None:
    """Prompt the user to select objects and report the count."""
    try:
        import rhinoscriptsyntax as rs
    except ImportError:
        print("This command must be run inside Rhino's Python environment.")
        return

    objects = rs.GetObjects(
        "Select objects to include in the BOM",
        preselect=True,
    )
    if not objects:
        print("No objects selected.")
        return

    print("Selected {0} object(s).".format(len(objects)))


if __name__ == "__main__":
    CreateBomCommand()
