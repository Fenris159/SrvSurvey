#!/usr/bin/env python3
"""Feedback loop: post-Wille FSDJumps vs SrvSurvey uptime (CDT logs).

Exit 0 = at least one post-Wille FSDJump overlapped a SrvSurvey session
        (Srv was present to track those jumps).
Exit 2 = post-Wille jumps exist but none overlapped SrvSurvey (offline gap).
Exit 1 = usage / path error.

Does not print secrets.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

CDT = timezone(timedelta(hours=-5))


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--journal-dir",
        type=Path,
        required=True,
        help="Elite Dangerous journal directory containing Journal.*.log files",
    )
    parser.add_argument(
        "--log-dir",
        type=Path,
        required=True,
        help="SrvSurvey log directory containing srvs-*.txt files",
    )
    return parser.parse_args(argv)


def parse_journal_ts(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def sessions(logs: Path) -> list[tuple[datetime, datetime, str]]:
    result: list[tuple[datetime, datetime, str]] = []
    for path in sorted(logs.glob("srvs-*.txt")):
        match = re.match(r"srvs-(\d{8})_(\d{6})\.txt", path.name)
        if not match:
            continue
        start_local = datetime.strptime(match.group(1) + match.group(2), "%Y%m%d%H%M%S").replace(
            tzinfo=CDT
        )
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        end_local = None
        for line in reversed(text.splitlines()):
            stamped = re.match(r"^(\d{2}:\d{2}:\d{2}):", line)
            if stamped:
                hour, minute, second = map(int, stamped.group(1).split(":"))
                end_local = start_local.replace(hour=hour, minute=minute, second=second)
                if end_local < start_local:
                    end_local = end_local + timedelta(days=1)
                break
        if end_local is None:
            end_local = datetime.fromtimestamp(path.stat().st_mtime, tz=timezone.utc).astimezone(CDT)
        result.append((start_local.astimezone(timezone.utc), end_local.astimezone(timezone.utc), path.name))
    return result


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    journal = args.journal_dir
    logs = args.log_dir
    if not journal.is_dir() or not logs.is_dir():
        print("LOOP_ERROR: journal or SrvSurvey log path missing on mount")
        return 1

    jumps: list[tuple[datetime, str]] = []
    for path in sorted(journal.glob("Journal.*.log")):
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            try:
                entry = json.loads(line)
            except json.JSONDecodeError:
                continue
            if entry.get("event") == "FSDJump" and entry.get("StarSystem"):
                jumps.append((parse_journal_ts(entry["timestamp"]), entry["StarSystem"]))

    last_wille = max((index for index, item in enumerate(jumps) if item[1] == "Wille"), default=None)
    if last_wille is None:
        print("LOOP_ERROR: no Wille FSDJump found")
        return 1

    after = jumps[last_wille + 1 :]
    window = sessions(logs)
    during = 0
    print(f"last_Wille={jumps[last_wille][0].isoformat()} after_count={len(after)}")
    for ts, system in after:
        hits = [name for start, end, name in window if start <= ts <= end]
        status = "DURING_SRV" if hits else "SRV_OFFLINE"
        during += int(bool(hits))
        print(f"{status} {ts.isoformat()} -> {system} sessions={hits or '-'}")

    print(f"SUMMARY during_srv={during} offline={len(after) - during}")
    if during == 0 and after:
        print("LOOP_RED: no post-Wille FSDJump overlapped SrvSurvey uptime")
        return 2
    print("LOOP_GREEN_UPTIME: SrvSurvey was running for some post-Wille jumps")
    return 0


if __name__ == "__main__":
    sys.exit(main())
