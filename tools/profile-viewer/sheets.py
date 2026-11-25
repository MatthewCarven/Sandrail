"""Sandrail's car-profile sheets, read for looking at.

No window and no writing: this half lists a profiles folder, reads a track's sheet and ``cars.csv``, joins
the car's numbers onto each run by its fingerprint, and sorts. ``viewer.py`` draws it.

The files are written by ``Sandrail.Core/Profiles/CarSheet.cs`` (docs/PROFILE-PLAN.md, "The model"): UTF-8 with
a BOM, CRLF line ends, commas, decimal points in any culture, a cell with a comma, quote or line end quoted with
its quotes doubled, and a blank cell for anything not reached or not applicable. One sheet a track,
``<track> <8-hex fingerprint>.csv``, one row a run, newest at the bottom; ``cars.csv`` one row a car.

**Read-only, and never in the game's way.** A file is opened, read whole and closed at once. Python opens files
on Windows sharing both reading and writing, so the game's ``File.AppendAllText`` (which shares reading) never
finds a sheet held by the viewer, and nothing ever lands in ``(written while open)`` because of it.
"""

from __future__ import annotations

import csv
import io
import os
import re
from dataclasses import dataclass, field
from pathlib import Path

#: Where the game writes sheets unless ``--profiles=`` says otherwise.
DEFAULT_FOLDER = Path(os.environ.get("APPDATA", str(Path.home()))) / "Godot" / "app_userdata" / "Sandrail" / "profiles"

#: ``cars.csv``, and ``cars (2).csv`` and on if a newer game changed its header.
_CARS_NAME = re.compile(r"^cars( \(\d+\))?\.csv$", re.IGNORECASE)

#: ``Seed 5 0070ecd8.csv``, ``4 847ed7b9 (2).csv``, ``Shakedown 8a6323aa (written while open).csv``.
_SHEET_NAME = re.compile(r"^(?P<track>.*?) (?P<print>[0-9a-f]{8})(?P<notes>(?: \((?:\d+|written while open)\))*)\.csv$")

# The column groups, shown and hidden together.
RUN = "Run"
LAPS = "Laps and splits"
SUMMARY = "Summary"
PIECE_TIMES = "Piece times"
PIECE_SPEEDS = "Piece speeds"
CAR = "Car"
GROUPS = (RUN, LAPS, SUMMARY, PIECE_TIMES, PIECE_SPEEDS, CAR)

_SUMMARY_NAMES = {"top km/h", "off road s", "worst slide deg", "spins", "respawns", "furthest m"}
_LAP_NAME = re.compile(r"^(?:split \d+|lap \d+|best lap) s$")
_PIECE_TIME = re.compile(r"^\d{2,} \S.* s$")
_PIECE_SPEED = re.compile(r"^\d{2,} \S.* km/h$")

#: Kept at the left while the rest scrolls sideways, in this order, when the sheet has them.
PINNED = ("when", "driver", "car", "caution")

#: The column every row has: the track's name, written by the game since 2026-11-25 and taken from the file's name
#: for an older row.
TRACK = "track"

#: The list's first entry, every track's runs in one table (2026-11-25), so they sort by track too.
ALL_TRACKS = "All tracks"

#: All tracks keeps these columns, and the track at the left with the rest: a split, a lap or a piece is a different
#: thing on each track, so those columns stay on the tracks' own sheets.
_SHARED_GROUPS = (RUN, SUMMARY)
PINNED_ALL = ("when", TRACK, "driver", "car", "caution")

#: The sheet's column holding the car's fingerprint, and cars.csv's.
SHEET_KEY = "car fingerprint"
CARS_KEY = "fingerprint"

#: A cell that sorts as a number. Plain decimals only: the game never writes a thousands separator, and a car's
#: fingerprint like ``4f918acd`` must stay text.
_NUMBER = re.compile(r"^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$")

#: More columns than this and Dear ImGui's table refuses; nothing the game writes comes near it.
MAX_COLUMNS = 500


@dataclass(frozen=True)
class SheetFile:
    """One track's sheet in the folder."""

    path: Path
    track: str
    fingerprint: str
    notes: tuple[str, ...] = ()

    @property
    def label(self) -> str:
        """``4  847ed7b9  (2)``: how the list names it."""
        bits = [self.track]
        if self.fingerprint:
            bits.append(self.fingerprint)
        bits.extend(f"({n})" for n in self.notes)
        return "  ".join(bits)


