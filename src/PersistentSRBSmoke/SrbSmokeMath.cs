using UnityEngine;

namespace PersistentSRBSmoke
{
    internal static class SrbSmokeMath
    {
        public static Vector3 ResolveExhaustDirection(ModuleEngines engine, Transform exhaust, Vessel vessel)
        {
            Vector3 fallback = Vector3.down;
            if (vessel != null)
                fallback = -(Vector3)vessel.upAxis;
            if (fallback.sqrMagnitude < 0.001f)
                fallback = Vector3.down;
            fallback.Normalize();

            if (exhaust == null)
                return fallback;

            Vector3 forward = exhaust.forward;
            if (forward.sqrMagnitude < 0.001f)
                return fallback;
            forward.Normalize();

            if (engine == null || engine.part == null)
                return -forward;

            Vector3 outwardHint = Vector3.zero;
            if (engine.thrustTransforms != null && engine.thrustTransforms.Count > 0)
            {
                Vector3 clusterCenter = Vector3.zero;
                int valid = 0;
                for (int i = 0; i < engine.thrustTransforms.Count; i++)
                {
                    Transform transform = engine.thrustTransforms[i];
                    if (transform == null)
                        continue;
                    clusterCenter += transform.position;
                    valid++;
                }

                if (valid > 0)
                    outwardHint = clusterCenter / valid - engine.part.transform.position;
            }

            if (outwardHint.sqrMagnitude < 0.01f)
                outwardHint = exhaust.position - engine.part.transform.position;

            if (outwardHint.sqrMagnitude >= 0.01f)
            {
                outwardHint.Normalize();
                float alignment = Vector3.Dot(forward, outwardHint);
                if (Mathf.Abs(alignment) >= 0.20f)
                    return alignment >= 0f ? forward : -forward;
            }

            return -forward;
        }

        public static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352DU;
            value ^= value >> 15;
            value *= 0x846CA68BU;
            value ^= value >> 16;
            return value;
        }

        public static float Hash01(uint value)
        {
            return (Hash(value) & 0x00FFFFFFU) / 16777215f;
        }
    }
}
