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
        public const int CurrentVersion = 1;
        public const string DefaultKey = "campaign";

        private readonly ISaveStore _store;
        private readonly Dictionary<int, ISaveMigration> _migrations = new Dictionary<int, ISaveMigration>();

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new FixJsonConverter() },
            Formatting = Formatting.Indented,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        public CampaignSaveService(ISaveStore store, IEnumerable<ISaveMigration> migrations = null)
        {
            _store = store;
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
            return root["campaign"].ToObject<CampaignState>(JsonSerializer.Create(Settings));
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
