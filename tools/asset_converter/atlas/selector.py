"""Shared, deterministic atlas size/page selection; no game content dependencies."""
from __future__ import annotations


def choose_candidate(candidates: list[dict], overhead: float = 0.05,
                     same_page_memory_tolerance: float = 0.02,
                     auto_max_side: int = 2048) -> dict:
    """Pick by measured memory/page cost; never expose size as a runtime type.

    Auto mode first stays within the measured <=2048 pool when it is valid,
    then stays within a small overhead of that pool's minimum decoded area and
    minimizes page count. 4096 remains measured/researchable and is an automatic
    fallback only when smaller sides cannot hold an atomic source block.
    """
    valid = [row for row in candidates if row.get("valid")]
    if not valid:
        raise ValueError("no atlas side can hold the bundle")
    if overhead < 0 or same_page_memory_tolerance < 0 or auto_max_side < 1:
        raise ValueError("candidate policy values must be valid")
    # 4096 remains measured and can be forced for research, but current real
    # lifecycle/render A/B shows no frame/load gain over 2048 and a much larger
    # transient decode workspace. Auto mode therefore uses <=2048 whenever it
    # can hold every atomic source block; 4096 is a correctness fallback.
    preferred = [row for row in valid if int(row["side"]) <= auto_max_side]
    pool = preferred or valid
    minimum = min(row["rgba_bytes"] for row in pool)
    allowed = [row for row in pool if row["rgba_bytes"] <= minimum * (1.0 + overhead)]
    fewest_pages = min(row["pages"] for row in allowed)
    page_candidates = [row for row in allowed if row["pages"] == fewest_pages]
    page_minimum = min(row["rgba_bytes"] for row in page_candidates)
    near_equal = [row for row in page_candidates
                  if row["rgba_bytes"] <= page_minimum * (1.0 + same_page_memory_tolerance)]
    return min(near_equal, key=lambda row: (row["side"], row["rgba_bytes"]))
