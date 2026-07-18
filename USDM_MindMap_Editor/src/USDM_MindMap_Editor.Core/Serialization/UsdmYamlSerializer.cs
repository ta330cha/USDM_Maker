using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Validation;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace USDM_MindMap_Editor.Core.Serialization;

public sealed class UsdmYamlSerializer
{
    private readonly UsdmDocumentValidator _validator;
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .WithEventEmitter(next => new QuotedIdentifierEventEmitter(next))
        .Build();
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public UsdmYamlSerializer(UsdmDocumentValidator? validator = null) => _validator = validator ?? new();

    public string Serialize(UsdmDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Version = AppVersion.Current;
        ThrowIfInvalid(document);
        return _serializer.Serialize(DocumentDto.From(document)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public UsdmDocument Deserialize(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) throw new ArgumentException("YAMLが空です。", nameof(yaml));
        try
        {
            var dto = _deserializer.Deserialize<DocumentDto>(yaml) ?? throw new InvalidDataException("YAML文書を読み取れませんでした。");
            var document = dto.ToModel();
            ThrowIfInvalid(document);
            return document;
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new InvalidDataException("YAMLの形式が不正です。", exception);
        }
    }

    private void ThrowIfInvalid(UsdmDocument document)
    {
        var validation = _validator.Validate(document);
        if (!validation.IsValid) throw new UsdmValidationException(validation.Errors);
    }

    private sealed class DocumentDto
    {
        public string Version { get; set; } = "";
        public string? Title { get; set; }
        public List<BackgroundDto> Backgrounds { get; set; } = [];
        public List<RequirementDto> Requirements { get; set; } = [];
        public List<SpecificationDto> Specifications { get; set; } = [];

        public static DocumentDto From(UsdmDocument document) => new()
        {
            Version = AppVersion.Current,
            Title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title,
            Backgrounds = document.Backgrounds.OrderBy(x => x.Id, StringComparer.Ordinal).Select(BackgroundDto.From).ToList(),
            Requirements = document.Requirements.OrderBy(x => x.Id, StringComparer.Ordinal).Select(RequirementDto.From).ToList(),
            Specifications = document.Specifications.OrderBy(x => x.Id, StringComparer.Ordinal).Select(SpecificationDto.From).ToList()
        };

        public UsdmDocument ToModel() => new()
        {
            Version = Version, Title = Title,
            Backgrounds = Backgrounds.Select(x => x.ToModel()).ToList(),
            Requirements = Requirements.Select(x => x.ToModel()).ToList(),
            Specifications = Specifications.Select(x => x.ToModel()).ToList()
        };
    }

    private sealed class BackgroundDto
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public static BackgroundDto From(BackgroundItem item) => new() { Id = item.Id, Text = item.Text };
        public BackgroundItem ToModel() => new() { Id = Id, Text = Text };
    }

    private sealed class RequirementDto
    {
        public string Id { get; set; } = "";
        public string Request { get; set; } = "";
        public string? Reason { get; set; }
        public string? ParentBackgroundId { get; set; }
        public string? ParentRequirementId { get; set; }
        public static RequirementDto From(RequirementNode item) => new()
        {
            Id = item.Id, Request = item.Request, Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason,
            ParentBackgroundId = item.ParentBackgroundId, ParentRequirementId = item.ParentRequirementId
        };
        public RequirementNode ToModel() => new()
        {
            Id = Id, Request = Request, Reason = Reason,
            ParentBackgroundId = ParentBackgroundId, ParentRequirementId = ParentRequirementId
        };
    }

    private sealed class SpecificationDto
    {
        public string Id { get; set; } = "";
        public string Specification { get; set; } = "";
        public string ParentRequirementId { get; set; } = "";
        public List<string>? RelatedRequirementIds { get; set; }
        public static SpecificationDto From(SpecificationNode item) => new()
        {
            Id = item.Id, Specification = item.Specification, ParentRequirementId = item.ParentRequirementId,
            RelatedRequirementIds = item.RelatedRequirementIds.Count == 0 ? null : item.RelatedRequirementIds.Order(StringComparer.Ordinal).ToList()
        };
        public SpecificationNode ToModel() => new()
        {
            Id = Id, Specification = Specification, ParentRequirementId = ParentRequirementId,
            RelatedRequirementIds = RelatedRequirementIds ?? []
        };
    }

    private sealed class QuotedIdentifierEventEmitter(IEventEmitter nextEmitter) : ChainedEventEmitter(nextEmitter)
    {
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string value && (IsIdentifier(value) || IsSemanticVersion(value)))
                eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
        }

        private static bool IsIdentifier(string value) =>
            value.Split('-').All(part => part.Length == 3 && part.All(char.IsAsciiDigit));

        private static bool IsSemanticVersion(string value) =>
            System.Version.TryParse(value, out _) && value.Count(x => x == '.') == 2;
    }
}
