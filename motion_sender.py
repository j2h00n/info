"""Unity용 독립 실행 모션 송신기. 웹캠+MediaPipe Pose로 머리 각도(조향)와
손 파닥임(점프)을 감지해서 UDP(5052)로 "x,y,angleDeg,jump"를 쏨.

Unity 쪽 Assets/New Folder/MotionController.cs가 이 프로토콜을 받도록
수정돼있음. Unity 프로젝트 실행 전(또는 후) 이 스크립트를 따로 실행해두면 됨:

    python motion_sender.py

python_game/motion_capture.py와 감지 로직은 같음(Panda3D 게임에 내장된 버전의
텍스처 미리보기 대신 여기선 cv2.imshow 창으로 미리보기를 띄움).
"""
import collections
import math
import socket
import sys
import time

import cv2
import mediapipe as mp

UDP_IP = "127.0.0.1"
UDP_PORT = 5052

# 파닥임(점프) 감지 파라미터
FLAP_WINDOW_SEC = 0.3
FLAP_MIN_PEAKS = 1
FLAP_MIN_RANGE = 0.035
NEAR_HEAD_X = 0.35
FLAP_COOLDOWN = 0.2

# 머리 기울기(각도) 조향 파라미터
MAX_TILT_DEG = 20.0   # Unity MotionController.cs의 maxTiltDeg와 맞출 것
INVERT_TILT = True
ANGLE_SMOOTH_RATE = 8.0


def toggles_and_range(hist):
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


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    mp_pose = mp.solutions.pose
    mp_drawing = mp.solutions.drawing_utils
    pose = mp_pose.Pose(model_complexity=0, min_detection_confidence=0.3, min_tracking_confidence=0.3)

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("웹캠을 열 수 없음")
        sys.exit(1)

    head_angle_deg = 0.0
    left_hist, right_hist = [], []
    last_jump_time = 0.0
    last_t = time.time()

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                time.sleep(0.05)
                continue

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

                for wrist, hist in ((left_wrist, left_hist), (right_wrist, right_hist)):
                    near_head = abs(wrist.x - nose.x) < NEAR_HEAD_X and wrist.y < nose.y + 0.45
                    if near_head and wrist.visibility > 0.4:
                        hist.append((now, wrist.y))
                    while hist and now - hist[0][0] > FLAP_WINDOW_SEC:
                        hist.pop(0)

                if now - last_jump_time > FLAP_COOLDOWN:
                    for hist in (left_hist, right_hist):
                        changes, y_range = toggles_and_range(hist)
                        if changes >= FLAP_MIN_PEAKS and y_range >= FLAP_MIN_RANGE:
                            jump_flag = 1
                            last_jump_time = now
                            hist.clear()
                            break

                mp_drawing.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)

            sign = -1.0 if INVERT_TILT else 1.0
            move_x = max(-1.0, min(1.0, (head_angle_deg / MAX_TILT_DEG) * sign))

            msg = f"{head_x:.4f},{head_y:.4f},{head_angle_deg:.2f},{jump_flag}".encode("utf-8")
            sock.sendto(msg, (UDP_IP, UDP_PORT))

            status = "JUMP!" if jump_flag else "tracking"
            cv2.putText(frame, f"{status} angle={head_angle_deg:+.1f} move_x={move_x:+.2f}",
                        (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            cv2.imshow("motion sender -> Unity (q to close)", frame)
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

            time.sleep(1 / 30.0)
    except KeyboardInterrupt:
        pass
    finally:
        pose.close()
        cap.release()
        cv2.destroyAllWindows()
        sock.close()


if __name__ == "__main__":
    main()
