using EFT.CameraControl;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class MapEditorFreecamRenderController : MonoBehaviour
    {
        private Camera _camera;
        private CameraLodBiasController _lodBiasController;
        private float _originalFarClipPlane;
        private float[] _originalLayerCullDistances;
        private bool _originalUseOcclusionCulling;
        private float _originalLodBias;
        private float _originalLodBiasFactor;
        private bool _applied;

        public void OnEnable()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
                return;

            _originalFarClipPlane = _camera.farClipPlane;
            _originalLayerCullDistances = (float[])_camera.layerCullDistances.Clone();
            _originalUseOcclusionCulling = _camera.useOcclusionCulling;

            var layerCull = new float[32];
            for (int i = 0; i < 32; i++)
                layerCull[i] = 10000f;
            _camera.layerCullDistances = layerCull;
            _camera.farClipPlane = 10000f;
            _camera.useOcclusionCulling = false;

            _originalLodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = 10f;

            _lodBiasController = GetComponent<CameraLodBiasController>();
            if (_lodBiasController != null)
            {
                _originalLodBiasFactor = _lodBiasController.LodBiasFactor;
                _lodBiasController.LodBiasFactor = 10f;
            }

            _applied = true;
        }

        public void OnDisable()
        {
            if (!_applied)
                return;

            if (_camera != null)
            {
                _camera.farClipPlane = _originalFarClipPlane;
                if (_originalLayerCullDistances != null)
                    _camera.layerCullDistances = _originalLayerCullDistances;
                _camera.useOcclusionCulling = _originalUseOcclusionCulling;
            }

            QualitySettings.lodBias = _originalLodBias;

            if (_lodBiasController != null)
                _lodBiasController.LodBiasFactor = _originalLodBiasFactor;

            _applied = false;
        }
    }
}
