"""The window, headless: DearPyGui builds its items and runs its callbacks without a viewport, so the table's
rows, their order after a sort, the new rows after the game appends and the column groups are all checked here.
What only a screen shows (the header's arrows, the frozen columns, the font) is checked with --screenshot."""

import pytest

dpg = pytest.importorskip("dearpygui.dearpygui")

import sheets
import viewer
from conftest import CARS, HEADER, ROWS, write_sheet

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
    assert view.current.key == "Shakedown 8a6323aa" and view.current.holds(SHEET)
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
    view.open_sheet(next(s for s in view.entries if s.track == "Seed 5"))
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
    assert view.folder == folder and view.current.holds(SHEET)


def test_settings_remember_folder_sheet_and_groups(folder, tmp_path):
    path = tmp_path / "settings" / "viewer.json"
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=path)
        v.show_group(sheets.CAR, True)
    finally:
        dpg.destroy_context()
    saved = viewer.load_settings(path)
    assert saved["folder"] == str(folder) and saved["sheet"] == "Shakedown 8a6323aa" and saved["groups"][sheets.CAR]


def test_command_line():
    args = viewer.parse_args(["C:/x", "--groups=Run,car", "--sort=time s:desc", "--no-settings"])
    assert args["path"] == "C:/x" and not args["settings"]
    assert args["groups"][sheets.CAR] and args["groups"][sheets.RUN] and not args["groups"][sheets.SUMMARY]
    assert args["sort"] == [("time s", True)]
    args = viewer.parse_args(["--sort=when:desc", "--sort=track", "--sort=time s", "--sheet=All tracks"])
    assert args["sort"] == [("when", True), ("track", False), ("time s", False)] and args["sheet"] == "All tracks"


# ------------------------------------------------------------------ the sort boxes (2026-11-25)

def boxes():
    return [(dpg.get_value(viewer.Viewer.SORT_COLUMN.format(k)),
             dpg.get_item_configuration(viewer.Viewer.SORT_WAY.format(k))["label"]) for k in range(viewer.SORT_KEYS)]


def test_the_boxes_offer_every_column_and_start_empty(view):
    items = dpg.get_item_configuration(viewer.Viewer.SORT_COLUMN.format(0))["items"]
    assert items[0] == viewer.NO_COLUMN and "time s" in items and "Mass (kg)" in items
    assert boxes() == [(viewer.NO_COLUMN, viewer.UP)] * 3


def test_three_columns_from_the_boxes(view):
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(0), "car")
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(1), "clean")
    view._turn_way(1)  # clean: yes before no
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(2), "time s")
    view._sort_from_boxes()
    assert view.sort_keys == [("car", False), ("clean", True), ("time s", False)]
    assert times(view) == ["110.00", "30.00", "116.91", "120.05"]
    assert boxes()[1] == ("clean", viewer.DOWN)


def test_a_box_left_empty_or_a_column_twice_is_passed_over(view):
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(0), viewer.NO_COLUMN)
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(1), "time s")
    dpg.set_value(viewer.Viewer.SORT_COLUMN.format(2), "time s")
    view._sort_from_boxes()
    assert view.sort_keys == [("time s", False)]
    assert boxes() == [("time s", viewer.UP), (viewer.NO_COLUMN, viewer.UP), (viewer.NO_COLUMN, viewer.UP)]


def test_a_header_click_fills_the_boxes(view):
    caution, time = (view.col_ids[view.table.index(n)] for n in ("caution", "time s"))
    view._on_sort([[caution, 1], [time, -1]])
    assert boxes() == [("caution", viewer.UP), ("time s", viewer.DOWN), (viewer.NO_COLUMN, viewer.UP)]


def test_the_report_of_a_table_just_built_keeps_the_other_columns(view):
    view.set_sort([("caution", False), ("time s", True)])
    assert view._fresh_table  # a new first column built the table again
    view._on_sort([[view.col_ids[view.table.index("caution")], 1]])  # Dear ImGui's first frame
    assert view.sort_keys == [("caution", False), ("time s", True)]
    view._on_sort([[view.col_ids[view.table.index("caution")], 1]])  # a real click on it
    assert view.sort_keys == [("caution", False)]


def test_clear_is_the_files_order(view):
    view.set_sort([("time s", False)])
    view.set_sort([])
    assert view.row_order() == [0, 1, 2, 3] and boxes()[0] == (viewer.NO_COLUMN, viewer.UP)


