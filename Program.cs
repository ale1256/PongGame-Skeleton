using System.Diagnostics;
using Silk.NET.SDL;

namespace TheAdventure;

public static class Program
{
    public static async Task Main()
    {
        // încarc si salvez BestRally
        using var saveStorage = new JsonFileStorage<SaveData>(AppPaths.GetSaveFilePath());
        var save = await LoadSaveOrDefaultAsync(saveStorage).ConfigureAwait(false);
        save = save with { LastPlayedUtc = DateTimeOffset.UtcNow };

        //lanț de task-uri de save 
        var pendingSave = QueueSave(Task.CompletedTask, saveStorage, save);
        var sdl = new Sdl(new SdlContext());

        IntPtr window = IntPtr.Zero;
        IntPtr renderer = IntPtr.Zero;

        try
        {
            // initializare  video + events + timer.
            var sdlInitResult = sdl.Init(Sdl.InitVideo | Sdl.InitEvents | Sdl.InitTimer);
            if (sdlInitResult < 0)
            {
                throw new InvalidOperationException(
                    $"SDL init: {sdl.GetErrorS()}{Environment.NewLine}Acest joc are nevoie de un mediu grafic.");
            }

            ReadOnlySpan<byte> keyboardState;
            unsafe
            {
                //Starea tastelor 
                keyboardState = new(sdl.GetKeyboardState(null), (int)KeyCode.Count);
            }

            var ev = new Event();
            //stopwatch pentru delta time în game loop
            var timer = Stopwatch.StartNew();

            unsafe
            {
                //fereastra de joc 
                window = (IntPtr)sdl.CreateWindow(
                    "The Adventure - Pong+", Sdl.WindowposUndefined, Sdl.WindowposUndefined, 800, 800,
                    (uint)WindowFlags.Resizable | (uint)WindowFlags.AllowHighdpi
                );

                if (window == IntPtr.Zero)
                {
                    var ex = sdl.GetErrorAsException();
                    if (ex != null)
                    {
                        throw ex;
                    }

                    throw new Exception("Failed to create window.");
                }
            }

            unsafe
            {
                // renderer-ul 2D accelerat 
                renderer = (IntPtr)sdl.CreateRenderer((Window*)window, -1, (uint)RendererFlags.Accelerated);
                sdl.RenderSetVSync((Renderer*)renderer, 1);
            }

            if (renderer == IntPtr.Zero)
            {
                var ex = sdl.GetErrorAsException();
                if (ex != null)
                {
                    throw ex;
                }

                throw new Exception("Failed to create renderer.");
            }

            int screenWidth = 800;
            int screenHeight = 800;
            unsafe
            {
                // dimensiunea reală de randare
                sdl.GetRendererOutputSize((Renderer*)renderer, ref screenWidth, ref screenHeight);
            }

            // initializare joc și îi dăm best-ul all-time 
            var game = new PongGame(screenWidth, screenHeight, bestRallyAllTime: save.BestRally);
            PrintControls(save);

            string? lastTitle = null;
            bool quit = false;
            var matchCounted = false;
            while (!quit)
            {
                //citire toate evenimentele SDL 
                while (sdl.PollEvent(ref ev) != 0)
                {
                    if (ev.Type == (uint)EventType.Quit)
                    {
                        quit = true;
                        break;
                    }

                    switch (ev.Type)
                    {
                        case (uint)EventType.Windowevent:
                        {
                            switch (ev.Window.Event)
                            {
                                case (byte)WindowEventID.SizeChanged:
                                {
                                    break;
                                }
                                case (byte)WindowEventID.TakeFocus:
                                {
                                    // la focus-ul tastaturii după click/alt-tab.
                                    unsafe
                                    {
                                        sdl.SetWindowInputFocus(sdl.GetWindowFromID(ev.Window.WindowID));
                                    }

                                    break;
                                }
                            }

                            break;
                        }

                        case (uint)EventType.Keydown:
                        {
                            // one-shot  trimitem către joc doar la primul KeyDown 
                            var key = (KeyCode)ev.Key.Keysym.Scancode;
                            if (key == KeyCode.Escape)
                            {
                                quit = true;
                                break;
                            }

                            if (ev.Key.Repeat == 0)
                            {
                                game.HandleKeyDown(key);
                            }

                            break;
                        }
                    }
                }

                //  timpul scurs de la ultimul frame
                var dt = (float)timer.Elapsed.TotalSeconds;
                timer.Restart();

                unsafe
                {
                    // dimensiunea renderer-ului se poate schimba dinamic.
                    sdl.GetRendererOutputSize((Renderer*)renderer, ref screenWidth, ref screenHeight);
                }
                game.Resize(screenWidth, screenHeight);

                // update-ul jocului
                game.Update(dt, keyboardState);

                if (!matchCounted && game.MatchOver)
                {
                    // numărăm meciul o singură dată când se termină
                    matchCounted = true;
                    save = save with { GamesPlayed = save.GamesPlayed + 1, LastPlayedUtc = DateTimeOffset.UtcNow };
                    pendingSave = QueueSave(pendingSave, saveStorage, save);
                }

                if (game.TryConsumeNewAllTimeBest(out var bestRally) && bestRally > save.BestRally)
                {
                    // salvăm best rally all-time daca e cazul
                    save = save with { BestRally = bestRally, LastPlayedUtc = DateTimeOffset.UtcNow };
                    pendingSave = QueueSave(pendingSave, saveStorage, save);
                }

                unsafe
                {
                    // cadrul curent.
                    var r = (Renderer*)renderer;
                    game.Render(sdl, r);
                    sdl.RenderPresent(r);
                }

                // titlu scor, mod, rally etc
                var title = BuildTitle(game, save);
                if (title != lastTitle)
                {
                    unsafe
                    {
                        sdl.SetWindowTitle((Window*)window, title);
                    }

                    lastTitle = title;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
        finally
        {
            unsafe
            {
                if (renderer != IntPtr.Zero)
                {
                    sdl.DestroyRenderer((Renderer*)renderer);
                }

                if (window != IntPtr.Zero)
                {
                    sdl.DestroyWindow((Window*)window);
                }
            }

            sdl.Quit();
        }

        try
        {
            await pendingSave.ConfigureAwait(false);
        }
        catch (SaveDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
        }
    }

    private static void PrintControls(SaveData save)
    {
        Console.WriteLine(" The Adventure - Pong");
        Console.WriteLine("Controls:");
        Console.WriteLine("  Player 1: W / S");
        Console.WriteLine("  Player 2: Up / Down (or AI when Single Player is ON)");
        Console.WriteLine("  Space: Pause/Resume");
        Console.WriteLine("  R: Restart match");
        Console.WriteLine("  F1: Toggle Single Player (AI)");
        Console.WriteLine("  F2: Cycle AI difficulty (Easy/Normal/Hard)");
        Console.WriteLine("  F3: Toggle center obstacles");
        Console.WriteLine("  1/2/3: First to 5/10/15 points");
        Console.WriteLine("  Esc: Quit");
        Console.WriteLine();
        Console.WriteLine("Power-ups:");
        Console.WriteLine("  GREEN: bigger paddle (temporary)");
        Console.WriteLine("  RED: smaller paddle (temporary)");
        Console.WriteLine();
        Console.WriteLine($"Save file: {AppPaths.GetSaveFilePath()}");
        Console.WriteLine($"Best rally (all-time): {save.BestRally}");
        Console.WriteLine($"Matches finished: {save.GamesPlayed}");
        Console.WriteLine();
    }

    private static string BuildTitle(PongGame game, SaveData save)
    {
        var mode = game.SinglePlayer ? $"Single Player (AI: {game.Difficulty})" : "2 Players";
        var score = $"Score {game.Score1}-{game.Score2} (to {game.WinningScore})";
        var arena = game.ObstaclesEnabled ? "Obstacles ON" : "Obstacles OFF";
        var rally = $"Rally {game.Rally} (best {game.BestRallyThisRun}, all-time {save.BestRally})";

        if (game.MatchOver)
        {
            return $"The Adventure - Pong | {mode} | {arena} | {score} | {rally} | Player {game.Winner} wins! (R to restart)";
        }

        if (game.Paused)
        {
            return $"The Adventure - Pong | {mode} | {arena} | {score} | {rally} | Paused (Space)";
        }

        if (game.ServeTimer > 0f)
        {
            return $"The Adventure - Pong | {mode} | {arena} | {score} | {rally} | Serve...";
        }

        return $"The Adventure - Pong| {mode} | {arena} | {score} | {rally}";
    }

    private static async Task<SaveData> LoadSaveOrDefaultAsync(IStorage<SaveData> storage)
    {
        try
        {
            // Dacă nu există fișierul folosim valorile default
            return await storage.TryLoadAsync().ConfigureAwait(false) ?? SaveData.Default;
        }
        catch (SaveDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return SaveData.Default;
        }
    }

    private static Task SafeSaveAsync(IStorage<SaveData> storage, SaveData save)
    {
        try
        {
            return storage.SaveAsync(save);
        }
        catch (SaveDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return Task.CompletedTask;
        }
    }

    private static Task QueueSave(Task pendingSave, IStorage<SaveData> storage, SaveData save)
    {//fiecare save pornește după ce se termină cel anterior.
        return pendingSave.ContinueWith(
                _ => SafeSaveAsync(storage, save),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default)
            .Unwrap();
    }
}
