import cv2
import socket
import os
import sys
import urllib.request
import time
import ctypes
from ctypes import wintypes
from collections import deque

# 웹캠 모션 조작
#  - 고개를 좌우로 움직이면 → 새가 좌우로 이동 (얼굴 위치)
#  - 손을 위아래로 흔들면 → 아래로 내리칠 때마다 날갯짓(점프) 한 번
# 유니티로 "얼굴x,얼굴y,누적날갯짓수,얼굴보임" 을 UDP(포트 5052)로 보냄
# 게임 왼쪽 아래에 띄울 카메라 화면(JPEG)은 UDP 포트 5053으로 보냄 (따로 창은 띄우지 않음)
# 손 인식은 MediaPipe가 있으면 손 위치로, 없으면 화면 속 움직임(얼굴 제외)으로 대신 판단
# 실행: python motion_tracker.py [유니티 프로세스 번호]  → 게임이 꺼지면 같이 꺼짐

HERE = os.path.dirname(os.path.abspath(__file__))
os.chdir(HERE)

# 게임이 창 없이 실행하면 출력 인코딩이 cp949라 이모지/일부 글자에서 바로 죽음 → UTF-8로 바꾸고, 내용은 로그 파일에도 남김
for stream in (sys.stdout, sys.stderr):
    try:
        stream.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass
_log_path = os.path.join(HERE, 'motion_tracker.log')
if os.path.exists(_log_path) and os.path.getsize(_log_path) > 512 * 1024:
    os.remove(_log_path)  # 너무 커지면 새로 시작
_log = open(_log_path, 'a', encoding='utf-8')  # 다시 켜져도 이전 기록이 남도록 이어 쓰기
_log.write(f"\n===== 트래커 시작 {time.strftime('%Y-%m-%d %H:%M:%S')} =====\n")
_print = print


def print(*args, **kwargs):
    text = ' '.join(str(a) for a in args)
    _log.write(text + '\n')
    _log.flush()
    try:
        _print(text, **kwargs)
    except Exception:
        pass


sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
# 윈도우: 게임이 재시작하며 포트를 잠깐 닫은 순간에 보내면 다음 전송이 "연결 재설정(10054)" 에러로 터짐 → 끔
try:
    sock.ioctl(socket.SIO_UDP_CONNRESET, False)
except (AttributeError, OSError):
    pass
serverAddressPort = ("127.0.0.1", 5052)
previewAddressPort = ("127.0.0.1", 5053)


def send(data, addr):
    """게임으로 보내기. 게임이 잠깐 안 받는 중이어도 트래커가 죽지 않게 에러는 무시"""
    try:
        sock.sendto(data, addr)
    except OSError:
        pass

# 게임(부모 프로세스)이 꺼졌는지 확인 → 카메라를 계속 붙잡고 있지 않게
parent_pid = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 0
# 메뉴에서 고른 카메라 이름. 없거나 AUTO면 0~2번 중 화면이 들어오는 걸 자동으로
camera_name = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] != "AUTO" else ""


def parent_alive():
    if parent_pid == 0:
        return True
    try:
        import ctypes
        k32 = ctypes.windll.kernel32
        h = k32.OpenProcess(0x1000, False, parent_pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return False
        code = ctypes.c_ulong()
        k32.GetExitCodeProcess(h, ctypes.byref(code))
        k32.CloseHandle(h)
        return code.value == 259  # STILL_ACTIVE
    except Exception:
        return True


def ensure_file(path, url, label):
    if os.path.exists(path):
        return True
    try:
        print(f"{label} 다운로드 중...")
        urllib.request.urlretrieve(url, path)
        print("다운로드 완료!")
        return True
    except Exception as e:
        print(f"⚠️ {label} 다운로드 실패: {e}")
        return False


# 얼굴 인식 모델 (OpenCV 기본)
xml_path = 'haarcascade_frontalface_default.xml'
ensure_file(xml_path, 'https://raw.githubusercontent.com/opencv/opencv/master/data/haarcascades/haarcascade_frontalface_default.xml', "얼굴 인식 모델")
face_cascade = cv2.CascadeClassifier(xml_path)

# 손 인식 모델 (MediaPipe, 설치돼 있을 때만)
hand_detector = None
try:
    import mediapipe as mp
    from mediapipe.tasks.python import vision, BaseOptions
    task_path = 'hand_landmarker.task'
    if ensure_file(task_path, 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task', "손 인식 모델"):
        options = vision.HandLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=task_path),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=2,
            min_hand_detection_confidence=0.6,
            min_hand_presence_confidence=0.6,
            min_tracking_confidence=0.5)
        hand_detector = vision.HandLandmarker.create_from_options(options)
        print("✅ MediaPipe 손 인식 사용")
