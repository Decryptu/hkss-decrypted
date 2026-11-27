// Ported from ArchipelagoMW/Archipelago.HollowKnight, IC/Modules/DeathLinkModule.cs & DeathLinkMessages.cs, commit 373cc00 (MIT)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using HKSS.Core;
using HKSS.Unity;
using HutongGames.PlayMaker;
using ItemChanger;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
using ItemChanger.UIDefs;
using Modding;

namespace HKSS.HollowKnight;

public enum DeathLinkStatus
{
    None,
    Pending,
    Dying,
}

public static class HkDeathLinkMessages
{
    public static readonly List<string> DefaultMessages = new()
    {
        "@ died.",
        "@ has perished.",
        "@ made poor life choices.",
        "@ didn't listen to Hornet's advice.",
        "@ took damage equal to or more than their current HP.",
        "@ made a fatal mistake.",
        "@ threw some shade at @.",
        "@ decided to set up a Shade Skip.",
        "Hopefully @ didn't have a fragile charm equipped.",
        "A true servant gives all for the Kingdom. Let @ relieve you of your life.",
        "Through @'s sacrifice, you are now dead.",
        "The truce remains. Our vigil holds. @ must respawn.",
    };

    public static readonly List<string> UnknownMessages = new()
    {
        "@ has died in a manner most unusual.",
        "@ found a way to break the game, and the game broke @ back.",
        "@ has lost The Game.",
    };

    public static readonly Dictionary<int, List<string>> MessagesByType = new()
    {
        {
            1, // Enemy damage
            new List<string>
            {
                "@ has discovered that there are bugs in Hallownest.",
                "@ should have dodged.",
                "@ should have jumped.",
                "@ significantly mistimed their parry attempt.",
                "@ should have considered equipping Dreamshield.",
                "@ must have never fought that enemy before.",
                "@ did not make it to phase 2.",
                "@ dashed in the wrong direction.",
                "@ tried to talk it out.",
                "@ made masterful use of their vulnerability frames.",
            }
        },
        {
            2, // Spikes
            new List<string>
            {
                "@ was in the wrong place.",
                "@ mistimed their jump.",
                "@ didn't see the sharp things.",
                "@ didn't see that saw.",
                "@ fought the spikes and the spikes won.",
                "@ sought roses but found only thorns.",
                "@ was pricked to death.",
                "@ dashed in the wrong direction.",
                "@ found their own Path of Pain.",
                "@ has strayed from the White King's roads.",
            }
        },
        {
            3, // Acid
            new List<string>
            {
                "@ was in the wrong place.",
                "@ mistimed their jump.",
                "@ forgot their floaties.",
                "What @ thought was H2O was H2SO4.",
                "@ wishes they could swim.",
                "@ used the wrong kind of dive.",
                "@ got into a fight with a pool of liquid and lost.",
                "@ forgot how to swim.",
            }
        },
        {
            999, // Dream realm
            new List<string>
            {
                "@ dozed off for good.",
                "@ was caught sleeping on the job.",
                "@ sought dreams but found only nightmares.",
                "@ got lost in Limbo.",
                "Good night, @.",
                "@ is resting in pieces.",
                "@ exploded into a thousand pieces of essence.",
                "Hey, @, you're finally awake.",
            }
        },
    };

    private static readonly Random Rng = new();

    public static string GetDeathMessage(int cause, string player)
    {
        bool knownCause = MessagesByType.TryGetValue(cause, out List<string>? typedMessages);
        List<string> candidates;
        if (knownCause && typedMessages != null)
        {
            candidates = new List<string>(typedMessages.Count + DefaultMessages.Count);
            candidates.AddRange(typedMessages);
            candidates.AddRange(DefaultMessages);
        }
        else
        {
            candidates = UnknownMessages;
        }

        string message = candidates[Rng.Next(0, candidates.Count)].Replace("@", player);
        if (!knownCause)
        {
            HkssPlugin.Log.LogWarning($"[HK] Unknown DeathLink cause of death: {cause}");
            message += $" (Type: {cause})";
        }
        return message;
    }
}

public sealed class HkDeathLinkModule : ItemChanger.Modules.Module
{
    public const string PreventShadeVariableName = "Deathlink Prevent Shade";
    public const string IsDeathlinkVariableName = "Is Deathlink Death";

    private static readonly MethodInfo? HeroControllerCanTakeDamage = typeof(HeroController)
        .GetMethod("CanTakeDamage", BindingFlags.NonPublic | BindingFlags.Instance);

    public DeathLinkShadeHandling ShadeHandling { get; set; } = DeathLinkShadeHandling.Vanilla;
    public bool BreakFragileCharms { get; set; } = true;

