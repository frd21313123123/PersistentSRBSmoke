using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Suppresses legacy/stock smoke particle emitters attached to SolidFuel engine parts while
    /// leaving flame/Waterfall visuals alone. The lookup is intentionally conservative: only
    /// particle components whose hierarchy/material/texture contains a smoke-like name are touched.
    /// </summary>
    internal sealed class StockSmokeSuppressor
    {
        private readonly Dictionary<int, List<Component>> _cachedTargets =
            new Dictionary<int, List<Component>>();

        public void RefreshPart(Part part)
        {
            if (part == null || part.gameObject == null)
                return;

            int key = part.GetInstanceID();
            List<Component> cached;
            if (_cachedTargets.TryGetValue(key, out cached) && cached != null && cached.Count > 0)
            {
                // Suppression also removes destroyed entries. If at least one target survives there
                // is no need to run GetComponentsInChildren and smoke-name inspection again.
                SuppressTargets(cached, true);
                if (cached.Count > 0)
                    return;
            }

            var targets = new List<Component>();
            Component[] components = part.gameObject.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !IsParticleEmitter(component) || !LooksLikeSmoke(component, part.transform))
                    continue;

                targets.Add(component);
            }

            _cachedTargets[key] = targets;
            SuppressTargets(targets, true);
        }

        public void SuppressCached(IEnumerable<Part> parts)
        {
            if (parts == null)
                return;

            foreach (Part part in parts)
            {
                if (part == null)
                    continue;

                List<Component> targets;
                if (_cachedTargets.TryGetValue(part.GetInstanceID(), out targets))
                {
                    // LateUpdate runs every frame. Avoid repeated reflection here; the deeper legacy
                    // property reset is performed by RefreshPart at the configured refresh interval.
                    SuppressTargets(targets, false);
                }
            }
        }

        public void ForgetMissing(ICollection<Part> liveParts)
        {
            if (_cachedTargets.Count == 0)
                return;

            var liveIds = new HashSet<int>();
            if (liveParts != null)
            {
                foreach (Part part in liveParts)
                {
                    if (part != null)
                        liveIds.Add(part.GetInstanceID());
                }
            }

            var remove = new List<int>();
            foreach (int id in _cachedTargets.Keys)
            {
                if (!liveIds.Contains(id))
                    remove.Add(id);
            }

            for (int i = 0; i < remove.Count; i++)
                _cachedTargets.Remove(remove[i]);
        }

        public void Clear()
        {
            _cachedTargets.Clear();
        }

        private static bool IsParticleEmitter(Component component)
        {
            if (component is ParticleSystem)
                return true;

            string typeName = component.GetType().Name;
            return typeName.IndexOf("ParticleEmitter", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("KSPParticle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeSmoke(Component component, Transform partRoot)
        {
            var signature = new StringBuilder(192);
            signature.Append(component.GetType().Name).Append(' ');

            Transform current = component.transform;
            int depth = 0;
            while (current != null && depth < 6)
            {
                signature.Append(current.name).Append(' ');
                if (current == partRoot)
                    break;
                current = current.parent;
                depth++;
            }

            try
            {
                Renderer renderer = component.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    Material material = renderer.sharedMaterial;
                    signature.Append(material.name).Append(' ');
                    if (material.mainTexture != null)
                        signature.Append(material.mainTexture.name).Append(' ');
                }
            }
            catch
            {
                // Some third-party particle wrappers expose unusual renderer state. Name-based
                // matching above is still enough for stock fx_smokeTrail_* objects.
            }

            string value = signature.ToString();
            return value.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("smoketrail", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("smoke trail", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SuppressTargets(List<Component> targets, bool deepLegacyReset)
        {
            if (targets == null)
                return;

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                Component component = targets[i];
                if (component == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                ParticleSystem system = component as ParticleSystem;
                if (system != null)
                {
                    try
                    {
                        var emission = system.emission;
                        emission.enabled = false;
                        if (system.isPlaying || system.particleCount > 0)
                            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    catch
                    {
                    }
                    continue;
                }

                // KSPParticleEmitter and several SmokeScreen-era wrappers are legacy components.
                // Reflection keeps the mod compatible without taking a hard dependency on them,
                // but only perform these relatively expensive lookups on the periodic deep refresh.
                if (deepLegacyReset)
                {
                    TrySetMember(component, "emit", false);
                    TrySetMember(component, "emissionRate", 0f);
                    TrySetMember(component, "minEmission", 0f);
                    TrySetMember(component, "maxEmission", 0f);
                }

                Behaviour behaviour = component as Behaviour;
                if (behaviour != null)
                    behaviour.enabled = false;
            }
        }

        private static void TrySetMember(Component component, string name, object value)
        {
            try
            {
                Type type = component.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.CanWrite)
                {
                    object converted = ConvertValue(value, property.PropertyType);
                    property.SetValue(component, converted, null);
                    return;
                }

                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    object converted = ConvertValue(value, field.FieldType);
                    field.SetValue(component, converted);
                }
            }
            catch
            {
            }
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);
            if (targetType == typeof(float))
                return Convert.ToSingle(value);
            if (targetType == typeof(double))
                return Convert.ToDouble(value);
            if (targetType == typeof(int))
                return Convert.ToInt32(value);
            return value;
        }
    }
}