def test_the_sort_stays_on_another_sheet_and_a_column_it_hasnt_is_said(view, folder):
    write_sheet(folder / "Seed 5 0070ecd8.csv", ["when,driver,car,caution,time s",
                                                  "2026-11-25 01:00:00,auto,stock,0.80,60.00",
                                                  "2026-11-25 01:01:00,auto,stock,0.85,58.00"])
    view.set_sort([("lap 1 s", False), ("time s", False)])
    view.refresh_list()
    view.open_sheet(next(e for e in view.entries if e.track == "Seed 5"))
    assert view.sort_keys == [("lap 1 s", False), ("time s", False)]
    assert times(view) == ["58.00", "60.00"]
    assert "not sorted by lap 1 s" in dpg.get_value(viewer.Viewer.STATUS)


def test_all_tracks_is_first_in_the_list_and_has_every_run(view, folder):
    write_sheet(folder / "Seed 5 0070ecd8.csv", ["when,driver,car,caution,time s",
                                                  "2026-11-25 01:00:00,auto,stock,0.80,60.00"])
    view.refresh_list()
    assert view.entries[0].everything
    view.open_sheet(view.entries[0])
    assert len(view.row_ids) == 5 and view.table.pinned == 5
    view.set_sort([("track", True), ("time s", False)])
    track = view.table.index("track")
    assert [view.table.rows[r][track] for r in view.row_order()] == ["Shakedown"] * 4 + ["Seed 5"]
    assert "All tracks (2): 5 runs" in dpg.get_value(viewer.Viewer.STATUS)


def test_a_new_file_beside_the_sheet_joins_it(view, folder):
    write_sheet(folder / "Shakedown 8a6323aa (2).csv", [
        "when,track,version,driver,car,car fingerprint,caution,time s",
        "2026-11-25 18:00:00,Shakedown,0.0.1+b,auto,stock,4f918acd,0.80,56.73"])
    view.tick(10_000.0)
    assert view.current.label == "Shakedown  8a6323aa  (2 files)" and len(view.row_ids) == 5


def test_settings_remember_the_sort(folder, tmp_path):
    path = tmp_path / "settings" / "viewer.json"
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=path)
        v.set_sort([("when", True), ("time s", False)])
    finally:
        dpg.destroy_context()
    assert viewer.load_settings(path)["sort"] == [["when", True], ["time s", False]]


# ------------------------------------------------------------------ column widths (2026-11-25)

def m(text):
    """A stand-in for the font: 8 px a character, as the window measures with DearPyGui once a frame is drawn."""
    return 8 * len(text)


def narrow_spaces(text):
    return 8 * len(text.replace(" ", "")) + 2 * text.count(" ")


def test_a_column_is_as_wide_as_its_widest_cell_or_its_header():
    assert viewer.fit_column("time s", ["64.33", "120.05"], m, viewer.ONE_LINE).width == 48 + viewer.SORT_ROOM
    assert viewer.fit_column("car", ["stock", "matthew-2026-11-23"], m, viewer.ONE_LINE).width == 8 * 18
    assert viewer.fit_column("car", [], m, viewer.ONE_LINE).width == 24 + viewer.SORT_ROOM


def test_the_long_column_is_held_to_its_cap():
    f = viewer.fit_column("differences from the stock car", ["Mass 1004 kg (stock 650); " * 10], m, cap=300)
    assert f.width == 300 and f.label == "differences from the stock car" and not f.angled


def test_a_long_header_is_angled_and_a_short_one_stays_flat():
    f = viewer.fit_column("32 corner-30-banked left km/h", ["61.5", "100.2"], m)
    assert f.angled and f.width == 40  # the numbers set the width
    assert viewer.fit_column("spins", ["0"], m).width == viewer.SORT_ROOM  # room for the sort arrow below it
    f = viewer.fit_column("car", ["matthew-2026-11-23"], m)
    assert not f.angled and f.width == 8 * 18
    # angled only when the header would make the column at least NARROWER_BY wider than its cells
    assert not viewer.fit_column("abcd", ["x" * 7], m).angled  # 59 against 56
    assert viewer.fit_column("abcd", ["x" * 5], m).angled  # 59 against 40


def test_wrapped_headers_are_as_narrow_as_three_lines_allow():
    assert viewer.wrap_header("Centre of mass drop (m)", 0, m) == ["Centre", "of mass", "drop (m)"]
    assert viewer.wrap_header("time s", 200, m) == ["time s"]  # room for it on one line
    f = viewer.fit_column("Centre of mass drop (m)", ["0.25"], m, viewer.WRAPPED)
    assert f.label == "Centre\nof mass\ndrop (m)" and f.width == 64 + viewer.SORT_ROOM and not f.angled
    # a wrap that saves less than NARROWER_BY isn't worth the lines
    assert viewer.fit_column("abcdefghijk l", ["0.25"], narrow_spaces, viewer.WRAPPED).label == "abcdefghijk l"


def config(v, name):
    return dpg.get_item_configuration(v.col_ids[v.table.index(name)])


