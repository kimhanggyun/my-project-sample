using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Flow;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Server
{
    /// <summary>
    /// 게임 하나의 서버 처리 (docs/impl/server.md 4.5). 게임을 늘리면 이 경계를 구현해 등록표에 더한다 (기존 코드 수정 없음).
    /// 공통 계정·토큰은 플랫폼 저장소, 세이브는 (게임 코드, 계정)별 게임 저장소.
    /// </summary>
    public interface IGameModule
    {
        /// <summary>직접 정한 게임 코드 (주 키, 예: "ancientwar").</summary>
        string Code { get; }
        /// <summary>이 게임으로 연결되는 앱 id (안드로이드 package명·iOS 번들 id). 키로 쓰지 않고 찾기만.</summary>
        IReadOnlyList<string> PackageIds { get; }
        CloudSave LoadOrCreate(string accountId);
        SavePutResponse Put(string accountId, SavePutRequest request);
        BattleVerdict SubmitBattle(string accountId, BattleSubmission submission);
    }

    /// <summary>게임 등록표: 게임 코드·앱 id → 게임.</summary>
    public sealed class GameRegistry
    {
        private readonly Dictionary<string, IGameModule> _byCode = new Dictionary<string, IGameModule>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byPackage = new Dictionary<string, string>(StringComparer.Ordinal);

        public GameRegistry Register(IGameModule game)
        {
            if (_byCode.ContainsKey(game.Code)) throw new ArgumentException("같은 게임 코드가 두 번 등록됨: " + game.Code);
            _byCode[game.Code] = game;
            foreach (var p in game.PackageIds)
            {
                if (_byPackage.ContainsKey(p)) throw new ArgumentException("같은 앱 id가 두 게임에 연결됨: " + p);
                _byPackage[p] = game.Code;
            }
            return this;
        }

        public IGameModule Find(string code) => code != null && _byCode.TryGetValue(code, out var g) ? g : null;
        public string CodeOfPackage(string packageId) => packageId != null && _byPackage.TryGetValue(packageId, out var c) ? c : null;

        public IReadOnlyList<string> Codes
        {
            get
            {
                var list = new List<string>(_byCode.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }
    }

    /// <summary>
    /// 이 저장소의 전략 전투 게임. 세이브는 서버가 주인: 새 가입이면 새 게임, 전투 결과는 서버가 검증·반영 (R-40).
    /// </summary>
    public sealed class StrategyGameModule : IGameModule
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly IGameStore _store;

        public StrategyGameModule(string code, IReadOnlyList<string> packageIds, IDataSource source, string theme, ITimeProvider time, IGameStore store)
        {
            Code = code;
            PackageIds = packageIds ?? Array.Empty<string>();
            _source = source;
            _defs = DefinitionSet.Load(source, theme);
            _time = time;
            _store = store;
        }

        public string Code { get; }
        public IReadOnlyList<string> PackageIds { get; }
        private long Now => _time.UtcNow.ToUnixTimeSeconds();

        public CloudSave LoadOrCreate(string accountId)
        {
            var save = _store.GetSave(Code, accountId);
            if (save != null) return save;
            var (ok, current) = _store.PutSave(Code, accountId, 0, ToJson(StartingKitService.CreateNewGame(_defs)), Now);
            return ok ? current : _store.GetSave(Code, accountId);
        }

        public SavePutResponse Put(string accountId, SavePutRequest req)
        {
            // 형식이 맞는 세이브만 받는다 (서버가 읽을 수 없는 세이브는 거절)
            if (req?.Json == null || FromJson(req.Json) == null) return new SavePutResponse { Ok = false, Current = _store.GetSave(Code, accountId) };
            var (ok, current) = _store.PutSave(Code, accountId, req.BaseRevision, req.Json, Now);
            return new SavePutResponse { Ok = ok, Current = current };
        }

        public BattleVerdict SubmitBattle(string accountId, BattleSubmission s)
        {
            var save = LoadOrCreate(accountId);
            if (s == null || s.BaseRevision != save.Revision)
                return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = save };
            var state = FromJson(save.Json);
            var verdict = new BattleVerifier(_defs, _source, _time).VerifyAndApply(state, s, out _);
            if (!verdict.Valid) return verdict;
            var (ok, current) = _store.PutSave(Code, accountId, save.Revision, ToJson(state), Now);
            if (!ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = current };
            verdict.Save = current;
            return verdict;
        }

        public static string ToJson(CampaignState state) => SaveJson.Write(state);
        public static CampaignState FromJson(string json) => SaveJson.Read(json);
    }
}
