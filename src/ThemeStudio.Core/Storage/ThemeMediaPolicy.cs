namespace ThemeStudio.Core.Storage;

public static class ThemeMediaPolicy
{
    public const long MaximumImageBytes = 32L * 1024 * 1024;
    public const long MaximumVideoBytes = 64L * 1024 * 1024;

    public static bool IsVideo(string extension) => extension.ToLowerInvariant() is ".mp4" or ".webm" or ".mov";

    public static long MaximumBytes(string extension) => IsVideo(extension) ? MaximumVideoBytes : MaximumImageBytes;

    public static void ValidateLength(string extension, long length)
    {
        if (length <= 0)
            throw new InvalidDataException("媒体文件为空。");

        var maximum = MaximumBytes(extension);
        if (length > maximum)
        {
            var label = IsVideo(extension) ? "视频" : "图片";
            throw new InvalidDataException($"{label}不能超过 {maximum / 1024 / 1024} MB。");
        }
    }
}
