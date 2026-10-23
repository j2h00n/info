"""FollowEnvironment.cs 대체: 최초 오프셋을 유지하며 플레이어를 따라가는 배경/카메라 유틸."""


class FollowOffset:
    def __init__(self, initial_self_pos, initial_target_pos):
        # 생성 시점의 (따라가는 물체 위치 - 대상 위치)를 오프셋으로 저장.
        # 이후 대상이 움직여도 이 간격을 그대로 유지함.
        self.offset = (
            initial_self_pos[0] - initial_target_pos[0],
            initial_self_pos[1] - initial_target_pos[1],
            initial_self_pos[2] - initial_target_pos[2],
        )

    def follow(self, target_pos):
        """대상의 현재 위치를 받아, 따라가는 물체가 있어야 할 위치(대상+오프셋)를 반환."""
        return (
            target_pos[0] + self.offset[0],
            target_pos[1] + self.offset[1],
            target_pos[2] + self.offset[2],
        )
