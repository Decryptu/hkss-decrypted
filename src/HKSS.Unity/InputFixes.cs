using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using HarmonyLib;
using HKSS.Core;
using InControl;

namespace HKSS.Unity;

/// <summary>
/// Prevents controller/keyboard binding corruption and mid-game input resets caused by:
/// 1. Duplicate DirectInput/Steam Virtual Gamepad interfaces flapping against an attached XInput controller.
/// 2. Redundant <c>InputHandler.MapControllerButtons</c> / <c>inputActions.Reset()</c> calls when active device changes.
/// 3. Keyboard and controller rebinds not syncing to <c>GameSettings</c> or flushing to <c>LocalSharedData</c>.
/// Compiled into both Hollow Knight and Silksong plugins.
/// </summary>
internal static class InputFixes
{
    private static readonly FieldInfo? NativeDeviceProfileField =
        AccessTools.Field(typeof(NativeInputDevice), "profile");

    private static readonly PropertyInfo? ActiveDeviceSetter =
        AccessTools.Property(typeof(InputManager), nameof(InputManager.ActiveDevice));

    private static readonly HashSet<NativeInputDevice> SuppressedDevices = new();
    private static InputHandler? _lastMappedHandler;
    private static InputDevice? _lastMappedDevice;
    private static GamepadType _lastMappedGamepadType = GamepadType.NONE;
    private static bool _hasMappedGamepad;
    private static bool _installed;

