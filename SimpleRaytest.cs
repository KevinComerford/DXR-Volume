using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.Experimental.Rendering;

public class SimpleRaytest : MonoBehaviour
{ 

    public RenderTexture renderTexture;
    public RayTracingShader rtshader;
    public Camera cam;

    public Vector3Int Res = new Vector3Int(512,512,512);

    public float scaler = 1.0f;
    public Vector3 WPosition;

    public Renderer DebugRenderer;
    public Light[] Lights;
    public uint AreaLightSamples = 16;

    List<Light> PointLights,ConeLights,DirectionalLights,AreaLights;

    RayTracingAccelerationStructure accelerationStructure;
    Vector3Int threads;




    void Start()
    {
        initializeSystem();
    //    UnityEditor.Rendering.EditorGraphicsSettings.
     //   UnityEditor.PlayerSettings.SetGraphicsAPIs(UnityEditor.BuildTarget.StandaloneWindows64, UnityEngine.Rendering.GraphicsDeviceType.Direct3D12); 
    }

    void initializeSystem()
    {
        accelerationStructure = new RayTracingAccelerationStructure();
        CollectGeo();
        RenderTextureDescriptor textureDescriptor = new RenderTextureDescriptor();
        textureDescriptor.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;

        renderTexture = new RenderTexture(Res.x, Res.y, Res.z);
        renderTexture.enableRandomWrite = true;
        renderTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        renderTexture.volumeDepth = Res.z;
        renderTexture.depth = 0;
        renderTexture.Create();

        DebugRenderer.material.SetTexture("_BaseMap", renderTexture);

        rtshader.SetTexture("g_Output", renderTexture);

       // threads = new Vector3Int(Mathf.FloorToInt(Res.x / 8f), Mathf.FloorToInt(Res.y / 8f), 1);
        threads = new Vector3Int(Res.x, Res.y, Res.z);
        SetUpLights();

    }

    void SetUpLights()
    {
        Lights = FindObjectsOfType<Light>();

        PointLights = new List<Light>();
        ConeLights = new List<Light>();
        DirectionalLights = new List<Light>();
        AreaLights = new List<Light>();

        for (int i = 0; i < Lights.Length; i++) {

            switch (Lights[i].type)
            {
                case LightType.Point:
                    PointLights.Add(Lights[i]);
                    break;
                    
                case LightType.Spot:
                    ConeLights.Add(Lights[i]);
                    break;

                case LightType.Directional:
                    DirectionalLights.Add(Lights[i]);
                    break;

                case LightType.Area:
                    AreaLights.Add(Lights[i]);
                    break;

                case LightType.Disc:
                    AreaLights.Add(Lights[i]); //Stacking area and disc
                    break;
                default:

                break;
            }           
         }
    }
    private void Update()
    {
        UpdateLights();
    }

