# Design

## Direction

A Windows-settings look on Avalonia and FluentAvalonia. Calm, dense enough to scan, nothing that needs explaining. Dark is the default, light is supported.

## Tokens

Defined in src/VRCFaceTracking/Styles/Shared.axaml and Styles/Tracking.axaml. Use the resources, never literals.

| Token | Value |
|---|---|
| Font | Inter |
| FontSizePageTitle | 20 |
| FontSizeBody | 14 |
| FontSizeCaption | 12 |
| ControlHeight | 32 |
| RowMinHeight | 40 |
| PageMaxWidth | 1100 |
| CardPadding | 16,14 |
| DisabledOpacity | 0.5 |
| Card radius | 6 |
| Chip and input radius | 4 |

Measured theme colours (dark / light):

| Role | Dark | Light |
|---|---|---|
| Window | #202020 | #F3F3F3 |
| Card fill | #0FFFFFFF | #B3FFFFFF |
| Card stroke | #12FFFFFF | #0F000000 |
| Divider | #15FFFFFF | #0F000000 |
| Text | #FFFFFF | #E4000000 |
| Dim text | #C5FFFFFF | #9E000000 |
| Success chip | #6CCB5F on #393D1B | #0F7B0F on #DFF6DD |
| Critical | #FF99A4 | #C42B1C |

Accent comes from the system accent brush.

## Card anatomy

1. Header: title (14 semibold) and one short line (12, dim) on the left; status chip and master switch on the right.
2. Hairline divider.
3. Rows: label on the left, one control on the right, 40 minimum height, 32 control height.
4. Optional group captions inside a card (12 semibold).
5. "More settings" toggle at the bottom for advanced rows, same left edge as the rows.

Switches align to the card's right padding edge. Disabled dependents dim once; nested dimming never compounds.

## Status chips

| State | Meaning |
|---|---|
| Off | Card switch is off |
| Paused | Master switch is off |
| N of M on | Some toggles in the card are on |
| Learning N% | Calibration is collecting range |
| Ready | Calibration has a learned range |
| Filling in | A fill-in is currently adding motion |
| Waiting for avatar | No avatar parameters yet |

Rows show a small state line only when it is true and useful ("Working now", "Always on", "Last active 2m ago", "Standing by"). A row that has never fired shows nothing negative.

## Page rules

- Title sits inside the shared max-width container.
- The master switch is bound to the mutator's enabled state and labelled "Adjust tracking".
- A live strip compares raw and adjusted values for eight channels, updated on a single UI timer only while the page is visible (about 16 Hz preview, about 2 Hz status).
- Cards appear in a fixed order: Calibration, Smoothing, Eyes, Mouth, Fill-ins, Auto-fixes, Advanced.
- Settings are placed by metadata on each mutation. A setting with no declaration lands in an "Other" group before Advanced.
- Sliders snap to their step, number boxes clamp and reject junk, range sliders keep a minimum gap and never cross.
- Developer-only rows appear only in developer mode, controlled from Settings.
- At 900x600 nothing clips or spills; the page scrolls vertically.

## Home

- Title "Home", then a Tracking card, a Camera streams card, modules, and OSC status.
- The Tracking card has an activity chip (Tracking, No new data, No tracking data, Paused for VRChat face tracking) and one row each for Eyes and Mouth and expression showing the module that supplies the signal.
- Two modules offering the same signal is normal. The second name appears as a neutral line ("Also provided by X"). It never uses error or warning colour.
- Ownership and activity refresh on one UI timer at about 2 Hz, only while Home is visible.
- Under the Tracking card description sits one neutral line, "Detected: Baballonia, Steam Frame". It is hidden when none are running. Detection reads running process names only and never starts anything.
- The Camera streams card appears only when a module is sending camera images. Home turns the image stream on while it is open and off when you leave.
- The card shows one frame per live stream in a wrapping grid, gap 16 on both axes, at most 6 streams. Frame height is 180. Width follows the stream's aspect ratio, clamped to 120..320, default 240.
- Each frame has a title above it (camera-title) and its resolution and rate below it (tracking-row-state).
- Cameras start blurred with a "Camera hidden" cue. The Blur/Show cameras button in the card header toggles it. Leaving Home blurs them again and clears freeze.
- Center guides and Freeze frame are switches below the grid, in the same rows as other cards. Center guides draw a cross over the frame. Freeze frame holds the last image.
- Recenter and Recalibrate sit under a frame, left aligned, wrapping inside the frame width. They only appear when that stream reports the matching control.
- A stream that stops sending for 5 seconds disappears. With no streams the card is hidden.
- Supported pixel formats are Rgba8888, Bgra8888, Gray8 and Jpeg. Bad data shows the cover, never an error.
- The Modules heading and list are hidden when no modules are installed.

## Settings

- Cards in fixed order: Appearance, Connection, Startup and updates, Developer, About.
- Connection holds send address, receive port and send port. Values commit when the field loses focus or on Enter. A bad value keeps the old setting and shows a red message beside the field. Ports accept 1 to 65535.
- After the send port row comes a divider, then "External tracker input" with a switch. Its status line reads "Listening on port N" or "Receiving N packets per second" and is hidden when the switch is off.
- When the switch is on, an "External input port" row appears with the same text field and inline error as the other ports. Out of range uses the standard port message. A port VRChat uses shows "Use a port VRChat does not use."
- External input is off by default. Nothing binds until the switch is on, and it binds loopback only.
- Received values only fill shapes and eye fields that no loaded module drives. A value not refreshed for 1 second is released back to neutral. Fill-ins and the avatar gap report treat an externally driven shape as provided.
- Startup and updates is hidden when neither SteamVR nor update checks are available.
- Developer has one Developer Mode switch in its header. When on, it reveals Logging and Troubleshooting groups. Troubleshooting actions stay disabled until "Enable repair tools" is on.
- About shows the version, links to source, license and privacy policy, and a collapsible contributors list.
- Camera streams no longer live in Settings.
- Row descriptions are one short line. Fields share one width token; the address field is wider.

## Copy

- Sentence case, American English, no em dashes, no exclamation marks, descriptions 10 words or fewer.
