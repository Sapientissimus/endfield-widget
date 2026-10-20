# Changelog

## 1.2.0

- Style the tray menu to match the widget's operator palette and appearance preset, with vector icons, grouped actions and a version header.
- Display registered global shortcuts in a separate column. Keep native keyboard navigation, Escape dismissal, click-through check marks and disabled launch states.
- Adapt menu spacing to display scaling and use system colors in Windows High Contrast. Preserve all seven actions and all fifteen interface languages.
- Fix ambiguous tray captions when switching between languages that share the same translation for different actions.

## 1.1.0

- Organize Settings with SKPORT account connection directly after language and server selection, followed by game launch. Group the three layout scales together and keep artwork controls in one section.
- Keep Play, calendar, refresh and settings controls visible without requiring hover or keyboard focus, including Play in compact and mini layouts. Only the close control remains hidden until hover or keyboard focus; per-layout Play visibility settings remain available.
- Remove animated transitions and their Settings switch. Layouts, opacity changes, operator artwork and calendar navigation now update immediately.
- Detect installed Endfield copies automatically, including games in additional Steam libraries. Prefer Steam when both Steam and standalone versions are installed.
- Add a game or launcher path field, file picker and return-to-automatic action in Settings, translated into all fifteen interface languages.
- Report missing or invalid paths with a recoverable action. Search runs in the background, and a manual override is preserved between launches.

## 1.0

Initial public release.

- Desktop Sanity display with SKPORT integration.
- Automatic official event, maintenance and debut banner dates.
- Searchable calendar with localized article descriptions.
- Operator artwork and adaptive colors.
- Three independently configurable layouts.
- Desktop placement, position locking, edge snapping and multiple monitor support.
- Steam launch, tray controls and optional Windows startup.
- Fifteen interface languages and local Windows time-zone support.
