using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>Shared construction for the procedural scenery: static, unlit-by-probes, shadowless mesh objects.</summary>
    static class SceneObjects
    {
        public static MeshRenderer Spawn(Transform parent, string objectName, Mesh mesh, params Material[] materials)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            return r;
        }
    }
}
