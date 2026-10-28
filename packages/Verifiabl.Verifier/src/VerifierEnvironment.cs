namespace Verifiabl.Verifier;

/// <summary>The Verifiabl environment a <see cref="VerifierClient"/> calls.</summary>
public enum VerifierEnvironment
{
    /// <summary>Live payslips. The default.</summary>
    Production = 0,

    /// <summary>Test payslips that sandbox issuers registered.</summary>
    Sandbox = 1,
}
