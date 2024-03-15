using System;
using System.Collections;
using System.Collections.Generic;
using Jotunn;
using OdinOnDemand.Components;

namespace OdinOnDemand.Utils.Net
{
    public static class ComponentLists
    {
        public static readonly Dictionary<Type, IList> MediaComponentLists = new Dictionary<Type, IList>
        {
            { typeof(MediaPlayerComponent), new List<MediaPlayerComponent>() },
            { typeof(BeltPlayerComponent), new List<BeltPlayerComponent>() },
            { typeof(CartPlayerComponent), new List<CartPlayerComponent>() },
            { typeof(ReceiverComponent), new List<ReceiverComponent>() }
        };
        
        public static readonly List<SpeakerComponent> SpeakerComponentList = new List<SpeakerComponent>();
        public static void AddComponent(Type type, object component)
        {
            if (MediaComponentLists.ContainsKey(type))
            {
                var list = MediaComponentLists[type];
                list.Add(component);
                MediaComponentLists[type] = list;
            }
        }

        public static void RemoveComponent(Type type, object component)
        {
            if (MediaComponentLists.ContainsKey(type))
            {
                var list = MediaComponentLists[type];
                list.Remove(component);
                MediaComponentLists[type] = list;
            }
        }
    }
}