import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


def write_sheet(path: Path, lines: list[str]) -> Path:
    """A file as CarSheet writes one: UTF-8 with its mark, CRLF after every line."""
    path.write_bytes(("\r\n".join(lines) + "\r\n").encode("utf-8-sig"))
    return path


HEADER = ("when,version,driver,car,car fingerprint,caution,clean,differences from the stock car,finished,time s,"
          "lap 1 s,lap 2 s,best lap s,top km/h,off road s,worst slide deg,spins,respawns,furthest m,"
          "01 start-finish s,02 corner-30-banked left s,01 start-finish km/h,02 corner-30-banked left km/h")

ROWS = [
    "2026-11-25 00:35:06,0.0.1+abc,auto,stock,4f918acd,0.50,yes,the stock car,yes,120.05,61.00,59.05,59.05,"
    "140.2,0.00,3,0,0,1280,4.10,6.20,98.0,61.5",
    "2026-11-25 00:35:27,0.0.1+abc,auto,stock,4f918acd,0.55,yes,the stock car,yes,116.91,59.00,57.91,57.91,"
    "141.0,0.00,4,0,0,1280,4.00,6.00,99.0,63.0",
    "2026-11-25 00:36:46,0.0.1+abc,auto,heavy,5d844085,0.50,no,\"Mass 1004 kg (stock 650); Grip 1.4 (stock 1.1)\","
    "no,30.00,,,,95.5,2.50,40,1,0,300,4.50,,90.0,",
    "2026-11-25 00:40:00,0.0.1+abc,you,heavy,5d844085,,yes,\"Mass 1004 kg (stock 650); Grip 1.4 (stock 1.1)\","
    "yes,110.00,55.00,55.00,55.00,150.0,0.10,12,0,0,1280,3.90,5.80,101.0,66.0",
]

CARS = [
    "car,fingerprint,wing,Mass (kg),Grip,Peak slip (°)",
    "stock,4f918acd,base:wing-none,650,1.1,8",
    "heavy,5d844085,base:wing-none,1004,1.4,13.46",
]


@pytest.fixture
def folder(tmp_path: Path) -> Path:
    write_sheet(tmp_path / "Shakedown 8a6323aa.csv", [HEADER] + ROWS)
    write_sheet(tmp_path / "cars.csv", CARS)
    return tmp_path
