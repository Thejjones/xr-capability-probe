using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace XrProbe
{
    // Applies the project settings for the probe's Android build. Run once after the project is created:
    //   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod XrProbe.ProbeProjectSetup.Apply
    // Safe to run again: every step overwrites the same settings.
    public static class ProbeProjectSetup
    {
        const string SettingsDir = "Assets/XrProbeSettings";

        [MenuItem("XR Probe/Apply Project Settings")]
        public static void Apply()
        {
            PlayerSettings.companyName = ProbeIdentity.CompanyName;
            PlayerSettings.productName = ProbeIdentity.ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ProbeIdentity.ApplicationId);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.forceInternetPermission = false;
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.High);
            PlayerSettings.stripEngineCode = true;
            // The app reads the legacy Input class (a click or touch, a key): the legacy input handler only.
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (ps != null && ps.Length > 0)
            {
                var so = new SerializedObject(ps[0]);
                var p = so.FindProperty("activeInputHandler");
                if (p != null) { p.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            var urp = CreateUrp();
            GraphicsSettings.defaultRenderPipeline = urp;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }

            Directory.CreateDirectory("Assets/XR");
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perBuild);
            if (perBuild == null)
            {
                perBuild = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuild, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perBuild, true);
            }
            if (!perBuild.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) perBuild.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = perBuild.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            EditorUtility.SetDirty(general);
            var manager = perBuild.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            openxr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openxr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;
            EditorUtility.SetDirty(openxr);
            AssetDatabase.SaveAssets();
            Debug.Log("ProbeProjectSetup: applied");
        }

        static UniversalRenderPipelineAsset CreateUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            string rendererPath = SettingsDir + "/ProbeRenderer.asset", urpPath = SettingsDir + "/ProbeURP.asset";
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
            }
            rendererData.renderingMode = RenderingMode.Forward;
            rendererData.postProcessData = null;
            rendererData.depthPrimingMode = DepthPrimingMode.Disabled;
            EditorUtility.SetDirty(rendererData);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, urpPath);
            }
            urp.supportsHDR = false;
            urp.msaaSampleCount = 2;
            urp.useSRPBatcher = true;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.shadowDistance = 10f;
            EditorUtility.SetDirty(urp);
            return urp;
        }
    }
}
