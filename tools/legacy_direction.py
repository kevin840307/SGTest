"""Facing-to-art lookup observed in the original character draw logic.

Return the source facing and horizontal flip separately. ``action_id`` is the
original numeric draw action; its gameplay meaning is deliberately not guessed.
"""
from __future__ import annotations


def map_character_direction(facing: int, action_id: int) -> tuple[int, bool]:
    if not 1 <= facing <= 8:
        raise ValueError("facing must be in the original 1..8 range")
    if facing in (5, 6, 7):
        return 8 - facing, True
    if facing in (4, 8) and 4 <= action_id <= 17:
        return (3 if facing == 4 else 1), True
    return facing, False