@dataclass
class Column:
    """A column of the table: its header, its group, and whether every filled cell in it is a number."""

    name: str
    group: str
    numeric: bool = False


@dataclass
class Table:
    """A sheet's runs with each car's numbers joined on: columns, and rows of text cells lined up with them."""

    columns: list[Column]
    rows: list[list[str]]
    pinned: int = 0
    notes: list[str] = field(default_factory=list)

    def index(self, name: str) -> int:
        return next(i for i, c in enumerate(self.columns) if c.name == name)


@dataclass(frozen=True)
class TrackSheet:
    """An entry in the list: a track's sheet with the files beside it read as one (2026-11-25), or All tracks.

    A track's runs can be in more than one file: a newer game's header starts ``(2)`` beside the old one (the track
    column did, on 2026-11-25), and rows held while a spreadsheet had the sheet open go in ``(written while open)``.
    """

    track: str
    fingerprint: str
    files: tuple[SheetFile, ...]
    everything: bool = False

    @property
    def key(self) -> str:
        """What the settings remember it by: ``Seed 5 0070ecd8``, or ``All tracks``."""
        if self.everything:
            return ALL_TRACKS
        return f"{self.track} {self.fingerprint}" if self.fingerprint else self.files[0].path.name

    @property
    def label(self) -> str:
        """``Seed 5  0070ecd8  (2 files)``: how the list names it."""
        if self.everything:
            return ALL_TRACKS
        label = "  ".join([self.track] + ([self.fingerprint] if self.fingerprint else []))
        return label + (f"  ({len(self.files)} files)" if len(self.files) > 1 else "")

    @property
    def paths(self) -> list[Path]:
        return [f.path for f in self.files]

    def holds(self, name: str) -> bool:
        """Whether a remembered name means this entry: its key, its label, or one of its files' names."""
        return name in (self.key, self.label) or any(f.path.name == name for f in self.files)


def _by_track(files) -> list[TrackSheet]:
    """Files grouped by track and fingerprint, in the order given; a file whose name isn't a sheet's is on its own."""
    groups: dict[tuple[str, str], list[SheetFile]] = {}
    for f in files:
        groups.setdefault((f.track, f.fingerprint) if f.fingerprint else (str(f.path), ""), []).append(f)
    return [TrackSheet(fs[0].track, fs[0].fingerprint, tuple(fs)) for fs in groups.values()]


def list_tracks(folder: Path) -> list[TrackSheet]:
    """The list's entries: All tracks first (if there's a sheet at all), then a track at a time, its files joined."""
    files = list_sheets(folder)
    tracks = _by_track(files)
    return ([TrackSheet(ALL_TRACKS, "", tuple(files), everything=True)] if tracks else []) + tracks


def is_lock_file(name: str) -> bool:
    """LibreOffice's ``.~lock.<name>#`` beside an open sheet, and Excel's ``~$<name>``."""
    return name.startswith(".~lock.") or name.startswith("~$")


def is_cars_file(name: str) -> bool:
    return bool(_CARS_NAME.match(name))


def list_sheets(folder: Path) -> list[SheetFile]:
    """Every track's sheet in the folder, siblings included, by track name; never ``cars.csv`` or a lock file."""
    sheets = []
    try:
        names = os.listdir(folder)
    except OSError:
        return []
    for name in names:
        if not name.lower().endswith(".csv") or is_lock_file(name) or is_cars_file(name):
            continue
        path = Path(folder) / name
        if not path.is_file():
            continue
        m = _SHEET_NAME.match(name)
        if m:
            notes = tuple(re.findall(r" \(([^)]*)\)", m.group("notes")))
            sheets.append(SheetFile(path, m.group("track"), m.group("print"), notes))
        else:
            sheets.append(SheetFile(path, path.stem, ""))
    sheets.sort(key=lambda s: (s.track.casefold(), s.fingerprint, s.notes))
    return sheets


def cars_files(folder: Path) -> list[Path]:
    """``cars.csv``, then ``cars (2).csv`` and on, oldest header first."""
    try:
        names = [n for n in os.listdir(folder) if is_cars_file(n)]
    except OSError:
        return []

    def order(name: str) -> int:
        m = re.search(r"\((\d+)\)", name)
        return int(m.group(1)) if m else 1

    return [Path(folder) / n for n in sorted(names, key=order)]


