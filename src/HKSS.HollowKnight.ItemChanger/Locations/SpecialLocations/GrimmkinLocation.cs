// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/GrimmkinLocation.cs, commit e57bc4e (LGPL-2.1)
using HutongGames.PlayMaker.Actions;
using ItemChanger.FsmStateActions;
using ItemChanger.Extensions;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Location for modifying the unique Grimmkin spawn of a scene.
    /// </summary>
    public class GrimmkinLocation : AutoLocation
    {
        public int grimmkinLevel;

        protected override void OnLoad()
        {
            Events.AddFsmEdit(UnsafeSceneName, new("Control"), EditFlamebearerControl);
            Events.AddFsmEdit(UnsafeSceneName, new("Flamebearer Spawn", "Spawn Control"), EditFlamebearerSpawn);
        }

        protected override void OnUnload()
        {
            Events.RemoveFsmEdit(UnsafeSceneName, new("Control"), EditFlamebearerControl);
            Events.RemoveFsmEdit(UnsafeSceneName, new("Flamebearer Spawn", "Spawn Control"), EditFlamebearerSpawn);
        }

        public override GiveInfo GetGiveInfo()
        {
            var info = base.GetGiveInfo();
            info.MessageType = MessageType.Corner;
            return info;
        }

        private void EditFlamebearerControl(PlayMakerFSM fsm)
        {
            if (!fsm.gameObject.name.StartsWith("Flamebearer")) return;

            FsmState? init = fsm.GetState("Init");
            if (init == null) return;
            int idx = Array.FindIndex(init.Actions, a => a is GetPlayerDataInt);
            if (idx >= 0)
            {
                init.ReplaceAction(new SetIntValue
                {
                    intVariable = ((GetPlayerDataInt)init.Actions[idx]).storeValue,
                    intValue = grimmkinLevel
                }, idx);
            }
        }

        private void EditFlamebearerSpawn(PlayMakerFSM fsm)
        {
            FsmState? state = fsm.GetState("State");
            FsmState? get = fsm.GetState("Get");

            if (state != null)
            {
                // Override Grimmchild level check
                state.ClearTransitions();
                state.AddTransition("FINISHED", $"Level {grimmkinLevel}");
                state.AddTransition("KILLED", "Do Nothing");
                bool Check()
                {
                    return Placement.AllObtained() || !GrimmchildRequirement();
                }

                BoolTest? bt = state.GetFirstActionOfType<BoolTest>();
                if (bt != null)
                {
                    state.SetActions(new DelegateBoolTest(Check, bt));
                }
            }

            if (get != null && !get.Actions.Any(a => a is AsyncLambda))
            {
                var kept = new List<FsmStateAction>();
                if (get.Actions.Length > 6) kept.Add(get.Actions[6]);
                kept.Add(new AsyncLambda(GiveAll));
                get.SetActions(kept.ToArray());
            }
        }

        public bool GrimmchildRequirement()
        {
            return PlayerData.instance.GetBool(nameof(PlayerData.equippedCharm_40)) && PlayerData.instance.GetInt(nameof(PlayerData.grimmChildLevel)) >= grimmkinLevel;
        }

    }
}
