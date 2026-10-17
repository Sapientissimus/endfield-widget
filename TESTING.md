# Testing

Tests run on isolated profiles with synthetic credentials and saved public official announcements. Normal application data is not used. The default interface is English; language-specific regression cases explicitly select their test language.

## Build and interface checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Catalog-Test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -Desktop
```

Catalog checks validate matching keys across every language, message references in C# and resource references in views. The application suite covers core parsing, interface behavior, all fifteen languages, first-launch defaults and native desktop integration. It renders the widget, settings and calendar for visual inspection. Native checks require an interactive Windows session.

## Reliability checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Audit.ps1
```

This probe compiles the production sources with a separate test entry point. It covers malformed caches and backup recovery, settings drafts, delayed responses, interrupted account connections, language-switch races, calendar recovery, bounded image downloads, shared publication requests and year boundaries. Network failures are injected through fake HTTP handlers.

## Windows startup checks

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Startup-Test.ps1
```

Policy checks use fake stores. Registry checks redirect only the test process's HKCU alias into a temporary subtree outside the real startup locations. A uniquely named native task has its logon trigger disabled, launches an offline child on demand and is removed afterwards. The child verifies the actual working directory and user context. Deleting its registration is checked while the child is still running.

The suites do not use personal tokens, the clipboard, actual game launch or real notifications. They do not change the system clock, time zone or the user's startup entry. Power, network and monitor recovery cases are simulated. Native task execution does not substitute for testing a real sign-out/reboot cycle.

Reports and render images are written to `.test-output`, which is excluded from Git. Generated binaries and private profiles are not repository content.

## Translation maintenance

English is the base catalog. Every supported locale must contain the same message keys. Most keys are English phrases; separate grammatical keys preserve language-specific singular, plural and case forms. Keep placeholders intact and check the rendered layout after changing text. Source article prose remains separate from interface translations.
