"""웹캠+MediaPipe Pose를 게임 프로세스 안에서 직접 처리 (별도 프로세스/UDP 없음).

MotionController+motion_tracker.py(UDP 기반)의 후속 버전. 웹캠 창을 따로
띄우지 않고, 매 프레임 결과를 Panda3D 텍스처로 변환해서 게임 창 한쪽에
미리보기로 보여줄 수 있게 최근 프레임(BGR numpy 배열)도 함께 반환한다.
"""
import math
import time

import cv2
import mediapipe as mp

# 파닥임(점프) 감지 파라미터 - 손목 수직 속도(정규화좌표/초) 기준 엣지 트리거.
# 오탐 줄이려고 구조적으로 두 가지를 더 걸음:
#  1) 스무딩: 프레임 하나짜리 순간속도는 mediapipe 랜드마크 지터에 민감해서, 지수이동평균
#     (EMA)으로 눌러 진짜 손 움직임만 남김.
#  2) 양손 동시성: 한쪽 손목만 빨리 움직였다고 인정 안 함. 실제 날갯짓은 보통 양팔이 같이
#     움직이니까, 두 손목이 SYNC_TOLERANCE 안에서 "같이" 빨라졌을 때만 파닥으로 인정 -
#     한쪽 팔만 우연히 휙 움직인 오탐(물건 집기, 긁기 등)이 크게 줄어듦.
# 시간 기반 쿨다운 대신 히스테리시스(armed/triggered)로 "파닥 한번 = 점프 한번"을 보장:
# 속도가 FLAP_VELOCITY를 넘는 "순간"에만 트리거되고, RESET_VELOCITY 밑으로 손이
# 다시 가라앉기 전까진 재무장 안 됨 - 손이 빠르게 계속 흔들려도 중복 점프 안 남.
FLAP_VELOCITY = 1.0        # 스무딩된 속도(/초) 기준 - 원값 1.3에서 낮춤(EMA가 피크를 눌러서)
FLAP_RESET_VELOCITY = 0.4  # 이 밑으로 속도가 떨어져야 다음 파닥을 다시 받아들임
REARM_QUIET_SEC = 0.2      # RESET_VELOCITY 밑으로 "이 시간 동안 계속" 있어야 재무장 -
                           # 한 프레임만 잠깐 느려져도(랜드마크 지터) 바로 재무장되던 것 방지
SPEED_SMOOTH_RATE = 15.0   # 속도 EMA 수렴 속도(초당 비율) - 크면 원속도에 가깝고 작으면 더 뭉갬
SYNC_TOLERANCE = 0.15      # 두 손목이 "동시"로 인정되는 최대 시간차(초)

# 머리 기울기(각도) 조향 파라미터 - 귀-귀 선의 수평 대비 각도를 씀
MAX_TILT_DEG = 20.0   # 이만큼 기울이면 조향값 -1/1(최대)
INVERT_TILT = True    # 반대로 느껴지면 True로 (벡터 방향 바꿀 때마다 부호 재확인 필요)
ANGLE_SMOOTH_RATE = 8.0  # 각도값 튀는 거 완화(초당 이 비율로 목표각에 수렴)


