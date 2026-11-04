using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    // Temporary M0 diagnostic. Log-only: creates short-lived Texture2D/Material objects, destroys them, and never touches scene objects.
    internal static class DecalFeasibilityProbe
    {
        private const string TinyPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        private static readonly string[] ShaderNames =
        {
            "Sprites/Default",
            "Unlit/Transparent",
            "Unlit/Texture",
            "Unlit/Color",
            "Standard",
            "Hidden/Internal-Colored",
            "UI/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Standard Unlit"
        };

        public static IEnumerator Run()
        {
            yield return new WaitForSeconds(10f);
            RunOnce("startup");

            while (Singleton<GameWorld>.Instance == null)
                yield return new WaitForSeconds(2f);

            yield return new WaitForSeconds(5f);
            RunOnce("raid");
        }

        private static void RunOnce(string stage)
        {
            try
            {
                var log = Plugin.Log;
                log.LogInfo($"[M0] ---- decal feasibility probe ({stage}) ----");
                log.LogInfo($"[M0] Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, shaderLevel {SystemInfo.graphicsShaderLevel}");
                ProbePngLoading();
                ProbeShaders();
                log.LogInfo($"[M0] ---- end probe ({stage}) ----");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[M0] probe failed: {ex}");
            }
        }

        private static void ProbePngLoading()
        {
            var log = Plugin.Log;
            var sb = new StringBuilder("[M0] loaded Unity assemblies: ");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name;
                if (n.StartsWith("UnityEngine.Image") || n.StartsWith("UnityEngine.UnityWebRequest"))
                    sb.Append(n).Append(' ');
            }
            log.LogInfo(sb.ToString());

            var imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", false);
            log.LogInfo($"[M0] ImageConversion type: {(imageConversion != null ? imageConversion.AssemblyQualifiedName : "NOT FOUND")}");
            var webTexture = Type.GetType("UnityEngine.Networking.UnityWebRequestTexture, UnityEngine.UnityWebRequestTextureModule", false);
            log.LogInfo($"[M0] UnityWebRequestTexture type: {(webTexture != null ? webTexture.AssemblyQualifiedName : "NOT FOUND")}");

            var loadImage = imageConversion?.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D), typeof(byte[]) }, null);
            if (loadImage == null)
            {
                log.LogWarning("[M0] ImageConversion.LoadImage(Texture2D, byte[]) not found");
                return;
            }

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var ok = (bool)loadImage.Invoke(null, new object[] { tex, Convert.FromBase64String(TinyPngBase64) });
                log.LogInfo($"[M0] LoadImage returned {ok}; texture {tex.width}x{tex.height} format {tex.format}, isReadable {tex.isReadable}");
                if (ok && tex.isReadable)
                    log.LogInfo($"[M0] pixel(0,0) = {tex.GetPixel(0, 0)}");
            }
            finally
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        private static void ProbeShaders()
        {
            var log = Plugin.Log;
            foreach (var name in ShaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    log.LogInfo($"[M0] shader '{name}': NOT FOUND");
                    continue;
                }

                Material mat = null;
                try
                {
                    mat = new Material(shader);
                    log.LogInfo($"[M0] shader '{name}': found, isSupported={shader.isSupported}, renderQueue={mat.renderQueue}, _MainTex={mat.HasProperty("_MainTex")}, _Color={mat.HasProperty("_Color")}, _Mode={mat.HasProperty("_Mode")}, _ZWrite={mat.HasProperty("_ZWrite")}, _SrcBlend={mat.HasProperty("_SrcBlend")}");
                }
                catch (Exception ex)
                {
                    log.LogWarning($"[M0] shader '{name}': material creation failed: {ex.Message}");
                }
                finally
                {
                    if (mat != null) UnityEngine.Object.Destroy(mat);
                }
            }
        }
    }
}
