// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Modules/SwimSkill.cs, commit e57bc4e (LGPL-2.1)
using GlobalEnums;
using HutongGames.PlayMaker.Actions;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
#pragma warning disable IDE1006 // Naming Styles

namespace ItemChanger.Modules
{
    /// <summary>
    /// Module which implements the swim custom skill.
    /// </summary>
    public class SwimSkill : Module
    {
        public bool canSwim { get; set; }

        public override void Initialize()
        {
            Events.AddFsmEdit(new("Surface Water Region"), EditWaterSurface);
            Modding.ModHooks.GetPlayerBoolHook += SkillBoolGetOverride;
            Modding.ModHooks.SetPlayerBoolHook += SkillBoolSetOverride;
        }

        public override void Unload()
        {
            Events.RemoveFsmEdit(new("Surface Water Region"), EditWaterSurface);
            Modding.ModHooks.GetPlayerBoolHook -= SkillBoolGetOverride;
            Modding.ModHooks.SetPlayerBoolHook -= SkillBoolSetOverride;
        }

        private bool SkillBoolGetOverride(string boolName, bool value)
        {
            return boolName switch
            {
                nameof(canSwim) => canSwim,
                _ => value,
            };
        }

        private bool SkillBoolSetOverride(string boolName, bool value)
        {
            switch (boolName)
            {
                case nameof(canSwim):
                    canSwim = value;
                    PlayMakerFSM.BroadcastEvent("SWIM GET");
                    break;
            }
            return value;
        }

        private void EditWaterSurface(PlayMakerFSM fsm)
        {
            if (fsm.gameObject.LocateMyFSM("Acid Armour Check") != null) return; // acid
            if (fsm.GetState("Check Swim") != null) return; // already edited

            Transform? splashTransform = fsm.transform.Find("Splash Surface");
            FsmState? idle = fsm.GetState("Idle");
            FsmState? bigSplash = fsm.GetState("Big Splash?");
            FsmState? splashInNorm = fsm.GetState("Splash In Norm");
            if (splashTransform == null || idle == null || bigSplash == null || splashInNorm == null || idle.Transitions.Length == 0)
                return;

            GameObject splashSurface = splashTransform.gameObject;
            splashSurface.layer = 17; // orig is 8, which can enable seam jumping when it intersects with other terrain colliders
            if (splashSurface.GetComponent<NonBouncer>() == null)
                splashSurface.AddComponent<NonBouncer>();

            FsmState checkSwim = fsm.AddState("Check Swim");
            FsmState damageHero = fsm.AddState("Damage Hero");

            idle.Transitions[0].SetToState(checkSwim);
            checkSwim.AddFirstAction(new DelegateBoolTest(() => canSwim, "SWIM", "DAMAGE"));
            checkSwim.AddTransition("SWIM", bigSplash);
            checkSwim.AddTransition("DAMAGE", damageHero);

            damageHero.SetActions(splashInNorm.Actions.Where(a => a is not SetPosition).ToArray()); // play splash audio and fling splash particles
            damageHero.AddLastAction(new Lambda(() => HeroController.instance.TakeDamage(fsm.gameObject, CollisionSide.bottom, 1, 2)));
            damageHero.AddTransition(FsmEvent.Finished, idle);
        }
    }
}
