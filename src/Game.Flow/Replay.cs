using System;
using System.Collections.Generic;
using Game.Battle.Model.Commands;
using Game.Battle.Model.Simulation;
using Game.Data.Json;
using Game.Data.Defs;
using Game.Foundation.Save;
using Game.Foundation.Math;
using Newtonsoft.Json;

namespace Game.Flow
{
    /// <summary>
    /// 전투 녹화 하나 (replay.md 2장): 영상이 아니라 전투 기록. 전투 설정(스텝 규칙을 뺀 것) + 명령 기록 + 결과 지문.
    /// 스텝 규칙(데이터·맵·보정·스위치)은 재생할 때 지금 데이터로 다시 채우고, 결과 지문이 다르면 "규칙이 바뀌어 다시 볼 수 없음" (4장).
    /// </summary>
    public sealed class ReplayRecord
    {
        public const int CurrentFormat = 1;
        public int Format { get; set; } = CurrentFormat;
        public string Id { get; set; }
        public string StepId { get; set; }
        public long SavedAtUnixSeconds { get; set; }
        public bool Won { get; set; }
        public string EndReason { get; set; }
        public long Ticks { get; set; }
        /// <summary>전투 시간 (초, 버림).</summary>
        public long Seconds { get; set; }
        public long PlayerStartTroops { get; set; }
        public long PlayerLosses { get; set; }
        /// <summary>고용(용병)을 데리고 나간 전투 (14 분류 merc).</summary>
        public bool Hired { get; set; }
        /// <summary>결과 상태 지문 (16진). 다시 돌린 결과와 같아야 재생할 수 있다.</summary>
        public string Hash { get; set; }
        /// <summary>스텝 규칙(Defs·Map·Balance·Combat·Flags)은 비어 있다.</summary>
        public BattleSetup Setup { get; set; }
        public List<BattleCommand> Commands { get; set; } = new List<BattleCommand>();
    }

    public sealed class ReplayRules
    {
        public const string File = "Rules/replay.json";
        public int MaxSaved { get; private set; } = 20;
        /// <summary>저장 안 한 최근 전투를 저절로 남기는 수 (R-101).</summary>
        public int RecentKeep { get; private set; } = 5;
        /// <summary>공유 녹화 목록에 보이는 개수 (스텝마다, R-81).</summary>
        public int SharedListMax { get; private set; } = 20;
        /// <summary>서버가 게임마다 남기는 공유 녹화 수 (넘으면 오래된 것부터 지움).</summary>
        public int SharedStoreMax { get; private set; } = 500;

        public static ReplayRules Load(IDataSource source)
        {
            var o = JsonUtil.ParseObject(source.ReadText(File));
            int max = (int)JsonUtil.RequireLong(o, "maxSaved", File);
            if (max < 1) throw new DataException(File + ": maxSaved는 1 이상이어야 합니다.");
            return new ReplayRules
            {
                MaxSaved = max,
                RecentKeep = o.ContainsKey("recentKeep") ? System.Math.Max(0, (int)JsonUtil.RequireLong(o, "recentKeep", File)) : 5,
                SharedListMax = o.ContainsKey("sharedListMax") ? (int)JsonUtil.RequireLong(o, "sharedListMax", File) : 20,
                SharedStoreMax = o.ContainsKey("sharedStoreMax") ? (int)JsonUtil.RequireLong(o, "sharedStoreMax", File) : 500,
            };
        }
    }

    public static class ReplayStatus
    {
        public const string Ok = "ok";
        /// <summary>스텝이 없어졌거나 기록을 읽을 수 없음.</summary>
        public const string Broken = "broken";
        /// <summary>다시 돌린 결과가 녹화와 다름 (규칙·데이터가 바뀜).</summary>
        public const string VersionChanged = "versionChanged";
    }

    /// <summary>다시 돌릴 준비: 전투 설정과 명령 기록 (틱 순서). Status가 Ok일 때만 쓴다.</summary>
    public sealed class ReplayPlayback
    {
        public string Status { get; internal set; }
        public BattleSetup Setup { get; internal set; }
        public List<BattleCommand> Commands { get; internal set; } = new List<BattleCommand>();
        public bool Ok => Status == ReplayStatus.Ok;

        /// <summary>틱이 된 명령을 넣는다 (<see cref="BattleSimulation.Run"/>과 같은 순서: 넣고 나서 Step). 다음에 볼 순번을 돌려준다.</summary>
        public int IssueDue(BattleSimulation sim, int next)
        {
            while (next < Commands.Count && Commands[next].Tick <= sim.Tick) sim.Issue(Commands[next++]);
            return next;
        }
    }

    public sealed class ReplayService
    {
        private readonly StoryFlow _flow;

        public ReplayService(StoryFlow flow) { _flow = flow; }

