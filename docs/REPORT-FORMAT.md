# The report

What the probe writes, how to get it off the unit, and how to read it.

## Where it is

The app writes two files into its own folder on the device and replaces them after every row, so a run that stops early still leaves a report:

```text
/sdcard/Android/data/io.github.thejjones.xrcapabilityprobe/files/aura-probe-report.txt
/sdcard/Android/data/io.github.thejjones.xrcapabilityprobe/files/aura-probe-report.json
```

If you built a fork with your own `Owner` in `Assets/XrProbe/Editor/ProbeIdentity.cs`, use your own application id in these paths. A soak also leaves `probe-soak-*.json` files beside them.

## Getting it off the unit

Three ways, in order of preference. None has been tried on a retail unit yet; please say which one worked for you.

1. **Pull the files.**

   ```text
   adb pull /sdcard/Android/data/io.github.thejjones.xrcapabilityprobe/files/aura-probe-report.txt
   adb pull /sdcard/Android/data/io.github.thejjones.xrcapabilityprobe/files/aura-probe-report.json
   ```

   This is expected to work. Some Android versions restrict access to an app's external files folder, and Android XR has not been checked.

2. **Read it from the log.** When a run finishes, the app writes the text report to the log one line at a time, each marked `XrProbe: report| `:

   ```text
   adb logcat -d -s Unity > probe-log.txt
   ```

   Keep the lines that contain `XrProbe: report| ` and remove everything up to and including that marker. The same sanitiser that cleans the files cleans these lines.

3. **Read it off the glasses.** The final screen shows a summary of every row and its status.

## If the report could not be written

The panel shows `report could not be written: <reason>` and the same line goes to the log. The rows stay on the panel and are in the log, so use the second or third way above, and please open an issue: a failed write on a real unit is a bug we want to hear about.

## The text file

Made for people. The first lines give the format number, the app version, the start and finish times in UTC, and the device facts. Then one line per row: status, id, name and a short detail. Read this one first, and read it before you share it.

## The JSON file

Made for tools. Format 1:

| Field | Meaning |
|---|---|
| `format` | The format number. This page describes format 1 |
| `app`, `version` | The app's name and version |
| `notice` | Present only in the sample: it says the values are simulated |
| `started_utc`, `finished_utc` | Start and finish of the run, UTC |
| `complete` | `true` if the run reached its end; `false` if it was stopped or cut short |
| `device` | Device facts from a short whitelist: Unity version, platform, device model, operating system string, processor, memory in MB, graphics device, XR loader, and from Android the manufacturer, model, device, product, release, SDK level, security patch date and ABIs. No serial number and no name you gave the device |
| `summary` | How many rows ended in each status |
| `rows` | One object per check, in the order they ran |
| `series` | Measurements over time: `thermal`, `battery`, `light`, `planes`, `soak_frames` |

Each row:

| Field | Meaning |
|---|---|
| `id` | A stable id such as `display.dimming` or `camera.frames.0` |
| `group` | `runtime`, `features`, `display`, `audio`, `camera`, `input`, `scene`, `device` or `soak` |
| `name` | A short name for people |
| `status` | See below |
| `detail` | One or two plain sentences: what was asked and what came back |
| `started_s`, `duration_s` | Seconds from the start of the run, and how long the row took |
| `values` | The row's numbers and lists, each named with its unit where it has one |

## What the statuses mean

| Status | Meaning |
|---|---|
| `pass` | The feature is there and did what the row asked |
| `fail` | The feature is there and did not work |
| `unsupported` | The unit does not offer it. This is an answer, not a problem with the probe |
| `info` | A fact or a measurement with no pass or fail to it |
| `error` | The check itself broke or timed out. Please report these |
| `skipped` | The row was not run, for example because you declined a permission it needs (shown as `denied (your choice)`) |

Rows in the `features` group say "reported by the runtime, not exercised". They record what the runtime claims. The rows with values and series show what the probe actually did.

## The sample

[sample-report.txt](sample-report.txt) and [sample-report.json](sample-report.json) were made from simulated data by the probe's own tests, to show the shape of a report. The first line of each says so. No value in them comes from a real device.
