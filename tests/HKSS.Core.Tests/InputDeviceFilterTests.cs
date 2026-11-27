using System.Collections.Generic;
using HKSS.Core;
using Xunit;

namespace HKSS.Core.Tests;

public sealed class InputDeviceFilterTests
{
    private static InputDeviceFilter.NativeDeviceDescriptor XInput(
        ushort vid = 0x045E,
        ushort pid = 0x028E,
        string? location = "usb-1",
        string? serial = "SN-001") =>
        new(
            isXInput: true,
            isDirectInputOrHid: false,
            matchedOnlyByLastResort: false,
            vendorId: vid,
            productId: pid,
            location: location,
            serialNumber: serial);

    private static InputDeviceFilter.NativeDeviceDescriptor DirectInput(
        ushort vid,
        ushort pid,
        bool lastResort = false,
        string? location = null,
        string? serial = null) =>
        new(
            isXInput: false,
            isDirectInputOrHid: true,
            matchedOnlyByLastResort: lastResort,
            vendorId: vid,
            productId: pid,
            location: location,
            serialNumber: serial);

    [Fact]
    public void SuppressesLastResort8BitDoDirectInputWhenXInputAttached()
    {
        var xinput = new List<InputDeviceFilter.NativeDeviceDescriptor> { XInput() };
        var eightBitDoGhost = DirectInput(vid: 0x2DC8, pid: 0x310B, lastResort: true);

        Assert.True(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(eightBitDoGhost, xinput));
    }

    [Fact]
    public void SuppressesSteamVirtualGamepadDirectInputWhenXInputAttached()
    {
        var xinput = new List<InputDeviceFilter.NativeDeviceDescriptor> { XInput() };
        var steamVirtual = DirectInput(
            vid: InputDeviceFilter.ValveVendorId,
            pid: InputDeviceFilter.SteamVirtualGamepadProductId,
            lastResort: false);

        Assert.True(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(steamVirtual, xinput));
    }

    [Fact]
    public void SuppressesDirectInputSharingLocationOrSerialWithXInput()
    {
        var xinput = new List<InputDeviceFilter.NativeDeviceDescriptor>
        {
            XInput(location: "hid-port-3", serial: "ABC123"),
        };

        Assert.True(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(
            DirectInput(vid: 0x1234, pid: 0x5678, location: "HID-PORT-3"), xinput));
        Assert.True(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(
            DirectInput(vid: 0x1234, pid: 0x5678, serial: "abc123"), xinput));
    }

    [Fact]
    public void KeepsDirectInputDevicesWhenNoXInputAttached()
    {
        var none = new List<InputDeviceFilter.NativeDeviceDescriptor>();
        var eightBitDo = DirectInput(vid: 0x2DC8, pid: 0x310B, lastResort: true);
        var steamVirtual = DirectInput(
            vid: InputDeviceFilter.ValveVendorId,
            pid: InputDeviceFilter.SteamVirtualGamepadProductId);

        Assert.False(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(eightBitDo, none));
        Assert.False(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(steamVirtual, none));
    }

    [Fact]
    public void KeepsExactMatchedSeparateDirectInputController()
    {
        var xinput = new List<InputDeviceFilter.NativeDeviceDescriptor> { XInput(location: "usb-1", serial: "SN-001") };
        var dualSense = DirectInput(vid: 0x054C, pid: 0x0CE6, lastResort: false, location: "usb-2", serial: "PS5-999");

        Assert.False(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(dualSense, xinput));
    }

    [Fact]
    public void NeverSuppressesXInputDeviceItself()
    {
        var xinput = XInput();
        var list = new List<InputDeviceFilter.NativeDeviceDescriptor> { xinput };

        Assert.False(InputDeviceFilter.ShouldSuppressAsXInputDuplicate(xinput, list));
    }
}
