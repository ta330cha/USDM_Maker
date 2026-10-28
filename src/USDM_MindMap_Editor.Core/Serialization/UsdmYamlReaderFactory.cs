using USDM_MindMap_Editor.Core.Models;
using USDM_MindMap_Editor.Core.Validation;
using YamlDotNet.Serialization;

namespace USDM_MindMap_Editor.Core.Serialization;

internal interface IUsdmVersionedReader
{
    UsdmDocument Read(string yaml);
}

internal sealed class UsdmYamlReaderFactory(IDeserializer deserializer)
{
    public IUsdmVersionedReader Create(int formatVersion) => formatVersion switch
    {
        DocumentFormat.Legacy => new LegacyReader(deserializer),
        DocumentFormat.ViewState => new ViewStateReader(deserializer),
        DocumentFormat.Current => new BackgroundPositionReader(deserializer),
        _ => throw new UsdmValidationException([new ValidationError("format_version.unsupported",
            $"format_version は {DocumentFormat.Legacy}〜{DocumentFormat.Current} に対応しています。")])
    };

    private sealed class LegacyReader(IDeserializer deserializer) : IUsdmVersionedReader
    {
        public UsdmDocument Read(string yaml) => ReadDto<DocumentDto>(deserializer, yaml).ToModel(new DocumentViewState());
    }

    private sealed class ViewStateReader(IDeserializer deserializer) : IUsdmVersionedReader
    {
        public UsdmDocument Read(string yaml)
        {
            var dto = ReadDto<ViewDocumentDto>(deserializer, yaml);
            return dto.ToModel(dto.View?.ToModel() ?? new DocumentViewState());
        }
    }

    private sealed class BackgroundPositionReader(IDeserializer deserializer) : IUsdmVersionedReader
    {
        public UsdmDocument Read(string yaml)
        {
            var dto = ReadDto<PositionedDocumentDto>(deserializer, yaml);
            return dto.ToModel(dto.View?.ToPositionedModel() ?? new DocumentViewState());
        }
    }

    private static T ReadDto<T>(IDeserializer deserializer, string yaml) where T : class =>
        deserializer.Deserialize<T>(yaml) ?? throw new InvalidDataException("YAML文書を読み取れませんでした。");
}
