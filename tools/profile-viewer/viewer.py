"""Sandrail profiles: a viewer, with sorting, for the car profile's sheets.

Point it at the profiles folder, pick a track's sheet, and click any header to sort the runs by it (again to
reverse, Shift-click to sort by another column after it). The car's numbers from ``cars.csv`` are joined onto each
run by its fingerprint, as the Car columns. It reads only, re-reads a sheet when the game adds a row, and never
holds a file: see ``sheets.py``.

On the same stack as Matthew's trellis-gui and PyBindery (DearPyGui), but on DearPyGui's own table rather than the
Trellis grid: a viewer needs sorting, frozen columns and only the rows on screen drawn, which that table has, and
none of an editor's cells, save or undo. Its error lines come from trellis-errors when it's installed.

    python viewer.py [folder or sheet.csv] [--groups=Run,Summary,Car] [--sort=time s[:desc]]
                     [--screenshot=shot.png] [--quit-after=seconds] [--no-settings]
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


class Viewer:
    """The window: the folder, its sheets down the left, the chosen one as a sortable table."""

    FOLDER = "folder"
    LIST = "sheet list"
    HOLDER = "table holder"
    STATUS = "status"
    DIALOG = "folder dialog"

    def __init__(self, folder: Path, sheet: str | None = None, groups: dict[str, bool] | None = None,
                 settings: Path | None = None, sort: list[tuple[str, bool]] | None = None):
        self.folder = Path(folder)
        self.settings = settings
        self.groups = dict(DEFAULT_GROUPS, **(groups or {}))
        self.sheet_files: list[sheets.SheetFile] = []
        self.current: sheets.SheetFile | None = None
        self.table: sheets.Table | None = None
        self.table_id = None
        self.col_ids: list = []
        self.row_ids: list = []
        self.cell_ids: list[list] = []
        self.tip_ids: dict[tuple[int, int], int] = {}
        self.sort_keys: list[tuple[str, bool]] = []  # (column name, descending), most significant first
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
        wanted = next((s for s in self.sheet_files if s.path.name == sheet), None)
        self.open_sheet(wanted or (self.sheet_files[0] if self.sheet_files else None), sort)
        self.save_settings()

    def refresh_list(self) -> bool:
        """Re-list the folder's sheets if its contents changed; says whether they did."""
        files = sheets.list_sheets(self.folder)
        stamp = [(f.path.name, sheets.stamp(f.path) is not None) for f in files]
        if stamp == self._folder_stamp:
            return False
        self._folder_stamp = stamp
        self.sheet_files = files
        dpg.delete_item(self.LIST, children_only=True)
        if not self.folder.is_dir():
            dpg.add_text("No such folder.", parent=self.LIST)
        elif not files:
            dpg.add_text("No sheets here yet.", parent=self.LIST, wrap=230)
        for f in files:
            dpg.add_selectable(label=f.label, parent=self.LIST, user_data=f, tag=f"sheet:{f.path.name}",
                               default_value=self.current is not None and f.path == self.current.path,
                               callback=lambda s, a, u: self.open_sheet(u))
        return True

    # ------------------------------------------------------------------ a sheet

    def open_sheet(self, sheet: sheets.SheetFile | None, sort: list[tuple[str, bool]] | None = None) -> None:
        """Show a sheet, sorted by when (the file's order) unless told otherwise."""
        self.current = sheet
        for f in self.sheet_files:
            dpg.set_value(f"sheet:{f.path.name}", sheet is not None and f.path == sheet.path)
        self.sort_keys = list(sort or [])
        self.table = None
        self._sheet_stamp = None
        self.reload(force=True)
        self.save_settings()

    def _stamp(self):
        if self.current is None:
            return None
        return sheets.stamp(self.current.path), tuple(sheets.stamp(p) for p in sheets.cars_files(self.folder))

    def reload(self, force: bool = False) -> bool:
        """Re-read the sheet and cars.csv if either changed (or if forced); says whether it did."""
        stamp = self._stamp()
        if not force and stamp == self._sheet_stamp:
            return False
        self._sheet_stamp = stamp
        if self.current is None:
            self._clear_table()
            self.status("Pick a sheet on the left." if self.sheet_files else "No sheets in this folder.")
            return True
        try:
            table = sheets.load(self.current.path, self.folder)
        except Exception as exc:  # a file gone or unreadable: say so, keep what's shown
            self.status(self.report(exc, f"Couldn't read {self.current.path.name}"))
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
        # ascending unless a column is marked as the default: so the sort being kept is that default.
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
        """DearPyGui's header clicks: [[column item, 1 or -1], ...], the first the most significant."""
        names = []
        for col_id, direction in specs or []:
            i = dpg.get_item_user_data(col_id)
            names.append((self.table.columns[i].name, direction < 0))
        self.sort_keys = names
        self.sort_rows()

    def sort_by(self, keys: list[tuple[str, bool]]) -> None:
        self.sort_keys = keys
        self.sort_rows()

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
                                        or self.current.path not in [f.path for f in self.sheet_files]):
                # the sheet shown went away, or an empty folder gained its first: show what's there
                self.open_sheet(self.sheet_files[0] if self.sheet_files else None)
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
                bits = [f"{self.current.label}: {len(t.rows)} run{'s' if len(t.rows) != 1 else ''}",
                        f"{len(t.columns) - cars} columns" + (f" and {cars} of the car's" if cars else ""),
                        f"{hidden} hidden" if hidden else "",
                        f"read {self._updated}"]
                text = ", ".join(b for b in bits if b) + ". Click a header to sort, Shift-click for a second."
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
        data = {"folder": str(self.folder), "sheet": self.current.path.name if self.current else None,
                "groups": self.groups}
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
    args = {"path": None, "groups": None, "sort": None, "screenshot": None, "quit_after": None, "settings": True}
    for a in argv:
        if a.startswith("--groups="):
            shown = {g.strip().casefold() for g in a[9:].split(",")}
            args["groups"] = {g: g.casefold() in shown for g in sheets.GROUPS}
        elif a.startswith("--sort="):
            name, _, way = a[7:].partition(":")
            args["sort"] = [(name, way.strip().lower() == "desc")]
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
    sheet = saved.get("sheet") if not args["path"] else None
    groups = args["groups"] or saved.get("groups")
    if args["screenshot"] and args["quit_after"] is None:
        args["quit_after"] = 3.0

    dpg.create_context()
    if FONT.is_file():  # the units' ° and ² aren't in DearPyGui's own font
        with dpg.font_registry():
            dpg.bind_font(dpg.add_font(str(FONT), 18))
    viewer = Viewer(folder, sheet, groups, SETTINGS if args["settings"] else None, args["sort"])

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
