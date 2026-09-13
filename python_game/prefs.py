"""Unity PlayerPrefs 대체: JSON 파일 기반 로컬 저장소."""
import json
import os

_PATH = os.path.join(os.path.dirname(__file__), "save.json")
_data = {}


def _load():
    global _data
    if os.path.exists(_PATH):
        try:
            with open(_PATH, "r", encoding="utf-8") as f:
                _data = json.load(f)
        except Exception:
            _data = {}
    else:
        _data = {}


def save():
    with open(_PATH, "w", encoding="utf-8") as f:
        json.dump(_data, f)


def get_int(key, default=0):
    return int(_data.get(key, default))


def set_int(key, value):
    _data[key] = int(value)


def get_float(key, default=0.0):
    return float(_data.get(key, default))


def set_float(key, value):
    _data[key] = float(value)


_load()
