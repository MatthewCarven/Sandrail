# Sandrail profiles: the car profile's viewer

A program of its own that shows the car profile's sheets (docs/PROFILE-PLAN.md) and sorts them by any column,
the car's numbers from `cars.csv` included. It only reads, and the game can keep writing while it's open.

## Use

Run `dist\SandrailProfiles.exe` (after `build.cmd`), or `python viewer.py`.

- **The folder** is the game's profiles folder unless you change it: type a path and press Enter, or Browse.
  Your folder, sheet and ticked groups are remembered in `%LOCALAPPDATA%\SandrailProfiles\viewer.json`.
- **The sheets** are down the left, one a track, `(2)` and `(written while open)` files included. `cars.csv`
  isn't listed: its numbers are joined onto every run. LibreOffice's `.~lock` files are ignored.
- **Click a header to sort** by it; click again to reverse it, and Shift-click another to sort by that next.
  Numbers sort as numbers, and blank cells (a piece a run never reached) go last whichever way you sort.
- **Show** ticks groups of columns on and off: Run, Laps and splits, Summary, Piece times, Piece speeds, and Car
  (the car's 50 numbers from `cars.csv`, off at first). When, driver, car and caution are always shown and
  stay at the left while you scroll sideways.
- **Click a run** to mark it, to keep your place while scrolling sideways. Hover over the differences from the
  stock car to read all of them.
- **New runs appear by themselves**: the sheet is checked twice a second and `cars.csv` with it. F5 re-reads now.

From a command line, for a look or a screenshot: `viewer.py [folder or sheet.csv] [--groups=Run,Car]
[--sort=time s[:desc]] [--screenshot=shot.png] [--quit-after=seconds] [--no-settings]`.

## Build

```
python -m pip install -r requirements.txt
build.cmd
```

`build.cmd` runs the tests and refuses to build if they fail, then makes one file, `dist\SandrailProfiles.exe`,
with PyInstaller. The exe isn't committed. Antivirus sometimes looks twice at a new PyInstaller exe.

## How it's made

- `sheets.py` reads, joins and sorts, with no window: the half the tests lean on. It opens each file just long
  enough to read it whole. Python on Windows shares reading and writing, so the game's append (`CarSheet.Append`,
  which shares reading) is never refused because of the viewer; a test holds a sheet open from Python while .NET
  appends to it.
- `viewer.py` is the window, on **DearPyGui**, the same stack as Matthew's trellis-gui and PyBindery. It uses
  DearPyGui's own table rather than the Trellis grid, since a viewer needs sorting, frozen columns and only the
  rows on screen drawn, which that table has, and none of an editor's cells, save or undo. Trellis's CSV reader
  is left out on purpose too: it keeps `0.50` as text (so trailing zeros survive), and the game writes every
  number with fixed decimals. Errors are put in words by Matthew's `trellis-errors` when it's installed.
- Two DearPyGui 2.3 things worth knowing: a table column is hidden with `enabled=False` (its `show` does nothing),
  and a sortable table reports a sort by its first column on its first frame unless a column is its
  `default_sort`.
- The columns are grouped by the names `CarSheet.cs` writes (`split 1 s`, `lap 1 s`, `best lap s`, `NN <piece> s`,
  `NN <piece> km/h`, the six summary names); a column it doesn't know goes with the run. If those names change in
  `CarSheet.cs`, change `sheets.py` and its tests with them.

Tests: `python -m pytest tests` (the reader and the window, headless; one test reads his real profiles folder,
if this machine has one, and checks it's left untouched).
