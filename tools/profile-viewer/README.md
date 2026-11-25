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
- **The columns fit themselves**: each is as wide as its widest cell, over every run and not only those on screen, or
  its header, whichever is wider. A header much wider than its numbers (a piece's time or speed, most of the car's
  numbers) is angled above the table instead, so the numbers set the width. Sort by one of those by clicking the short
  header cell under its slanted name. The differences from the stock car stay at 300 px: hover over them to read all
  of them.
- **A column refits by itself** when a new run needs more room (a longer car name), keeping your scroll and marked
  runs. Drag a column's edge to make it wider or narrower and it keeps that width until you open another sheet or press
  **Fit columns**, which fits every column again. Double-clicking an edge is Dear ImGui's own fit, to the runs on screen
  only.
- **Click a run** to mark it, to keep your place while scrolling sideways.
- **Right-click a run to copy it**: its cells go on the clipboard as one line separated by commas, the columns shown
  in the order shown (a hidden group isn't copied). A cell with a comma or a quote in it is put in quotes, as a CSV
  has it. The status line says what was copied for a few seconds. A right-click doesn't mark the run.
- **New runs appear by themselves**: the sheet is checked twice a second and `cars.csv` with it. F5 re-reads now.

From a command line, for a look or a screenshot: `viewer.py [folder or sheet.csv] [--sheet="All tracks"]
[--groups=Run,Car] [--sort=when:desc --sort=track --sort="time s"] [--headers=angled|wrapped|"one line"]
[--screenshot=shot.png] [--quit-after=seconds] [--no-settings]` (`--sort=` once a column, the first the most
significant; `--headers=` lays the long headers out another way, wrapped onto up to three lines or on one).

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
- **The right-click** is an item handler on each run's first cell, the selectable that spans the row, so it's heard
  anywhere along the run, over the frozen columns or the scrolled ones. DearPyGui's `get_clipboard_text` crashes the
  process when the clipboard holds no text (an image, say), so the viewer only ever sets it.
- **The widths** (`fit_column` in `viewer.py`, tested with a stand-in for the font) are the viewer's own, because Dear
  ImGui's fit only measures the rows it draws, and with the clipper that's the rows on screen. More DearPyGui 2.3
  things found on the way:
  - A column's width is taken only as the table is built (`init_width_or_weight`); `configure_item(width=)` does
    nothing after. So the table is built again to refit it, keeping the scroll (put back two frames later, once the
    new table knows its size), the marked runs and any dragged widths. That takes about 150 ms for All tracks with
    every column.
  - `get_text_size` gives None until the first frame is drawn, so the first table is Dear ImGui's fit, and the first
    frame's tick fits it.
  - A column's `rect_size` (from `get_item_state`) is the room for its cells, the same measure as
    `init_width_or_weight`, which is how a dragged column is told apart.
  - A header needs 27 px beside its label at 18 px: 15 for the sort arrow and 12 for the 2 or 3 after a Shift-click.
    Short of that, Dear ImGui cuts the label to `Ce...`, every line of a wrapped one.
  - Angled headers (`angled_header` with `no_header_label`) are a row of their own above the headers, there whenever
    any column has one, shown or not, so two rows are frozen, not one. None are drawn above the frozen columns, so
    theirs stay flat.
- The columns are grouped by the names `CarSheet.cs` writes (`split 1 s`, `lap 1 s`, `best lap s`, `NN <piece> s`,
  `NN <piece> km/h`, the six summary names); a column it doesn't know goes with the run. If those names change in
  `CarSheet.cs`, change `sheets.py` and its tests with them.

Tests: `python -m pytest tests` (the reader and the window, headless; one test reads his real profiles folder,
if this machine has one, and checks it's left untouched).
