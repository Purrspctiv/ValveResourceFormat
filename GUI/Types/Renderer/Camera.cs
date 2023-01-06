using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.WinForms;

namespace GUI.Types.Renderer
{
    internal class Camera
    {
        private const float CAMERASPEED = 300f; // Per second
        private const float FOV = OpenTK.Mathematics.MathHelper.PiOver4;

        public Vector3 Location { get; private set; }
        public float Pitch { get; private set; }
        public float Yaw { get; private set; }
        public float Scale { get; private set; } = 1.0f;

        private Matrix4x4 ProjectionMatrix;
        public Matrix4x4 CameraViewMatrix { get; private set; }
        public Matrix4x4 ViewProjectionMatrix { get; private set; }
        public Frustum ViewFrustum { get; } = new Frustum();

        // Set from outside this class by forms code
        public bool MouseOverRenderArea { get; set; }

        private Vector2 WindowSize;
        private float AspectRatio;

        private bool MouseDragging;

        private Vector2 MouseDelta;
        private Vector2 MousePreviousPosition;

        private INativeInput NativeInput;

        public Camera()
        {
            Location = new Vector3(1);
            LookAt(new Vector3(0));
        }

        private void RecalculateMatrices()
        {
            CameraViewMatrix = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateLookAt(Location, Location + GetForwardVector(), Vector3.UnitZ);
            ViewProjectionMatrix = CameraViewMatrix * ProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        // Calculate forward vector from pitch and yaw
        private Vector3 GetForwardVector()
        {
            return new Vector3((float)(Math.Cos(Yaw) * Math.Cos(Pitch)), (float)(Math.Sin(Yaw) * Math.Cos(Pitch)), (float)Math.Sin(Pitch));
        }

        private Vector3 GetRightVector()
        {
            return new Vector3((float)Math.Cos(Yaw - OpenTK.Mathematics.MathHelper.PiOver2), (float)Math.Sin(Yaw - OpenTK.Mathematics.MathHelper.PiOver2), 0);
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            // Store window size and aspect ratio
            AspectRatio = viewportWidth / (float)viewportHeight;
            WindowSize = new Vector2(viewportWidth, viewportHeight);

            // Calculate projection matrix
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(FOV, AspectRatio, 1.0f, 40000.0f);

            RecalculateMatrices();

            // setup viewport
            GL.Viewport(0, 0, viewportWidth, viewportHeight);
        }

        public void CopyFrom(Camera fromOther)
        {
            AspectRatio = fromOther.AspectRatio;
            WindowSize = fromOther.WindowSize;
            Location = fromOther.Location;
            Pitch = fromOther.Pitch;
            Yaw = fromOther.Yaw;
            ProjectionMatrix = fromOther.ProjectionMatrix;
            CameraViewMatrix = fromOther.CameraViewMatrix;
            ViewProjectionMatrix = fromOther.ViewProjectionMatrix;
            ViewFrustum.Update(ViewProjectionMatrix);
        }

        public void SetLocation(Vector3 location)
        {
            Location = location;
            RecalculateMatrices();
        }

        public void SetLocationPitchYaw(Vector3 location, float pitch, float yaw)
        {
            Location = location;
            Pitch = pitch;
            Yaw = yaw;
            RecalculateMatrices();
        }

        public void LookAt(Vector3 target)
        {
            var dir = Vector3.Normalize(target - Location);
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

            ClampRotation();
            RecalculateMatrices();
        }

        public void SetFromTransformMatrix(Matrix4x4 matrix)
        {
            Location = matrix.Translation;

            // Extract view direction from view matrix and use it to calculate pitch and yaw
            var dir = new Vector3(matrix.M11, matrix.M12, matrix.M13);
            Yaw = (float)Math.Atan2(dir.Y, dir.X);
            Pitch = (float)Math.Asin(dir.Z);

            RecalculateMatrices();
        }

        public void SetScale(float scale)
        {
            Scale = scale;
            RecalculateMatrices();
        }

        public void Tick(float deltaTime)
        {
            if (!MouseOverRenderArea)
            {
                return;
            }

            // Use the keyboard state to update position
            HandleKeyboardInput(deltaTime);

            // Full width of the screen is a 1 PI (180deg)
            Yaw -= (float)Math.PI * MouseDelta.X / WindowSize.X;
            Pitch -= (float)Math.PI / AspectRatio * MouseDelta.Y / WindowSize.Y;

            ClampRotation();

            RecalculateMatrices();
        }

        public void HandleInput(INativeInput nativeInput)
        {
            NativeInput = nativeInput;

            if (MouseOverRenderArea && nativeInput.IsMouseButtonDown(MouseButton.Left))
            {
                if (!MouseDragging)
                {
                    MouseDragging = true;
                    MousePreviousPosition = new Vector2(nativeInput.MousePosition.X, nativeInput.MousePosition.Y);
                }

                var mouseNewCoords = new Vector2(nativeInput.MousePosition.X, nativeInput.MousePosition.Y);

                MouseDelta.X = mouseNewCoords.X - MousePreviousPosition.X;
                MouseDelta.Y = mouseNewCoords.Y - MousePreviousPosition.Y;

                MousePreviousPosition = mouseNewCoords;
            }

            if (!MouseOverRenderArea || !nativeInput.IsMouseButtonDown(MouseButton.Left))
            {
                MouseDragging = false;
                MouseDelta = default;
            }
        }

        private void HandleKeyboardInput(float deltaTime)
        {
            var speed = CAMERASPEED * deltaTime;

            // Double speed if shift is pressed
            if (NativeInput.IsKeyDown(Keys.LeftShift))
            {
                speed *= 2;
            }
            else if (NativeInput.IsKeyDown(Keys.F))
            {
                speed *= 10;
            }

            if (NativeInput.IsKeyDown(Keys.W))
            {
                Location += GetForwardVector() * speed;
            }

            if (NativeInput.IsKeyDown(Keys.S))
            {
                Location -= GetForwardVector() * speed;
            }

            if (NativeInput.IsKeyDown(Keys.D))
            {
                Location += GetRightVector() * speed;
            }

            if (NativeInput.IsKeyDown(Keys.A))
            {
                Location -= GetRightVector() * speed;
            }

            if (NativeInput.IsKeyDown(Keys.Z))
            {
                Location += new Vector3(0, 0, -speed);
            }

            if (NativeInput.IsKeyDown(Keys.Q))
            {
                Location += new Vector3(0, 0, speed);
            }
        }

        // Prevent camera from going upside-down
        private void ClampRotation()
        {
            if (Pitch >= OpenTK.Mathematics.MathHelper.PiOver2)
            {
                Pitch = OpenTK.Mathematics.MathHelper.PiOver2 - 0.001f;
            }
            else if (Pitch <= -OpenTK.Mathematics.MathHelper.PiOver2)
            {
                Pitch = -OpenTK.Mathematics.MathHelper.PiOver2 + 0.001f;
            }
        }
    }
}
