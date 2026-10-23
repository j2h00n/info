"""Flappy-Bird류 3D 러너 Panda3D 이식판. Unity 원본의 BirdController/MotionController/
BoundaryWall/FollowEnvironment/PythonLauncher 로직을 통합한 실행 엔트리포인트.

[좌표계 주의] 이 파일에서 가장 헷갈리는 부분:
  - 게임 로직(bird.py/wall.py)은 position=[x, y(높이), z(전진)] 를 씀 (Unity 방식).
  - Panda3D는 X=좌우, Y=전진, Z=위 이므로, 로직 값을 노드에 넣을 때
    setPos(x, z, y) 처럼 y와 z를 맞바꿔서 넣음. (예: self.bird_np.setPos(bx, bz, by))
  - 파이프 박스도 make_box("pipe", sx, sz, sy) 처럼 크기 순서가 바뀌어 있음.

[파일 구조]
  Game.__init__   : 창/카메라/로직 객체 생성
  _setup_audio    : 효과음/BGM
  _setup_lights   : 조명
  _build_scene    : 새/쉴드/바닥 노드 생성
  _build_ui       : HUD, 시작 메뉴, 슬라이더, 재시작 버튼
  _build_motion_preview : 웹캠 미리보기 이미지
  _bind_input     : 키보드 입력 연결
  update          : 매 프레임 실행되는 메인 루프 (입력 -> 물리 -> 충돌 -> 렌더 동기화)
"""
import os

from direct.showbase.ShowBase import ShowBase
from direct.gui.OnscreenText import OnscreenText
from direct.gui.OnscreenImage import OnscreenImage
from direct.gui.DirectGui import DirectButton, DirectSlider, DirectFrame
from direct.gui import DirectGuiGlobals as DGG
from panda3d.core import (
    AmbientLight,
    DirectionalLight,
    Filename,
    Texture,
    TransparencyAttrib,
    TextNode,
    WindowProperties,
)

from . import prefs
from .bird import Bird
from .boundary import Boundary
from .wall import WallSpawner
from .motion_capture import MotionCapture
from .geometry import make_box
from .seagull_loader import load_seagull_parts
from .scenery import SceneryManager, spawn_instance
from direct.interval.IntervalGlobal import Sequence, LerpHprInterval


