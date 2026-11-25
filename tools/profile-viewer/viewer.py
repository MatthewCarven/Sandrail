"""Sandrail profiles: a viewer, with sorting, for the car profile's sheets.

Point it at the profiles folder, pick a track's sheet (its files read as one) or All tracks, and sort the runs by up to
three columns with the boxes above the table, or by clicking a header (again to reverse, Shift-click to sort by
another column after it). The car's numbers from ``cars.csv`` are joined onto each run by its fingerprint, as the
Car columns. It reads only, re-reads a sheet when the game adds a row, and never holds a file: see ``sheets.py``.

On the same stack as Matthew's trellis-gui and PyBindery (DearPyGui), but on DearPyGui's own table rather than the
Trellis grid: a viewer needs sorting, frozen columns and only the rows on screen drawn, which that table has, and
none of an editor's cells, save or undo. Its error lines come from trellis-errors when it's installed.

    python viewer.py [folder or sheet.csv] [--sheet=All tracks] [--groups=Run,Summary,Car]
                     [--sort=when:desc --sort=track --sort=time s] [--screenshot=shot.png] [--quit-after=seconds]
                     [--no-settings]
"""

from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path

import dearpygui.dearpygui as dpg

import sheets

try:  # Matthew's error reporter, shared with trellis-gui; the viewer works without it.
    from trellis_errors import describe_error
except ImportError:  # pragma: no cover - depends on the machine
    describe_error = None

TITLE = "Sandrail profiles"
SETTINGS = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "SandrailProfiles" / "viewer.json"
FONT = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "segoeui.ttf"

#: How often the open sheet (and cars.csv) is checked for new rows, and the folder for new sheets, in seconds.
SHEET_POLL = 0.5
FOLDER_POLL = 2.0

#: The one long column, cut to this width with the whole text on hover.
LONG_COLUMNS = {"differences from the stock car": 300}

DEFAULT_GROUPS = {g: g != sheets.CAR for g in sheets.GROUPS}

#: How many columns the boxes above the table sort by, the first the most significant (his ask, 2026-11-25).
SORT_KEYS = 3
NO_COLUMN = "(none)"
UP, DOWN = "\u2191", "\u2193"  # Segoe UI has them; DearPyGui 2.3 loads a font's characters as they're drawn