        /// <summary>끝난 전투를 녹화로 (출진 설정은 사본으로 떠 둔다).</summary>
        public static ReplayRecord Capture(SortiePlan plan, IReadOnlyList<BattleCommand> commands, BattleResult result, long nowUnixSeconds)
        {
            var bare = BareSetup(plan.Setup);
            long start = 0, lost = 0;
            foreach (var c in result.Corps)
                if (c.Team == StoryFlow.PlayerTeam) { start += c.StartTroops; lost += c.Losses; }
            var record = new ReplayRecord
            {
                StepId = plan.Step.Id,
                SavedAtUnixSeconds = nowUnixSeconds,
                Won = result.Winner == StoryFlow.PlayerTeam,
                EndReason = result.EndReason,
                Ticks = result.Ticks,
                Seconds = result.ElapsedSeconds.FloorToLong(),
                PlayerStartTroops = start,
                PlayerLosses = lost,
                Hired = plan.Hired != null && !plan.Hired.IsEmpty,
                Hash = result.StateHash.ToString("x16"),
                Setup = bare,
                Commands = new List<BattleCommand>(commands ?? (IReadOnlyList<BattleCommand>)new List<BattleCommand>()),
            };
            return Copy(record); // 출진 설정과 떼어 놓는다
        }

        /// <summary>스텝 규칙(Defs·Map·Balance·Combat·Flags)을 뺀 전투 설정 (얕은 사본, 저장할 때 JSON으로 떼어 낸다).</summary>
        /// <summary>분류 (14 칩): territory (영토 전투) · merc (고용을 데리고 나감) · story.</summary>
        public static string CategoryOf(ReplayRecord r)
        {
            if (r?.StepId != null && r.StepId.StartsWith("territory", StringComparison.Ordinal)) return "territory";
            return r != null && r.Hired ? "merc" : "story";
        }

        public static BattleSetup BareSetup(BattleSetup s) => new BattleSetup
        {
            Seed = s.Seed,
            MaxMinutes = s.MaxMinutes,
            DefeatConditions = s.DefeatConditions,
            Corps = s.Corps,
            Objects = s.Objects,
            HoldOut = s.HoldOut,
            InitialExplored = s.InitialExplored,
            Contests = s.Contests,
            EncounterContests = s.EncounterContests,
            Wind = s.Wind,
            Weather = s.Weather,
            TimeOfDay = s.TimeOfDay,
            BattleType = s.BattleType,
            Changes = s.Changes,
            Events = s.Events,
        };

        public static ReplayRecord Copy(ReplayRecord r) => Read(Write(r));
        public static string Write(ReplayRecord r) => ReplayJson.Write(r);
        public static ReplayRecord Read(string json) => ReplayJson.Read<ReplayRecord>(json);

        /// <summary>재생 준비: 스텝 규칙을 지금 데이터로 채운다. 지문 확인은 <see cref="Check"/>.</summary>
        public ReplayPlayback Prepare(ReplayRecord r)
        {
            var p = new ReplayPlayback { Status = ReplayStatus.Broken };
            if (r?.Setup == null || r.StepId == null || r.Format != ReplayRecord.CurrentFormat) return p;
            try
            {
                var copy = Copy(r);
                _flow.ApplyStepRules(copy.Setup, _flow.LoadStep(r.StepId));
                p.Setup = copy.Setup;
                p.Commands = StableByTick(copy.Commands);
                p.Status = ReplayStatus.Ok;
            }
            catch (Exception) { p.Setup = null; p.Status = ReplayStatus.Broken; }
            return p;
        }

        /// <summary>끝까지 한 번 돌려 녹화 지문과 같은지 본다. 같으면 Ok인 재생 준비를 (새로) 돌려준다.</summary>
        public ReplayPlayback Check(ReplayRecord r)
        {
            var probe = Prepare(r);
            if (!probe.Ok) return probe;
            BattleResult result;
            try { result = BattleSimulation.Run(probe.Setup, probe.Commands); }
            catch (Exception) { return new ReplayPlayback { Status = ReplayStatus.Broken }; }
            if (result.StateHash.ToString("x16") != r.Hash) return new ReplayPlayback { Status = ReplayStatus.VersionChanged };
            return Prepare(r);
        }

        private static List<BattleCommand> StableByTick(List<BattleCommand> list)
        {
            var indexed = new List<(BattleCommand c, int i)>();
            for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
            indexed.Sort((a, b) => a.c.Tick != b.c.Tick ? a.c.Tick.CompareTo(b.c.Tick) : a.i.CompareTo(b.i));
            var sorted = new List<BattleCommand>();
            foreach (var x in indexed) sorted.Add(x.c);
            return sorted;
        }
    }

    /// <summary>내 녹화 목록 (기기에만, replay.md 3장). 새것이 앞. 한도를 넘으면 가장 오래된 것부터 지운다.</summary>
    public sealed class ReplayLibrary
    {
        public const string SaveKey = "replays";
        public long NextNumber { get; set; } = 1;
        public List<ReplayRecord> Items { get; set; } = new List<ReplayRecord>();
        /// <summary>저장 안 한 최근 전투 (새것이 앞, R-101). [저장]하면 Items로 옮긴다.</summary>
        public List<ReplayRecord> Recent { get; set; } = new List<ReplayRecord>();

