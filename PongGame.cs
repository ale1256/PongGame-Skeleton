using Silk.NET.SDL;
using Rectangle = Silk.NET.Maths.Rectangle<int>;

namespace TheAdventure;

public sealed class PongGame
{
    //logica jocului Pong
    //dimensiuni + mișcare
    private const int PaddleWidth = 16;
    private const int PaddleHeight = 120;
    private const int PaddleMargin = 40;
    private const float PaddleSpeed = 700f;
    // single player cât de repede urmărește mingea
    private const float EasySpeed = 520f;
    private const float NormalSpeed = 640f;
    private const float FastSpeed = 820f;

    // minge: dimensiune + viteză 
    private const int BallSize = 14;
    private const float BallStartSpeed = 520f;
    private const float BallSpeedIncreasePerHit = 22f;
    private const float BallMaxSpeed = 1200f;
    private const float MaxBounce = MathF.PI * 0.33f;

    //pauză scurtă între puncte (serve delay)
    private const float Delay = 0.65f;

    // Obstacolele centrale 
    private const int ObstacleWidth = 18;
    private const float ObstacleSpeed = 260f;

    //power-ups: apar random, au viață limitată și efect temporar pe paletă
    private const int PowerUpSize = 18;
    private const float PowerUpLifeSeconds = 7f;
    private const float PowerUpEffectSeconds = 6f;
    private const float PowerUpSpawnMinSeconds = 4f;
    private const float PowerUpSpawnMaxSeconds = 9f;

    private readonly Random _random = new();

    public enum AiDifficulty : byte
    {
        Easy,
        Normal,
        Hard,
    }

    private enum PaddleEffect : byte
    {
        None,
        Grow,
        Shrink,
    }

    private enum PowerUpType : byte
    {
        Grow,
        Shrink,
    }

    private int _screenWidth;
    private int _screenHeight;

    private float _paddle1Y;
    private float _paddle2Y;
    private float _paddle1Velocity;
    private float _paddle2Velocity;

    private float _paddle1Scale = 1f;
    private float _paddle2Scale = 1f;
    private PaddleEffect _paddle1Effect;
    private PaddleEffect _paddle2Effect;
    private float _paddle1EffectTimer;
    private float _paddle2EffectTimer;

    private float _ballX;
    private float _ballY;
    private float _ballVx;
    private float _ballVy;

    private int _score1;
    private int _score2;
    private int _winningScore = 10;
    private int _rally;
    private int _bestRallyThisRun;
    private int _bestRallyAllTime;
    private bool _bestRallyAllTimeDirty; // setat când se schimbă best-ul all-time 
    private readonly List<int> _completedRallies = new();
    private int _averageRally;

    private bool _paused = true;
    private bool _singlePlayer = true;
    private bool _matchOver;
    private int _winner;
    private AiDifficulty _aiDifficulty = AiDifficulty.Normal;
    private bool _obstaclesEnabled = true;

    private float _serveTimer;
    private int _serveDirectionX = 1;
    private int _lastHitByPlayer;

    private float _obstacle1Y;
    private float _obstacle2Y;
    private float _obstacle1Vy = ObstacleSpeed;
    private float _obstacle2Vy = -ObstacleSpeed;

    private bool _powerUpActive;
    private PowerUpType _powerUpType;
    private float _powerUpX;
    private float _powerUpY;
    private float _powerUpLifeLeft;
    private float _nextPowerUpIn = 2f;

    public PongGame(int screenWidth, int screenHeight, int bestRallyAllTime = 0)
    {
        _bestRallyAllTime = Math.Max(0, bestRallyAllTime);
        Resize(screenWidth, screenHeight);
        ResetMatch();
    }

    public int Score1 => _score1;
    public int Score2 => _score2;
    public bool Paused => _paused;
    public bool SinglePlayer => _singlePlayer;
    public bool MatchOver => _matchOver;
    public int Winner => _winner;
    public float ServeTimer => _serveTimer;
    public int WinningScore => _winningScore;
    public AiDifficulty Difficulty => _aiDifficulty;
    public bool ObstaclesEnabled => _obstaclesEnabled;
    public int Rally => _rally;
    public int BestRallyThisRun => _bestRallyThisRun;
    public int BestRallyAllTime => _bestRallyAllTime;
    public int AverageRally => _averageRally;

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _screenWidth = width;
        _screenHeight = height;

