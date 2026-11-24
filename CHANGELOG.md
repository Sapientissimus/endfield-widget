# Changelog

## 1.3.1

**Build 16**

- Show the build number in Settings under About.
- Link to the official maintenance notice when the calendar has a preliminary date but cannot read the published schedule.

**Earlier builds**

- Read complete official maintenance announcements formatted as a single paragraph with line breaks, including their localized version names.

- Use the selected official article language for version update names, with English as a fallback when no translation is available.

- Include version launch dates announced in official developer communications before a precise maintenance schedule is available.
- Mark preliminary dates as having no announced time, and replace them with confirmed maintenance for the same version without duplicate calendar entries.

## 1.3.0

- Discover scheduled version-preview livestreams from the official YouTube channel and display their start time in the Windows time zone.
- Display the verified video's own thumbnail on its calendar card. Open the direct video link from the announcement action.
- Localize the stream card caption while retaining its original title in details and search.
- Replace the announcement button's enclosing focus frame with an accessible underline.
- Consolidate intermediate local builds into this public feature release.

## 1.2.5

- Expand the calendar with reruns, weapon banners, sign-in events, Sanity Supply and dated milestones from official announcements.
- Mark starts and ends in the month grid. Resolve maintenance-linked deadlines only when an official time is available.
- Redesign the calendar with an illustrated header, graphite panels and event thumbnails from official articles.
- Preserve localized event names in differently formatted headings and nested quotation marks.

All 1,376 standard application checks passed.

## 1.2.1

- Preserve the localized featured operator name when official announcements use nested quotation marks, including the Argent Flow announcement. The name now follows the selected article language without creating a separate English-name entry for the same debut.
- Add regression coverage for long operator names after banner changes and keep time-zone test fixtures independent of expired event dates.

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
