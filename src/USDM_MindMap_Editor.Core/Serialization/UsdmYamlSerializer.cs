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
        .WithDuplicateKeyChecking()
        .Build();

    public UsdmYamlSerializer(UsdmDocumentValidator? validator = null) => _validator = validator ?? new();

    public string Serialize(UsdmDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Saving never initializes layout or mutates an editing document. The
        // application commits all positions before handing a document to us.
        var snapshot = new UsdmDocument
        {
            Version = AppVersion.Current,
            FormatVersion = DocumentFormat.Current,
            Title = document.Title,
            Backgrounds = document.Backgrounds,
            Requirements = document.Requirements,
            Specifications = document.Specifications,
            View = document.View ?? new DocumentViewState()
        };
        ThrowIfInvalid(snapshot, requireBackgroundPositions: true);
        return _serializer.Serialize(PositionedDocumentDto.From(snapshot)).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public UsdmDocument Deserialize(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml)) throw new ArgumentException("YAMLが空です。", nameof(yaml));
        try
        {
            var header = _deserializer.Deserialize<FormatHeader>(yaml)
                ?? throw new InvalidDataException("YAML文書を読み取れませんでした。");
            var reader = new UsdmYamlReaderFactory(_deserializer).Create(header.FormatVersion ?? DocumentFormat.Legacy);
            var document = reader.Read(yaml);
            // Missing positions are intentional in old/partial files. Their
            // initialization and overlap correction belong to the layout layer.
            ThrowIfInvalid(document);
            return document;
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException("YAMLの形式が不正です。", exception);
        }
    }

    private void ThrowIfInvalid(UsdmDocument document, bool requireBackgroundPositions = false)
    {
        var validation = _validator.Validate(document, requireCurrentVersion: false, requireBackgroundPositions);
        if (!validation.IsValid) throw new UsdmValidationException(validation.Errors);
    }

    private sealed class FormatHeader
    {
        public int? FormatVersion { get; set; }
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