except Exception as e:
    print(f"⚠️ MediaPipe 없음 → 움직임 감지로 손 흔들기 판단 ({e})")

# 얼굴 인식 (MediaPipe): OpenCV 기본 얼굴 인식보다 어두운 곳·고개 돌림에 훨씬 강함. 없으면 OpenCV로
face_detector = None
if hand_detector is not None:
    try:
        face_path = 'blaze_face_short_range.tflite'
        if ensure_file(face_path, 'https://storage.googleapis.com/mediapipe-models/face_detector/blaze_face_short_range/float16/latest/blaze_face_short_range.tflite', "얼굴 인식 모델"):
            face_detector = vision.FaceDetector.create_from_options(vision.FaceDetectorOptions(
                base_options=BaseOptions(model_asset_path=face_path),
                running_mode=vision.RunningMode.VIDEO,
                min_detection_confidence=0.5))
            print("✅ MediaPipe 얼굴 인식 사용")
    except Exception as e:
        print(f"⚠️ MediaPipe 얼굴 인식 실패 → OpenCV 얼굴 인식 사용 ({e})")


class FlapDetector:
    """손 위치(x, y: 0=화면 위, 1=아래)를 받아, 짧은 시간 안에 '확실히 아래로' 내리쳤을 때만 날갯짓 한 번.
    - 충분히 크게 내려가야 함 (DOWN)
    - 옆으로 흔든 게 아니라 거의 수직으로 내려가야 함 (SIDEWAYS)
    - 여러 프레임에 걸쳐 계속 내려가야 함 (손을 잘못 인식해 한 프레임 튄 건 무시)
    - 다시 충분히 올려야 다음 날갯짓을 받음 (UP)"""
    DOWN = 0.11      # 이만큼 내려가야 날갯짓 (화면 높이 비율, 클수록 크게 흔들어야 함)
    UP = 0.065        # 이만큼 다시 올라가야 다음 날갯짓 준비
    WINDOW = 0.35    # 내려가는 동작이 이 시간 안에 일어나야 함 (천천히 내리는 건 무시)
    COOLDOWN = 0.24  # 날갯짓 사이 최소 간격(초)
    SIDEWAYS = 0.75   # 옆으로 움직인 거리가 내려간 거리의 이 비율보다 크면 무시
    MIN_FRAMES = 3   # 맨 위에서 지금까지 최소 이만큼의 프레임 동안 내려와야 함
    STEADY = 0.7    # 그 사이 프레임의 이 비율 이상이 실제로 내려가는 중이어야 함

    def __init__(self):
        self.reset()
        self.last_flap = 0.0

    def reset(self):
        self.recent = deque()
        self.armed = True
        self.bottom = 0.0
        self.last_seen = 0.0

    def update(self, x, y, t):
        if t - self.last_seen > 0.3:
            self.reset()
        self.last_seen = t
        self.recent.append((t, x, y))
        while self.recent and t - self.recent[0][0] > self.WINDOW:
            self.recent.popleft()

        if self.armed:
            pts = list(self.recent)
            top_i = min(range(len(pts)), key=lambda i: pts[i][2])
            _, top_x, top_y = pts[top_i]
            drop = y - top_y
            if drop > self.DOWN and t - self.last_flap > self.COOLDOWN:
                stroke = pts[top_i:]
                steps = len(stroke) - 1
                going_down = sum(1 for i in range(steps) if stroke[i + 1][2] > stroke[i][2])
                prev_drop = stroke[-2][2] - top_y if steps >= 1 else 0.0  # 직전 프레임에서도 이미 반 넘게 내려와 있어야 함 (한 프레임 튐 방지)
                if (steps >= self.MIN_FRAMES
                        and going_down >= steps * self.STEADY
                        and prev_drop > self.DOWN * 0.5
                        and abs(x - top_x) < drop * self.SIDEWAYS):
                    self.armed = False
                    self.bottom = y
                    self.last_flap = t
                    return True
        else:
            self.bottom = max(self.bottom, y)
            if self.bottom - y > self.UP:
                self.armed = True
                self.recent.clear()
                self.recent.append((t, x, y))
        return False


