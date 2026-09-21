"""Flappy-Bird류 3D 러너 Panda3D 이식판. Unity 원본의 BirdController/MotionController/
BoundaryWall/FollowEnvironment/PythonLauncher 로직을 통합한 실행 엔트리포인트."""
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
    def __init__(self, easy=False):
        ShowBase.__init__(self)

        # CC0 배경 애셋(glTF)이 PBR 머티리얼을 써서 기본 파이프라인으론 검게 나옴 - simplepbr로 보정
        try:
            import simplepbr
            simplepbr.init()
        except ImportError:
            pass

        # 실험 모드: 로직/구조는 원본과 동일, 체감 난이도(속도/중력/틈/간격)만 낮춤
        self.easy = easy

        props = WindowProperties()
        props.setTitle("Flappy Runner 3D (Python)" + (" - 실험 모드" if easy else ""))
        props.setSize(1600, 900)
        self.win.requestProperties(props)

        # Unity 원본 Main Camera: field of view: 60, near clip: 0.3, far clip: 1000
        self.camLens.setFov(60)
        self.camLens.setNearFar(0.3, 1000)

        self._setup_lights()
        self._korean_font = self.loader.loadFont("/c/Windows/Fonts/malgun.ttf")

        self.bird_logic = Bird(easy=easy)
        self.boundary = Boundary()
        self.walls = WallSpawner(easy=easy)
        self.scenery = SceneryManager()
        self.motion = MotionCapture()
        self.motion_learning_rate = 1.5

        self.motion_enabled = prefs.get_int("MotionMode", 0) == 1
        if self.motion_enabled:
            self.motion.start()

        self._build_scene()
        self._setup_audio()
        self._build_ui()
        self._build_motion_preview()
        self._bind_input()

        self.taskMgr.add(self.update, "update")

    # ---------- audio ----------
    def _setup_audio(self):
        assets_dir = os.path.join(os.path.dirname(os.path.dirname(__file__)), "Assets")
        jump_path = os.path.join(
            assets_dir, "Game Sound Solutions - 8 bits Elements", "jump", "jump_15.wav"
        )
        bgm_path = os.path.join(
            assets_dir, "RedsenGameMusic_Afternoon", "RedsenGameMusic_Afternoon_Cute_Casual_Loopable.wav"
        )

        self.jump_sfx = None
        if os.path.exists(jump_path):
            self.jump_sfx = self.loader.loadSfx(Filename.from_os_specific(jump_path))
        self.bgm_sfx = None
        if os.path.exists(bgm_path):
            self.bgm_sfx = self.loader.loadSfx(Filename.from_os_specific(bgm_path))

        self.jump_volume = prefs.get_float("JumpVolume", 1.0)
        self.music_volume = prefs.get_float("MusicVolume", 1.0)

        if self.jump_sfx:
            self.jump_sfx.setVolume(self.jump_volume)
        if self.bgm_sfx:
            self.bgm_sfx.setVolume(self.music_volume)
            self.bgm_sfx.setLoop(True)
            self.bgm_sfx.play()

    def _play_jump_sfx(self):
        if self.jump_sfx:
            self.jump_sfx.play()

    def _play_flap_animation(self):
        for wing_np, sign in ((self.wing_neg, -1), (self.wing_pos, 1)):
            if wing_np is None:
                continue
            Sequence(
                LerpHprInterval(wing_np, 0.08, (0, 0, sign * 45)),
                LerpHprInterval(wing_np, 0.15, (0, 0, 0)),
            ).start()

    # ---------- scene ----------
    def _setup_lights(self):
        alight = AmbientLight("ambient")
        alight.setColor((0.4, 0.4, 0.45, 1))
        self.render.setLight(self.render.attachNewNode(alight))

        dlight = DirectionalLight("sun")
        dlight.setColor((0.9, 0.9, 0.85, 1))
        dlnp = self.render.attachNewNode(dlight)
        dlnp.setHpr(45, -60, 0)
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

        self.left_shield = make_box("left_shield", 0.3, 6, 10, color=(0.3, 0.7, 1, 0.6))
        self.right_shield = make_box("right_shield", 0.3, 6, 10, color=(0.3, 0.7, 1, 0.6))
        for shield in (self.left_shield, self.right_shield):
            shield.reparentTo(self.render)
            shield.setTransparency(TransparencyAttrib.M_alpha)
            if shield_tex is not None:
                shield.setTexture(shield_tex)

        # 바닥: 파이프가 내려가는 최저 높이(컬럼별로 랜덤이라 "가장 안 내려가는" 케이스
        # 기준)보다 살짝 높은 곳에 둬서, 파이프 밑으로 저공비행해서 빠져나가는 구멍이
        # 안 생기게 함. 시각적 바닥과 충돌판정 높이를 일치시킴(FLOOR_Y).
        self.FLOOR_Y = -7.6
        self.CEILING_Y = 10.3
        ground = make_box("ground", 200, 4000, 1, color=(0.3, 0.55, 0.28, 1))
        ground.setPos(0, 2000, self.FLOOR_Y)
        ground.reparentTo(self.render)

        self._chunk_nodes = {}  # id(chunk) -> list[NodePath]
        self._scenery_nodes = {}  # id(chunk) -> list[NodePath]

    def _build_ui(self):
        # 인게임 HUD: ScoreText(anchor 0,1 top-left)/WallText(anchor 1,1 top-right), 둘다 anchoredPos(0,0)
        aspect = self.get_aspect_ratio()
        inset = 0.05  # 화면 정가장자리(0 오프셋)라 살짝 안쪽으로 마진만 줌
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
        k = 2.0 / 900.0

        # StartMenu: sizeDelta (384.62, 392.05), anchoredPos ~ (0,0)
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
        self._motion_box_on_color = (0.2, 0.8, 0.3, 1)
        self._motion_box_off_color = (0.3, 0.3, 0.3, 1)
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
        self.start_button = DirectButton(
            parent=self.start_menu, pos=(1.6 * k, 0, -128 * k), text="시작", text_font=self._korean_font,
            relief=DGG.FLAT, text_scale=0.06,
            frameSize=(-93.41 / 2 * k, 93.41 / 2 * k, -22.4 / 2 * k, 22.4 / 2 * k),
            frameColor=(0.2, 0.7, 0.3, 1), command=self._on_space,
        )

        # Restart 버튼: Canvas 직속(StartMenu 자식 아님), anchoredPos (0,0), sizeDelta (160,30)
        self.restart_button = DirectButton(
            pos=(0, 0, 0), text="RESTART!", text_font=self._korean_font, text_scale=0.07,
            relief=DGG.FLAT, frameSize=(-160 / 2 * k, 160 / 2 * k, -30 / 2 * k, 30 / 2 * k),
            frameColor=(0.2, 0.85, 0.2, 1), command=self._restart,
        )
        self.restart_button.hide()

    def _build_motion_preview(self):
        """웹캠 미리보기를 별도 창이 아니라 게임 창 우하단에 작게 띄움."""
        self._motion_tex = Texture("motion_preview")
        self._motion_tex_size = None
        aspect = self.get_aspect_ratio()
        self.motion_preview = OnscreenImage(
            image=self._motion_tex, pos=(aspect - 0.32, 0, -0.72), scale=(0.3, 1, 0.22),
        )
        if self.motion_enabled:
            self.motion_preview.show()
        else:
            self.motion_preview.hide()

    def _update_motion_preview(self):
        frame = self.motion.last_frame
        if frame is None:
            return
        h, w = frame.shape[:2]
        if self._motion_tex_size != (w, h):
            self._motion_tex.setup2dTexture(w, h, Texture.T_unsigned_byte, Texture.F_rgb)
            self._motion_tex_size = (w, h)
        flipped = frame[::-1]  # Panda 텍스처는 아래->위 순서라 세로 뒤집어서 넣음
        self._motion_tex.setRamImageAs(flipped.tobytes(), "BGR")

    def _bind_input(self):
        self._keys = {"left": False, "right": False}
        self.accept("space", self._on_space)
        self.accept("m", self._toggle_motion)
        self.accept("r", self._restart)
        self.accept("arrow_left", self._set_key, ["left", True])
        self.accept("arrow_left-up", self._set_key, ["left", False])
        self.accept("arrow_right", self._set_key, ["right", True])
        self.accept("arrow_right-up", self._set_key, ["right", False])

    def _set_key(self, name, value):
        self._keys[name] = value

    def _on_space(self):
        if not self.bird_logic.is_game_started:
            self.bird_logic.start_game()
            self.start_menu.hide()
        else:
            if self.bird_logic.jump():
                self._play_jump_sfx()
                self._play_flap_animation()

    def _toggle_motion(self):
        self._set_motion_enabled(not self.motion_enabled)

    def _set_motion_enabled(self, enabled):
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
        self.jump_volume = self.jump_slider["value"]
        if self.jump_sfx:
            self.jump_sfx.setVolume(self.jump_volume)
        prefs.set_float("JumpVolume", self.jump_volume)
        prefs.save()

    def _on_music_volume_changed(self):
        self.music_volume = self.music_slider["value"]
        if self.bgm_sfx:
            self.bgm_sfx.setVolume(self.music_volume)
        prefs.set_float("MusicVolume", self.music_volume)
        prefs.save()

    def _restart(self):
        self.bird_logic.restart()
        self.walls = WallSpawner(easy=self.easy)
        for nodes in self._chunk_nodes.values():
            for np in nodes:
                np.removeNode()
        self._chunk_nodes = {}

        self.scenery = SceneryManager()
        for nodes in self._scenery_nodes.values():
            for np in nodes:
                np.removeNode()
        self._scenery_nodes = {}

        self.restart_button.hide()
        self.start_menu.show()

    # ---------- per-frame ----------
    def update(self, task):
        dt = globalClock.getDt()

        move_x = 1.0 if self._keys["left"] else (-1.0 if self._keys["right"] else 0.0)
        if self.motion_enabled:
            jumped = self.motion.update(self.motion_learning_rate, dt)
            self._update_motion_preview()
            if jumped and self.bird_logic.jump():
                self._play_jump_sfx()
                self._play_flap_animation()
            motion_x = self.motion.get_move_x()
            move_x = motion_x if abs(motion_x) > abs(move_x) else move_x

        self.bird_logic.update(dt, move_x)

        clamped_x, alpha_l, alpha_r = self.boundary.clamp_and_shield_alpha(self.bird_logic.position[0])
        self.bird_logic.position[0] = clamped_x

        bx, by, bz = self.bird_logic.position
        self.bird_np.setPos(bx, bz, by)
        self.bird_np.setR(self.bird_logic.roll)

        self.left_shield.setPos(-Boundary.LIMIT_X, bz, by)
        self.right_shield.setPos(Boundary.LIMIT_X, bz, by)
        self.left_shield.setAlphaScale(alpha_l)
        self.right_shield.setAlphaScale(alpha_r)
        self.left_shield.show() if alpha_l > 0 else self.left_shield.hide()
        self.right_shield.show() if alpha_r > 0 else self.right_shield.hide()

        # Unity 원본: 카메라가 새(Cube)의 자식으로 로컬 오프셋 (0, +2, -7)에 고정.
        self.camera.setPos(bx, bz - 8, by + 4)
        self.camera.lookAt(bx, bz, by)

        self.scenery.update(bz)
        self._sync_scenery_nodes()

        if self.bird_logic.is_game_started and not self.bird_logic.is_game_over:
            self.walls.update(bz)
            self._sync_wall_nodes()

            if self.walls.check_collision(bx, by, bz, bird_half=0.5):
                self.bird_logic.on_collision(is_wall_cube=True)

            # 저공비행/고공비행으로 파이프 위아래를 그냥 뚫고 지나가는 것 방지
            if by < self.FLOOR_Y or by > self.CEILING_Y:
                self.bird_logic.on_collision(is_wall_cube=True)

        self.score_text.setText(f"Score : {self.bird_logic.score}")
        self.wall_text.setText(f"Wall : {self.bird_logic.passed_walls}")

        if self.bird_logic.is_game_over and self.restart_button.isHidden():
            self.restart_button.show()

        return task.cont

    def _sync_wall_nodes(self):
        current_ids = set()
        for chunk in self.walls.chunks:
            current_ids.add(id(chunk))
            if id(chunk) in self._chunk_nodes:
                continue
            nodes = []
            for pipe in chunk:
                np = make_box("pipe", pipe.sx, pipe.sz, pipe.sy, color=(0.85, 0.75, 0.35, 1))
                np.reparentTo(self.render)
                np.setPos(pipe.x, pipe.z, pipe.y)
                nodes.append(np)
            self._chunk_nodes[id(chunk)] = nodes

        for cid in list(self._chunk_nodes.keys()):
            if cid not in current_ids:
                for np in self._chunk_nodes.pop(cid):
                    np.removeNode()

    def _sync_scenery_nodes(self):
        current_ids = set()
        for chunk in self.scenery.chunks:
            current_ids.add(id(chunk))
            if id(chunk) in self._scenery_nodes:
                continue
            nodes = []
            for item in chunk:
                np = spawn_instance(item.filename, self.render)
                if np is None:
                    continue
                np.setPos(item.x, item.z, item.y)
                np.setScale(item.scale)
                np.setH(item.heading)
                nodes.append(np)
            self._scenery_nodes[id(chunk)] = nodes

        for cid in list(self._scenery_nodes.keys()):
            if cid not in current_ids:
                for np in self._scenery_nodes.pop(cid):
                    np.removeNode()


def main():
    app = Game()
    app.run()


if __name__ == "__main__":
    main()
