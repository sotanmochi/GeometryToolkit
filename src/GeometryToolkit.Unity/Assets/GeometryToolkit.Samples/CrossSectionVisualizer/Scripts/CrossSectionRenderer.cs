using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.Samples
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class CrossSectionRenderer : MonoBehaviour
    {
        [SerializeField] private Material _meshMaterial;
        [SerializeField] private Color _color = new Color(1.0f, 0.5f, 0.0f, 0.5f);
        [SerializeField] private TMPro.TMP_Text _textMesh;
        
        private Mesh _mesh;
        private Material _materialInstance;

        private void Awake()
        {
            _mesh = new();
            _mesh.name = "CrossSection";

            if (TryGetComponent(out MeshFilter meshFilter))
            {
                meshFilter.mesh = _mesh;
            }

            if (TryGetComponent(out MeshRenderer meshRenderer))
            {
                if (_meshMaterial != null)
                {
                    _materialInstance = new Material(_meshMaterial);
                    _materialInstance.SetColor("_Color", _color);
                    meshRenderer.material = _materialInstance;
                }
            }
        }

        private void OnDestroy()
        {
            Destroy(_mesh);
            if (_materialInstance != null)
            {
                Destroy(_materialInstance);
            }
        }

        private void OnValidate()
        {
            if (_materialInstance != null && Application.isEditor)
            {
                _materialInstance.SetColor("_Color", _color);
            }
        }

        public void SetColor(Color color)
        {
            _color = color;
            if (_materialInstance != null)
            {
                _materialInstance.SetColor("_Color", _color);
            }
        }

        public void SetText(string text)
        {
            if (_textMesh != null)
            {
                _textMesh.text = text;
            }
        }

        public void UpdateMesh(CrossSection crossSection)
        {
            this.transform.position = crossSection.Centroid;
            _mesh.Clear();
            _mesh.SetVertices(crossSection.Vertices);
            _mesh.SetIndices(crossSection.Triangles, MeshTopology.Triangles, 0);
        }
    }
}
