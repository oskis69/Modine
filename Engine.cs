﻿using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using ImGuiNET;

using SN = System.Numerics;

using GameEngine.Common;
using GameEngine.Importer;
using GameEngine.Rendering;
using static GameEngine.Rendering.SceneObject;
using GameEngine.ImGUI;
using System.Runtime.InteropServices;

namespace GameEngine
{
    class Game : GameWindow
    {
        public Game(int width, int height, string title)
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                Title = title,
                Size = new Vector2i(width, height),
                WindowBorder = WindowBorder.Resizable,
                StartVisible = false,
                StartFocused = true,
                WindowState = WindowState.Normal,
                API = ContextAPI.OpenGL,
                Profile = ContextProfile.Core,
                APIVersion = new Version(4, 3),
                Flags = ContextFlags.Debug
            })
        {
            CenterWindow();
            viewportSize = this.Size;
            previousViewportSize = viewportSize;
        }

        private bool viewportHovered;
        public bool ShowDepth_Stencil = false;

        private Vector2i viewportSize;
        private Vector2i previousViewportSize;
        private Vector2i viewportPos;
        private float pitch = 0.5f, yaw = 0.0f;
        float sensitivity = 0.01f;

        int frameCount = 0;
        double elapsedTime = 0.0, fps = 0.0, ms;

        Vector3 ambient = new(0.05f);
        Vector3 direction = new(1);
        float shadowFactor = 0.75f;
        Material mat_monkey;
        Material mat_cube;
        Material mat_floor;
        public Shader PBRShader;
        public Shader lightShader;
        public Shader shadowShader;
        public Shader postprocessShader;
        public Shader outlineShader;
        Matrix4 projectionMatrix;
        Matrix4 viewMatrix;

        Mesh suzanne;
        Mesh floor;
        Mesh cube;
        VertexData[] vertexData;
        int[] indices;
        VertexData[] planeVertexData;
        int[] planeIndices;
        VertexData[] cubeVertexData;
        int[] cubeIndices;
        VertexData[] sphereVertexData;
        int[] sphereIndices;
        int triangleCount = 0;

        Camera camera;
        List<Mesh> Meshes = new List<Mesh>();
        static List<SceneObject> sceneObjects = new List<SceneObject>();
        int selectedSceneObject = 0;
        Light light;
        Light light2;
        int count_PointLights, count_Meshes = 0;

        PolygonMode _polygonMode = PolygonMode.Fill;
        private bool vsyncOn = true;

        private ImGuiController ImGuiController;
        int FBO;
        int framebufferTexture;
        int depthStencilTexture;

        int depthMapFBO;
        int depthMap;
        int shadowRes = 2048;

        bool renderShadowMap = false;

        private int VAO;
        private int VBO;

        float[] PPvertices =
        {
            -1f,  1f,
            -1f, -1f,
             1f,  1f,
             1f,  1f,
            -1f, -1f,
             1f, -1f,
        };

        private static void OnDebugMessage(
            DebugSource source,     // Source of the debugging message.
            DebugType type,         // Type of the debugging message.
            int id,                 // ID associated with the message.
            DebugSeverity severity, // Severity of the message.
            int length,             // Length of the string in pMessage.
            IntPtr pMessage,        // Pointer to message string.
            IntPtr pUserParam)      // The pointer you gave to OpenGL, explained later.
        {
            // In order to access the string pointed to by pMessage, you can use Marshal
            // class to copy its contents to a C# string without unsafe code. You can
            // also use the new function Marshal.PtrToStringUTF8 since .NET Core 1.1.
            string message = Marshal.PtrToStringAnsi(pMessage, length);

            // The rest of the function is up to you to implement, however a debug output
            // is always useful.
            Console.WriteLine("[{0} source={1} type={2} id={3}] \n{4}", severity, source, type, id, message);

            // Potentially, you may want to throw from the function for certain severity
            // messages.
            if (type == DebugType.DebugTypeError)
            {
                throw new Exception(message);
            }
        }

        private static DebugProc DebugMessageDelegate = OnDebugMessage;

        unsafe protected override void OnLoad()
        {
            base.OnLoad();

            GL.DebugMessageCallback(DebugMessageDelegate, IntPtr.Zero);
            GL.Enable(EnableCap.DebugOutput);

            MakeCurrent();
            IsVisible = true;
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.StencilTest);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Replace);
            GL.PointSize(5);

            VSync = VSyncMode.Adaptive;

            FBO = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);

            Framebuffers.SetupFBO(ref framebufferTexture, ref depthStencilTexture, viewportSize);
            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

            OpenTK.Graphics.OpenGL4.ErrorCode error = GL.GetError();
            if (error != OpenTK.Graphics.OpenGL4.ErrorCode.NoError) Console.WriteLine("OpenGL Error: " + error.ToString());
            if (status != FramebufferErrorCode.FramebufferComplete) Console.WriteLine($"Framebuffer is incomplete: {status}");

            Framebuffers.SetupShadowFBO(ref depthMapFBO, ref depthMap, shadowRes);

            projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(75), 1280 / 768, 0.1f, 100);
            viewMatrix = Matrix4.LookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY);

            PBRShader = new Shader("Shaders/PBR/mesh.vert", "Shaders/PBR/mesh.frag");
            shadowShader = new Shader("Shaders/PBR/shadow.vert", "Shaders/PBR/shadow.frag");
            lightShader = new Shader("Shaders/Lights/light.vert", "Shaders/Lights/light.frag");
            postprocessShader = new Shader("Shaders/Postprocessing/postprocess.vert", "Shaders/Postprocessing/postprocess.frag");
            outlineShader = new Shader("Shaders/Postprocessing/outlineSelection.vert", "Shaders/Postprocessing/outlineSelection.frag");

            VAO = GL.GenVertexArray();
            GL.BindVertexArray(VAO);

            VBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VBO);
            GL.BufferData(BufferTarget.ArrayBuffer, PPvertices.Length * sizeof(float), PPvertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(postprocessShader.GetAttribLocation("aPosition"));
            GL.VertexAttribPointer(postprocessShader.GetAttribLocation("aPosition"), 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            camera = new Camera(new(0, 1, 2), -Vector3.UnitZ, 10);
            mat_monkey = new(new(0, 0.45f, 1), 0, 0.2f);
            mat_cube = new(new(0.875f, 0.6f, 0.185f), 1, 0.45f);
            mat_floor = new(new(1, 1, 1), 0, 0.2f);
            mat_monkey.SetShaderUniforms(PBRShader);
            mat_floor.SetShaderUniforms(PBRShader);

            PBRShader.SetVector3("ambient", ambient);
            PBRShader.SetVector3("direction", direction);
            PBRShader.SetFloat("shadowFactor", shadowFactor);

            ModelImporter.LoadModel("Importing/Suzanne.fbx", out vertexData, out indices);
            ModelImporter.LoadModel("Importing/Floor.fbx", out planeVertexData, out planeIndices);  
            ModelImporter.LoadModel("Importing/RoundedCube.fbx", out cubeVertexData, out cubeIndices);
            ModelImporter.LoadModel("Importing/Sphere.fbx", out sphereVertexData, out sphereIndices);
            suzanne = new Mesh(vertexData, indices, PBRShader, true, true, mat_monkey);
            suzanne.scale = new(0.75f);
            suzanne.position = new(0, 2, 0);
            suzanne.rotation = new(-125, 0, 0);

            floor = new Mesh(planeVertexData, planeIndices, PBRShader, true, true, mat_floor);
            floor.position = new(0, 0, 0);
            floor.scale = new(1, 1, 1);
            floor.rotation = new(-90, 0, 0);

            cube = new Mesh(cubeVertexData, cubeIndices, PBRShader, true, true, mat_cube);
            cube.position = new(3, 1, 0);
            cube.scale = new(0.5f);
            cube.rotation = new(-90, -40, 0);

            light = new Light(lightShader, new(1, 1, 0), 5);
            light2 = new Light(lightShader, new(1, 0, 1), 5);
            light.position = new(3, 4, -3);
            light2.position = new(-2, 7, -6);

            SceneObject _monkey = new("Monkey", SceneObjectType.Mesh, suzanne);
            SceneObject _cube = new("Cube", SceneObjectType.Mesh, cube);
            SceneObject _floor = new("Floor", SceneObjectType.Mesh, floor);
            SceneObject _light = new("Light1", SceneObjectType.Light, null, light);
            SceneObject _light2 = new("Light2", SceneObjectType.Light, null, light2);
            sceneObjects.Add(_monkey);
            sceneObjects.Add(_floor);
            sceneObjects.Add(_light);
            sceneObjects.Add(_light2);
            sceneObjects.Add(_cube);

            count_Meshes = 0;
            count_PointLights = 0;
            foreach (SceneObject sceneObject in sceneObjects)
            {
                if (sceneObject.Type == SceneObjectType.Mesh) count_Meshes += 1;
                else if (sceneObject.Type == SceneObjectType.Light) count_PointLights += 1;
            }

            triangleCount = CalculateTriangles();

            ImGuiController = new ImGuiController(viewportSize.X, viewportSize.Y);
            ImGuiWindows.LoadTheme();

            GLFW.MaximizeWindow(WindowPtr);
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            if (IsMouseButtonDown(MouseButton.Button2))
            {
                CursorState = CursorState.Grabbed;
                
                float deltaX = MouseState.Delta.X;
                float deltaY = MouseState.Delta.Y;
                yaw += deltaX * sensitivity;
                pitch -= deltaY * sensitivity;
                pitch = MathHelper.Clamp(pitch, -MathHelper.PiOver2 + 0.005f, MathHelper.PiOver2 - 0.005f);

                camera.direction = new Vector3(
                    (float)Math.Cos(yaw) * (float)Math.Cos(pitch),
                    (float)Math.Sin(pitch),
                    (float)Math.Sin(yaw) * (float)Math.Cos(pitch));
            }
            else CursorState = CursorState.Normal;

            float moveAmount = (float)(camera.speed * args.Time);
            if (viewportHovered)
            {
                if (IsKeyDown(Keys.W)) camera.position += moveAmount * camera.direction;
                if (IsKeyDown(Keys.S)) camera.position -= moveAmount * camera.direction;
                if (IsKeyDown(Keys.A)) camera.position -= moveAmount * Vector3.Normalize(Vector3.Cross(camera.direction, Vector3.UnitY));
                if (IsKeyDown(Keys.D)) camera.position += moveAmount * Vector3.Normalize(Vector3.Cross(camera.direction, Vector3.UnitY));
                if (IsKeyDown(Keys.E)) camera.position += moveAmount * Vector3.UnitY;
                if (IsKeyDown(Keys.Q)) camera.position -= moveAmount * Vector3.UnitY;                
            }

            frameCount++;
            elapsedTime += args.Time;
            if (elapsedTime >= 0.1f)
            {
                fps = frameCount / elapsedTime;
                ms = 1000 * elapsedTime / frameCount;
                frameCount = 0;
                elapsedTime = 0.0;
            }
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            RenderScene(args.Time);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            RenderScene(0.017f);

            ImGuiController.WindowResized(e.Width, e.Height);
        }

        public void RenderScene(double time)
        {
            count_Meshes = 0;
            count_PointLights = 0;
            foreach (SceneObject sceneObject in sceneObjects)
            {
                if (sceneObject.Type == SceneObjectType.Mesh) count_Meshes += 1;
                else if (sceneObject.Type == SceneObjectType.Light) count_PointLights += 1;
            }

            // Render shadow scene
            GL.Viewport(0, 0, shadowRes, shadowRes);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, depthMapFBO);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            // Draw only meshes
            foreach (SceneObject sceneObject in sceneObjects) if (sceneObject.Type == SceneObjectType.Mesh) sceneObject.Mesh.meshShader = shadowShader;
            renderShadowMap = true;
            UpdateMatrices();
            foreach (SceneObject sceneObject in sceneObjects) if (sceneObject.Type == SceneObjectType.Mesh && sceneObject.Mesh.castShadow == true) sceneObject.Mesh.Render();

            // Render normal scene
            GL.Viewport(0, 0, viewportSize.X, viewportSize.Y);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FBO);
            GL.ClearColor(new Color4(ambient.X, ambient.Y, ambient.Z, 1));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            GL.PolygonMode(MaterialFace.FrontAndBack, _polygonMode);

            foreach (SceneObject sceneObject in sceneObjects)
            {
                if (sceneObject.Type == SceneObjectType.Mesh) sceneObject.Mesh.meshShader = PBRShader;
                if (sceneObject.Type == SceneObjectType.Light) sceneObject.Light.lightShader = lightShader;
            }

            PBRShader.SetVector3("viewPos", camera.position);
            PBRShader.SetInt("countPL", count_PointLights);

            renderShadowMap = false;
            UpdateMatrices();

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, depthMap);

            if (sceneObjects.Count > 0)
            {
                // Render sceneobject list
                GL.Enable(EnableCap.StencilTest);

                GL.StencilFunc(StencilFunction.Always, 1, 0xFF);
                GL.StencilOp(StencilOp.Replace, StencilOp.Replace, StencilOp.Replace);
                GL.StencilMask(0x00);

                int index = 0;
                for (int i = 0; i < sceneObjects.Count; i++)
                {
                    if (sceneObjects[i].Type == SceneObjectType.Light)
                    {
                        PBRShader.SetVector3("pointLights[" + index + "].lightColor", sceneObjects[i].Light.lightColor);
                        PBRShader.SetVector3("pointLights[" + index + "].lightPos", sceneObjects[i].Light.position);
                        PBRShader.SetFloat("pointLights[" + index + "].strength", sceneObjects[i].Light.strength);
                        index += 1;
                    }
                }
                
                PBRShader.Use();
                for (int i = 0; i < sceneObjects.Count; i++)
                {
                    if (sceneObjects[i].Type == SceneObjectType.Mesh)
                    {
                        sceneObjects[i].Mesh.meshShader.SetInt("smoothShading", Convert.ToInt32(sceneObjects[i].Mesh.smoothShading));
                        sceneObjects[i].Mesh.Material.SetShaderUniforms(PBRShader);
                        sceneObjects[i].Mesh.Render();
                    }
                }

                lightShader.Use();
                for (int i = 0; i < sceneObjects.Count; i++) if (sceneObjects[i].Type == SceneObjectType.Light) sceneObjects[i].Light.Render(camera.position, camera.direction, pitch, yaw);

                // Render selected sceneobject infront of everything and dont write to color buffer
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.CullFace);
                GL.ColorMask(false, false, false, false);
                GL.StencilFunc(StencilFunction.Notequal, 1, 0xFF);
                GL.StencilMask(0xFF);

                if (sceneObjects[selectedSceneObject].Type == SceneObjectType.Mesh)
                {
                    PBRShader.Use();
                    sceneObjects[selectedSceneObject].Mesh.meshShader.SetInt("smoothShading", Convert.ToInt32(sceneObjects[selectedSceneObject].Mesh.smoothShading));
                    sceneObjects[selectedSceneObject].Mesh.Material.SetShaderUniforms(PBRShader);
                    sceneObjects[selectedSceneObject].Mesh.Render();
                }
                else if (sceneObjects[selectedSceneObject].Type == SceneObjectType.Light)
                {
                    lightShader.Use();
                    sceneObjects[selectedSceneObject].Light.Render(camera.position, camera.direction, pitch, yaw);
                }

                GL.ColorMask(true, true, true, true);
                GL.Enable(EnableCap.DepthTest);
                GL.Enable(EnableCap.CullFace);
                GL.Disable(EnableCap.StencilTest);
            }

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            // Bind framebuffer texture
            postprocessShader.SetInt("frameBufferTexture", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, framebufferTexture);

            // Bind depth texture
            postprocessShader.SetInt("depth", 1);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, depthStencilTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.DepthStencilTextureMode, (int)All.DepthComponent);
            
            // Render quad with framebuffer and postprocessing
            postprocessShader.Use();
            GL.BindVertexArray(VAO);
            GL.Disable(EnableCap.DepthTest);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.Enable(EnableCap.DepthTest);

            // Bind framebuffer texture
            outlineShader.SetInt("frameBufferTexture", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, framebufferTexture);

            // Bind stencil texture for outline in fragshader
            outlineShader.SetInt("stencilTexture", 2);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, depthStencilTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.DepthStencilTextureMode, (int)All.StencilIndex);

            // Render quad with framebuffer and added outline
            outlineShader.Use();
            GL.Disable(EnableCap.DepthTest);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.Enable(EnableCap.DepthTest);

            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            //Resize framebuffer textures
            if (viewportSize != previousViewportSize)
            {
                GL.BindTexture(TextureTarget.Texture2D, framebufferTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f, viewportSize.X, viewportSize.Y, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);

                GL.BindTexture(TextureTarget.Texture2D, depthStencilTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8, viewportSize.X, viewportSize.Y, 0, PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);

                OpenTK.Graphics.OpenGL4.ErrorCode error = GL.GetError();
                if (error != OpenTK.Graphics.OpenGL4.ErrorCode.NoError) Console.WriteLine("OpenGL Error: " + error.ToString());

                UpdateMatrices();
                previousViewportSize = viewportSize;
            }

            ImGuiController.Update(this, (float)time);
            ImGui.DockSpaceOverViewport();

            ImGuiWindows.Header();
            ImGuiWindows.SmallStats(viewportSize, viewportPos, fps, ms, count_Meshes, count_PointLights, triangleCount);
            ImGuiWindows.Viewport(framebufferTexture, depthMap, out viewportSize, out viewportPos, out viewportHovered, shadowRes);
            ImGuiWindows.MaterialEditor(ref sceneObjects, ref PBRShader, selectedSceneObject);
            ImGuiWindows.Outliner(ref sceneObjects, ref selectedSceneObject, ref triangleCount);
            ImGuiWindows.ObjectProperties(ref sceneObjects, selectedSceneObject);
            //ImGui.ShowDemoWindow();

            if (IsKeyPressed(Keys.Space))
            {
                SN.Vector2 mousePos = new SN.Vector2(MouseState.Position.X, MouseState.Position.Y) - new SN.Vector2(20, 20);
                ImGui.SetNextWindowPos(mousePos);
                ImGui.OpenPopup("test popup");
            }

            if (ImGui.BeginPopup("test popup", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize))
            {
                ImGui.Text("Add");
                ImGui.Dummy(new System.Numerics.Vector2(0f, 5));
                ImGui.Separator();
                ImGui.Dummy(new System.Numerics.Vector2(0f, 5));

                Random rnd = new Random();
                int randomNum = rnd.Next(1, 101);

                if (ImGui.BeginMenu("Mesh"))
                {
                    if (ImGui.MenuItem("Cube"))
                    {
                        Mesh cube = new Mesh(cubeVertexData, cubeIndices, PBRShader, true, true, mat_monkey);
                        cube.rotation.X = -90;
                        SceneObject _cube = new("Cube" + randomNum, SceneObjectType.Mesh, cube);
                        sceneObjects.Add(_cube);

                        selectedSceneObject = sceneObjects.Count - 1;

                        triangleCount = CalculateTriangles();
                    }

                    ImGui.Dummy(new System.Numerics.Vector2(0f, 5));

                    if (ImGui.MenuItem("Sphere"))
                    {
                        Mesh sphere = new Mesh(sphereVertexData, sphereIndices, PBRShader, true, true, mat_floor);
                        sphere.rotation.X = -90;
                        SceneObject _sphere = new("Sphere" + randomNum, SceneObjectType.Mesh, sphere);
                        sceneObjects.Add(_sphere);

                        selectedSceneObject = sceneObjects.Count - 1;

                        triangleCount = CalculateTriangles();
                    }

                    ImGui.Dummy(new System.Numerics.Vector2(0f, 5));

                    if (ImGui.MenuItem("Plane"))
                    {
                        Mesh plane = new Mesh(planeVertexData, planeIndices, PBRShader, true, true, mat_floor);
                        plane.rotation.X = -90;
                        SceneObject _plane = new("Plane" + randomNum, SceneObjectType.Mesh, plane);
                        sceneObjects.Add(_plane);

                        selectedSceneObject = sceneObjects.Count - 1;

                        triangleCount = CalculateTriangles();
                    }
                    
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Light"))
                {
                    if (ImGui.MenuItem("Point Light"))
                    {
                        Light light = new Light(lightShader, new(1, 1, 1), 5);
                        SceneObject _light = new("Light" + randomNum, SceneObjectType.Light, null, light);
                        sceneObjects.Add(_light);

                        selectedSceneObject = sceneObjects.Count - 1;
                    }

                    ImGui.EndMenu();
                }

                ImGui.Dummy(new System.Numerics.Vector2(0f, 5));
                ImGui.Separator();
                ImGui.Dummy(new System.Numerics.Vector2(0f, 5));

                if (ImGui.Button("Remove Selected") && sceneObjects.Count != 0)
                {
                    sceneObjects[selectedSceneObject].Dispose();
                    sceneObjects.RemoveAt(selectedSceneObject);
                    triangleCount = Game.CalculateTriangles();
                    if (selectedSceneObject != 0) selectedSceneObject -= 1;
                }

                ImGui.EndPopup();
            }

            ImGuiWindows.Settings(ref vsyncOn, ref ShowDepth_Stencil, ref shadowRes, ref depthMap, ref direction, ref ambient, ref shadowFactor, ref PBRShader, ref postprocessShader, ref outlineShader);
            VSync = vsyncOn ? VSyncMode.On : VSyncMode.Off;

            ImGuiController.Render();

            SwapBuffers();
        }

        public static int CalculateTriangles()
        {
            int count = 0;
            foreach (SceneObject sceneObject in sceneObjects) if (sceneObject.Type == SceneObjectType.Mesh) count += sceneObject.Mesh.vertexCount / 3;
            
            return count;
        }

        public void FocusObject()
        {
            Vector3 targetPosition = suzanne.position;
            Vector3 direction = Vector3.Normalize(targetPosition - camera.position);

            camera.direction = direction;
        }

        public void UpdateMatrices()
        {
            float aspectRatio = (float)viewportSize.X / viewportSize.Y;
            Matrix4 lightSpaceMatrix = Matrix4.LookAt(direction * 10, new(0, 0, 0), Vector3.UnitY) * Matrix4.CreateOrthographicOffCenter(-10, 10, -10, 10, 0.1f, 100);
            
            if (!renderShadowMap)
            {
                projectionMatrix = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(75), aspectRatio, 0.1f, 100);
                viewMatrix = Matrix4.LookAt(camera.position, camera.position + camera.direction, Vector3.UnitY);
                PBRShader.SetMatrix4("projection", projectionMatrix);
                PBRShader.SetMatrix4("view", viewMatrix);
                PBRShader.SetMatrix4("lightSpaceMatrix", lightSpaceMatrix);
                lightShader.SetMatrix4("projection", projectionMatrix);
                lightShader.SetMatrix4("view", viewMatrix);
            }
            else
            {
                shadowShader.SetMatrix4("lightSpaceMatrix", lightSpaceMatrix);
            }
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (viewportHovered)
            {
                if (e.Key == Keys.F) FocusObject();

                if (e.Key == Keys.Escape) Close();
                if (e.Key == Keys.D1)
                {
                    GL.Enable(EnableCap.CullFace);
                    _polygonMode = PolygonMode.Fill;
                }
                if (e.Key == Keys.D2)
                {
                    GL.Disable(EnableCap.CullFace);
                    _polygonMode = PolygonMode.Line;
                }
                if (e.Key == Keys.D3)
                {
                    GL.Disable(EnableCap.CullFace);
                    _polygonMode = PolygonMode.Point;
                }
            }
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            ImGuiController.PressChar((char)e.Unicode);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            ImGuiController.MouseScroll(e.Offset);
        }
    }
}