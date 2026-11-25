import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

import sheets
from conftest import CARS, HEADER, ROWS, write_sheet


def column(table, name):
    i = table.index(name)
    return [r[i] for r in table.rows]


def sorted_column(table, name, by, descending=False):
    order = sheets.sort_order(table, [(table.index(by), descending)])
    i = table.index(name)
    return [table.rows[k][i] for k in order]


# ------------------------------------------------------------------ reading


def test_reads_bom_crlf_and_quoted_cells(tmp_path):
    path = write_sheet(tmp_path / "t 0000abcd.csv", ['when,"a, b",c', '1,"say ""hi""","two\r\nlines"'])
    header, rows = sheets.read(path)
    assert header == ["when", "a, b", "c"]
    assert rows == [["1", 'say "hi"', "two\r\nlines"]]


def test_a_row_still_being_written_waits_for_the_next_read():
    header, rows = sheets.parse("a,b\r\n1,2\r\n3,")
    assert header == ["a", "b"] and rows == [["1", "2"]]


def test_short_rows_padded_long_rows_widen_the_header_blank_lines_skipped():
    header, rows = sheets.parse("a,b\r\n1\r\n\r\n2,3,4\r\n")
    assert header == ["a", "b", "column 3"]
    assert rows == [["1", "", ""], ["2", "3", "4"]]


def test_empty_file_is_an_empty_table(tmp_path):
    path = tmp_path / "x 00000000.csv"
    path.write_bytes(b"")
    table = sheets.load(path)
    assert table.columns == [] and table.rows == []


# ------------------------------------------------------------------ the folder


def test_lists_sheets_and_their_siblings_never_cars_or_locks(tmp_path):
    for name in ["Seed 5 0070ecd8.csv", "4 847ed7b9.csv", "4 847ed7b9 (2).csv",
                 "Shakedown 8a6323aa (written while open).csv", "cars.csv", "cars (2).csv",
                 ".~lock.4 847ed7b9.csv#", "~$4 847ed7b9.csv", "notes.txt", "my own.csv"]:
        (tmp_path / name).write_bytes(b"a\r\n")
    found = [(s.track, s.fingerprint, s.notes) for s in sheets.list_sheets(tmp_path)]
    assert found == [
        ("4", "847ed7b9", ()),
        ("4", "847ed7b9", ("2",)),
        ("my own", "", ()),
        ("Seed 5", "0070ecd8", ()),
        ("Shakedown", "8a6323aa", ("written while open",)),
    ]
    assert sheets.list_sheets(tmp_path)[1].label == "4  847ed7b9  (2)"


def test_a_missing_folder_lists_nothing(tmp_path):
    assert sheets.list_sheets(tmp_path / "nope") == []


# ------------------------------------------------------------------ the table


