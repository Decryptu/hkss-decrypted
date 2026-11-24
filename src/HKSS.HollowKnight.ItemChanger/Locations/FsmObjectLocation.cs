// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Locations/FsmObjectLocation.cs, commit e57bc4e (LGPL-2.1)
using ItemChanger.Extensions;

namespace ItemChanger.Locations
{
    /// <summary>
    /// ObjectLocation which replaces an FsmGameObject.
    /// </summary>
    public class FsmObjectLocation : ObjectLocation
    {
        public string fsmName;
        public string fsmParent;
        public string fsmVariable;

        public override void OnActiveSceneChanged(Scene to)
        {
            GetContainer(out GameObject obj, out string containerType);
            PlaceContainer(obj, containerType);
            FindGameObject(fsmParent).LocateMyFSM(fsmName).FsmVariables.FindFsmGameObject(fsmVariable).Value = obj;
        }
    }
}