class MotionCapture:
    def __init__(self, camera_index=0):
        self._camera_index = camera_index  # 웹캠 번호(0=기본 카메라). 카메라가 여러 개면 1,2로 변경
        self._cap = None    # cv2.VideoCapture 객체 (start()에서 생성, 꺼져있으면 None)
        self._pose = None   # MediaPipe Pose 추론기
        self._mp_pose = mp.solutions.pose
        self._mp_drawing = mp.solutions.drawing_utils

        # ----- 외부(main.py)에서 읽는 결과값 -----
        self.head_x = 0.5            # 코 x좌표(0~1 정규화, 좌->우)
        self.head_y = 0.5            # 코 y좌표(0~1, 아래->위로 뒤집음)
        self.head_angle_deg = 0.0    # 스무딩된 머리 기울기 각도(도) - 조향의 원천값
        self.last_frame = None       # 스켈레톤이 그려진 최근 BGR 프레임 (미리보기용)
        self.pose_found = False      # 이번 프레임에 사람이 감지됐는지

        # ----- 파닥 감지용 내부 상태 (손목별) -----
        self._left_prev_y = None     # 직전 프레임 손목 y (속도 계산용)
        self._right_prev_y = None
        self._left_speed = 0.0       # EMA로 스무딩된 손목 수직속도(위로 +)
        self._right_speed = 0.0
        self._left_high_at = None   # 마지막으로 속도가 임계값 넘었던 시각
        self._right_high_at = None
        self._left_active_at = None  # 마지막으로 |속도|가 RESET_VELOCITY 넘었던 시각
        self._right_active_at = None
        self._armed = True           # True일 때만 새 파닥을 인정(히스테리시스 래치)

    def start(self):
        """웹캠과 MediaPipe 모델을 켬. 이미 켜져있으면 무시."""
        if self._cap is not None:
            return
        # 주의: CAP_DSHOW/MJPG 강제 지정 금지 (이 PC에선 화면이 깨짐 - CLAUDE.md 참고)
        self._cap = cv2.VideoCapture(self._camera_index)
        # model_complexity: 0(가장 빠름/덜 정확) ~ 2(느림/정확). confidence를 낮추면 더 잘 잡지만 오탐 증가
        self._pose = self._mp_pose.Pose(
            model_complexity=0, min_detection_confidence=0.3, min_tracking_confidence=0.3
        )

    def stop(self):
        """웹캠/모델 해제. 모션 모드를 끌 때 호출."""
        if self._cap is not None:
            self._cap.release()
            self._cap = None
        if self._pose is not None:
            self._pose.close()
            self._pose = None
        self.last_frame = None

    def _update_wrist_speed(self, y, prev_y, speed, dt):
        """(new_speed, new_prev_y) 반환 - 수직 속도(위로 +)를 EMA로 스무딩.
        부호 있는 속도를 씀 - 파닥 한번은 "올라감"과 "내려감" 두 구간을 다 가지는데,
        절대값으로만 보면 두 구간이 둘 다 트리거돼서 한번 파닥에 점프가 두번 남.
        올라가는 방향(양수)만 트리거 조건으로 쓰고, 재무장 판정에는 절대값을 씀."""
        if prev_y is None or dt <= 0:
            return 0.0, y
        raw_speed = (prev_y - y) / dt  # mediapipe y는 아래로 갈수록 커짐 -> 위로 갈 때 양수
        speed += (raw_speed - speed) * min(1.0, dt * SPEED_SMOOTH_RATE)
        return speed, y

    def update(self, learning_rate, dt):
        """매 프레임 호출. (jumped: bool)을 반환하고 self.head_x/head_y/last_frame 갱신.
        learning_rate 인자는 호환용이며 현재 로직에선 쓰지 않음. dt=지난 프레임 시간(초)."""
        if self._cap is None:
            return False

        ret, frame = self._cap.read()
        if not ret:
            return False

        frame = cv2.flip(frame, 1)  # 거울처럼 좌우 반전 (내가 오른쪽으로 기울이면 화면에서도 오른쪽)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)  # MediaPipe는 RGB 입력을 요구
        results = self._pose.process(rgb)

        now = time.time()
        jumped = False
        self.pose_found = bool(results.pose_landmarks)

        if results.pose_landmarks:
            h, w = frame.shape[:2]
            lm = results.pose_landmarks.landmark
            # 사용하는 랜드마크: 코, 양쪽 귀(조향), 양쪽 손목(파닥)
            nose = lm[self._mp_pose.PoseLandmark.NOSE]
            left_ear = lm[self._mp_pose.PoseLandmark.LEFT_EAR]
            right_ear = lm[self._mp_pose.PoseLandmark.RIGHT_EAR]
            left_wrist = lm[self._mp_pose.PoseLandmark.LEFT_WRIST]
            right_wrist = lm[self._mp_pose.PoseLandmark.RIGHT_WRIST]

            self.head_x = nose.x
            self.head_y = 1.0 - nose.y

            # 귀-귀 선의 기울기 각도를 조향각으로 씀. 정규화좌표(0~1)를 그대로 쓰면
            # 가로세로 비율이 달라 각도가 왜곡되니 픽셀단위로 변환.
            # (left-right 순서로 빼면 수평일 때 atan2가 180이 나와서 반대로 뺌: 0이 나오게)
            # dx, dy = 오른쪽 귀 -> 왼쪽 귀 벡터(픽셀). atan2(dy, dx) = 이 벡터의 수평 대비 각도(도).
            # 그 뒤 EMA(지수이동평균)로 각도를 부드럽게 따라가게 해서 떨림을 줄임
            dx = (left_ear.x - right_ear.x) * w
            dy = (left_ear.y - right_ear.y) * h
            if abs(dx) > 1e-3 or abs(dy) > 1e-3:
                raw_angle = math.degrees(math.atan2(dy, dx))
                self.head_angle_deg += (raw_angle - self.head_angle_deg) * min(1.0, dt * ANGLE_SMOOTH_RATE)

            # 파닥 감지는 머리 기준 안 씀 - 손목이 카메라에 잡히기만 하면 위아래 흔들림을
            # 그대로 판정에 씀 (손이 코 근처인지, 고개를 기울이고 있는지는 안 봄)
            # visibility(0~1) > 0.4: 손목이 화면에 충분히 보일 때만 사용. 낮추면 가려진 손도 쓰지만 지터 증가
            if left_wrist.visibility > 0.4:
                # 1) 이번 프레임 손목 y로 스무딩된 수직속도 갱신
                self._left_speed, self._left_prev_y = self._update_wrist_speed(
                    left_wrist.y, self._left_prev_y, self._left_speed, dt
                )
                # 2) 위로 FLAP_VELOCITY보다 빠르면 "빨랐던 시각" 기록 (아래 양손 동시성 판정에 사용)
                if self._left_speed > FLAP_VELOCITY:
                    self._left_high_at = now
                # 3) 위/아래 상관없이 RESET_VELOCITY보다 빠르면 "움직이는 중" 시각 기록 (재무장 판정에 사용)
                if abs(self._left_speed) > FLAP_RESET_VELOCITY:
                    self._left_active_at = now
            else:
                # 손목이 안 보이면 기록 전부 초기화 (오래된 값으로 오판하는 것 방지)
                self._left_prev_y, self._left_speed = None, 0.0
                self._left_high_at, self._left_active_at = None, None

            # 오른쪽 손목도 위와 동일한 처리
            if right_wrist.visibility > 0.4:
                self._right_speed, self._right_prev_y = self._update_wrist_speed(
                    right_wrist.y, self._right_prev_y, self._right_speed, dt
                )
                if self._right_speed > FLAP_VELOCITY:
                    self._right_high_at = now
                if abs(self._right_speed) > FLAP_RESET_VELOCITY:
                    self._right_active_at = now
            else:
                self._right_prev_y, self._right_speed = None, 0.0
                self._right_high_at, self._right_active_at = None, None

            # 양손 동시성: 둘 다 최근 SYNC_TOLERANCE 안에서 빨랐어야 파닥으로 인정
            # 조건 3개: (a) 양손 다 기록 있음 (b) 두 손의 시각 차이가 허용치 이내 (c) 그 시각이 아직 "방금"임
            both_high = (
                self._left_high_at is not None and self._right_high_at is not None
                and abs(self._left_high_at - self._right_high_at) <= SYNC_TOLERANCE
                and now - max(self._left_high_at, self._right_high_at) <= SYNC_TOLERANCE
            )
            # 무장 상태(_armed)일 때만 점프 발생 -> 곧바로 해제해서 같은 파닥이 중복 점프되지 않게 함
            if self._armed and both_high:
                jumped = True
                self._armed = False

            # 재무장은 양손 다 REARM_QUIET_SEC 동안 "계속" 조용해야만 - 순간적인 속도
            # 저점(랜드마크 지터)으로 바로 재무장돼서 한 파닥에 두번 튀는 것 방지
            left_quiet = self._left_active_at is None or now - self._left_active_at >= REARM_QUIET_SEC
            right_quiet = self._right_active_at is None or now - self._right_active_at >= REARM_QUIET_SEC
            if left_quiet and right_quiet:
                self._armed = True

            # 미리보기 프레임에 스켈레톤(관절/뼈대) 그리기
            self._mp_drawing.draw_landmarks(frame, results.pose_landmarks, self._mp_pose.POSE_CONNECTIONS)

        # 미리보기 왼쪽 위에 디버그 텍스트 3줄: 상태 / 각도+조향값 / 손목 속도
        # (파닥 감도 튜닝할 때 L_spd/R_spd 값을 보고 FLAP_VELOCITY를 조절하면 편함)
        status = "JUMP!" if jumped else ("tracking" if self.pose_found else "no pose")
        cv2.putText(frame, status, (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
        cv2.putText(
            frame, f"angle={self.head_angle_deg:+.1f} move_x={self.get_move_x():+.2f}",
            (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2,
        )
        cv2.putText(
            frame, f"L_spd={self._left_speed:.2f} R_spd={self._right_speed:.2f}",
            (10, 75), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 200, 0), 2,
        )

        self.last_frame = frame
        return jumped

    def get_move_x(self):
        """머리 각도 -> 조향값(-1~1). MAX_TILT_DEG만큼 기울이면 최대. 부호 규칙은 CONVENTIONS.md 참고."""
        sign = -1.0 if INVERT_TILT else 1.0
        move_x = (self.head_angle_deg / MAX_TILT_DEG) * sign
        return max(-1.0, min(1.0, move_x))
