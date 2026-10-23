"""BoundaryWall.cs 대체: 좌우 쉴드 경계, 거리비례 알파, 이동범위 clamp."""


class Boundary:
    # 원본 Unity 값(34.5)은 9기둥 배치 기준. 지금은 3기둥(가로폭 23, 가장자리 기둥
    # 중심 ±8 + 반폭 3.5)이라 그 가장자리에 맞춰 좁힘 - 안 그러면 기둥 옆으로 그냥 빠져나감.
    # (wall.py의 COLUMN_COUNT/PILLAR_WIDTH를 바꾸면 이 값도 같이 조정해야 함)
    LIMIT_X = 11.5            # 새가 갈 수 있는 좌우 한계(|x| <= LIMIT_X)
    WARNING_DISTANCE = 5.0    # 벽에 이 거리 안쪽으로 다가오면 쉴드가 보이기 시작

    def clamp_and_shield_alpha(self, bird_x):
        """bird_x clamp 결과와 (left_alpha, right_alpha)를 반환.
        alpha: 0=투명(멀리 있음) ~ 1=불투명(벽에 닿음). main.py가 쉴드 박스 투명도에 사용."""
        # 새 x좌표를 [-LIMIT_X, +LIMIT_X] 안으로 가둠
        clamped_x = max(-self.LIMIT_X, min(self.LIMIT_X, bird_x))

        # 왼쪽 벽까지 거리 -> 가까울수록 alpha 1에 가까워짐
        dist_to_left = abs(-self.LIMIT_X - clamped_x)
        alpha_left = max(0.0, min(1.0, 1.0 - dist_to_left / self.WARNING_DISTANCE))

        # 오른쪽 벽도 동일
        dist_to_right = abs(self.LIMIT_X - clamped_x)
        alpha_right = max(0.0, min(1.0, 1.0 - dist_to_right / self.WARNING_DISTANCE))

        return clamped_x, alpha_left, alpha_right