def read_text(path: Path) -> str:
    """A file's text, read whole and let go at once (see the module's note on sharing)."""
    with open(path, "rb") as f:
        data = f.read()
    return data.decode("utf-8-sig", errors="replace")


def parse(text: str) -> tuple[list[str], list[list[str]]]:
    """A header and its rows. A last row the game is still writing (no line end yet) is left for the next read,
    blank lines are skipped, and a short row is padded with blanks; a long one widens the header."""
    records = list(csv.reader(io.StringIO(text, newline="")))
    if text and not text.endswith(("\n", "\r")) and records:
        records.pop()
    records = [r for r in records if r]
    if not records:
        return [], []
    header, rows = records[0], records[1:]
    width = max([len(header)] + [len(r) for r in rows])
    header = header + [f"column {i + 1}" for i in range(len(header), width)]
    rows = [r + [""] * (width - len(r)) for r in rows]
    return header, rows


def read(path: Path) -> tuple[list[str], list[list[str]]]:
    return parse(read_text(path))


@dataclass
class CarBook:
    """Every profiled car's numbers by fingerprint, from ``cars.csv`` and any ``cars (2).csv`` after it."""

    columns: list[str] = field(default_factory=list)
    by_print: dict[str, dict[str, str]] = field(default_factory=dict)


def read_cars(folder: Path) -> CarBook:
    book = CarBook()
    for path in cars_files(folder):
        header, rows = read(path)
        if CARS_KEY not in header:
            continue
        key = header.index(CARS_KEY)
        for name in header:
            if name not in ("car", CARS_KEY) and name not in book.columns:
                book.columns.append(name)
        for row in rows:
            if row[key].strip():
                book.by_print[row[key].strip()] = dict(zip(header, row))
    return book


def group_of(name: str) -> str:
    """Which group a sheet column belongs to, by the names CarSheet writes; anything new is part of the run."""
    if _LAP_NAME.match(name):
        return LAPS
    if name in _SUMMARY_NAMES:
        return SUMMARY
    if _PIECE_SPEED.match(name):
        return PIECE_SPEEDS
    if _PIECE_TIME.match(name):
        return PIECE_TIMES
    return RUN


def is_number(text: str) -> bool:
    return bool(_NUMBER.match(text.strip()))


def is_blank(text: str) -> bool:
    return not text.strip()


def merge_headers(headers: list[list[str]]) -> list[str]:
    """Headers as one, each column where its own header put it: a column one file has and another hasn't goes in after
    the column before it there, so ``track`` lands after ``when`` whichever file comes first."""
    merged: list[str] = []
    for header in headers:
        before = None
        for name in header:
            if name not in merged:
                merged.insert(merged.index(before) + 1 if before is not None else 0, name)
            before = name
    return merged


def _with_track(header: list[str]) -> list[str]:
    if TRACK in header or not header:
        return header
    at = header.index("when") + 1 if "when" in header else 0
    return header[:at] + [TRACK] + header[at:]


def _lay_out(header: list[str], parts) -> list[list[str]]:
    """Each part's rows under the one header: ``parts`` are ``(their header, their rows, their track)``; a cell the
    header hasn't is dropped, a column the part hasn't is blank, and a blank track is the part's own."""
    t = header.index(TRACK) if TRACK in header else None
    rows = []
    for h, rs, track in parts:
        at = [(header.index(n), i) for i, n in enumerate(h) if n in header]
        for r in rs:
            row = [""] * len(header)
            for j, i in at:
                row[j] = r[i]
            if t is not None and is_blank(row[t]):
                row[t] = track
            rows.append(row)
    return rows


def _by_when(header: list[str], rows: list[list[str]]) -> list[list[str]]:
    """Rows in the order they were driven: ``when`` is ``2026-11-25 17:11:02``, which sorts as text; ties keep their order."""
    if "when" in header:
        w = header.index("when")
        rows.sort(key=lambda r: r[w])
    return rows


def read_track(entry: TrackSheet) -> tuple[list[str], list[list[str]]]:
    """A track's files as one sheet: their headers merged, a ``track`` column in it, a row from each file in the order
    they were driven, and a row without a track (an older file's) given the one in its file's name."""
    parts = [(*read(f.path), f.track) for f in entry.files]
    header = _with_track(merge_headers([h for h, _, _ in parts]))
    rows = _lay_out(header, parts)
    return header, (_by_when(header, rows) if len(parts) > 1 else rows)


