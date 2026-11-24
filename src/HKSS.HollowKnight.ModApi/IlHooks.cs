using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using UnityEngine;
using static HKSS.HollowKnight.ModApi.HookUtil;

namespace IL
{
    public static class DreamPlantOrb
    {
        private static readonly MethodInfo M_OnTriggerEnter2D = Require(typeof(global::DreamPlantOrb), "OnTriggerEnter2D", typeof(Collider2D));
        public static event ILContext.Manipulator OnTriggerEnter2D
        {
            add => HookEndpointManager.Modify(M_OnTriggerEnter2D, value);
            remove => HookEndpointManager.Unmodify(M_OnTriggerEnter2D, value);
        }
    }

    public static class GameManager
    {
        private static readonly MethodInfo M_CheckAllAchievements = Require(typeof(global::GameManager), "CheckAllAchievements");
        public static event ILContext.Manipulator CheckAllAchievements
        {
            add => HookEndpointManager.Modify(M_CheckAllAchievements, value);
            remove => HookEndpointManager.Unmodify(M_CheckAllAchievements, value);
        }
    }
}
