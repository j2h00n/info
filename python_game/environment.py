"""FollowEnvironment.cs 대체: 최초 오프셋을 유지하며 플레이어를 따라가는 배경/카메라 유틸."""


class FollowOffset:
    def __init__(self, initial_self_pos, initial_target_pos):
        self.offset = (
            initial_self_pos[0] - initial_target_pos[0],
            initial_self_pos[1] - initial_target_pos[1],
            initial_self_pos[2] - initial_target_pos[2],
        )

    def follow(self, target_pos):
        return (
            target_pos[0] + self.offset[0],
            target_pos[1] + self.offset[1],
            target_pos[2] + self.offset[2],
        )
