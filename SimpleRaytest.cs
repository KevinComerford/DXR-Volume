using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.Experimental.Rendering;

public class SimpleRaytest : MonoBehaviour
{ 
    public RenderTexture renderTexture;
    public RayTracingShader rtshader;

    public Vector3Int Res = new Vector3Int(512,512,512);

    public float scaler = 1.0f;
    public Vector3 WPosition;

    public Renderer DebugRenderer;
    public Light[] Lights;
    public uint AreaLightSamples = 16;

    List<Light> PointLights,ConeLights,DirectionalLights,AreaLights;

    RayTracingAccelerationStructure accelerationStructure;
    Vector3Int threads;

    struct PointLightData
    {
        //Point
        public Vector3 PointLightsPos;
        public Vector4 PointLightsColors;
    }

    struct ConeLightData
    {
        public Vector3 ConeLightsWS;
        public Vector4 ConeLightsColors;
        public Vector3 ConeLightsDir;
        public Vector2 ConeLightsPram;
    }

    struct DirLightData
    {
        public Vector3 DirLightsDir;
        public Vector4 DirLightsColors;
    }
    struct AreaLightData
    {
        public Matrix4x4 AreaLightsMatrix;
        public Matrix4x4 AreaLightsMatrixInv;
        public Vector3 AreaLightsPos;
        public Vector4 AreaLightsColors;
        public Vector3 AreaLightsSize;
    }


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

        //Set up buffers with data stride
        ComputeBuffer pointBuffer = new ComputeBuffer(PointLights.Count, (3 + 4) * 4);
        ComputeBuffer coneBuffer = new ComputeBuffer(ConeLights.Count, (3 + 4 + 3 + 2) * 4);
        ComputeBuffer dirBuffer = new ComputeBuffer(DirectionalLights.Count, (3 + 4) * 4);
        ComputeBuffer areaBuffer = new ComputeBuffer(AreaLights.Count, (4*4 + 4*4 + 3 + 4 + 3) * 4);

        PointLightData[] PointLDatas = new PointLightData[PointLights.Count];
        ConeLightData[] ConeLDatas = new ConeLightData[ConeLights.Count];
        DirLightData[] DirLDatas = new DirLightData[DirectionalLights.Count];
        AreaLightData[] AreaLDatas = new AreaLightData[AreaLights.Count];

        for (int i =0; i< PointLDatas.Length; i++)
        {
            PointLDatas[i].PointLightsPos = PointLights[i].transform.position;
            PointLDatas[i].PointLightsColors =  PointLights[i].color * PointLights[i].intensity;
        }

        for (int i = 0; i < ConeLDatas.Length; i++)
        {
            ConeLDatas[i].ConeLightsWS = ConeLights[i].transform.position;
            ConeLDatas[i].ConeLightsColors = ConeLights[i].color * ConeLights[i].intensity;
            ConeLDatas[i].ConeLightsDir = ConeLights[i].transform.forward;

            float flPhiDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].spotAngle * 0.5f * Mathf.Deg2Rad)); // outer cone
            float flThetaDot = Mathf.Clamp01(Mathf.Cos(ConeLights[i].innerSpotAngle * 0.5f * Mathf.Deg2Rad)); // inner cone

            ConeLDatas[i].ConeLightsPram = new Vector4(flPhiDot, 1.0f / Mathf.Max(0.01f, flThetaDot - flPhiDot), 0, 0);
        }

        for (int i = 0; i < DirLDatas.Length; i++)
        {
            DirLDatas[i].DirLightsDir = DirectionalLights[i].transform.forward;
            DirLDatas[i].DirLightsColors = DirectionalLights[i].color * DirectionalLights[i].intensity;
        }

        for (int i = 0; i < AreaLDatas.Length; i++)
        {
            AreaLDatas[i].AreaLightsPos = AreaLights[i].transform.position;
            AreaLDatas[i].AreaLightsMatrix = Matrix4x4.TRS(AreaLights[i].transform.position, AreaLights[i].transform.rotation , Vector3.one);
            AreaLDatas[i].AreaLightsMatrixInv = AreaLDatas[i].AreaLightsMatrix.inverse;
            AreaLDatas[i].AreaLightsColors = AreaLights[i].color * AreaLights[i].intensity;
            AreaLDatas[i].AreaLightsSize = new Vector3( AreaLights[i].areaSize.x, AreaLights[i].areaSize.y, AreaLights[i].type == LightType.Disc ? 1:0 ) ; //Packing for area or disc logic
        }

        pointBuffer.SetData(PointLDatas);
        coneBuffer.SetData(ConeLDatas);
        dirBuffer.SetData(DirLDatas);
        areaBuffer.SetData(AreaLDatas);


        //General
        rtshader.SetVector("Size", Vector3.one * scaler);
        rtshader.SetVector("WPosition", WPosition);
        rtshader.SetFloat("_Seed", ( Random.Range(0.0f,64.0f) ) );

        //Point
        rtshader.SetInt("PointLightCount", PointLDatas.Length); //Add a stack overflow loop or computebuffer
        rtshader.SetBuffer("PLD", pointBuffer); ;

        //Cone
        rtshader.SetInt("ConeLightCount", ConeLDatas.Length); //Add a stack overflow loop or computebuffer
        rtshader.SetBuffer("CLD", coneBuffer);

        //Directional
        rtshader.SetInt("DirLightCount", DirLDatas.Length); //Add a stack overflow loop or computebuffer
        rtshader.SetBuffer("DLD", dirBuffer);

        //Area
        rtshader.SetInt("AreaLightCount", AreaLDatas.Length); //Add a stack overflow loop or computebuffer
        rtshader.SetInt("AreaLightSamples", System.Convert.ToInt32(AreaLightSamples) );
        rtshader.SetBuffer("ALD", areaBuffer);

        //Dispatching
        rtshader.Dispatch("MainRayGenShader", threads.x, threads.y, threads.z);

        pointBuffer.Release();
        coneBuffer.Release();
        dirBuffer.Release();
        areaBuffer.Release();
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