        /// <summary>끝난 전투를 최근에 더한다 (id를 붙이고, keep개를 넘으면 가장 오래된 것을 밀어낸다, R-101).</summary>
        public void AddRecent(ReplayRecord r, int keep)
        {
            r.Id = "rp" + NextNumber++;
            if (keep <= 0) return;
            Recent.Insert(0, r);
            while (Recent.Count > keep) Recent.RemoveAt(Recent.Count - 1);
        }

        /// <summary>최근 녹화를 저장 목록으로 옮긴다 (id 그대로). 없으면 false. 한도를 넘으면 가장 오래된 저장을 지운다.</summary>
        public bool Keep(string id, int maxSaved)
        {
            var r = Recent.Find(x => x.Id == id);
            if (r == null) return false;
            Recent.Remove(r);
            Items.Insert(0, r);
            while (Items.Count > Math.Max(1, maxSaved)) Items.RemoveAt(Items.Count - 1);
            return true;
        }

        /// <summary>더하고 id를 붙인다. 한도를 넘어 지운 개수를 돌려준다.</summary>
        public int Add(ReplayRecord r, int maxSaved)
        {
            r.Id = "rp" + NextNumber++;
            Items.Insert(0, r);
            int removed = 0;
            while (Items.Count > Math.Max(1, maxSaved)) { Items.RemoveAt(Items.Count - 1); removed++; }
            return removed;
        }

        public bool Remove(string id) => Items.RemoveAll(x => x.Id == id) + Recent.RemoveAll(x => x.Id == id) > 0;
        public ReplayRecord Find(string id) => Items.Find(x => x.Id == id) ?? Recent.Find(x => x.Id == id);

        public static ReplayLibrary Read(ISaveStore store)
        {
            try { return store.Exists(SaveKey) ? ReplayJson.Read<ReplayLibrary>(store.Read(SaveKey)) ?? new ReplayLibrary() : new ReplayLibrary(); }
            catch (Exception) { return new ReplayLibrary(); }
        }

        public void Write(ISaveStore store) => store.Write(SaveKey, ReplayJson.Write(this));
    }

    /// <summary>
    /// 녹화 JSON: Fix·Fix?는 원본 정수, 목록은 기본값에 덧붙이지 않고 바꾼다 (예: DefeatConditions 기본 2개).
    /// </summary>
    public static class ReplayJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new FixConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        public static string Write(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        private sealed class FixConverter : JsonConverter
        {
            public override bool CanConvert(Type t) => t == typeof(Fix) || t == typeof(Fix?);
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null) writer.WriteNull();
                else writer.WriteValue(((Fix)value).Raw);
            }
            public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return t == typeof(Fix?) ? (object)null : Fix.Zero;
                return Fix.FromRaw(Convert.ToInt64(reader.Value));
            }
        }
    }
}

namespace Game.Flow
{
    /// <summary>
    /// 녹화 공유 (공략, replay.md 5장, R-81): 서버(와 가짜 서버)가 같은 규칙으로 받고 고른다.
    /// 받기 = 지금 규칙으로 다시 돌려 결과 지문이 같을 때만 (조작·옛 규칙 녹화는 거절).
    /// 고르기 = 그 스텝의 것 중 이긴 것 먼저, 손실률 낮은 것, 짧은 것, 최근 것 순으로 SharedListMax개.
    /// </summary>
    public static class SharedReplays
    {
        public static Game.Online.ShareReplayResponse Validate(ReplayService service, string json, out ReplayRecord record)
        {
            record = null;
            try { record = ReplayService.Read(json); } catch (Exception) { record = null; }
            if (record == null || record.Setup == null || string.IsNullOrEmpty(record.StepId))
                return new Game.Online.ShareReplayResponse { Ok = false, Reason = ReplayStatus.Broken };
            var check = service.Check(record);
            if (!check.Ok) return new Game.Online.ShareReplayResponse { Ok = false, Reason = check.Status };
            return new Game.Online.ShareReplayResponse { Ok = true, Replay = Summary(record) };
        }

        public static Game.Online.SharedReplaySummary Summary(ReplayRecord r) => new Game.Online.SharedReplaySummary
        {
            StepId = r.StepId,
            Won = r.Won,
            Seconds = r.Seconds,
            PlayerStartTroops = r.PlayerStartTroops,
            PlayerLosses = r.PlayerLosses,
            Hash = r.Hash,
        };

        public static List<Game.Online.SharedReplaySummary> Pick(IEnumerable<Game.Online.SharedReplaySummary> all, string stepId, int max)
        {
            var list = new List<Game.Online.SharedReplaySummary>();
            foreach (var s in all) if (s.StepId == stepId) list.Add(s);
            list.Sort((a, b) =>
            {
                if (a.Won != b.Won) return a.Won ? -1 : 1;
                // 손실률 (정수 비교: a.loss/a.start < b.loss/b.start)
                long l = a.PlayerLosses * System.Math.Max(1, b.PlayerStartTroops), r = b.PlayerLosses * System.Math.Max(1, a.PlayerStartTroops);
                if (l != r) return l < r ? -1 : 1;
                if (a.Seconds != b.Seconds) return a.Seconds < b.Seconds ? -1 : 1;
                return b.SharedAtUnixSeconds.CompareTo(a.SharedAtUnixSeconds);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }
    }
}
