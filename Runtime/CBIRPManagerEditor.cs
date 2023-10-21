﻿#if !COMPILER_UDONSHARP && UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDK3.Editor;

namespace z3y
{
    [InitializeOnLoad, ExecuteInEditMode]
    public class CBIRPManagerEditor : MonoBehaviour, IActiveBuildTargetChanged
    {
        static CBIRPManagerEditor()
        {
            EditorApplication.update += StaticUpdate;
            VRCSdkControlPanel.OnSdkPanelEnable += AddBuildHook;
        }

        private static void AddBuildHook(object sender, EventArgs e)
        {
            if (VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder))
            {
                builder.OnSdkBuildStart += OnBuildStarted;
                builder.OnSdkBuildFinish += OnBuildFinish;
            }
        }
        const string constantsGeneratedHlslPath = "Packages/z3y.clusteredbirp/Shaders/ConstantsGenerated.hlsl";
        private static void OnBuildStarted(object sender, object target)
        {
            var scene = SceneManager.GetActiveScene();
            var rootGameObjects = scene.GetRootGameObjects();

            CBIRPManager manager = null;


            foreach (var gameObject in rootGameObjects)
            {
                if (manager == null)
                {
                    manager = gameObject.GetComponentInChildren<CBIRPManager>(true);
                }
                else
                {
                    break;
                }
            }

            var probesCount = manager.GetProbesCount();
            var lightsCount = manager.GetLightsCount();

            string code = $"#define CBIRP_MAX_LIGHTS {lightsCount + 1}" + Environment.NewLine + $"#define CBIRP_MAX_PROBES {probesCount + 1}";

            File.WriteAllText(constantsGeneratedHlslPath, code);
            AssetDatabase.ImportAsset(constantsGeneratedHlslPath);
        }
        private static void OnBuildFinish(object sender, object target)
        {
            string code = $"#define CBIRP_MAX_LIGHTS 128" + Environment.NewLine + "#define CBIRP_MAX_PROBES 64";

            File.WriteAllText(constantsGeneratedHlslPath, code);
            AssetDatabase.ImportAsset(constantsGeneratedHlslPath);
        }

        public CBIRPManager manager;
        public static CBIRPManagerEditor instance;
        private List<CBIRPReflectionProbe> _probes = new List<CBIRPReflectionProbe>();
        const int _probesSize = 64;
        private Vector4[] _probe0 = new Vector4[_probesSize];
        private Vector4[] _probe1 = new Vector4[_probesSize];
        private Vector4[] _probe2 = new Vector4[_probesSize];
        private Vector4[] _probe3 = new Vector4[_probesSize];

        public List<CBIRPLight> _lights = new List<CBIRPLight>();

        public int callbackOrder => 0;

        private void OnEnable()
        {
            instance = this;
        }

        void Start()
        {
            if (Application.isPlaying)
            {
                Destroy(this);
                return;
            }
            InitializeArrays();
            manager.OnValidate();
        }

        public static void StaticUpdate()
        {
            if (!instance)
            {
                return;
            }
            instance.LocalUpdate();
        }

        public void LocalUpdate()
        {
            manager.SetGlobalUniforms();

            Shader.SetGlobalVector("_Udon_CBIRP_PlayerCamera", SceneView.lastActiveSceneView.camera.transform.position);
            _probes = _probes.Where(x => x != null && x.transform && x.gameObject.activeInHierarchy).ToList();
            InitializeArrays();
            for (int i = 0; i < _probes.Count; i++)
            {
                CBIRPReflectionProbe probe = _probes[i];

                _probe0[i] = probe.GetData0();
                _probe1[i] = probe.GetData1();
                _probe2[i] = probe.GetData2();
                _probe3[i] = probe.GetData3();
            }
            _probes = SortProbesByImportance(_probes);

            manager._probe0 = _probe0;
            manager._probe1 = _probe1;
            manager._probe2 = _probe2;
            manager._probe3 = _probe3;
            manager.InitializePropertyIDs();

            if (manager._probe3.Length > 0)
            {
                manager._probeDecodeInstructions = manager._probe3[0];
            }

            manager.UpdateProbeGlobals();


            manager.InitializeLightsCount(_lights.Count);

            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null || !l.transform)
                {
                    _lights.RemoveAt(i);
                    return;
                }
                l.CopyShadowmaskID();
                CopyLightToManager(manager, l, i);

