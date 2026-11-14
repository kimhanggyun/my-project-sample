namespace Game.Flow
{
    /// <summary>
    /// 닉네임 장수 (hiring-v2.md 2장, verification R-98): 플레이어 자신. 모든 유저가 같은 틀(avatar)을 쓰고 이름은 닉네임.
    /// 용병으로 등록할 수 있는 장수는 이 장수뿐이고, 남의 닉네임 장수를 고용하면 avatar_hired 틀로 바뀐다 (내 닉네임 장수와 겹치지 않게).
    /// </summary>
    public static class AvatarGeneral
    {
        public const string GeneralId = "avatar";
        public const string HiredId = "avatar_hired";

        public static bool IsAvatar(string generalId) => generalId == GeneralId || generalId == HiredId;
    }
}
