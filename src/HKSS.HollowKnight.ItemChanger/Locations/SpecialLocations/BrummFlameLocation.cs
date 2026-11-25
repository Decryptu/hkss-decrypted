// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/SpecialLocations/BrummFlameLocation.cs, commit e57bc4e (LGPL-2.1)
using HutongGames.PlayMaker.Actions;
using ItemChanger.FsmStateActions;
using ItemChanger.Extensions;

namespace ItemChanger.Locations.SpecialLocations
{
    /// <summary>
    /// Location which gives items when Brumm's Grimmkin flame would be received.
    /// </summary>
    public class BrummFlameLocation : AutoLocation
    {
        protected override void OnLoad()
        {
            Events.AddFsmEdit(UnsafeSceneName, new("Brumm Torch NPC", "Conversation Control"), EditBrummConvo);
            Events.AddLanguageEdit(new("CP2", "BRUMM_DEEPNEST_3"), EditBrummText);
        }

        protected override void OnUnload()
        {
            Events.RemoveFsmEdit(UnsafeSceneName, new("Brumm Torch NPC", "Conversation Control"), EditBrummConvo);
            Events.RemoveLanguageEdit(new("CP2", "BRUMM_DEEPNEST_3"), EditBrummText);
        }

        private void EditBrummConvo(PlayMakerFSM fsm)
        {
            FsmState? checkActive = fsm.GetState("Check Active");
            FsmState? convo1 = fsm.GetState("Convo 1");
            FsmState? get = fsm.GetState("Get");

            if (checkActive != null)
            {
                PlayerDataBoolTest? pdbt = checkActive.GetFirstActionOfType<PlayerDataBoolTest>();
                if (pdbt != null)
                {
                    checkActive.SetActions(
                        new DelegateBoolTest(() => IsBrummActive() && !Placement.AllObtained(), pdbt)
                    );
                }
            }

            convo1?.RemoveActionsOfType<IntCompare>();

            if (get != null && !get.Actions.Any(a => a is AsyncLambda))
            {
                var kept = new List<FsmStateAction>();
                if (get.Actions.Length > 6) kept.Add(get.Actions[6]);
                if (get.Actions.Length > 14) kept.Add(get.Actions[14]);
                kept.Add(new AsyncLambda(GiveAllAsync(fsm.transform)));
                get.SetActions(kept.ToArray());
            }
        }

        private void EditBrummText(ref string value)
        {
            string text = Placement.GetUIName(40);
            value = string.Format(Language.Language.Get("BRUMM_DEEPNEST_3_HINT", "Fmt"), text);
            Placement.OnPreview(text);
        }

        private static bool IsBrummActive()
        {
            int grimmchildLevel = PlayerData.instance.GetInt("grimmChildLevel");
            return PlayerData.instance.GetBool("equippedCharm_40") && grimmchildLevel >= 3; // && !Ref.PD.GetBool("gotBrummsFlame") && grimmchildLevel < 5;
        }
    }
}
