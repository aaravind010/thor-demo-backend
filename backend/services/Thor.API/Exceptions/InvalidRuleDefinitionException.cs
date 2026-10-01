namespace Thor.Api.Exceptions;

/// <summary>A rule's type, scope, or <c>rule_definition</c> would be rejected by the engine that runs it.</summary>
public sealed class InvalidRuleDefinitionException(string message) : Exception(message);
