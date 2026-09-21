"""웹캠+MediaPipe Pose를 게임 프로세스 안에서 직접 처리 (별도 프로세스/UDP 없음).

MotionController+motion_tracker.py(UDP 기반)의 후속 버전. 웹캠 창을 따로
띄우지 않고, 매 프레임 결과를 Panda3D 텍스처로 변환해서 게임 창 한쪽에
미리보기로 보여줄 수 있게 최근 프레임(BGR numpy 배열)도 함께 반환한다.
"""
import math
import time

import cv2
import mediapipe as mp

# 파닥임(점프) 감지 파라미터 - 반응속도 우선 튜닝(방향전환 1번만 있어도 인정, 창/쿨다운 단축)
FLAP_WINDOW_SEC = 0.3
FLAP_MIN_PEAKS = 1
FLAP_MIN_RANGE = 0.035
NEAR_HEAD_X = 0.35
FLAP_COOLDOWN = 0.2

# 머리 기울기(각도) 조향 파라미터 - 귀-귀 선의 수평 대비 각도를 씀
MAX_TILT_DEG = 20.0   # 이만큼 기울이면 조향값 -1/1(최대)
INVERT_TILT = True    # 반대로 느껴지면 True로 (벡터 방향 바꿀 때마다 부호 재확인 필요)
ANGLE_SMOOTH_RATE = 8.0  # 각도값 튀는 거 완화(초당 이 비율로 목표각에 수렴)


class MotionCapture:
    def __init__(self, camera_index=0):
        self._camera_index = camera_index
        self._cap = None
        self._pose = None
        self._mp_pose = mp.solutions.pose
        self._mp_drawing = mp.solutions.drawing_utils

        self.head_x = 0.5
        self.head_y = 0.5
        self.head_angle_deg = 0.0
        self.last_frame = None
        self.pose_found = False

        self._left_hist = []   # [(t, y)]
        self._right_hist = []
        self._last_jump_time = 0.0

    def start(self):
        if self._cap is not None:
            return
        self._cap = cv2.VideoCapture(self._camera_index)
        self._pose = self._mp_pose.Pose(
            model_complexity=0, min_detection_confidence=0.3, min_tracking_confidence=0.3
        )

    def stop(self):
        if self._cap is not None:
            self._cap.release()
            self._cap = None
        if self._pose is not None:
            self._pose.close()
            self._pose = None
        self.last_frame = None

    def _toggles_and_range(self, hist):
        ys = [y for _, y in hist]
        if len(ys) < 3:
            return 0, 0.0
        diffs = [ys[i + 1] - ys[i] for i in range(len(ys) - 1)]
        changes, prev_sign = 0, 0
        for d in diffs:
            if abs(d) < 1e-4:
                continue
            sign = 1 if d > 0 else -1
            if prev_sign != 0 and sign != prev_sign:
                changes += 1
            prev_sign = sign
        return changes, (max(ys) - min(ys))

    def update(self, learning_rate, dt):
        """매 프레임 호출. (jumped: bool)을 반환하고 self.head_x/head_y/last_frame 갱신."""
        if self._cap is None:
            return False

        ret, frame = self._cap.read()
        if not ret:
            return False

        frame = cv2.flip(frame, 1)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = self._pose.process(rgb)

        now = time.time()
        jumped = False
        self.pose_found = bool(results.pose_landmarks)

        if results.pose_landmarks:
            h, w = frame.shape[:2]
            lm = results.pose_landmarks.landmark
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
            dx = (left_ear.x - right_ear.x) * w
            dy = (left_ear.y - right_ear.y) * h
            if abs(dx) > 1e-3 or abs(dy) > 1e-3:
                raw_angle = math.degrees(math.atan2(dy, dx))
                self.head_angle_deg += (raw_angle - self.head_angle_deg) * min(1.0, dt * ANGLE_SMOOTH_RATE)

            # 조향이 이제 머리 "위치"가 아니라 "각도"라 고개를 크게 옆으로 움직일 필요가 없고,
            # 그만큼 손이 코 근처에서 벗어날 일도 줄어듦 - near_head 판정을 넉넉하게 둠
            for wrist, hist in ((left_wrist, self._left_hist), (right_wrist, self._right_hist)):
                near_head = abs(wrist.x - nose.x) < NEAR_HEAD_X and wrist.y < nose.y + 0.45
                if near_head and wrist.visibility > 0.4:
                    hist.append((now, wrist.y))
                while hist and now - hist[0][0] > FLAP_WINDOW_SEC:
                    hist.pop(0)

            if now - self._last_jump_time > FLAP_COOLDOWN:
                for hist in (self._left_hist, self._right_hist):
                    changes, y_range = self._toggles_and_range(hist)
                    if changes >= FLAP_MIN_PEAKS and y_range >= FLAP_MIN_RANGE:
                        jumped = True
                        self._last_jump_time = now
                        hist.clear()
                        break

            self._mp_drawing.draw_landmarks(frame, results.pose_landmarks, self._mp_pose.POSE_CONNECTIONS)

        status = "JUMP!" if jumped else ("tracking" if self.pose_found else "no pose")
        cv2.putText(frame, status, (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
        cv2.putText(
            frame, f"angle={self.head_angle_deg:+.1f} move_x={self.get_move_x():+.2f}",
            (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 255), 2,
        )

        self.last_frame = frame
        return jumped

    def get_move_x(self):
        sign = -1.0 if INVERT_TILT else 1.0
        move_x = (self.head_angle_deg / MAX_TILT_DEG) * sign
        return max(-1.0, min(1.0, move_x))