class Viewer:
    """The window: the folder, its sheets down the left, the chosen one as a sortable table."""

    FOLDER = "folder"
    LIST = "sheet list"
    HOLDER = "table holder"
    STATUS = "status"
    DIALOG = "folder dialog"
    SORT_COLUMN = "sort column {}"
    SORT_WAY = "sort way {}"

    def __init__(self, folder: Path, sheet: str | None = None, groups: dict[str, bool] | None = None,
                 settings: Path | None = None, sort: list[tuple[str, bool]] | None = None):
        self.folder = Path(folder)
        self.settings = settings
        self.groups = dict(DEFAULT_GROUPS, **(groups or {}))
        self.entries: list[sheets.TrackSheet] = []
        self.current: sheets.TrackSheet | None = None
        self.table: sheets.Table | None = None
        self.table_id = None
        self.col_ids: list = []
        self.row_ids: list = []
        self.cell_ids: list[list] = []
        self.tip_ids: dict[tuple[int, int], int] = {}
        self.sort_keys: list[tuple[str, bool]] = list(sort or [])[:SORT_KEYS]  # (column, descending), first first
        self._ways = [False] * SORT_KEYS  # each box's way, descending or not, kept while its column is (none)
        self._fresh_table = False
        self._sheet_stamp = None
        self._folder_stamp = None
        self._next_sheet_poll = 0.0
        self._next_folder_poll = 0.0
        self._updated = ""
        self._build()
        self.set_folder(self.folder, sheet, sort)

    # ------------------------------------------------------------------ building

    def _build(self) -> None:
        with dpg.window(tag="main"):
            with dpg.group(horizontal=True):
                dpg.add_text("Folder")
                dpg.add_input_text(tag=self.FOLDER, default_value=str(self.folder), width=-230, on_enter=True,
                                   callback=lambda s, a: self.set_folder(Path(a.strip().strip('"'))))
                dpg.add_button(label="Browse...", callback=self._browse)
                dpg.add_button(label="Reload (F5)", callback=lambda: self.reload(force=True))
            with dpg.group(horizontal=True):
                with dpg.child_window(tag=self.LIST, width=250, height=-26):
                    pass
                with dpg.group():
                    with dpg.group(horizontal=True):
                        dpg.add_text("Show")
                        for g in sheets.GROUPS:
                            dpg.add_checkbox(label=g, default_value=self.groups[g], user_data=g,
                                             callback=lambda s, a, u: self.show_group(u, a))
                    with dpg.group(horizontal=True):
                        for k in range(SORT_KEYS):
                            dpg.add_text("Sort by" if k == 0 else "then")
                            dpg.add_combo([NO_COLUMN], tag=self.SORT_COLUMN.format(k), default_value=NO_COLUMN,
                                          width=230, height_mode=dpg.mvComboHeight_Largest,
                                          callback=lambda s, a: self._sort_from_boxes())
                            dpg.add_button(label=UP, tag=self.SORT_WAY.format(k), width=30, user_data=k,
                                           callback=lambda s, a, u: self._turn_way(u))
                            with dpg.tooltip(self.SORT_WAY.format(k)):
                                dpg.add_text("Up: smallest, earliest or A first. Down: largest, latest or Z first.")
                        dpg.add_button(label="Clear", callback=lambda: self.set_sort([]))
                    with dpg.group(tag=self.HOLDER):
                        pass
            dpg.add_text(tag=self.STATUS)

        with dpg.file_dialog(tag=self.DIALOG, directory_selector=True, show=False, modal=True, width=720,
                             height=460, callback=lambda s, a: self.set_folder(Path(a["file_path_name"]))):
            pass

        with dpg.handler_registry():
            dpg.add_key_press_handler(dpg.mvKey_F5, callback=lambda: self.reload(force=True))

    def _browse(self) -> None:
        start = self.folder if self.folder.is_dir() else Path.home()
        dpg.configure_item(self.DIALOG, default_path=str(start))
        dpg.show_item(self.DIALOG)

    # ------------------------------------------------------------------ the folder

    def set_folder(self, folder: Path, sheet: str | None = None, sort: list[tuple[str, bool]] | None = None) -> None:
        """Look in another folder; a sheet's own path picks its folder and it."""
        if folder.suffix.lower() == ".csv" and not folder.is_dir():
            folder, sheet = folder.parent, folder.name
        self.folder = folder
        dpg.set_value(self.FOLDER, str(folder))
        self._folder_stamp = None
        self.refresh_list()
        wanted = next((e for e in self.entries if sheet and e.holds(sheet)), None)
        self.open_sheet(wanted or self.first_track(), sort)
        self.save_settings()

    def first_track(self) -> sheets.TrackSheet | None:
        """The first track's own sheet, or All tracks if that's all there is (it never is: it needs a track)."""
        return next((e for e in self.entries if not e.everything), self.entries[0] if self.entries else None)

    def refresh_list(self) -> bool:
        """Re-list the folder's sheets if its contents changed; says whether they did."""
        entries = sheets.list_tracks(self.folder)
        stamp = [(e.key, tuple(p.name for p in e.paths)) for e in entries]
        if stamp == self._folder_stamp:
            return False
        self._folder_stamp = stamp
        self.entries = entries
        if self.current is not None:  # the same entry, with any file that's come since (a (2), say)
            self.current = next((e for e in entries if e.key == self.current.key), self.current)
        dpg.delete_item(self.LIST, children_only=True)
        if not self.folder.is_dir():
            dpg.add_text("No such folder.", parent=self.LIST)
        elif not entries:
            dpg.add_text("No sheets here yet.", parent=self.LIST, wrap=230)
        for e in entries:
            dpg.add_selectable(label=e.label, parent=self.LIST, user_data=e, tag=f"sheet:{e.key}",
                               default_value=self.current is not None and e.key == self.current.key,
                               callback=lambda s, a, u: self.open_sheet(u))
            if e.everything:
                dpg.add_separator(parent=self.LIST)
        return True

    # ------------------------------------------------------------------ a sheet

    def open_sheet(self, sheet: sheets.TrackSheet | None, sort: list[tuple[str, bool]] | None = None) -> None:
        """Show an entry, sorted as the last one was (a column it hasn't is passed over) unless told otherwise."""
        self.current = sheet
        for e in self.entries:
            dpg.set_value(f"sheet:{e.key}", sheet is not None and e.key == sheet.key)
        if sort is not None:
            self.sort_keys = list(sort)[:SORT_KEYS]
        self.table = None
        self._sheet_stamp = None
        self.reload(force=True)
        self.save_settings()

    def _stamp(self):
        if self.current is None:
            return None
        return (tuple(sheets.stamp(p) for p in self.current.paths),
                tuple(sheets.stamp(p) for p in sheets.cars_files(self.folder)))

    def reload(self, force: bool = False) -> bool:
        """Re-read the sheet and cars.csv if either changed (or if forced); says whether it did."""
        stamp = self._stamp()
        if not force and stamp == self._sheet_stamp:
            return False
        self._sheet_stamp = stamp
        if self.current is None:
            self._clear_table()
            self.status("Pick a sheet on the left." if self.entries else "No sheets in this folder.")
            return True
        try:
            table = sheets.load(self.current, self.folder)
        except Exception as exc:  # a file gone or unreadable: say so, keep what's shown
            self.status(self.report(exc, f"Couldn't read {self.current.label}"))
            return True
        old = self.table
        if old is not None and [c.name for c in table.columns] == [c.name for c in old.columns] \
                and len(table.rows) >= len(old.rows):
            self._update_rows(table)
        else:
            self._build_table(table)
        self.sort_rows()
        self._updated = time.strftime("%H:%M:%S")
        self.status()
        return True

    def _clear_table(self) -> None:
        dpg.delete_item(self.HOLDER, children_only=True)
        self.table, self.table_id = None, None
        self.col_ids, self.row_ids, self.cell_ids, self.tip_ids = [], [], [], {}

    def _build_table(self, table: sheets.Table) -> None:
        self._clear_table()
        self.table = table
        for k in range(SORT_KEYS):
            dpg.configure_item(self.SORT_COLUMN.format(k), items=[NO_COLUMN] + [c.name for c in table.columns])
        self._show_sort()
        if not table.columns:
            dpg.add_text("This sheet is empty.", parent=self.HOLDER)
            return
        self.table_id = dpg.add_table(
            parent=self.HOLDER, header_row=True, sortable=True, sort_multi=True, sort_tristate=False,
            callback=lambda s, a: self._on_sort(a), resizable=True, hideable=False, reorderable=False,
            policy=dpg.mvTable_SizingFixedFit, scrollX=True, scrollY=True, freeze_rows=1,
            freeze_columns=table.pinned, row_background=True, borders_innerV=True, borders_outerV=True,
            borders_outerH=True, clipper=True, height=-26, width=-1)
        # Dear ImGui always sorts by something, and on a new table's first frame it reports the first column
        # ascending unless a column is marked as the default: so the sort's first column is that default, its
        # arrow in the header, and the report it makes then is passed over (_on_sort), keeping the other two.
        self._fresh_table = True
        primary, descending = self.sort_keys[0] if self.sort_keys else (None, False)
        for i, col in enumerate(table.columns):
            long = LONG_COLUMNS.get(col.name)
            first = col.name == primary
            self.col_ids.append(dpg.add_table_column(
                label=col.name, parent=self.table_id, user_data=i, enabled=self._shown(i),
                width_fixed=long is not None, init_width_or_weight=long or 0,
                default_sort=first, prefer_sort_descending=first and descending,
                prefer_sort_ascending=not (first and descending)))
        for row in table.rows:
            self._add_row(row)

    def _add_row(self, row: list[str]) -> None:
        r = len(self.row_ids)
        with dpg.table_row(parent=self.table_id, user_data=r) as row_id:
            cells = []
            for c, text in enumerate(row):
                if c == 0:  # click a run to mark it, to keep your place scrolling sideways
                    cells.append(dpg.add_selectable(label=text, span_columns=True))
                else:
                    cells.append(dpg.add_text(text))
                    if self.table.columns[c].name in LONG_COLUMNS:
                        with dpg.tooltip(cells[-1]):
                            self.tip_ids[(r, c)] = dpg.add_text(text, wrap=560)
        self.row_ids.append(row_id)
        self.cell_ids.append(cells)

    def _update_rows(self, table: sheets.Table) -> None:
        """The same columns: change the cells that changed (a car newly in cars.csv) and add the new runs."""
        old, self.table = self.table, table
        for r, (before, after) in enumerate(zip(old.rows, table.rows)):
            for c, (a, b) in enumerate(zip(before, after)):
                if a != b:
                    self._set_cell(r, c, b)
        for row in table.rows[len(old.rows):]:
            self._add_row(row)

    def _set_cell(self, r: int, c: int, text: str) -> None:
        item = self.cell_ids[r][c]
        if c == 0:
            dpg.configure_item(item, label=text)
        else:
            dpg.set_value(item, text)
        if (r, c) in self.tip_ids:
            dpg.set_value(self.tip_ids[(r, c)], text)

    # ------------------------------------------------------------------ sorting and showing

    def _on_sort(self, specs) -> None:
        """DearPyGui's header clicks: [[column item, 1 or -1], ...], the first the most significant. The first report
        from a table just built is Dear ImGui's own (see _build_table), not a click: the sort's first column, or the
        table's first column if the sort has none here. That one is passed over, keeping the sort's other columns."""
        names = []
        for col_id, direction in specs or []:
            i = dpg.get_item_user_data(col_id)
            names.append((self.table.columns[i].name, direction < 0))
        if self._fresh_table:
            self._fresh_table = False
            here = [c.name for c in self.table.columns]
            built = self.sort_keys[:1] if self.sort_keys and self.sort_keys[0][0] in here else [(here[0], False)]
            if names == built:
                self.sort_rows()
                return
        self.sort_keys = names[:SORT_KEYS]
        self._show_sort()
        self.sort_rows()
        self.save_settings()

    def set_sort(self, keys: list[tuple[str, bool]]) -> None:
        """Sorts by these columns, the first the most significant; none is the file's order. A new first column builds
        the table again, so its header shows the arrow."""
        old = self.sort_keys[:1]
        self.sort_keys = list(keys)[:SORT_KEYS]
        self._show_sort()
        if self.table is not None and self.table_id is not None and self.sort_keys[:1] != old:
            self._build_table(self.table)
        self.sort_rows()
        self.save_settings()

    sort_by = set_sort

    def _sort_from_boxes(self) -> None:
        """The boxes' columns as the sort, in order, a box left at (none) or naming a column twice passed over."""
        keys = []
        for k in range(SORT_KEYS):
            name = dpg.get_value(self.SORT_COLUMN.format(k))
            if name and name != NO_COLUMN and name not in [n for n, _ in keys]:
                keys.append((name, self._ways[k]))
        self.set_sort(keys)

    def _turn_way(self, k: int) -> None:
        self._ways[k] = not self._ways[k]
        self._sort_from_boxes()

    def _show_sort(self) -> None:
        """The boxes as the sort is: a column and its way each, then (none)."""
        for k in range(SORT_KEYS):
            if k < len(self.sort_keys):
                name, self._ways[k] = self.sort_keys[k]
            else:
                name = NO_COLUMN
            dpg.set_value(self.SORT_COLUMN.format(k), name)
            dpg.configure_item(self.SORT_WAY.format(k), label=DOWN if self._ways[k] else UP)

    def sort_rows(self) -> None:
        if self.table is None or self.table_id is None:
            return
        names = [c.name for c in self.table.columns]
        keys = [(names.index(n), d) for n, d in self.sort_keys if n in names]
        order = sheets.sort_order(self.table, keys)
        dpg.reorder_items(self.table_id, 1, [self.row_ids[i] for i in order])

    def row_order(self) -> list[int]:
        """The file's row index of each row as shown, top to bottom."""
        return [dpg.get_item_user_data(r) for r in dpg.get_item_children(self.table_id, 1)]

    def _shown(self, i: int) -> bool:
        return i < self.table.pinned or self.groups.get(self.table.columns[i].group, True)

    def show_group(self, group: str, shown: bool) -> None:
        self.groups[group] = shown
        if self.table is not None:
            for i, col_id in enumerate(self.col_ids):  # a column's show does nothing in DearPyGui 2.3; enabled hides it
                dpg.configure_item(col_id, enabled=self._shown(i))
        self.save_settings()
        self.status()

    # ------------------------------------------------------------------ polling, status, settings

    def tick(self, now: float) -> None:
        """Called every frame: notice new sheets and new rows."""
        if now >= self._next_folder_poll:
            self._next_folder_poll = now + FOLDER_POLL
            if self.refresh_list() and (self.current is None
                                        or self.current.key not in [e.key for e in self.entries]):
                # the sheet shown went away, or an empty folder gained its first: show what's there
                self.open_sheet(self.first_track())
        if now >= self._next_sheet_poll:
            self._next_sheet_poll = now + SHEET_POLL
            self.reload()

    def status(self, text: str | None = None) -> None:
        if text is None:
            t = self.table
            if self.current is None or t is None:
                text = ""
            else:
                cars = sum(1 for c in t.columns if c.group == sheets.CAR)
                hidden = sum(1 for i in range(len(t.columns)) if not self._shown(i))
                tracks = sum(1 for e in self.entries if not e.everything)
                where = f"All tracks ({tracks})" if self.current.everything else self.current.label
                bits = [f"{where}: {len(t.rows)} run{'s' if len(t.rows) != 1 else ''}",
                        f"{len(t.columns) - cars} columns" + (f" and {cars} of the car's" if cars else ""),
                        f"{hidden} hidden" if hidden else "",
                        f"read {self._updated}"]
                missing = [n for n, _ in self.sort_keys if n not in [c.name for c in t.columns]]
                if missing:
                    bits.append(f"not sorted by {', '.join(missing)}: this sheet hasn't {'it' if len(missing) == 1 else 'them'}")
                text = ", ".join(b for b in bits if b) + ". Sort with the boxes above, or click a header (Shift-click adds the next)."
                text += "".join(" " + n for n in t.notes)
        dpg.set_value(self.STATUS, text)

    def report(self, exc: BaseException, action: str) -> str:
        """A short line for the status bar (and the whole report on stderr), the way trellis-gui does it."""
        reason = str(exc) or type(exc).__name__
        if describe_error is not None:
            try:
                d = describe_error(exc).to_dict()
                spec = d.get("type_specific") or {}
                reason = spec.get("strerror") or d.get("message") or reason
                print(describe_error(exc), file=sys.stderr)
            except Exception:
                pass
        return f"{action}: {reason}"

    def save_settings(self) -> None:
        if self.settings is None:
            return
        data = {"folder": str(self.folder), "sheet": self.current.key if self.current else None,
                "groups": self.groups, "sort": [[n, d] for n, d in self.sort_keys]}
        try:
            self.settings.parent.mkdir(parents=True, exist_ok=True)
            self.settings.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError:
            pass


