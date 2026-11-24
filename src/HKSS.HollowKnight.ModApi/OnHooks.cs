using System;
using System.Collections;
using System.Reflection;
using GlobalEnums;
using MonoMod.RuntimeDetour.HookGen;
using UnityEngine;

namespace HKSS.HollowKnight.ModApi
{
    internal static class HookUtil
    {
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static MethodInfo Require(Type type, string name, params Type[] paramTypes)
        {
            MethodInfo? m = paramTypes.Length == 0
                ? type.GetMethod(name, Flags, null, Type.EmptyTypes, null)
                : type.GetMethod(name, Flags, null, paramTypes, null);
            if (m == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return m;
        }
    }
}

namespace On
{
    using static HKSS.HollowKnight.ModApi.HookUtil;

    public static class DeactivateIfPlayerdataFalse
    {
        public delegate void orig_OnEnable(global::DeactivateIfPlayerdataFalse self);
        public delegate void hook_OnEnable(orig_OnEnable orig, global::DeactivateIfPlayerdataFalse self);
        private static readonly MethodInfo M_OnEnable = Require(typeof(global::DeactivateIfPlayerdataFalse), "OnEnable");
        public static event hook_OnEnable OnEnable
        {
            add => HookEndpointManager.Add<hook_OnEnable>(M_OnEnable, value);
            remove => HookEndpointManager.Remove<hook_OnEnable>(M_OnEnable, value);
        }
    }

    public static class DeactivateIfPlayerdataTrue
    {
        public delegate void orig_OnEnable(global::DeactivateIfPlayerdataTrue self);
        public delegate void hook_OnEnable(orig_OnEnable orig, global::DeactivateIfPlayerdataTrue self);
        private static readonly MethodInfo M_OnEnable = Require(typeof(global::DeactivateIfPlayerdataTrue), "OnEnable");
        public static event hook_OnEnable OnEnable
        {
            add => HookEndpointManager.Add<hook_OnEnable>(M_OnEnable, value);
            remove => HookEndpointManager.Remove<hook_OnEnable>(M_OnEnable, value);
        }
    }

    public static class DialogueBox
    {
        public delegate void orig_HideText(global::DialogueBox self);
        public delegate void hook_HideText(orig_HideText orig, global::DialogueBox self);
        private static readonly MethodInfo M_HideText = Require(typeof(global::DialogueBox), "HideText");
        public static event hook_HideText HideText
        {
            add => HookEndpointManager.Add<hook_HideText>(M_HideText, value);
            remove => HookEndpointManager.Remove<hook_HideText>(M_HideText, value);
        }
    }

    public static class DreamPlant
    {
        public delegate void orig_Awake(global::DreamPlant self);
        public delegate void hook_Awake(orig_Awake orig, global::DreamPlant self);
        private static readonly MethodInfo M_Awake = Require(typeof(global::DreamPlant), "Awake");
        public static event hook_Awake Awake
        {
            add => HookEndpointManager.Add<hook_Awake>(M_Awake, value);
            remove => HookEndpointManager.Remove<hook_Awake>(M_Awake, value);
        }

        public delegate IEnumerator orig_CheckOrbs(global::DreamPlant self);
        public delegate IEnumerator hook_CheckOrbs(orig_CheckOrbs orig, global::DreamPlant self);
        private static readonly MethodInfo M_CheckOrbs = Require(typeof(global::DreamPlant), "CheckOrbs");
        public static event hook_CheckOrbs CheckOrbs
        {
            add => HookEndpointManager.Add<hook_CheckOrbs>(M_CheckOrbs, value);
            remove => HookEndpointManager.Remove<hook_CheckOrbs>(M_CheckOrbs, value);
        }

