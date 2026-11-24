// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Modules/GatedGrimmTent.cs, commit e57bc4e (LGPL-2.1)
namespace ItemChanger.Modules
{
    /// <summary>
    /// Module which adds a gate in Grimm_Main_Tent when the Nightmare Lantern has not been lit.
    /// </summary>
    [DefaultModule]
    public class GatedGrimmTent : Module
    {
        public override void Initialize()
        {
            Events.AddSceneChangeEdit(SceneNames.Grimm_Main_Tent, ActivateGrimmTentGate);
        }

        public override void Unload()
        {
            Events.RemoveSceneChangeEdit(SceneNames.Grimm_Main_Tent, ActivateGrimmTentGate);
        }

        private void ActivateGrimmTentGate(Scene to)
        {
            if (!PlayerData.instance.GetBool(nameof(PlayerData.nightmareLanternLit)))
            {
                PlayMakerFSM.BroadcastEvent("BG CLOSE");
            }
        }
    }
}
