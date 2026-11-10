#if !NETFRAMEWORK
using System.Diagnostics;
using Xunit;

namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>
/// PDFs that crashed PdfPig 0.1.16 with a stack overflow. Each is read in a
/// child process so a regression fails the test instead of the test host.
/// </summary>
public class ProcessIsolationTests
{
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void SurvivesASelfReferencingImageFilter() =>
        AssertReadFinishes(TestPdfs.SelfReferencingFilterPdf());

    [Fact]
    public void SurvivesACyclicDestsNameTree() =>
        AssertReadFinishes(TestPdfs.CyclicDestsPdf());

    private static void AssertReadFinishes(byte[] pdf)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, pdf);
            (int exitCode, string output) = RunChild(path);
            Assert.True(exitCode == 0, $"The child process exited with {exitCode}:\n{Tail(output)}");
            Assert.Contains(Program.Finished, output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (int ExitCode, string Output) RunChild(string pdfPath)
    {
        var start = new ProcessStartInfo(DotnetHost())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(Program.ReadPdfCommand);
        start.ArgumentList.Add(pdfPath);

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(ChildTimeout))
        {
            child.Kill(entireProcessTree: true);
            throw new TimeoutException($"Reading the PDF took longer than {ChildTimeout}.");
        }

        child.WaitForExit();
        return (child.ExitCode, stdout.Result + stderr.Result);
    }

    /// <summary>A stack overflow prints thousands of frames; the last ones name the caller.</summary>
    private static string Tail(string output)
    {
        const int Length = 4000;
        return output.Length <= Length ? output : output.Substring(output.Length - Length);
    }

    /// <summary>The dotnet CLI sets DOTNET_HOST_PATH for the processes it starts.</summary>
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
}
#endif
