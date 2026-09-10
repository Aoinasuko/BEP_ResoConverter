using System;
using System.Reflection;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    // Package types are discovered at runtime so this assembly also loads in a plain Unity project.
    internal static class OptionalComponent
    {
        internal static Type FindType(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        internal static object Get(object instance, string name)
        {
            if (instance == null || instance is UnityEngine.Object unityObject && unityObject == null) return null;
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                var field = type.GetField(name, flags);
                if (field != null) return field.GetValue(instance);
                var property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(instance);
            }
            return null;
        }

        internal static T Get<T>(object instance, string name, T fallback = default)
        {
            var value = Get(instance, name);
            if (value is T typed) return typed;
            try { return value == null ? fallback : (T)Convert.ChangeType(value, typeof(T)); }
            catch (Exception) { return fallback; }
        }

        internal static Component On(GameObject root, string name)
        {
            var type = FindType(name);
            return type == null ? null : root.GetComponent(type);
        }
    }
}
