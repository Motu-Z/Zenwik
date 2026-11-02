using System.IO;
using Zentwik.Core.Helpers;

namespace Zentwik.Tests.Fakes
{
    /// <summary>临时目录版 IAppPaths：每次构造一个新 GUID 目录，删目录由测试自己负责。</summary>
    public sealed class FakeAppPaths : IAppPaths
    {
        public FakeAppPaths()
            => DataDirectory = Path.Combine(Path.GetTempPath(), "Zentwik.Tests", Guid.NewGuid().ToString("N"));

        /// <summary>接口之外的临时根目录，供测试断言与清理。</summary>
        public string DataDirectory { get; }

        public string Root => Ensure();
        public string Logs => Path.Combine(Ensure(), "Logs");
        public MigrationOutcome Migration => MigrationOutcome.NotNeeded;
        public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
        public string File(string fileName) => Path.Combine(Ensure(), fileName);
        public string Dir(string name) => Path.Combine(Ensure(), name);

        private string Ensure()
        {
            Directory.CreateDirectory(DataDirectory);
            return DataDirectory;
        }
    }
}
