using System.Threading;
using Zentwik.Core.Services;

namespace Zentwik.Tests.Fakes
{
    /// <summary>
    /// 空壳 ISmuReader：IsAvailable 恒 false，写入返回「全成功」的空结果，不碰任何硬件。
    ///
    /// WaitForAvailability 与 IsAvailable 保持一致返回 false —— 若返回 true，
    /// TuningApplyService 会继续走到 WriteCurveOptimizer，而那是本 fake 明确不模拟的路径。
    /// </summary>
    public sealed class FakeSmuReader : ISmuReader
    {
        public bool IsAvailable => false;
        public string? LastError => "FakeSmuReader：测试不访问 SMU";
        public int CoreCount => 0;
        public uint CoresPerCcd => 0;
        public uint CoresPerCcdRaw => 0;
        public uint PeekCoresPerCcdRaw() => 0;
        public SmuSnapshot ReadSnapshot() => new() { Success = true };
        public SmuWriteResult WriteCurveOptimizer(IReadOnlyList<int> margins) => new() { AllSucceeded = true };
        public SmuFMaxResult WriteFMax(uint mhz) => new() { AllSucceeded = true };
        public bool WaitForAvailability(int maxSeconds = 60, CancellationToken token = default) => false;
        public void Dispose() { }
    }
}