        public delegate void orig_OnTriggerEnter2D(global::DreamPlant self, Collider2D collision);
        public delegate void hook_OnTriggerEnter2D(orig_OnTriggerEnter2D orig, global::DreamPlant self, Collider2D collision);
        private static readonly MethodInfo M_OnTriggerEnter2D = Require(typeof(global::DreamPlant), "OnTriggerEnter2D", typeof(Collider2D));
        public static event hook_OnTriggerEnter2D OnTriggerEnter2D
        {
            add => HookEndpointManager.Add<hook_OnTriggerEnter2D>(M_OnTriggerEnter2D, value);
            remove => HookEndpointManager.Remove<hook_OnTriggerEnter2D>(M_OnTriggerEnter2D, value);
        }

        public delegate void orig_Start(global::DreamPlant self);
        public delegate void hook_Start(orig_Start orig, global::DreamPlant self);
        private static readonly MethodInfo M_Start = Require(typeof(global::DreamPlant), "Start");
        public static event hook_Start Start
        {
            add => HookEndpointManager.Add<hook_Start>(M_Start, value);
            remove => HookEndpointManager.Remove<hook_Start>(M_Start, value);
        }
    }

    public static class GameManager
    {
        public delegate void orig_BeginSceneTransition(global::GameManager self, global::GameManager.SceneLoadInfo info);
        public delegate void hook_BeginSceneTransition(orig_BeginSceneTransition orig, global::GameManager self, global::GameManager.SceneLoadInfo info);
        private static readonly MethodInfo M_BeginSceneTransition = Require(typeof(global::GameManager), "BeginSceneTransition", typeof(global::GameManager.SceneLoadInfo));
        public static event hook_BeginSceneTransition BeginSceneTransition
        {
            add => HookEndpointManager.Add<hook_BeginSceneTransition>(M_BeginSceneTransition, value);
            remove => HookEndpointManager.Remove<hook_BeginSceneTransition>(M_BeginSceneTransition, value);
        }

        public delegate void orig_ContinueGame(global::GameManager self);
        public delegate void hook_ContinueGame(orig_ContinueGame orig, global::GameManager self);
        private static readonly MethodInfo M_ContinueGame = Require(typeof(global::GameManager), "ContinueGame");
        public static event hook_ContinueGame ContinueGame
        {
            add => HookEndpointManager.Add<hook_ContinueGame>(M_ContinueGame, value);
            remove => HookEndpointManager.Remove<hook_ContinueGame>(M_ContinueGame, value);
        }

        public delegate void orig_FinishedEnteringScene(global::GameManager self);
        public delegate void hook_FinishedEnteringScene(orig_FinishedEnteringScene orig, global::GameManager self);
        private static readonly MethodInfo M_FinishedEnteringScene = Require(typeof(global::GameManager), "FinishedEnteringScene");
        public static event hook_FinishedEnteringScene FinishedEnteringScene
        {
            add => HookEndpointManager.Add<hook_FinishedEnteringScene>(M_FinishedEnteringScene, value);
            remove => HookEndpointManager.Remove<hook_FinishedEnteringScene>(M_FinishedEnteringScene, value);
        }

        public delegate void orig_ResetSemiPersistentItems(global::GameManager self);
        public delegate void hook_ResetSemiPersistentItems(orig_ResetSemiPersistentItems orig, global::GameManager self);
        private static readonly MethodInfo M_ResetSemiPersistentItems = Require(typeof(global::GameManager), "ResetSemiPersistentItems");
        public static event hook_ResetSemiPersistentItems ResetSemiPersistentItems
        {
            add => HookEndpointManager.Add<hook_ResetSemiPersistentItems>(M_ResetSemiPersistentItems, value);
            remove => HookEndpointManager.Remove<hook_ResetSemiPersistentItems>(M_ResetSemiPersistentItems, value);
        }