def read_all(entries: list[TrackSheet]) -> tuple[list[str], list[list[str]]]:
    """Every track's runs as one sheet: the run's and the summary's columns, in the order they were driven."""
    parts = []
    for entry in entries:
        if entry.everything:
            continue
        h, rs = read_track(entry)
        parts.append((h, rs, entry.track))
    header = merge_headers([[n for n in h if group_of(n) in _SHARED_GROUPS] for h, _, _ in parts])
    return header, _by_when(header, _lay_out(header, parts))


def build_table(header: list[str], rows: list[list[str]], cars: CarBook | None = None,
                pinned: tuple[str, ...] = PINNED) -> Table:
    """The sheet's columns (the pinned ones first, the rest in the file's order), then the car's numbers
    joined on the fingerprint (blank where cars.csv doesn't know the car)."""
    notes = []
    order = [header.index(p) for p in pinned if p in header]
    order += [i for i in range(len(header)) if i not in order]
    columns = [Column(header[i], group_of(header[i])) for i in order]
    table_rows = [[r[i] for i in order] for r in rows]

    if cars and cars.columns:
        taken = set(header)
        key = header.index(SHEET_KEY) if SHEET_KEY in header else None
        if key is None:
            notes.append(f"No '{SHEET_KEY}' column, so no car numbers.")
        for name in cars.columns:
            columns.append(Column(name if name not in taken else f"car {name}", CAR))
        for src, out in zip(rows, table_rows):
            car = cars.by_print.get(src[key].strip(), {}) if key is not None else {}
            out.extend(car.get(name, "") for name in cars.columns)

    if len(columns) > MAX_COLUMNS:
        notes.append(f"Only the first {MAX_COLUMNS} of {len(columns)} columns are shown.")
        columns = columns[:MAX_COLUMNS]
        table_rows = [r[:MAX_COLUMNS] for r in table_rows]

    for i, col in enumerate(columns):
        filled = [r[i] for r in table_rows if not is_blank(r[i])]
        col.numeric = bool(filled) and all(is_number(c) for c in filled)

    return Table(columns, table_rows, sum(1 for p in pinned if p in header), notes)


def entry_of(path: Path) -> TrackSheet:
    """One sheet file as an entry of its own, named as the list would name it."""
    path = Path(path)
    m = _SHEET_NAME.match(path.name)
    f = SheetFile(path, m.group("track"), m.group("print")) if m else SheetFile(path, path.stem, "")
    return TrackSheet(f.track, f.fingerprint, (f,))


def load(sheet: Path | TrackSheet, folder: Path | None = None) -> Table:
    """An entry (or one sheet file) as a table, with the car's numbers from the cars.csv in its folder: a track's files
    as one, or All tracks' shared columns over every track."""
    entry = sheet if isinstance(sheet, TrackSheet) else entry_of(Path(sheet))
    if folder is None:
        folder = entry.files[0].path.parent if entry.files else Path(".")
    if entry.everything:
        header, rows = read_all(_by_track(entry.files))
        return build_table(header, rows, read_cars(folder), PINNED_ALL)
    header, rows = read_track(entry)
    return build_table(header, rows, read_cars(folder))


def sort_order(table: Table, keys: list[tuple[int, bool]]) -> list[int]:
    """Row indices in sorted order. ``keys`` are ``(column, descending)``, the first the most significant.

    Numbers sort as numbers (``0.50`` and ``0.55`` alike), text as text ignoring case, and **blanks go last in
    either direction**, never as 0: a piece a crashed run never reached isn't its fastest. Ties keep the file's
    order, oldest first."""
    order = list(range(len(table.rows)))
    for col, descending in reversed(keys):
        if not 0 <= col < len(table.columns):
            continue
        cells = [r[col] for r in table.rows]
        filled = [i for i in order if not is_blank(cells[i])]
        blank = [i for i in order if is_blank(cells[i])]
        if table.columns[col].numeric:
            filled.sort(key=lambda i: float(cells[i]), reverse=descending)
        else:
            filled.sort(key=lambda i: cells[i].strip().casefold(), reverse=descending)
        order = filled + blank
    return order


def stamp(path: Path) -> tuple[int, int] | None:
    """A file's size and time, to notice the game adding a row; None if it's gone."""
    try:
        st = os.stat(path)
    except OSError:
        return None
    return st.st_size, st.st_mtime_ns
