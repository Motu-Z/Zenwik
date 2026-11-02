using Zentwik.Core.Services;

namespace Zentwik.Tests.Fakes
{
    /// <summary>空壳 IProcessWatcher：ProfileService.Apply 只会调 ReapplyNow，其余不参与。</summary>
    public sealed class FakeProcessWatcher : IProcessWatcher
    {
        public bool UsingEvents => false;
        public bool HasOwnership => false;
        public bool IsRunning => false;
        public Action? WmiUnavailableNotifier { get; set; }
        public event Action? ProcessSetChanged { add { } remove { } }

        /// <summary>Apply 触发的重扫次数，供断言「有没有让在跑的进程跟上新规则」。</summary>
        public int ReapplyCount { get; private set; }

        public void Start() { }
        public void Stop() { }
        public void ResetCache() { }
        public void SetHostVisible(bool visible) { }
        public void ReapplyNow() => ReapplyCount++;
    }
}
