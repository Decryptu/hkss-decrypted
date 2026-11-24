using System.Diagnostics;
using GlobalEnums;
using HarmonyLib;
using HKSS.Core;
using UnityEngine;

namespace HKSS.Unity;

/// <summary>
/// Takes the game from its title screen to gameplay without input: loads the run's save, or starts
/// a new game the first time, then tells the launcher the player can take over.
/// </summary>
internal sealed class AutoLoad : MonoBehaviour
{
    private enum Step
    {
        WaitForMenu,
        CheckingSave,
        Loading,
        Done,
    }

    private const float BootTimeout = 120f;
    private const float SettledDelay = 2.5f;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Step _step;
    private float _nextSkip;
    private float _inLevelSince = -1f;
    private float _menuFadeSpeed;

    internal static bool IsReady { get; private set; }

    private void Update()
    {
        if (_step == Step.Done) return;

        // The travel screen owns the sound until the player can see the game.
        AudioListener.volume = 0f;
        if (_clock.Elapsed.TotalSeconds > BootTimeout)
        {
            // The player may still be able to continue from the menu, so show the game.
            Finish(Signal.Error, $"Not in gameplay after {BootTimeout} s");
            return;
        }

        switch (_step)
        {
            case Step.WaitForMenu:
                TryStart();
                break;
            case Step.Loading:
                SkipOpening();
                if (PlayerIsInGame()) Finish(Signal.Ready, null);
                break;
        }
    }

    private void TryStart()
    {
        GameManager gm = GameManager._instance;
        UIManager? ui = gm == null ? null : gm.ui;
        if (gm == null || ui == null || !gm.IsMenuScene()) return;
        if (_menuFadeSpeed == 0f)
        {
            _menuFadeSpeed = ui.MENU_FADE_SPEED;
            ui.MENU_FADE_SPEED = 100f;
        }
        if (ui.menuState != MainMenuState.MAIN_MENU) return;
        if (!Platform.Current.IsSaveStoreMounted) return;

#if SILKSONG
        if (!FastBoot.MenuShown) return;
#else
        // Hollow Knight fades its first menu in without GoToMainMenu.
        CanvasGroup menu = ui.mainMenuScreen;
        if (!menu.isActiveAndEnabled || !menu.interactable || menu.alpha < 0.99f) return;
#endif

        Mark("menu");
        _step = Step.CheckingSave;
        Platform.Current.IsSaveSlotInUse(RunContext.SaveSlot, used =>
        {
            _step = Step.Loading;
            gm.profileID = RunContext.SaveSlot;
            if (used) gm.LoadGameFromUI(RunContext.SaveSlot);
            else ui.StartNewGame();
            Mark(used ? "loading save" : "starting a new game");
        });
    }

    private void SkipOpening()
    {
        if (Time.unscaledTime < _nextSkip) return;
        _nextSkip = Time.unscaledTime + 0.1f;
        OpeningSequence opening = FindFirstObjectByType<OpeningSequence>();
        if (opening == null) return;
        var chain = Traverse.Create(opening).Field("chainSequence").GetValue<ChainSequence>();
        if (chain == null || !chain.IsPlaying) return;
#if SILKSONG
        // Silksong locks parts of its opening until they have been watched.
        Traverse.Create(chain).Property("CurrentSequence").GetValue<SkippableSequence>()?.AllowSkip();
#endif
        chain.SkipSingle();
    }

    private bool PlayerIsInGame()
    {
        GameManager gm = GameManager._instance;
        HeroController hero = HeroController.instance;
        if (gm == null || hero == null || gm.IsInSceneTransition || !gm.IsGameplayScene())
        {
            _inLevelSince = -1f;
            return false;
        }
        if (hero.CanInput()) return true;

        // A new game opens on a short scene without control; show it rather than hide it.
        if (_inLevelSince < 0f) _inLevelSince = Time.unscaledTime;
        return Time.unscaledTime - _inLevelSince > SettledDelay;
    }

    private void Finish(string state, string? detail)
    {
        _step = Step.Done;
        IsReady = true;
        if (_menuFadeSpeed > 0f) GameManager._instance.ui.MENU_FADE_SPEED = _menuFadeSpeed;
        AudioListener.volume = 1f;
        Mark(state);
        HkssPlugin.Signals.Send(state, detail: detail);
    }

    private void Mark(string what) =>
        HkssPlugin.Log.LogInfo($"[boot] {what} at {Time.realtimeSinceStartup:0.00} s");
}