    private void UpdateLights()
    {
        //  CollectGeo();

        //if (Lights.Length > 512) { 
        //    Debug.LogWarning("Too many lights. Yell at Kevin to the overflow feature");
        //    return;}

        //Find sizes
        int[] listsCount = new int[] { PointLights.Count, ConeLights.Count, DirectionalLights.Count, AreaLights.Count };
        int maxCount =  Mathf.Max(listsCount);



        //Make new arrays
        Vector4[] PointLightsPos = new Vector4[PointLights.Count];
        Vector4[] PointLightsColors = new Vector4[PointLights.Count];

        Vector4[] ConeLightsWS = new Vector4[ConeLights.Count];
        Vector4[] ConeLightsColors = new Vector4[ConeLights.Count];
        Vector4[] ConeLightsDir = new Vector4[ConeLights.Count];
        Vector4[] ConeLightsPram = new Vector4[ConeLights.Count];

        Vector4[] DirLightsDir = new Vector4[DirectionalLights.Count];
        Vector4[] DirLightsColors = new Vector4[DirectionalLights.Count];

        Vector4[] AreaLightsPos = new Vector4[AreaLights.Count];
        Matrix4x4[] AreaLightsMatrix = new Matrix4x4[AreaLights.Count];
        Matrix4x4[] AreaLightsMatrixInv = new Matrix4x4[AreaLights.Count];
        Vector4[] AreaLightsColors = new Vector4[AreaLights.Count];
        Vector4[] AreaLightsSize = new Vector4[AreaLights.Count];

        for (int i =0; i< PointLightsPos.Length; i++)
        {
            PointLightsPos[i] =     PointLights[i].transform.position;
            PointLightsColors[i] =  PointLights[i].color * PointLights[i].intensity;
        }

        for (int i = 0; i < ConeLightsWS.Length; i++)
        {
            ConeLightsWS[i] = ConeLights[i].transform.position;
            ConeLightsColors[i] = ConeLights[i].color * ConeLights[i].intensity;
            ConeLightsDir[i] = ConeLights[i].transform.forward;

            float flPhiDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].spotAngle * 0.5f * Mathf.Deg2Rad)); // outer cone
            float flThetaDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].innerSpotAngle * 0.5f * Mathf.Deg2Rad)); // inner cone

            ConeLightsPram[i] = new Vector4(flPhiDot, 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot), 0, 0);
        }

        for (int i = 0; i < DirLightsDir.Length; i++)
        {
            DirLightsDir[i] = DirectionalLights[i].transform.forward;
            DirLightsColors[i] = DirectionalLights[i].color * DirectionalLights[i].intensity;
        }

        for (int i = 0; i < AreaLightsPos.Length; i++)
        {
            AreaLightsPos[i] = AreaLights[i].transform.position;
            AreaLightsMatrix[i] = Matrix4x4.TRS(AreaLights[i].transform.position, AreaLights[i].transform.rotation , Vector3.one);
            AreaLightsMatrixInv[i] = AreaLightsMatrix[i].inverse;
            AreaLightsColors[i] = AreaLights[i].color * AreaLights[i].intensity;
            AreaLightsSize[i] = new Vector3( AreaLights[i].areaSize.x, AreaLights[i].areaSize.y, AreaLights[i].type == LightType.Disc ? 1:0 ) ; //Packing for area or disc logic
        }

        //General
        rtshader.SetVector("Size", Vector3.one * scaler);
        rtshader.SetVector("WPosition", WPosition);
        rtshader.SetFloat("_Seed", ( Random.Range(0.0f,64.0f) ) );

        //Point
        rtshader.SetVectorArray("PointLightsWS", PointLightsPos);
        rtshader.SetVectorArray("PointLightsColor", PointLightsColors);
        rtshader.SetInt("PointLightCount", PointLightsPos.Length); //Add a stack overflow loop or computebuffer

        //Cone
        rtshader.SetVectorArray("ConeLightsWS", ConeLightsWS);
        rtshader.SetVectorArray("ConeLightsColor", ConeLightsColors);
        rtshader.SetVectorArray("ConeLightsDir", ConeLightsDir);
        rtshader.SetVectorArray("ConeLightsPram", ConeLightsPram);
        rtshader.SetInt("ConeLightCount", ConeLightsWS.Length); //Add a stack overflow loop or computebuffer

        //Directional
        rtshader.SetVectorArray("DirLightsDir", DirLightsDir);
        rtshader.SetVectorArray("DirLightsColor", DirLightsColors);
        rtshader.SetInt("DirLightCount", DirLightsDir.Length); //Add a stack overflow loop or computebuffer

        //Area
        rtshader.SetMatrixArray("AreaLightsMatrix", AreaLightsMatrix );
        rtshader.SetMatrixArray("AreaLightsMatrixInv", AreaLightsMatrixInv);
        rtshader.SetVectorArray("AreaLightsWS", AreaLightsPos);        
        rtshader.SetVectorArray("AreaLightsColor", AreaLightsColors);
        rtshader.SetVectorArray("AreaLightsSize", AreaLightsSize);
        rtshader.SetInt("AreaLightCount", AreaLightsPos.Length); //Add a stack overflow loop or computebuffer
        rtshader.SetInt("AreaLightSamples", System.Convert.ToInt32(AreaLightSamples) );

        //Dispatching
        rtshader.Dispatch("MainRayGenShader", threads.x, threads.y, threads.z);
    }


    void CollectGeo()
    {
        Renderer[] Meshes = FindObjectsOfType<Renderer>();

        for (int i = 0; i< Meshes.Length; i++)
        {
            accelerationStructure.AddInstance(Meshes[i]);
        }
        accelerationStructure.Build();
        rtshader.SetAccelerationStructure("g_SceneAccelStruct", accelerationStructure);

    }


}
