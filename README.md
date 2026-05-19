# The Adventure pong
Un joc Pong 2D simplu, cu randare pe SDL2 folosind **Silk.NET**.

# Cerințe
- .NET SDK 10 (LTS)
- La prima compilare/rulare e nevoie de internet pentru 'dotnet restore'

## Rulare
dotnet run

# Photo of the game
<img width="831" height="833" alt="Screenshot 2026-05-19 at 20 35 37" src="https://github.com/user-attachments/assets/b8262152-ef8e-443e-b208-3b3d01781015" />


# Controale

- Player 1: `W` / `S`
- Player 2: `↑` / `↓` sau AI în modul Single Player
- `Space`: Pauză 
- `R`: Restart match
- `F1`: Toggle Single Player 
- `F2`: Schimbă dificultatea (Easy/Normal/Hard)
- `F3`: Toggle obstacolele din centru
- `1` / `2` / `3`: Primul la 5 / 10 / 15 puncte
- `Esc`: Ieșire

# Gameplay

- Primul la 5/10/15 puncte câștigă 
- După fiecare punct există un scurt serve delay
- Fereastra este redimensionabilă, iar jocul se adaptează automat
- Power-ups: pătrate verzi/roșii care măresc/micșorează paleta temporar
- Obstacole: două bare în centru care ricosează mingea

# Persistență (save/high-score)

Jocul salvează automat un mic fișier JSON cu:
- BestRally  – cel mai lung rally 
- GamesPlayed – câte meciuri au fost terminate.

# AI usage
Am folosit Geminni pentru debug si implementarea unor algoritmi pentru care am avut nevoie de ajutor 
