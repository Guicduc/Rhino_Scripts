"""Utilities for collecting and aggregating BOM data from Rhino objects."""
from __future__ import annotations

from collections import defaultdict
from typing import Any, Iterable


DEFAULT_FIELDS = ("name", "type", "layer")


def _get_attr(obj: Any, attr_path: str) -> Any:
    """Safely read an attribute path from an object or dict."""
    current = obj
    for part in attr_path.split("."):
        if current is None:
            return None
        if isinstance(current, dict):
            current = current.get(part)
        else:
            current = getattr(current, part, None)
    return current


def _first_value(obj: Any, candidates: Iterable[str]) -> Any:
    for candidate in candidates:
        value = _get_attr(obj, candidate)
        if value is not None:
            return value
    return None


def extract_object_properties(obj: Any, extra_fields: Iterable[str] | None = None) -> dict[str, Any]:
    """Extract standard properties from a Rhino object.

    The function accepts Rhino object instances or dict-like data.
    """
    data: dict[str, Any] = {
        "name": _first_value(obj, ("Name", "name", "Attributes.Name"))
        or _first_value(obj, ("ObjectName", "Attributes.ObjectName")),
        "type": _first_value(obj, ("ObjectType", "type"))
        or _first_value(obj, ("Geometry.ObjectType", "Geometry.Type")),
        "layer": _first_value(obj, ("Layer", "layer", "Attributes.Layer"))
        or _first_value(obj, ("Attributes.LayerIndex", "LayerIndex")),
    }

    if extra_fields:
        for field in extra_fields:
            if field in data:
                continue
            data[field] = _first_value(obj, (field, field.title(), f"Attributes.{field}"))

    return data


def aggregate_bom(
    objects: Iterable[Any],
    key_fields: Iterable[str] = ("name", "type"),
    extra_fields: Iterable[str] | None = None,
) -> list[dict[str, Any]]:
    """Aggregate Rhino objects into BOM rows grouped by key fields."""
    key_fields = tuple(key_fields)
    extra_fields = tuple(extra_fields or [])

    grouped: dict[tuple[Any, ...], dict[str, Any]] = {}
    extra_values: dict[tuple[Any, ...], dict[str, set[Any]]] = defaultdict(lambda: defaultdict(set))

    for obj in objects:
        data = extract_object_properties(obj, extra_fields=extra_fields)
        key = tuple(data.get(field) for field in key_fields)

        if key not in grouped:
            grouped[key] = {field: data.get(field) for field in key_fields}
            grouped[key]["quantity"] = 0

        grouped[key]["quantity"] += 1

        for field in extra_fields:
            value = data.get(field)
            if value is not None:
                extra_values[key][field].add(value)

    for key, row in grouped.items():
        for field in extra_fields:
            values = extra_values[key].get(field)
            if not values:
                row[field] = None
            elif len(values) == 1:
                row[field] = next(iter(values))
            else:
                row[field] = "; ".join(str(value) for value in sorted(values))

    return list(grouped.values())
