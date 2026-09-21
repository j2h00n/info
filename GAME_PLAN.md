# 게임 개발 계획서

Unity로 만든 3D 플래피버드류 게임(새가 전진하며 기둥 장애물을 피하는 러너)을
Python(Panda3D)으로 포팅 + 웹캠 모션 조작 기능을 추가하는 프로젝트.

---

## 1단계 — 분석 및 설계

- Unity 원본 프로젝트(`Assets/`) 구조 전수 조사: 씬 파일(`SampleScene.unity`) 파싱, 커스텀 스크립트 6개
  (`BirdController`, `BoundaryWall`, `FollowEnvironment`, `MotionController`, `NewMonoBehaviourScript`,
  `PythonLauncher`) 로직 확인
- 물리 수치(중력, 점프력, 이동속도, 틸트각), 카메라(FOV/오프셋), UI 레이아웃(RectTransform 값),
  오디오 클립 실제 파일 특정
- 벽 생성 로직이 `BoundaryWall.cs`가 아니라 `NewMonoBehaviourScript.cs`에 있다는 점 등 원본 구조의
  함정 파악
- 포팅 대상 스택 결정: Python + Panda3D(3D 렌더링/물리 직접 구현) + OpenCV/MediaPipe(모션 인식)
- 라이선스 경계 확정: Unity 유료 애셋(BOXOPHOBIC, Mellow Fox, SailCharacterPack 등)은 그대로 이식
  불가 — 필요한 배경/장식은 별도 CC0(퍼블릭도메인급) 애셋으로 대체

## 2단계 — 핵심 게임로직 이식

- `bird.py`: 중력/점프/전진/틸트 물리, 점수·최고기록 계산 (Panda3D 비의존 순수 로직)
- `wall.py`: 컬럼형 파이프 장애물 스포너(랜덤 틈 위치), 청크 단위 생성/제거
- `boundary.py`: 좌우 이동범위 clamp + 경계 쉴드 페이드
- `geometry.py`: 프리미티브(박스) 즉석 생성 유틸 — 외부 모델 없이 파이프/쉴드/바닥에 사용
- `seagull_loader.py`: 원본 새 모델(FBX, ASCII 6.1 구버전 포맷) 직접 파싱해서 지오메트리로 재구성,
  날개/몸통 그룹 분리
- `prefs.py`: Unity `PlayerPrefs` 대체(JSON 파일 기반 저장)
- `main.py`: Panda3D `ShowBase` 앱 — 씬/조명/카메라/입력/게임루프 통합

## 3단계 — 비주얼/오디오/UI 통합

- 카메라 구도(FOV, 위치, 각도) 원본 값 반영 후 실제 플레이 화면에 맞게 재조정(3D감, 시야각)
- UI: 시작화면(고득점 표시, 음량 슬라이더, 모션조작 토글, 시작버튼), 인게임 HUD(점수/기둥 통과수),
  재시작 버튼 — 원본 RectTransform 좌표를 Panda 좌표계로 환산해 배치
- 오디오: 점프 효과음 + 배경음악 연동, 볼륨 설정 저장
- 배경 장식: CC0 라이선스 3D 애셋(풀/나무/구름)으로 절차적 배치, 순수 장식용 바닥 추가
- 쉴드 이펙트: 무료 텍스처 입혀서 근접 구현

## 4단계 — 모션 인식 시스템

- 1차: 배경제거(MOG2) 기반 움직임 추적 → 부정확해서 폐기
- 2차: MediaPipe Pose 도입 — 코/귀/손목 랜드마크로 정밀 추적
- 조향 방식을 "머리 위치" → "머리 기울기 각도"(귀-귀 선 각도)로 재설계
- 점프 제스처를 "머리 Y속도 스파이크" → "머리 옆 손 파닥임(방향전환 감지)"으로 재설계
- Python 게임 내장형(`motion_capture.py`, 별도 프로세스/UDP 없이 게임 창 안에 웹캠 미리보기 통합)
- Unity 연동용 독립 송신기(`motion_sender.py`) + Unity `MotionController.cs`를 새 프로토콜
  (`x,y,angleDeg,jump`)에 맞게 수정 — Unity 원본에서도 동일한 모션 조작 사용 가능하게 함

## 5단계 — 밸런싱, 실험모드, 마무리

- 난이도 파라미터(벽 간격, 틈 크기, 전진속도) 시행착오 튜닝 — 화면 프레이밍(가로 기둥 수,
  세로/가로 비율)에 맞춰 레이아웃 재조정
- 저공비행으로 장애물 밑을 그냥 통과하는 버그 발견 및 바닥 충돌판정 추가로 수정
- 실험 모드(`experiment.py`): 동일 로직 재사용, 난이도만 낮춘 별도 진입점 — 초심자/테스트용
- 좌우 반전, 물리 방향 부호 등 자잘한 조작 버그 수정
- 웹캠 백엔드/코덱 이슈, 좀비 프로세스로 인한 카메라 점유 등 환경 이슈 트러블슈팅
- Git 브랜치(`kang-ji-hun`)에 단계별 커밋/푸시로 진행상황 기록
