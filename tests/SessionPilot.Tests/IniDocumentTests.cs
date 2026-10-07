using System.Text;

namespace SessionPilot.Tests;

public class IniDocumentTests
{
    [Fact]
    public void Utf16BomAndCrlf_RoundTripBytes()
    {
        const string text = "; synthetic comment\r\n[Custom]\r\nAlpha=one\r\n\r\n# tail\r\n";
        var bytes = EncodeUtf16(text);
        var document = IniDocument.Parse(bytes);
        Assert.Equal(TextEncoding.Utf16Le, document.Encoding);
        Assert.True(document.HasBom);
        Assert.Equal("crlf", document.NewLineStyle);
        Assert.Equal(bytes, document.Serialize());
    }

    [Fact]
    public void LfWithoutBom_RoundTripBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("[Section]\nKeep=value\n");
        var document = IniDocument.Parse(bytes);
        Assert.Equal(TextEncoding.Utf8, document.Encoding);
        Assert.False(document.HasBom);
        Assert.Equal("lf", document.NewLineStyle);
        Assert.Equal(bytes, document.Serialize());
    }

    [Fact]
    public void UnknownSectionAndKey_SurviveAnEdit()
    {
        var document = IniDocument.Parse("[Known]\r\nVisible=old\r\n[Vendor.Private]\r\nMystery=stay\r\n", TextEncoding.Utf8);
        var edited = document.Apply([new IniEdit { Section = "Known", Key = "Visible", Value = "new" }]);
        Assert.True(edited.Succeeded);
        var text = Encoding.UTF8.GetString(edited.Document.Serialize());
        Assert.Contains("[Vendor.Private]", text, StringComparison.Ordinal);
        Assert.Contains("Mystery=stay", text, StringComparison.Ordinal);
        Assert.Contains("Visible=new", text, StringComparison.Ordinal);
        Assert.Equal("old", document.Find("Known", "Visible").Line!.Value);
    }

    [Fact]
    public void DuplicateKey_RefusesTheWrite()
    {
        var document = IniDocument.Parse("[ProcessDefaults]\r\nDefaultPriorities=a.exe,normal\r\nDefaultPriorities=b.exe,idle\r\n");
        var edited = document.Apply([new IniEdit { Section = "ProcessDefaults", Key = "DefaultPriorities", Value = "c.exe,normal" }]);
        Assert.False(edited.Succeeded);
        Assert.Contains("ambiguous", edited.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingKey_IsNotCreated()
    {
        var document = IniDocument.Parse("[ProcessDefaults]\r\nDefaultPriorities=\r\n");
        var edited = document.Apply([new IniEdit { Section = "ProcessDefaults", Key = "CPUSets", Value = "0" }]);
        Assert.False(edited.Succeeded);
        Assert.Contains("does not exist", edited.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InlineComment_RefusesTheEdit()
    {
        var document = IniDocument.Parse("[Custom]\r\nAlpha=one ; keep\r\n");
        var edited = document.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]);
        Assert.False(edited.Succeeded);
    }

    [Fact]
    public void SpacedValue_KeepsItsWhitespace_AndAnUneditedFileRoundTrips()
    {
        var bytes = Encoding.UTF8.GetBytes("[Custom]\r\nAlpha = old \r\n");
        var document = IniDocument.Parse(bytes);
        Assert.Equal(bytes, document.Serialize());
        var edited = document.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "new" }]);
        Assert.True(edited.Succeeded);
        Assert.Contains("Alpha = new \r\n", Encoding.UTF8.GetString(edited.Document.Serialize()), StringComparison.Ordinal);
    }

    [Fact]
    public void Encoding_RejectsValuesItCannotStore()
    {
        var latin = IniDocument.Parse(Encoding.GetEncoding(28591).GetBytes("[Custom]\r\nAlpha=caf\u00e9\r\n"));
        Assert.Equal(TextEncoding.Latin1, latin.Encoding);
        var rejected = latin.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "日本" }]);
        Assert.False(rejected.Succeeded);
        Assert.Contains("Latin1", rejected.Message, StringComparison.Ordinal);
        Assert.True(latin.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "é" }]).Succeeded);
        var utf = IniDocument.Parse("[Custom]\r\nAlpha=one\r\n", TextEncoding.Utf8);
        Assert.True(utf.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "日本" }]).Succeeded);
    }

    [Fact]
    public void DuplicateEdit_IsRejected()
    {
        var document = IniDocument.Parse("[Custom]\r\nAlpha=one\r\n");
        var edited = document.Apply(
        [
            new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" },
            new IniEdit { Section = "custom", Key = "alpha", Value = "three" }
        ]);
        Assert.False(edited.Succeeded);
        Assert.Contains("edited more than once", edited.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewInlineComment_IsRejected()
    {
        var document = IniDocument.Parse("[Custom]\r\nAlpha=one\r\n");
        var edited = document.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "two ; hidden" }]);
        Assert.False(edited.Succeeded);
        Assert.Contains("inline comment", edited.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("one", document.Find("Custom", "Alpha").Line!.Value);
    }

    [Fact]
    public void Apply_RejectsALeadingSemicolon_ThatTheKeptSpacingTurnsIntoAComment()
    {
        var document = IniDocument.Parse("[Custom]\r\nAlpha = one\r\n");
        var edited = document.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = ";x" }]);
        Assert.False(edited.Succeeded);
        Assert.Contains("inline comment", edited.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] EncodeUtf16(string text)
    {
        var body = Encoding.Unicode.GetBytes(text);
        var bytes = new byte[body.Length + 2];
        bytes[0] = 0xFF;
        bytes[1] = 0xFE;
        body.CopyTo(bytes, 2);
        return bytes;
    }
}
