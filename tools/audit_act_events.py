#!/usr/bin/env python3
"""Audit ACT event tokens over the effective archive+loose client view.

Original ACT bytes stay in the owned local client/archive. Output is metadata
only: virtual paths, action keys, event frame indices, token counts and syntax.
"""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter, defaultdict
from pathlib import Path

if __package__:
    from .inspect_act import parse_act
    from .legacy_group_archive import GroupArchive
else:
    from inspect_act import parse_act
    from legacy_group_archive import GroupArchive

CONTROL_TOKENS = {"+", "-", "$", "#", "*"}
AUDIO_ATOM = re.compile(r"^[A-Za-z0-9_.-]+$")


def normalize_virtual(path: str) -> str:
    return "/".join(part for part in path.replace("\\", "/").split("/") if part)


def split_audio_candidates(token: str) -> list[str]:
    return [part for part in re.split(r"[,\n]", token) if part]


def classify_token(token: str) -> str:
    if token in CONTROL_TOKENS:
        return "control"
    candidates = split_audio_candidates(token)
    if candidates and all(AUDIO_ATOM.fullmatch(part) for part in candidates):
        return "audio_candidate"
    return "unknown"


def collect_effective_sources(client: Path, archive_path: Path | None) -> tuple[dict, dict, set[str]]:
    effective: dict[str, dict] = {}
    audio_files: set[str] = set()
    archive_count = 0
    if archive_path is not None:
        archive = GroupArchive(archive_path)
        for entry in archive.entries:
            virtual = normalize_virtual(entry.path)
            lower = virtual.casefold()
            if lower.startswith("audio/se/") and lower.endswith(".wav"):
                audio_files.add(Path(virtual).stem.casefold())
            if not lower.endswith(".act"):
                continue
            archive_count += 1
            effective[lower] = {
                "path": virtual, "kind": "archive", "raw": archive.read_entry(entry)
            }

    loose_count = 0
    override_count = 0
    for path in sorted(client.rglob("*")):
        if not path.is_file() or path.is_symlink():
            continue
        virtual = normalize_virtual(path.relative_to(client).as_posix())
        lower = virtual.casefold()
        if lower.startswith("audio/se/") and path.suffix.lower() == ".wav":
            audio_files.add(path.stem.casefold())
        if path.suffix.lower() != ".act":
            continue
        loose_count += 1
        key = lower
        if key in effective:
            override_count += 1
        effective[key] = {"path": virtual, "kind": "loose", "raw": path.read_bytes()}

    return effective, {
        "archive_act_count": archive_count,
        "loose_act_count": loose_count,
        "loose_override_count": override_count,
        "effective_act_count": len(effective),
        "effective_audio_se_stem_count": len(audio_files),
    }, audio_files


def audit_sources(sources: dict, source_stats: dict | None = None,
                  audio_files: set[str] | None = None) -> dict:
    token_counts = Counter()
    class_counts = Counter()
    action_counts = Counter()
    audio_atom_counts = Counter()
    source_kind_counts = Counter()
    examples = defaultdict(list)
    failures = []
    total_events = 0
    total_actions = 0

    for key in sorted(sources):
        source = sources[key]
        try:
            actions = parse_act(source["raw"])
        except (ValueError, UnicodeError) as exc:
            failures.append({"path": source["path"], "error": str(exc)})
            continue
        source_kind_counts[source["kind"]] += 1
        total_actions += len(actions)
        for action in actions:
            for event in action["events"]:
                total_events += 1
                token = event["token"]
                category = classify_token(token)
                token_counts[token] += 1
                class_counts[category] += 1
                action_counts[action["key"]] += 1
                if category == "audio_candidate":
                    for atom in split_audio_candidates(token):
                        audio_atom_counts[atom] += 1
                if len(examples[token]) < 5:
                    examples[token].append({
                        "path": source["path"],
                        "source_kind": source["kind"],
                        "action": action["key"],
                        "frame": event["at_frame_raw"],
                    })

    missing_audio_atoms = []
    if audio_files is not None:
        missing_audio_atoms = sorted(atom for atom in audio_atom_counts
                                     if atom.casefold() not in audio_files)

    return {
        "version": 2,
        **(source_stats or {}),
        "parsed_effective_act_count": sum(source_kind_counts.values()),
        "source_kind_counts": dict(sorted(source_kind_counts.items())),
        "action_count": total_actions,
        "event_count": total_events,
        "unique_exact_tokens": len(token_counts),
        "classification_counts": dict(sorted(class_counts.items())),
        "control_token_counts": {token: token_counts[token]
                                 for token in sorted(CONTROL_TOKENS)
                                 if token_counts[token]},
        "action_event_counts": dict(sorted(action_counts.items())),
        "exact_token_counts": dict(sorted(token_counts.items(), key=lambda row: (-row[1], row[0]))),
        "audio_atom_counts": dict(sorted(audio_atom_counts.items(), key=lambda row: (-row[1], row[0]))),
        "missing_audio_atoms": missing_audio_atoms,
        "unknown_tokens": sorted(token for token in token_counts
                                 if classify_token(token) == "unknown"),
        "examples": {token: examples[token] for token in sorted(examples)},
        "failures": failures,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    client = args.client.resolve()
    if not client.is_dir():
        parser.error("client directory does not exist")
    if args.archive is not None and not args.archive.is_file():
        parser.error("archive does not exist")
    sources, stats, audio_files = collect_effective_sources(client, args.archive)
    report = audit_sources(sources, stats, audio_files)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
    print(json.dumps({
        "archive_act_count": report.get("archive_act_count", 0),
        "loose_act_count": report.get("loose_act_count", 0),
        "loose_override_count": report.get("loose_override_count", 0),
        "effective_act_count": report.get("effective_act_count", 0),
        "parsed_effective_act_count": report["parsed_effective_act_count"],
        "action_count": report["action_count"],
        "event_count": report["event_count"],
        "unique_exact_tokens": report["unique_exact_tokens"],
        "classification_counts": report["classification_counts"],
        "control_token_counts": report["control_token_counts"],
        "unknown_tokens": report["unknown_tokens"],
        "missing_audio_atoms": report["missing_audio_atoms"],
        "failures": report["failures"][:10],
    }, ensure_ascii=False, indent=2))
    return 1 if report["failures"] or report["unknown_tokens"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
