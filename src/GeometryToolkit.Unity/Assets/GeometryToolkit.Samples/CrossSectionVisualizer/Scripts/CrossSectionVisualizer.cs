using System.Collections.Generic;
using UnityEngine;

namespace GeometryToolkit.Samples
{
    public sealed class CrossSectionVisualizer : MonoBehaviour
    {
        [SerializeField] private CrossSectionRenderer _rendererPrefab;
        [SerializeField] private Color _positiveAreaColor = new Color(1.0f, 0.5f, 0.0f, 0.5f); // Orange
        [SerializeField] private Color _negativeAreaColor = new Color(0.0f, 0.7f, 1.0f, 0.5f); // Light blue
        
        private readonly List<CrossSectionRenderer> _crossSectionRenderers = new();
        
        private void OnDestroy()
        {
            foreach (var renderer in _crossSectionRenderers)
            {
                Destroy(renderer.gameObject);
            }
            _crossSectionRenderers.Clear();
        }
        
        public void SetCrossSections(List<CrossSection> crossSections)
        {
            foreach (var renderer in _crossSectionRenderers)
            {
                Destroy(renderer.gameObject);
            }
            _crossSectionRenderers.Clear();

            // Create new renderers for each cross-section.
            for (var i = 0; i < crossSections.Count; i++)
            {
                var crossSectionRenderer = GameObject.Instantiate(_rendererPrefab, transform, false);
                crossSectionRenderer.name = $"CrossSection_{i}";

                var color = crossSections[i].SignedArea >= 0 ? _positiveAreaColor : _negativeAreaColor;
                crossSectionRenderer.SetColor(color);

                crossSectionRenderer.SetText($"Perimeter{System.Environment.NewLine}{crossSections[i].Perimeter * 100f:0.00} [cm]");

                crossSectionRenderer.UpdateMesh(crossSections[i]);
                _crossSectionRenderers.Add(crossSectionRenderer);
            }
        }

        public void Show(int index)
        {
            if (index < 0 || index >= _crossSectionRenderers.Count) return;
            _crossSectionRenderers[index].gameObject.SetActive(true);
        }

        public void Hide(int index)
        {
            if (index < 0 || index >= _crossSectionRenderers.Count) return;
            _crossSectionRenderers[index].gameObject.SetActive(false);
        }
    }
}
