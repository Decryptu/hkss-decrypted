// Ported from Decryptu/silksong-decrypted, src/SilksongRandomizer/Patches/ArchipelagoJsonCompatibilityPatches.cs
// Upstream commit: 34857d0 (MIT License).

using System;
using System.Reflection;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Converters;
using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HKSS.Unity;

/// <summary>
/// Unity's serializer discovery can register every <see cref="JsonConverter"/> in the AppDomain
/// globally in <see cref="JsonConvert.DefaultSettings"/>. Archipelago.MultiClient.Net's
/// <see cref="PermissionsEnumConverter"/> claims <c>string</c> and <c>int</c>, which breaks
/// RoomInfo deserialization, signal serialization, and Silksong save serialization unless scoped
/// to its own type.
/// </summary>
internal static class ArchipelagoJsonCompatibility
{
    internal static void Install(Harmony harmony)
    {
        MethodInfo? packetCanConvert = AccessTools.Method(
            typeof(ArchipelagoPacketConverter),
            nameof(ArchipelagoPacketConverter.CanConvert),
            new[] { typeof(Type) });
        if (packetCanConvert != null)
            harmony.Patch(packetCanConvert, prefix: new HarmonyMethod(typeof(ArchipelagoJsonCompatibility), nameof(PacketCanConvertPrefix)));

        MethodInfo? permsCanConvert = AccessTools.Method(
            typeof(PermissionsEnumConverter),
            nameof(PermissionsEnumConverter.CanConvert),
            new[] { typeof(Type) });
        if (permsCanConvert != null)
            harmony.Patch(permsCanConvert, prefix: new HarmonyMethod(typeof(ArchipelagoJsonCompatibility), nameof(PermissionsCanConvertPrefix)));

        MethodInfo? stringEnumCanConvert = AccessTools.Method(
            typeof(StringEnumConverter),
            nameof(StringEnumConverter.CanConvert),
            new[] { typeof(Type) });
        if (stringEnumCanConvert != null)
            harmony.Patch(stringEnumCanConvert, prefix: new HarmonyMethod(typeof(ArchipelagoJsonCompatibility), nameof(NumericProtocolEnumPrefix)));

        MethodInfo? genericEnumCanConvert = AccessTools.Method(
            typeof(JsonConverter<Enum>),
            nameof(JsonConverter<Enum>.CanConvert),
            new[] { typeof(Type) });
        if (genericEnumCanConvert != null)
            harmony.Patch(genericEnumCanConvert, prefix: new HarmonyMethod(typeof(ArchipelagoJsonCompatibility), nameof(NumericProtocolEnumPrefix)));
    }

    private static bool PacketCanConvertPrefix(Type objectType, ref bool __result)
    {
        __result = objectType == typeof(ArchipelagoPacketBase);
        return false;
    }

    private static bool PermissionsCanConvertPrefix(Type objectType, ref bool __result)
    {
        __result = objectType == typeof(Permissions);
        return false;
    }

    private static bool NumericProtocolEnumPrefix(Type objectType, ref bool __result)
    {
        Type targetType = Nullable.GetUnderlyingType(objectType) ?? objectType;
        if (targetType != typeof(ItemsHandlingFlags) &&
            targetType != typeof(ArchipelagoClientState) &&
            targetType != typeof(HintStatus))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
