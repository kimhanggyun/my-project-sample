using Game.Campaign;
using Game.Campaign.Save;
using Game.Data.Defs;
using Game.Data.Json;
using Game.Flow;
using Game.Foundation.Save;
using Game.Foundation.Time;
using Game.Online;

namespace Game.Server
{
    /// <summary>
    /// 서버 쪽 게임 처리 (백엔드 종류와 무관한 부분). HTTP는 Program.cs, 저장은 IAccountStore.
    /// 세이브는 서버가 주인이다: 새 계정이면 새 게임을 만들고, 전투 결과는 서버가 검증·반영한 세이브가 정답 (R-40).
    /// </summary>
    public sealed class GameService
    {
        private readonly DefinitionSet _defs;
        private readonly IDataSource _source;
        private readonly ITimeProvider _time;
        private readonly IAccountStore _store;

        public GameService(DefinitionSet defs, IDataSource source, ITimeProvider time, IAccountStore store)
        {
            _defs = defs;
            _source = source;
            _time = time;
            _store = store;
        }

        public IAccountStore Store => _store;
        public long Now => _time.UtcNow.ToUnixTimeSeconds();

        /// <summary>계정 세이브. 없으면 새 게임으로 만든다 (Revision 1).</summary>
        public CloudSave LoadOrCreate(string accountId)
        {
            var save = _store.GetSave(accountId);
            if (save != null) return save;
            var (ok, current) = _store.PutSave(accountId, 0, ToJson(StartingKitService.CreateNewGame(_defs)), Now);
            return ok ? current : _store.GetSave(accountId);
        }

        public SavePutResponse Put(string accountId, SavePutRequest req)
        {
            // 형식이 맞는 세이브만 받는다 (서버가 읽을 수 없는 세이브는 거절)
            if (req?.Json == null || FromJson(req.Json) == null) return new SavePutResponse { Ok = false, Current = _store.GetSave(accountId) };
            var (ok, current) = _store.PutSave(accountId, req.BaseRevision, req.Json, Now);
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
            var (ok, current) = _store.PutSave(accountId, save.Revision, ToJson(state), Now);
            if (!ok) return new BattleVerdict { Valid = false, Reason = VerdictReasons.StaleSave, Save = current };
            verdict.Save = current;
            return verdict;
        }

        public static string ToJson(CampaignState state)
        {
            var mem = new InMemorySaveStore();
            new CampaignSaveService(mem).Save(state);
            return mem.Read(CampaignSaveService.DefaultKey);
        }

        public static CampaignState FromJson(string json)
        {
            try
            {
                var mem = new InMemorySaveStore();
                mem.Write(CampaignSaveService.DefaultKey, json);
                return new CampaignSaveService(mem).Load();
            }
            catch (System.Exception) { return null; }
        }
    }
}
