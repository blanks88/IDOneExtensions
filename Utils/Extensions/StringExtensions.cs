namespace Utils.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Concat path and source validating presence of slashes
    /// </summary>
    /// <param name="path">File base path or Url</param>
    /// <param name="src">Relative path, file name or endpoint</param>
    /// <returns>Path without double slashed if present on endpoint</returns>
    public static string ConcatToPath(this string path, string? src)
        => !string.IsNullOrEmpty(src)
            ? $"{path}{(path.EndsWith("/") || src.StartsWith("/") ? "" : "/")}{src}"
            : path;
}