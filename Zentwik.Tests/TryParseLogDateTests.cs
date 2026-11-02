using Zentwik.Core.Services;
using Xunit;

namespace Zentwik.Tests
{
    /// <summary>
    /// TryParseLogDate：从 {prefix}{yyyyMMdd}.log 切日期段，切分点是最后一个连字符。
    /// internal 方法，靠 InternalsVisibleTo("Zentwik.Tests") 直接调用。
    /// </summary>
    public sealed class TryParseLogDateTests
    {
        [Theory]
        [InlineData("worker-20261101.log")]
        [InlineData("my-app-20261101.log")]
        public void Parses_date_from_file_name(string file)
        {
            Assert.True(LogService.TryParseLogDate(file, out DateTime date));
            Assert.Equal(new DateTime(2026, 11, 1), date);
        }

        [Theory]
        [InlineData("no-date.log")]
        [InlineData(".log")]
        [InlineData("20261101.log")]
        public void Returns_false_without_throwing_when_no_date_segment(string file)
        {
            Assert.False(LogService.TryParseLogDate(file, out _));
        }
    }
}
