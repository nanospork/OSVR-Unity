﻿using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System;

namespace OSVR
{
    namespace Unity
    {
        public class OsvrRenderManager : MonoBehaviour
        {
            public const int RENDER_EVENT = 0;
            public const int SHUTDOWN_EVENT = 1;
            public const int UPDATE_RENDERINFO_EVENT = 2;
            private const string PluginName = "osvrUnityRenderingPlugin";

            // Allow for calling into the debug console from C++
            [DllImport(PluginName)]
            private static extern void LinkDebug([MarshalAs(UnmanagedType.FunctionPtr)]IntPtr debugCal);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate void DebugLog(string log);
            private static readonly DebugLog debugLog = DebugWrapper;
            private static readonly IntPtr functionPointer = Marshal.GetFunctionPointerForDelegate(debugLog);
            private static void DebugWrapper(string log) { Debug.Log(log); }

            //get the render event function that we'll call every frame via GL.IssuePluginEvent
            [DllImport(PluginName, CallingConvention = CallingConvention.StdCall)]
            private static extern IntPtr GetRenderEventFunc();

            //Pass a pointer to a texture (RenderTexture.GetNativeTexturePtr()) to the plugin
            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern void SetColorBufferFromUnity(System.IntPtr texturePtr, int eye);

            //Create a RenderManager object in the plugin, passing in a ClientContext
            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern Byte CreateRenderManagerFromUnity(OSVR.ClientKit.SafeClientContextHandle /*OSVR_ClientContext*/ ctx);

            [StructLayout(LayoutKind.Sequential)]
            public struct OSVR_ViewportDescription
            {
                public double left;    //< Left side of the viewport in pixels
                public double lower;   //< First pixel in the viewport at the bottom.
                public double width;   //< Last pixel in the viewport at the top
                public double height;   //< Last pixel on the right of the viewport in pixels
            }

            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern OSVR_ViewportDescription GetViewport(int eye);

            [StructLayout(LayoutKind.Sequential)]
            public struct OSVR_ProjectionMatrix
            {
                public double left;
                public double right;
                public double top;
                public double bottom;
                public double nearClip;        //< Cannot name "near" because Visual Studio keyword
                public double farClip;
            }

            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern OSVR_ProjectionMatrix GetProjectionMatrix(int eye);

            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern OSVR.ClientKit.Pose3 GetEyePose(int eye);


            [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
            private static extern void ShutdownRenderManager();

            private OSVR.ClientKit.ClientContext _renderManagerClientContext;

            public int InitRenderManager()
            {
                //LinkDebug(functionPointer); // Hook our c++ plugin into Unity's console log.
                _renderManagerClientContext = new OSVR.ClientKit.ClientContext("com.sensics.rendermanagercontext", 0);
                return CreateRenderManager(_renderManagerClientContext);
            }

            public OSVR.ClientKit.Pose3 GetRenderManagerEyePose(int eye)
            {
                return GetEyePose(eye);
            }

            public OSVR.ClientKit.Viewport GetEyeViewport(int eye)
            {
                OSVR.ClientKit.Viewport v = new OSVR.ClientKit.Viewport();
                OSVR_ViewportDescription viewportDescription = GetViewport(eye);
                v.Left = (int)viewportDescription.left;
                v.Bottom = (int)viewportDescription.lower;
                v.Width = (int)viewportDescription.width;
                v.Height = (int)viewportDescription.height;
                return v;
            }

            public Matrix4x4 GetEyeProjectionMatrix(int eye)
            {
                OSVR_ProjectionMatrix pm = GetProjectionMatrix(eye);
                return PerspectiveOffCenter((float)pm.left, (float)pm.right, (float)pm.bottom, (float)pm.top, (float)pm.nearClip, (float)pm.farClip);
                
            }

            //Returns a Unity Matrix4x4 from the provided boundaries
            //from http://docs.unity3d.com/ScriptReference/Camera-projectionMatrix.html
            static Matrix4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
            {
                float x = 2.0F * near / (right - left);
                float y = 2.0F * near / (top - bottom);
                float a = (right + left) / (right - left);
                float b = (top + bottom) / (top - bottom);
                float c = -(far + near) / (far - near);
                float d = -(2.0F * far * near) / (far - near);
                float e = -1.0F;
                Matrix4x4 m = new Matrix4x4();
                m[0, 0] = x;
                m[0, 1] = 0;
                m[0, 2] = a;
                m[0, 3] = 0;
                m[1, 0] = 0;
                m[1, 1] = y;
                m[1, 2] = b;
                m[1, 3] = 0;
                m[2, 0] = 0;
                m[2, 1] = 0;
                m[2, 2] = c;
                m[2, 3] = d;
                m[3, 0] = 0;
                m[3, 1] = 0;
                m[3, 2] = e;
                m[3, 3] = 0;
                return m;
            }

            //Call the Unity Rendering Plugin to initialize the RenderManager
            public int CreateRenderManager(OSVR.ClientKit.ClientContext clientContext)
            {
                return CreateRenderManagerFromUnity(clientContext.ContextHandle);
            }

            //Pass pointer to eye-camera RenderTexture to the Unity Rendering Plugin
            public void SetEyeColorBuffer(IntPtr colorBuffer, int eye)
            {               
                SetColorBufferFromUnity(colorBuffer, eye);
            }

            //Get a pointer to the plugin's rendering function
            public IntPtr GetRenderEventFunction()
            {
                return GetRenderEventFunc();
            }

            //Shutdown RenderManager and Dispose of the ClientContext we created for it
            public void ExitRenderManager()
            {
                ShutdownRenderManager();
                if (null != _renderManagerClientContext)
                {
                    _renderManagerClientContext.Dispose();
                    _renderManagerClientContext = null;
                }
            }

            //helper functions to determine is RenderManager is supported
            //Is the RenderManager supported? Requires D3D11 or OpenGL, currently.
            public bool IsRenderManagerSupported()
            {
                bool support = true;
#if UNITY_ANDROID
                Debug.Log("RenderManager not yet supported on Android.");
                support = false;
#endif
                if (!SystemInfo.graphicsDeviceVersion.Contains("OpenGL") && !SystemInfo.graphicsDeviceVersion.Contains("Direct3D 11"))
                {
                    Debug.LogError("RenderManager not supported on " +
                        SystemInfo.graphicsDeviceVersion + ". Only Direct3D11 is currently supported.");
                    support = false;
                }

                if (!SystemInfo.supportsRenderTextures)
                {
                    Debug.LogError("RenderManager not supported. RenderTexture (Unity Pro feature) is unavailable.");
                    support = false;
                }
                if (!IsUnityVersionSupported())
                {
                    Debug.LogError("RenderManager not supported. Unity 5.2+ is required for RenderManager support.");
                    support = false;
                }
                return support;
            }

            //Unity 5.2+ is required as the plugin uses the native plugin interface introduced in Unity 5.2
            public bool IsUnityVersionSupported()
            {
                bool support = true;
                try
                {
                    string version = new Regex(@"(\d+\.\d+)\..*").Replace(Application.unityVersion, "$1");
                    if (new Version(version) < new Version("5.2"))
                    {
                        support = false;
                    }
                }
                catch
                {
                    Debug.LogWarning("Unable to determine Unity version from: " + Application.unityVersion);
                    support = false;
                }
                return support;
            }
        }
    }
}