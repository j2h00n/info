"""Unity용 독립 실행 모션 송신기. 웹캠+MediaPipe Pose로 머리 각도(조향)와
손 파닥임(점프)을 감지해서 UDP(5052)로 "x,y,angleDeg,jump"를 쏨.

Unity 쪽 Assets/New Folder/MotionController.cs가 이 프로토콜을 받도록
수정돼있음. Unity 프로젝트 실행 전(또는 후) 이 스크립트를 따로 실행해두면 됨:

    python motion_sender.py

python_game/motion_capture.py와 감지 로직은 같음(Panda3D 게임에 내장된 버전의
텍스처 미리보기 대신 여기선 cv2.imshow 창으로 미리보기를 띄움).
"""
import math
import socket
import sys
import time

import cv2
import mediapipe as mp

UDP_IP = "127.0.0.1"  # 같은 PC의 Unity로 전송 (다른 PC로 보내려면 그 PC IP로 변경)
UDP_PORT = 5052       # Unity MotionController.cs의 수신 포트와 반드시 같아야 함

# 파닥임(점프) 감지 파라미터 - 손목 수직 속도(정규화좌표/초) 기준 엣지 트리거.
# 오탐 줄이려고 구조적으로 두 가지를 더 걸음:
#  1) 스무딩: 프레임 하나짜리 순간속도는 mediapipe 랜드마크 지터에 민감해서, 지수이동평균
#     (EMA)으로 눌러 진짜 손 움직임만 남김.
#  2) 양손 동시성: 한쪽 손목만 빨리 움직였다고 인정 안 함. 실제 날갯짓은 보통 양팔이 같이
#     움직이니까, 두 손목이 SYNC_TOLERANCE 안에서 "같이" 빨라졌을 때만 파닥으로 인정.
# 시간 기반 쿨다운 대신 히스테리시스(armed/triggered)로 "파닥 한번 = 점프 한번"을 보장:
# 속도가 FLAP_VELOCITY를 넘는 "순간"에만 트리거되고, RESET_VELOCITY 밑으로 손이
# 다시 가라앉기 전까진 재무장 안 됨 - 손이 빠르게 계속 흔들려도 중복 점프 안 남.
FLAP_VELOCITY = 1.0        # 스무딩된 속도(/초) 기준 - 원값 1.3에서 낮춤(EMA가 피크를 눌러서)
FLAP_RESET_VELOCITY = 0.4  # 이 밑으로 속도가 떨어져야 다음 파닥을 다시 받아들임
REARM_QUIET_SEC = 0.2      # RESET_VELOCITY 밑으로 "이 시간 동안 계속" 있어야 재무장 -
                           # 한 프레임만 잠깐 느려져도(랜드마크 지터) 바로 재무장되던 것 방지
SPEED_SMOOTH_RATE = 15.0   # 속도 EMA 수렴 속도(초당 비율)
SYNC_TOLERANCE = 0.15      # 두 손목이 "동시"로 인정되는 최대 시간차(초)

# 머리 기울기(각도) 조향 파라미터
MAX_TILT_DEG = 20.0   # Unity MotionController.cs의 maxTiltDeg와 맞출 것
INVERT_TILT = True
ANGLE_SMOOTH_RATE = 8.0


