using UnityEngine;

namespace MapLootEditorLite.Client
{
    internal sealed class WorldSurfaceHit
    {
        public GameObject HitGameObject { get; set; }
        public GameObject SelectedGameObject { get; set; }
        public Collider HitCollider { get; set; }
        public Transform HitTransform { get; set; }
        public Vector3 Point { get; set; }
        public Vector3 Normal { get; set; }
        public int TriangleIndex { get; set; }
        public Renderer Renderer { get; set; }
        public Mesh Mesh { get; set; }
        public Material[] Materials { get; set; }
    }
}
