using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GeometryToolkit.Samples
{
    public sealed class CrossSectionListView : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private VisualElement _scrollViewContents;

        public event Action<int, bool> VisibilityChanged;

        private void Awake()
        {
            var crossSectionScrollView = _uiDocument.rootVisualElement.Q<ScrollView>("cross-section-list");
            _scrollViewContents = crossSectionScrollView.contentContainer;
            _scrollViewContents.Clear();
        }

        public void SetCrossSections(List<CrossSection> crossSections)
        {
            _scrollViewContents.Clear();
            for (var index = 0; index < crossSections.Count; index++)
            {
                var itemElement = CreateItemElement(crossSections[index], index);
                _scrollViewContents.Add(itemElement);
            }
        }

        private VisualElement CreateItemElement(CrossSection crossSection, int itemIndex)
        {
            var item = new VisualElement();
            item.AddToClassList("cross-section-item");

            var header = new VisualElement();
            header.AddToClassList("cross-section-item-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            var visibilityToggle = new Toggle();
            visibilityToggle.value = true;

            var title = new Label();
            title.AddToClassList("cross-section-item-title");
            title.text = $"Cross Section {itemIndex + 1}";

            var info = new Label();
            info.AddToClassList("cross-section-item-info");
            info.text = $"Perimeter: {crossSection.Perimeter * 100f:F2} [cm]\nArea: {crossSection.SignedArea:F4} [m²]";

            header.Add(visibilityToggle);
            header.Add(title);
            item.Add(header);
            item.Add(info);

            visibilityToggle.RegisterValueChangedCallback(evt =>
            {
                var isVisible = evt.newValue;
                VisibilityChanged?.Invoke(itemIndex, isVisible);
            });

            return item;
        }
    }
}