def update_wrist_speed(y, prev_y, speed, dt):
    """(new_speed, new_prev_y) 반환 - 수직 속도(위로 +)를 EMA로 스무딩.
    부호 있는 속도를 씀 - 절대값만 보면 파닥의 "올라감"/"내려감" 구간이 둘 다
    트리거돼서 한번 파닥에 점프가 두번 남. 올라가는 방향(양수)만 트리거로 씀."""
    if prev_y is None or dt <= 0:
        return 0.0, y
    raw_speed = (prev_y - y) / dt  # mediapipe y는 아래로 갈수록 커짐 -> 위로 갈 때 양수
    speed += (raw_speed - speed) * min(1.0, dt * SPEED_SMOOTH_RATE)
    return speed, y


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)  # UDP 소켓(연결 없이 패킷만 전송)

    mp_pose = mp.solutions.pose
    mp_drawing = mp.solutions.drawing_utils
    pose = mp_pose.Pose(model_complexity=0, min_detection_confidence=0.3, min_tracking_confidence=0.3)

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("웹캠을 열 수 없음")
        sys.exit(1)

    # 상태 변수들 - python_game/motion_capture.py의 MotionCapture 멤버와 1:1 대응
    head_angle_deg = 0.0
    left_prev_y, right_prev_y = None, None
    left_speed, right_speed = 0.0, 0.0
    left_high_at, right_high_at = None, None
    left_active_at, right_active_at = None, None
    armed = True
    last_t = time.time()

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                time.sleep(0.05)
                continue

            # dt = 직전 프레임과의 시간차(초). 0으로 나누기 방지로 최소 1ms 보장
            now = time.time()
            dt = max(1e-3, now - last_t)
            last_t = now

            frame = cv2.flip(frame, 1)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = pose.process(rgb)

            jump_flag = 0
            head_x, head_y = 0.5, 0.5

            if results.pose_landmarks:
                h, w = frame.shape[:2]
                lm = results.pose_landmarks.landmark
                nose = lm[mp_pose.PoseLandmark.NOSE]
                left_ear = lm[mp_pose.PoseLandmark.LEFT_EAR]
                right_ear = lm[mp_pose.PoseLandmark.RIGHT_EAR]
                left_wrist = lm[mp_pose.PoseLandmark.LEFT_WRIST]
                right_wrist = lm[mp_pose.PoseLandmark.RIGHT_WRIST]

                head_x, head_y = nose.x, 1.0 - nose.y

                dx = (left_ear.x - right_ear.x) * w
                dy = (left_ear.y - right_ear.y) * h
                if abs(dx) > 1e-3 or abs(dy) > 1e-3:
                    raw_angle = math.degrees(math.atan2(dy, dx))
                    head_angle_deg += (raw_angle - head_angle_deg) * min(1.0, dt * ANGLE_SMOOTH_RATE)

                # 손목 속도 처리: python_game/motion_capture.py의 update()와 동일 로직
                # (visibility>0.4면 속도 갱신 -> FLAP_VELOCITY 넘으면 high_at, RESET_VELOCITY 넘으면 active_at 기록)
                if left_wrist.visibility > 0.4:
                    left_speed, left_prev_y = update_wrist_speed(left_wrist.y, left_prev_y, left_speed, dt)
                    if left_speed > FLAP_VELOCITY:
                        left_high_at = now
                    if abs(left_speed) > FLAP_RESET_VELOCITY:
                        left_active_at = now
                else:
                    left_prev_y, left_speed = None, 0.0
                    left_high_at, left_active_at = None, None

                if right_wrist.visibility > 0.4:
                    right_speed, right_prev_y = update_wrist_speed(right_wrist.y, right_prev_y, right_speed, dt)
                    if right_speed > FLAP_VELOCITY:
                        right_high_at = now
                    if abs(right_speed) > FLAP_RESET_VELOCITY:
                        right_active_at = now
                else:
                    right_prev_y, right_speed = None, 0.0
                    right_high_at, right_active_at = None, None

                # 양손이 SYNC_TOLERANCE 안에서 같이 빨라졌을 때만 파닥 인정, armed일 때 1회만 점프
                both_high = (
                    left_high_at is not None and right_high_at is not None
                    and abs(left_high_at - right_high_at) <= SYNC_TOLERANCE
                    and now - max(left_high_at, right_high_at) <= SYNC_TOLERANCE
                )
                if armed and both_high:
                    jump_flag = 1
                    armed = False

                # 재무장은 양손 다 REARM_QUIET_SEC 동안 계속 조용해야만
                left_quiet = left_active_at is None or now - left_active_at >= REARM_QUIET_SEC
                right_quiet = right_active_at is None or now - right_active_at >= REARM_QUIET_SEC
                if left_quiet and right_quiet:
                    armed = True

                mp_drawing.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)

            sign = -1.0 if INVERT_TILT else 1.0
            move_x = max(-1.0, min(1.0, (head_angle_deg / MAX_TILT_DEG) * sign))

            # 프로토콜: "코x,코y,머리각도,점프(0/1)" 문자열 한 줄을 매 프레임 전송
            msg = f"{head_x:.4f},{head_y:.4f},{head_angle_deg:.2f},{jump_flag}".encode("utf-8")
            sock.sendto(msg, (UDP_IP, UDP_PORT))

            status = "JUMP!" if jump_flag else "tracking"
            cv2.putText(frame, f"{status} angle={head_angle_deg:+.1f} move_x={move_x:+.2f}",
                        (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            cv2.putText(frame, f"L_spd={left_speed:.2f} R_spd={right_speed:.2f}",
                        (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 200, 0), 2)
            cv2.imshow("motion sender -> Unity (q to close)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

            time.sleep(1 / 30.0)  # 전송 속도 제한(약 30회/초). 줄이면 CPU 사용량 증가
    except KeyboardInterrupt:
        pass
    finally:
        pose.close()
        cap.release()
        cv2.destroyAllWindows()
        sock.close()


if __name__ == "__main__":
    main()
