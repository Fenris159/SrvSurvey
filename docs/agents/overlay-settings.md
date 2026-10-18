# Overlay settings design rule

When a workspace has an overlay settings window, place its overlay-specific
settings in that window. Keep cross-workspace controls in Global overlay settings.
For a workspace without an overlay settings window, retain its existing settings
location until one is available.

Expose assignment for each workspace overlay shortcut in both places:

- The workspace's overlay settings window, in its Panel availability and shortcuts
  section when available.
- The master Settings → Input shortcut list.

Both editors must use the same binding and saved preference. Changes, validation,
default restoration, and commander profile scope must agree in both locations.
Keep existing settings and shortcut actions available when moving controls.

For new or changed assignment controls, verify that editing from the workspace
updates the master binding and persisted shortcut, and that editing or restoring
defaults from Input updates the workspace control.
