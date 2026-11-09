using System;
using System.Collections.Generic;
using Game.Battle.Model.Simulation;
using Game.Campaign;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Flow
{
    /// <summary>세이브 ↔ 클라우드 세이브 JSON (기기·서버 공통 형식).</summary>
    public static class SaveJson
    {
        public static string Write(CampaignState state)
        {
            var mem = new InMemorySaveStore();
            new CampaignSaveService(mem).Save(state);
            return mem.Read(CampaignSaveService.DefaultKey);
        }

        public static CampaignState Read(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var mem = new InMemorySaveStore();
                mem.Write(CampaignSaveService.DefaultKey, json);
                return new CampaignSaveService(mem).Load();
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>서버 시각 = 기기 시각 + 차이 (서버 시각을 받으면 맞춘다). 시간에 달린 규칙(부상 회복 …)이 서버와 같게 계산된다.</summary>
    public sealed class ServerClock : ITimeProvider
    {
        private readonly ITimeProvider _device;
        public long OffsetSeconds { get; private set; }

        public ServerClock(ITimeProvider device) { _device = device; }

        public DateTimeOffset UtcNow => _device.UtcNow.AddSeconds(OffsetSeconds);

        public void Sync(long serverUnixSeconds) => OffsetSeconds = serverUnixSeconds - _device.UtcNow.ToUnixTimeSeconds();
    }

    public static class SyncOutcomes
    {
        /// <summary>서버 세이브를 받아 씀.</summary>
        public const string FromServer = "fromServer";
        /// <summary>처음 연결: 기기 진행이 더 많아 기기 세이브를 올림.</summary>
        public const string Uploaded = "uploaded";
        /// <summary>서버에 연결하지 못함: 기기 세이브 그대로.</summary>
        public const string Offline = "offline";
        /// <summary>전투가 서버에서 반영됨.</summary>
        public const string Accepted = "accepted";
        /// <summary>서버가 전투를 거절: 서버 세이브로 되돌림.</summary>
        public const string Rejected = "rejected";
    }

    public sealed class SyncResult
    {
        public string Outcome { get; internal set; }
        /// <summary>앞으로 쓸 진행 상태 (Offline이면 넘긴 기기 상태 그대로).</summary>
        public CampaignState State { get; internal set; }
        /// <summary>서버 Revision (Offline이면 그대로).</summary>
        public long Revision { get; internal set; }
        public BattleVerdict Verdict { get; internal set; }
    }

    /// <summary>
    /// 기기 쪽 동기화 흐름 (docs/impl/server.md 5장). 서버가 세이브의 주인이다 (R-40).
    /// 시작: 로그인 → 서버 시각 → 서버 세이브. 처음 연결이고 기기 진행(클리어한 스텝)이 더 많으면 기기 세이브를 올리고, 아니면 서버 세이브를 받아 쓴다.
    /// 전투 뒤: 결과를 제출하고, 서버가 반영한 세이브를 받아 쓴다. 거절되면 서버 세이브로 되돌린다. 서버에 닿지 않으면 기기 결과를 그대로 둔다.
    /// </summary>
    public sealed class CloudSync
    {
        private readonly IOnlineService _online;
        private readonly ServerClock _clock;

        public CloudSync(IOnlineService online, ServerClock clock)
        {
            _online = online;
            _clock = clock;
        }

        /// <param name="localRevision">이 기기가 마지막으로 맞춘 서버 Revision (한 번도 안 맞췄으면 0).</param>
        public void Start(CampaignState local, long localRevision, Action<SyncResult> done)
        {
            var offline = new SyncResult { Outcome = SyncOutcomes.Offline, State = local, Revision = localRevision };
            _online.SignIn(ok =>
            {
                if (!ok) { done(offline); return; }
                _online.GetTime(t =>
                {
                    if (t != null) _clock?.Sync(t.UnixSeconds);
                    _online.LoadSave(save =>
                    {
                        var server = save == null ? null : SaveJson.Read(save.Json);
                        if (server == null) { done(offline); return; }
                        bool firstLink = localRevision == 0;
                        if (firstLink && local != null && local.ClearedSteps.Count > server.ClearedSteps.Count)
                        {
                            _online.PutSave(new SavePutRequest { BaseRevision = save.Revision, Json = SaveJson.Write(local) }, put =>
                            {
                                if (put != null && put.Ok) done(new SyncResult { Outcome = SyncOutcomes.Uploaded, State = local, Revision = put.Current.Revision });
                                else done(new SyncResult { Outcome = SyncOutcomes.FromServer, State = server, Revision = save.Revision });
                            });
                            return;
                        }
                        done(new SyncResult { Outcome = SyncOutcomes.FromServer, State = server, Revision = save.Revision });
                    });
                });
            });
        }

        /// <summary>전투 제출 메시지 (출진 준비 결과 + 명령 기록 + 기기 결과).</summary>
        public static BattleSubmission SubmissionOf(SortiePlan plan, IReadOnlyList<Game.Battle.Model.Commands.BattleCommand> commands, BattleResult result, long baseRevision)
        {
            var s = new BattleSubmission
            {
                BaseRevision = baseRevision,
                StepId = plan.Step.Id,
                Seed = plan.Setup.Seed,
                Supply = plan.SupplyTaken,
                Commands = new List<Game.Battle.Model.Commands.BattleCommand>(commands),
                ClaimedHash = result.StateHash.ToString("x16"),
                ClaimedTicks = result.Ticks,
            };
            bool all = true;
            foreach (var c in plan.Setup.Corps)
                if (c.Team == StoryFlow.PlayerTeam)
                {
                    if (c.Delegated) s.DelegateGenerals.Add(c.General.Id);
                    else all = false;
                }
            if (all && s.DelegateGenerals.Count > 0) { s.Delegate = true; s.DelegateGenerals.Clear(); }
            foreach (var c in plan.Setup.Contests)
                s.ContestChoices[c.ContestId] = c.ChoicesA == null ? new List<string>() : new List<string>(c.ChoicesA);
            return s;
        }

        /// <param name="localAfter">기기에서 결과를 반영한 상태 (서버에 닿지 않으면 그대로 쓴다).</param>
        public void SubmitBattle(BattleSubmission submission, CampaignState localAfter, Action<SyncResult> done)
        {
            _online.SubmitBattle(submission, verdict =>
            {
                if (verdict == null) { done(new SyncResult { Outcome = SyncOutcomes.Offline, State = localAfter, Revision = submission.BaseRevision }); return; }
                var server = verdict.Save == null ? null : SaveJson.Read(verdict.Save.Json);
                if (verdict.Valid && server != null)
                {
                    done(new SyncResult { Outcome = SyncOutcomes.Accepted, State = server, Revision = verdict.Save.Revision, Verdict = verdict });
                    return;
                }
                if (server != null)
                {
                    done(new SyncResult { Outcome = SyncOutcomes.Rejected, State = server, Revision = verdict.Save.Revision, Verdict = verdict });
                    return;
                }
                // 거절 + 세이브 없음(지문 불일치 등): 서버 세이브를 다시 받아 되돌린다
                _online.LoadSave(save =>
                {
                    var s = save == null ? null : SaveJson.Read(save.Json);
                    done(s == null
                        ? new SyncResult { Outcome = SyncOutcomes.Offline, State = localAfter, Revision = submission.BaseRevision, Verdict = verdict }
                        : new SyncResult { Outcome = SyncOutcomes.Rejected, State = s, Revision = save.Revision, Verdict = verdict });
                });
            });
        }
    }

    /// <summary>
    /// 가짜 온라인 (서비스 설정 online = fake): 서버 없이 기기 안에서 같은 규칙(세이브 Revision, 전투 검증 R-40)을 흉내 낸다.
    /// 화면 흐름을 서버 없이 확인할 때 쓴다.
    /// </summary>
    public sealed class LocalOnlineService : IOnlineService
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly ISaveStore _persist;
        private CloudSave _save;

        public const string PersistKey = "fake-server";

        /// <param name="persist">가짜 서버의 세이브를 남겨 둘 곳 (없으면 메모리만: 앱을 다시 켜면 새 서버).</param>
        public LocalOnlineService(DefinitionSet defs, IDataSource source, ITimeProvider time, ISaveStore persist = null)
        {
            _defs = defs;
            _source = source;
            _time = time;
            _persist = persist;
            if (persist != null && persist.Exists(PersistKey))
                try { _save = OnlineJson.Read<CloudSave>(persist.Read(PersistKey)); } catch (Exception) { _save = null; }
        }

        private void Keep(CloudSave s)
        {
            _save = s;
            _persist?.Write(PersistKey, OnlineJson.Write(s));
        }

        public bool IsAvailable => true;
        public string AccountId => "local";
        public void SignIn(Action<bool> done) => done?.Invoke(true);
        public void GetTime(Action<ServerTimeResponse> done) => done?.Invoke(new ServerTimeResponse { UnixSeconds = _time.UtcNow.ToUnixTimeSeconds() });

        public void LoadSave(Action<CloudSave> done)
        {
            if (_save == null) Keep(new CloudSave { Revision = 1, Json = SaveJson.Write(StartingKitService.CreateNewGame(_defs)), SavedAtUnixSeconds = Now });
            done?.Invoke(Copy(_save));
        }

        public void PutSave(SavePutRequest request, Action<SavePutResponse> done)
        {
            long rev = _save?.Revision ?? 0;
            if (request == null || request.BaseRevision != rev || SaveJson.Read(request.Json) == null) { done?.Invoke(new SavePutResponse { Ok = false, Current = _save == null ? null : Copy(_save) }); return; }
            Keep(new CloudSave { Revision = rev + 1, Json = request.Json, SavedAtUnixSeconds = Now });
            done?.Invoke(new SavePutResponse { Ok = true, Current = Copy(_save) });
        }

        public void SubmitBattle(BattleSubmission s, Action<BattleVerdict> done)
        {
            LoadSave(_ => { });
            if (s == null || s.BaseRevision != _save.Revision) { done?.Invoke(new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = Copy(_save) }); return; }
            var state = SaveJson.Read(_save.Json);
            var v = new BattleVerifier(_defs, _source, _time).VerifyAndApply(state, s, out _);
            if (v.Valid)
            {
                Keep(new CloudSave { Revision = _save.Revision + 1, Json = SaveJson.Write(state), SavedAtUnixSeconds = Now });
                v.Save = Copy(_save);
            }
            done?.Invoke(v);
        }

        private long Now => _time.UtcNow.ToUnixTimeSeconds();
        private static CloudSave Copy(CloudSave s) => new CloudSave { Revision = s.Revision, Json = s.Json, SavedAtUnixSeconds = s.SavedAtUnixSeconds };
    }
}
