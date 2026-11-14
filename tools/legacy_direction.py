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


def map_enemy_direction(facing: int, action_id: int) -> tuple[int, bool]:
    """Map original enemy facing for action indices in the UTF-16LE SWDAH table."""
    if not 1 <= facing <= 8:
        raise ValueError("facing must be in the original 1..8 range")
    if not 0 <= action_id <= 4:
        raise ValueError("enemy action index must be in the original 0..4 range")
    if facing == 2 and action_id in (0, 2):
        return 1, False
    if facing in (4, 8) and action_id == 1:
        return facing, False
    if facing in (4, 5):
        return 3, True
    if facing == 6:
        return (1 if action_id in (0, 2) else 2), True
    if facing in (7, 8):
        return 1, True
    return facing, False