def test_columns_grouped_by_the_names_the_game_writes(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    groups = {c.name: c.group for c in table.columns}
    assert groups["time s"] == sheets.RUN
    assert groups["differences from the stock car"] == sheets.RUN
    assert groups["lap 2 s"] == groups["best lap s"] == sheets.LAPS
    assert groups["furthest m"] == groups["spins"] == sheets.SUMMARY
    assert groups["02 corner-30-banked left s"] == sheets.PIECE_TIMES
    assert groups["02 corner-30-banked left km/h"] == sheets.PIECE_SPEEDS
    assert groups["Mass (kg)"] == groups["wing"] == sheets.CAR
    assert sheets.group_of("split 3 s") == sheets.LAPS


def test_the_track_column_goes_with_the_run(tmp_path):
    """Since 2026-11-25 every row says its track, after when; older sheets have no such column."""
    write_sheet(tmp_path / "Seed 5 0070ecd8.csv", [
        "when,track,version,driver,car,car fingerprint,caution,time s",
        '2026-11-25 18:00:00,"Seed 5, a sprint",0.0.1+abc,auto,stock,4f918acd,0.80,86.66',
    ])
    table = sheets.load(tmp_path / "Seed 5 0070ecd8.csv")
    names = [c.name for c in table.columns]
    assert names[:5] == ["when", "driver", "car", "caution", "track"]
    assert table.columns[table.index("track")].group == sheets.RUN
    assert column(table, "track") == ["Seed 5, a sprint"]


OLD = ["when,version,driver,car,car fingerprint,caution,time s,top km/h,01 start s",
       "2026-11-25 01:00:00,0.0.1+a,auto,stock,4f918acd,0.50,90.00,100.0,4.00",
       "2026-11-25 03:00:00,0.0.1+a,auto,stock,4f918acd,0.55,88.00,101.0,3.90"]
NEW = ["when,track,version,driver,car,car fingerprint,caution,time s,top km/h,01 start s",
       "2026-11-25 02:00:00,Seed 5,0.0.1+b,you,stock,4f918acd,,95.00,99.0,4.20",
       "2026-11-25 17:00:00,Seed 5,0.0.1+b,auto,stock,4f918acd,0.60,86.66,102.0,3.80"]
LOOP = ["when,track,version,driver,car,car fingerprint,caution,time s,lap 1 s,top km/h,01 start-finish s",
        "2026-11-25 04:00:00,4,0.0.1+b,auto,heavy,5d844085,0.50,120.00,40.00,140.0,2.00"]


@pytest.fixture
def two_tracks(tmp_path):
    write_sheet(tmp_path / "Seed 5 0070ecd8.csv", OLD)
    write_sheet(tmp_path / "Seed 5 0070ecd8 (2).csv", NEW)
    write_sheet(tmp_path / "4 847ed7b9.csv", LOOP)
    write_sheet(tmp_path / "cars.csv", CARS)
    return tmp_path


def test_a_tracks_files_are_one_entry_after_all_tracks(two_tracks):
    entries = sheets.list_tracks(two_tracks)
    assert [e.label for e in entries] == ["All tracks", "4  847ed7b9", "Seed 5  0070ecd8  (2 files)"]
    assert entries[0].everything and len(entries[0].files) == 3
    assert entries[2].key == "Seed 5 0070ecd8"
    assert entries[2].holds("Seed 5 0070ecd8 (2).csv") and entries[2].holds("Seed 5 0070ecd8")
    assert sheets.list_tracks(two_tracks / "nope") == []


def test_merged_headers_keep_each_column_where_its_file_put_it():
    assert sheets.merge_headers([["when", "version", "time s"], ["when", "track", "version", "time s"]]) \
        == ["when", "track", "version", "time s"]
    assert sheets.merge_headers([["when", "track", "a"], ["when", "a", "b"]]) == ["when", "track", "a", "b"]


def test_a_tracks_old_and_new_files_read_as_one_sheet_in_the_order_driven(two_tracks):
    entry = sheets.list_tracks(two_tracks)[2]
    table = sheets.load(entry, two_tracks)
    assert column(table, "when") == ["2026-11-25 01:00:00", "2026-11-25 02:00:00", "2026-11-25 03:00:00",
                                     "2026-11-25 17:00:00"]
    assert column(table, "track") == ["Seed 5"] * 4  # the old file's rows from its name
    assert column(table, "time s") == ["90.00", "95.00", "88.00", "86.66"]
    assert column(table, "Mass (kg)") == ["650"] * 4


def test_all_tracks_has_every_run_with_the_shared_columns_and_the_track_pinned(two_tracks):
    table = sheets.load(sheets.list_tracks(two_tracks)[0], two_tracks)
    names = [c.name for c in table.columns]
    assert names[:5] == ["when", "track", "driver", "car", "caution"] and table.pinned == 5
    assert "time s" in names and "top km/h" in names and "Mass (kg)" in names
    assert not any(n.startswith("01 ") or n.startswith("lap ") for n in names)
    assert column(table, "track") == ["Seed 5", "Seed 5", "Seed 5", "4", "Seed 5"]
    order = sheets.sort_order(table, [(table.index("track"), False), (table.index("time s"), False)])
    assert [table.rows[i][table.index("time s")] for i in order] == ["120.00", "86.66", "88.00", "90.00", "95.00"]


def test_pinned_columns_first_then_the_files_order(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    names = [c.name for c in table.columns]
    # an older sheet without a track column gets one, from the file's name, after when
    assert names[:6] == ["when", "driver", "car", "caution", "track", "version"]
    assert set(column(table, "track")) == {"Shakedown"}
    assert table.pinned == 4
    assert names.index("time s") < names.index("01 start-finish s") < names.index("Mass (kg)")


def test_car_numbers_joined_on_the_fingerprint(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert column(table, "Mass (kg)") == ["650", "650", "1004", "1004"]
    assert column(table, "Peak slip (°)") == ["8", "8", "13.46", "13.46"]
    assert "fingerprint" not in [c.name for c in table.columns]


def test_a_car_cars_csv_doesnt_know_is_blank(folder):
    write_sheet(folder / "cars.csv", CARS[:2])
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert column(table, "Mass (kg)") == ["650", "650", "", ""]


def test_no_cars_csv_no_car_columns(folder):
    (folder / "cars.csv").unlink()
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert not [c for c in table.columns if c.group == sheets.CAR]


def test_a_later_cars_file_adds_columns_and_wins(folder):
    write_sheet(folder / "cars (2).csv", ["car,fingerprint,wing,Mass (kg),Grip,Peak slip (°),New thing",
                                          "heavy,5d844085,base:wing-twin,1004,1.4,13.46,7"])
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert column(table, "wing") == ["base:wing-none", "base:wing-none", "base:wing-twin", "base:wing-twin"]
    assert column(table, "New thing") == ["", "", "7", "7"]


def test_a_car_column_named_like_a_sheet_column_is_told_apart():
    cars = sheets.CarBook(["time s"], {"x": {"time s": "9"}})
    table = sheets.build_table(["car fingerprint", "time s"], [["x", "1"]], cars)
    assert [c.name for c in table.columns] == ["car fingerprint", "time s", "car time s"]


def test_numbers_are_numbers_even_with_trailing_zeros(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    numeric = {c.name: c.numeric for c in table.columns}
    assert numeric["caution"] and numeric["time s"] and numeric["lap 1 s"] and numeric["Mass (kg)"]
    assert not numeric["car fingerprint"] and not numeric["when"] and not numeric["clean"]
    assert not numeric["version"] and not numeric["wing"]


# ------------------------------------------------------------------ sorting


def test_sorts_numbers_as_numbers(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert sorted_column(table, "time s", "time s") == ["30.00", "110.00", "116.91", "120.05"]
    assert sorted_column(table, "time s", "time s", True) == ["120.05", "116.91", "110.00", "30.00"]


def test_blanks_go_last_both_ways(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert sorted_column(table, "caution", "caution") == ["0.50", "0.50", "0.55", ""]
    assert sorted_column(table, "caution", "caution", True) == ["0.55", "0.50", "0.50", ""]
    assert sorted_column(table, "lap 1 s", "lap 1 s", True) == ["61.00", "59.00", "55.00", ""]


def test_ties_keep_the_files_order(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    for descending in (False, True):
        order = sheets.sort_order(table, [(table.index("car"), descending)])
        whens = [table.rows[k][0] for k in order]
        heavy = [w for w in whens if w >= "2026-11-25 00:36"]
        assert heavy == ["2026-11-25 00:36:46", "2026-11-25 00:40:00"]


def test_text_sorts_ignoring_case():
    table = sheets.build_table(["car"], [["b"], ["A"], ["c"], [""]])
    assert sorted_column(table, "car", "car") == ["A", "b", "c", ""]


def test_sorts_by_several_columns(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    keys = [(table.index("caution"), False), (table.index("time s"), True)]
    order = sheets.sort_order(table, keys)
    assert [table.rows[k][table.index("time s")] for k in order] == ["120.05", "30.00", "116.91", "110.00"]


def test_a_car_column_sorts_too(folder):
    table = sheets.load(folder / "Shakedown 8a6323aa.csv")
    assert sorted_column(table, "car", "Mass (kg)", True) == ["heavy", "heavy", "stock", "stock"]


# ------------------------------------------------------------------ never in the game's way


@pytest.mark.skipif(sys.platform != "win32" or not shutil.which("pwsh"), reason="the game's own append, on Windows")
def test_the_games_append_goes_through_while_python_holds_the_sheet(folder):
    """Even a handle held open (the viewer only ever holds one for a read) shares writing, so .NET's
    File.AppendAllText, which is what CarSheet.Append calls, isn't refused."""
    path = folder / "Shakedown 8a6323aa.csv"
    with open(path, "rb"):
        script = f"[IO.File]::AppendAllText('{path}', \"new,row`r`n\")"
        done = subprocess.run(["pwsh", "-NoProfile", "-Command", script], capture_output=True, text=True)
    assert done.returncode == 0, done.stderr
    assert sheets.read(path)[1][-1][:2] == ["new", "row"]


# ------------------------------------------------------------------ his real sheets, read only


REAL = sheets.DEFAULT_FOLDER


@pytest.mark.skipif(not REAL.is_dir(), reason="no profiles folder on this machine")
def test_his_real_sheets_read_and_are_left_untouched():
    files = sheets.list_sheets(REAL) + [sheets.SheetFile(p, "cars", "") for p in sheets.cars_files(REAL)]
    before = {f.path: sheets.stamp(f.path) for f in files}
    for f in sheets.list_sheets(REAL):
        table = sheets.load(f.path)
        lines = [l for l in sheets.read_text(f.path).splitlines() if l]
        assert len(table.rows) == len(lines) - 1
        assert table.columns[0].name == "when"
    runs = 0
    for entry in sheets.list_tracks(REAL):
        table = sheets.load(entry, REAL)
        if entry.everything:
            everything = len(table.rows)
        else:
            runs += len(table.rows)
        assert all(r[table.index("track")] for r in table.rows)
    assert everything == runs
    assert {f.path: sheets.stamp(f.path) for f in files} == before
