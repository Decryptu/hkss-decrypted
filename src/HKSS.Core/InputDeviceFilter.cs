using System;
using System.Collections.Generic;

namespace HKSS.Core;

/// <summary>
/// Pure classification rules for suppressing duplicate DirectInput/HID controller interfaces
/// when an XInput controller is already attached.
/// </summary>
public static class InputDeviceFilter
{
    /// <summary>Valve Corporation USB Vendor ID (Steam Virtual Gamepad).</summary>
    public const ushort ValveVendorId = 0x28DE;

    /// <summary>Steam Virtual Gamepad DirectInput Product ID.</summary>
    public const ushort SteamVirtualGamepadProductId = 0x11FF;

    public readonly struct NativeDeviceDescriptor
    {
        public bool IsXInput { get; }
        public bool IsDirectInputOrHid { get; }
        public bool MatchedOnlyByLastResort { get; }
        public ushort VendorId { get; }
        public ushort ProductId { get; }
        public string Location { get; }
        public string SerialNumber { get; }

        public NativeDeviceDescriptor(
            bool isXInput,
            bool isDirectInputOrHid,
            bool matchedOnlyByLastResort,
            ushort vendorId,
            ushort productId,
            string? location,
            string? serialNumber)
        {
            IsXInput = isXInput;
            IsDirectInputOrHid = isDirectInputOrHid;
            MatchedOnlyByLastResort = matchedOnlyByLastResort;
            VendorId = vendorId;
            ProductId = productId;
            Location = location ?? string.Empty;
            SerialNumber = serialNumber ?? string.Empty;
        }
    }

    /// <summary>
    /// Returns true when a DirectInput/HID device should be marked passive because an XInput
    /// device is already attached and this interface is a conflicting duplicate or a
    /// vendor-only last-resort fallback profile (for example, an 8BitDo Ultimate 2 matched to
    /// the SN30 Pro DirectInput profile or Steam Input's virtual DirectInput gamepad).
    /// </summary>
    public static bool ShouldSuppressAsXInputDuplicate(
        NativeDeviceDescriptor candidate,
        IReadOnlyList<NativeDeviceDescriptor> attachedXInputDevices)
    {
        if (candidate.IsXInput || !candidate.IsDirectInputOrHid)
            return false;

        if (attachedXInputDevices == null || attachedXInputDevices.Count == 0)
            return false;

        // Steam Input exposes a virtual DirectInput "Steam Controller" (0x28DE:0x11FF)
        // alongside its XInput controller, causing active-device flapping.
        if (candidate.VendorId == ValveVendorId && candidate.ProductId == SteamVirtualGamepadProductId)
            return true;

        // Vendor-only LastResortMatchers (such as 8BitDo SN30 Pro matching any 8BitDo DirectInput
        // device by VendorID 0x2DC8) bind the wrong axes and buttons when the controller is
        // already operating via XInput.
        if (candidate.MatchedOnlyByLastResort)
            return true;

        for (int i = 0; i < attachedXInputDevices.Count; i++)
        {
            NativeDeviceDescriptor xinput = attachedXInputDevices[i];
            if (!string.IsNullOrEmpty(candidate.Location)
                && string.Equals(candidate.Location, xinput.Location, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(candidate.SerialNumber)
                && string.Equals(candidate.SerialNumber, xinput.SerialNumber, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
