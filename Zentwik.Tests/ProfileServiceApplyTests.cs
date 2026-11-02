using System.IO;
using Zentwik.Core.Helpers;
using Zentwik.Core.Services;
using Zentwik.Tests.Fakes;
using Xunit;

namespace Zentwik.Tests
{
    /// <summary>ProfileService.Apply：把方案写回 tuning / bindings，再交给 TuningApplyService 下发。</summary>
    public sealed class ProfileServiceApplyTests : IDisposable
    {
        private readonly FakeAppPaths _paths = new();
        private readonly FakeTuningStore _tuning = new();
        private readonly FakeBindingStore _bindings = new();
        private readonly ProfileService _profiles;

        public ProfileServiceApplyTests()
        {
            FakeLogService log = new();
            TuningApplyService apply = new(log, new FakeUiSettingsStore(), _tuning, new FakeSmuReader());
            _profiles = new ProfileService(log, _tuning, _bindings, apply, new FakeProcessWatcher(), _paths);
            _profiles.Load();
            _profiles.Save(new ProfileSnapshot { Id = "a", Name = "性能", CurveOptimizer = [5, 5, 5, 5], FMaxMHz = 5000, BindingRules = [new BindingRule { Name = "game.exe", Target = BindingTarget.CCD0 }] });
            _profiles.Save(new ProfileSnapshot { Id = "b", Name = "静音", CurveOptimizer = [-10, -10, -10, -10], FMaxMHz = 4800 });
        }

        public void Dispose()
        {
            if (Directory.Exists(_paths.DataDirectory))
                Directory.Delete(_paths.DataDirectory, recursive: true);
        }

        [Fact]
        public void Apply_first_profile_writes_tuning_and_bindings()
        {
            Assert.True(_profiles.Apply("a"));
            Assert.Equal(new[] { 5, 5, 5, 5 }, _tuning.LastSaved!.CurveOptimizer);
            Assert.Equal(5000u, _tuning.LastSaved!.FMaxMHz);
            Assert.Equal("game.exe", Assert.Single(_bindings.LastSaved!).Name);
        }

        [Fact]
        public void Apply_second_profile_overwrites_previous_store_values()
        {
            Assert.True(_profiles.Apply("a"));
            Assert.True(_profiles.Apply("b"));
            Assert.Equal(new[] { -10, -10, -10, -10 }, _tuning.LastSaved!.CurveOptimizer);
            Assert.Equal(4800u, _tuning.LastSaved!.FMaxMHz);
            Assert.Empty(_bindings.LastSaved!);
        }
    }
}
