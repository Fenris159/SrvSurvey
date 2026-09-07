# Rhino HUD regression fixtures

Small HUD/terrain crops from the maintainer's authorized deployment observation on
2026-09-06, 04:51–04:55 UTC. Each gzip file contains two little-endian Int32 dimensions
followed by BGRA pixels. Source frames 10/20 contain one bar, 40/60/80 two bars,
100/120 three bars (including a rig outside the mineral zone), and 140 no visible HUD.
The figures inside the circles are irrelevant to bar detection.

The detector's binary bar-shape mask is derived from frame 20. It retains geometry
only, with no reference RGB values. Other frames are validation examples. Tests also
recolor the captured HUD and select the corresponding picker color to validate alternate palettes.
Bright colored bars are positive examples; grayscale versions must never report
a present bar. Pixel checks cover all six primary/secondary hues and reject
neutral highlights, black and dark saturated colors.

`reported.bgra.gz` is the maintainer's 506x260 screenshot supplied on 2026-09-06
after testing the calibration controls. Only slot 1 has a segmented bar; slots
2–6 contain empty circle rims. It reproduces missed active bars and false rim
matches at the larger HUD size. The screenshot with calibration guides drawn over
the HUD is intentionally not used as detector input.

`reported-live.bgra.gz` is the next clean deployment screenshot supplied by the
maintainer; only slot 1 has a bar. `live-observer.bgra.gz` is a small crop of the
game frame captured during the subsequent live inspection, while the observer
incorrectly displayed BAR for slot 2. Tests include modest calibration differences
and isolated continuous rims to reproduce the false-positive classification.

`after-movement.bgra.gz` is the 400x220 HUD crop captured after the user drove
and deployed rig 2. Both bars 1 and 2 are visible, but the former label matcher
returned all six slots unknown when reusing the earlier live-observer reference.
The color-group regression uses the same geometry without learning any labels.
Sequence tests use frames 20, 40, 60 and 100 to preserve identities across movement,
then remove the first bar's colored pixels to verify that later rigs are not renumbered.
