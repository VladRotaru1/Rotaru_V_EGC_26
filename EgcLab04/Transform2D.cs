using OpenTK.Mathematics;

namespace Egc;

/// <summary>
/// Poziția, rotația și scalarea unui obiect din scenă. Din ele se construiește matricea model,
/// care duce vârfurile obiectului din object space în world space (secțiunea 5).
/// </summary>
public class Transform2D
{
    public Vector2 Position = Vector2.Zero;   // translația, în unități de lume
    public float Rotation = 0.0f;             // unghiul, în GRADE, pozitiv = sens trigonometric
    public Vector2 Scale = Vector2.One;       // factorii de scalare pe x și pe y
    public float Depth = 0.0f;                // coordonata z; contează doar în perspectivă (secțiunea 7.2)

    public Matrix4 ModelMatrix =>
        // TODO (T1): scalare, apoi rotație, apoi translație
        Matrix4.CreateScale(Scale.X, Scale.Y, 1.0f) *
        Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation)) *
        Matrix4.CreateTranslation(Position.X, Position.Y, Depth);
    // T1: Dacă am aplica translația înaintea rotației, obiectul s-ar muta mai întâi la coordonatele de translație (-1.2, 1.0).
    // Rotația de 45 de grade s-ar aplica ulterior în jurul originii lumii (0,0), nu în jurul centrului obiectului.
    // Astfel, obiectul ar orbita în jurul originii, ajungând într-o poziție finală greșită pe ecran.


    /// <summary>O copie independentă (folosită pentru readucerea scenei în starea inițială).</summary>
    public Transform2D Clone() => (Transform2D)MemberwiseClone();
}