        var paddle1Height = GetPaddleHeight(_paddle1Scale);
        var paddle2Height = GetPaddleHeight(_paddle2Scale);
        _paddle1Y = Clamp(_paddle1Y, 0, _screenHeight - paddle1Height);
        _paddle2Y = Clamp(_paddle2Y, 0, _screenHeight - paddle2Height);
        _ballX = Clamp(_ballX, 0, _screenWidth - BallSize);
        _ballY = Clamp(_ballY, 0, _screenHeight - BallSize);

        var obstacleH = GetObstacleHeight();
        _obstacle1Y = Clamp(_obstacle1Y, 0, _screenHeight - obstacleH);
        _obstacle2Y = Clamp(_obstacle2Y, 0, _screenHeight - obstacleH);

        if (_powerUpActive)
        {
            _powerUpX = Clamp(_powerUpX, 0, _screenWidth - PowerUpSize);
            _powerUpY = Clamp(_powerUpY, 0, _screenHeight - PowerUpSize);
        }
    }

    // comenzi one-shot: pauză, restart, single player/AI, dificultate, obstacole, target score.
    public void HandleKeyDown(KeyCode key)
    {
        if (key == KeyCode.Space && !_matchOver)
        {
            _paused = !_paused;
            return;
        }

        if (key == KeyCode.R)
        {
            ResetMatch();
            return;
        }

        if (key == KeyCode.F1)
        {
            _singlePlayer = !_singlePlayer;
            return;
        }

        if (key == KeyCode.F2)
        {
            _aiDifficulty = _aiDifficulty switch
            {
                AiDifficulty.Easy => AiDifficulty.Normal,
                AiDifficulty.Normal => AiDifficulty.Hard,
                _ => AiDifficulty.Easy,
            };
            return;
        }

        if (key == KeyCode.F3)
        {
            _obstaclesEnabled = !_obstaclesEnabled;
            ResetArena();
            return;
        }

        if (key == KeyCode.One)
        {
            SetWinningScore(5);
            return;
        }

        if (key == KeyCode.Two)
        {
            SetWinningScore(10);
            return;
        }

        if (key == KeyCode.Three)
        {
            SetWinningScore(15);
            return;
        }
    }

    // mișcare, coliziuni, scor, spawn power-ups
    public void Update(float dtSeconds, ReadOnlySpan<byte> keyboardState)
    {
        if (_screenWidth <= 0 || _screenHeight <= 0)
        {
            return;
        }

        var dt = MathF.Min(MathF.Max(dtSeconds, 0f), 0.05f);

        if (!_paused && !_matchOver)
        {
            UpdateEffectTimers(dt);
            UpdatePowerUps(dt);
            UpdateObstacles(dt);
        }

        UpdatePaddles(dt, keyboardState);

        if (_paused || _matchOver)
        {
            return;
        }

        if (_serveTimer > 0f)
        {
            _serveTimer -= dt;
            if (_serveTimer <= 0f)
            {
                StartBall(_serveDirectionX);
            }

            return;
        }

        _ballX += _ballVx * dt;
        _ballY += _ballVy * dt;

        //bounce to top/bottom.
        if (_ballY <= 0f)
        {
            _ballY = 0f;
            _ballVy = MathF.Abs(_ballVy);
        }
        else if (_ballY >= _screenHeight - BallSize)
        {
            _ballY = _screenHeight - BallSize;
            _ballVy = -MathF.Abs(_ballVy);
        }

        var paddle1Height = GetPaddleHeight(_paddle1Scale);
        var paddle2Height = GetPaddleHeight(_paddle2Scale);

        var paddle1X = PaddleMargin;
        var paddle2X = _screenWidth - PaddleMargin - PaddleWidth;

        if (_obstaclesEnabled)
        {
            var obstacleH = GetObstacleHeight();
            var obstacleGap = GetObstacleGap();
            var obstacleX1 = _screenWidth / 2 - obstacleGap - ObstacleWidth;
            var obstacleX2 = _screenWidth / 2 + obstacleGap;
            HandleBlockCollision(obstacleX1, _obstacle1Y, ObstacleWidth, obstacleH);
            HandleBlockCollision(obstacleX2, _obstacle2Y, ObstacleWidth, obstacleH);
        }

        if (_ballVx < 0f &&
            Intersects(_ballX, _ballY, BallSize, BallSize, paddle1X, _paddle1Y, PaddleWidth, paddle1Height))
        {
            _ballX = paddle1X + PaddleWidth;
            _lastHitByPlayer = 1;
            RegisterPaddleHit();
            BounceFromPaddle(_paddle1Y, paddle1Height, _paddle1Velocity, directionX: 1);
        }
        else if (_ballVx > 0f &&
                 Intersects(_ballX, _ballY, BallSize, BallSize, paddle2X, _paddle2Y, PaddleWidth, paddle2Height))
        {
            _ballX = paddle2X - BallSize;
            _lastHitByPlayer = 2;
            RegisterPaddleHit();
            BounceFromPaddle(_paddle2Y, paddle2Height, _paddle2Velocity, directionX: -1);
        }

        //power-up pickup.
        if (_powerUpActive &&
            Intersects(_ballX, _ballY, BallSize, BallSize, _powerUpX, _powerUpY, PowerUpSize, PowerUpSize))
        {
            var targetPlayer = _lastHitByPlayer != 0 ? _lastHitByPlayer : (_ballVx >= 0f ? 1 : 2);
            ApplyPowerUp(_powerUpType, targetPlayer);
            _powerUpActive = false;
        }

        //scoring.
        if (_ballX + BallSize < 0f)
        {
            ScorePoint(scoringPlayer: 2);
        }
        else if (_ballX > _screenWidth)
        {
            ScorePoint(scoringPlayer: 1);
        }
    }

    public unsafe void Render(Sdl sdl, Renderer* renderer)
    {
        //background.
        sdl.SetRenderDrawColor(renderer, 18, 18, 22, 255);
        sdl.RenderClear(renderer);

        DrawCenterLine(sdl, renderer);

        if (_obstaclesEnabled)
        {
            var obstacleH = GetObstacleHeight();
            var gap = GetObstacleGap();
            var obstacleX1 = _screenWidth / 2 - gap - ObstacleWidth;
            var obstacleX2 = _screenWidth / 2 + gap;

            sdl.SetRenderDrawColor(renderer, 105, 105, 120, 255);
            FillRect(sdl, renderer, obstacleX1, (int)_obstacle1Y, ObstacleWidth, obstacleH);
            FillRect(sdl, renderer, obstacleX2, (int)_obstacle2Y, ObstacleWidth, obstacleH);
        }

        if (_powerUpActive)
        {
            var (r, g, b) = _powerUpType == PowerUpType.Grow ? (80, 220, 120) : (230, 90, 90);
            sdl.SetRenderDrawColor(renderer, (byte)r, (byte)g, (byte)b, 235);
            FillRect(sdl, renderer, (int)_powerUpX, (int)_powerUpY, PowerUpSize, PowerUpSize);
        }

        //entities.
        var paddle1Height = (int)GetPaddleHeight(_paddle1Scale);
        var paddle2Height = (int)GetPaddleHeight(_paddle2Scale);

        var (p1r, p1g, p1b) = _paddle1Effect switch
        {
            PaddleEffect.Grow => (80, 220, 120),
            PaddleEffect.Shrink => (230, 90, 90),
            _ => (240, 240, 240),
        };
        sdl.SetRenderDrawColor(renderer, (byte)p1r, (byte)p1g, (byte)p1b, 255);
        FillRect(sdl, renderer, PaddleMargin, (int)_paddle1Y, PaddleWidth, paddle1Height);

        var (p2r, p2g, p2b) = _paddle2Effect switch
        {
            PaddleEffect.Grow => (80, 220, 120),
            PaddleEffect.Shrink => (230, 90, 90),
            _ => (240, 240, 240),
        };
        sdl.SetRenderDrawColor(renderer, (byte)p2r, (byte)p2g, (byte)p2b, 255);
        FillRect(sdl, renderer, _screenWidth - PaddleMargin - PaddleWidth, (int)_paddle2Y, PaddleWidth, paddle2Height);

        sdl.SetRenderDrawBlendMode(renderer, BlendMode.Blend);
        sdl.SetRenderDrawColor(renderer, 240, 240, 240, 70);
        FillRect(sdl, renderer, (int)_ballX - 5, (int)_ballY - 5, BallSize + 10, BallSize + 10);
        sdl.SetRenderDrawColor(renderer, 240, 240, 240, 255);
        FillRect(sdl, renderer, (int)_ballX, (int)_ballY, BallSize, BallSize);
        sdl.SetRenderDrawBlendMode(renderer, BlendMode.None);

        // score
        sdl.SetRenderDrawColor(renderer, 170, 170, 180, 255);
        var seg = Math.Clamp(_screenWidth / 30, 14, 28);
        var scoreGap = seg;
        var scoreboardY = Math.Clamp(_screenHeight / 18, 18, 56);
        var leftX = _screenWidth / 2 - (seg * 2 + seg / 2) - scoreGap;
        var rightX = _screenWidth / 2 + scoreGap;
        DrawNumber(sdl, renderer, _score1, leftX, scoreboardY, seg);
        DrawNumber(sdl, renderer, _score2, rightX, scoreboardY, seg);

        //overlay UI
        if (_matchOver)
        {
            sdl.SetRenderDrawColor(renderer, 60, 200, 80, 255);
            var halfW = _screenWidth / 2;
            var rect = _winner == 1
                ? new Rectangle(10, 10, halfW - 20, _screenHeight - 20)
                : new Rectangle(halfW + 10, 10, halfW - 20, _screenHeight - 20);
            sdl.RenderDrawRect(renderer, &rect);
        }
        else if (_paused)
        {
            sdl.SetRenderDrawColor(renderer, 255, 210, 60, 255);
            DrawPauseIcon(sdl, renderer);
        }
        else if (_serveTimer > 0f)
        {
            sdl.SetRenderDrawColor(renderer, 255, 210, 60, 255);
            DrawServePip(sdl, renderer);
        }
    }

    private void ResetMatch()
    {
        _score1 = 0;
        _score2 = 0;
        _matchOver = false;
        _winner = 0;
        _paused = true;
        _lastHitByPlayer = 0;

        _rally = 0;
        _bestRallyThisRun = 0;
        _completedRallies.Clear();
        _averageRally = 0;

        _paddle1Scale = 1f;
        _paddle2Scale = 1f;
        _paddle1Effect = PaddleEffect.None;
        _paddle2Effect = PaddleEffect.None;
        _paddle1EffectTimer = 0f;
        _paddle2EffectTimer = 0f;

        var paddle1Height = GetPaddleHeight(_paddle1Scale);
        var paddle2Height = GetPaddleHeight(_paddle2Scale);
        _paddle1Y = (_screenHeight - paddle1Height) / 2f;
        _paddle2Y = (_screenHeight - paddle2Height) / 2f;

        ResetArena();
        ResetPowerUps();
        StartServe(directionX: _random.Next(0, 2) == 0 ? -1 : 1, keepPaused: true);
    }

    private void UpdatePaddles(float dt, ReadOnlySpan<byte> keyboardState)
    {
        var paddle1Height = GetPaddleHeight(_paddle1Scale);
        var paddle2Height = GetPaddleHeight(_paddle2Scale);

        var oldPaddle1Y = _paddle1Y;
        var oldPaddle2Y = _paddle2Y;

        float paddle1Move = 0f;
        if (keyboardState[(int)KeyCode.W] > 0)
        {
            paddle1Move -= 1f;
        }
        if (keyboardState[(int)KeyCode.S] > 0)
        {
            paddle1Move += 1f;
        }

        _paddle1Y += paddle1Move * PaddleSpeed * dt;

        if (_singlePlayer)
        {
            var aiSpeed = GetAiSpeed();
            var targetY = _matchOver
                ? (_screenHeight - paddle2Height) / 2f
                : Clamp(_ballY + BallSize / 2f - paddle2Height / 2f, 0, _screenHeight - paddle2Height);

            var center = _paddle2Y + paddle2Height / 2f;
            var targetCenter = targetY + paddle2Height / 2f;
            var delta = targetCenter - center;

            var deadZone = MathF.Max(6f, paddle2Height * 0.06f);
            if (MathF.Abs(delta) > deadZone)
            {
                _paddle2Y += MathF.Sign(delta) * aiSpeed * dt;
            }
        }
        else
        {
            float paddle2Move = 0f;
            if (keyboardState[(int)KeyCode.Up] > 0)
            {
                paddle2Move -= 1f;
            }
            if (keyboardState[(int)KeyCode.Down] > 0)
            {
                paddle2Move += 1f;
            }

            _paddle2Y += paddle2Move * PaddleSpeed * dt;
        }

        _paddle1Y = Clamp(_paddle1Y, 0, _screenHeight - paddle1Height);
        _paddle2Y = Clamp(_paddle2Y, 0, _screenHeight - paddle2Height);

        _paddle1Velocity = dt > 0f ? (_paddle1Y - oldPaddle1Y) / dt : 0f;
        _paddle2Velocity = dt > 0f ? (_paddle2Y - oldPaddle2Y) / dt : 0f;
    }

    private void ScorePoint(int scoringPlayer)
    {
        FinalizeRally();

        if (scoringPlayer == 1)
        {
            _score1++;
        }
        else
        {
            _score2++;
        }

        if (_score1 >= _winningScore || _score2 >= _winningScore)
        {
            _matchOver = true;
            _paused = true;
            _winner = _score1 >= _winningScore ? 1 : 2;
            return;
        }

        StartServe(directionX: scoringPlayer == 1 ? 1 : -1, keepPaused: false);
    }

    private void StartServe(int directionX, bool keepPaused)
    {
        _serveDirectionX = directionX == 0 ? 1 : Math.Sign(directionX);
        _lastHitByPlayer = 0;
        _rally = 0;

        _ballX = (_screenWidth - BallSize) / 2f;
        _ballY = (_screenHeight - BallSize) / 2f;
        _ballVx = 0f;
        _ballVy = 0f;

        ResetArena();
        ResetPowerUps();

        _serveTimer = Delay;
        if (!keepPaused)
        {
            _paused = false;
        }
    }

    private void StartBall(int directionX)
    {
        var dir = directionX == 0 ? 1 : Math.Sign(directionX);
        _lastHitByPlayer = dir > 0 ? 1 : 2;
        var angle = (_random.NextSingle() * 2f - 1f) * (MathF.PI * 0.18f);
        _ballVx = MathF.Cos(angle) * BallStartSpeed * dir;
        _ballVy = MathF.Sin(angle) * BallStartSpeed;
    }

    private void BounceFromPaddle(float paddleY, float paddleHeight, float paddleVelocity, int directionX)
    {
        var ballCenter = _ballY + BallSize / 2f;
        var paddleCenter = paddleY + paddleHeight / 2f;
        var offset = (ballCenter - paddleCenter) / (paddleHeight / 2f);
        offset = Clamp(offset, -1f, 1f);

        var speed = MathF.Sqrt(_ballVx * _ballVx + _ballVy * _ballVy);
        speed = MathF.Min(BallMaxSpeed, speed + BallSpeedIncreasePerHit);

        var angle = offset * MaxBounce;

        var dir = directionX == 0 ? 1 : Math.Sign(directionX);
        _ballVx = MathF.Cos(angle) * speed * dir;
        _ballVy = MathF.Sin(angle) * speed;

        var spin = Clamp(paddleVelocity / PaddleSpeed, -1f, 1f) * (speed * 0.28f);
        _ballVy += spin;

        var newSpeed = MathF.Sqrt(_ballVx * _ballVx + _ballVy * _ballVy);
        if (newSpeed > 0f)
        {
            var scale = speed / newSpeed;
            _ballVx *= scale;
            _ballVy *= scale;
        }

        if (_ballVx == 0f)
        {
            _ballVx = speed * dir;
        }
    }

    private void SetWinningScore(int newWinningScore)
    {
        newWinningScore = Math.Clamp(newWinningScore, 1, 99);
        if (_winningScore == newWinningScore)
        {
            return;
        }

        _winningScore = newWinningScore;
        ResetMatch();
    }

    private float GetAiSpeed()
    {
        return _aiDifficulty switch
        {
            AiDifficulty.Easy => EasySpeed,
            AiDifficulty.Hard => FastSpeed,
            _ => NormalSpeed,
        };
    }

    private float GetPaddleHeight(float scale)
    {
        var desired = PaddleHeight * scale;
        var max = MathF.Max(40f, _screenHeight - 20f);
        return MathF.Min(desired, max);
    }

    private int GetObstacleHeight()
    {
        return Math.Clamp(_screenHeight / 4, 90, 220);
    }

    private int GetObstacleGap()
    {
        return Math.Clamp(_screenWidth / 18, 22, 60);
    }

    private void ResetArena()
    {
        if (_screenWidth <= 0 || _screenHeight <= 0)
        {
            return;
        }

        var obstacleH = GetObstacleHeight();
        var maxY = MathF.Max(0f, _screenHeight - obstacleH);
        _obstacle1Y = _random.NextSingle() * maxY;
        _obstacle2Y = _random.NextSingle() * maxY;

        _obstacle1Vy = (_random.Next(0, 2) == 0 ? -1f : 1f) * ObstacleSpeed;
        _obstacle2Vy = (_random.Next(0, 2) == 0 ? -1f : 1f) * ObstacleSpeed;
    }

    private void UpdateObstacles(float dt)
    {
        if (!_obstaclesEnabled)
        {
            return;
        }

        var obstacleH = GetObstacleHeight();
        var maxY = MathF.Max(0f, _screenHeight - obstacleH);

        _obstacle1Y += _obstacle1Vy * dt;
        if (_obstacle1Y < 0f)
        {
            _obstacle1Y = 0f;
            _obstacle1Vy = MathF.Abs(_obstacle1Vy);
        }
        else if (_obstacle1Y > maxY)
        {
            _obstacle1Y = maxY;
            _obstacle1Vy = -MathF.Abs(_obstacle1Vy);
        }

        _obstacle2Y += _obstacle2Vy * dt;
        if (_obstacle2Y < 0f)
        {
            _obstacle2Y = 0f;
            _obstacle2Vy = MathF.Abs(_obstacle2Vy);
        }
        else if (_obstacle2Y > maxY)
        {
            _obstacle2Y = maxY;
            _obstacle2Vy = -MathF.Abs(_obstacle2Vy);
        }
    }

    private void ResetPowerUps()
    {
        _powerUpActive = false;
        _powerUpLifeLeft = 0f;
        _nextPowerUpIn = 1.5f;
    }

    private void UpdatePowerUps(float dt)
    {
        if (_powerUpActive)
        {
            _powerUpLifeLeft -= dt;
            if (_powerUpLifeLeft <= 0f)
            {
                _powerUpActive = false;
            }

            return;
        }

        _nextPowerUpIn -= dt;
        if (_nextPowerUpIn <= 0f && _serveTimer <= 0f)
        {
            SpawnPowerUp();
        }
    }

    private void SpawnPowerUp()
    {
        _powerUpActive = true;
        _powerUpType = _random.Next(0, 2) == 0 ? PowerUpType.Grow : PowerUpType.Shrink;
        _powerUpLifeLeft = PoweSecondsUp;

        var cx = _screenWidth / 2f - PowerUpSize / 2f;
        var x = cx + _random.NextSingle() * 140f - 70f;
        var yMin = 70f;
        var yMax = MathF.Max(yMin, _screenHeight - 70f - PowerUpSize);
        var y = yMin + _random.NextSingle() * (yMax - yMin);

        _powerUpX = Clamp(x, 10, _screenWidth - 10 - PowerUpSize);
        _powerUpY = Clamp(y, 10, _screenHeight - 10 - PowerUpSize);

        _nextPowerUpIn = PowerUpSpawnMinSeconds + _random.NextSingle() * (PowerUpSpawnMaxSeconds - PowerUpSpawnMinSeconds);
    }

    private void ApplyPowerUp(PowerUpType type, int player)
    {
        var effect = type == PowerUpType.Grow ? PaddleEffect.Grow : PaddleEffect.Shrink;
        var scale = type == PowerUpType.Grow ? 1.35f : 0.75f;

        if (player == 1)
        {
            _paddle1Scale = scale;
            _paddle1Effect = effect;
            _paddle1EffectTimer = PowerUpEffectSeconds;
            _paddle1Y = Clamp(_paddle1Y, 0, _screenHeight - GetPaddleHeight(_paddle1Scale));
            return;
        }

        if (player == 2)
        {
            _paddle2Scale = scale;
            _paddle2Effect = effect;
            _paddle2EffectTimer = PowerUpEffectSeconds;
            _paddle2Y = Clamp(_paddle2Y, 0, _screenHeight - GetPaddleHeight(_paddle2Scale));
            return;
        }

        _paddle1Scale = scale;
        _paddle2Scale = scale;
        _paddle1Effect = effect;
        _paddle2Effect = effect;
        _paddle1EffectTimer = PowerUpEffectSeconds;
        _paddle2EffectTimer = PowerUpEffectSeconds;
    }

    private void UpdateEffectTimers(float dt)
    {
        if (_paddle1EffectTimer > 0f)
        {
            _paddle1EffectTimer -= dt;
            if (_paddle1EffectTimer <= 0f)
            {
                _paddle1EffectTimer = 0f;
                _paddle1Effect = PaddleEffect.None;
                _paddle1Scale = 1f;
                _paddle1Y = Clamp(_paddle1Y, 0, _screenHeight - GetPaddleHeight(_paddle1Scale));
            }
        }

        if (_paddle2EffectTimer > 0f)
        {
            _paddle2EffectTimer -= dt;
            if (_paddle2EffectTimer <= 0f)
            {
                _paddle2EffectTimer = 0f;
                _paddle2Effect = PaddleEffect.None;
                _paddle2Scale = 1f;
                _paddle2Y = Clamp(_paddle2Y, 0, _screenHeight - GetPaddleHeight(_paddle2Scale));
            }
        }
    }

    private bool HandleBlockCollision(int x, float y, int w, int h)
    {
        if (!Intersects(_ballX, _ballY, BallSize, BallSize, x, y, w, h))
        {
            return false;
        }

        var ballRight = _ballX + BallSize;
        var ballBottom = _ballY + BallSize;
        var blockRight = x + w;
        var blockBottom = y + h;

        var overlapLeft = ballRight - x;
        var overlapRight = blockRight - _ballX;
        var overlapTop = ballBottom - y;
        var overlapBottom = blockBottom - _ballY;

        var minX = MathF.Min(overlapLeft, overlapRight);
        var minY = MathF.Min(overlapTop, overlapBottom);

        if (minX < minY)
        {
            if (overlapLeft < overlapRight)
            {
                _ballX = x - BallSize;
                _ballVx = -MathF.Abs(_ballVx);
            }
            else
            {
                _ballX = blockRight;
                _ballVx = MathF.Abs(_ballVx);
            }
        }
        else
        {
            if (overlapTop < overlapBottom)
            {
                _ballY = y - BallSize;
                _ballVy = -MathF.Abs(_ballVy);
            }
            else
            {
                _ballY = blockBottom;
                _ballVy = MathF.Abs(_ballVy);
            }
        }

        var speed = GetBallSpeed();
        SetBallSpeed(MathF.Min(BallMaxSpeed, speed + BallSpeedIncreasePerHit * 0.35f));
        return true;
    }

    private float GetBallSpeed()
    {
        return MathF.Sqrt(_ballVx * _ballVx + _ballVy * _ballVy);
    }

    private void SetBallSpeed(float speed)
    {
        var current = GetBallSpeed();
        if (current <= 0f || speed <= 0f)
        {
            return;
        }

        var scale = speed / current;
        _ballVx *= scale;
        _ballVy *= scale;
    }

    //când mingea atinge o paletă: crește rally și actualizează best-urile
    private void RegisterPaddleHit()
    {
        _rally++;
        if (_rally > _bestRallyThisRun)
        {
            _bestRallyThisRun = _rally;
        }

        if (_rally > _bestRallyAllTime)
        {
            _bestRallyAllTime = _rally;
            _bestRallyAllTimeDirty = true;
        }
    }

    //Program.cs: schimbarea best-ului all-time 
    public bool TryConsumeNewAllTimeBest(out int bestRally)
    {
        if (!_bestRallyAllTimeDirty)
        {
            bestRally = _bestRallyAllTime;
            return false;
        }

        _bestRallyAllTimeDirty = false;
        bestRally = _bestRallyAllTime;
        return true;
    }

    private void FinalizeRally()
    {
        if (_rally <= 0)
        {
            return;
        }

        _completedRallies.Add(_rally);
        _averageRally = (int)Math.Round(_completedRallies.Average());
        _rally = 0;
    }

    private static bool Intersects(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
    {
        return ax < bx + bw &&
               ax + aw > bx &&
               ay < by + bh &&
               ay + ah > by;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }
        return value > max ? max : value;
    }

    private unsafe void DrawCenterLine(Sdl sdl, Renderer* renderer)
    {
        sdl.SetRenderDrawColor(renderer, 80, 80, 90, 255);

        var x = _screenWidth / 2 - 2;
        var dashW = 4;
        var dashH = Math.Clamp(_screenHeight / 32, 10, 22);
        var spacing = dashH;

        for (var y = 0; y < _screenHeight; y += dashH + spacing)
        {
            FillRect(sdl, renderer, x, y, dashW, dashH);
        }
    }

    private static unsafe void FillRect(Sdl sdl, Renderer* renderer, int x, int y, int w, int h)
    {
        var rect = new Rectangle(x, y, w, h);
        sdl.RenderFillRect(renderer, &rect);
    }

    private unsafe void DrawPauseIcon(Sdl sdl, Renderer* renderer)
    {
        var barW = Math.Clamp(_screenWidth / 40, 14, 24);
        var barH = Math.Clamp(_screenHeight / 8, 70, 150);
        var gap = barW;

        var cx = _screenWidth / 2;
        var cy = _screenHeight / 2;

        FillRect(sdl, renderer, cx - gap / 2 - barW, cy - barH / 2, barW, barH);
        FillRect(sdl, renderer, cx + gap / 2, cy - barH / 2, barW, barH);
    }

    private unsafe void DrawServePip(Sdl sdl, Renderer* renderer)
    {
        var size = Math.Clamp(_screenWidth / 90, 6, 12);
        var cx = _screenWidth / 2 - size / 2;
        var cy = Math.Clamp(_screenHeight / 7, 80, 180);
        FillRect(sdl, renderer, cx, cy, size, size);
    }

    private static unsafe void DrawNumber(Sdl sdl, Renderer* renderer, int number, int x, int y, int seg)
    {
        number = Math.Clamp(number, 0, 99);
        var tens = number / 10;
        var ones = number % 10;

        var digitW = seg;
        var spacing = seg / 2;

        if (number >= 10)
        {
            DrawDigit(sdl, renderer, tens, x, y, seg);
            DrawDigit(sdl, renderer, ones, x + digitW + spacing, y, seg);
        }
        else
        {
            DrawDigit(sdl, renderer, ones, x + (digitW + spacing) / 2, y, seg);
        }
    }

    private static unsafe void DrawDigit(Sdl sdl, Renderer* renderer, int digit, int x, int y, int seg)
    {
        digit = Math.Clamp(digit, 0, 9);

        var t = Math.Clamp(seg / 5, 2, 6);

        var top = new Rectangle(x + t, y, seg - 2 * t, t);
        var mid = new Rectangle(x + t, y + seg, seg - 2 * t, t);
        var bot = new Rectangle(x + t, y + 2 * seg, seg - 2 * t, t);

        var ul = new Rectangle(x, y + t, t, seg - t);
        var ur = new Rectangle(x + seg - t, y + t, t, seg - t);
        var ll = new Rectangle(x, y + seg + t, t, seg - t);
        var lr = new Rectangle(x + seg - t, y + seg + t, t, seg - t);

        var mask = DigitMask[digit];

        if ((mask & 1) != 0) sdl.RenderFillRect(renderer, &top);
        if ((mask & 2) != 0) sdl.RenderFillRect(renderer, &ul);
        if ((mask & 4) != 0) sdl.RenderFillRect(renderer, &ur);
        if ((mask & 8) != 0) sdl.RenderFillRect(renderer, &mid);
        if ((mask & 16) != 0) sdl.RenderFillRect(renderer, &ll);
        if ((mask & 32) != 0) sdl.RenderFillRect(renderer, &lr);
        if ((mask & 64) != 0) sdl.RenderFillRect(renderer, &bot);
    }

    // bit order: top, ul, ur, mid, ll, lr, bot
    //asta e AI generated
    private static readonly byte[] DigitMask =
    [
        1 | 2 | 4 | 16 | 32 | 64,
        4 | 32,
        1 | 4 | 8 | 16 | 64,
        1 | 4 | 8 | 32 | 64,
        2 | 4 | 8 | 32,
        1 | 2 | 8 | 32 | 64,
        1 | 2 | 8 | 16 | 32 | 64,
        1 | 4 | 32,
        1 | 2 | 4 | 8 | 16 | 32 | 64,
        1 | 2 | 4 | 8 | 32 | 64,
    ];
    //sfarsit AI generated
}
