# Sandrail profiles: the car profile's viewer

A program of its own that shows the car profile's sheets (docs/PROFILE-PLAN.md) and sorts them by any column,
the car's numbers from `cars.csv` included. It only reads, and the game can keep writing while it's open.

## Use

Run `dist\SandrailProfiles.exe` (after `build.cmd`), or `python viewer.py`.

- **The folder** is the game's profiles folder unless you change it: type a path and press Enter, or Browse.
  Your folder, sheet, ticked groups and sort are remembered in `%LOCALAPPDATA%\SandrailProfiles\viewer.json`.
- **The sheets** are down the left, one a track. A track's `(2)` and `(written while open)` files are read with
  it as one sheet, the list saying how many files (a newer game's header starts a `(2)`, as the track column did
  on 2026-11-25). Every row has a `track`, from the file's name for a row older than that column. `cars.csv`
  isn't listed: its numbers are joined onto every run. LibreOffice's `.~lock` files are ignored.
- **All tracks**, at the top of the list, is every track's runs in one table, so they sort by track too. It has
  the columns every track shares (the run, the summary and the car); splits, laps and pieces are a different
  thing on each track, so they stay on the tracks' own sheets. The track stays at the left there.
- **Sort by up to three columns** with the boxes above the table: a column in each, the first the most
  significant, and the arrow beside it for its way (up: smallest, earliest or A first). Clear goes back to the
  order the runs were driven. The sort stays as you go from sheet to sheet; a column a sheet hasn't is passed over,
  and the line at the bottom says so.
- **Or click a header to sort** by it; click again to reverse it, and Shift-click another to sort by that next.
  The boxes follow. Numbers sort as numbers, and blank cells (a piece a run never reached) go last whichever way
  you sort.
- **Show** ticks groups of columns on and off: Run, Laps and splits, Summary, Piece times, Piece speeds, and Car
  (the car's 50 numbers from `cars.csv`, off at first). When, driver, car and caution are always shown and
  stay at the left while you scroll sideways.
- **Click a run** to mark it, to keep your place while scrolling sideways. Hover over the differences from the
  stock car to read all of them.
- **New runs appear by themselves**: the sheet is checked twice a second and `cars.csv` with it. F5 re-reads now.

From a command line, for a look or a screenshot: `viewer.py [folder or sheet.csv] [--sheet="All tracks"]
[--groups=Run,Car] [--sort=when:desc --sort=track --sort="time s"] [--screenshot=shot.png] [--quit-after=seconds]
[--no-settings]` (`--sort=` once a column, the first the most significant).

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
