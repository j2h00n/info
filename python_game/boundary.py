"""BoundaryWall.cs 대체: 좌우 쉴드 경계, 거리비례 알파, 이동범위 clamp."""


class Boundary:
    # 원본 Unity 값(34.5)은 9기둥 배치 기준. 지금은 3기둥(가로폭 23, 가장자리 기둥
    # 중심 ±8 + 반폭 3.5)이라 그 가장자리에 맞춰 좁힘 - 안 그러면 기둥 옆으로 그냥 빠져나감.
    LIMIT_X = 11.5
    WARNING_DISTANCE = 5.0

    def clamp_and_shield_alpha(self, bird_x):
        """bird_x clamp 결과와 (left_alpha, right_alpha)를 반환."""
        clamped_x = max(-self.LIMIT_X, min(self.LIMIT_X, bird_x))

        dist_to_left = abs(-self.LIMIT_X - clamped_x)
        alpha_left = max(0.0, min(1.0, 1.0 - dist_to_left / self.WARNING_DISTANCE))

        dist_to_right = abs(self.LIMIT_X - clamped_x)
        alpha_right = max(0.0, min(1.0, 1.0 - dist_to_right / self.WARNING_DISTANCE))

        return clamped_x, alpha_left, alpha_right
