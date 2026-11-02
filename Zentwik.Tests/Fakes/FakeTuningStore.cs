using Zentwik.Core.Helpers;

namespace Zentwik.Tests.Fakes
{
    /// <summary>内存版 ITuningStore：Load 不动内存，Save 记录最后一次写入供断言。</summary>
    public sealed class FakeTuningStore : ITuningStore
    {
        public TuningProfile Profile { get; private set; } = new();

        /// <summary>最后一次 Save 的内容；null 表示还没保存过。</summary>
        public TuningProfile? LastSaved { get; private set; }

        public void Load() { }

        public void Save(TuningProfile profile)
        {
            Profile = profile;
            LastSaved = profile;
        }

        public uint ResetToFactory() => 0;
    }
}
