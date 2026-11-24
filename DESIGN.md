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
- The Camera streams card appears only when a module is sending camera images. Home turns the image stream on while it is open and off when you leave.
- Cameras start blurred with a "Camera hidden" cue. "Show cameras" reveals them. Leaving Home blurs them again and clears freeze.
- Each stream shows its size and frame rate under the frame. Center guides draw a cross over the frame. Freeze frame holds the last image.
- The Modules heading and list are hidden when no modules are installed.

## Settings

- Cards in fixed order: Appearance, Connection, Startup and updates, Developer, About.
- Connection holds send address, receive port and send port. Values commit when the field loses focus or on Enter. A bad value keeps the old setting and shows a red message beside the field. Ports accept 1 to 65535.
- Startup and updates is hidden when neither SteamVR nor update checks are available.
- Developer has one Developer Mode switch in its header. When on, it reveals Logging and Troubleshooting groups. Troubleshooting actions stay disabled until "Enable repair tools" is on.
- About shows the version, links to source, license and privacy policy, and a collapsible contributors list.
- Camera streams no longer live in Settings.
- Row descriptions are one short line. Fields share one width token; the address field is wider.
