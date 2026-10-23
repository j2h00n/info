"""Unity PlayerPrefs 대체: JSON 파일 기반 로컬 저장소."""
import json
import os

# 저장 파일 위치: python_game/save.json (gitignore됨). 지우면 최고기록/설정 초기화.
_PATH = os.path.join(os.path.dirname(__file__), "save.json")
_data = {}  # 메모리 상의 저장 데이터 {키: 값}. set_*는 여기만 바꾸고, save()를 불러야 파일에 기록됨


def _load():
    """모듈 import 시 1회 호출: 파일이 있으면 읽고, 없거나 깨졌으면 빈 dict로 시작."""
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
    """메모리의 _data를 save.json에 기록."""
    with open(_PATH, "w", encoding="utf-8") as f:
        json.dump(_data, f)


# --- Unity PlayerPrefs.GetInt/SetInt/GetFloat/SetFloat 와 같은 이름/동작 ---
def get_int(key, default=0):
    return int(_data.get(key, default))


def set_int(key, value):
    _data[key] = int(value)


def get_float(key, default=0.0):
    return float(_data.get(key, default))


def set_float(key, value):
    _data[key] = float(value)


_load()
