"""Excel export utilities."""

from __future__ import annotations

from typing import Iterable, Mapping, Sequence

from openpyxl import Workbook

BASIC_HEADERS = ("Nome", "Quantidade", "Propriedades")
DEFAULT_EXTRA_HEADERS = ("Observações", "Preço Unitário")


def export_to_excel(
    rows: Iterable[Mapping[str, object]],
    filepath: str,
    *,
    extra_headers: Sequence[str] = DEFAULT_EXTRA_HEADERS,
    sheet_name: str = "Itens",
) -> str:
    """Export rows to an Excel file with editable columns.

    Each row mapping is expected to include keys for the basic headers.
    Additional headers are created empty so users can edit them.

    Args:
        rows: Iterable of row mappings with values for "Nome", "Quantidade", "Propriedades".
        filepath: Destination path for the Excel file.
        extra_headers: Additional editable column headers.
        sheet_name: Name of the worksheet.

    Returns:
        The path where the workbook was saved.
    """

    workbook = Workbook()
    worksheet = workbook.active
    worksheet.title = sheet_name

    headers = list(BASIC_HEADERS) + list(extra_headers)
    worksheet.append(headers)

    for row in rows:
        values = [row.get(header, "") for header in BASIC_HEADERS]
        values.extend("" for _ in extra_headers)
        worksheet.append(values)

    workbook.save(filepath)
    return filepath
