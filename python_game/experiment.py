"""실험 모드 진입점: main.py와 완전히 동일한 게임을 그대로 재사용하되
난이도(전진속도/중력/점프력/벽 간격/틈 크기)만 낮춰서 실행.

로직/구조를 복제하지 않고 Game(easy=True)로 기존 코드를 그대로 재사용함 -
난이도 수치는 bird.py(Bird.__init__)와 wall.py(WallSpawner.__init__)의
easy 분기에서 관리.
"""
from .main import Game


def main():
    app = Game(easy=True)
    app.run()


if __name__ == "__main__":
    main()
