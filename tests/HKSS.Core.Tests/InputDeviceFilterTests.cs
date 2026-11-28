using System.Collections.Generic;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class InputDeviceFilterTests
{
    private static InputDeviceFilter.NativeDeviceDescriptor Primary(
        ushort vid = 0x045E,
        ushort pid = 0x028E,
        bool lastResort = false,
        string? location = "usb-1",
        string? serial = "SN-001") =>
        new(
            isPrimaryDriver: true,
            isLegacyDriver: false,
            matchedOnlyByLastResort: lastResort,
            vendorId: vid,
            productId: pid,
            location: location,
            serialNumber: serial);

    private static InputDeviceFilter.NativeDeviceDescriptor Legacy(
        ushort vid,
        ushort pid,
        bool lastResort = false,
        string? location = null,
        string? serial = null) =>
        new(
            isPrimaryDriver: false,
            isLegacyDriver: true,
            matchedOnlyByLastResort: lastResort,
            vendorId: vid,
            productId: pid,
            location: location,
            serialNumber: serial);

    [Fact]
    public void SuppressesLastResort8BitDoDirectInputWhenPrimaryAttached()
    {
        var primary = new List<InputDeviceFilter.NativeDeviceDescriptor> { Primary() };
        var eightBitDoGhost = Legacy(vid: 0x2DC8, pid: 0x310B, lastResort: true);

        Assert.True(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(eightBitDoGhost, primary));
    }

    [Fact]
    public void SuppressesSteamVirtualGamepadDirectInputWhenPrimaryAttached()
    {
        var primary = new List<InputDeviceFilter.NativeDeviceDescriptor> { Primary() };
        var steamVirtual = Legacy(
            vid: InputDeviceFilter.ValveVendorId,
            pid: InputDeviceFilter.SteamVirtualGamepadProductId,
            lastResort: false);

        Assert.True(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(steamVirtual, primary));
    }

    [Fact]
    public void SuppressesLegacyDeviceSharingLocationSerialOrVidPidWithPrimary()
    {
        var primary = new List<InputDeviceFilter.NativeDeviceDescriptor>
        {
            Primary(vid: 0x2DC8, pid: 0x310B, location: "hid-port-3", serial: "ABC123"),
        };

        Assert.True(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(
            Legacy(vid: 0x1234, pid: 0x5678, location: "HID-PORT-3"), primary));
        Assert.True(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(
            Legacy(vid: 0x1234, pid: 0x5678, serial: "abc123"), primary));
        Assert.True(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(
            Legacy(vid: 0x2DC8, pid: 0x310B, location: "other", serial: "other"), primary));
    }

    [Fact]
    public void KeepsLegacyDevicesWhenNoPrimaryAttached()
    {
        var none = new List<InputDeviceFilter.NativeDeviceDescriptor>();
        var eightBitDo = Legacy(vid: 0x2DC8, pid: 0x310B, lastResort: true);
        var steamVirtual = Legacy(
            vid: InputDeviceFilter.ValveVendorId,
            pid: InputDeviceFilter.SteamVirtualGamepadProductId);

        Assert.False(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(eightBitDo, none));
        Assert.False(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(steamVirtual, none));
    }

    [Fact]
    public void KeepsExactMatchedSeparateLegacyController()
    {
        var primary = new List<InputDeviceFilter.NativeDeviceDescriptor>
        {
            Primary(vid: 0x045E, pid: 0x028E, location: "usb-1", serial: "SN-001"),
        };
        var dualSense = Legacy(vid: 0x054C, pid: 0x0CE6, lastResort: false, location: "usb-2", serial: "PS5-999");

        Assert.False(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(dualSense, primary));
    }

    [Fact]
    public void NeverSuppressesPrimaryDeviceEvenWhenMatchedByLastResort()
    {
        // SDLGenericNativeProfile matches SDLController devices via LastResortMatchers.
        var sdlGeneric = Primary(vid: 0x2DC8, pid: 0x310B, lastResort: true);
        var list = new List<InputDeviceFilter.NativeDeviceDescriptor> { sdlGeneric };

        Assert.False(InputDeviceFilter.ShouldSuppressAsPrimaryDuplicate(sdlGeneric, list));
    }
}
