# Screenshots for Journeys

Open **Settings → Screenshots** and enable screenshot processing. SrvSurvey
creates verified PNGs for the Journey photo library; it does not change Elite's
screenshot keys or render the game capture itself.

## Game BMP captures

Elite's normal F10 and high-resolution Alt+F10 captures produce BMP files and
`Screenshot` journal events. Desktop shortcut conflicts, game mode restrictions,
and Proton/compositor behavior can affect whether a capture is produced.

Automatic source detection uses the Pictures folder belonging to a standard
configured Windows/Wine/Proton journal profile. It does not search inside the
journal folder for images. When that profile cannot establish a Pictures path,
it falls back to the platform's Pictures location. Windows known-folder support
includes redirected Pictures folders. Choose the BMP source manually when the
automatic folder does not match your installation.

The journal filename is resolved against the selected source, including virtual
or placeholder paths. Late or locked files stay pending and retry without
blocking journal updates, including after restarting SrvSurvey. Each pending
capture retains its original Commander, location and processing choices.

## Optional Steam captures

Enable **Process Steam screenshots** as well as the main processing option to
include Elite's Steam JPEG captures, normally taken with F12. Automatic detection
supports Windows Steam, native Linux Steam and Flatpak Steam installations and
uses Elite's app ID, 359320. A manual Steam source folder is also available.
Steam accounts remain separate; check the detected folder when changing account
or using a nonstandard installation.

Steam captures have no Elite `Screenshot` event. SrvSurvey matches their capture
time to the relevant Commander's journal history, using system, station and body
context known at that time. It does not borrow a later body's name or carry a
body into a different system. Unmatched captures remain untouched until they can
be attributed. Journals covering the capture are needed for this matching.

## Output and optional processing

The **PNG destination** is read-only and shared with Journeys. **Open folder**
creates and opens that location. A previously configured custom destination is
retained; source-folder or journal changes do not move the photo library.

Location labels and original-file removal apply to both sources. Labels are
optional. Originals are removed only after the converted PNG is published and
verified; failures retain the original. Persistent processing receipts prevent
the same capture from being converted repeatedly. Disabling processing pauses
pending work. Faster lossless encoding preserves decoded image quality, though
PNG file sizes can be larger than with slower compression.

Source folders and the PNG destination stay local during portable profile sync.
They can be recovered through an explicit hardware restore from the same
computer's backup. Processing options remain portable.

## Viewing and sharing

In **Navigation → Journeys**, select a visited system and double-click a listed
PNG to open it in the system image viewer. Photographs are organized by system
and body; a return visit can therefore show images from another visit there.
Unprocessed BMPs and Steam JPEGs do not appear in this PNG library.

A completed Journey's **Export as HTML** creates a report folder containing
`index.html` and an `images` subfolder. Share the entire folder. In the browser,
photo tiles open a larger viewer with previous/next controls and arrow-key
navigation; Escape closes it. The report's local image links remain portable.

## When a capture is missing or looks wrong

- Check whether the original BMP or Steam JPEG was created before investigating
  conversion. A game or desktop shortcut problem cannot be repaired by conversion.
- Check the selected source and detected Steam account, enable the relevant
  processing options, and review the conversion status or application log.
- Allow a high-resolution BMP to finish writing. Repeated capture attempts add
  more work; completed conversions are reported individually.
- If a Steam capture has no matching journal context, retain the original and
  make the relevant Commander journals available. Older captures may lack body
  information that the journal did not record at that time.
- Inspect the original image for bands or tiles. Artifacts already in the BMP
  originate during game capture, before SrvSurvey's PNG conversion. High-resolution
  capture while moving under Proton may differ from Windows behavior.
