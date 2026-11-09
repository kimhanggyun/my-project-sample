using System.IO;

namespace Game.Data.Json
{
    /// <summary>데이터 파일 읽기 경계. Unity 빌드에서는 Resources 구현(ResourcesDataSource), 에디터·테스트에서는 폴더 구현.</summary>
    public interface IDataSource
    {
        bool Exists(string relativePath);
        string ReadText(string relativePath);
    }

    public sealed class FileDataSource : IDataSource
    {
        private readonly string _root;
        public FileDataSource(string root) { _root = root; }
        private string Full(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        public bool Exists(string relativePath) => File.Exists(Full(relativePath));
        public string ReadText(string relativePath) => File.ReadAllText(Full(relativePath));
    }
}
