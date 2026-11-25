// Ported from HomothetyHk/HollowKnight.ItemChanger, ItemChanger/Internal/MessageController.cs, commit e57bc4e (LGPL-2.1)
using System.Collections;

namespace ItemChanger.Internal
{
    public class MessageController : MonoBehaviour
    {
        private static MessageController instance;
        private static Coroutine? activeDisplay;
        private readonly Queue<(Sprite sprite, string text)> messages = new Queue<(Sprite icon, string text)>();

        public void Update()
        {
            if (messages.Any() && activeDisplay == null)
            {
                var (sprite, text) = messages.Dequeue();
                activeDisplay = StartCoroutine(SendMessage(sprite, text));
            }
        }

        public static void Enqueue(Sprite sprite, string text)
        {
            instance.messages.Enqueue((sprite, text));
        }

        public static void Error()
        {
            Enqueue(Modding.CanvasUtil.NullSprite(), "Error: see ModLog for details.");
        }

        private IEnumerator SendMessage(Sprite sprite, string text)
        {
            try
            {
                GameObject popup = ObjectCache.RelicGetMsg;
                if (popup != null)
                {
                    Transform textTransform = popup.transform.Find("Text");
                    if (textTransform != null)
                    {
                        TMProOld.TextMeshPro tmp = textTransform.GetComponent<TMProOld.TextMeshPro>();
                        if (tmp != null) tmp.text = text;
                    }

                    Transform iconTransform = popup.transform.Find("Icon");
                    if (iconTransform != null)
                    {
                        SpriteRenderer sr = iconTransform.GetComponent<SpriteRenderer>();
                        if (sr != null)
                        {
                            sr.sprite = sprite;
                            sr.sortingOrder = 1; // show on top of blankers, etc
                        }
                    }

                    popup.SetActive(true);
                }

                yield return new WaitForSeconds(3f);
            }
            finally
            {
                activeDisplay = null;
            }
        }


        internal static void Setup()
        {
            GameObject obj = new GameObject("MessageController parent");
            instance = obj.AddComponent<MessageController>();
            DontDestroyOnLoad(obj);
        }

        internal static void Clear()
        {
            instance.messages.Clear();
        }
    }
}
