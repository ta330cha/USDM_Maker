using System.Globalization;
using USDM_MindMap_Editor.Core.Ids;
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
        document.FormatVersion = DocumentFormat.Current;
        document.View ??= new DocumentViewState();
        document.View.Pan ??= new PanState();
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
        catch (YamlException exception)
        {
            throw new InvalidDataException("YAMLの形式が不正です。", exception);
        }
    }

    private void ThrowIfInvalid(UsdmDocument document)
    {
        var validation = _validator.Validate(document, requireCurrentVersion: false);
        if (!validation.IsValid) throw new UsdmValidationException(validation.Errors);
    }

    private sealed class DocumentDto
    {
        public string Version { get; set; } = "";
        public int? FormatVersion { get; set; }
        public string? Title { get; set; }
        public List<BackgroundDto> Backgrounds { get; set; } = [];
        public List<RequirementDto> Requirements { get; set; } = [];
        public List<SpecificationDto> Specifications { get; set; } = [];
        public ViewDto? View { get; set; }

        public static DocumentDto From(UsdmDocument document) => new()
        {
            Version = AppVersion.Current,
            FormatVersion = DocumentFormat.Current,
            Title = string.IsNullOrWhiteSpace(document.Title) ? null : document.Title,
            Backgrounds = document.Backgrounds.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer).Select(BackgroundDto.From).ToList(),
            Requirements = document.Requirements.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer).Select(RequirementDto.From).ToList(),
            Specifications = document.Specifications.OrderBy(x => x.Id, UsdmIdRules.NumericSegmentComparer).Select(SpecificationDto.From).ToList(),
            View = ViewDto.From(document.View)
        };

        public UsdmDocument ToModel() => new()
        {
            Version = Version,
            FormatVersion = FormatVersion ?? DocumentFormat.Legacy,
            Title = Title,
            Backgrounds = Backgrounds.Select(x => x.ToModel()).ToList(),
            Requirements = Requirements.Select(x => x.ToModel()).ToList(),
            Specifications = Specifications.Select(x => x.ToModel()).ToList(),
            View = View?.ToModel() ?? new DocumentViewState()
        };
    }

    private sealed class ViewDto
    {
        public double Zoom { get; set; } = 1.0;
        public PanDto? Pan { get; set; } = new();
        public Dictionary<string, NodeViewDto>? Nodes { get; set; }

        public static ViewDto From(DocumentViewState view)
        {
            var nodes = view.Nodes
                .Where(x => x.Value.ReasonExpanded)
                .OrderBy(x => x.Key, NodeKeyComparer.Instance)
                .ToDictionary(x => x.Key, x => new NodeViewDto { ReasonExpanded = true }, StringComparer.Ordinal);
            return new ViewDto
            {
                Zoom = view.Zoom,
                Pan = new PanDto { X = view.Pan.X, Y = view.Pan.Y },
                Nodes = nodes.Count == 0 ? null : nodes
            };
        }

        public DocumentViewState ToModel() => new()
        {
            Zoom = Zoom,
                Pan = new PanState { X = Pan?.X ?? 0, Y = Pan?.Y ?? 0 },
            Nodes = (Nodes ?? new Dictionary<string, NodeViewDto>(StringComparer.Ordinal))
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => new NodeViewState { ReasonExpanded = x.Value!.ReasonExpanded }, StringComparer.Ordinal)
        };
    }

    private sealed class PanDto
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    private sealed class NodeViewDto
    {
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public bool ReasonExpanded { get; set; }
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
            Id = item.Id,
            Request = item.Request,
            Reason = string.IsNullOrWhiteSpace(item.Reason) ? null : item.Reason,
            ParentBackgroundId = item.ParentBackgroundId,
            ParentRequirementId = item.ParentRequirementId
        };
        public RequirementNode ToModel() => new()
        {
            Id = Id,
            Request = Request,
            Reason = Reason,
            ParentBackgroundId = ParentBackgroundId,
            ParentRequirementId = ParentRequirementId
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
            Id = item.Id,
            Specification = item.Specification,
            ParentRequirementId = item.ParentRequirementId,
            RelatedRequirementIds = item.RelatedRequirementIds.Count == 0
                ? null
                : item.RelatedRequirementIds.Order(UsdmIdRules.NumericSegmentComparer).ToList()
        };
        public SpecificationNode ToModel() => new()
        {
            Id = Id,
            Specification = Specification,
            ParentRequirementId = ParentRequirementId,
            RelatedRequirementIds = RelatedRequirementIds ?? []
        };
    }

    private sealed class NodeKeyComparer : IComparer<string>
    {
        public static NodeKeyComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var leftParts = left.Split(':', 2);
            var rightParts = right.Split(':', 2);
            var kindComparison = string.Compare(leftParts[0], rightParts[0], StringComparison.Ordinal) switch
            {
                0 => 0,
                var value => value
            };
            return kindComparison != 0
                ? kindComparison
                : UsdmIdRules.NumericSegmentComparer.Compare(leftParts.ElementAtOrDefault(1), rightParts.ElementAtOrDefault(1));
        }
    }

    private sealed class QuotedIdentifierEventEmitter(IEventEmitter nextEmitter) : ChainedEventEmitter(nextEmitter)
    {
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string value && (UsdmIdRules.IsValidId(value, value.Count(x => x == '-') + 1) || IsSemanticVersion(value)))
                eventInfo.Style = ScalarStyle.DoubleQuoted;
            base.Emit(eventInfo, emitter);
        }

        private static bool IsSemanticVersion(string value) =>
            System.Version.TryParse(value, out _) && value.Count(x => x == '.') == 2;
    }
}