    internal static void Install(Harmony harmony)
    {
        if (_installed) return;
        _installed = true;

        TryPatch(harmony,
            AccessTools.Method(typeof(InputManager), nameof(InputManager.AttachDevice), new[] { typeof(InputDevice) }),
            postfix: nameof(OnDeviceTopologyChanged));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputManager), nameof(InputManager.DetachDevice), new[] { typeof(InputDevice) }),
            postfix: nameof(OnDeviceTopologyChanged));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), "ControllerAttached", new[] { typeof(InputDevice) }),
            prefix: nameof(BeforeControllerEvent));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), "ControllerActivated", new[] { typeof(InputDevice) }),
            prefix: nameof(BeforeControllerEvent));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), "ControllerDetached", new[] { typeof(InputDevice) }),
            postfix: nameof(AfterControllerDetached));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), nameof(InputHandler.SetActiveGamepadType), new[] { typeof(InputDevice) }),
            prefix: nameof(BeforeSetActiveGamepadType));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), nameof(InputHandler.MapControllerButtons), new[] { typeof(GamepadType) }),
            prefix: nameof(BeforeMapControllerButtons),
            postfix: nameof(AfterMapControllerButtons));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), nameof(InputHandler.ResetDefaultControllerButtonBindings)),
            prefix: nameof(InvalidateGamepadMapCache));

        TryPatch(harmony,
            AccessTools.Method(typeof(InputHandler), nameof(InputHandler.ResetDefaultKeyBindings)),
            prefix: nameof(InvalidateGamepadMapCache));

        TryPatch(harmony,
            AccessTools.Method(typeof(MappableKey), nameof(MappableKey.OnBindingAdded), new[] { typeof(PlayerAction), typeof(BindingSource) }),
            postfix: nameof(AfterKeyBindingAdded));

        TryPatch(harmony,
            AccessTools.Method(typeof(MappableControllerButton), nameof(MappableControllerButton.OnBindingAdded), new[] { typeof(PlayerAction), typeof(BindingSource) }),
            postfix: nameof(AfterButtonBindingAdded));

        TryPatch(harmony,
            AccessTools.Method(typeof(GameSettings), nameof(GameSettings.SaveKeyboardSettings)),
            postfix: nameof(AfterSaveKeyboardSettings));
    }

    private static void TryPatch(Harmony harmony, MethodBase? target, string? prefix = null, string? postfix = null)
    {
        if (target == null) return;
        try
        {
            harmony.Patch(
                target,
                prefix: prefix != null ? new HarmonyMethod(typeof(InputFixes), prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(typeof(InputFixes), postfix) : null);
        }
        catch (Exception ex)
        {
            HkssPlugin.Log?.LogWarning($"[Input] Failed to patch {target.DeclaringType?.Name}.{target.Name}: {ex.Message}");
        }
    }

    private static void OnDeviceTopologyChanged()
    {
        RefreshDuplicateDevicePassivity();
    }

    private static void RefreshDuplicateDevicePassivity()
    {
        var devices = InputManager.Devices;
        if (devices == null || devices.Count == 0)
        {
            SuppressedDevices.Clear();
            return;
        }

        var xinputDevices = new List<InputDeviceFilter.NativeDeviceDescriptor>();
        NativeInputDevice? firstXInput = null;

        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i] is NativeInputDevice native && native.Info.driverType == InputDeviceDriverType.XInput)
            {
                firstXInput ??= native;
                xinputDevices.Add(Describe(native));
            }
        }

        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i] is not NativeInputDevice native) continue;

            bool shouldSuppress = InputDeviceFilter.ShouldSuppressAsXInputDuplicate(Describe(native), xinputDevices);
            if (shouldSuppress)
            {
                if (SuppressedDevices.Add(native))
                {
                    native.Passive = true;
                    HkssPlugin.Log?.LogInfo(
                        $"[Input] Suppressed duplicate controller '{native.Name}' ({native.Info.driverType}, VID=0x{native.Info.vendorID:X4}, PID=0x{native.Info.productID:X4}) while XInput controller '{firstXInput?.Name}' is attached.");
                }
                else if (!native.Passive)
                {
                    native.Passive = true;
                }

                if (InputManager.ActiveDevice == native && firstXInput != null)
                {
                    ActiveDeviceSetter?.SetValue(null, firstXInput);
                }
            }
            else if (SuppressedDevices.Remove(native))
            {
                native.Passive = false;
                HkssPlugin.Log?.LogInfo($"[Input] Restored controller '{native.Name}' (no XInput controller attached).");
            }
        }

        SuppressedDevices.RemoveWhere(d => !d.IsAttached);
    }

    private static InputDeviceFilter.NativeDeviceDescriptor Describe(NativeInputDevice device)
    {
        InputDeviceInfo info = device.Info;
        bool isXInput = info.driverType == InputDeviceDriverType.XInput;
        bool isDirectInputOrHid = info.driverType == InputDeviceDriverType.DirectInput
            || info.driverType == InputDeviceDriverType.HID;

        bool matchedOnlyByLastResort = false;
        if (isDirectInputOrHid && NativeDeviceProfileField?.GetValue(device) is InputDeviceProfile profile)
        {
            matchedOnlyByLastResort = !profile.Matches(info) && profile.LastResortMatches(info);
        }

        return new InputDeviceFilter.NativeDeviceDescriptor(
            isXInput: isXInput,
            isDirectInputOrHid: isDirectInputOrHid,
            matchedOnlyByLastResort: matchedOnlyByLastResort,
            vendorId: info.vendorID,
            productId: info.productID,
            location: info.location,
            serialNumber: info.serialNumber);
    }

    private static bool BeforeControllerEvent(InputDevice? inputDevice)
    {
        if (inputDevice == null) return true;
        RefreshDuplicateDevicePassivity();
        return !inputDevice.Passive;
    }

    private static void AfterControllerDetached()
    {
        InvalidateGamepadMapCache();
    }

    private static bool BeforeSetActiveGamepadType(InputHandler __instance, InputDevice? inputDevice)
    {
        if (inputDevice == null) return true;
        RefreshDuplicateDevicePassivity();
        if (inputDevice.Passive) return false;

        // Avoid redundant inputActions.Reset() when the same device is reactivated with the same mapping.
        if (_hasMappedGamepad
            && ReferenceEquals(_lastMappedHandler, __instance)
            && ReferenceEquals(_lastMappedDevice, inputDevice)
            && __instance.gamepadState != GamepadState.DETACHED
            && __instance.activeGamepadType == _lastMappedGamepadType
            && __instance.activeGamepadType != GamepadType.NONE)
        {
            return false;
        }

        _lastMappedDevice = inputDevice;
        return true;
    }

    private static void BeforeMapControllerButtons(InputHandler __instance)
    {
        // Sync any in-memory keyboard bindings to GameSettings before MapControllerButtons
        // calls inputActions.Reset() + MapKeyboardLayoutFromGameSettings(), so unsaved
        // keyboard rebinds are never reverted by a controller event.
        if (__instance.inputActions == null || __instance.inputActions.IsListeningForBinding)
            return;

        if (HasAnyKeyboardOrMouseBinding(__instance.inputActions))
        {
            __instance.SendKeyBindingsToGameSettings();
        }
    }

    private static void AfterMapControllerButtons(InputHandler __instance, GamepadType gamePadType)
    {
        _lastMappedHandler = __instance;
        _lastMappedGamepadType = gamePadType;
        _hasMappedGamepad = gamePadType != GamepadType.NONE;
    }

    private static void InvalidateGamepadMapCache()
    {
        _hasMappedGamepad = false;
        _lastMappedDevice = null;
        _lastMappedGamepadType = GamepadType.NONE;
    }

    private static bool HasAnyKeyboardOrMouseBinding(HeroActions actions)
    {
        var list = actions.Actions;
        for (int i = 0; i < list.Count; i++)
        {
            var bindings = list[i].Bindings;
            for (int j = 0; j < bindings.Count; j++)
            {
                var type = bindings[j].BindingSourceType;
                if (type == BindingSourceType.KeyBindingSource || type == BindingSourceType.MouseBindingSource)
                    return true;
            }
        }
        return false;
    }

    private static void AfterKeyBindingAdded()
    {
        InputHandler? ih = InputHandler.Instance;
        if (ih != null && ih.inputActions != null && !ih.inputActions.IsListeningForBinding)
        {
            ih.SendKeyBindingsToGameSettings();
        }
    }

    private static void AfterButtonBindingAdded()
    {
        InputHandler? ih = InputHandler.Instance;
        if (ih != null && ih.inputActions != null && !ih.inputActions.IsListeningForBinding)
        {
            ih.SendButtonBindingsToGameSettings();
        }
    }

    private static void AfterSaveKeyboardSettings()
    {
        Platform.Current?.LocalSharedData?.Save();
    }
}
