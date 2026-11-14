using System;
using System.Collections.Generic;
using Game.Foundation.Math;
using Game.Foundation.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Campaign.Save
{
    /// <summary>세이브 형식 변환기. fromVersion → fromVersion+1.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        void Apply(JObject save);
    }

    /// <summary>
    /// 진행 상태 저장·불러오기. 형식에 버전 번호를 넣고, 예전 버전은 변환기를 차례로 적용한다.
    /// </summary>
    public sealed class CampaignSaveService
    {
        /// <summary>v2: 포로 목록, 스텝별 탐색 기억, 장수 포로 정보·기록 카운터·받은 성장 보상. v3: 숨은 보상 도감, 스킬북·특성서 보관함. v4: 상황별 편성.</summary>
        public const int CurrentVersion = 5;
        public const string DefaultKey = "campaign";

        private readonly ISaveStore _store;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        /// <summary>게임에 들어 있는 변환기 (예전 세이브를 지금 형식으로).</summary>
        public static readonly IReadOnlyList<ISaveMigration> BuiltInMigrations = new ISaveMigration[] { new V1ToV2(), new V2ToV3(), new V3ToV4(), new V4ToV5() };

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new FixJsonConverter() },
            Formatting = Formatting.Indented,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        public CampaignSaveService(ISaveStore store, IEnumerable<ISaveMigration> migrations = null)
        {
            _store = store;
            foreach (var m in BuiltInMigrations) _migrations[m.FromVersion] = m;
            if (migrations != null) foreach (var m in migrations) _migrations[m.FromVersion] = m;
        }

        public void Save(CampaignState state, string key = DefaultKey)
        {
            var root = new JObject
            {
                ["version"] = CurrentVersion,
                ["campaign"] = JObject.FromObject(state, JsonSerializer.Create(Settings)),
            };
            _store.Write(key, root.ToString(Formatting.Indented));
        }

        public CampaignState Load(string key = DefaultKey)
        {
            var text = _store.Read(key);
            if (text == null) return null;
            var root = JObject.Parse(text);
            int version = root.Value<int>("version");
            if (version > CurrentVersion)
                throw new InvalidOperationException($"세이브 버전 {version}이 게임 버전({CurrentVersion})보다 새롭습니다.");
            while (version < CurrentVersion)
            {
                if (!_migrations.TryGetValue(version, out var m))
                    throw new InvalidOperationException($"세이브 버전 {version} → {version + 1} 변환기가 없습니다.");
                m.Apply(root);
                version++;
                root["version"] = version;
            }
            var state = root["campaign"].ToObject<CampaignState>(JsonSerializer.Create(Settings));
            if (state != null) Game.Campaign.Roster.Army.Normalize(null, state); // 예전 군대(같은 병종 부대 여러 개·칸 편성) → 병종 묶음 (army-v2.md 7장)
            return state;
        }
    }

    /// <summary>v1 → v2: 새 목록·사전을 빈 값으로 채운다 (기존 값은 그대로).</summary>
    internal sealed class V1ToV2 : ISaveMigration
    {
        public int FromVersion => 1;

        public void Apply(JObject save)
        {
            var c = (JObject)save["campaign"];
            if (c["Prisoners"] == null) c["Prisoners"] = new JArray();
            if (c["ExploredBySteps"] == null) c["ExploredBySteps"] = new JObject();
            if (c["Generals"] is JArray gens)
                foreach (JObject g in gens)
                {
                    if (g["Records"] == null) g["Records"] = new JObject();
                    if (g["GrantedRewards"] == null) g["GrantedRewards"] = new JObject();
                }
        }
    }

    /// <summary>v2 → v3: 도감·보관함을 빈 값으로.</summary>
    internal sealed class V2ToV3 : ISaveMigration
    {
        public int FromVersion => 2;

        public void Apply(JObject save)
        {
            var c = (JObject)save["campaign"];
            if (c["FoundSecrets"] == null) c["FoundSecrets"] = new JArray();
            if (c["SkillBooks"] == null) c["SkillBooks"] = new JObject();
            if (c["TraitBooks"] == null) c["TraitBooks"] = new JObject();
        }
    }

    /// <summary>v4 → v5: 모집 천장·장수 조각을 빈 값으로.</summary>
    internal sealed class V4ToV5 : ISaveMigration
    {
        public int FromVersion => 4;

        public void Apply(JObject save)
        {
            var c = (JObject)save["campaign"];
            if (c["GachaPity"] == null) c["GachaPity"] = new JObject();
            if (c["GeneralShards"] == null) c["GeneralShards"] = new JObject();
        }
    }

    /// <summary>v3 → v4: 상황별 편성을 빈 값으로 (기본 편성은 그대로).</summary>
    internal sealed class V3ToV4 : ISaveMigration
    {
        public int FromVersion => 3;

        public void Apply(JObject save)
        {
            var c = (JObject)save["campaign"];
            if (c["CompositionPresets"] == null) c["CompositionPresets"] = new JObject();
        }
    }

    /// <summary>Fix는 원본 정수(raw)로 저장해 정밀도 손실이 없게 한다.</summary>
    public sealed class FixJsonConverter : JsonConverter<Fix>
    {
        public override void WriteJson(JsonWriter writer, Fix value, JsonSerializer serializer) => writer.WriteValue(value.Raw);
        public override Fix ReadJson(JsonReader reader, Type objectType, Fix existingValue, bool hasExistingValue, JsonSerializer serializer) =>
            Fix.FromRaw(Convert.ToInt64(reader.Value));
    }
}