        public delegate void orig_SaveLevelState(global::GameManager self);
        public delegate void hook_SaveLevelState(orig_SaveLevelState orig, global::GameManager self);
        private static readonly MethodInfo M_SaveLevelState = Require(typeof(global::GameManager), "SaveLevelState");
        public static event hook_SaveLevelState SaveLevelState
        {
            add => HookEndpointManager.Add<hook_SaveLevelState>(M_SaveLevelState, value);
            remove => HookEndpointManager.Remove<hook_SaveLevelState>(M_SaveLevelState, value);
        }

        public delegate void orig_StartNewGame(global::GameManager self, bool permadeathMode, bool bossRushMode);
        public delegate void hook_StartNewGame(orig_StartNewGame orig, global::GameManager self, bool permadeathMode, bool bossRushMode);
        private static readonly MethodInfo M_StartNewGame = Require(typeof(global::GameManager), "StartNewGame", typeof(bool), typeof(bool));
        public static event hook_StartNewGame StartNewGame
        {
            add => HookEndpointManager.Add<hook_StartNewGame>(M_StartNewGame, value);
            remove => HookEndpointManager.Remove<hook_StartNewGame>(M_StartNewGame, value);
        }
    }

    public static class HealthManager
    {
        public delegate void orig_Die(global::HealthManager self, float? attackDirection, AttackTypes attackType, bool ignoreEvasion);
        public delegate void hook_Die(orig_Die orig, global::HealthManager self, float? attackDirection, AttackTypes attackType, bool ignoreEvasion);
        private static readonly MethodInfo M_Die = Require(typeof(global::HealthManager), "Die", typeof(float?), typeof(AttackTypes), typeof(bool));
        public static event hook_Die Die
        {
            add => HookEndpointManager.Add<hook_Die>(M_Die, value);
            remove => HookEndpointManager.Remove<hook_Die>(M_Die, value);
        }
    }

    public static class HeroController
    {
        public delegate bool orig_CanAttack(global::HeroController self);
        public delegate bool hook_CanAttack(orig_CanAttack orig, global::HeroController self);
        private static readonly MethodInfo M_CanAttack = Require(typeof(global::HeroController), "CanAttack");
        public static event hook_CanAttack CanAttack
        {
            add => HookEndpointManager.Add<hook_CanAttack>(M_CanAttack, value);
            remove => HookEndpointManager.Remove<hook_CanAttack>(M_CanAttack, value);
        }

        public delegate bool orig_CanDash(global::HeroController self);
        public delegate bool hook_CanDash(orig_CanDash orig, global::HeroController self);
        private static readonly MethodInfo M_CanDash = Require(typeof(global::HeroController), "CanDash");
        public static event hook_CanDash CanDash
        {
            add => HookEndpointManager.Add<hook_CanDash>(M_CanDash, value);
            remove => HookEndpointManager.Remove<hook_CanDash>(M_CanDash, value);
        }

        public delegate bool orig_CanFocus(global::HeroController self);
        public delegate bool hook_CanFocus(orig_CanFocus orig, global::HeroController self);
        private static readonly MethodInfo M_CanFocus = Require(typeof(global::HeroController), "CanFocus");
        public static event hook_CanFocus CanFocus
        {
            add => HookEndpointManager.Add<hook_CanFocus>(M_CanFocus, value);
            remove => HookEndpointManager.Remove<hook_CanFocus>(M_CanFocus, value);
        }

        public delegate void orig_DoAttack(global::HeroController self);
        public delegate void hook_DoAttack(orig_DoAttack orig, global::HeroController self);
        private static readonly MethodInfo M_DoAttack = Require(typeof(global::HeroController), "DoAttack");
        public static event hook_DoAttack DoAttack
        {
            add => HookEndpointManager.Add<hook_DoAttack>(M_DoAttack, value);
            remove => HookEndpointManager.Remove<hook_DoAttack>(M_DoAttack, value);
        }