    private readonly ConcurrentQueue<string> _pendingDeathNotices = new();
    private DeathLinkService? _service;
    private DeathLinkStatus _status;
    private int _lastDamageType;
    private DateTime _lastDamageTime;
    private bool _hasEditedFsm;

    private void Reset()
    {
        _lastDamageType = 0;
        _lastDamageTime = DateTime.MinValue;
        _status = DeathLinkStatus.None;
        while (_pendingDeathNotices.TryDequeue(out _)) { }
    }

    public override void Initialize()
    {
        Reset();
        ApManager.OnConnected += AttachToSession;
        ApManager.OnDisconnected += DetachFromSession;
        if (ApManager.Instance?.Client is ApClient client && client.IsConnected)
        {
            AttachToSession(client);
        }

        ModHooks.HeroUpdateHook += OnHeroUpdate;
        On.HeroController.TakeDamage += OnTakeDamage;
        Events.AddFsmEdit(new FsmID("Hero Death Anim"), FsmEdit);
    }

    public override void Unload()
    {
        Reset();
        ApManager.OnConnected -= AttachToSession;
        ApManager.OnDisconnected -= DetachFromSession;
        DetachFromSession();

        ModHooks.HeroUpdateHook -= OnHeroUpdate;
        On.HeroController.TakeDamage -= OnTakeDamage;
        Events.RemoveFsmEdit(new FsmID("Hero Death Anim"), FsmEdit);
        _hasEditedFsm = false;
    }

    private void AttachToSession(ApClient client)
    {
        DetachFromSession();
        ArchipelagoSession? session = client.Session;
        if (session == null) return;

        try
        {
            _service = session.CreateDeathLinkService();
            _service.EnableDeathLink();
            _service.OnDeathLinkReceived += OnDeathLinkReceived;
            HkssPlugin.Log.LogInfo($"[HK] DeathLink enabled (shade={ShadeHandling}, breakFragile={BreakFragileCharms}).");
        }
        catch (Exception ex)
        {
            HkssPlugin.Log.LogError($"[HK] Failed to initialize DeathLink service: {ex}");
            _service = null;
        }
    }

    private void DetachFromSession()
    {
        if (_service != null)
        {
            _service.OnDeathLinkReceived -= OnDeathLinkReceived;
            _service = null;
        }
    }

    private void FsmEdit(PlayMakerFSM fsm)
    {
        if (_hasEditedFsm) return;
        _hasEditedFsm = true;

        FsmState? mapZone = fsm.GetState("Map Zone");
        FsmState? breakMsg = fsm.GetState("Break Msg");
        FsmState? removeOvercharm = fsm.GetState("Remove Overcharm");
        FsmState? deathEnding = fsm.GetState("End");
        FsmState? limitSoul = fsm.GetState("Limit Soul?");
        if (mapZone == null || breakMsg == null || removeOvercharm == null || deathEnding == null || limitSoul == null)
        {
            HkssPlugin.Log.LogWarning("[HK] Hero Death Anim FSM did not have expected states; skipping DeathLink FSM edit.");
            return;
        }

        FsmBool preventShade = fsm.AddFsmBool(PreventShadeVariableName, false);
        FsmBool isDeathlink = fsm.AddFsmBool(IsDeathlinkVariableName, false);

        mapZone.AddFirstAction(new Lambda(() =>
        {
            bool isDeathlinkDeath = _status == DeathLinkStatus.Dying;
            if (!isDeathlinkDeath)
            {
                SendDeathLink();
                return;
            }

            isDeathlink.Value = true;
            DeathLinkShadeHandling handling = PlayerOptions.DeathLink.Value switch
            {
                DeathRecovery.Vanilla => DeathLinkShadeHandling.Vanilla,
                DeathRecovery.NoNewMarker => DeathLinkShadeHandling.Shadeless,
                DeathRecovery.PreserveExisting => DeathLinkShadeHandling.Shade,
                _ => ShadeHandling,
            };
            preventShade.Value = !(
                handling == DeathLinkShadeHandling.Vanilla
                || (handling == DeathLinkShadeHandling.Shade && PlayerData.instance.shadeScene == "None"));
        }));

        FsmState createShadeCheck = fsm.AddState("Create Shade?");
        createShadeCheck.AddLastAction(new DelegateBoolTest(() => isDeathlink.Value, null, "FINISHED"));
        createShadeCheck.AddLastAction(new DelegateBoolTest(() => preventShade.Value, "SKIP SHADE", null));
        createShadeCheck.AddTransition("SKIP SHADE", "Save");
        createShadeCheck.AddTransition("FINISHED", "Remove Geo");

        FsmState breakFragilesCheck = fsm.AddState("Break Fragiles?");
        breakFragilesCheck.AddLastAction(new DelegateBoolTest(() => isDeathlink.Value, null, "FINISHED"));
        breakFragilesCheck.AddLastAction(new DelegateBoolTest(() => !BreakFragileCharms, "SKIP BREAK", null));
        breakFragilesCheck.AddTransition("SKIP BREAK", createShadeCheck);
        breakFragilesCheck.AddTransition("FINISHED", "Break Glass HP");

        mapZone.RemoveTransitionsOn("FINISHED");
        mapZone.AddTransition("FINISHED", breakFragilesCheck);

        breakMsg.RemoveTransitionsOn("FINISHED");
        breakMsg.AddTransition("FINISHED", createShadeCheck);
        removeOvercharm.RemoveTransitionsOn("FINISHED");
        removeOvercharm.AddTransition("FINISHED", createShadeCheck);

        limitSoul.Actions = Array.Empty<FsmStateAction>();
        if (deathEnding.Actions.Length > 0)
        {
            deathEnding.Actions[0] = new Lambda(() =>
            {
                if (!preventShade.Value)
                {
                    fsm.Fsm.BroadcastEvent("SOUL LIMITER UP");
                    GameManager.instance.StartSoulLimiter();
                }
            });
        }

        FsmState cleanupDeathlink = fsm.AddState("Cleanup Deathlink");
        cleanupDeathlink.AddFirstAction(new Lambda(() =>
        {
            preventShade.Value = false;
            isDeathlink.Value = false;
            _status = DeathLinkStatus.None;
        }));

        foreach (string stateName in new[] { "Dream Return", "Wait for HeroController", "Shade?" })
        {
            FsmState? endState = fsm.GetState(stateName);
            endState?.AddTransition("FINISHED", cleanupDeathlink);
        }
    }

