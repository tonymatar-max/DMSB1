namespace NexusDocs.Api.Infrastructure.Flow;

/// <summary>
/// Thrown by <see cref="WorkflowEngine"/> when a caller-supplied input fails a business rule
/// (e.g. a Rejected decision with no comment). Callers that want the standard 400 behaviour
/// should let this bubble to a global exception handler / filter rather than catching it locally.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