        public delegate void orig_MaxHealth(global::HeroController self);
        public delegate void hook_MaxHealth(orig_MaxHealth orig, global::HeroController self);
        private static readonly MethodInfo M_MaxHealth = Require(typeof(global::HeroController), "MaxHealth");
        public static event hook_MaxHealth MaxHealth
        {
            add => HookEndpointManager.Add<hook_MaxHealth>(M_MaxHealth, value);
            remove => HookEndpointManager.Remove<hook_MaxHealth>(M_MaxHealth, value);
        }

        public delegate IEnumerator orig_Respawn(global::HeroController self);
        public delegate IEnumerator hook_Respawn(orig_Respawn orig, global::HeroController self);
        private static readonly MethodInfo M_Respawn = Require(typeof(global::HeroController), "Respawn");
        public static event hook_Respawn Respawn
        {
            add => HookEndpointManager.Add<hook_Respawn>(M_Respawn, value);
            remove => HookEndpointManager.Remove<hook_Respawn>(M_Respawn, value);
        }

        public delegate void orig_TakeDamage(global::HeroController self, GameObject go, CollisionSide damageSide, int damageAmount, int hazardType);
        public delegate void hook_TakeDamage(orig_TakeDamage orig, global::HeroController self, GameObject go, CollisionSide damageSide, int damageAmount, int hazardType);
        private static readonly MethodInfo M_TakeDamage = Require(typeof(global::HeroController), "TakeDamage", typeof(GameObject), typeof(CollisionSide), typeof(int), typeof(int));
        public static event hook_TakeDamage TakeDamage
        {
            add => HookEndpointManager.Add<hook_TakeDamage>(M_TakeDamage, value);
            remove => HookEndpointManager.Remove<hook_TakeDamage>(M_TakeDamage, value);
        }
    }

    public static class PersistentIntItem
    {
        public delegate void orig_Awake(global::PersistentIntItem self);
        public delegate void hook_Awake(orig_Awake orig, global::PersistentIntItem self);
        private static readonly MethodInfo M_Awake = Require(typeof(global::PersistentIntItem), "Awake");
        public static event hook_Awake Awake
        {
            add => HookEndpointManager.Add<hook_Awake>(M_Awake, value);
            remove => HookEndpointManager.Remove<hook_Awake>(M_Awake, value);
        }
    }

    public static class PlayMakerFSM
    {
        public delegate void orig_OnEnable(global::PlayMakerFSM self);
        public delegate void hook_OnEnable(orig_OnEnable orig, global::PlayMakerFSM self);
        private static readonly MethodInfo M_OnEnable = Require(typeof(global::PlayMakerFSM), "OnEnable");
        public static event hook_OnEnable OnEnable
        {
            add => HookEndpointManager.Add<hook_OnEnable>(M_OnEnable, value);
            remove => HookEndpointManager.Remove<hook_OnEnable>(M_OnEnable, value);
        }
    }

    public static class PlayerData
    {
        public delegate void orig_CountCharms(global::PlayerData self);
        public delegate void hook_CountCharms(orig_CountCharms orig, global::PlayerData self);
        private static readonly MethodInfo M_CountCharms = Require(typeof(global::PlayerData), "CountCharms");
        public static event hook_CountCharms CountCharms
        {
            add => HookEndpointManager.Add<hook_CountCharms>(M_CountCharms, value);
            remove => HookEndpointManager.Remove<hook_CountCharms>(M_CountCharms, value);
        }

        public delegate void orig_CountGameCompletion(global::PlayerData self);
        public delegate void hook_CountGameCompletion(orig_CountGameCompletion orig, global::PlayerData self);
        private static readonly MethodInfo M_CountGameCompletion = Require(typeof(global::PlayerData), "CountGameCompletion");
        public static event hook_CountGameCompletion CountGameCompletion
        {
            add => HookEndpointManager.Add<hook_CountGameCompletion>(M_CountGameCompletion, value);
            remove => HookEndpointManager.Remove<hook_CountGameCompletion>(M_CountGameCompletion, value);
        }

