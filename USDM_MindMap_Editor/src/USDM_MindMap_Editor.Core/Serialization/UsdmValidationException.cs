using USDM_MindMap_Editor.Core.Validation;

namespace USDM_MindMap_Editor.Core.Serialization;

public sealed class UsdmValidationException : Exception
{
    public UsdmValidationException(IReadOnlyList<ValidationError> errors)
        : base(string.Join(Environment.NewLine, errors.Select(x => x.Message))) => Errors = errors;

    public IReadOnlyList<ValidationError> Errors { get; }
}