def load_settings(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def parse_args(argv: list[str]) -> dict:
    args = {"path": None, "sheet": None, "groups": None, "sort": None, "screenshot": None, "quit_after": None,
            "settings": True}
    for a in argv:
        if a.startswith("--groups="):
            shown = {g.strip().casefold() for g in a[9:].split(",")}
            args["groups"] = {g: g.casefold() in shown for g in sheets.GROUPS}
        elif a.startswith("--sort="):  # once a column, the first the most significant
            name, _, way = a[7:].partition(":")
            args["sort"] = (args["sort"] or []) + [(name, way.strip().lower() == "desc")]
        elif a.startswith("--sheet="):
            args["sheet"] = a[8:]
        elif a.startswith("--screenshot="):
            args["screenshot"] = a[13:]
        elif a.startswith("--quit-after="):
            args["quit_after"] = float(a[13:])
        elif a == "--no-settings":
            args["settings"] = False
        elif not a.startswith("--"):
            args["path"] = a
    return args


def main(argv: list[str] | None = None) -> None:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    saved = load_settings(SETTINGS) if args["settings"] else {}
    folder = Path(args["path"] or saved.get("folder") or sheets.DEFAULT_FOLDER)
    sheet = args["sheet"] or (saved.get("sheet") if not args["path"] else None)
    groups = args["groups"] or saved.get("groups")
    sort = args["sort"] or [(str(n), bool(d)) for n, d in saved.get("sort") or []] or None
    if args["screenshot"] and args["quit_after"] is None:
        args["quit_after"] = 3.0

    dpg.create_context()
    if FONT.is_file():  # the units' ° and ² and the sort boxes' arrows aren't in DearPyGui's own font
        with dpg.font_registry():
            dpg.bind_font(dpg.add_font(str(FONT), 18))
    viewer = Viewer(folder, sheet, groups, SETTINGS if args["settings"] else None, sort)

    dpg.create_viewport(title=TITLE, width=1500, height=860)
    dpg.setup_dearpygui()
    dpg.show_viewport()
    dpg.set_primary_window("main", True)

    started = time.monotonic()
    shot_at = None
    while dpg.is_dearpygui_running():
        now = time.monotonic()
        viewer.tick(now)
        if args["quit_after"] is not None and now - started >= args["quit_after"]:
            if args["screenshot"] and shot_at is None:
                dpg.output_frame_buffer(file=args["screenshot"])
                shot_at = dpg.get_frame_count()
            elif shot_at is None or dpg.get_frame_count() > shot_at + 5:
                break
        dpg.render_dearpygui_frame()
    dpg.destroy_context()


if __name__ == "__main__":
    main()
