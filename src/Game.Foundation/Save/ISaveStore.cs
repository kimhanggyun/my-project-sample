using System.Collections.Generic;
using System.IO;

namespace Game.Foundation.Save
{
    /// <summary>세이브 저장소 경계. 기기 저장·클라우드 저장 구현을 갈아 끼운다.</summary>
    public interface ISaveStore
    {
        bool Exists(string key);
        string Read(string key);
        void Write(string key, string content);
        void Delete(string key);
    }

    public sealed class InMemorySaveStore : ISaveStore
    {
        private readonly Dictionary<string, string> _items = new Dictionary<string, string>();
        public bool Exists(string key) => _items.ContainsKey(key);
        public string Read(string key) => _items.TryGetValue(key, out var v) ? v : null;
        public void Write(string key, string content) { _items[key] = content; }
        public void Delete(string key) { _items.Remove(key); }
    }

    /// <summary>폴더 아래 파일로 저장. 폴더 경로는 바깥(엔진)에서 넘겨준다.</summary>
    public sealed class FileSaveStore : ISaveStore
    {
        private readonly string _root;

        public FileSaveStore(string rootDirectory)
        {
            _root = rootDirectory;
            Directory.CreateDirectory(_root);
        }

        private string PathOf(string key) => System.IO.Path.Combine(_root, key + ".json");
        public bool Exists(string key) => File.Exists(PathOf(key));
        public string Read(string key) => Exists(key) ? File.ReadAllText(PathOf(key)) : null;

        public void Write(string key, string content)
        {
            // 쓰다가 끊겨도 기존 세이브가 깨지지 않게 임시 파일 → 교체
            string target = PathOf(key);
            string temp = target + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(target)) File.Delete(target);
            File.Move(temp, target);
        }

        public void Delete(string key) { if (Exists(key)) File.Delete(PathOf(key)); }
    }
}
