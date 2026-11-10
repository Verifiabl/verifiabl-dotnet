namespace Verifiabl.Verifier.Pdf.Tests;

/// <summary>
/// Entry point for <see cref="ProcessIsolationTests"/>, which read hostile PDFs
/// in a child process: a stack overflow ends the process and cannot be caught.
/// </summary>
public static class Program
{
    internal const string ReadPdfCommand = "--read-pdf";

    internal const string Finished = "read-finished";

    public static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != ReadPdfCommand)
        {
            return 2;
        }

        try
        {
            PayslipPdfReader.Read(File.ReadAllBytes(args[1]));
        }
        catch (Exception exception)
        {
            // Any managed failure means the process survived, which is all these tests check.
            Console.WriteLine(exception.GetType().Name);
        }

        Console.WriteLine(Finished);
        return 0;
    }
}
