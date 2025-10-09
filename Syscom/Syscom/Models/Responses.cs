namespace Syscom.Models;

#pragma warning disable CS8618
#pragma warning disable CS8601
#pragma warning disable CS8603
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

public class SyscomAccessTokenResponse
{
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
}

public class SyscomProductsSearchResponse
{
    [JsonPropertyName("productos")] public List<SyscomProduct>? Products { get; set; }
    [JsonPropertyName("pagina")] public int PaginaActual { get; set; }
    [JsonPropertyName("paginas")] public int Paginas { get; set; }
    [JsonPropertyName("cantidad")] public int Total { get; set; }
}

public partial class SyscomProductResponse
{
    [JsonPropertyName("cantidad")] public long Cantidad { get; set; }

    [JsonPropertyName("pagina")] public long Pagina { get; set; }

    [JsonPropertyName("paginas")] public long Paginas { get; set; }

    [JsonPropertyName("productos")] public List<SyscomProduct> Productos { get; set; }

    [JsonPropertyName("todo")] public bool Todo { get; set; }
}

public partial class SyscomProduct
{
    [JsonPropertyName("producto_id")]
    [JsonConverter(typeof(SyscomParseStringConverter))]
    public long ProductoId { get; set; }

    [JsonPropertyName("modelo")] public string Modelo { get; set; }

    [JsonPropertyName("total_existencia")] public long TotalExistencia { get; set; }

    [JsonPropertyName("titulo")] public string Titulo { get; set; }

    [JsonPropertyName("marca")] public string Marca { get; set; }

    [JsonPropertyName("sat_key")]
    // [JsonConverter(typeof(SyscomParseStringConverter))]
    public string SatKey { get; set; }

    [JsonPropertyName("sat_description")] public string SatDescription { get; set; }

    [JsonPropertyName("img_portada")] public Uri ImgPortada { get; set; }

    [JsonPropertyName("link_privado")] public Uri LinkPrivado { get; set; }

    [JsonPropertyName("categorias")] public List<SyscomCategory> Categorias { get; set; }

    [JsonPropertyName("pvol")] public string Pvol { get; set; }

    [JsonPropertyName("marca_logo")] public Uri MarcaLogo { get; set; }

    [JsonPropertyName("link")] public string Link { get; set; }

    [JsonPropertyName("peso")] public string Peso { get; set; }

    [JsonPropertyName("unidad_de_medida")] public SyscomMeasurementUnit MeasurementUnit { get; set; }

    [JsonPropertyName("precios")] public SyscomPrices Prices { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proyecto")]
    public bool? Proyecto { get; set; }
}

public partial class SyscomCategory
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(SyscomParseStringConverter))]
    public long Id { get; set; }

    [JsonPropertyName("nombre")] public string Nombre { get; set; }

    [JsonPropertyName("nivel")] public long Nivel { get; set; }
}

public partial class SyscomPrices
{
    [JsonPropertyName("precio_1")] public string Precio1 { get; set; }

    [JsonPropertyName("precio_especial")] public string PrecioEspecial { get; set; }

    [JsonPropertyName("precio_descuento")] public string PrecioDescuento { get; set; }
    
    [JsonPropertyName("precio_lista")] public string PrecioLista { get; set; }
}

public partial class SyscomMeasurementUnit
{
    [JsonPropertyName("codigo_unidad")]
    [JsonConverter(typeof(SyscomParseStringConverter))]
    public long CodigoUnidad { get; set; }

    [JsonPropertyName("nombre")] public string Nombre { get; set; }

    [JsonPropertyName("clave_unidad_sat")] public string ClaveUnidadSat { get; set; }
}

public partial class SyscomProductResponse
{
    public static SyscomProductResponse FromJson(string json) =>
        JsonSerializer.Deserialize<SyscomProductResponse>(json, SyscomResponseConverter.Settings);
}

public static class SyscomResponseSerialize
{
    public static string ToJson(this SyscomProductResponse self) =>
        JsonSerializer.Serialize(self, SyscomResponseConverter.Settings);
}

internal static class SyscomResponseConverter
{
    public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
    {
        Converters =
        {
            new SyscomDateOnlyConverter(),
            new SyscomTimeOnlyConverter(),
            SyscomIsoDateTimeOffsetConverter.Singleton
        },
    };
}

internal class SyscomParseStringConverter : JsonConverter<long>
{
    public override bool CanConvert(Type t) => t == typeof(long);

    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return long.TryParse(value, out var l) ? l : throw new Exception("Cannot unmarshal type long");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value.ToString(), options);
    }

    public static readonly SyscomParseStringConverter Singleton = new();
}

public class SyscomDateOnlyConverter(string? serializationFormat) : JsonConverter<DateOnly>
{
    private readonly string _serializationFormat = serializationFormat ?? "yyyy-MM-dd";

    public SyscomDateOnlyConverter() : this(null)
    {
    }

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return DateOnly.Parse(value ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(_serializationFormat));
}

public class SyscomTimeOnlyConverter(string? serializationFormat) : JsonConverter<TimeOnly>
{
    private readonly string _serializationFormat = serializationFormat ?? "HH:mm:ss.fff";

    public SyscomTimeOnlyConverter() : this(null)
    {
    }

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return TimeOnly.Parse(value!);
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(_serializationFormat));
}

internal class SyscomIsoDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override bool CanConvert(Type t) => t == typeof(DateTimeOffset);

    private const string DefaultDateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK";

    private DateTimeStyles _dateTimeStyles = DateTimeStyles.RoundtripKind;
    private string? _dateTimeFormat;
    private CultureInfo? _culture;

    public DateTimeStyles DateTimeStyles
    {
        get => _dateTimeStyles;
        set => _dateTimeStyles = value;
    }

    public string? DateTimeFormat
    {
        get => _dateTimeFormat ?? string.Empty;
        set => _dateTimeFormat = string.IsNullOrEmpty(value) ? null : value;
    }

    public CultureInfo Culture
    {
        get => _culture ?? CultureInfo.CurrentCulture;
        set => _culture = value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        if ((_dateTimeStyles & DateTimeStyles.AdjustToUniversal) == DateTimeStyles.AdjustToUniversal
            || (_dateTimeStyles & DateTimeStyles.AssumeUniversal) == DateTimeStyles.AssumeUniversal)
        {
            value = value.ToUniversalTime();
        }

        var text = value.ToString(_dateTimeFormat ?? DefaultDateTimeFormat, Culture);

        writer.WriteStringValue(text);
    }

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateText = reader.GetString();

        return string.IsNullOrEmpty(dateText) switch
        {
            false when !string.IsNullOrEmpty(_dateTimeFormat) => DateTimeOffset.ParseExact(dateText ?? string.Empty,
                _dateTimeFormat,
                Culture, _dateTimeStyles),
            false => DateTimeOffset.Parse(dateText ?? string.Empty, Culture, _dateTimeStyles),
            _ => default
        };
    }


    public static readonly SyscomIsoDateTimeOffsetConverter Singleton = new();
}
#pragma warning restore CS8618
#pragma warning restore CS8601
#pragma warning restore CS8603