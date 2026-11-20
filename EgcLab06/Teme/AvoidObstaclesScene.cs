using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema acasă - Varianta B: Evită obstacolele.
/// </summary>
public sealed class AvoidObstaclesScene : Scene
{
    private const string RestartGame = "joc nou";

    // Geometria drumului și a celor 3 benzi (0 = stânga, 1 = centru, 2 = dreapta)
    private const int LaneCount = 3;
    private const float LaneWidth = 2.4f;
    private const float RoadLength = 52.0f;
    private const float RoadStartZ = -38.0f;
    private const float RoadEndZ = 14.0f;

    // Parametrii jucătorului și ai fizicii
    private const float PlayerZ = 3.0f;
    private const float PlayerHalfX = 0.40f;
    private const float PlayerHalfY = 0.60f;
    private const float PlayerHalfZ = 0.40f;
    private const float GroundCenterY = PlayerHalfY;
    private const float Gravity = 28.0f;
    private const float JumpSpeed = 11.2f;       // înălțime maximă = v² / (2g) ≈ 2.24
    private const float LaneSwitchDuration = 0.15f; // < 0.2 s (EaseOut)

    // Parametrii lumii și ai obstacolelor
    private const float BaseSpeed = 11.0f;
    private const float MaxSpeed = 28.0f;
    private const float SpawnZ = -36.0f;
    private const float DespawnZ = 10.0f;

    private sealed class ObstacleSlot
    {
        public SceneObject Obj = null!;
        public bool Active;
        public bool IsHigh;
        public Vector3 HalfExtents;
    }

    private readonly Random _rng = new();
    private readonly List<SceneObject> _roadDashes = new();
    private readonly List<ObstacleSlot> _obstaclePool = new();

    // Ierarhia părinte-copil a jucătorului
    private SceneObject _playerPivot = null!;
    private SceneObject _playerBody = null!;
    private SceneObject _playerHead = null!;
    private SceneObject _dronePivot = null!;
    private SceneObject _playerShadow = null!;

    // Starea curentă a jocului
    private Tween _laneTween = null!;
    private int _currentLane = 1;
    private float _playerY = GroundCenterY;
    private float _verticalSpeed;

    private float _worldSpeed = BaseSpeed;
    private float _spawnTimer;
    private float _survivalTime;
    private float _bestTime;
    private bool _gameOver;

    public override string Name => "evită obstacolele";

    public override string Status => _gameOver
        ? $"COLIZIUNE! Scor: {_survivalTime:F1} s | Record: {_bestTime:F1} s | R = joc nou"
        : $"Scor: {_survivalTime:F1} s | Record: {_bestTime:F1} s | Viteză: {_worldSpeed:F1} u/s";

