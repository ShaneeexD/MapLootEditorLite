using System;
using System.Linq;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public static class ComponentHelper
    {
        public static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var type = Type.GetType(name, false);
            if (type != null)
                return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType(name, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetTypes().FirstOrDefault(t => t.Name == name);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            return null;
        }

        public static Component AddComponentByName(GameObject go, string name)
        {
            if (go == null || string.IsNullOrWhiteSpace(name))
                return null;
            var type = ResolveType(name);
            if (type == null)
            {
                Plugin.Log.LogWarning($"Cannot add component '{name}': type not found.");
                return null;
            }
            if (!typeof(Component).IsAssignableFrom(type))
            {
                Plugin.Log.LogWarning($"Type '{type.Name}' is not a Component.");
                return null;
            }
            return go.AddComponent(type);
        }

        public static void ApplyAddedComponents(GameObject instance, System.Collections.Generic.List<string> addedComponents)
        {
            if (instance == null || addedComponents == null)
                return;
            foreach (var typeName in addedComponents)
            {
                if (string.IsNullOrWhiteSpace(typeName))
                    continue;
                AddComponentByName(instance, typeName);
            }
        }

        private static System.Collections.Generic.List<Type> _allComponentTypes;

        public static System.Collections.Generic.List<Type> GetAllComponentTypes()
        {
            if (_allComponentTypes != null)
                return _allComponentTypes;

            _allComponentTypes = new System.Collections.Generic.List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    _allComponentTypes.AddRange(asm.GetTypes().Where(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract && !t.IsGenericTypeDefinition));
                }
                catch { }
            }
            _allComponentTypes = _allComponentTypes.OrderBy(t => t.FullName).ToList();
            return _allComponentTypes;
        }
    }
}
