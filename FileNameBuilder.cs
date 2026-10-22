using System.Text;

namespace SoloScreen;

/// <summary>按模式串生成文件名（不含扩展名）。</summary>
internal static class FileNameBuilder
{
    public const string DefaultPattern = "$YYYY-$MM-$DD-$hh-$mm-$ss-$fff";

    public static string Build(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) pattern = DefaultPattern;

        var now = DateTime.Now;
        var name = pattern
            .Replace("$YYYY", now.ToString("yyyy"))
            .Replace("$fff", now.ToString("fff"))
            .Replace("$MM", now.ToString("MM"))
            .Replace("$DD", now.ToString("dd"))
            .Replace("$hh", now.ToString("HH"))
            .Replace("$mm", now.ToString("mm"))
            .Replace("$ss", now.ToString("ss"));

        // 非法字符替换为 '-'
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '-');
        }

        return name.Length == 0 ? now.ToString("yyyy-MM-dd-HH-mm-ss-fff") : name;
    }
}
