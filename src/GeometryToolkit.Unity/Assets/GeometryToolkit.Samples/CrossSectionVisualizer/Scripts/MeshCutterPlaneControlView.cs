using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GeometryToolkit.Samples
{
    public sealed class MeshCutterPlaneControlView : MonoBehaviour
    {
        enum SliderType
        {
            PositionX,
            PositionY,
            PositionZ,
            RotationX,
            RotationY,
            RotationZ
        }

        [SerializeField] private UIDocument _uiDocument;

        private Button _resetButton;

        private Slider _positionXSlider;
        private Slider _positionYSlider;
        private Slider _positionZSlider;
        private Label _positionXValueLabel;
        private Label _positionYValueLabel;
        private Label _positionZValueLabel;

        private Slider _rotationXSlider;
        private Slider _rotationYSlider;
        private Slider _rotationZSlider;
        private Label _rotationXValueLabel;
        private Label _rotationYValueLabel;
        private Label _rotationZValueLabel;

        private Vector3 _defaultPosition;
        private Vector3 _defaultEulerAngles;

        public event Action<Vector3, Vector3> PlaneTransformChanged;

        private void Awake()
        {
            var root = _uiDocument.rootVisualElement;

            // Get references to UI elements
            _resetButton = root.Q<Button>("reset-button");

            _positionXSlider = root.Q<Slider>("position-x-slider");
            _positionYSlider = root.Q<Slider>("position-y-slider");
            _positionZSlider = root.Q<Slider>("position-z-slider");

            _positionXValueLabel = root.Q<Label>("position-x-value");
            _positionYValueLabel = root.Q<Label>("position-y-value");
            _positionZValueLabel = root.Q<Label>("position-z-value");

            _rotationXSlider = root.Q<Slider>("rotation-x-slider");
            _rotationYSlider = root.Q<Slider>("rotation-y-slider");
            _rotationZSlider = root.Q<Slider>("rotation-z-slider");

            _rotationXValueLabel = root.Q<Label>("rotation-x-value");
            _rotationYValueLabel = root.Q<Label>("rotation-y-value");
            _rotationZValueLabel = root.Q<Label>("rotation-z-value");

            // Register event handlers
            _resetButton.clicked += ResetToDefault;

            _positionXSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.PositionX, evt.newValue));
            _positionYSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.PositionY, evt.newValue));
            _positionZSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.PositionZ, evt.newValue));

            _rotationXSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.RotationX, evt.newValue));
            _rotationYSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.RotationY, evt.newValue));
            _rotationZSlider.RegisterValueChangedCallback(evt => OnValueChanged(SliderType.RotationZ, evt.newValue));

            _positionXSlider.SetValueWithoutNotify(_defaultPosition.x);
            _positionYSlider.SetValueWithoutNotify(_defaultPosition.y);
            _positionZSlider.SetValueWithoutNotify(_defaultPosition.z);

            _positionXValueLabel.text = $"{_defaultPosition.x:F2}";
            _positionYValueLabel.text = $"{_defaultPosition.y:F2}";
            _positionZValueLabel.text = $"{_defaultPosition.z:F2}";

            _rotationXSlider.SetValueWithoutNotify(_defaultEulerAngles.x);
            _rotationYSlider.SetValueWithoutNotify(_defaultEulerAngles.y);
            _rotationZSlider.SetValueWithoutNotify(_defaultEulerAngles.z);

            _rotationXValueLabel.text = $"{_defaultEulerAngles.x:F2}";
            _rotationYValueLabel.text = $"{_defaultEulerAngles.y:F2}";
            _rotationZValueLabel.text = $"{_defaultEulerAngles.z:F2}";
        }

        public void SetDefaultPlaneTransform(Vector3 position, Vector3 eulerAngles)
        {
            _defaultPosition = position;
            _defaultEulerAngles = eulerAngles;
            ResetToDefault();
        }

        private void ResetToDefault()
        {
            // Reset position sliders
            _positionXSlider.SetValueWithoutNotify(_defaultPosition.x);
            _positionYSlider.SetValueWithoutNotify(_defaultPosition.y);
            _positionZSlider.SetValueWithoutNotify(_defaultPosition.z);

            _positionXValueLabel.text = $"{_defaultPosition.x:F2}";
            _positionYValueLabel.text = $"{_defaultPosition.y:F2}";
            _positionZValueLabel.text = $"{_defaultPosition.z:F2}";

            // Reset rotation sliders
            _rotationXSlider.SetValueWithoutNotify(_defaultEulerAngles.x);
            _rotationYSlider.SetValueWithoutNotify(_defaultEulerAngles.y);
            _rotationZSlider.SetValueWithoutNotify(_defaultEulerAngles.z);

            _rotationXValueLabel.text = $"{_defaultEulerAngles.x:F2}";
            _rotationYValueLabel.text = $"{_defaultEulerAngles.y:F2}";
            _rotationZValueLabel.text = $"{_defaultEulerAngles.z:F2}";

            // Invoke reset event
            PlaneTransformChanged?.Invoke(_defaultPosition, _defaultEulerAngles);
        }

        private void OnValueChanged(SliderType sliderType, float newValue)
        {
            switch (sliderType)
            {
                case SliderType.PositionX:
                    _positionXSlider.value = newValue;
                    _positionXValueLabel.text = $"{newValue:F2}";
                    break;
                case SliderType.PositionY:
                    _positionYSlider.value = newValue;
                    _positionYValueLabel.text = $"{newValue:F2}";
                    break;
                case SliderType.PositionZ:
                    _positionZSlider.value = newValue;
                    _positionZValueLabel.text = $"{newValue:F2}";
                    break;
                case SliderType.RotationX:
                    _rotationXSlider.value = newValue;
                    _rotationXValueLabel.text = $"{newValue:F2}";
                    break;
                case SliderType.RotationY:
                    _rotationYSlider.value = newValue;
                    _rotationYValueLabel.text = $"{newValue:F2}";
                    break;
                case SliderType.RotationZ:
                    _rotationZSlider.value = newValue;
                    _rotationZValueLabel.text = $"{newValue:F2}";
                    break;
            }

            var planeCenterPosition = new Vector3(
                _positionXSlider.value,
                _positionYSlider.value,
                _positionZSlider.value
            );

            var planeEulerAngles = new Vector3(
                _rotationXSlider.value,
                _rotationYSlider.value,
                _rotationZSlider.value
            );

            PlaneTransformChanged?.Invoke(planeCenterPosition, planeEulerAngles);
        }
    }
}
