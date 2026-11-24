"""The window, headless: DearPyGui builds its items and runs its callbacks without a viewport, so the table's
rows, their order after a sort, the new rows after the game appends and the column groups are all checked here.
What only a screen shows (the header's arrows, the frozen columns, the font) is checked with --screenshot."""

import pytest

dpg = pytest.importorskip("dearpygui.dearpygui")

import sheets
import viewer
from conftest import CARS, ROWS, write_sheet

SHEET = "Shakedown 8a6323aa.csv"


@pytest.fixture
def view(folder):
    dpg.create_context()
    v = viewer.Viewer(folder, settings=None)
    yield v
    dpg.destroy_context()


def times(v):
    i = v.table.index("time s")
    return [v.table.rows[r][i] for r in v.row_order()]


def append(path, line):
    with open(path, "ab") as f:
        f.write((line + "\r\n").encode("utf-8"))


def test_opens_the_first_sheet_with_every_run(view):
    assert view.current.path.name == SHEET
    assert len(view.row_ids) == 4
    assert view.row_order() == [0, 1, 2, 3]
    assert "4 runs" in dpg.get_value(viewer.Viewer.STATUS)


def test_a_header_click_sorts_and_again_reverses(view):
    col = view.col_ids[view.table.index("time s")]
    view._on_sort([[col, 1]])
    assert times(view) == ["30.00", "110.00", "116.91", "120.05"]
    view._on_sort([[col, -1]])
    assert times(view) == ["120.05", "116.91", "110.00", "30.00"]


def test_shift_click_sorts_by_a_second_column(view):
    caution, time = (view.col_ids[view.table.index(n)] for n in ("caution", "time s"))
    view._on_sort([[caution, 1], [time, -1]])
    assert times(view) == ["120.05", "30.00", "116.91", "110.00"]


def test_blank_cells_sort_last_from_the_window_too(view):
    view.sort_by([("lap 1 s", False)])
    assert times(view)[-1] == "30.00"
    view.sort_by([("lap 1 s", True)])
    assert times(view)[-1] == "30.00"


def test_a_new_run_from_the_game_appears_in_its_sorted_place(view, folder):
    view.sort_by([("time s", False)])
    append(folder / SHEET, ROWS[0].replace("00:35:06", "00:50:00").replace("120.05", "100.50"))
    assert view.reload()
    assert len(view.row_ids) == 5
    assert times(view) == ["30.00", "100.50", "110.00", "116.91", "120.05"]
    assert not view.reload()  # nothing new, nothing done


def test_a_half_written_row_waits(view, folder):
    with open(folder / SHEET, "ab") as f:
        f.write(b"2026-11-25 00:51:00,0.0.1+abc,auto,st")
    view.reload()
    assert len(view.row_ids) == 4


def test_a_car_newly_in_cars_csv_fills_its_cells(folder):
    write_sheet(folder / "cars.csv", CARS[:2])
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=None)
        mass = v.table.index("Mass (kg)")
        assert dpg.get_value(v.cell_ids[2][mass]) == ""
        write_sheet(folder / "cars.csv", CARS)
        assert v.reload()
        assert dpg.get_value(v.cell_ids[2][mass]) == "1004"
    finally:
        dpg.destroy_context()


def test_car_columns_hidden_until_ticked_pinned_ones_always_shown(view):
    car = [i for i, c in enumerate(view.table.columns) if c.group == sheets.CAR]
    shown = lambda i: dpg.get_item_configuration(view.col_ids[i])["enabled"]
    assert car and not any(shown(i) for i in car)
    view.show_group(sheets.CAR, True)
    assert all(shown(i) for i in car)
    view.show_group(sheets.RUN, False)
    assert shown(view.table.index("when")) and shown(view.table.index("caution"))
    assert not shown(view.table.index("version"))


def test_the_long_column_has_its_whole_text_on_hover(view):
    c = view.table.index("differences from the stock car")
    assert dpg.get_value(view.tip_ids[(2, c)]).startswith("Mass 1004 kg")


def test_picking_another_sheet(view, folder):
    write_sheet(folder / "Seed 5 0070ecd8.csv", ["when,driver,car,caution,time s", "2026-11-25 01:00:00,auto,stock,0.80,60.00"])
    view.refresh_list()
    view.open_sheet(next(s for s in view.sheet_files if s.track == "Seed 5"))
    assert len(view.row_ids) == 1 and view.table.pinned == 4


def test_a_sheet_that_goes_away_falls_back_to_another(view, folder):
    write_sheet(folder / "Seed 5 0070ecd8.csv", ["when,time s", "x,1"])
    (folder / SHEET).unlink()
    view.tick(10_000.0)
    assert view.current.track == "Seed 5"


def test_an_empty_folder_opens_its_first_sheet_when_the_game_writes_one(view, tmp_path):
    empty = tmp_path / "empty"
    empty.mkdir()
    view.set_folder(empty)
    assert view.current is None
    write_sheet(empty / "Shakedown 8a6323aa.csv", ["when,time s", "x,1"])
    view.tick(10_000.0)
    assert view.current.track == "Shakedown" and len(view.row_ids) == 1


def test_a_missing_folder_says_so(view, tmp_path):
    view.set_folder(tmp_path / "nope")
    assert view.current is None and view.table is None
    assert dpg.get_value(viewer.Viewer.STATUS) == "No sheets in this folder."


def test_a_sheets_own_path_opens_its_folder_and_it(view, folder):
    write_sheet(folder / "Seed 5 0070ecd8.csv", ["when,time s", "x,1"])
    view.set_folder(folder / SHEET)
    assert view.folder == folder and view.current.path.name == SHEET


def test_settings_remember_folder_sheet_and_groups(folder, tmp_path):
    path = tmp_path / "settings" / "viewer.json"
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=path)
        v.show_group(sheets.CAR, True)
    finally:
        dpg.destroy_context()
    saved = viewer.load_settings(path)
    assert saved["folder"] == str(folder) and saved["sheet"] == SHEET and saved["groups"][sheets.CAR]


def test_command_line():
    args = viewer.parse_args(["C:/x", "--groups=Run,car", "--sort=time s:desc", "--no-settings"])
    assert args["path"] == "C:/x" and not args["settings"]
    assert args["groups"][sheets.CAR] and args["groups"][sheets.RUN] and not args["groups"][sheets.SUMMARY]
    assert args["sort"] == [("time s", True)]
