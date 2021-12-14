﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using com.rfilkov.kinect;


namespace com.rfilkov.components
{
    /// <summary>
    /// ForegroundBlendRenderer provides volumetric rendering and lighting of the real environment, filtered by the background-removal manager.
    /// </summary>
    public class ForegroundBlendRenderer : MonoBehaviour
    {
        [Tooltip("Reference to background removal manager. If left to None, it looks up the first available BR-manager in the scene.")]
        public BackgroundRemovalManager backgroundRemovalManager = null;

        [Tooltip("Depth value in meters, used for invalid depth points.")]
        public float invalidDepthValue = 0f;

        [Tooltip("Whether to maximize the rendered object on the screen, or not.")]
        public bool maximizeOnScreen = true;

        [Tooltip("Whether to apply per-pixel lighting on the foreground, or not.")]
        public bool applyLighting = false;

        [Tooltip("Camera used to scale the mesh, to fill the camera's background. If left empty, it will default to the main camera in the scene.")]
        public Camera foregroundCamera;

        [Tooltip("Background image (if any) that needs to be overlayed by this blend renderer.")]
        public UnityEngine.UI.RawImage backgroundImage;


        // references to KM and data
        private KinectManager kinectManager = null;
        private KinectInterop.SensorData sensorData = null;
        private DepthSensorBase sensorInt = null;
        private Material matRenderer = null;

        // depth image buffer (in depth camera resolution)
        private ComputeBuffer depthImageBuffer = null;

        // textures
        private Texture alphaTex = null;
        private Texture colorTex = null;

        // lighting
        private FragmentLighting lighting = new FragmentLighting();

        // saved screen width & height
        private int lastScreenW = 0;
        private int lastScreenH = 0;
        private int lastColorW = 0;
        private int lastColorH = 0;
        private Vector3 initialScale = Vector3.one;


        void Start()
        {
            kinectManager = KinectManager.Instance;
            initialScale = transform.localScale;

            if (backgroundRemovalManager == null)
            {
                backgroundRemovalManager = FindObjectOfType<BackgroundRemovalManager>();
            }

            Renderer meshRenderer = GetComponent<Renderer>();
            if (meshRenderer)
            {
                Shader blendShader = Shader.Find("Kinect/ForegroundBlendShader");
                if(blendShader != null)
                {
                    matRenderer = new Material(blendShader);
                    meshRenderer.material = matRenderer;
                }
            }

            if (kinectManager && kinectManager.IsInitialized() && backgroundRemovalManager && backgroundRemovalManager.enabled)
            {
                // get sensor data
                sensorData = kinectManager.GetSensorData(backgroundRemovalManager.sensorIndex);
                sensorInt = sensorData != null ? (DepthSensorBase)sensorData.sensorInterface : null;
            }

            if (foregroundCamera == null)
            {
                foregroundCamera = Camera.main;
            }

            // find scene lights
            Light[] sceneLights = GameObject.FindObjectsOfType<Light>();
            lighting.SetLightsAndBounds(sceneLights, transform.position, new Vector3(20f, 20f, 20f));

            //Debug.Log("sceneLights: " + sceneLights.Length);
            //for(int i = 0; i < sceneLights.Length; i++)
            //{
            //    Debug.Log(i.ToString() + " - " + sceneLights[i].name + " - " + sceneLights[i].type);
            //}
        }


        void OnDestroy()
        {
            if (sensorData != null && sensorData.colorDepthBuffer != null)
            {
                sensorData.colorDepthBuffer.Release();
                sensorData.colorDepthBuffer = null;
            }

            if (depthImageBuffer != null)
            {
                //depthImageCopy = null;

                depthImageBuffer.Release();
                depthImageBuffer = null;
            }

            // release lighting resources
            lighting.ReleaseResources();
        }


