using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

public sealed class CarOrbitScene : Scene
{
    private const string ToggleAuto = "mod automat / manual";
    private const float TrackHalfWidth = 5.0f;
    private const float WheelRadius = 0.35f;

    private SceneObject _carPivot = null!;
    private SceneObject _orbitPivot = null!;
    private SceneObject _orbitSphere = null!;
    private readonly List<SceneObject> _wheelPivots = new();

    private Tween _moveTween = null!;
    private bool _autoMove = true;
    private float _carX = -TrackHalfWidth;

    public override string Name => "mașină și orbită";

    public override string Status => $"{(_autoMove ? "automat" : "manual")} | X = {_carX:F2}";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left,
        Actions.Right,
        ToggleAuto,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 5.5f, 11.5f);
        CameraTarget = new Vector3(0.0f, 0.8f, 0.0f);

        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(ToggleAuto, Keys.R);

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 32, 16));
        Mesh wheelMesh = AddMesh(Shapes.ShadedCylinder(WheelRadius, 0.22f, 24));

        // Podeaua și șoseaua
        AddObject("podea", grid);

        SceneObject road = AddObject("șosea", cube);
        road.Transform.Position = new Vector3(0.0f, 0.02f, 0.0f);
        road.Transform.Scale = new Vector3(13.5f, 0.04f, 3.2f);
        road.Tint = new Color4(0.20f, 0.22f, 0.26f, 1.0f);

        SceneObject stripe = AddObject("marcaj", cube);
        stripe.Transform.Position = new Vector3(0.0f, 0.045f, 0.0f);
        stripe.Transform.Scale = new Vector3(13.0f, 0.01f, 0.14f);
        stripe.Tint = new Color4(0.95f, 0.85f, 0.25f, 1.0f);

        // 1. Pivotul principal al mașinii (se deplasează stânga-dreapta pe X)
        _carPivot = AddObject("pivot mașină", null);
        _carPivot.Transform.Position = new Vector3(_carX, 0.04f, 0.0f);

        // Șasiul (copil al lui _carPivot)
        SceneObject chassis = AddObject("șasiu", cube, _carPivot);
        chassis.Transform.Position = new Vector3(0.0f, 0.55f, 0.0f);
        chassis.Transform.Scale = new Vector3(2.4f, 0.5f, 1.2f);
        chassis.Tint = new Color4(0.85f, 0.22f, 0.22f, 1.0f);

        // Cabina (copil al lui _carPivot)
        SceneObject cabin = AddObject("cabină", cube, _carPivot);
        cabin.Transform.Position = new Vector3(-0.15f, 1.0f, 0.0f);
        cabin.Transform.Scale = new Vector3(1.2f, 0.45f, 1.0f);
        cabin.Tint = new Color4(0.28f, 0.65f, 0.95f, 1.0f);

        // Faruri față (orientate spre +X)
        foreach (float z in new[] { -0.4f, 0.4f })
        {
            SceneObject headlight = AddObject("far", cube, _carPivot);
            headlight.Transform.Position = new Vector3(1.21f, 0.55f, z);
            headlight.Transform.Scale = new Vector3(0.06f, 0.16f, 0.22f);
            headlight.Tint = new Color4(1.0f, 0.95f, 0.6f, 1.0f);
        }

        // Cele 4 roți cilindrice (fiecare cu pivot pentru rotația în jurul axei Z)
        Vector3[] wheelOffsets =
        {
            new(-0.75f, WheelRadius,  0.66f),
            new( 0.75f, WheelRadius,  0.66f),
            new(-0.75f, WheelRadius, -0.66f),
            new( 0.75f, WheelRadius, -0.66f),
        };

        _wheelPivots.Clear();
        for (int i = 0; i < wheelOffsets.Length; i++)
        {
            SceneObject wheelPivot = AddObject($"pivot roată {i}", null, _carPivot);
            wheelPivot.Transform.Position = wheelOffsets[i];
            _wheelPivots.Add(wheelPivot);

            SceneObject wheel = AddObject($"roată {i}", wheelMesh, wheelPivot);
            wheel.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f); // așază axul cilindrului pe Z
            wheel.Tint = new Color4(0.12f, 0.12f, 0.14f, 1.0f);
        }

        // 2. Pivotul orbitei (copil al mașinii -> o urmărește și se rotește continuu pe Y)
        _orbitPivot = AddObject("pivot orbită", null, _carPivot);
        _orbitPivot.Transform.Position = new Vector3(0.0f, 0.85f, 0.0f);
        _orbitPivot.Spin = new Vector3(0.0f, 150.0f, 0.0f);

        // 3. Sfera care orbitează în jurul mașinii (copil al lui _orbitPivot)
        _orbitSphere = AddObject("sferă orbită", sphere, _orbitPivot);
        _orbitSphere.Transform.Position = new Vector3(2.2f, 0.0f, 0.0f);
        _orbitSphere.Transform.Scale = new Vector3(0.4f);
        _orbitSphere.Tint = new Color4(1.0f, 0.75f, 0.15f, 1.0f);

        // Animația stânga-dreapta (dus-întors)
        _moveTween = new Tween(
            from: new Vector3(-TrackHalfWidth, 0.04f, 0.0f),
            to: new Vector3(TrackHalfWidth, 0.04f, 0.0f),
            duration: 3.0f,
            ease: Easing.EaseInOut,
            pingPong: true
        );
    }

    public override void Update(float dt, Input input)
    {
        if (input.WasPressed(ToggleAuto))
        {
            _autoMove = !_autoMove;
            if (_autoMove) _moveTween.Restart();
        }

        float prevX = _carX;
        float axis = input.Axis(Actions.Left, Actions.Right);

        // Apăsarea A/D sau Săgeți trece pe control manual; R revine la automat
        if (MathF.Abs(axis) > 0.01f)
        {
            _autoMove = false;
            _carX = Math.Clamp(_carX + axis * 5.0f * dt, -TrackHalfWidth, TrackHalfWidth);
        }
        else if (_autoMove)
        {
            _moveTween.Update(dt);
            _carX = _moveTween.Value.X;
        }

        _carPivot.Transform.Position = new Vector3(_carX, 0.04f, 0.0f);

        // Învârtirea roților proporțional cu deplasarea pe X
        float dx = _carX - prevX;
        float wheelDeg = -(dx / WheelRadius) * (180.0f / MathF.PI);
        foreach (SceneObject wheelPivot in _wheelPivots)
        {
            wheelPivot.Transform.Rotation += new Vector3(0.0f, 0.0f, wheelDeg);
        }

        // Actualizează Spin-ul pentru _orbitPivot
        base.Update(dt, input);
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Mod deplasare  : {(_autoMove ? "automat (Tween)" : "manual (A/D sau Săgeți)")}";
        yield return $"Poziție mașină : X = {_carX:F2}";
        yield return $"Unghi orbită   : {_orbitPivot.Transform.Rotation.Y % 360.0f:F0}°";
    }
}