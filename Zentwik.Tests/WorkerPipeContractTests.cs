using Zentwik.Core.Ipc;
using Xunit;

namespace Zentwik.Tests
{
    /// <summary>管道契约：常量形状 + 版本协商只有「相等才通过」一条规则。</summary>
    public sealed class WorkerPipeContractTests
    {
        [Fact]
        public void Constants_match_contract()
        {
            Assert.Equal(1, WorkerPipeContract.ProtocolVersion);
            Assert.False(string.IsNullOrWhiteSpace(WorkerPipeContract.PipeName));
            Assert.StartsWith(@"Global\", WorkerPipeContract.WorkerMutexName);
        }

        [Fact]
        public void ValidateVersion_accepts_only_protocol_version()
        {
            Assert.True(WorkerPipeContract.ValidateVersion(1, out string ok));
            Assert.Equal(string.Empty, ok);

            foreach (int bad in new[] { 0, 2, -1 })
            {
                Assert.False(WorkerPipeContract.ValidateVersion(bad, out string error));
                Assert.Equal(WorkerPipeContract.VersionMismatchMessage, error);
            }
        }

        [Fact]
        public void VersionMismatchResponse_has_fixed_shape()
        {
            PipeResponse response = WorkerPipeContract.VersionMismatchResponse();
            Assert.Equal(WorkerPipeContract.ProtocolVersion, response.Version);
            Assert.False(response.Success);
            Assert.Equal(WorkerPipeContract.VersionMismatchMessage, response.Message);
            Assert.Null(response.Payload);
        }
    }
}