    public void MurderPlayer()
    {
        HeroController? hc = HeroController.instance;
        if (hc == null) return;
        HkssPlugin.Log.LogDebug($"[HK] DeathLink-initiated kill in scene {GameManager.instance?.sceneName}.");
        _status = DeathLinkStatus.Dying;
        hc.TakeDamage(hc.gameObject, GlobalEnums.CollisionSide.other, 9999, 0);
    }

    private void OnHeroUpdate()
    {
        while (_pendingDeathNotices.TryDequeue(out string cause))
        {
            ApManager.Instance?.PushToast($"DeathLink: {cause}", ToastKind.Error);
            new MsgUIDef
            {
                name = new BoxedString(cause),
                sprite = new EmptySprite(),
            }.SendMessage(MessageType.Corner, null);
        }

        if (_status != DeathLinkStatus.Pending) return;
        if (DoubleSteelSoul.IsDefeated)
        {
            _status = DeathLinkStatus.None;
            return;
        }
        HeroController? hc = HeroController.instance;
        PlayerData? pd = PlayerData.instance;
        if (hc == null || pd == null) return;

        bool canTakeDamage = HeroControllerCanTakeDamage == null || (bool)HeroControllerCanTakeDamage.Invoke(hc, null);
        if (hc.acceptingInput
            && hc.damageMode == GlobalEnums.DamageMode.FULL_DAMAGE
            && pd.GetInt(nameof(PlayerData.health)) > 0
            && canTakeDamage)
        {
            MurderPlayer();
        }
    }

    private void OnTakeDamage(
        On.HeroController.orig_TakeDamage orig,
        HeroController self,
        UnityEngine.GameObject go,
        GlobalEnums.CollisionSide damageSide,
        int damageAmount,
        int hazardType)
    {
        _lastDamageTime = DateTime.UtcNow;
        _lastDamageType = hazardType;
        orig(self, go, damageSide, damageAmount, hazardType);
    }

    public void SendDeathLink()
    {
        if (_status == DeathLinkStatus.Dying || _service == null) return;

        ApClient? client = ApManager.Instance?.Client;
        ArchipelagoSession? session = client?.Session;
        if (session == null || client == null) return;

        if ((DateTime.UtcNow - _lastDamageTime).TotalSeconds > 5)
        {
            _lastDamageType = 0;
        }

        string alias = session.Players?.ActivePlayer?.Alias ?? client.ConnectedSlot;
        string message = HkDeathLinkMessages.GetDeathMessage(_lastDamageType, alias);
        HkssPlugin.Log.LogInfo($"[HK] Sending DeathLink: {message}");
        _service.SendDeathLink(new DeathLink(alias, message));
    }

    private void OnDeathLinkReceived(DeathLink deathLink)
    {
        if (_status != DeathLinkStatus.None) return;

        _status = DeathLinkStatus.Pending;
        string cause = string.IsNullOrWhiteSpace(deathLink.Cause)
            ? $"{deathLink.Source} died."
            : deathLink.Cause;
        _pendingDeathNotices.Enqueue(cause);
        _lastDamageType = 0;
    }
}
