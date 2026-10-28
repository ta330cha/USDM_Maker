namespace USDM_MindMap_Editor.Core.Validation;

public sealed record ValidationError(string Code, string Message, string? NodeId = null);

public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = [];
    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<ValidationError> Errors => _errors;
    internal void Add(string code, string message, string? nodeId = null) => _errors.Add(new(code, message, nodeId));
}
