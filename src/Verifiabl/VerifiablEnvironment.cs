namespace Verifiabl;

/// <summary>The Verifiabl API environment a client or barcode targets.</summary>
public enum VerifiablEnvironment
{
    /// <summary>Production endpoints. The default.</summary>
    Production = 0,

    /// <summary>Sandbox endpoints, for integration testing with sandbox credentials.</summary>
    Sandbox = 1,
}
