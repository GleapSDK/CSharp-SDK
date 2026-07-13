using System.Diagnostics.CodeAnalysis;

namespace GleapSDK.Models;

/// <summary>Parameter type for an AI tool parameter.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "String/Number/Boolean are the AI tool parameter type names defined by the Gleap widget protocol.")]
public enum AIParamType { String, Number, Boolean }
