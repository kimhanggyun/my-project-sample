using System;
using System.IO;
using Game.Online;
using Xunit;

namespace Game.Server.Tests
{
    /// <summary>폴더 저장소: 배포가 바뀌는 동안 옛 · 새 서버가 같은 폴더를 함께 써도 서로의 기록을 덮어쓰지 않는다 (2026-11-16).</summary>
    public class FolderStoreTests
    {
        private static string NewDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "folderstore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact(DisplayName = "폴더 저장소: 새 서버가 켜진 뒤 옛 서버가 쓴 우편 · 세이브를 새 서버가 다시 읽고, 새 서버가 써도 옛 기록이 남는다")]
        public void OtherInstanceWritesAreReloaded()
        {
            string dir = NewDir();
            try
            {
                var old = new FolderStore(dir) { CheckEvery = TimeSpan.Zero };
                var who = old.CreateGuest();
                var neu = new FolderStore(dir) { CheckEvery = TimeSpan.Zero }; // 새 판이 켜지며 읽음

                // 옛 서버가 그 뒤에 쓴다 (켜진 새 서버는 아직 모름)
                old.AddMail("g", who.AccountId, new Mail { Kind = MailKinds.AdminGift, Resource = "gem", Amount = 5 });
                var (ok, _) = old.PutSave("g", who.AccountId, 0, "{}", 100);
                Assert.True(ok);

                // 새 서버: 다시 읽어 옛 서버의 우편 · 세이브를 본다
                Assert.Single(neu.MailOf("g", who.AccountId));
                long rev = neu.GetSave("g", who.AccountId).Revision;
                Assert.Equal(old.GetSave("g", who.AccountId).Revision, rev);

                // 새 서버가 쓰면 옛 서버 기록 위에 쓴다 (덮어써 지우지 않음)
                neu.AddMail("g", who.AccountId, new Mail { Kind = MailKinds.AdminGift, Resource = "gold", Amount = 1 });
                Assert.Equal(2, new FolderStore(dir).MailOf("g", who.AccountId).Count);
                Assert.Equal(2, old.MailOf("g", who.AccountId).Count); // 옛 서버도 다시 읽음
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Fact(DisplayName = "폴더 저장소: 혼자 쓰면 판 표시가 내 것이라 다시 읽지 않는다 (메모리 그대로)")]
        public void OwnWritesDoNotReload()
        {
            string dir = NewDir();
            try
            {
                var s = new FolderStore(dir) { CheckEvery = TimeSpan.Zero };
                var who = s.CreateGuest();
                s.AddMail("g", who.AccountId, new Mail { Kind = MailKinds.AdminGift, Resource = "gem", Amount = 5 });
                Assert.Single(s.MailOf("g", who.AccountId));
                Assert.True(File.Exists(Path.Combine(dir, "store.gen")));
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }
    }
}
