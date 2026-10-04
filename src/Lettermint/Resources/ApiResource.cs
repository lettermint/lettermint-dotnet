using System.Diagnostics;
using Lettermint.Internal;

namespace Lettermint;

/// <summary>
/// Base of the sub-clients. The transport (and with it the tokens) lives in a
/// private field; <c>ToString()</c>, the debugger and JSON serialization show no
/// credentials.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public abstract class ApiResource
{
    private protected ApiResource(Transport transport) => Transport = transport;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private protected Transport Transport { get; }

    /// <summary>The sub-client's name; contains no credentials.</summary>
    public override string ToString() => GetType().Name;
}
