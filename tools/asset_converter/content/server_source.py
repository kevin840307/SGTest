"""Resolve the original server Dat root for offline content conversion."""
from pathlib import Path


def resolve_server_dat(value: Path | None,
                       old_root: Path = Path(r"C:\SGO\Recovered\Old")) -> Path:
    if value is not None:
        resolved = value.resolve()
        if not resolved.is_dir():
            raise ValueError(f"server Dat missing: {resolved}")
        return resolved
    if not old_root.is_dir():
        raise ValueError("server Dat was not specified and Recovered/Old is unavailable")
    candidates = sorted(
        (child / "Dat").resolve()
        for child in old_root.iterdir()
        if child.is_dir() and (child / "Dat").is_dir()
    )
    if len(candidates) != 1:
        raise ValueError(
            f"cannot uniquely resolve server Dat under {old_root}; pass --server-dat"
        )
    return candidates[0]