    public override IEnumerable<string> ActionNames => new[]
    {
        Actions.Left,
        Actions.Right,
        Actions.Jump,
        RestartGame,
    };

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 4.6f, 9.2f);
        CameraTarget = new Vector3(0.0f, 1.0f, -6.0f);

        input.Bind(Actions.Left, Keys.A, Keys.Left);
        input.Bind(Actions.Right, Keys.D, Keys.Right);
        input.Bind(Actions.Jump, Keys.Space);
        input.Bind(RestartGame, Keys.R);

        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 24, 12));
        Mesh disk = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        BuildRoad(cube);
        BuildPlayer(cube, sphere, disk);
        BuildObstaclePool(cube);

        _laneTween = new Tween(Vector3.Zero, Vector3.Zero, LaneSwitchDuration, Easing.EaseOut);
        ResetGame(announce: false);
    }

    private void BuildRoad(Mesh cube)
    {
        float roadCenterZ = (RoadStartZ + RoadEndZ) * 0.5f;

        // Asfaltul (suprafața superioară la Y = 0)
        SceneObject road = AddObject("drum", cube);
        road.Transform.Position = new Vector3(0.0f, -0.05f, roadCenterZ);
        road.Transform.Scale = new Vector3(LaneCount * LaneWidth + 0.8f, 0.1f, RoadLength);
        road.Tint = new Color4(0.18f, 0.20f, 0.24f, 1.0f);

        // Bordurile stânga / dreapta
        float borderX = (LaneCount * LaneWidth + 0.8f) * 0.5f + 0.15f;
        foreach (float side in new[] { -1.0f, 1.0f })
        {
            SceneObject curb = AddObject("bordură", cube);
            curb.Transform.Position = new Vector3(side * borderX, 0.08f, roadCenterZ);
            curb.Transform.Scale = new Vector3(0.3f, 0.16f, RoadLength);
            curb.Tint = new Color4(0.35f, 0.38f, 0.44f, 1.0f);
        }

        // Bonus: marcaje discontinue între cele 3 benzi (se mișcă odată cu lumea)
        _roadDashes.Clear();
        const int dashesPerLine = 13;
        const float dashSpacing = 4.0f;
        foreach (float laneDividerX in new[] { -LaneWidth * 0.5f, LaneWidth * 0.5f })
        {
            for (int i = 0; i < dashesPerLine; i++)
            {
                SceneObject dash = AddObject("marcaj", cube);
                dash.Transform.Position = new Vector3(laneDividerX, 0.005f, RoadStartZ + i * dashSpacing);
                dash.Transform.Scale = new Vector3(0.14f, 0.01f, 1.8f);
                dash.Tint = new Color4(0.92f, 0.92f, 0.95f, 1.0f);
                _roadDashes.Add(dash);
            }
        }
    }

    private void BuildPlayer(Mesh cube, Mesh sphere, Mesh disk)
    {
        // Umbra de pe asfalt
        _playerShadow = AddObject("umbră jucător", disk);
        _playerShadow.Tint = new Color4(0.05f, 0.05f, 0.08f, 1.0f);

        // Pivotul jucătorului (la nivelul tălpilor)
        _playerPivot = AddObject("pivot jucător", null);

        // Corpul (copil al pivotului)
        _playerBody = AddObject("corp jucător", cube, _playerPivot);
        _playerBody.Transform.Position = new Vector3(0.0f, 0.48f, 0.0f);
        _playerBody.Transform.Scale = new Vector3(0.82f, 0.96f, 0.82f);

        // Capul / vizorul (copil al pivotului)
        _playerHead = AddObject("cap jucător", cube, _playerPivot);
        _playerHead.Transform.Position = new Vector3(0.0f, 1.08f, 0.0f);
        _playerHead.Transform.Scale = new Vector3(0.50f, 0.28f, 0.50f);
        _playerHead.Tint = new Color4(0.85f, 0.95f, 1.0f, 1.0f);

        // Satelit care orbitează deasupra jucătorului (ierarhie pe 2 niveluri)
        _dronePivot = AddObject("pivot satelit", null, _playerPivot);
        _dronePivot.Transform.Position = new Vector3(0.0f, 1.45f, 0.0f);
        _dronePivot.Spin = new Vector3(0.0f, 220.0f, 0.0f);

        SceneObject drone = AddObject("satelit", sphere, _dronePivot);
        drone.Transform.Position = new Vector3(0.42f, 0.0f, 0.0f);
        drone.Transform.Scale = new Vector3(0.11f);
        drone.Tint = new Color4(0.3f, 0.95f, 0.85f, 1.0f);
    }

    private void BuildObstaclePool(Mesh cube)
    {
        _obstaclePool.Clear();
        for (int i = 0; i < 24; i++)
        {
            SceneObject obj = AddObject($"obstacol {i}", cube);
            obj.Visible = false;
            _obstaclePool.Add(new ObstacleSlot { Obj = obj });
        }
    }

    private void ResetGame(bool announce)
    {
        _currentLane = 1;
        float startX = LaneToX(_currentLane);
        _laneTween = new Tween(new Vector3(startX, 0.0f, 0.0f), new Vector3(startX, 0.0f, 0.0f),
                               LaneSwitchDuration, Easing.EaseOut);

        _playerY = GroundCenterY;
        _verticalSpeed = 0.0f;
        _worldSpeed = BaseSpeed;
        _spawnTimer = 0.35f;
        _survivalTime = 0.0f;
        _gameOver = false;

        _playerBody.Tint = new Color4(0.22f, 0.65f, 1.0f, 1.0f);

        foreach (ObstacleSlot slot in _obstaclePool)
        {
            slot.Active = false;
            slot.Obj.Visible = false;
        }

        UpdatePlayerVisuals(startX);

        if (announce)
        {
            ConsolePanel.PrintMessage("joc nou pornit");
        }
    }

    public override void Update(float dt, Input input)
    {
        if (input.WasPressed(RestartGame))
        {
            ResetGame(announce: true);
            return;
        }

        if (_gameOver)
        {
            return;
        }

        // Actualizează Spin-ul obiectelor (inclusiv satelitul jucătorului) folosind dt
        base.Update(dt, input);

        // Creșterea timpului supraviețuit, a vitezei lumii și a frecvenței obstacolelor
        _survivalTime += dt;
        _worldSpeed = MathF.Min(BaseSpeed + _survivalTime * 0.45f, MaxSpeed);

        // 1. Schimbarea benzii (Tween cu EaseOut, 0.15 s)
        if (input.WasPressed(Actions.Left) && _currentLane > 0)
        {
            _currentLane--;
            _laneTween.GoTo(new Vector3(LaneToX(_currentLane), 0.0f, 0.0f), LaneSwitchDuration);
        }
        if (input.WasPressed(Actions.Right) && _currentLane < LaneCount - 1)
        {
            _currentLane++;
            _laneTween.GoTo(new Vector3(LaneToX(_currentLane), 0.0f, 0.0f), LaneSwitchDuration);
        }
        _laneTween.Update(dt);
        float playerX = _laneTween.Value.X;

        // 2. Saltul cu gravitație
        bool onGround = _playerY <= GroundCenterY + 1e-4f;
        if (onGround && input.WasPressed(Actions.Jump))
        {
            _verticalSpeed = JumpSpeed;
        }

        if (!onGround || _verticalSpeed > 0.0f)
        {
            _verticalSpeed -= Gravity * dt;
            _playerY += _verticalSpeed * dt;
            if (_playerY <= GroundCenterY)
            {
                _playerY = GroundCenterY;
                _verticalSpeed = 0.0f;
            }
        }

        UpdatePlayerVisuals(playerX);

        // 3. Deplasarea marcajelor de pe drum (bonus)
        const float wrapLength = 13 * 4.0f;
        foreach (SceneObject dash in _roadDashes)
        {
            Vector3 p = dash.Transform.Position;
            p.Z += _worldSpeed * dt;
            if (p.Z > RoadEndZ)
            {
                p.Z -= wrapLength;
            }
            dash.Transform.Position = p;
        }

        // 4. Generarea valurilor de obstacole (frecvența crește în timp)
        _spawnTimer -= dt;
        if (_spawnTimer <= 0.0f)
        {
            SpawnWave();
            float interval = MathF.Max(0.55f, 1.35f - _survivalTime * 0.022f);
            _spawnTimer = interval;
        }

        // 5. Deplasarea obstacolelor și testul de coliziune AABB
        var playerCenter = new Vector3(playerX, _playerY, PlayerZ);
        var playerHalf = new Vector3(PlayerHalfX, PlayerHalfY, PlayerHalfZ);

        foreach (ObstacleSlot slot in _obstaclePool)
        {
            if (!slot.Active) continue;

            Vector3 pos = slot.Obj.Transform.Position;
            pos.Z += _worldSpeed * dt;
            slot.Obj.Transform.Position = pos;

            if (pos.Z > DespawnZ)
            {
                slot.Active = false;
                slot.Obj.Visible = false;
                continue;
            }

            if (IntersectsAabb(playerCenter, playerHalf, pos, slot.HalfExtents))
            {
                _gameOver = true;
                _bestTime = MathF.Max(_bestTime, _survivalTime);
                _playerBody.Tint = new Color4(0.95f, 0.25f, 0.25f, 1.0f);
                ConsolePanel.PrintMessage($"coliziune la {_survivalTime:F1} s (record: {_bestTime:F1} s) — apasă R");
                break;
            }
        }
    }

    private void SpawnWave()
    {
        // Cel puțin o bandă rămâne liberă pe fiecare val, astfel încât traseul să fie mereu posibil
        int safeLane = _rng.Next(LaneCount);
        bool doubleObstacle = _survivalTime > 6.0f && _rng.NextDouble() < 0.55;
        int skippedLane = doubleObstacle ? -1 : _rng.Next(LaneCount - 1);

        int candidateIndex = 0;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (lane == safeLane) continue;
            if (!doubleObstacle && candidateIndex++ == skippedLane) continue;

            bool isHigh = _rng.NextDouble() < 0.5;
            SpawnObstacleOnLane(lane, isHigh);
        }
    }

    private void SpawnObstacleOnLane(int lane, bool isHigh)
    {
        ObstacleSlot? slot = _obstaclePool.FirstOrDefault(s => !s.Active);
        if (slot == null) return;

        float width = 1.75f;
        float height = isHigh ? 2.60f : 0.85f;
        float depth = isHigh ? 1.00f : 0.90f;

        slot.Active = true;
        slot.IsHigh = isHigh;
        slot.HalfExtents = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f);

        slot.Obj.Visible = true;
        slot.Obj.Transform.Position = new Vector3(LaneToX(lane), height * 0.5f, SpawnZ);
        slot.Obj.Transform.Scale = new Vector3(width, height, depth);
        slot.Obj.Tint = isHigh
            ? new Color4(0.88f, 0.20f, 0.25f, 1.0f)  // înalt (roșu) -> se ocolește
            : new Color4(0.95f, 0.58f, 0.15f, 1.0f); // jos (portocaliu) -> se poate sări
    }

    private void UpdatePlayerVisuals(float playerX)
    {
        float footY = _playerY - GroundCenterY;
        _playerPivot.Transform.Position = new Vector3(playerX, footY, PlayerZ);

        float shadowScale = 1.0f / (1.0f + 0.35f * footY);
        _playerShadow.Transform.Position = new Vector3(playerX, 0.01f, PlayerZ);
        _playerShadow.Transform.Scale = new Vector3(shadowScale, 1.0f, shadowScale);
    }

    /// <summary>Test de suprapunere între două cutii aliniate cu axele (AABB) pe toate cele 3 axe.</summary>
    private static bool IntersectsAabb(Vector3 centerA, Vector3 halfA, Vector3 centerB, Vector3 halfB)
    {
        return MathF.Abs(centerA.X - centerB.X) < (halfA.X + halfB.X)
            && MathF.Abs(centerA.Y - centerB.Y) < (halfA.Y + halfB.Y)
            && MathF.Abs(centerA.Z - centerB.Z) < (halfA.Z + halfB.Z);
    }

    private static float LaneToX(int lane) => (lane - 1) * LaneWidth;

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        int activeObstacles = _obstaclePool.Count(s => s.Active);
        yield return $"Stare joc      : {(_gameOver ? "COLIZIUNE (apasă R)" : "în desfășurare")}";
        yield return $"Scor / Record  : {_survivalTime:F2} s  /  {_bestTime:F2} s";
        yield return $"Bandă / Y      : {_currentLane + 1} / {LaneCount}   (înălțime salt: {_playerY - GroundCenterY:F2})";
        yield return $"Viteză / Obst. : {_worldSpeed:F1} u/s   ({activeObstacles} obstacole active)";
    }
}