                manager.ComputeLightUniforms(i);
            }
            manager.CopyActiveLights();
            manager.UpdateLightGlobals();
            manager.UpdateCullFar(manager.cullFar);
            //manager.SetCrtUniforms();

        }

        public static List<CBIRPReflectionProbe> SortProbesByImportance(List<CBIRPReflectionProbe> probes)
        {
            return probes.OrderByDescending(x => x.probe.importance).ThenByDescending(x => x.gameObject.name).ToList();
        }

        public static void CopyLightToManager(CBIRPManager manager, CBIRPLight l, int i)
        {
            manager._lightTransforms[i] = l.transform;
            manager._lightDynamicAll[i] = l.dynamicTransform;
            manager._lightsRange[i] = l.range;
            manager._lightsColor[i] = l.color;
            manager._lightsIntensity[i] = l.intensity;
            manager._lightsShadowmask[i] = l._shadowMaskID;
            manager._specualOnly[i] = l.specularOnlyShadowmask;

            if (l.type == CBIRPLight.LightType.Spot)
            {
                manager._lightsType[i] = 1;
            }

            manager._lightsOuterAngle[i] = l.outerAngle;
            manager._lightsInnerAnglePercent[i] = l.innerAnglePercent;
        }

        public void AddProbe(CBIRPReflectionProbe probe)
        {
            if (_probes.Contains(probe)) return;

            //Debug.Log("Registed probe " + probe.gameObject.name);
            _probes.Add(probe);
            InitializeArrays();
        }
        private void InitializeArrays()
        {
            _probe0 = new Vector4[64];
            _probe1 = new Vector4[64];
            _probe2 = new Vector4[64];
            _probe3 = new Vector4[64];
        }
        public void RemoveProbe(CBIRPReflectionProbe probe)
        {
            //Debug.Log("Removed probe " + probe.gameObject.name);
            _probes.Remove(probe);
            InitializeArrays();
        }

        public void AddLight(CBIRPLight light)
        {
            _lights.Add(light);
        }
        public void RemoveLight(CBIRPLight light)
        {
            _lights.Remove(light);
        }

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
        {
            // clear out packed texture arrays to not destroy performance on android
            var probes = AssetDatabase.FindAssets("CBIRP_ProbeArray_");
            foreach (var probe in probes)
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(probe));
            }
        }
    }

    public class DestroyCBIRPProbes : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var rootGameObjects = scene.GetRootGameObjects();

            var instancesProbes = new List<CBIRPReflectionProbe>();
            var instancesLights = new List<CBIRPLight>();


            CBIRPManagerEditor managere = null;
            CBIRPManager manager = null;


            foreach (var gameObject in rootGameObjects)
            {
                var instance = gameObject.GetComponentsInChildren<CBIRPReflectionProbe>(true);

                if (managere == null)
                {
                    managere = gameObject.GetComponentInChildren<CBIRPManagerEditor>(true);
                }

                if (instance is null || instance.Length == 0)
                {
                    continue;
                }
                instancesProbes.AddRange(instance);
            }

            foreach (var gameObject in rootGameObjects)
            {
                var instance = gameObject.GetComponentsInChildren<CBIRPLight>(true);

                if (manager == null)
                {
                    manager = gameObject.GetComponentInChildren<CBIRPManager>(true);
                }

                if (instance is null || instance.Length == 0)
                {
                    continue;
                }
                instancesLights.AddRange(instance);
            }


            for (int i = 0; i < instancesProbes.Count; i++)
            {
                instancesProbes[i].InitailizeData();
            }

            var activeProbes = instancesProbes.Where(x => x.gameObject.activeInHierarchy).ToList();
            activeProbes = CBIRPManagerEditor.SortProbesByImportance(activeProbes);
            manager._probe0 = new Vector4[activeProbes.Count];
            manager._probe1 = new Vector4[activeProbes.Count];
            manager._probe2 = new Vector4[activeProbes.Count];
            manager._probe3 = new Vector4[activeProbes.Count];

            for (int i = 0; i < activeProbes.Count; i++)
            {
                CBIRPReflectionProbe probe = activeProbes[i];
                manager._probe0[i] = probe.GetData0();
                manager._probe1[i] = probe.GetData1();
                manager._probe2[i] = probe.GetData2();
                manager._probe3[i] = probe.GetData3();
            }

            manager.InitializeLightsCount(instancesLights.Count);

            for (int i = 0; i < instancesLights.Count; i++)
            {
                var l = instancesLights[i];
                if (l == null || !l.transform)
                {
                    instancesLights.RemoveAt(i);
                    return;
                }
                l.InitializeLight();
                l.CopyShadowmaskID();
                CBIRPManagerEditor.CopyLightToManager(manager, l, i);

                manager.ComputeLightUniforms(i);
            }

            if (manager._probe3.Length > 0)
            {
                manager._probeDecodeInstructions = manager._probe3[0];
            }


            foreach (var instance in instancesProbes)
            {
                GameObject.DestroyImmediate(instance.gameObject);
            }
        }
    }
}
#endif