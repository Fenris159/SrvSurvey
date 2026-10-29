# Mining cargo regression fixture

`refinery-cargo-2026-10-28.jsonl.gz` is a sanitized mining/cargo-only extract of the user-supplied `Journal.2026-10-27T221233.01.log` (original SHA-256 `d631be5d93248ead5ea1c9af699299a6bd04eb424b8cd53da630878c900b0b59`). Commander identifiers are synthetic. Credits, personal ship names, account details, unrelated events and unused fields are removed. Event order, second-resolution timestamps, repeated refinery notifications and all cargo quantities are retained.

Expected completed-session tons, in chronological order: **288, 301, 301, 301, 301, 251**. Corresponding refinery carryover after session end: **0, 6, 6, 6, 6, 0**. Total production is **1,743 session tons + 24 carryover tons = 1,767 tons unloaded**.

The old content-hash filter reports **273, 289, 293, 292, 287, 242** session tons. Counting every refinery notification also fails: notifications exceed actual cargo production. Correct reconciliation must exclude limpets, purchases, transfers and other non-mining cargo movements, and remain stable across saving/reopening and history replay.

Focused companion tests cover cargo operations without an exact journal quantity (including limpet synthesis, ship loss, ship changes and mission cargo). Those operations establish a new cargo baseline without changing already confirmed gross yield. Refinery production overlapping that uncertain interval stays labeled as an estimate.
