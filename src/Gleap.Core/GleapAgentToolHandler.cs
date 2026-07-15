using System.Collections.Generic;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// Executes a dashboard-defined Frontend tool that an AI agent (e.g. Kai) invokes.
/// The handler receives the parameters the agent collected and returns the result the
/// agent waits for: a string, or any JSON-serializable object (serialized to JSON).
/// Throwing reports the failure back to the agent. Register one with
/// <see cref="Gleap.RegisterAgentTool"/>.
/// </summary>
/// <param name="parameters">The parameter values the agent supplied, keyed by name.</param>
/// <returns>The tool result delivered to the agent.</returns>
public delegate Task<object?> GleapAgentToolHandler(IReadOnlyDictionary<string, object?> parameters);
