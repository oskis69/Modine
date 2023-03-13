using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

using Modine.Common;

namespace Modine.Rendering
{
    public struct VertexData
    {
        public Vector3 Position;
        public Vector3 Normals;
        public Vector2 UVs;

        public VertexData(Vector3 position, Vector3 normals, Vector2 uvs)
        {
            this.Position = position;
            this.Normals = normals;
            this.UVs = uvs;
        }
    }

    public class Mesh : SceneObject
    {
        private int vaoHandle;
        private int vboHandle;
        private int eboHandle;
        public int vertexCount;
        public bool smoothShading;
        public bool castShadow;
        public string meshName;
        public Material Material;
        public Shader meshShader;

        public Vector3 position = Vector3.Zero;
        public Vector3 rotation = Vector3.Zero;
        public Vector3 scale = Vector3.One;

        public Mesh(VertexData[] vertData, int[] indices, Shader shader, bool SmoothShading, bool CastShadow, Material material) : base()
        {
            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertData.Length * 8 * sizeof(float), vertData, BufferUsageHint.StaticDraw);

            eboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, eboHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(shader.GetAttribLocation("aPosition"));
            GL.VertexAttribPointer(shader.GetAttribLocation("aPosition"), 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
            GL.EnableVertexAttribArray(shader.GetAttribLocation("aNormals"));
            GL.VertexAttribPointer(shader.GetAttribLocation("aNormals"), 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(shader.GetAttribLocation("aUV"));
            GL.VertexAttribPointer(shader.GetAttribLocation("aUV"), 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));
            
            meshName = Name;
            meshShader = shader;
            vertexCount = indices.Length;
            smoothShading = SmoothShading;
            castShadow = CastShadow;

            Material = material;
            meshShader.SetInt("smoothShading", Convert.ToInt32(SmoothShading));

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }

        public void Render()
        {   
            Matrix4 model = Matrix4.Identity;
            model *= Matrix4.CreateScale(scale);
            model *= Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotation.X)) *
                     Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotation.Y)) *
                     Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotation.Z));
            model *= Matrix4.CreateTranslation(position);

            meshShader.SetMatrix4("model", model);

            GL.BindVertexArray(vaoHandle);

            if (vertexCount > 0)
            {
                if (eboHandle > 0) GL.DrawElements(PrimitiveType.Triangles, vertexCount, DrawElementsType.UnsignedInt, 0);
                else GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);
            }

            GL.BindVertexArray(0);
        }

        public override void Dispose()
        {
            GL.DeleteVertexArray(vaoHandle);
            GL.DeleteBuffer(vboHandle);
        }
    }
}