def send_status(status):
    """카메라 상태를 게임에 알림 → 왼쪽 아래 카메라 창에 안내 문구로 표시"""
    send(f"STATUS,{status}".encode(), serverAddressPort)


def list_dshow_cameras():
    """DirectShow 비디오 장치 이름 목록 (OpenCV CAP_DSHOW 번호 순서와 같음)"""
    ole32 = ctypes.windll.ole32
    oleaut32 = ctypes.windll.oleaut32

    class GUID(ctypes.Structure):
        _fields_ = [("d1", ctypes.c_ulong), ("d2", ctypes.c_ushort), ("d3", ctypes.c_ushort), ("d4", ctypes.c_ubyte * 8)]

    def guid(s):
        g = GUID()
        ole32.CLSIDFromString(ctypes.c_wchar_p(s), ctypes.byref(g))
        return g

    def method(ptr, index, restype, *argtypes):
        vtbl = ctypes.cast(ptr, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
        return ctypes.WINFUNCTYPE(restype, ctypes.c_void_p, *argtypes)(vtbl[index])

    def release(ptr):
        if ptr:
            method(ptr, 2, ctypes.c_ulong)(ptr)

    class VARIANT(ctypes.Structure):
        _fields_ = [("vt", ctypes.c_ushort), ("r1", ctypes.c_ushort), ("r2", ctypes.c_ushort), ("r3", ctypes.c_ushort),
                    ("val", ctypes.c_void_p), ("pad", ctypes.c_void_p)]

    names = []
    ole32.CoInitialize(None)
    dev_enum = ctypes.c_void_p()
    hr = ole32.CoCreateInstance(ctypes.byref(guid("{62BE5D10-60EB-11d0-BD3B-00A0C911CE86}")), None, 1,
                                ctypes.byref(guid("{29840822-5B84-11D0-BD3B-00A0C911CE86}")), ctypes.byref(dev_enum))
    if hr != 0 or not dev_enum:
        return names
    enum_mon = ctypes.c_void_p()
    hr = method(dev_enum, 3, ctypes.c_long, ctypes.POINTER(GUID), ctypes.POINTER(ctypes.c_void_p), wintypes.DWORD)(
        dev_enum, ctypes.byref(guid("{860BB310-5D01-11d0-BD3B-00A0C911CE86}")), ctypes.byref(enum_mon), 0)
    if hr == 0 and enum_mon:  # S_FALSE(1) = 장치 없음
        iid_bag = guid("{55272A00-42CB-11CE-8135-00AA004BB851}")
        while True:
            mon = ctypes.c_void_p()
            fetched = ctypes.c_ulong()
            if method(enum_mon, 3, ctypes.c_long, ctypes.c_ulong, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_ulong))(
                    enum_mon, 1, ctypes.byref(mon), ctypes.byref(fetched)) != 0 or fetched.value == 0:
                break
            name = ""
            bag = ctypes.c_void_p()
            if method(mon, 9, ctypes.c_long, ctypes.c_void_p, ctypes.c_void_p, ctypes.POINTER(GUID), ctypes.POINTER(ctypes.c_void_p))(
                    mon, None, None, ctypes.byref(iid_bag), ctypes.byref(bag)) == 0:
                var = VARIANT()
                if method(bag, 3, ctypes.c_long, ctypes.c_wchar_p, ctypes.POINTER(VARIANT), ctypes.c_void_p)(
                        bag, "FriendlyName", ctypes.byref(var), None) == 0 and var.vt == 8:
                    name = ctypes.wstring_at(var.val)
                oleaut32.VariantClear(ctypes.byref(var))
                release(bag)
            names.append(name)
            release(mon)
        release(enum_mon)
    release(dev_enum)
    return names


def camera_indices():
    """메뉴에서 고른 카메라 이름 → DirectShow 번호. 못 찾으면(뽑혔거나 이름을 못 읽음) 자동으로 0~2번"""
    if camera_name:
        try:
            names = list_dshow_cameras()
            print(f"카메라 목록: {names}")
            if camera_name in names:
                return [names.index(camera_name)]
            print(f"⚠️ '{camera_name}' 카메라를 못 찾아서 자동으로 고릅니다")
        except Exception as e:
            print(f"⚠️ 카메라 이름 읽기 실패: {e}")
    return range(3)


