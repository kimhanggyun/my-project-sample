using System.Collections.Generic;
using Game.Data.Json;
using Game.Foundation.Save;
using Game.Online;
using Newtonsoft.Json.Linq;

namespace Game.Flow
{
    /// <summary>앱 시작 단계 id (startup.md 1장).</summary>
    public static class StartupSteps
    {
        public const string Splash = "splash";
        public const string VersionCheck = "versionCheck";
        public const string SignIn = "signIn";
        public const string Terms = "terms";
        public const string Title = "title";
    }

    /// <summary>로그인 버튼 하나 (데이터). 새 로그인 수단은 구현 + 여기 한 줄 (startup.md 4장).</summary>
    public sealed class SignInOption
    {
        public string Id { get; internal set; }
        public string NameKey { get; internal set; }
        /// <summary>active / tobe / hidden.</summary>
        public string Status { get; internal set; }
        public bool IsActive => Status == "active";
    }

    /// <summary>기기에 남기는 시작 기록: 동의한 약관 판, 마지막 로그인 수단 (다음 실행에 자동 로그인).</summary>
    public sealed class StartupRecord
    {
        public const string SaveKey = "startup";
        public int TermsVersion { get; set; }
        public long TermsAcceptedAtUnixSeconds { get; set; }
        public string SignInMethod { get; set; }
    }

    /// <summary>
    /// 앱 시작 순서 (startup.md, 데이터 Ui/startup.json): 스플래시 → 버전 확인(서버 전까지 건너뜀) → 로그인(처음이거나 수단이 없을 때) →
    /// 약관 동의(처음이거나 약관 판이 오르면) → 타이틀. 화면은 지금 단계를 마칠 때 <see cref="Next"/>로 다음 단계를 묻는다.
    /// </summary>
    public sealed class StartupFlow
    {
        public const string File = "Ui/startup.json";

        public IReadOnlyList<string> Steps { get; private set; }
        public List<SignInOption> SignInOptions { get; private set; }
        public int TermsVersion { get; private set; }
        /// <summary>스플래시 시간 (밀리초).</summary>
        public int SplashMillis { get; private set; }

        public static StartupFlow Load(IDataSource source)
        {
            var o = JsonUtil.ParseObject(source.ReadText(File));
            var steps = JsonUtil.StringList(o, "steps");
            foreach (var st in steps)
                if (st != StartupSteps.Splash && st != StartupSteps.VersionCheck && st != StartupSteps.SignIn && st != StartupSteps.Terms && st != StartupSteps.Title)
                    throw new DataException(File + ": 모르는 단계 '" + st + "'.");
            if (steps.Count == 0 || steps[steps.Count - 1] != StartupSteps.Title) throw new DataException(File + ": steps는 title로 끝나야 합니다.");
            var options = new List<SignInOption>();
            foreach (JObject s in (JArray)o["signIn"] ?? new JArray())
                options.Add(new SignInOption { Id = JsonUtil.RequireString(s, "id", File), NameKey = JsonUtil.RequireString(s, "nameKey", File), Status = s.Value<string>("status") ?? "active" });
            if (steps.Contains(StartupSteps.SignIn) && !options.Exists(x => x.IsActive)) throw new DataException(File + ": 쓸 수 있는 로그인 수단이 하나는 있어야 합니다.");
            return new StartupFlow
            {
                Steps = steps,
                SignInOptions = options,
                TermsVersion = (int)JsonUtil.RequireLong(o, "termsVersion", File),
                SplashMillis = (int)(o.Value<decimal?>("splashSeconds") * 1000 ?? 1500),
            };
        }

        /// <summary>
        /// 지금 단계 다음 (처음이면 current = null). 건너뛰는 단계: 버전 확인(서버가 없으면), 로그인(전에 로그인한 수단이 아직 쓸 수 있으면 자동),
        /// 약관(지금 판에 이미 동의). 마지막은 늘 title.
        /// </summary>
        public string Next(string current, StartupRecord record, bool hasServer)
        {
            int i = current == null ? -1 : IndexOf(current);
            for (int k = i + 1; k < Steps.Count; k++)
                if (!Skip(Steps[k], record, hasServer)) return Steps[k];
            return StartupSteps.Title;
        }

        private int IndexOf(string step)
        {
            for (int i = 0; i < Steps.Count; i++) if (Steps[i] == step) return i;
            return Steps.Count - 1;
        }

        private bool Skip(string step, StartupRecord r, bool hasServer)
        {
            switch (step)
            {
                case StartupSteps.VersionCheck: return !hasServer; // 서버 전까지는 자리만 (startup.md 3장)
                case StartupSteps.SignIn: return r?.SignInMethod != null && SignInOptions.Exists(o => o.Id == r.SignInMethod && o.IsActive);
                case StartupSteps.Terms: return r != null && r.TermsVersion >= TermsVersion;
                default: return false;
            }
        }

        public static StartupRecord ReadRecord(ISaveStore store)
        {
            try { return store.Exists(StartupRecord.SaveKey) ? OnlineJson.Read<StartupRecord>(store.Read(StartupRecord.SaveKey)) ?? new StartupRecord() : new StartupRecord(); }
            catch (System.Exception) { return new StartupRecord(); }
        }

        public static void WriteRecord(ISaveStore store, StartupRecord record) => store.Write(StartupRecord.SaveKey, OnlineJson.Write(record));
    }
}
