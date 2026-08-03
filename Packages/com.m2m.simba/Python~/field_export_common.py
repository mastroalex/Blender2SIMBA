from __future__ import annotations
import struct
from dataclasses import dataclass
from pathlib import Path
import numpy as np

@dataclass
class FieldBlock:
    name: str
    units: str
    values: np.ndarray  # (frames, values)


def radius_field(points: np.ndarray) -> np.ndarray:
    """Distanza dall'asse Z originale: sqrt(x^2+y^2), frame per frame."""
    return np.sqrt(points[..., 0] ** 2 + points[..., 1] ** 2).astype(np.float32)


def write_string(f, text: str) -> None:
    raw = text.encode("utf-8")
    f.write(struct.pack("<i", len(raw)))
    f.write(raw)


def sanitize_field(values: np.ndarray, frame_count: int, value_count: int, name: str) -> np.ndarray:
    a = np.asarray(values)
    a = np.squeeze(a)
    if a.ndim == 1:
        if a.size != value_count:
            raise ValueError(f"{name}: attesi {value_count} valori, trovati {a.size}")
        a = np.repeat(a[None, :], frame_count, axis=0)
    elif a.ndim == 2:
        if a.shape == (value_count, frame_count):
            a = a.T
        elif a.shape != (frame_count, value_count):
            raise ValueError(f"{name}: forma {a.shape}, atteso ({frame_count},{value_count})")
    else:
        raise ValueError(f"{name}: campo non scalare, forma {a.shape}")
    a = np.asarray(a, dtype=np.float32)
    if not np.isfinite(a).all():
        finite = a[np.isfinite(a)]
        replacement = float(np.mean(finite)) if finite.size else 0.0
        a = np.nan_to_num(a, nan=replacement, posinf=replacement, neginf=replacement)
    return np.ascontiguousarray(a)


def field_stats(field: FieldBlock):
    values = field.values
    return (
        float(values.min()), float(values.max()),
        values.min(axis=1).astype(np.float32),
        values.max(axis=1).astype(np.float32),
    )
