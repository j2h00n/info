"""웹캠으로 얼굴(머리) 위치를 추적해 UDP(5052)로 정규화 좌표(headX,headY)를 쏘는 독립 프로세스.

MotionController(Unity C# 원본)가 기대하던 프로토콜과 동일:
  "x,y" (0.0~1.0 정규화, CultureInfo.InvariantCulture 상당의 '.' 소수점) 텍스트를 UDP로 전송.
"""
import socket
import sys
import time

import cv2

UDP_IP = "127.0.0.1"
UDP_PORT = 5052


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
    face_cascade = cv2.CascadeClassifier(cascade_path)

    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("웹캠을 열 수 없음")
        sys.exit(1)

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                time.sleep(0.05)
                continue

            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            h, w = gray.shape
            faces = face_cascade.detectMultiScale(gray, scaleFactor=1.2, minNeighbors=5, minSize=(60, 60))

            if len(faces) > 0:
                fx, fy, fw, fh = max(faces, key=lambda f: f[2] * f[3])
                center_x = fx + fw / 2.0
                center_y = fy + fh / 2.0
                head_x = center_x / w
                head_y = 1.0 - (center_y / h)  # 위로 올릴수록 값 커지도록 반전

                msg = f"{head_x:.4f},{head_y:.4f}".encode("utf-8")
                sock.sendto(msg, (UDP_IP, UDP_PORT))

            time.sleep(1 / 30.0)
    except KeyboardInterrupt:
        pass
    finally:
        cap.release()
        sock.close()


if __name__ == "__main__":
    main()