        public delegate void orig_IncrementInt(global::PlayerData self, string intName);
        public delegate void hook_IncrementInt(orig_IncrementInt orig, global::PlayerData self, string intName);
        private static readonly MethodInfo M_IncrementInt = Require(typeof(global::PlayerData), "IncrementInt", typeof(string));
        public static event hook_IncrementInt IncrementInt
        {
            add => HookEndpointManager.Add<hook_IncrementInt>(M_IncrementInt, value);
            remove => HookEndpointManager.Remove<hook_IncrementInt>(M_IncrementInt, value);
        }

        public delegate void orig_SetInt(global::PlayerData self, string intName, int value);
        public delegate void hook_SetInt(orig_SetInt orig, global::PlayerData self, string intName, int value);
        private static readonly MethodInfo M_SetInt = Require(typeof(global::PlayerData), "SetInt", typeof(string), typeof(int));
        public static event hook_SetInt SetInt
        {
            add => HookEndpointManager.Add<hook_SetInt>(M_SetInt, value);
            remove => HookEndpointManager.Remove<hook_SetInt>(M_SetInt, value);
        }
    }

    public static class SceneAdditiveLoadConditional
    {
        public delegate void orig_OnEnable(global::SceneAdditiveLoadConditional self);
        public delegate void hook_OnEnable(orig_OnEnable orig, global::SceneAdditiveLoadConditional self);
        private static readonly MethodInfo M_OnEnable = Require(typeof(global::SceneAdditiveLoadConditional), "OnEnable");
        public static event hook_OnEnable OnEnable
        {
            add => HookEndpointManager.Add<hook_OnEnable>(M_OnEnable, value);
            remove => HookEndpointManager.Remove<hook_OnEnable>(M_OnEnable, value);
        }
    }

    public static class SceneManager
    {
        public delegate void orig_Start(global::SceneManager self);
        public delegate void hook_Start(orig_Start orig, global::SceneManager self);
        private static readonly MethodInfo M_Start = Require(typeof(global::SceneManager), "Start");
        public static event hook_Start Start
        {
            add => HookEndpointManager.Add<hook_Start>(M_Start, value);
            remove => HookEndpointManager.Remove<hook_Start>(M_Start, value);
        }
    }

    public static class ShopItemStats
    {
        public delegate void orig_OnEnable(global::ShopItemStats self);
        public delegate void hook_OnEnable(orig_OnEnable orig, global::ShopItemStats self);
        private static readonly MethodInfo M_OnEnable = Require(typeof(global::ShopItemStats), "OnEnable");
        public static event hook_OnEnable OnEnable
        {
            add => HookEndpointManager.Add<hook_OnEnable>(M_OnEnable, value);
            remove => HookEndpointManager.Remove<hook_OnEnable>(M_OnEnable, value);
        }
    }

    public static class ShopMenuStock
    {
        public delegate void orig_BuildItemList(global::ShopMenuStock self);
        public delegate void hook_BuildItemList(orig_BuildItemList orig, global::ShopMenuStock self);
        private static readonly MethodInfo M_BuildItemList = Require(typeof(global::ShopMenuStock), "BuildItemList");
        public static event hook_BuildItemList BuildItemList
        {
            add => HookEndpointManager.Add<hook_BuildItemList>(M_BuildItemList, value);
            remove => HookEndpointManager.Remove<hook_BuildItemList>(M_BuildItemList, value);
        }

        public delegate bool orig_StockLeft(global::ShopMenuStock self);
        public delegate bool hook_StockLeft(orig_StockLeft orig, global::ShopMenuStock self);
        private static readonly MethodInfo M_StockLeft = Require(typeof(global::ShopMenuStock), "StockLeft");
        public static event hook_StockLeft StockLeft
        {
            add => HookEndpointManager.Add<hook_StockLeft>(M_StockLeft, value);
            remove => HookEndpointManager.Remove<hook_StockLeft>(M_StockLeft, value);
        }
    }
}
