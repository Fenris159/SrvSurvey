# SrvSurvey

SrvSurvey is an overlay-first Elite Dangerous companion. Its desktop surfaces configure the companion and provide tools that supplement, rather than replace, information presented during gameplay.

## Language

**SrvSurvey-XP**:
The user-facing product name displayed beside the logo in the main window.
_Avoid_: SrvSurvey Cross-Platform, SrvSurvey-XP Cross-Platform

**CMDR'S COMPANION**:
The short product-role subtitle displayed beneath SrvSurvey-XP in the main-window brand lockup.
_Avoid_: Overlay Companion, Cross-Platform

**Overlay panel**:
An in-game surface that presents live gameplay context and is the primary feedback surface while playing.
_Avoid_: Main-window panel, dashboard panel

**Hosted overlay window**:
The shared overlay-panel lifecycle that shows, hides, prepares, and places a passive overlay panel from a "should show" intent.
_Avoid_: Overlay coordinator window loop, SynchronizeWindow

**Main window**:
The desktop surface used briefly for configuration, search, and utility workflows outside the primary gameplay feedback loop.
_Avoid_: Gameplay dashboard, commander console

**Overview**:
The main-window landing surface for commander identity, application and journal health, location, and multi-commander controls.
_Avoid_: Gameplay dashboard, recommendation feed

**Functional parity**:
Every existing command, field, status panel, deep link, search provider, copy action, and detached-tool launcher remains available after a presentation change. A visual prototype is never an authoritative inventory of application behavior.
_Avoid_: Simplified feature set, representative controls

**Global overlay settings**:
Cross-category controls for overlay appearance, interaction, and integration that apply beyond one gameplay activity.
_Avoid_: Complete overlay catalog, category overlay settings

**Overlay category shortcut**:
A navigation control that opens a lean, category-filtered overlay configuration window without routing through a complete settings catalog.
_Avoid_: Overlay gear, complete overlay settings

**Keyboard activation**:
A configured shortcut candidate from the desktop hook, the game display, or the Wayland portal, after focus evidence has been sampled.
_Avoid_: Global keyboard hook event, raw key press
