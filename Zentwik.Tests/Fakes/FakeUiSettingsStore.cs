using Zentwik.Core.Helpers;

namespace Zentwik.Tests.Fakes
{
    /// <summary>内存版 IUiSettingsStore：Load/Save 都不落盘，Settings 就是类型默认值。</summary>
    public sealed class FakeUiSettingsStore : IUiSettingsStore
    {
        public UiSettings Settings { get; } = new();

        public void Load() { }
        public void ResetToFactory() { }
        public void Save() { }
    }
}
