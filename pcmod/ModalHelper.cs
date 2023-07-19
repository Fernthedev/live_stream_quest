using System.Reflection;
using BeatSaberMarkupLanguage;
using UnityEngine;

namespace LiveStreamQuest
{
    public static class ModalHelper
    {
        public static void Parse(Transform parent, string resource, object host)
        {
            BSMLParser.instance.Parse(Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), resource), parent.gameObject, host);
        }
    }
}