class Game(ShowBase):
    def __init__(self, easy=True):
        ShowBase.__init__(self)

        # CC0 배경 애셋(glTF)이 PBR 머티리얼을 써서 기본 파이프라인으론 검게 나옴 - simplepbr로 보정
        try:
            import simplepbr
            simplepbr.init()
        except ImportError:
            pass  # simplepbr 미설치면 그냥 넘어감(배경이 어둡게 보일 수 있음)

        # 실험 모드: 로직/구조는 원본과 동일, 체감 난이도(속도/중력/틈/간격)만 낮춤
        # (Game() 기본값이 easy=True라 python -m python_game.main 으로 켜도 쉬운 모드.
        #  원본 난이도로 하려면 아래 main()에서 Game(easy=False)로 바꾸면 됨)
        self.easy = easy

        # 창 제목/크기 설정. 창 크기를 바꾸면 UI 좌표 환산(_build_ui의 k값, 900)도 같이 확인할 것
        props = WindowProperties()
        props.setTitle("Flappy Runner 3D (Python)" + (" - 실험 모드" if easy else ""))
        props.setSize(1600, 900)
        self.win.requestProperties(props)

        # Unity 원본 Main Camera: field of view: 60, near clip: 0.3, far clip: 1000
        self.camLens.setFov(60)  # 시야각. 클수록 넓게 보임
        self.camLens.setNearFar(0.3, 1000)

        self._setup_lights()
        # 한글 UI용 폰트(Windows 맑은 고딕). Panda 경로 형식이라 /c/Windows/... 로 씀
        self._korean_font = self.loader.loadFont("/c/Windows/Fonts/malgun.ttf")

        # ----- 게임 로직 객체들 (모두 Panda3D와 무관한 순수 로직) -----
        self.bird_logic = Bird(easy=easy)        # 새 물리/점수
        self.boundary = Boundary()               # 좌우 경계 + 쉴드 알파
        self.walls = WallSpawner(easy=easy)      # 기둥 생성/충돌
        self.scenery = SceneryManager()          # 배경 장식 배치
        self.motion = MotionCapture()            # 웹캠 모션 인식
        self.motion_learning_rate = 1.5          # motion.update에 넘기는 값 (현재 motion_capture에선 사용 안 함)

        # 모션 모드는 이전 실행에서 저장된 설정(save.json)을 따름
        self.motion_enabled = prefs.get_int("MotionMode", 0) == 1
        if self.motion_enabled:
            self.motion.start()

        self._build_scene()
        self._setup_audio()
        self._build_ui()
        self._build_motion_preview()
        self._bind_input()

        # 매 프레임 self.update를 호출하도록 등록
        self.taskMgr.add(self.update, "update")

    # ---------- audio ----------
    def _setup_audio(self):
        """점프 효과음(jump_sfx)과 배경음(bgm_sfx) 로드. 파일이 없으면 None으로 두고 조용히 건너뜀."""
        assets_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), "Assets")
        jump_path = os.path.join(
            assets_dir, "Game Sound Solutions - 8 bits Elements", "jump", "jump_15.wav"
        )
        bgm_path = os.path.join(
            assets_dir, "RedsenGameMusic_Afternoon", "RedsenGameMusic_Afternoon_Cute_Casual_Loopable.wav"
        )

        # Windows 경로는 Panda 로더가 못 읽으므로 Filename.from_os_specific으로 변환 필수
        self.jump_sfx = None
        if os.path.exists(jump_path):
            self.jump_sfx = self.loader.loadSfx(Filename.from_os_specific(jump_path))
        self.bgm_sfx = None
        if os.path.exists(bgm_path):
            self.bgm_sfx = self.loader.loadSfx(Filename.from_os_specific(bgm_path))

        # 저장된 볼륨(0~1) 불러오기. 기본 1.0 = 최대
        self.jump_volume = prefs.get_float("JumpVolume", 1.0)
        self.music_volume = prefs.get_float("MusicVolume", 1.0)

        if self.jump_sfx:
            self.jump_sfx.setVolume(self.jump_volume)
        if self.bgm_sfx:
            self.bgm_sfx.setVolume(self.music_volume)
            self.bgm_sfx.setLoop(True)  # 반복 재생
            self.bgm_sfx.play()

    def _play_jump_sfx(self):
        if self.jump_sfx:
            self.jump_sfx.play()

    def _play_flap_animation(self):
        """날개 파닥 애니메이션: 날개를 0.08초 동안 45도 올렸다가 0.15초 동안 원위치.
        각도(45)나 시간을 바꾸면 파닥 느낌이 달라짐. sign은 좌/우 날개를 반대로 돌리기 위한 부호."""
        for wing_np, sign in ((self.wing_neg, -1), (self.wing_pos, 1)):
            if wing_np is None:
                continue
            Sequence(
                LerpHprInterval(wing_np, 0.08, (0, 0, sign * 45)),
                LerpHprInterval(wing_np, 0.15, (0, 0, 0)),
            ).start()

    # ---------- scene ----------
    def _setup_lights(self):
        """조명 2개: 전체를 은은하게 밝히는 앰비언트 + 태양 역할의 방향광."""
        alight = AmbientLight("ambient")
        alight.setColor((0.4, 0.4, 0.45, 1))  # (R,G,B,A) - 값을 키우면 그림자 쪽도 밝아짐
        self.render.setLight(self.render.attachNewNode(alight))

        dlight = DirectionalLight("sun")
        dlight.setColor((0.9, 0.9, 0.85, 1))  # 태양빛 색/세기
        dlnp = self.render.attachNewNode(dlight)
        dlnp.setHpr(45, -60, 0)  # 빛이 비추는 방향(Heading, Pitch, Roll)
        self.render.setLight(dlnp)

    def _build_scene(self):
        # Unity 원본: 물리 콜라이더는 1x1x1 Cube, 실제 비주얼은 그 자식으로 붙은
        # Seagull.fbx(SailCharacterPack) 모델. 콜라이더용 빈 노드에 시각 모델을 자식으로 붙임.
        self.bird_np = self.render.attach_new_node("bird")
        seagull_parts = load_seagull_parts()
        self.wing_neg = None
        self.wing_pos = None
        if seagull_parts is not None:
            seagull_parts["root"].reparentTo(self.bird_np)
            seagull_parts["root"].setH(180)  # 모델이 진행방향 반대로 향해있어 180도 보정
            self.wing_neg = seagull_parts["wing_neg"]
            self.wing_pos = seagull_parts["wing_pos"]
        else:
            # FBX 로드 실패 시 노란 1x1x1 박스로 대체 (색 바꾸려면 color=(r,g,b,a) 수정)
            fallback = make_box("bird_fallback", 1, 1, 1, color=(1, 0.85, 0.1, 1))
            fallback.reparentTo(self.bird_np)

        # Shield Shader FREE(무료 애셋) 실제 텍스처를 입혀 쉴드 느낌 근접시킴
        shield_tex_path = os.path.join(
            os.path.dirname(os.path.dirname(__file__)),
            "Assets", "Shield Shader FREE", "Textures", "Shader", "Hex Tile.png",
        )
        shield_tex = None
        if os.path.exists(shield_tex_path):
            shield_tex = self.loader.loadTexture(Filename.from_os_specific(shield_tex_path))

        # 좌/우 쉴드: 얇은 반투명 파란 판(가로0.3 x 깊이6 x 높이10). 크기/색은 여기서 조절
        self.left_shield = make_box("left_shield", 0.3, 6, 10, color=(0.3, 0.7, 1, 0.6))
        self.right_shield = make_box("right_shield", 0.3, 6, 10, color=(0.3, 0.7, 1, 0.6))
        for shield in (self.left_shield, self.right_shield):
            shield.reparentTo(self.render)
            shield.setTransparency(TransparencyAttrib.M_alpha)  # 알파(투명도) 적용 허용
            if shield_tex is not None:
                shield.setTexture(shield_tex)

        # 바닥: 파이프가 내려가는 최저 높이(컬럼별로 랜덤이라 "가장 안 내려가는" 케이스
        # 기준)보다 살짝 높은 곳에 둬서, 파이프 밑으로 저공비행해서 빠져나가는 구멍이
        # 안 생기게 함. 시각적 바닥과 충돌판정 높이를 일치시킴(FLOOR_Y).
        # (scenery.py의 GROUND_Y(-7.1)도 이 값 바로 위로 맞춰져 있으니 같이 수정)
        self.FLOOR_Y = -7.6     # 이보다 낮아지면 사망(바닥)
        self.CEILING_Y = 10.3   # 이보다 높아지면 사망(천장)
        ground = make_box("ground", 200, 4000, 1, color=(0.3, 0.55, 0.28, 1))
        ground.setPos(0, 2000, self.FLOOR_Y)
        ground.reparentTo(self.render)

        # 화면에 실제로 만들어진 노드를 추적: id(chunk) -> list[NodePath]
        # (wall.py/scenery.py의 chunk가 사라지면 같이 지우기 위해 필요)
        self._chunk_nodes = {}
        self._scenery_nodes = {}

    def _build_ui(self):
        # 인게임 HUD: ScoreText(anchor 0,1 top-left)/WallText(anchor 1,1 top-right), 둘다 anchoredPos(0,0)
        aspect = self.get_aspect_ratio()
        inset = 0.05  # 화면 정가장자리(0 오프셋)라 살짝 안쪽으로 마진만 줌
        # 좌상단 점수 / 우상단 벽 수 (scale=글자 크기, fg=글자색)
        self.score_text = OnscreenText(
            text="Score : 0", pos=(-aspect + inset, 1 - inset - 0.05), scale=0.06,
            align=TextNode.ALeft, fg=(1, 1, 1, 1), font=self._korean_font,
        )
        self.wall_text = OnscreenText(
            text="Wall : 0", pos=(aspect - inset, 1 - inset - 0.05), scale=0.06,
            align=TextNode.ARight, fg=(1, 1, 1, 1), font=self._korean_font,
        )

        # Unity 원본 RectTransform 값 실측(px, CanvasScaler Constant Pixel Size, 창 1600x900 기준)을
        # Panda aspect2d 좌표로 환산: k = 2/screen_height, x_panda = anchor_dx*aspect*2 + px*k, y_panda = anchoredY*k
        # (Canvas 앵커가 전부 (0.5,0.5) 중앙이라 StartMenu 자체가 (0,0)에 오고, 자식들도 그대로 오프셋만 적용됨)
        # 즉 "Unity 픽셀 값 * k = Panda 좌표". 아래 UI 위치를 옮기고 싶으면 px 숫자만 바꾸면 됨.
        k = 2.0 / 900.0

        # StartMenu: sizeDelta (384.62, 392.05), anchoredPos ~ (0,0)
        # 시작 메뉴 배경 패널(반투명 검정). frameSize=(left,right,bottom,top)
        self.start_menu = DirectFrame(
            frameColor=(0, 0, 0, 0.6),
            frameSize=(-384.62 / 2 * k, 384.62 / 2 * k, -392.05 / 2 * k, 392.05 / 2 * k),
            pos=(0, 0, 0),
        )
        # Text1(highScoreText): anchoredPos (-86.216, 2.599)
        self.high_score_text = OnscreenText(
            text=f"High Score\n{self.bird_logic.best_score}",
            pos=(-86.216 * k, 2.599 * k), scale=0.06, fg=(1, 1, 1, 1), font=self._korean_font,
            parent=self.start_menu, align=TextNode.ACenter,
        )
        # Text2(highWallText): anchoredPos (99, 0.395)
        self.high_wall_text = OnscreenText(
            text=f"High Wall\n{self.bird_logic.best_wall}",
            pos=(99 * k, 0.395 * k), scale=0.06, fg=(1, 1, 1, 1), font=self._korean_font,
            parent=self.start_menu, align=TextNode.ACenter,
        )
        # DirectSlider 기본 폭은 스케일 미적용시 2유닛(-1..1) - Unity sizeDelta.x=160px에 맞춰 환산
        slider_scale = (160 * k) / 2.0  # ≈0.178

        # Slider1(jumpSoundSlider): anchoredPos (1.6, 132)
        OnscreenText(
            text="점프 음량", pos=(1.6 * k - slider_scale - 0.05, 132 * k), scale=0.05, fg=(1, 1, 1, 1),
            font=self._korean_font, parent=self.start_menu, align=TextNode.ARight,
        )
        self.jump_slider = DirectSlider(
            parent=self.start_menu, pos=(1.6 * k, 0, 132 * k), range=(0, 1), value=self.jump_volume,
            pageSize=0.1, scale=slider_scale, command=self._on_jump_volume_changed,
        )
        # Slider2(musicSoundSlider): anchoredPos (1.6, 67)
        OnscreenText(
            text="음악 음량", pos=(1.6 * k - slider_scale - 0.05, 67 * k), scale=0.05, fg=(1, 1, 1, 1),
            font=self._korean_font, parent=self.start_menu, align=TextNode.ARight,
        )
        self.music_slider = DirectSlider(
            parent=self.start_menu, pos=(1.6 * k, 0, 67 * k), range=(0, 1), value=self.music_volume,
            pageSize=0.1, scale=slider_scale, command=self._on_music_volume_changed,
        )

        # Motion Toggle: anchoredPos (0, -169.8). DirectCheckButton 기본 인디케이터가 예측불가하게
        # 커서(화면을 뒤덮는 버그) 대신 작은 DirectButton 체크박스를 손수 구성.
        toggle_y = -169.8 * k
        self._motion_box_on_color = (0.2, 0.8, 0.3, 1)    # 켜짐=초록
        self._motion_box_off_color = (0.3, 0.3, 0.3, 1)   # 꺼짐=회색
        self.motion_toggle_box = DirectButton(
            parent=self.start_menu, pos=(-0.12, 0, toggle_y),
            relief=DGG.FLAT, frameSize=(-0.035, 0.035, -0.035, 0.035),
            frameColor=self._motion_box_on_color if self.motion_enabled else self._motion_box_off_color,
            command=self._toggle_motion,
        )
        OnscreenText(
            text="모션 조작", pos=(-0.06, toggle_y - 0.018), scale=0.05, fg=(1, 1, 1, 1),
            font=self._korean_font, parent=self.start_menu, align=TextNode.ALeft,
        )

        # Start Button: anchoredPos (1.6, -128), sizeDelta (93.41, 22.4)
        # 클릭 시 Space와 같은 _on_space 실행 (게임 시작)
        self.start_button = DirectButton(
            parent=self.start_menu, pos=(1.6 * k, 0, -128 * k), text="시작", text_font=self._korean_font,
            relief=DGG.FLAT, text_scale=0.06,
            frameSize=(-93.41 / 2 * k, 93.41 / 2 * k, -22.4 / 2 * k, 22.4 / 2 * k),
            frameColor=(0.2, 0.7, 0.3, 1), command=self._on_space,
        )

        # Restart 버튼: Canvas 직속(StartMenu 자식 아님), anchoredPos (0,0), sizeDelta (160,30)
        # 평소엔 숨겨두고, 게임오버가 되면 update()에서 show()함
        self.restart_button = DirectButton(
            pos=(0, 0, 0), text="RESTART!", text_font=self._korean_font, text_scale=0.07,
            relief=DGG.FLAT, frameSize=(-160 / 2 * k, 160 / 2 * k, -30 / 2 * k, 30 / 2 * k),
            frameColor=(0.2, 0.85, 0.2, 1), command=self._restart,
        )
        self.restart_button.hide()

    def _build_motion_preview(self):
        """웹캠 미리보기를 별도 창이 아니라 게임 창 우하단에 작게 띄움."""
        self._motion_tex = Texture("motion_preview")
        self._motion_tex_size = None  # 현재 텍스처 해상도(w,h). 바뀌면 다시 setup
        aspect = self.get_aspect_ratio()
        # pos=(x,0,z)로 위치, scale=(가로,1,세로)로 크기 조절 (미리보기 위치/크기 바꾸려면 여기)
        self.motion_preview = OnscreenImage(
            image=self._motion_tex, pos=(aspect - 0.32, 0, -0.72), scale=(0.3, 1, 0.22),
        )
        if self.motion_enabled:
            self.motion_preview.show()
        else:
            self.motion_preview.hide()

    def _update_motion_preview(self):
        """motion.last_frame(OpenCV BGR 배열)을 Panda 텍스처로 복사해 미리보기 갱신."""
        frame = self.motion.last_frame
        if frame is None:
            return
        h, w = frame.shape[:2]
        # 해상도가 처음이거나 바뀌었을 때만 텍스처 재설정
        if self._motion_tex_size != (w, h):
            self._motion_tex.setup2dTexture(w, h, Texture.T_unsigned_byte, Texture.F_rgb)
            self._motion_tex_size = (w, h)
        flipped = frame[::-1]  # Panda 텍스처는 아래->위 순서라 세로 뒤집어서 넣음
        self._motion_tex.setRamImageAs(flipped.tobytes(), "BGR")

    def _bind_input(self):
        """키 입력 연결. 키를 바꾸고 싶으면 accept()의 첫 인자 문자열을 수정."""
        self._keys = {"left": False, "right": False}  # 현재 눌려있는지 상태
        self.accept("space", self._on_space)          # 시작/점프/재시작
        self.accept("m", self._toggle_motion)         # 모션 컨트롤 on/off
        # "-up"이 붙은 이벤트는 키를 뗄 때 발생
        self.accept("arrow_left", self._set_key, ["left", True])
        self.accept("arrow_left-up", self._set_key, ["left", False])
        self.accept("arrow_right", self._set_key, ["right", True])
        self.accept("arrow_right-up", self._set_key, ["right", False])

    def _set_key(self, name, value):
        self._keys[name] = value

    def _on_space(self):
        """Space(또는 시작 버튼) 동작: 상태에 따라 시작 -> 재시작 -> 점프 순으로 분기."""
        if not self.bird_logic.is_game_started:
            self.bird_logic.start_game()
            self.start_menu.hide()
        elif self.bird_logic.is_game_over:
            self._restart()
        else:
            # jump()가 True(실제로 점프함)일 때만 효과음/애니메이션 재생
            if self.bird_logic.jump():
                self._play_jump_sfx()
                self._play_flap_animation()

    def _toggle_motion(self):
        self._set_motion_enabled(not self.motion_enabled)

    def _set_motion_enabled(self, enabled):
        """모션 컨트롤 켜기/끄기: 체크박스 색, 설정 저장, 웹캠 시작/중지, 미리보기 표시를 함께 처리."""
        self.motion_enabled = enabled
        self.motion_toggle_box["frameColor"] = (
            self._motion_box_on_color if enabled else self._motion_box_off_color
        )
        prefs.set_int("MotionMode", 1 if enabled else 0)
        prefs.save()
        if enabled:
            self.motion.start()
            self.motion_preview.show()
        else:
            self.motion.stop()
            self.motion_preview.hide()

    def _on_jump_volume_changed(self):
        """점프 음량 슬라이더가 움직일 때마다 호출: 볼륨 적용 + 저장."""
        self.jump_volume = self.jump_slider["value"]
        if self.jump_sfx:
            self.jump_sfx.setVolume(self.jump_volume)
        prefs.set_float("JumpVolume", self.jump_volume)
        prefs.save()

    def _on_music_volume_changed(self):
        """음악 음량 슬라이더가 움직일 때마다 호출: 볼륨 적용 + 저장."""
        self.music_volume = self.music_slider["value"]
        if self.bgm_sfx:
            self.bgm_sfx.setVolume(self.music_volume)
        prefs.set_float("MusicVolume", self.music_volume)
        prefs.save()

    def _restart(self):
        """게임오버 후 재시작: 새/기둥/배경을 전부 초기 상태로 되돌리고 시작 메뉴를 다시 보여줌."""
        self.bird_logic.restart()
        # 기둥: 스포너를 새로 만들고, 화면에 남은 기둥 노드를 모두 삭제
        self.walls = WallSpawner(easy=self.easy)
        for nodes in self._chunk_nodes.values():
            for np in nodes:
                np.removeNode()
        self._chunk_nodes = {}

        # 배경 장식도 동일하게 초기화
        self.scenery = SceneryManager()
        for nodes in self._scenery_nodes.values():
            for np in nodes:
                np.removeNode()
        self._scenery_nodes = {}

        self.restart_button.hide()
        self.start_menu.show()

    # ---------- per-frame ----------
    def update(self, task):
        """매 프레임 실행. 순서: 입력 -> 새 물리 -> 경계 -> 노드 위치 -> 카메라 -> 기둥/배경 -> 충돌 -> HUD."""
        dt = globalClock.getDt()  # 지난 프레임 이후 경과 시간(초)

        # 키보드 입력 -> move_x(-1~1). 왼쪽키=+1, 오른쪽키=-1 (부호 규칙은 CONVENTIONS.md 참고)
        move_x = 1.0 if self._keys["left"] else (-1.0 if self._keys["right"] else 0.0)
        if self.motion_enabled:
            # 모션 모드: 웹캠 한 프레임 처리 -> 파닥(jumped) 감지 시 점프
            jumped = self.motion.update(self.motion_learning_rate, dt)
            self._update_motion_preview()
            if jumped and self.bird_logic.jump():
                self._play_jump_sfx()
                self._play_flap_animation()
            # 키보드와 머리 기울기 중 절대값이 더 큰 입력을 채택
            motion_x = self.motion.get_move_x()
            move_x = motion_x if abs(motion_x) > abs(move_x) else move_x

        # 새 물리 1프레임 진행 (중력, 이동, 기울기, 점수)
        self.bird_logic.update(dt, move_x)

        # 좌우 경계: x를 한계 안으로 가두고, 가까우면 쉴드가 보이도록 알파 계산
        clamped_x, alpha_l, alpha_r = self.boundary.clamp_and_shield_alpha(self.bird_logic.position[0])
        self.bird_logic.position[0] = clamped_x

        # 로직 좌표(x, y=높이, z=전진) -> Panda 좌표(x, 전진, 높이)로 바꿔서 새 노드에 적용
        bx, by, bz = self.bird_logic.position
        self.bird_np.setPos(bx, bz, by)
        self.bird_np.setR(self.bird_logic.roll)  # 좌우 기울기

        # 쉴드는 새의 전진/높이를 따라다니고, 좌우(X)는 경계 위치에 고정
        self.left_shield.setPos(-Boundary.LIMIT_X, bz, by)
        self.right_shield.setPos(Boundary.LIMIT_X, bz, by)
        self.left_shield.setAlphaScale(alpha_l)
        self.right_shield.setAlphaScale(alpha_r)
        self.left_shield.show() if alpha_l > 0 else self.left_shield.hide()
        self.right_shield.show() if alpha_r > 0 else self.right_shield.hide()

        # Unity 원본: 카메라가 새(Cube)의 자식으로 로컬 오프셋 (0, +2, -7)에 고정.
        # 카메라 위치(새 기준 뒤 8, 위 4)를 바꾸면 시점이 달라짐. lookAt은 새를 바라봄
        self.camera.setPos(bx, bz - 8, by + 4)
        self.camera.lookAt(bx, bz, by)

        # 배경 장식은 게임 시작 전에도 계속 생성/갱신
        self.scenery.update(bz)
        self._sync_scenery_nodes()

        # 플레이 중일 때만: 기둥 갱신 + 충돌 판정
        if self.bird_logic.is_game_started and not self.bird_logic.is_game_over:
            self.walls.update(bz)
            self._sync_wall_nodes()

            # 기둥과 충돌 (bird_half = 새 히트박스 반크기, 키우면 충돌이 더 잘 남)
            if self.walls.check_collision(bx, by, bz, bird_half=0.5):
                self.bird_logic.on_collision(is_wall_cube=True)

            # 저공비행/고공비행으로 파이프 위아래를 그냥 뚫고 지나가는 것 방지
            if by < self.FLOOR_Y or by > self.CEILING_Y:
                self.bird_logic.on_collision(is_wall_cube=True)

        # HUD 텍스트 갱신
        self.score_text.setText(f"Score : {self.bird_logic.score}")
        self.wall_text.setText(f"Wall : {self.bird_logic.passed_walls}")

        # 게임오버면 재시작 버튼 표시
        if self.bird_logic.is_game_over and self.restart_button.isHidden():
            self.restart_button.show()

        return task.cont  # task.cont = 다음 프레임에도 계속 실행

    def _sync_wall_nodes(self):
        """wall.py의 chunk 목록과 화면의 기둥 노드를 맞춤: 새 chunk는 박스 생성, 사라진 chunk는 노드 삭제."""
        current_ids = set()
        for chunk in self.walls.chunks:
            current_ids.add(id(chunk))
            if id(chunk) in self._chunk_nodes:
                continue  # 이미 만들어진 chunk
            nodes = []
            for pipe in chunk:
                # 크기 순서 주의: make_box(이름, 가로 sx, Panda Y=깊이 sz, Panda Z=높이 sy)
                np = make_box("pipe", pipe.sx, pipe.sz, pipe.sy, color=(0.85, 0.75, 0.35, 1))  # 기둥 색은 color
                np.reparentTo(self.render)
                np.setPos(pipe.x, pipe.z, pipe.y)  # 로직(x,y,z) -> Panda(x,z,y)
                nodes.append(np)
            self._chunk_nodes[id(chunk)] = nodes

        # 더 이상 존재하지 않는 chunk의 노드 제거 (메모리 누수 방지)
        for cid in list(self._chunk_nodes.keys()):
            if cid not in current_ids:
                for np in self._chunk_nodes.pop(cid):
                    np.removeNode()

    def _sync_scenery_nodes(self):
        """_sync_wall_nodes와 같은 방식으로 배경 장식(풀/나무/구름) 노드를 생성/삭제."""
        current_ids = set()
        for chunk in self.scenery.chunks:
            current_ids.add(id(chunk))
            if id(chunk) in self._scenery_nodes:
                continue
            nodes = []
            for item in chunk:
                np = spawn_instance(item.filename, self.render)
                if np is None:
                    continue  # 모델 파일이 없으면 건너뜀
                np.setPos(item.x, item.z, item.y)  # 로직(x,y,z) -> Panda(x,z,y)
                np.setScale(item.scale)
                np.setH(item.heading)
                nodes.append(np)
            self._scenery_nodes[id(chunk)] = nodes

        for cid in list(self._scenery_nodes.keys()):
            if cid not in current_ids:
                for np in self._scenery_nodes.pop(cid):
                    np.removeNode()


def main():
    app = Game()  # 기본값 easy=True. 원본 난이도로 하려면 Game(easy=False)
    app.run()


if __name__ == "__main__":
    main()
