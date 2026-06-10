using System.Collections.Generic;
using BepInEx.Configuration;
using Studio;
using UnityEngine;

namespace TOHYK
{
    public class MeshRaycaster
    {
        private MeshFilter[] _cachedMeshFilters;
        private SkinnedMeshRenderer[] _cachedSkinnedRenderers;
        private Collider[] _targetColliders;
        private readonly Mesh _bakedMesh = new Mesh();
        List<Collider> disabledColliders = new List<Collider>();
        private readonly HashSet<Transform> _excluded = new HashSet<Transform>();
        private readonly List<Transform> _tempTransforms = new List<Transform>(256);
        private readonly List<Collider> _tempColliders = new List<Collider>(128);
        private bool _hasCache;
        private readonly Dictionary<Mesh, MeshData> _meshDataCache = new Dictionary<Mesh, MeshData>();

        private class MeshData
        {
            public Vector3[] Vertices;
            public int[] Triangles;

            public MeshData(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Triangles = mesh.triangles;
            }
        }

        private MeshData GetCachedMeshData(Mesh mesh)
        {
            if (_meshDataCache.TryGetValue(mesh, out var data))
                return data;

            data = new MeshData(mesh);
            _meshDataCache[mesh] = data;
            return data;
        }

        public bool Raycast(
            Ray ray,
            float maxDist,
            IDictionary<int, GuideObject> targets,
            out Vector3 hitPoint,
            out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;
            float closest = maxDist;
            bool found = false;

            if (_cachedMeshFilters != null)
            {
                foreach (var filter in _cachedMeshFilters)
                {
                    if (filter == null)
                        continue;

                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled || !renderer.isVisible)
                        continue;

                    if (_excluded.Contains(filter.transform))
                        continue;

                    if (!renderer.bounds.IntersectRay(ray, out float boundsD) || boundsD > closest)
                        continue;

                    var mesh = filter.sharedMesh;
                    if (mesh == null)
                        continue;

                    if (RaycastMesh(ray, mesh, filter.transform, closest, out float d, out Vector3 n))
                    {
                        closest = d;
                        hitPoint = ray.GetPoint(d);
                        hitNormal = n;
                        found = true;
                    }
                }
            }

            if (_cachedSkinnedRenderers != null)
            {
                foreach (var smr in _cachedSkinnedRenderers)
                {
                    if (smr == null || !smr.enabled)
                        continue;
                    if (_excluded.Contains(smr.transform))
                        continue;

                    if (!smr.bounds.IntersectRay(ray, out float boundsD) || boundsD > closest)
                        continue;

                    _bakedMesh.Clear();
                    smr.BakeMesh(_bakedMesh);

                    if (RaycastMesh(ray, _bakedMesh, smr.transform, closest, out float d, out Vector3 n))
                    {
                        TOHYK.Log.LogInfo($"[SurfaceSnap] SkinnedMesh hit: {smr.transform.name} at distance {d}");
                        closest = d;
                        hitPoint = ray.GetPoint(d);
                        hitNormal = n;
                        found = true;
                    }
                }
            }
            
            disabledColliders.Clear();
            if (_targetColliders != null)
            {
                foreach (var col in _targetColliders)
                {
                    if (col == null || !col.enabled) continue;
                    col.enabled = false;
                    disabledColliders.Add(col);
                }
            }

            if (Physics.Raycast(ray, out RaycastHit physHit, closest))
            {
                TOHYK.Log.LogInfo(
                    $"[SurfaceSnap] Physics hit: {physHit.collider.transform.name} at distance {physHit.distance}");
                closest = physHit.distance;
                hitPoint = physHit.point;
                hitNormal = physHit.normal;
                found = true;
            }

            foreach (var col in disabledColliders)
                col.enabled = true;

            return found;
        }

        public void BuildCache(IDictionary<int, GuideObject> targets)
        {
            _cachedMeshFilters = Object.FindObjectsOfType<MeshFilter>();
            _cachedSkinnedRenderers = Object.FindObjectsOfType<SkinnedMeshRenderer>();
            BuildTargetCollidersCache(targets);
            _hasCache = true;
        }

        private void BuildTargetCollidersCache(IDictionary<int, GuideObject> targets)
        {
            _tempColliders.Clear();
            foreach (var go in targets.Values)
            {
                go.transformTarget.GetComponentsInChildren(true, _tempColliders);
            }
            _targetColliders = _tempColliders.ToArray();
        }

        public void Clear()
        {
            if (!_hasCache)
                return;

            _cachedMeshFilters = null;
            _cachedSkinnedRenderers = null;
            _targetColliders = null;
            _excluded.Clear();
            _meshDataCache.Clear();
            _hasCache = false;
        }

        public void BuildExcludeSet(IDictionary<int, GuideObject> targets)
        {
            _excluded.Clear();

            foreach (var go in targets.Values)
            {
                go.transformTarget.GetComponentsInChildren(true, _tempTransforms);
                foreach (var t in _tempTransforms)
                    _excluded.Add(t);
            }

            foreach (var selectedCharacter in KKAPI.Studio.StudioAPI.GetSelectedCharacters())
            {
                selectedCharacter.charInfo.transform.GetComponentsInChildren(true, _tempTransforms);
                foreach(var t in _tempTransforms)
                    _excluded.Add(t);
            }
        }

        private bool RaycastMesh(Ray ray, Mesh mesh, Transform transform, float maxDist, out float hitDist,
            out Vector3 hitNormal)
        {
            hitDist = maxDist;
            hitNormal = Vector3.up;
            bool hit = false;

            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            Vector3 localOrigin = worldToLocal.MultiplyPoint3x4(ray.origin);
            Vector3 localDir = worldToLocal.MultiplyVector(ray.direction).normalized;
            Ray localRay = new Ray(localOrigin, localDir);

            var data = GetCachedMeshData(mesh);
            var verts = data.Vertices;
            var tris = data.Triangles;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v0 = verts[tris[i]];
                Vector3 v1 = verts[tris[i + 1]];
                Vector3 v2 = verts[tris[i + 2]];

                if (RayTriangle(localRay, v0, v1, v2, out float t) && t > 0f)
                {
                    Vector3 localHit = localRay.GetPoint(t);
                    Vector3 worldHit = transform.TransformPoint(localHit);
                    float worldDist = Vector3.Distance(ray.origin, worldHit);

                    if (worldDist < hitDist)
                    {
                        hitDist = worldDist;
                        Vector3 localNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                        hitNormal = transform.TransformDirection(localNormal).normalized;
                        hit = true;
                    }
                }
            }

            return hit;
        }

        private static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, e2);
            float a = Vector3.Dot(e1, h);

            if (a > -1e-6f && a < 1e-6f)
                return false;

            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(ray.direction, q);
            if (v < 0f || u + v > 1f)
                return false;

            t = f * Vector3.Dot(e2, q);
            return t > 1e-6f;
        }
    }
}