def open_camera():
    """0~2번 카메라를 DirectShow → Media Foundation 순으로 열어 보고, 실제로 화면이 들어오는 것을 사용.
    메뉴에서 카메라를 골랐으면 그 카메라만 시도.
    열리기는 하는데 화면이 안 들어오면 다른 프로그램(디스코드, 줌 등)이 카메라를 쓰는 중인 경우가 많음."""
    busy = False
    for idx in camera_indices():
        for api, api_name in ((cv2.CAP_DSHOW, "DSHOW"), (cv2.CAP_MSMF, "MSMF")):
            c = cv2.VideoCapture(idx, api)
            if not c.isOpened():
                c.release()
                continue
            c.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            c.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            for _ in range(15):  # 켜지자마자는 빈 화면이 몇 장 올 수 있어서 여러 번 시도
                ret, frame = c.read()
                if ret and frame is not None:
                    print(f"✅ 웹캠 {idx}번 ({api_name}) 연결 성공!")
                    return c, False
                time.sleep(0.05)
            print(f"⚠️ 웹캠 {idx}번 ({api_name}) 열렸지만 화면이 안 들어옴")
            busy = True
            c.release()
    return None, busy


# 카메라를 찾을 때까지 2초마다 다시 시도 (다른 프로그램이 카메라를 놓아주면 그때 바로 연결됨)
cap = None
while cap is None:
    if not parent_alive():
        sys.exit()
    send_status("connecting")
    cap, busy = open_camera()
    if cap is None:
        print("❌ 사용 가능한 웹캠 없음 → 2초 뒤 다시 시도" + (" (다른 프로그램이 사용 중인 듯)" if busy else ""))
        send_status("busy" if busy else "nocam")
        time.sleep(2.0)

print(f"부모 프로세스: {parent_pid or '없음'} / 손 인식: {'MediaPipe' if hand_detector else '움직임 감지'}")

GREEN, RED, GOLD, WHITE =(80, 220, 80), (60, 60, 230), (60, 190, 250), (240, 240, 240)

fail_count = 0
flap_count = 0
flap_flash = 0.0
detectors = {}             # 손마다 하나씩 (MediaPipe: 왼손/오른손, 움직임 감지: 하나)
last_face = None           # 얼굴을 잠깐 놓쳐도 0.4초 동안은 마지막 위치 유지
last_face_time = 0.0
prev_small = None          # 움직임 감지용 이전 프레임
start = time.time()
last_parent_check = start
frames = 0
last_ts = -1

