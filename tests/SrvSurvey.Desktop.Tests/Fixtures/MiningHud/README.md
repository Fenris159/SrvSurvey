# Rhino HUD regression fixtures

Small HUD/terrain crops from the maintainer's authorized deployment observation on
2026-09-06, 04:51–04:55 UTC. Each gzip file contains two little-endian Int32 dimensions
followed by BGRA pixels. Source frames 10/20 contain one bar, 40/60/80 two bars,
100/120 three bars (including a rig outside the mineral zone), and 140 no visible HUD.
The figures inside the circles are irrelevant to bar detection.

The detector's binary bar-shape mask is derived from frame 20. It retains geometry
only, with no reference RGB values. Other frames are validation examples. Tests also
recolor the captured HUD to exercise independence from the original green hue.
