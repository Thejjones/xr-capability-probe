# Safety

The probe has never been run on a retail unit. Treat the first run as an experiment.

- **Do not run it on a unit you cannot afford to have misbehave.** Sit down, stay still, have the puck charged above about 60 %, and have someone near who can take the glasses off you.
- **The first screen** says what will happen: the lenses will dim and colours will flash for a few seconds; it takes about 5 minutes, then an optional 10-minute soak. Nothing runs until you start it with a pinch, a trackpad click, a tap or a key. **No timer ever starts it.** If no input of any kind has been seen for a minute the screen says "No input detected: nothing was run" and keeps waiting: a wearer who cannot stop a run should not be put through one.
- **What it changes on the unit:** only the lens dimming level and the environment blend mode. Each is put back in a `finally` block, again if the app is paused (glasses off, system menu) or quit, and the report records what was read back. It only reads refresh rates.
- **If the lenses stay dark:** use the glasses' own tint control to clear them, or take them off and close the app. The `display.dimming` row of the report says whether the original level was confirmed restored; the end-of-run panel summary repeats that.
- **The soak** draws a synthetic figure for 10 minutes. These limits are the authors' conservative defaults, not vendor figures, and the reason for any stop is written in the row:

  | Condition | Limit |
  |---|---|
  | Android thermal status | stops (and does not start) at 3 or more |
  | Battery temperature | stops at 42.0 C or more |
  | Battery level | stops under 20 % while not charging |

  A tap, pinch or key stops it early (ignored for the first 3 seconds so the press that started the run does not stop it). A reading that cannot be read is not a reason to stop. Do not leave it running unattended on a unit that is already hot.
- **Camera and microphone** are only touched to count frames and to measure a level; banners say when each is in use. The app does nothing to disable the unit's camera indicator; whether one lights is unconfirmed.
- **After the run**, one trackpad click on the final screen erases the saved test anchor. See [PRIVACY.md](PRIVACY.md).