        void Update()
        {
            if (matRenderer == null || sensorData == null || sensorInt == null)
                return;

            if(alphaTex == null || alphaTex.width != sensorData.colorImageWidth || alphaTex.height != sensorData.colorImageHeight)
            {
                // alpha texture
                alphaTex = backgroundRemovalManager.GetAlphaTex();

                if(alphaTex != null)
                {
                    matRenderer.SetTexture("_AlphaTex", alphaTex);
                }
            }

            if(colorTex == null || colorTex.width != sensorData.colorImageWidth || colorTex.height != sensorData.colorImageHeight)
            {
                // color texture
                colorTex = !backgroundRemovalManager.computeAlphaMaskOnly ? backgroundRemovalManager.GetForegroundTex() : alphaTex;  // sensorInt.pointCloudColorTexture

                if (colorTex != null)
                {
                    matRenderer.SetInt("_TexResX", colorTex.width);
                    matRenderer.SetInt("_TexResY", colorTex.height);

                    matRenderer.SetTexture("_ColorTex", colorTex);
                }
            }

            if (colorTex == null || alphaTex == null /**|| foregroundCamera == null*/)
                return;

            if (sensorInt.pointCloudResolution == DepthSensorBase.PointCloudResolution.DepthCameraResolution)
            {
                int depthBufferLength = sensorData.depthImageWidth * sensorData.depthImageHeight / 2;
                if (depthImageBuffer == null || depthImageBuffer.count != depthBufferLength)
                {
                    //int depthImageLength = sensorData.depthImageWidth * sensorData.depthImageHeight;
                    //depthImageCopy = new ushort[depthImageLength];

                    depthImageBuffer = KinectInterop.CreateComputeBuffer(depthImageBuffer, depthBufferLength, sizeof(uint));
                    matRenderer.SetBuffer("_DepthMap", depthImageBuffer);
                    //Debug.Log("Created depthImageBuffer with len: " + depthBufferLength);
                }

                if (depthImageBuffer != null && sensorData.depthImage != null)
                {
                    //KinectInterop.CopyBytes(sensorData.depthImage, sizeof(ushort), depthImageCopy, sizeof(ushort));
                    KinectInterop.SetComputeBufferData(depthImageBuffer, sensorData.depthImage, depthBufferLength, sizeof(uint));
                }

                //Debug.Log("ForegroundBlendRenderer DepthFrameTime: " + lastDepthFrameTime);
            }
            else
            {
                int bufferLength = sensorData.colorImageWidth * sensorData.colorImageHeight / 2;
                if (sensorData.colorDepthBuffer == null || sensorData.colorDepthBuffer.count != bufferLength)
                {
                    sensorData.colorDepthBuffer = new ComputeBuffer(bufferLength, sizeof(uint));
                    matRenderer.SetBuffer("_DepthMap", sensorData.colorDepthBuffer);
                    //Debug.Log("Created colorDepthBuffer with len: " + bufferLength);
                }

                //Debug.Log("ForegroundBlendRenderer ColorDepthBufferTime: " + sensorData.lastColorDepthBufferTime);
            }

            matRenderer.SetFloat("_DepthDistance", 0f);
            matRenderer.SetFloat("_InvDepthVal", invalidDepthValue);

            int curScreenW = foregroundCamera ? foregroundCamera.pixelWidth : Screen.width;
            int curScreenH = foregroundCamera ? foregroundCamera.pixelHeight : Screen.height;
            if (lastScreenW != curScreenW || lastScreenH != curScreenH || lastColorW != sensorData.colorImageWidth || lastColorH != sensorData.colorImageHeight)
            {
                ScaleRendererTransform(colorTex, curScreenW, curScreenH);
            }

            // update lighting parameters
            lighting.UpdateLighting(matRenderer, applyLighting);
        }

        // scales the renderer's transform properly
        private void ScaleRendererTransform(Texture colorTex, int curScreenW, int curScreenH)
        {
            lastScreenW = curScreenW;
            lastScreenH = curScreenH;
            lastColorW = sensorData.colorImageWidth;
            lastColorH = sensorData.colorImageHeight;

            Vector3 localScale = transform.localScale;

            if (maximizeOnScreen && foregroundCamera)
            {
                float objectZ = transform.position.z;
                float screenW = foregroundCamera.pixelWidth;
                float screenH = foregroundCamera.pixelHeight;

                if (backgroundImage)
                {
                    PortraitBackground portraitBack = backgroundImage.gameObject.GetComponent<PortraitBackground>();

                    if (portraitBack != null)
                    {
                        Rect backRect = portraitBack.GetBackgroundRect();
                        screenW = backRect.width;
                        screenH = backRect.height;
                    }
                }

                Vector3 vLeft = foregroundCamera.ScreenToWorldPoint(new Vector3(0f, screenH / 2f, objectZ));
                Vector3 vRight = foregroundCamera.ScreenToWorldPoint(new Vector3(screenW, screenH / 2f, objectZ));
                float distLeftRight = (vRight - vLeft).magnitude;

                Vector3 vBottom = foregroundCamera.ScreenToWorldPoint(new Vector3(screenW / 2f, 0f, objectZ));
                Vector3 vTop = foregroundCamera.ScreenToWorldPoint(new Vector3(screenW / 2f, screenH, objectZ));
                float distBottomTop = (vTop - vBottom).magnitude;

                localScale.x = distLeftRight / initialScale.x;
                localScale.y = distBottomTop / initialScale.y;
                //Debug.Log("ForegroundRenderer scale: " + localScale + ", screenW: " + screenW + ", screenH: " + screenH);
            }

            // scale according to color-tex resolution
            //localScale.y = localScale.x * colorTex.height / colorTex.width;

            // apply color image scale
            Vector3 colorImageScale = kinectManager.GetColorImageScale(backgroundRemovalManager.sensorIndex);
            if (colorImageScale.x < 0f)
                localScale.x = -localScale.x;
            if (colorImageScale.y < 0f)
                localScale.y = -localScale.y;

            transform.localScale = localScale;
        }

    }
}
