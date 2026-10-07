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
