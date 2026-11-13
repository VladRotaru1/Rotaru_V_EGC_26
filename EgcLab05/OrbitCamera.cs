using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Cameră orbită: privește mereu spre un punct țintă (Target) și se deplasează pe o sferă
/// în jurul acestuia, definită prin distanță (rază), azimut și elevație.
/// </summary>
public class OrbitCamera : Camera
{
    public Vector3 Target { get; set; }
    public float Distance { get; private set; }
    public float Azimuth { get; private set; }     // grade, în jurul axei Y
    public float Elevation { get; private set; }   // grade, față de planul orizontal

    public const float MaxElevation = 89.0f;
    public const float MinDistance = 1.0f;
    public const float MaxDistance = 40.0f;

    public float MouseSensitivity = 0.2f;
    public float KeyRotateSpeed = 60.0f;           // grade / secundă pentru săgeți
    public float ZoomStep = 0.8f;
    public float MoveSpeed = 4.0f;                 // pentru deplasarea țintei cu W A S D
    public float SprintFactor = 2.5f;

    private static readonly Vector3 StartTarget = new(0.0f, 1.0f, -1.0f);
    private const float StartDistance = 7.0f;
    private const float StartAzimuth = 90.0f;
    private const float StartElevation = 20.0f;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "Orbită";

    public override Vector3 Position
    {
        get
        {
            float azRad = MathHelper.DegreesToRadians(Azimuth);
            float elRad = MathHelper.DegreesToRadians(Elevation);
            float cosEl = MathF.Cos(elRad);

            Vector3 offset = new(
                cosEl * MathF.Cos(azRad),
                MathF.Sin(elRad),
                cosEl * MathF.Sin(azRad)
            );
            return Target + Distance * offset;
        }
    }

    public override Vector3 Front => Vector3.Normalize(Target - Position);

    public override Matrix4 ViewMatrix =>
        Matrix4.LookAt(Position, Target, Vector3.UnitY);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // 1. Rotire cu clic stânga ținut apăsat + mișcarea mouse-ului
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity;
        }

        // 2. Rotire de la tastatură cu tastele săgeți
        if (keyboard.IsKeyDown(Keys.Left)) Azimuth += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth -= KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Up)) Elevation += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Down)) Elevation -= KeyRotateSpeed * dt;

        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);

        // 3. Rotița mouse-ului modifică distanța (zoom), nu FOV-ul
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            Distance = Math.Clamp(Distance - mouse.ScrollDelta.Y * ZoomStep, MinDistance, MaxDistance);
        }

        // 4. Bonus 2: W A S D mută ținta în planul orizontal
        Vector3 forward = new(Front.X, 0.0f, Front.Z);
        if (forward.LengthSquared > 1e-6f)
        {
            forward = Vector3.Normalize(forward);
        }
        else
        {
            float azRad = MathHelper.DegreesToRadians(Azimuth);
            forward = new Vector3(-MathF.Cos(azRad), 0.0f, -MathF.Sin(azRad));
        }

        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += forward;
        if (keyboard.IsKeyDown(Keys.S)) move -= forward;
        if (keyboard.IsKeyDown(Keys.D)) move += right;
        if (keyboard.IsKeyDown(Keys.A)) move -= right;

        if (move.LengthSquared > 0.0f)
        {
            move = Vector3.Normalize(move);
            float speed = MoveSpeed * (keyboard.IsKeyDown(Keys.LeftShift) ? SprintFactor : 1.0f);
            Target += move * speed * dt;
        }
    }

    /// <summary>
    /// Bonus 1 (FPS -> Orbită): poziționează camera orbită astfel încât să păstreze
    /// exact poziția și orientarea camerei FPS, cu ținta la <paramref name="distance"/> în față.
    /// </summary>
    public void SetFromView(Vector3 cameraPosition, Vector3 cameraFront, float distance = 5.0f)
    {
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        Vector3 dir = Vector3.Normalize(cameraFront);
        Target = cameraPosition + dir * Distance;

        Vector3 o = -dir; // vectorul unitar țintă -> cameră
        Elevation = Math.Clamp(
            MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(o.Y, -1.0f, 1.0f))),
            -MaxElevation,
            MaxElevation
        );
        Azimuth = MathHelper.RadiansToDegrees(MathF.Atan2(o.Z, o.X));
    }

    public override void Reset()
    {
        Target = StartTarget;
        Distance = StartDistance;
        Azimuth = StartAzimuth;
        Elevation = StartElevation;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 pos = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanță       : {Distance,6:F2}";
        yield return $"Azimut / elev. : {NormalizeAngle(Azimuth),7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({pos.X,6:F2}, {pos.Y,6:F2}, {pos.Z,6:F2})";
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f) degrees -= 360.0f;
        if (degrees <= -180.0f) degrees += 360.0f;
        return degrees;
    }
}