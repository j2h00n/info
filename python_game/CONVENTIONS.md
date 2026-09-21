# 좌표계 / 부호 컨벤션

버그 재발(좌우 반전 3연속, 각도 랜드마크 왔다갔다) 방지용 참조 문서.
헷갈리는 부호/좌표 바꾸기 전에 여기부터 확인할 것.

## 월드 좌표 ↔ Panda3D 좌표 매핑

Unity(X=좌우, Y=높이, Z=전진) 기준 변수명을 그대로 쓰되, Panda3D NodePath에는
`setPos(x, z, y)` 순서로 넣는다 (Panda는 Y가 전진, Z가 높이라서 뒤바뀜).

```
world X (좌우)   -> Panda X  (그대로)
world Y (높이)   -> Panda Z
world Z (전진)   -> Panda Y
```

`main.py` 전역에서 `node.setPos(x, z, y)` 패턴 - 순서 절대 착각 금지.

## 조향 부호 체인 (헷갈렸던 부분)

머리 기울기 → move_x → roll → flap 방향, 이 4단계 부호가 전부 이어져 있음.
하나 바꾸면 나머지 다 같이 확인해야 함.

1. **귀 각도 계산** (`motion_capture.py`, `motion_sender.py`):
   `dx = left_ear.x - right_ear.x`, `dy = left_ear.y - right_ear.y` (반드시 left - right 순서.
   right - left로 하면 수평 상태가 180도로 잡히는 버그 재현됨 - 실제 겪었음).
2. **`get_move_x()`**: `INVERT_TILT = True` 로 고정. 이유: 머리를 오른쪽으로 기울이면
   캐릭터가 오른쪽으로 가야 하는데, 원시 각도 부호가 반대라서 뒤집어야 함.
3. **키보드 매핑** (`main.py`): `move_x = +1.0 (left 키) / -1.0 (right 키)`.
   직관과 반대로 보이지만, roll 각도 정의(`target_roll = -move_x * TILT_ANGLE`, `bird.py`)와
   맞물려서 이렇게 둬야 결과적으로 왼쪽 키 = 왼쪽 이동이 됨.
4. **flap 방향** (`bird.py jump()`): `dir_x = math.sin(roll_rad)` (부호 이미 한 번 뒤집힌 상태).
   roll이 양수(오른쪽으로 기움)일 때 flap도 오른쪽 성분을 가져야 시각적 기울기와 일치.

**부호 하나라도 바꿀 일 생기면**: 1→2→3→4 순서로 전부 다시 타보고, 실제 실행해서
"왼쪽 보면 왼쪽으로 간다"를 직접 확인 후 커밋. 코드만 보고 부호 판단하지 말 것 -
이 체인은 과거 세 번 다 코드상으로는 맞아 보였는데 실제로는 반대였음.

## 상수 하나 바꾸면 같이 확인해야 하는 것들 (`wall.py`)

`PILLAR_HEIGHT`/`GAP_POS_MIN`/`GAP_POS_MAX`/`BASE_GAP_SIZE` 중 하나라도 바꾸면
`main.py`의 `FLOOR_Y`/`CEILING_Y`도 반드시 재계산할 것 (최악의 gap 위치 기준 역산).
안 하면 저공비행/공중부양 exploit 재발함 - 실제 겪었음.
