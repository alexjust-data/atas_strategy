namespace MyAtas.Risk.Utils;

public static class Diagnostics
{
    public static string Short(string s, int n = 6) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n]);
}