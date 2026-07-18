using UnityEngine;

namespace TOHYK
{
    public static class PivotGeometryUtils
    {
        public static Vector3 GetBoundsCenterOffsetLocal(Transform root)
        {
            if (root == null)
                return Vector3.zero;

            Bounds? combined = null;

            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(false);
            foreach (var mr in meshRenderers)
            {
                if (mr == null || !mr.enabled)
                    continue;
                Encapsulate(ref combined, mr.bounds);
            }

            var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(false);
            foreach (var sr in skinnedRenderers)
            {
                if (sr == null || !sr.enabled)
                    continue;
                Encapsulate(ref combined, sr.bounds);
            }

            if (combined == null)
                return Vector3.zero;

            return root.InverseTransformPoint(combined.Value.center);
        }

        private static void Encapsulate(ref Bounds? combined, Bounds b)
        {
            if (combined == null)
                combined = b;
            else
            {
                var value = combined.Value;
                value.Encapsulate(b);
                combined = value;
            }
        }
    }
}