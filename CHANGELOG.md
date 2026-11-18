# Changelog

All notable changes are written here. Dates are when the change was made.

## Unreleased

First public draft (2026-11-18). Source only; not run on a retail unit.

- The checks: runtime and extensions, provider flags, display (eyes, refresh rate read only, blend mode, colour check, dimming), microphone inputs and a level read, Camera2 list and frame counts, hands and pinch, light estimation, anchors (create, save, list, load, restart), planes, thermal and battery sampling, and a 10-minute soak of a synthetic skinned figure.
- A first screen before anything runs; a deliberate start (a pinch, click, tap or key) that no timer ever replaces: with no input at all the screen says "No input detected: nothing was run" and waits. In-use banners for camera and microphone; a plain reason before each permission dialog; a declined permission is "denied (your choice)" and skips the rows behind it.
- Soak safety: it does not start and it stops by itself on thermal status 3 or more, a battery temperature of 42 C or more, or a battery under 20 % while not charging; a tap, pinch or key also stops it.
- The device is left as found: the lens dimming level and the blend mode are put back in `finally` blocks, on pause, on quit and on destroy, with a read-back reported. The refresh rate is only read.
- Privacy: only whitelisted device facts; no serial, device name, network name or addresses; anchor ids replaced by a per-run hash; exception text cleaned; an automated test feeds poison values through every check that takes text from the platform and through the permission and eye-check probes; a second test keeps realistic runtime, extension, graphics and build strings from being redacted.
- A failed or empty report write is shown on the panel and in the log ("report could not be written: ..."), with the rows still on the panel; the end screen is a summary the wearer can read off; the text report is also written to the log at the end.
- A clean-up action on the final screen erases the saved test anchor; `allowBackup` is off; no `INTERNET` permission. The app does nothing to disable the unit's camera indicator; whether one lights is unconfirmed.
- The application id is `io.github.thejjones.xrcapabilityprobe`. A build whose owner is the placeholder value `OWNER` is refused unless a test build is asked for.
- Licence: Apache 2.0.
- EditMode tests, a sample report made from simulated data, a link file that keeps the report types under managed stripping, and a build script for a debug-signed APK. The README, `docs/PRIVACY.md`, `docs/SAFETY.md` and `docs/BUILDING.md` describe it; third-party notices list what the build contains.
