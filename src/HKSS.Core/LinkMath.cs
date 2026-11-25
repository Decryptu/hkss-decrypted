using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace HKSS.Core;

/// <summary>
/// Pure math and packet helpers for Hollow Knight SoulLink, GeoLink, and KnockbackLink.
/// </summary>
public static class LinkMath
{
    public const int HkBaseSoulCapacity = 99;
    public const int HkMaxSharedGeo = 9999999;
    public const string KnockbackLinkTag = "KnockbackLink";

    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static int ComputePredictedPool(int authoritative, int pendingDelta, int min, int max) =>
        Clamp(authoritative + pendingDelta, min, max);

    public static bool TryNormalizeDirection(double x, double y, out float nx, out float ny)
    {
        nx = 0f;
        ny = 0f;
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            return false;

        double scale = Math.Max(Math.Abs(x), Math.Abs(y));
        if (scale <= 0.0) return false;

        x /= scale;
        y /= scale;
        double len = Math.Sqrt(x * x + y * y);
        if (len <= 0.0) return false;

        nx = (float)(x / len);
        ny = (float)(y / len);
        return true;
    }

    public static bool TryParseKnockbackPacket(
        IReadOnlyCollection<string>? tags,
        IDictionary<string, JToken>? data,
        out float dirX,
        out float dirY,
        out string sender,
        out string? senderUuid,
        out string dedupKey)
    {
        dirX = 0f;
        dirY = 0f;
        sender = string.Empty;
        senderUuid = null;
        dedupKey = string.Empty;

        if (tags == null || data == null) return false;
        bool hasTag = false;
        foreach (string t in tags)
        {
            if (string.Equals(t, KnockbackLinkTag, StringComparison.Ordinal))
            {
                hasTag = true;
                break;
            }
        }
        if (!hasTag) return false;

        if (!data.TryGetValue("source", out JToken? sourceTok) || sourceTok.Type != JTokenType.String)
            return false;
        if (!data.TryGetValue("time", out JToken? timeTok) || !TryNumber(timeTok, out double time))
            return false;
        if (!data.TryGetValue("value", out JToken? valTok) || valTok is not JObject vec)
            return false;
        if (!TryNumber(vec["x"], out double x) || !TryNumber(vec["y"], out double y))
            return false;
        if (!TryNormalizeDirection(x, y, out dirX, out dirY))
            return false;

        sender = sourceTok.Value<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sender) || sender.Length > 256)
            return false;

        if (data.TryGetValue("uuid", out JToken? uuidTok))
        {
            if (uuidTok.Type != JTokenType.String) return false;
            senderUuid = uuidTok.Value<string>();
            if (senderUuid != null && senderUuid.Length > 256) return false;
        }

        dedupKey = FormatKnockbackKey(sender, senderUuid, time);
        return true;
    }

    public static string FormatKnockbackKey(string sender, string? senderUuid, double time) =>
        $"{sender}\n{senderUuid ?? string.Empty}\n{time.ToString("R", CultureInfo.InvariantCulture)}";

    private static bool TryNumber(JToken? token, out double value)
    {
        value = 0;
        if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            return false;
        value = token.Value<double>();
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
