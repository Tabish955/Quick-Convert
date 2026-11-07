using QuickConvert.Core.Ipc;
using Xunit;

namespace QuickConvert.Core.Tests;

public class SingleInstancePipeServiceTests
{
    [Fact]
    public async Task SendFilesToPrimaryAsync_NoFiles_ReturnsNoFiles()
    {
        var result = await SingleInstancePipeService.SendFilesToPrimaryAsync([], pipeName: "qc-test-dead-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(FileSendResult.NoFiles, result);
    }

    [Fact]
    public async Task SendFilesToPrimaryAsync_NoServer_ReturnsFailed_FastAndWithoutThrowing()
    {
        // A GUID pipe name is guaranteed dead: no server can ever answer on it.
        var deadPipe = "qc-test-dead-" + Guid.NewGuid().ToString("N");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await SingleInstancePipeService.SendFilesToPrimaryAsync(
            ["C:\\some\\file.jpg"], timeoutMs: 50, pipeName: deadPipe);
        sw.Stop();

        Assert.Equal(FileSendResult.Failed, result);

        // 5 attempts × (50 ms timeout + 60 ms back-off) ≈ 550 ms ceiling. Generous bound to
        // keep this stable on loaded CI runners while still proving the retry is bounded.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"send took {sw.Elapsed}, expected a bounded fast-fail");
    }
}
