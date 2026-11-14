using System.Linq;
using Game.Campaign;
using Game.Flow;
using Game.Online;
using Xunit;

namespace Game.Tests.Campaign
{
    /// <summary>R-86 개발 행동 (마스터): 자원 지급·모든 스텝 해금·장수 주기.</summary>
    public class DevActionTests
    {
        [Fact(DisplayName = "R-86 dev.grant: 군량·금·옥을 1 ~ 10억만큼 더한다. 다른 자원·0 이하·상한 넘으면 안 됨")]
        public void Grant()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            long supply = s.Wallet["supply"];
            Assert.True(runner.Apply(s, PlayerActionRunner.DevGrant("supply", 50000)));
            Assert.Equal(supply + 50000, s.Wallet["supply"]);
            Assert.True(runner.Apply(s, PlayerActionRunner.DevGrant("gem", 10)));
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGrant("wood", 10)));
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGrant("gold", 0)));
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGrant("gold", PlayerActionRunner.DevGrantMax + 1)));
        }

        [Fact(DisplayName = "R-86 dev.unlockAll: 모든 스텝을 깬 것으로 (다시 하면 바뀔 것이 없어 false)")]
        public void UnlockAll()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.True(runner.Apply(s, PlayerActionRunner.DevUnlockAll()));
            foreach (var id in Kit.D.StepIndex.Order) Assert.Contains(id, s.ClearedSteps);
            Assert.False(runner.Apply(s, PlayerActionRunner.DevUnlockAll()));
        }

        [Fact(DisplayName = "R-98 avatar.create: 닉네임 장수가 없을 때만 합류 (예전 세이브), 있으면 false")]
        public void AvatarCreate()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.False(runner.Apply(s, PlayerActionRunner.AvatarCreate())); // 새 게임은 처음 지급에 있음
            s.Generals.RemoveAll(g => g.GeneralId == AvatarGeneral.GeneralId);
            Assert.True(runner.Apply(s, PlayerActionRunner.AvatarCreate()));
            Assert.Equal(1, s.Generals.Count(g => g.GeneralId == AvatarGeneral.GeneralId));
        }

        [Fact(DisplayName = "R-86 dev.giveGeneral: 없는 장수를 합류시킨다 (\"*\" = 아직 없는 모든 장수), 이미 있으면 false")]
        public void GiveGeneral()
        {
            var runner = new PlayerActionRunner(Kit.D);
            var s = StartingKitService.CreateNewGame(Kit.D);
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGiveGeneral(s.Generals[0].GeneralId)));
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGiveGeneral("no_such_general")));
            Assert.True(runner.Apply(s, PlayerActionRunner.DevGiveGeneral("*")));
            foreach (var id in Kit.D.Generals.Keys) if (id != AvatarGeneral.HiredId) Assert.NotNull(s.FindGeneral(id)); // 고용 틀(avatar_hired)은 주지 않는다
            Assert.Null(s.FindGeneral(AvatarGeneral.HiredId));
            Assert.False(runner.Apply(s, PlayerActionRunner.DevGiveGeneral("*")));
            Assert.True(ActionKinds.IsDev(ActionKinds.DevGrant));
            Assert.False(ActionKinds.IsDev(ActionKinds.MailClaim));
        }
    }
}
