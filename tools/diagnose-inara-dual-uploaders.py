#!/usr/bin/env python3
"""Feedback loop: detect dual Inara uploaders (SrvSurvey + EDMC).

Config-only check. A dormant EDMC install with inara_out=1 is NOT a live
uploader; pair this with a runtime check (tasklist / user confirmation) before
treating dual-config as causal.

Exit 0 = only one uploader configured.
Exit 2 = both configured (config risk only; not proof of concurrent traffic).
Exit 1 = path/config error.

Does not print secrets.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

SRV_UI = Path("/mnt/lr-gamingpc/c/Users/Drew/AppData/Roaming/SrvSurvey/cross-platform-ui.json")
SRV_PROFILE = Path("/mnt/lr-gamingpc/c/Users/Drew/AppData/Roaming/SrvSurvey/cross-platform/F472567-live.json")
EDMC = Path("/mnt/lr-gamingpc/c/Users/Drew/AppData/Local/EDMarketConnector/config.toml")
EDMC_LOCK = Path(
    "/mnt/lr-gamingpc/c/Users/Drew/Saved Games/Frontier Developments/Elite Dangerous/edmc-journal-lock.txt"
)


def main() -> int:
    if not SRV_UI.is_file() or not SRV_PROFILE.is_file():
        print("LOOP_ERROR: SrvSurvey config missing")
        return 1

    ui = json.loads(SRV_UI.read_text(encoding="utf-8"))
    profile = json.loads(SRV_PROFILE.read_text(encoding="utf-8"))
    inara_ui = ui.get("Inara") or {}
    srv_enabled = bool(inara_ui.get("UploadEnabled"))
    srv_key = bool(str(profile.get("inaraApiKey") or "").strip())
    srv_active = srv_enabled and srv_key

    edmc_configured = False
    if EDMC.is_file():
        text = EDMC.read_text(encoding="utf-8", errors="replace")
        out_match = re.search(r"^inara_out\s*=\s*(\d+)", text, re.M)
        key_present = bool(re.search(r"^inara_apikeys\s*=", text, re.M))
        edmc_configured = out_match is not None and out_match.group(1) != "0" and key_present

    lock_present = EDMC_LOCK.is_file()
    print(
        f"srv_inara_active={srv_active} edmc_inara_configured={edmc_configured} "
        f"edmc_journal_lock_present={lock_present}"
    )
    print(
        "NOTE: edmc_inara_configured is config-only; if EDMC is not running it "
        "is not a live dual-uploader."
    )
    if srv_active and edmc_configured:
        print("LOOP_AMBER: dual Inara uploaders configured (runtime unknown)")
        return 2
    print("LOOP_GREEN: at most one Inara uploader configured")
    return 0


if __name__ == "__main__":
    sys.exit(main())