while True:
    try:
        if time.time() - last_parent_check > 2.0:
            last_parent_check = time.time()
            if not parent_alive():
                print("게임이 종료되어 트래커도 종료합니다.")
                break

        ret, image = cap.read()

        # 프레임을 읽지 못했을 때 방어 코드
        if not ret or image is None:
            fail_count += 1
            if fail_count > 50:
                # 도중에 카메라가 끊기면(다른 프로그램이 가져감, 선 뽑힘) 다시 연결될 때까지 기다림
                print("⚠️ 웹캠 화면이 끊김 → 다시 연결 시도")
                cap.release()
                cap = None
                while cap is None and parent_alive():
                    cap, busy = open_camera()
                    if cap is None:
                        send_status("busy" if busy else "nocam")
                        time.sleep(2.0)
                if cap is None:
                    break
                fail_count = 0
            time.sleep(0.03)
            continue
        fail_count = 0

        now = time.time()
        image = cv2.flip(image, 1)  # 거울 모드: 내가 왼쪽으로 가면 화면에서도 왼쪽
        img_h, img_w = image.shape[:2]

        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        rgb = None
        if hand_detector is not None or face_detector is not None:
            rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            ts = max(last_ts + 1, int((now - start) * 1000))  # MediaPipe는 시간값이 꼭 계속 커져야 함
            last_ts = ts
            mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

        # ── 얼굴: 가장 큰 얼굴 하나. 위치는 코끝 기준 (몸을 옮겨도, 고개만 돌려도 반응) ──
        face = None   # (x, y, w, h, 코끝x, 코끝y) 픽셀
        if face_detector is not None:
            dets = face_detector.detect_for_video(mp_image, ts).detections
            if dets:
                d = max(dets, key=lambda d: d.bounding_box.width * d.bounding_box.height)
                bb = d.bounding_box
                if len(d.keypoints) > 2:
                    px, py = d.keypoints[2].x * img_w, d.keypoints[2].y * img_h  # 2번 = 코끝
                else:
                    px, py = bb.origin_x + bb.width / 2.0, bb.origin_y + bb.height / 2.0
                face = (bb.origin_x, bb.origin_y, bb.width, bb.height, px, py)
        else:
            small_gray = cv2.resize(gray, (img_w // 2, img_h // 2))
            faces = face_cascade.detectMultiScale(small_gray, scaleFactor=1.1, minNeighbors=5, minSize=(30, 30))
            if len(faces) > 0:
                x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
                face = (x * 2, y * 2, w * 2, h * 2, x * 2 + w, y * 2 + h)
        if face is not None:
            last_face, last_face_time = face, now
        elif last_face is not None and now - last_face_time < 0.4:
            face = last_face  # 잠깐 놓쳐도 0.4초는 마지막 위치 유지

        nx, ny, face_ok = 0.5, 0.5, 0
        if face is not None:
            x, y, w, h, px, py = [int(v) for v in face]
            nx = px / img_w
            ny = py / img_h
            face_ok = 1
            cv2.rectangle(image, (x, y), (x + w, y + h), GREEN, 4)
            cv2.circle(image, (px, py), 9, RED, -1)

        # ── 손: 아래로 내리치면 날갯짓 ──
        flapped = False
        if hand_detector is not None:
            result = hand_detector.detect_for_video(mp_image, ts)
            for i, lm in enumerate(result.hand_landmarks):
                key = result.handedness[i][0].category_name if i < len(result.handedness) and result.handedness[i] else str(i)
                palm = lm[9]  # 손바닥 가운데 (가운뎃손가락 뿌리)
                det = detectors.setdefault(key, FlapDetector())
                if det.update(palm.x, palm.y, now):
                    flapped = True
                for p in lm:
                    cv2.circle(image, (int(p.x * img_w), int(p.y * img_h)), 5, GOLD, -1)
                cv2.circle(image, (int(palm.x * img_w), int(palm.y * img_h)), 14, GOLD, 4)
        else:
            # 얼굴을 뺀 나머지에서 움직인 부분의 높이를 손 높이로 사용
            small = cv2.GaussianBlur(cv2.resize(gray, (160, 120)), (5, 5), 0)
            if prev_small is not None:
                diff = cv2.absdiff(small, prev_small)
                _, mask = cv2.threshold(diff, 25, 255, cv2.THRESH_BINARY)
                if face is not None:
                    x, y, w, h = [int(v) // 4 for v in face[:4]]
                    mask[max(0, y - h // 2):y + h + h, max(0, x - w // 3):x + w + w // 3] = 0  # 얼굴/목 주변 제외
                m = cv2.moments(mask, True)
                if m['m00'] > 60:
                    cy = m['m01'] / m['m00'] / 120.0
                    cx = m['m10'] / m['m00'] / 160.0
                    if detectors.setdefault('motion', FlapDetector()).update(cx, cy, now):
                        flapped = True
                    cv2.circle(image, (int(cx * img_w), int(cy * img_h)), 14, GOLD, 4)
            prev_small = small

        if flapped:
            flap_count += 1
            flap_flash = now

        # 유니티로 전송
        send(f"{nx:.4f},{ny:.4f},{flap_count},{face_ok}".encode(), serverAddressPort)

        # ── 표시 (게임 왼쪽 아래 화면에서 보이도록 반으로 줄여도 읽히는 크기) ──
        if now - flap_flash < 0.25:
            cv2.putText(image, "FLAP!", (img_w // 2 - 110, 90), cv2.FONT_HERSHEY_DUPLEX, 2.4, GOLD, 5)
        if face_ok == 0:
            cv2.putText(image, "NO FACE", (16, 50), cv2.FONT_HERSHEY_DUPLEX, 1.4, RED, 3)

        # 게임으로 카메라 화면 전송 (320x240 JPEG, UDP 한 번에 들어가는 크기)
        preview = cv2.resize(image, (320, 240), interpolation=cv2.INTER_AREA)
        ok, jpg = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, 70])
        if ok and len(jpg) < 60000:
            send(jpg.tobytes(), previewAddressPort)

        frames += 1
        if frames == 1:
            print("첫 프레임 처리 완료, 게임으로 전송 시작")
    except Exception:
        # 한 프레임 처리 중 예상 못 한 에러가 나도 트래커가 죽지 않게 기록만 하고 계속
        import traceback
        print("⚠️ 프레임 처리 중 에러 (계속 진행): " + traceback.format_exc())
        time.sleep(0.05)

cap.release()
print("트래커 종료")
