using Verifiabl.Verifier.Extraction.Internal;

namespace Verifiabl.Verifier.Extraction.Tests;

/// <summary>Names how a read ended as the extraction module's expected.json files do.</summary>
internal static class ReadStatus
{
    /// <summary>
    /// The status name of <paramref name="read"/>: <c>ok</c>, <c>timedOut</c> when it was cancelled,
    /// <c>fileTooLarge</c> when the host refused the file, or the status its failure reports.
    /// </summary>
    internal static string Of(Action read)
    {
        try
        {
            read();
            return "ok";
        }
        catch (PayloadExtractionException failure)
        {
            return NameOf(failure);
        }
        catch (OperationCanceledException)
        {
            return "timedOut";
        }
    }

    // Every status has its own message, so the failure identifies the status exactly.
    private static string NameOf(PayloadExtractionException failure)
    {
        if (failure.Message == ExtractionModule.FileTooLarge().Message)
        {
            return "fileTooLarge";
        }

        foreach (ExtractionStatus status in (ExtractionStatus[])Enum.GetValues(typeof(ExtractionStatus)))
        {
            if (status is ExtractionStatus.Ok or ExtractionStatus.Partial)
            {
                continue;
            }

            PayloadExtractionException expected = ExtractionModule.FailureFor(status);
            if (expected.Failure == failure.Failure && expected.Message == failure.Message)
            {
                string name = status.ToString();
                return char.ToLowerInvariant(name[0]) + name.Substring(1);
            }
        }

        return $"unexpected failure: {failure.Message}";
    }
}