@pytest.fixture
def fitted(folder):
    dpg.create_context()
    v = viewer.Viewer(folder, settings=None, measure=m)
    yield v
    dpg.destroy_context()


def test_columns_fit_every_run_not_only_those_on_screen(folder):
    rows = [ROWS[0].replace("00:35:06", f"01:{i:02}:00") for i in range(59)]
    rows.append(ROWS[0].replace("00:35:06", "02:00:00").replace(",stock,", ",a-long-name-on-the-last-run,"))
    write_sheet(folder / SHEET, [HEADER] + rows)
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=None, measure=m)
        assert config(v, "car")["init_width_or_weight"] == 8 * len("a-long-name-on-the-last-run")
    finally:
        dpg.destroy_context()


def test_the_window_angles_long_headers_but_not_the_frozen_columns(fitted):
    piece = config(fitted, "02 corner-30-banked left s")
    assert piece["angled_header"] and piece["no_header_label"] and piece["init_width_or_weight"] == 32
    caution = config(fitted, "caution")  # frozen: Dear ImGui draws no angled header there
    assert not caution["angled_header"] and caution["init_width_or_weight"] == 56 + viewer.SORT_ROOM
    assert config(fitted, "differences from the stock car")["init_width_or_weight"] == 300


def test_a_hidden_group_is_fitted_too_so_showing_it_builds_nothing(fitted):
    table_id = fitted.table_id
    mass = config(fitted, "Mass (kg)")
    assert mass["init_width_or_weight"] == 32 and mass["angled_header"] and not mass["enabled"]
    fitted.show_group(sheets.CAR, True)
    assert fitted.table_id == table_id


def test_a_new_run_too_wide_for_its_column_fits_it_again(fitted, folder):
    dpg.set_value(fitted.cell_ids[1][0], True)  # a marked run
    table_id = fitted.table_id
    append(folder / SHEET, ROWS[0].replace("00:35:06", "00:50:00").replace(",stock,", ",a-much-longer-car-name,"))
    assert fitted.reload()
    assert fitted.table_id != table_id and len(fitted.row_ids) == 5
    assert config(fitted, "car")["init_width_or_weight"] == 8 * len("a-much-longer-car-name")
    assert dpg.get_value(fitted.cell_ids[1][0]) and not dpg.get_value(fitted.cell_ids[0][0])


def test_a_new_run_that_fits_is_only_added(fitted, folder):
    table_id = fitted.table_id
    append(folder / SHEET, ROWS[0].replace("00:35:06", "00:50:00"))
    assert fitted.reload()
    assert fitted.table_id == table_id and len(fitted.row_ids) == 5


def test_a_dragged_column_keeps_its_width_until_fit_columns(fitted, monkeypatch):
    car = fitted.col_ids[fitted.table.index("car")]
    real = dpg.get_item_state
    monkeypatch.setattr(dpg, "get_item_state", lambda item: {"rect_size": [500, 0]} if item == car else real(item))
    fitted.set_sort([("time s", False)])  # builds the table again, for the header's arrow
    assert config(fitted, "car")["init_width_or_weight"] == 500
    fitted.set_sort([("caution", False)])  # and again: still his width
    assert config(fitted, "car")["init_width_or_weight"] == 500
    fitted.fit_columns()
    assert config(fitted, "car")["init_width_or_weight"] == 24 + viewer.SORT_ROOM  # its header, wider than "stock"
    assert [n for n, _ in fitted.sort_keys] == ["caution"]


def test_the_columns_are_fitted_once_text_can_be_measured(folder):
    ready = []
    dpg.create_context()
    try:
        v = viewer.Viewer(folder, settings=None, measure=lambda text: m(text) if ready else None)
        assert v.fits is None and config(v, "car")["init_width_or_weight"] == 0  # Dear ImGui's own fit meanwhile
        ready.append(True)  # the first frame has been drawn
        v.tick(0.0)
        assert v.fits is not None and config(v, "car")["init_width_or_weight"] == 24 + viewer.SORT_ROOM
    finally:
        dpg.destroy_context()


def test_headers_from_the_command_line():
    assert viewer.parse_args(["--headers=Wrapped"])["headers"] == viewer.WRAPPED
    assert viewer.parse_args([])["headers"] is None


def test_the_headers_stay_put_under_angled_ones(fitted, folder):
    # angled headers are a row of their own, so two rows are frozen: the angled ones and the headers under them
    assert dpg.get_item_configuration(fitted.table_id)["freeze_rows"] == 2
    dpg.destroy_context()
    dpg.create_context()
    flat = viewer.Viewer(folder, settings=None, measure=m, headers=viewer.ONE_LINE)
    assert dpg.get_item_configuration(flat.table_id)["freeze_rows"] == 1
