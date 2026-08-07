using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Mirrors a camera about a horizontal plane and renders the result into a RenderTexture,
/// which the river shader samples in screen space.
///
/// A cubemap reflection probe cannot do this job: it gives the water an average of the
/// surroundings, so you get a sheen but no recognisable bamboo. A planar reflection gives the
/// real thing, and it stays correct while the camera pans, which a baked plate would not.
///
/// Cost is controlled three ways: the render target is a fraction of screen size, the culling
/// mask restricts the pass to whatever actually needs to appear in the water, and shadows are
/// off in the reflected pass.
/// </summary>
[ExecuteAlways]
public class PlanarReflection : MonoBehaviour
{
    [Tooltip("Water height in world space. The mirror plane.")]
    public float waterLevel = 0f;

    [Tooltip("Reflection target size as a fraction of the screen. 0.5 is usually plenty.")]
    [Range(0.15f, 1f)] public float resolutionScale = 0.5f;

    [Tooltip("Only these layers are drawn into the reflection.")]
    public LayerMask reflectLayers = ~0;

    [Tooltip("Material using BambooArena/RiverWater.")]
    public Material waterMaterial;

    [Tooltip("Skip anything further than this from the camera when reflecting.")]
    public float reflectFarClip = 90f;

    private Camera _reflectionCam;
    private RenderTexture _rt;
    private static bool _rendering;   // guards against the reflection camera reflecting itself

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Cleanup();
    }

    private void Cleanup()
    {
        if (_reflectionCam != null)
        {
            if (Application.isPlaying) Destroy(_reflectionCam.gameObject);
            else DestroyImmediate(_reflectionCam.gameObject);
            _reflectionCam = null;
        }
        if (_rt != null)
        {
            _rt.Release();
            if (Application.isPlaying) Destroy(_rt); else DestroyImmediate(_rt);
            _rt = null;
        }
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (_rendering) return;
        if (waterMaterial == null) return;
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        // Scene view is allowed through: without it the water reads black while editing.

        EnsureTargets(cam);

        // Reflect the view matrix about the water plane rather than mirroring the transform by
        // hand: this keeps handedness consistent with the oblique projection below.
        Vector4 plane = new Vector4(0f, 1f, 0f, -waterLevel);
        Matrix4x4 reflection = ReflectionMatrix(plane);
        _reflectionCam.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

        // Without an oblique near plane the mirrored camera sits below the water, inside the
        // bank, and fills the frame with the underside of the terrain instead of the grove.
        Vector4 clipPlane = CameraSpacePlane(_reflectionCam.worldToCameraMatrix,
                                             new Vector3(0f, waterLevel, 0f), Vector3.up, 1f);
        _reflectionCam.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

        _reflectionCam.fieldOfView = cam.fieldOfView;
        _reflectionCam.aspect = cam.aspect;
        _reflectionCam.farClipPlane = reflectFarClip;
        _reflectionCam.cullingMask = reflectLayers;
        // position only matters for sorting and LOD, the matrices above do the real work
        _reflectionCam.transform.position = reflection.MultiplyPoint(cam.transform.position);

        // Mirroring flips winding order, so front faces would be culled without this.
        GL.invertCulling = true;
        _rendering = true;
        _reflectionCam.Render();
        _rendering = false;
        GL.invertCulling = false;

        waterMaterial.SetTexture("_ReflectionTex", _rt);
    }

    /// <summary>Householder reflection about a world plane (nx, ny, nz, d).</summary>
    private static Matrix4x4 ReflectionMatrix(Vector4 p)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * p.x * p.x; m.m01 = -2f * p.x * p.y; m.m02 = -2f * p.x * p.z; m.m03 = -2f * p.w * p.x;
        m.m10 = -2f * p.y * p.x; m.m11 = 1f - 2f * p.y * p.y; m.m12 = -2f * p.y * p.z; m.m13 = -2f * p.w * p.y;
        m.m20 = -2f * p.z * p.x; m.m21 = -2f * p.z * p.y; m.m22 = 1f - 2f * p.z * p.z; m.m23 = -2f * p.w * p.z;
        return m;
    }

    /// <summary>World plane expressed in the reflected camera's space, for the oblique matrix.</summary>
    private static Vector4 CameraSpacePlane(Matrix4x4 worldToCamera, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offset = pos + normal * 0.015f;          // tiny bias stops z-fighting at the shoreline
        Vector3 cpos = worldToCamera.MultiplyPoint(offset);
        Vector3 cnrm = worldToCamera.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnrm.x, cnrm.y, cnrm.z, -Vector3.Dot(cpos, cnrm));
    }

    private void EnsureTargets(Camera cam)
    {
        int w = Mathf.Max(64, Mathf.RoundToInt(cam.pixelWidth * resolutionScale));
        int h = Mathf.Max(64, Mathf.RoundToInt(cam.pixelHeight * resolutionScale));

        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR)
            {
                name = "RiverReflection",
                antiAliasing = 1,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _rt.Create();
        }

        if (_reflectionCam == null)
        {
            var go = new GameObject("ReflectionCamera(auto)") { hideFlags = HideFlags.HideAndDontSave };
            _reflectionCam = go.AddComponent<Camera>();
            _reflectionCam.enabled = false;                 // driven manually, never on its own
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;                     // shadows in a mirror are not worth the cost
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.renderPostProcessing = false;
        }

        _reflectionCam.targetTexture = _rt;
        // Follow the source camera's background. With a solid-colour sky this keeps the
        // reflection from clearing to something the main view never shows.
        _reflectionCam.clearFlags = cam.clearFlags == CameraClearFlags.Skybox
            ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        _reflectionCam.backgroundColor = cam.backgroundColor;
        _reflectionCam.allowHDR = cam.allowHDR;
        _reflectionCam.allowMSAA = false;
    }
}
