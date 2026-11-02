using System.IO;
using Zentwik.Core.Helpers;
using Zentwik.Core.Services;
using Zentwik.Tests.Fakes;
using Xunit;

namespace Zentwik.Tests
{
    /// <summary>场景仲裁：三条规则同时命中时，Priority 数字最小的那条赢。</summary>
    public sealed class ScenarioServiceTests : IDisposable
    {
        private readonly FakeTuningStore _tuning = new();
        private readonly FakeAppPaths _paths = new();
        private readonly ScenarioService _scenarios;

        public ScenarioServiceTests()
        {
            FakeLogService log = new();
            TuningApplyService apply = new(log, new FakeUiSettingsStore(), _tuning, new FakeSmuReader());
            ProfileService profiles = new(log, _tuning, new FakeBindingStore(), apply, new FakeProcessWatcher(), _paths);
            _scenarios = new ScenarioService(log, profiles, _paths);

            foreach ((string id, int[] co) in new[] { ("p10", new[] { 1, 1 }), ("p5", new[] { 5, 5 }), ("p20", new[] { 2, 2 }) })
                profiles.Save(new ProfileSnapshot { Id = id, Name = id, CurveOptimizer = co, FMaxMHz = 4500 });

            foreach ((string id, int priority) in new[] { ("p10", 10), ("p5", 5), ("p20", 20) })
                _scenarios.Save(new ScenarioRule
                {
                    Trigger = ScenarioTrigger.ProcessRunning, Argument = id + ".exe", ProfileId = id, Priority = priority
                });

            _scenarios.SetEnabled(true);
        }

        /// <summary>Save 会把 profiles.json / scenarios.json 写进临时目录，用例结束后整个目录删掉。</summary>
        public void Dispose()
        {
            if (Directory.Exists(_paths.DataDirectory)) Directory.Delete(_paths.DataDirectory, recursive: true);
        }

        [Fact]
        public void Notify_three_hits_picks_smallest_priority()
        {
            _scenarios.Notify([
                (ScenarioTrigger.ProcessRunning, "p10.exe"),
                (ScenarioTrigger.ProcessRunning, "p5.exe"),
                (ScenarioTrigger.ProcessRunning, "p20.exe")]);

            Assert.Equal("p5", _scenarios.LastAppliedProfileId);
            Assert.Equal(new[] { 5, 5 }, _tuning.LastSaved!.CurveOptimizer);
            Assert.Equal(4500u, _tuning.LastSaved!.FMaxMHz);
        }

        [Fact]
        public void Notify_without_smallest_falls_back_to_next()
        {
            _scenarios.Notify([
                (ScenarioTrigger.ProcessRunning, "p10.exe"),
                (ScenarioTrigger.ProcessRunning, "p20.exe")]);

            Assert.Equal("p10", _scenarios.LastAppliedProfileId);
            Assert.Equal(new[] { 1, 1 }, _tuning.LastSaved!.CurveOptimizer);
        }
    }
}
